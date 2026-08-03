using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;
using Lingmai.RedMist.Generation.Batches;

namespace LipSyncReviewer;

public sealed class LipSyncReviewManifestLoader
{
    public const string ReviewPolicyVersion = "cx503.1";

    private static readonly HashSet<string> ReviewableIssueCodes = new(StringComparer.Ordinal)
    {
        "human_review.required",
        "lip_sync.human_review.required",
        "source.original_missing",
    };

    private readonly string _schemaDirectory;

    public LipSyncReviewManifestLoader(string schemaDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaDirectory);
        _schemaDirectory = Path.GetFullPath(schemaDirectory);
        if (!Directory.Exists(_schemaDirectory))
            throw new DirectoryNotFoundException($"Schema directory not found: {_schemaDirectory}");
    }

    public async Task<LoadedLipSyncReviewManifest> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string fullPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The shot manifest was not found.", fullPath);

        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = Parse(bytes);
        ValidateAgainstSchema(document.RootElement);
        await using (var manifestStream = new MemoryStream(bytes, writable: false))
        {
            try
            {
                await new ShotBatchManifestReader()
                    .ReadAsync(manifestStream, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GenerationBatchValidationException exception)
            {
                throw new LipSyncReviewInputException(
                    exception.Code,
                    "The shot manifest failed its locked content validation.",
                    exception);
            }
        }

        JsonElement root = document.RootElement;
        string manifestId = RequiredString(root, "manifestId");
        string manifestHash = RequiredString(root, "contentHash");
        string sourceBundleId = RequiredString(root, "sourceBundleId");
        string sourceBundleHash = RequiredString(root, "sourceBundleContentHash");
        string language = RequiredString(root, "language");
        var reviewable = new List<LipSyncReviewCandidate>();
        var blocked = new List<LipSyncBlockedResponse>();
        var responseIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement response in root.GetProperty("responseRequirements").EnumerateArray())
        {
            string responseId = RequiredString(response, "responseId");
            if (!responseIds.Add(responseId))
            {
                throw new LipSyncReviewInputException(
                    "manifest.duplicate_response",
                    $"Duplicate response requirement '{responseId}'.");
            }

            string readiness = RequiredString(response, "readinessStatus");
            string[] issueCodes = response.GetProperty("issueCodes")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            LipSyncReviewMediaPin lipSync = RequiredMediaPin(response, "lipSyncMedia");
            LipSyncReviewMediaPin? audio = OptionalMediaPin(response, "audioMedia");
            LipSyncReviewMediaPin? fallback = OptionalMediaPin(response, "fallbackMedia");
            bool structurallyReviewable =
                readiness is "needs_review" or "ready" &&
                lipSync.MediaType is "video" or "animation" &&
                audio?.MediaType == "audio" &&
                fallback?.MediaType is "video" or "animation" or "image" &&
                issueCodes.All(ReviewableIssueCodes.Contains);

            if (!structurallyReviewable)
            {
                blocked.Add(new LipSyncBlockedResponse(responseId, readiness, issueCodes));
                continue;
            }

            var dialogue = RequiredLocalizedValue(response, "text");
            var emotion = RequiredLocalizedValue(response, "emotion");
            LipSyncReviewMediaPin lockedAudio = audio!;
            LipSyncReviewMediaPin lockedFallback = fallback!;
            string dialogueHash = HashCanonical(
                responseId,
                language,
                dialogue.LocalizationKey,
                dialogue.Text);
            string presentationHash = HashCanonical(
                ReviewPolicyVersion,
                manifestHash,
                dialogueHash,
                CanonicalPin(lipSync),
                CanonicalPin(lockedAudio),
                CanonicalPin(lockedFallback));
            reviewable.Add(new LipSyncReviewCandidate(
                responseId,
                response.GetProperty("sourceNodeIds")
                    .EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToArray(),
                dialogue,
                emotion,
                dialogueHash,
                lipSync,
                lockedAudio,
                lockedFallback,
                readiness,
                issueCodes,
                presentationHash));
        }

        return new LoadedLipSyncReviewManifest(
            manifestId,
            manifestHash,
            sourceBundleId,
            sourceBundleHash,
            language,
            reviewable.AsReadOnly(),
            blocked.AsReadOnly());
    }

    private static JsonDocument Parse(byte[] bytes)
    {
        try
        {
            return JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                });
        }
        catch (JsonException exception)
        {
            throw new LipSyncReviewInputException(
                "manifest.json",
                "The shot manifest is not valid JSON.",
                exception);
        }
    }

    private void ValidateAgainstSchema(JsonElement instance)
    {
        var buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry(),
            DialectRegistry = new DialectRegistry(),
            VocabularyRegistry = new VocabularyRegistry(),
        };
        JsonSchema? shotManifestSchema = null;
        foreach (string path in Directory.EnumerateFiles(_schemaDirectory, "*.schema.json").Order())
        {
            using JsonDocument schemaDocument = JsonDocument.Parse(File.ReadAllText(path));
            JsonSchema schema = JsonSchema.Build(schemaDocument.RootElement.Clone(), buildOptions);
            if (string.Equals(
                    Path.GetFileName(path),
                    "shot-manifest.schema.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                shotManifestSchema = schema;
            }
        }

        if (shotManifestSchema is null)
            throw new FileNotFoundException("The shot manifest schema was not found.");
        EvaluationResults evaluation = shotManifestSchema.Evaluate(
            instance,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });
        if (!evaluation.IsValid)
        {
            throw new LipSyncReviewInputException(
                "manifest.schema",
                "The shot manifest does not satisfy the Draft 2020-12 contract.");
        }
    }

    private static LipSyncReviewLocalizedValue RequiredLocalizedValue(
        JsonElement parent,
        string propertyName)
    {
        JsonElement value = parent.GetProperty(propertyName);
        return new LipSyncReviewLocalizedValue(
            RequiredString(value, "localizationKey"),
            RequiredString(value, "text"));
    }

    private static LipSyncReviewMediaPin RequiredMediaPin(
        JsonElement parent,
        string propertyName) =>
        ParseMediaPin(parent.GetProperty(propertyName));

    private static LipSyncReviewMediaPin? OptionalMediaPin(
        JsonElement parent,
        string propertyName)
    {
        JsonElement element = parent.GetProperty(propertyName);
        return element.ValueKind == JsonValueKind.Null ? null : ParseMediaPin(element);
    }

    private static LipSyncReviewMediaPin ParseMediaPin(JsonElement element) =>
        new(
            RequiredString(element, "mediaRef"),
            RequiredString(element, "assetVersion"),
            RequiredString(element, "contentHash"),
            RequiredString(element, "mediaType"));

    private static string RequiredString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new LipSyncReviewInputException(
                "manifest.shape",
                $"Required string '{propertyName}' is missing or empty.");
        }

        return value.GetString()!;
    }

    private static string CanonicalPin(LipSyncReviewMediaPin pin) =>
        string.Join("\n", pin.MediaRef, pin.AssetVersion, pin.ContentHash, pin.MediaType);

    private static string HashCanonical(params string[] parts) =>
        "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts))))
            .ToLowerInvariant();
}

public sealed class LipSyncReviewInputException : Exception
{
    public LipSyncReviewInputException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NarrativeCompiler;

public sealed class StoryBundleCompiler
{
    private const string ChapterFileName = "chapter.source.json";
    private const string LocalizationFileName = "localization.zh-CN.json";
    private const string OutputFileName = "story.bundle.json";

    public string Compile(string sourceDirectory, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var sourcePath = Path.GetFullPath(sourceDirectory);
        var outputPath = Path.GetFullPath(outputDirectory);

        // The compiler deliberately reads exactly these two reviewed inputs. In particular,
        // it never scans sibling content or the internal originalization crosswalk.
        var chapter = ReadObject(
            Path.Combine(sourcePath, ChapterFileName),
            ChapterFileName);
        var localization = ReadObject(
            Path.Combine(sourcePath, LocalizationFileName),
            LocalizationFileName);

        RequireApproved(chapter, "/approvalStatus", ChapterFileName);
        var sceneNodes = RequiredArray(chapter, "sceneNodes", "/sceneNodes", ChapterFileName);
        var mediaAssets = RequiredArray(chapter, "mediaAssets", "/mediaAssets", ChapterFileName);
        var voiceIntents = OptionalArray(chapter, "voiceIntents", "/voiceIntents", ChapterFileName);
        var npcResponses = OptionalArray(chapter, "npcResponses", "/npcResponses", ChapterFileName);
        RequireApprovedItems(sceneNodes, "/sceneNodes", ChapterFileName);
        RequireApprovedItems(mediaAssets, "/mediaAssets", ChapterFileName);
        RequireApprovedItems(voiceIntents, "/voiceIntents", ChapterFileName);
        RequireApprovedItems(npcResponses, "/npcResponses", ChapterFileName);
        RequireApproved(localization, "/approvalStatus", LocalizationFileName);

        var schemaVersion = RequiredString(
            chapter,
            "schemaVersion",
            "/schemaVersion",
            ChapterFileName);
        var id = RequiredString(chapter, "id", "/id", ChapterFileName);
        var entryNodeId = RequiredString(
            chapter,
            "entryNodeId",
            "/entryNodeId",
            ChapterFileName);
        var distributionStatus = RequiredString(
            chapter,
            "distributionStatus",
            "/distributionStatus",
            ChapterFileName);
        var language = RequiredString(
            localization,
            "language",
            "/language",
            LocalizationFileName);
        var entries = RequiredObject(
            localization,
            "entries",
            "/entries",
            LocalizationFileName);

        var bundle = new JsonObject
        {
            ["schemaVersion"] = schemaVersion,
            ["id"] = id,
            ["entryNodeId"] = entryNodeId,
            ["contentHash"] = string.Empty,
            ["approvalStatus"] = "approved",
            ["distributionStatus"] = distributionStatus,
            ["localizations"] = new JsonObject
            {
                [language] = entries.DeepClone(),
            },
            ["storyFacts"] = new JsonArray(),
            ["characterDefinitions"] = new JsonArray(),
            ["characterStates"] = new JsonArray(),
            ["questDefinitions"] = new JsonArray(),
            ["sceneNodes"] = sceneNodes.DeepClone(),
            ["voiceIntents"] = voiceIntents.DeepClone(),
            ["npcResponses"] = npcResponses.DeepClone(),
            ["combatantStates"] = new JsonArray(),
            ["encounterDefinitions"] = new JsonArray(),
            ["combatResolutions"] = new JsonArray(),
            ["mediaAssets"] = mediaAssets.DeepClone(),
            ["generationJobs"] = new JsonArray(),
            ["saveGames"] = new JsonArray(),
        };

        bundle["contentHash"] = ComputeContentHash(bundle);
        var outputBytes = SerializeIndented(bundle);
        return WriteAtomically(outputPath, outputBytes);
    }

    private static JsonObject ReadObject(string path, string sourceName)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllBytes(path)) as JsonObject
                ?? throw Error(sourceName, "/", "expected a JSON object");
        }
        catch (JsonException exception)
        {
            throw Error(sourceName, "/", $"invalid JSON: {exception.Message}");
        }
    }

    private static void RequireApprovedItems(
        JsonArray items,
        string arrayPointer,
        string sourceName)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var itemPointer = $"{arrayPointer}/{index}";
            if (items[index] is not JsonObject item)
            {
                throw Error(sourceName, itemPointer, "expected a JSON object");
            }

            RequireApproved(item, $"{itemPointer}/approvalStatus", sourceName);
        }
    }

    private static void RequireApproved(
        JsonObject owner,
        string pointer,
        string sourceName)
    {
        var propertyName = LastPointerSegment(pointer);
        if (owner[propertyName] is not JsonValue value ||
            !value.TryGetValue<string>(out var status) ||
            !string.Equals(status, "approved", StringComparison.Ordinal))
        {
            throw Error(sourceName, pointer, "expected the value 'approved'");
        }
    }

    private static string RequiredString(
        JsonObject owner,
        string propertyName,
        string pointer,
        string sourceName)
    {
        if (owner[propertyName] is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw Error(sourceName, pointer, "expected a non-empty string");
    }

    private static JsonArray RequiredArray(
        JsonObject owner,
        string propertyName,
        string pointer,
        string sourceName) =>
        owner[propertyName] as JsonArray
        ?? throw Error(sourceName, pointer, "expected an array");

    private static JsonArray OptionalArray(
        JsonObject owner,
        string propertyName,
        string pointer,
        string sourceName)
    {
        if (owner[propertyName] is null)
        {
            return new JsonArray();
        }

        return owner[propertyName] as JsonArray
            ?? throw Error(sourceName, pointer, "expected an array");
    }

    private static JsonObject RequiredObject(
        JsonObject owner,
        string propertyName,
        string pointer,
        string sourceName) =>
        owner[propertyName] as JsonObject
        ?? throw Error(sourceName, pointer, "expected a JSON object");

    private static string ComputeContentHash(JsonObject bundle)
    {
        var content = bundle.DeepClone().AsObject();
        content.Remove("contentHash");
        var canonicalBytes = SerializeCanonical(content);
        var digest = SHA256.HashData(canonicalBytes);
        return "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static byte[] SerializeCanonical(JsonNode? node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, node);
        }

        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (var property in value.OrderBy(
                             property => property.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (var item in value)
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static byte[] SerializeIndented(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = true }))
        {
            node.WriteTo(writer);
        }

        return stream.ToArray();
    }

    private static string WriteAtomically(string outputDirectory, byte[] bytes)
    {
        Directory.CreateDirectory(outputDirectory);
        var destinationPath = Path.Combine(outputDirectory, OutputFileName);
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{OutputFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string LastPointerSegment(string pointer)
    {
        var separator = pointer.LastIndexOf('/');
        return separator >= 0 ? pointer[(separator + 1)..] : pointer;
    }

    private static InvalidDataException Error(
        string sourceName,
        string pointer,
        string message) =>
        new($"{sourceName}#{pointer}: {message}.");
}

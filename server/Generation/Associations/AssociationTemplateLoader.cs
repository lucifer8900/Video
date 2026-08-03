using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lingmai.RedMist.Contracts.Associations;

namespace Lingmai.RedMist.Generation.Associations;

public sealed class AssociationTemplateLoadException : IOException
{
    public AssociationTemplateLoadException(
        string sourcePath,
        string jsonPath,
        string jsonPointer,
        string code)
        : base($"Association template '{sourcePath}' is invalid at '{jsonPointer}' ({code}).")
    {
        SourcePath = sourcePath;
        JsonPath = jsonPath;
        JsonPointer = jsonPointer;
        Code = code;
    }

    public string SourcePath { get; }

    public string JsonPath { get; }

    public string JsonPointer { get; }

    public string Code { get; }
}

public sealed record LoadedAssociationTemplate(
    string SourcePath,
    AssociationTemplateContract Template);

public sealed class ApprovedAssociationTemplate
{
    internal ApprovedAssociationTemplate(
        string sourcePath,
        AssociationTemplateContract template)
    {
        SourcePath = sourcePath;
        Template = template;
    }

    public string SourcePath { get; }

    public AssociationTemplateContract Template { get; }
}

public sealed class AssociationTemplateCatalog
{
    internal AssociationTemplateCatalog(IReadOnlyList<LoadedAssociationTemplate> templates)
    {
        AllTemplates = Array.AsReadOnly(templates.ToArray());
        ApprovedTemplates = Array.AsReadOnly(templates
            .Where(item => item.Template.ApprovalStatus == "approved")
            .Select(item => new ApprovedAssociationTemplate(
                item.SourcePath,
                Freeze(item.Template)))
            .ToArray());
    }

    public IReadOnlyList<LoadedAssociationTemplate> AllTemplates { get; }

    public IReadOnlyList<ApprovedAssociationTemplate> ApprovedTemplates { get; }

    private static AssociationTemplateContract Freeze(AssociationTemplateContract source)
    {
        var requirements = source.Preconditions.RequiresLedger
            .Select(item => new AssociationLedgerRequirementContract(
                item.Type,
                item.MinSeverity,
                item.MaxAgeWorldClock))
            .ToArray();
        var preconditions = new AssociationPreconditionsContract(
            Array.AsReadOnly(requirements),
            Array.AsReadOnly(source.Preconditions.ForbidsFacts.ToArray()),
            Array.AsReadOnly(source.Preconditions.ChapterWindow.ToArray()));
        var slots = new Dictionary<string, AssociationParameterSlotContract>(StringComparer.Ordinal);
        foreach ((string name, AssociationParameterSlotContract slot) in source.ParameterSlots)
        {
            slots.Add(name, new AssociationParameterSlotContract(slot.Source));
        }

        var effects = source.AllowedEffects
            .Select(item => new AssociationAllowedEffectContract(
                item.Op,
                item.Field,
                item.Range is null ? null : Array.AsReadOnly(item.Range.ToArray()),
                item.Value))
            .ToArray();
        return new AssociationTemplateContract(
            source.SchemaVersion,
            source.TemplateId,
            source.Kind,
            source.AuthorityLevel,
            preconditions,
            new ReadOnlyDictionary<string, AssociationParameterSlotContract>(slots),
            Array.AsReadOnly(source.InjectionPoints.ToArray()),
            source.TextPolicy,
            Array.AsReadOnly(effects),
            source.MediaPolicy,
            source.FallbackThreadId,
            source.CooldownWorldClock,
            source.MaxTriggersPerPlayer,
            source.ApprovalStatus);
    }
}

public sealed class AssociationTemplateLoader
{
    public const int MaximumFileCount = 256;
    public const int MaximumTemplateBytes = 128 * 1024;
    public const int MaximumTotalBytes = 8 * 1024 * 1024;
    public const int MaximumJsonDepth = 32;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth,
    };
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly AssociationTemplateValidator _validator;

    public AssociationTemplateLoader(AssociationTemplateValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public AssociationTemplateCatalog LoadDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw Error(".", "$", string.Empty, AssociationTemplateErrorCodes.InvalidDirectory);
        }

        string rootPath;
        try
        {
            rootPath = Path.GetFullPath(directoryPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            throw Error(".", "$", string.Empty, AssociationTemplateErrorCodes.InvalidDirectory);
        }

        if (!Directory.Exists(rootPath))
        {
            throw Error(".", "$", string.Empty, AssociationTemplateErrorCodes.InvalidDirectory);
        }

        if ((GetAttributes(rootPath, ".") & FileAttributes.ReparsePoint) != 0)
        {
            throw Error(".", "$", string.Empty, AssociationTemplateErrorCodes.ReparsePoint);
        }

        string[] files = EnumerateTemplateFiles(rootPath);
        if (files.Length > MaximumFileCount)
        {
            throw Error(".", "$", string.Empty, AssociationTemplateErrorCodes.TooManyFiles);
        }

        var loaded = new List<LoadedAssociationTemplate>(files.Length);
        long totalBytes = 0;
        foreach (string filePath in files)
        {
            string sourcePath = SafeRelativePath(rootPath, filePath);
            if ((GetAttributes(filePath, sourcePath) & FileAttributes.ReparsePoint) != 0)
            {
                throw Error(sourcePath, "$", string.Empty, AssociationTemplateErrorCodes.ReparsePoint);
            }

            byte[] bytes = ReadBounded(filePath, sourcePath);
            totalBytes += bytes.Length;
            if (totalBytes > MaximumTotalBytes)
            {
                throw Error(sourcePath, "$", string.Empty, AssociationTemplateErrorCodes.TotalSizeExceeded);
            }

            AssociationTemplateContract template = Parse(bytes, sourcePath);
            loaded.Add(new LoadedAssociationTemplate(sourcePath, template));
        }

        foreach (LoadedAssociationTemplate item in loaded)
        {
            AssociationTemplateValidationIssue? issue = _validator.ValidateIdentity(item.Template);
            if (issue is not null)
            {
                throw Error(item.SourcePath, issue.JsonPath, issue.JsonPointer, issue.Code);
            }
        }

        RejectDuplicateTemplateIds(loaded);
        foreach (LoadedAssociationTemplate item in loaded)
        {
            AssociationTemplateValidationIssue? issue = _validator.Validate(item.Template).FirstOrDefault();
            if (issue is not null)
            {
                throw Error(item.SourcePath, issue.JsonPath, issue.JsonPointer, issue.Code);
            }
        }

        return new AssociationTemplateCatalog(loaded.ToArray());
    }

    private static byte[] ReadBounded(string filePath, string sourcePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            if (stream.Length > MaximumTemplateBytes)
            {
                throw Error(sourcePath, "$", string.Empty, AssociationTemplateErrorCodes.FileTooLarge);
            }

            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (AssociationTemplateLoadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            throw Error(sourcePath, "$", string.Empty, AssociationTemplateErrorCodes.InvalidDirectory);
        }
    }

    private static string[] EnumerateTemplateFiles(string rootPath)
    {
        try
        {
            var files = new List<string>();
            foreach (string path in Directory.EnumerateFiles(rootPath, "*", SearchOption.TopDirectoryOnly))
            {
                if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                files.Add(Path.GetFullPath(path));
                if (files.Count > MaximumFileCount)
                {
                    throw Error(".", "$", string.Empty, AssociationTemplateErrorCodes.TooManyFiles);
                }
            }

            files.Sort((left, right) => StringComparer.Ordinal.Compare(
                Path.GetFileName(left),
                Path.GetFileName(right)));
            return files.ToArray();
        }
        catch (AssociationTemplateLoadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            throw Error(".", "$", string.Empty, AssociationTemplateErrorCodes.InvalidDirectory);
        }
    }

    private static FileAttributes GetAttributes(string path, string sourcePath)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            throw Error(sourcePath, "$", string.Empty, AssociationTemplateErrorCodes.InvalidDirectory);
        }
    }

    private static AssociationTemplateContract Parse(byte[] bytes, string sourcePath)
    {
        try
        {
            ReadOnlyMemory<byte> json = RemoveUtf8Bom(bytes);
            using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
            DetectDuplicateProperties(document.RootElement, "$", string.Empty, sourcePath);
            ValidateRequiredStructure(document.RootElement, sourcePath);
            return JsonSerializer.Deserialize<AssociationTemplateContract>(json.Span, SerializerOptions)
                   ?? throw Error(sourcePath, "$", string.Empty, AssociationTemplateErrorCodes.InvalidJson);
        }
        catch (AssociationTemplateLoadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            string jsonPath = string.IsNullOrWhiteSpace(exception.Path) ? "$" : exception.Path;
            throw Error(
                sourcePath,
                jsonPath,
                JsonPathToPointer(jsonPath),
                AssociationTemplateErrorCodes.InvalidJson);
        }
    }

    private static void ValidateRequiredStructure(JsonElement root, string sourcePath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Error(sourcePath, "$", string.Empty, AssociationTemplateErrorCodes.InvalidContract);
        }

        string[] rootProperties =
        {
            "schemaVersion",
            "templateId",
            "kind",
            "authorityLevel",
            "preconditions",
            "parameterSlots",
            "injectionPoints",
            "textPolicy",
            "allowedEffects",
            "mediaPolicy",
            "fallbackThreadId",
            "cooldownWorldClock",
            "maxTriggersPerPlayer",
            "approvalStatus",
        };
        foreach (string propertyName in rootProperties)
        {
            string code = propertyName == "fallbackThreadId"
                ? AssociationTemplateErrorCodes.MissingFallback
                : AssociationTemplateErrorCodes.InvalidContract;
            RequireProperty(root, propertyName, "$", string.Empty, sourcePath, code);
        }

        JsonElement preconditions = root.GetProperty("preconditions");
        if (preconditions.ValueKind == JsonValueKind.Object)
        {
            RequireProperty(
                preconditions,
                "requiresLedger",
                "$.preconditions",
                "/preconditions",
                sourcePath);
            RequireProperty(
                preconditions,
                "forbidsFacts",
                "$.preconditions",
                "/preconditions",
                sourcePath);
            RequireProperty(
                preconditions,
                "chapterWindow",
                "$.preconditions",
                "/preconditions",
                sourcePath);

            JsonElement requirements = preconditions.GetProperty("requiresLedger");
            if (requirements.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement requirement in requirements.EnumerateArray())
                {
                    if (requirement.ValueKind == JsonValueKind.Object)
                    {
                        string path = $"$.preconditions.requiresLedger[{index}]";
                        string pointer = $"/preconditions/requiresLedger/{index}";
                        RequireProperty(requirement, "type", path, pointer, sourcePath);
                        RequireProperty(requirement, "minSeverity", path, pointer, sourcePath);
                        RequireProperty(requirement, "maxAgeWorldClock", path, pointer, sourcePath);
                    }

                    index++;
                }
            }
        }

        JsonElement parameterSlots = root.GetProperty("parameterSlots");
        if (parameterSlots.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty slot in parameterSlots.EnumerateObject())
            {
                if (slot.Value.ValueKind != JsonValueKind.Object) continue;
                RequireProperty(
                    slot.Value,
                    "source",
                    "$.parameterSlots." + slot.Name,
                    "/parameterSlots/" + EscapePointer(slot.Name),
                    sourcePath);
            }
        }

        JsonElement effects = root.GetProperty("allowedEffects");
        if (effects.ValueKind != JsonValueKind.Array) return;

        int effectIndex = 0;
        foreach (JsonElement effect in effects.EnumerateArray())
        {
            if (effect.ValueKind == JsonValueKind.Object)
            {
                string path = $"$.allowedEffects[{effectIndex}]";
                string pointer = $"/allowedEffects/{effectIndex}";
                JsonElement op = RequireProperty(effect, "op", path, pointer, sourcePath);
                if (op.ValueKind == JsonValueKind.String)
                {
                    string? operation = op.GetString();
                    if (operation == AssociationEffectOperations.Relationship)
                    {
                        RequireProperty(effect, "field", path, pointer, sourcePath);
                        RequireProperty(effect, "range", path, pointer, sourcePath);
                    }
                    else if (operation is AssociationEffectOperations.Clue or AssociationEffectOperations.BranchUnlock)
                    {
                        RequireProperty(effect, "value", path, pointer, sourcePath);
                    }
                }
            }

            effectIndex++;
        }
    }

    private static JsonElement RequireProperty(
        JsonElement parent,
        string propertyName,
        string parentPath,
        string parentPointer,
        string sourcePath,
        string code = AssociationTemplateErrorCodes.InvalidContract)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            throw Error(
                sourcePath,
                parentPath + "." + propertyName,
                parentPointer + "/" + EscapePointer(propertyName),
                code);
        }

        return value;
    }

    private static void DetectDuplicateProperties(
        JsonElement value,
        string path,
        string pointer,
        string sourcePath)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var exact = new HashSet<string>(StringComparer.Ordinal);
            var casing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                string childPath = AppendPath(path, property.Name);
                string childPointer = pointer + "/" + EscapePointer(property.Name);
                if (!exact.Add(property.Name) || !casing.Add(property.Name))
                {
                    throw Error(
                        sourcePath,
                        childPath,
                        childPointer,
                        AssociationTemplateErrorCodes.DuplicateJsonProperty);
                }

                DetectDuplicateProperties(property.Value, childPath, childPointer, sourcePath);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                DetectDuplicateProperties(
                    item,
                    $"{path}[{index}]",
                    pointer + "/" + index,
                    sourcePath);
                index++;
            }
        }
    }

    private static void RejectDuplicateTemplateIds(IReadOnlyList<LoadedAssociationTemplate> templates)
    {
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var casing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LoadedAssociationTemplate item in templates)
        {
            string? templateId = item.Template.TemplateId;
            if (templateId is null) continue;
            if (!exact.Add(templateId) || !casing.Add(templateId))
            {
                throw Error(
                    item.SourcePath,
                    "$.templateId",
                    "/templateId",
                    AssociationTemplateErrorCodes.DuplicateTemplateId);
            }
        }
    }

    private static string SafeRelativePath(string rootPath, string filePath)
    {
        string relative = Path.GetRelativePath(rootPath, filePath);
        if (Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw Error(".", "$", string.Empty, AssociationTemplateErrorCodes.InvalidDirectory);
        }

        return string.Concat(relative.Select(character =>
                char.IsControl(character) ? '_' : character))
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static ReadOnlyMemory<byte> RemoveUtf8Bom(byte[] value) =>
        value.Length >= 3 && value[0] == 0xEF && value[1] == 0xBB && value[2] == 0xBF
            ? value.AsMemory(3)
            : value;

    private static string AppendPath(string path, string propertyName) =>
        propertyName.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? path + "." + propertyName
            : path + "['" + propertyName.Replace("'", "\\'", StringComparison.Ordinal) + "']";

    private static string JsonPathToPointer(string path)
    {
        if (path == "$") return string.Empty;
        var pointer = new StringBuilder();
        foreach (Match match in Regex.Matches(path, "(?:\\.([^\\.\\[\\]]+)|\\[(\\d+)\\])"))
        {
            string segment = match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value;
            string safeSegment = string.Concat(segment
                .Take(128)
                .Select(character => char.IsControl(character) ? '_' : character));
            pointer.Append('/').Append(EscapePointer(safeSegment));
        }

        return pointer.ToString();
    }

    private static string EscapePointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static AssociationTemplateLoadException Error(
        string sourcePath,
        string jsonPath,
        string jsonPointer,
        string code) =>
        new(
            SanitizeDiagnostic(sourcePath, 512),
            SanitizeDiagnostic(jsonPath, 512),
            SanitizeDiagnostic(jsonPointer, 512),
            code);

    private static string SanitizeDiagnostic(string value, int maximumLength) =>
        string.Concat(value
            .Take(maximumLength)
            .Select(character => char.IsControl(character) ? '_' : character));
}

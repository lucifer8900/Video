using System.Globalization;
using System.Text.RegularExpressions;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Contracts.Ledger;

namespace Lingmai.RedMist.Generation.Associations;

public static class AssociationTemplateErrorCodes
{
    public const string InvalidDirectory = "association.invalid_directory";
    public const string ReparsePoint = "association.reparse_point";
    public const string TooManyFiles = "association.too_many_files";
    public const string FileTooLarge = "association.file_too_large";
    public const string TotalSizeExceeded = "association.total_size_exceeded";
    public const string InvalidJson = "association.invalid_json";
    public const string DuplicateJsonProperty = "association.duplicate_json_property";
    public const string DuplicateTemplateId = "association.duplicate_template_id";
    public const string InvalidContract = "association.invalid_contract";
    public const string UnknownEntity = "association.unknown_entity";
    public const string UnknownParameterSlot = "association.unknown_parameter_slot";
    public const string UnauthorizedEffect = "association.unauthorized_effect";
    public const string MissingFallback = "association.missing_fallback";
    public const string UnknownFallback = "association.unknown_fallback";
    public const string UnapprovedFallback = "association.unapproved_fallback";
}

public sealed record AssociationTemplateValidationIssue(
    string JsonPath,
    string JsonPointer,
    string Code);

public sealed class AssociationTemplateValidator
{
    private static readonly Regex IdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SlotNamePattern = new(
        "^[A-Za-z][A-Za-z0-9_]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TemplateReferencePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]*(?:\\{[A-Za-z][A-Za-z0-9_]*\\}[A-Za-z0-9._:-]*)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlaceholderPattern = new(
        "\\{(?<slot>[A-Za-z][A-Za-z0-9_]*)\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ActorSourcePattern = new(
        "^ledger\\.actors\\[[0-7]\\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlySet<string> ApprovalStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        "draft",
        "needs_review",
        "approved",
    };
    private static readonly IReadOnlySet<string> InjectionPoints = new HashSet<string>(StringComparer.Ordinal)
    {
        "node_intro",
        "travel_event",
        "npc_mention",
    };
    private static readonly IReadOnlySet<string> RelationshipFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "trust",
        "respect",
        "fear",
        "debt",
        "suspicion",
        "affection",
    };

    private readonly AssociationEntityRegistry _entities;
    private readonly AssociationFallbackThreadRegistry _fallbackThreads;

    public AssociationTemplateValidator(
        AssociationEntityRegistry entities,
        AssociationFallbackThreadRegistry fallbackThreads)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _fallbackThreads = fallbackThreads ?? throw new ArgumentNullException(nameof(fallbackThreads));
    }

    public AssociationTemplateValidationIssue? ValidateIdentity(
        AssociationTemplateContract template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return IsId(template.TemplateId) && template.TemplateId.StartsWith("assoc.", StringComparison.Ordinal)
            ? null
            : new AssociationTemplateValidationIssue(
                "$.templateId",
                "/templateId",
                AssociationTemplateErrorCodes.InvalidContract);
    }

    public IReadOnlyList<AssociationTemplateValidationIssue> Validate(
        AssociationTemplateContract template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var issues = new List<AssociationTemplateValidationIssue>();

        AddUnless(issues, template.SchemaVersion == "1.0.0", "$.schemaVersion", "/schemaVersion");
        AddUnless(
            issues,
            IsId(template.TemplateId) && template.TemplateId.StartsWith("assoc.", StringComparison.Ordinal),
            "$.templateId",
            "/templateId");
        AddUnless(issues, AssociationKinds.All.Contains(template.Kind), "$.kind", "/kind");
        AddUnless(issues, template.AuthorityLevel == "L2", "$.authorityLevel", "/authorityLevel");

        ValidatePreconditions(template, issues);
        ValidateParameterSlots(template, issues);
        ValidateInjectionPoints(template, issues);
        AddUnless(
            issues,
            template.TextPolicy == "llm_variant_within_style_guide",
            "$.textPolicy",
            "/textPolicy");
        ValidateEffects(template, issues);
        AddUnless(
            issues,
            AssociationMediaPolicies.All.Contains(template.MediaPolicy),
            "$.mediaPolicy",
            "/mediaPolicy");
        ValidateFallback(template, issues);
        AddUnless(
            issues,
            template.CooldownWorldClock is >= 0 and <= int.MaxValue,
            "$.cooldownWorldClock",
            "/cooldownWorldClock");
        AddUnless(
            issues,
            template.MaxTriggersPerPlayer is >= 1 and <= 100,
            "$.maxTriggersPerPlayer",
            "/maxTriggersPerPlayer");
        AddUnless(
            issues,
            ApprovalStatuses.Contains(template.ApprovalStatus),
            "$.approvalStatus",
            "/approvalStatus");

        return issues;
    }

    private void ValidatePreconditions(
        AssociationTemplateContract template,
        ICollection<AssociationTemplateValidationIssue> issues)
    {
        AssociationPreconditionsContract? value = template.Preconditions;
        if (value is null)
        {
            Add(issues, "$.preconditions", "/preconditions");
            return;
        }

        if (value.RequiresLedger is null || value.RequiresLedger.Count is < 1 or > 16)
        {
            Add(issues, "$.preconditions.requiresLedger", "/preconditions/requiresLedger");
        }
        else
        {
            for (int index = 0; index < value.RequiresLedger.Count; index++)
            {
                AssociationLedgerRequirementContract? requirement = value.RequiresLedger[index];
                string path = $"$.preconditions.requiresLedger[{index}]";
                string pointer = $"/preconditions/requiresLedger/{index}";
                if (requirement is null)
                {
                    Add(issues, path, pointer);
                    continue;
                }

                AddUnless(issues, LedgerEventTypes.All.Contains(requirement.Type), path + ".type", pointer + "/type");
                AddUnless(issues, requirement.MinSeverity is >= 1 and <= 5, path + ".minSeverity", pointer + "/minSeverity");
                AddUnless(
                    issues,
                    requirement.MaxAgeWorldClock is >= 0 and <= int.MaxValue,
                    path + ".maxAgeWorldClock",
                    pointer + "/maxAgeWorldClock");
            }
        }

        if (value.ForbidsFacts is null || value.ForbidsFacts.Count > 32)
        {
            Add(issues, "$.preconditions.forbidsFacts", "/preconditions/forbidsFacts");
        }
        else
        {
            ValidateUniqueStrings(value.ForbidsFacts, "$.preconditions.forbidsFacts", "/preconditions/forbidsFacts", issues);
            for (int index = 0; index < value.ForbidsFacts.Count; index++)
            {
                ValidateEntityReference(
                    value.ForbidsFacts[index],
                    template.ParameterSlots,
                    _entities.ContainsFact,
                    "fact.",
                    $"$.preconditions.forbidsFacts[{index}]",
                    $"/preconditions/forbidsFacts/{index}",
                    issues);
            }
        }

        if (value.ChapterWindow is null || value.ChapterWindow.Count is < 1 or > 32)
        {
            Add(issues, "$.preconditions.chapterWindow", "/preconditions/chapterWindow");
        }
        else
        {
            ValidateUniqueStrings(value.ChapterWindow, "$.preconditions.chapterWindow", "/preconditions/chapterWindow", issues);
            for (int index = 0; index < value.ChapterWindow.Count; index++)
            {
                string chapter = value.ChapterWindow[index];
                if (!IsId(chapter) || !_entities.ContainsChapter(chapter))
                {
                    Add(
                        issues,
                        $"$.preconditions.chapterWindow[{index}]",
                        $"/preconditions/chapterWindow/{index}",
                        AssociationTemplateErrorCodes.UnknownEntity);
                }
            }
        }
    }

    private static void ValidateParameterSlots(
        AssociationTemplateContract template,
        ICollection<AssociationTemplateValidationIssue> issues)
    {
        if (template.ParameterSlots is null || template.ParameterSlots.Count > 16)
        {
            Add(issues, "$.parameterSlots", "/parameterSlots");
            return;
        }

        foreach ((string name, AssociationParameterSlotContract? slot) in template.ParameterSlots)
        {
            string escaped = EscapePointer(name);
            string path = "$.parameterSlots." + name;
            string pointer = "/parameterSlots/" + escaped;
            AddUnless(issues, SlotNamePattern.IsMatch(name), path, pointer);
            if (slot is null)
            {
                Add(issues, path, pointer);
                continue;
            }

            bool validSource = slot.Source == "route.upcomingNode.location" ||
                               (slot.Source is not null && ActorSourcePattern.IsMatch(slot.Source));
            AddUnless(issues, validSource, path + ".source", pointer + "/source");
        }
    }

    private static void ValidateInjectionPoints(
        AssociationTemplateContract template,
        ICollection<AssociationTemplateValidationIssue> issues)
    {
        if (template.InjectionPoints is null || template.InjectionPoints.Count is < 1 or > 3)
        {
            Add(issues, "$.injectionPoints", "/injectionPoints");
            return;
        }

        ValidateUniqueStrings(template.InjectionPoints, "$.injectionPoints", "/injectionPoints", issues);
        for (int index = 0; index < template.InjectionPoints.Count; index++)
        {
            AddUnless(
                issues,
                InjectionPoints.Contains(template.InjectionPoints[index]),
                $"$.injectionPoints[{index}]",
                $"/injectionPoints/{index}");
        }
    }

    private void ValidateEffects(
        AssociationTemplateContract template,
        ICollection<AssociationTemplateValidationIssue> issues)
    {
        if (template.AllowedEffects is null || template.AllowedEffects.Count > 16)
        {
            Add(issues, "$.allowedEffects", "/allowedEffects");
            return;
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < template.AllowedEffects.Count; index++)
        {
            AssociationAllowedEffectContract? effect = template.AllowedEffects[index];
            string path = $"$.allowedEffects[{index}]";
            string pointer = $"/allowedEffects/{index}";
            if (effect is null)
            {
                Add(issues, path, pointer);
                continue;
            }

            if (!AssociationEffectOperations.All.Contains(effect.Op))
            {
                Add(issues, path + ".op", pointer + "/op", AssociationTemplateErrorCodes.UnauthorizedEffect);
                continue;
            }

            string signature = string.Join(
                '|',
                effect.Op,
                effect.Field ?? string.Empty,
                effect.Value ?? string.Empty,
                effect.Range is null
                    ? string.Empty
                    : string.Join(',', effect.Range.Select(number => number.ToString("R", CultureInfo.InvariantCulture))));
            AddUnless(issues, unique.Add(signature), path, pointer);

            if (effect.Op == AssociationEffectOperations.Relationship)
            {
                bool validRange = effect.Range is { Count: 2 } &&
                                  effect.Range.All(number => double.IsFinite(number) && number is >= -10 and <= 10) &&
                                  effect.Range[0] <= effect.Range[1];
                AddUnless(issues, effect.Field is not null && RelationshipFields.Contains(effect.Field), path + ".field", pointer + "/field");
                AddUnless(issues, validRange, path + ".range", pointer + "/range");
                AddUnless(issues, effect.Value is null, path + ".value", pointer + "/value");
                if (template.ParameterSlots is null ||
                    !template.ParameterSlots.TryGetValue("target", out AssociationParameterSlotContract? target))
                {
                    Add(issues, "$.parameterSlots.target", "/parameterSlots/target");
                }
                else
                {
                    AddUnless(
                        issues,
                        target is not null &&
                        target.Source is not null &&
                        ActorSourcePattern.IsMatch(target.Source),
                        "$.parameterSlots.target.source",
                        "/parameterSlots/target/source");
                }
                continue;
            }

            AddUnless(issues, effect.Field is null, path + ".field", pointer + "/field");
            AddUnless(issues, effect.Range is null, path + ".range", pointer + "/range");
            Func<string, bool> contains = effect.Op == AssociationEffectOperations.Clue
                ? _entities.ContainsClue
                : _entities.ContainsBranch;
            string requiredPrefix = effect.Op == AssociationEffectOperations.Clue
                ? "clue."
                : "branch.";
            ValidateEntityReference(
                effect.Value,
                template.ParameterSlots,
                contains,
                requiredPrefix,
                path + ".value",
                pointer + "/value",
                issues);
        }
    }

    private void ValidateFallback(
        AssociationTemplateContract template,
        ICollection<AssociationTemplateValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(template.FallbackThreadId))
        {
            Add(
                issues,
                "$.fallbackThreadId",
                "/fallbackThreadId",
                AssociationTemplateErrorCodes.MissingFallback);
            return;
        }

        if (!IsId(template.FallbackThreadId) ||
            !template.FallbackThreadId.StartsWith("assoc.", StringComparison.Ordinal))
        {
            Add(issues, "$.fallbackThreadId", "/fallbackThreadId");
            return;
        }

        if (!_fallbackThreads.TryGetApprovalStatus(template.FallbackThreadId, out string fallbackApproval))
        {
            Add(
                issues,
                "$.fallbackThreadId",
                "/fallbackThreadId",
                AssociationTemplateErrorCodes.UnknownFallback);
        }
        else if (template.ApprovalStatus == "approved" && fallbackApproval != "approved")
        {
            Add(
                issues,
                "$.fallbackThreadId",
                "/fallbackThreadId",
                AssociationTemplateErrorCodes.UnapprovedFallback);
        }
    }

    private static void ValidateEntityReference(
        string? reference,
        IReadOnlyDictionary<string, AssociationParameterSlotContract>? slots,
        Func<string, bool> containsLiteral,
        string requiredPrefix,
        string path,
        string pointer,
        ICollection<AssociationTemplateValidationIssue> issues)
    {
        if (reference is null ||
            reference.Length is < 1 or > 160 ||
            !TemplateReferencePattern.IsMatch(reference))
        {
            Add(issues, path, pointer);
            return;
        }

        MatchCollection placeholders = PlaceholderPattern.Matches(reference);
        if (!reference.StartsWith(requiredPrefix, StringComparison.Ordinal))
        {
            Add(issues, path, pointer, AssociationTemplateErrorCodes.UnknownEntity);
            return;
        }

        if (placeholders.Count == 0)
        {
            if (!containsLiteral(reference))
            {
                Add(issues, path, pointer, AssociationTemplateErrorCodes.UnknownEntity);
            }

            return;
        }

        foreach (Match placeholder in placeholders)
        {
            string name = placeholder.Groups["slot"].Value;
            if (slots is null || !slots.ContainsKey(name))
            {
                Add(issues, path, pointer, AssociationTemplateErrorCodes.UnknownParameterSlot);
                return;
            }
        }
    }

    private static void ValidateUniqueStrings(
        IReadOnlyList<string> values,
        string path,
        string pointer,
        ICollection<AssociationTemplateValidationIssue> issues)
    {
        if (values.Any(value => value is null) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            Add(issues, path, pointer);
        }
    }

    private static bool IsId(string? value) =>
        value is { Length: >= 1 and <= 160 } && IdPattern.IsMatch(value);

    private static void AddUnless(
        ICollection<AssociationTemplateValidationIssue> issues,
        bool condition,
        string path,
        string pointer)
    {
        if (!condition) Add(issues, path, pointer);
    }

    private static void Add(
        ICollection<AssociationTemplateValidationIssue> issues,
        string path,
        string pointer,
        string code = AssociationTemplateErrorCodes.InvalidContract) =>
        issues.Add(new AssociationTemplateValidationIssue(path, pointer, code));

    private static string EscapePointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}

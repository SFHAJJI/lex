using System.Text.Json.Nodes;

namespace Lex.Mcp;

public enum LegalResultKind
{
    Search,
    ExactText,
    Comparison,
    Timeline,
    Ranking,
    Inventory,
    Verification,
}

public enum LegalEffectKind
{
    Provision,
    Diff,
    History,
    Timeline,
    Ranking,
    InForce,
    CitedBy,
    Coverage,
    Workspace,
    Verification,
}

public enum LegalArgumentKind
{
    String,
    Integer,
}

public sealed record LegalArgumentDefinition(
    string Name,
    string Description,
    LegalArgumentKind Kind = LegalArgumentKind.String,
    int MinimumLength = LegalOperationCatalog.MinimumStringLength,
    int MaximumLength = LegalOperationCatalog.MaximumStringLength,
    int? Minimum = null,
    int? Maximum = null,
    bool IsDate = false,
    IReadOnlyList<string>? AllowedValues = null,
    bool Mcp = true,
    bool Planner = true,
    int? MaximumListValues = null,
    int? MaximumListItemLength = null,
    string? PlannerGuidance = null,
    bool Opaque = false,
    bool RequiresAbsoluteHttpUri = false)
{
    internal JsonObject Schema(
        bool planner,
        Func<string, IReadOnlyList<string>?>? valueOverride = null)
    {
        JsonObject schema;
        IReadOnlyList<string>? overridden = null;
        if (Kind == LegalArgumentKind.Integer)
        {
            schema = new JsonObject { ["type"] = "integer" };
            if (Minimum is not null) schema["minimum"] = Minimum.Value;
            if (Maximum is not null) schema["maximum"] = Maximum.Value;
        }
        else
        {
            schema = new JsonObject
            {
                ["type"] = "string",
                ["minLength"] = MinimumLength,
                ["maxLength"] = MaximumLength,
            };
            if (IsDate)
            {
                schema["pattern"] = LegalOperationCatalog.IsoDatePattern;
                if (planner) schema["format"] = "date";
            }
            else if (RequiresAbsoluteHttpUri)
            {
                schema["pattern"] = LegalOperationCatalog.AbsoluteHttpUriPattern;
            }
            overridden = AllowedValues is null ? valueOverride?.Invoke(Name) : null;
            var values = AllowedValues ?? overridden;
            if (values is not null)
                schema["enum"] = new JsonArray(values.Select(value => (JsonNode)value).ToArray());
        }

        var description = planner && overridden is not null
            ? Name == "publisher"
                ? "mounted publisher id; OMIT for the whole corpus"
                : "mounted jurisdiction code; OMIT for the whole corpus"
            : planner ? PlannerGuidance ?? Description : Description;
        if (!string.IsNullOrWhiteSpace(description)) schema["description"] = description;
        return schema;
    }
}

public sealed record LegalRequiredChoice(
    IReadOnlyList<string> Names,
    string Message);

public sealed record LegalConditionalRequirement(
    string WhenArgument,
    string WhenValue,
    string RequiredArgument,
    IReadOnlyList<string>? PlannerAlternativeArguments = null);

public sealed record LegalAllowedValuesCoupling(
    string Argument,
    string WhenArgument,
    IReadOnlyList<string> AllowedValues);

public sealed record LegalDateRangeCoupling(string FromArgument, string ToArgument);

/// <summary>
/// Canonical domain definition for one executable read-only legal operation. Both transports and
/// the assistant planner project this record; neither owns another argument allowlist or bound.
/// </summary>
public sealed record LegalOperationDefinition(
    string Name,
    int PlannerOrdinal,
    int McpOrdinal,
    string Description,
    LegalResultKind ResultKind,
    LegalEffectKind PrimaryEffect,
    bool RequiresWorkResolution,
    IReadOnlyList<LegalArgumentDefinition> Arguments,
    IReadOnlyList<string> McpRequired,
    IReadOnlyList<string>? PlannerRequired = null,
    IReadOnlyList<LegalRequiredChoice>? PlannerRequiredChoices = null,
    IReadOnlyList<string>? DefaultedPlannerDates = null,
    IReadOnlyList<LegalConditionalRequirement>? ConditionalRequirements = null,
    IReadOnlyList<LegalAllowedValuesCoupling>? AllowedValuesCouplings = null,
    LegalDateRangeCoupling? DateRange = null,
    string? PublisherAuthorityArgument = null,
    bool SupportsAnchorResolution = false,
    IReadOnlyList<LegalEffectKind>? AdditionalEffects = null)
{
    public IReadOnlyList<LegalArgumentDefinition> McpArguments { get; } =
        Arguments.Where(argument => argument.Mcp).ToArray();

    public IReadOnlyList<LegalArgumentDefinition> PlannerArguments { get; } =
        Arguments.Where(argument => argument.Planner).ToArray();

    public JsonObject McpToolDefinition() => new()
    {
        ["name"] = Name,
        ["description"] = Description,
        ["inputSchema"] = InputSchema(McpArguments, McpRequired, planner: false),
    };

    public JsonObject PlannerInputSchema(
        Func<string, IReadOnlyList<string>?>? valueOverride = null,
        IReadOnlyList<string>? subjectReferences = null,
        string? resolvedArticleNumber = null)
    {
        IReadOnlyList<LegalArgumentDefinition> arguments = PlannerArguments;
        IReadOnlyList<string> required = PlannerRequired ?? [];
        IReadOnlyList<LegalRequiredChoice> choices = PlannerRequiredChoices ?? [];
        if (RequiresWorkResolution && subjectReferences is not null)
        {
            if (subjectReferences.Count == 0
                || subjectReferences.Any(reference => string.IsNullOrWhiteSpace(reference)
                    || reference.Length > LegalOperationCatalog.MaximumSubjectReferenceLength)
                || subjectReferences.Distinct(StringComparer.Ordinal).Count()
                    != subjectReferences.Count)
                throw new InvalidDataException("Subject references must be non-empty, unique and bounded.");
            arguments =
            [
                .. PlannerArguments.Where(argument =>
                    !LegalOperationCatalog.IsPlannerIdentityArgument(argument.Name)
                    && argument.Name != "article_number"),
                new LegalArgumentDefinition(
                    LegalOperationCatalog.SubjectReferenceArgument,
                    "Opaque server-resolved subject reference.",
                    MaximumLength: LegalOperationCatalog.MaximumSubjectReferenceLength,
                    AllowedValues: subjectReferences,
                    Mcp: false),
            ];
            required =
            [
                .. required.Where(name =>
                    !LegalOperationCatalog.IsPlannerIdentityArgument(name)),
                LegalOperationCatalog.SubjectReferenceArgument,
            ];
            choices = choices
                .Where(choice => resolvedArticleNumber is null
                    || !choice.Names.Contains("article_number", StringComparer.Ordinal))
                .Select(choice => new LegalRequiredChoice(
                    choice.Names.Where(name =>
                        !LegalOperationCatalog.IsPlannerIdentityArgument(name)
                        && name != "article_number").ToArray(), choice.Message))
                .Where(choice => choice.Names.Count > 0)
                .ToArray();
        }
        var schema = InputSchema(
            arguments.OrderBy(argument => argument.Name, StringComparer.Ordinal),
            required, planner: true, valueOverride);
        if (choices.Count == 1)
            schema["anyOf"] = RequiredAnyOf(choices[0].Names);
        else if (choices.Count > 1)
            schema["allOf"] = new JsonArray(choices.Select(choice => (JsonNode)new JsonObject
            {
                ["anyOf"] = RequiredAnyOf(choice.Names),
            }).ToArray());
        return schema;
    }

    private JsonObject InputSchema(
        IEnumerable<LegalArgumentDefinition> arguments,
        IReadOnlyList<string> required,
        bool planner,
        Func<string, IReadOnlyList<string>?>? valueOverride = null)
    {
        var properties = new JsonObject();
        foreach (var argument in arguments)
            properties[argument.Name] = argument.Schema(planner, valueOverride);
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (!planner || required.Count > 0)
            schema["required"] = new JsonArray(required.Select(value => (JsonNode)value).ToArray());
        if (!planner && ConditionalRequirements is { Count: > 0 } conditional)
            schema["allOf"] = new JsonArray(conditional.Select(requirement =>
                (JsonNode)new JsonObject
                {
                    ["if"] = new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            [requirement.WhenArgument] = new JsonObject
                            {
                                ["const"] = requirement.WhenValue,
                            },
                        },
                        ["required"] = new JsonArray(requirement.WhenArgument),
                    },
                    ["then"] = new JsonObject
                    {
                        ["required"] = new JsonArray(requirement.RequiredArgument),
                    },
                }).ToArray());
        return schema;
    }

    private static JsonArray RequiredAnyOf(IReadOnlyList<string> names) => new(names
        .Select(name => (JsonNode)new JsonObject { ["required"] = new JsonArray(name) })
        .ToArray());
}

/// <summary>
/// One canonical catalogue for MCP dispatch, input validation, assistant planning, and typed
/// result/effect policy. Adding a legal tool means adding exactly one definition here.
/// </summary>
public static class LegalOperationCatalog
{
    public const int MinimumStringLength = 1;
    public const int MaximumStringLength = 1_000;
    public const int MaximumWorkQueryLength = 900;
    public const int MaximumArticleNumberLength = 64;
    public const int MaximumAnchorLength = 512;
    public const int MaximumShortLength = 64;
    public const int MaximumLanguageLength = 16;
    public const int MaximumDateLength = 10;
    public const int MaximumVersionKeyLength = 128;
    public const int MaximumPublisherMetadataIdentifierLength = 2_048;
    public const int MaximumListValues = 50;
    public const int MaximumOffset = 100_000;
    public const int MaximumSubjectReferenceLength = 64;
    public const string SubjectReferenceArgument = "subject_ref";
    public const string IsoDateFormat = "yyyy-MM-dd";
    public const string IsoDatePattern =
        @"^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$";
    public const string AbsoluteHttpUriPattern = "^https?://";

    private static LegalArgumentDefinition S(
        string name,
        string description,
        int maximum = MaximumStringLength,
        bool mcp = true,
        bool planner = true,
        IReadOnlyList<string>? values = null,
        int? maximumListValues = null,
        int? maximumListItemLength = null,
        string? plannerGuidance = null,
        bool opaque = false,
        bool requiresAbsoluteHttpUri = false) => new(
            name, description, MaximumLength: maximum, AllowedValues: values,
            Mcp: mcp, Planner: planner, MaximumListValues: maximumListValues,
            MaximumListItemLength: maximumListItemLength,
            PlannerGuidance: plannerGuidance, Opaque: opaque,
            RequiresAbsoluteHttpUri: requiresAbsoluteHttpUri);

    private static LegalArgumentDefinition D(string name, string description) => new(
        name, description, MaximumLength: MaximumDateLength, IsDate: true);

    private static LegalArgumentDefinition I(
        string name, string description, int minimum, int maximum) => new(
        name, description, LegalArgumentKind.Integer, Minimum: minimum, Maximum: maximum);

    private static LegalArgumentDefinition P(
        string name, int maximum = MaximumStringLength) =>
        S(name, "", maximum, mcp: false);

    private static readonly string WorkDescription =
        "Work-level lex_id (publisher:workkey), version-level lex_id (version segment ignored), "
        + "or verbatim publisher identifier with publisher supplied. Unknown document -> call "
        + "search first.";

    public static IReadOnlyList<LegalOperationDefinition> Operations { get; } =
    [
        new(
            "search",
            0, 4,
            "Filtered legal search. keyword is deterministic FTS5/BM25; hybrid adds the pinned "
            + "local encoder and fixed RRF when verified vectors are mounted. No generative model "
            + "participates. The response query_plan separates resolved work constraints, article "
            + "and role intent, and residual provision terms. Returns hits WITHOUT body text; "
            + "full state via as_of.",
            LegalResultKind.Search, LegalEffectKind.Workspace, false,
            [
                S("query", "search terms"),
                S("publisher", "optional publisher id", MaximumShortLength),
                S("jurisdiction", "optional jurisdiction code from index metadata, e.g. LU or EU", MaximumShortLength),
                S("document_type", "backward-compatible document type filter"),
                S("source_class", "optional source document class"),
                S("hierarchy", "optional normalized legal hierarchy"),
                S("act_form", "optional legal act form"),
                S("binding_status", "optional binding status"),
                S("domain", "optional reviewed legal domain id"),
                S("publisher_metadata_identifier",
                    "optional exact official publisher-metadata URI returned by a search hit",
                    MaximumPublisherMetadataIdentifierLength, planner: false,
                    requiresAbsoluteHttpUri: true),
                S("language", "optional language code", MaximumLanguageLength),
                S("retrieval_mode", "keyword or hybrid; default keyword until activation",
                    MaximumShortLength, values: ["keyword", "hybrid"]),
                S("time_scope", "all_versions or as_of", MaximumShortLength,
                    values: ["all_versions", "as_of"],
                    plannerGuidance: "all_versions (default) or as_of; as_of requires as_of=YYYY-MM-DD"),
                D("as_of", "ISO date required when time_scope=as_of"),
                S("fuzzy", "auto or off; visible fallback only", MaximumShortLength,
                    values: ["auto", "off"]),
                S("works", "optional comma-separated work ids: restrict search to these works",
                    maximumListValues: MaximumListValues),
                I("limit", "default 10; minimum 1, maximum 50", 1, 50),
            ],
            ["query"], ["query"],
            ConditionalRequirements:
            [new("time_scope", "as_of", "as_of")]),
        new(
            "as_of",
            1, 0,
            "The state of one document as it stood on one date. Pure lookup, no ranking. "
            + "mode=outline lists provisions without text and may be narrowed with anchors; "
            + "mode=select returns only the named anchors' text; mode=full (default) returns the "
            + "whole text. Every provision carries its own permalink and hash.",
            LegalResultKind.ExactText, LegalEffectKind.Provision, true,
            [
                S("work", WorkDescription),
                P("work_query", MaximumWorkQueryLength),
                P("article_number", MaximumArticleNumberLength),
                D("date", "ISO date YYYY-MM-DD"),
                S("version_key",
                    "optional opaque key returned by timeline or an ambiguous_version choice",
                    MaximumVersionKeyLength, opaque: true),
                S("publisher", "publisher id; required when work is not publisher-qualified",
                    MaximumShortLength, planner: false),
                S("language", "optional language code, e.g. fr", MaximumLanguageLength),
                S("mode", "full | outline | select (default full)", MaximumShortLength,
                    values: ["full", "outline", "select"],
                    plannerGuidance: "full (default), outline or select; select requires anchors or article_number"),
                S("anchors", "optional comma-separated provision anchors for mode=outline; "
                    + "required for mode=select, e.g. art_1er,art_33", MaximumStringLength,
                    maximumListValues: MaximumListValues,
                    maximumListItemLength: MaximumAnchorLength),
            ],
            ["work", "date"], ["date"],
            [new(["work", "work_query"], "Operation 'as_of' requires a work identity.")],
            ["date"],
            [new("mode", "select", "anchors", ["article_number"])],
            [new("anchors", "mode", ["outline", "select"])],
            PublisherAuthorityArgument: "work",
            SupportsAnchorResolution: true),
        new(
            "timeline",
            3, 1,
            "Every publisher state a document has been in: timeline intervals, version keys, and "
            + "explicit timeline_semantics. Legilux intervals describe applicability; EUR-Lex "
            + "intervals describe official consolidated wording states, not entry into force.",
            LegalResultKind.Timeline, LegalEffectKind.Timeline, true,
            [
                S("work", WorkDescription), P("work_query", MaximumWorkQueryLength),
                S("publisher", "publisher id; required when work is not publisher-qualified",
                    MaximumShortLength, planner: false),
                I("limit", "max versions (default 100)", 1, 200),
                I("offset", "pagination offset", 0, MaximumOffset),
            ],
            ["work"], [],
            [new(["work", "work_query"], "Operation 'timeline' requires a work identity.")],
            PublisherAuthorityArgument: "work"),
        new(
            "in_force_on",
            6, 2,
            "Compatibility name for publisher states covering a date, computed from timeline "
            + "intervals and deduplicated by work. Legilux states describe applicability; EUR-Lex "
            + "states are official consolidated wording states and must not be called entry into "
            + "force. Every envelope carries timeline_semantics and the result carries a "
            + "mandatory population disclosure.",
            LegalResultKind.Inventory, LegalEffectKind.InForce, false,
            AggregateArguments(includeOrder: false, includeWindow: false, includeDate: true,
                defaultLimit: 50),
            ["date"], ["date"], DefaultedPlannerDates: ["date"],
            AdditionalEffects: [LegalEffectKind.Workspace]),
        new(
            "diff",
            2, 3,
            "What changed between two dates for one work: which publisher versions cover the "
            + "selected dates and, where both texts are held, retrieve them via as_of to compare. "
            + "An optional held article anchor scopes the typed comparison workspace. Read "
            + "timeline_semantics before describing legal applicability.",
            LegalResultKind.Comparison, LegalEffectKind.Diff, true,
            [
                S("work", WorkDescription), P("work_query", MaximumWorkQueryLength),
                P("article_number", MaximumArticleNumberLength),
                D("from_date", "ISO date"), D("to_date", "ISO date"),
                S("from_version_key",
                    "optional opaque key returned by timeline or an ambiguous_version choice",
                    MaximumVersionKeyLength, opaque: true),
                S("to_version_key",
                    "optional opaque key returned by timeline or an ambiguous_version choice",
                    MaximumVersionKeyLength, opaque: true),
                S("publisher", "publisher id; required when work is not publisher-qualified",
                    MaximumShortLength, planner: false),
                S("language", "language code", MaximumLanguageLength),
                S("anchor", "optional held provision anchor returned by search, e.g. art_92",
                    MaximumAnchorLength),
            ],
            ["work", "from_date", "to_date"], ["from_date", "to_date"],
            [new(["work", "work_query"], "Operation 'diff' requires a work identity.")],
            DateRange: new("from_date", "to_date"),
            PublisherAuthorityArgument: "work",
            SupportsAnchorResolution: true),
        new(
            "article_history",
            4, 5,
            "Every distinct text ONE provision (article/annex) has had on its publisher timeline, "
            + "plus lifecycle events (inserted/removed/renumbered, renumbering detected "
            + "mechanically by identical text hash). Read timeline_semantics before calling an "
            + "interval legal applicability. Answers \"what did Article X say over its life / when "
            + "did it change\".",
            LegalResultKind.Timeline, LegalEffectKind.History, true,
            [
                S("work", WorkDescription), P("work_query", MaximumWorkQueryLength),
                P("article_number", MaximumArticleNumberLength),
                S("publisher", "publisher id; required when work is not publisher-qualified",
                    MaximumShortLength, planner: false),
                S("anchor", "provision anchor, e.g. art_1er (find it via search or as_of "
                    + "mode=outline)", MaximumAnchorLength),
                S("language", "optional language code; defaults to the work's primary derived "
                    + "language", MaximumLanguageLength),
                D("from_date", "optional ISO date: keep only the states in force at or after it"),
                D("to_date", "optional ISO date: keep only the states that began on or before it"),
            ],
            ["work", "anchor"], [],
            [
                new(["work", "work_query"],
                    "Operation 'article_history' requires a work identity."),
                new(["anchor", "article_number"],
                    "article_history requires an anchor or article_number."),
            ],
            DateRange: new("from_date", "to_date"),
            PublisherAuthorityArgument: "work",
            SupportsAnchorResolution: true),
        new(
            "provenance",
            9, 6,
            "Proof chain for one lex_id: source URI, retrieval time, record/body hashes, event "
            + "chain, corpus commit, index build, stamp signature.",
            LegalResultKind.Verification, LegalEffectKind.Verification, true,
            [
                S("lex_id", "full lex_id"), P("work_query", MaximumWorkQueryLength),
                S("language", "optional", MaximumLanguageLength),
            ],
            ["lex_id"], [],
            [new(["work_query", "lex_id"],
                "Operation 'provenance' requires a work identity.")],
            PublisherAuthorityArgument: "lex_id"),
        new(
            "coverage",
            7, 7,
            "What we hold and what we lack, tier by tier: counts, date ranges, history_begins, "
            + "known gaps. This tool exists to say what we do NOT have.",
            LegalResultKind.Inventory, LegalEffectKind.Coverage, false,
            [S("publisher", "optional publisher id", MaximumShortLength)], []),
        new(
            "cited_by",
            8, 8,
            "Which held provision versions contain captured publisher cross-references to this "
            + "work. This reverse lookup reports the publisher references Lex captured; it does "
            + "not classify their relationship type or assess current legal effect.",
            LegalResultKind.Inventory, LegalEffectKind.CitedBy, true,
            [
                S("work", "the law being cited, e.g. lu-legilux:loi-2020-06-04-a476"),
                P("work_query", MaximumWorkQueryLength),
                I("limit", "default 50", 1, 100),
            ],
            ["work"], [],
            [new(["work", "work_query"], "Operation 'cited_by' requires a work identity.")],
            PublisherAuthorityArgument: "work"),
        new(
            "changes_in_period",
            5, 9,
            "ACROSS the corpus: which works gained new versions between two dates, how many each, "
            + "and when — the aggregate counterpart of diff/timeline (which cover ONE work). Use "
            + "for \"what changed between 2025 and 2026\", \"which laws changed most during the "
            + "pandemic\", \"what moved last month\". order=by_churn ranks by number of new "
            + "versions; by_date (default) lists most recently changed first.",
            LegalResultKind.Ranking, LegalEffectKind.Ranking, false,
            AggregateArguments(includeOrder: true, includeWindow: true, includeDate: false,
                defaultLimit: 20),
            ["from_date", "to_date"], ["from_date", "to_date"],
            DateRange: new("from_date", "to_date"),
            AdditionalEffects: [LegalEffectKind.Workspace]),
    ];

    private static readonly IReadOnlyList<LegalOperationDefinition> PlannerOperations =
        OrderedOperations(operation => operation.PlannerOrdinal, "planner");

    private static readonly IReadOnlyList<LegalOperationDefinition> McpOperations =
        OrderedOperations(operation => operation.McpOrdinal, "MCP");

    public static IReadOnlyList<string> ToolNames { get; } =
        PlannerOperations.Select(operation => operation.Name).ToArray();

    public static LegalOperationDefinition Get(string name) =>
        TryGet(name) ?? throw new InvalidDataException($"Unknown legal operation '{name}'.");

    public static LegalOperationDefinition? TryGet(string name) =>
        Operations.SingleOrDefault(operation => operation.Name == name);

    public static bool IsPlannerIdentityArgument(string name) =>
        name is "work" or "work_query" or "lex_id";

    public static JsonArray McpToolDefinitions() => new(McpOperations
        .Select(operation => (JsonNode)operation.McpToolDefinition()).ToArray());

    private static IReadOnlyList<LegalOperationDefinition> OrderedOperations(
        Func<LegalOperationDefinition, int> ordinal,
        string projection)
    {
        if (Operations.Select(operation => operation.Name)
                .Distinct(StringComparer.Ordinal).Count() != Operations.Count)
            throw new InvalidDataException("Legal operation names must be unique.");
        var ordered = Operations.OrderBy(ordinal).ToArray();
        if (!ordered.Select(ordinal).SequenceEqual(Enumerable.Range(0, Operations.Count)))
            throw new InvalidDataException(
                $"Legal operation {projection} ordinals must be unique and contiguous.");
        return ordered;
    }

    private static IReadOnlyList<LegalArgumentDefinition> AggregateArguments(
        bool includeOrder,
        bool includeWindow,
        bool includeDate,
        int defaultLimit)
    {
        var arguments = new List<LegalArgumentDefinition>();
        if (includeDate) arguments.Add(D("date", "ISO date"));
        if (includeWindow)
        {
            arguments.Add(D("from_date", "ISO date, start of window (inclusive)"));
            arguments.Add(D("to_date", "ISO date, end of window (inclusive)"));
        }
        arguments.AddRange([
            S("publisher", includeDate ? "optional publisher id, e.g. lu-legilux"
                : "optional publisher id", MaximumShortLength),
            S("jurisdiction", "optional jurisdiction code from index metadata, e.g. LU or EU",
                MaximumShortLength),
            S("document_type", includeDate
                ? "backward-compatible source document class filter"
                : "optional source document class(es), comma-separated; prefix with ! to "
                    + "exclude, e.g. !RECUEIL,!CODE_RECUEIL for instruments only"),
            S("source_class", includeDate
                ? "optional source document class"
                : "optional source document class; alias of document_type"),
            S("hierarchy", "optional normalized legal hierarchy"),
            S("act_form", "optional legal act form"),
            S("binding_status", "optional binding status"),
            S("domain", "optional reviewed legal domain"),
            S("language", "optional language code", MaximumLanguageLength),
        ]);
        if (includeOrder)
            arguments.Add(S("order", "by_date (default) or by_churn", MaximumShortLength,
                values: ["by_date", "by_churn"]));
        arguments.Add(I("limit", $"default {defaultLimit}", 1, 100));
        arguments.Add(I("offset", includeOrder ? "skip this many, for paging" : "pagination offset",
            0, MaximumOffset));
        return arguments;
    }
}

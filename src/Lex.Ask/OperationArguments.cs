using System.Globalization;
using System.Text.Json.Nodes;
using Lex.Mcp;

namespace Lex.Ask;

/// <summary>
/// Converts untrusted planner arguments into the exact bounded arguments frozen in an
/// <see cref="OperationPlan"/>. MCP performs its own validation too; this earlier boundary keeps
/// invalid model output from becoming an attempted legal operation.
///
/// <para>Two kinds of planner mistake reach here and they are not the same mistake. One leaves the
/// answer undetermined: an absent date, a stray corpus filter, a tuning value the model invented.
/// The other selects a different law, a different provision or a different instant: a date that is
/// present but does not parse, a work identity too long to be the one meant, a reversed comparison
/// window. The first kind is <em>repaired</em> and the repair is recorded; the second kind is
/// refused, with the message it has always been refused with. The dividing line is whether a
/// substitute exists that cannot change which law, which provision or which point in time is
/// answered. Where no such substitute exists, refusing is the only honest option.</para>
///
/// <para>Repairs are returned rather than logged here: this is a static with no logger, and a
/// silent repair nobody counts becomes silent drift. <c>AskService</c> emits one
/// <c>planner_argument_repaired</c> diagnostic per repair and puts them in the plan trace.</para>
/// </summary>
internal static class OperationArguments
{
    private const int MaximumStringLength = LegalOperationCatalog.MaximumStringLength;

    /// <summary>The shortest trimmed value this boundary accepts for any string argument; the
    /// planner schema emits it as <c>minLength</c>.</summary>
    public const int MinimumStringLength = 1;

    /// <summary>The clarification option bounds, emitted as <c>minItems</c>, <c>maxItems</c> and
    /// the item <c>maxLength</c>.</summary>
    public const int MinimumOptionCount = 2;

    public const int MaximumOptionCount = 4;

    public const int MaximumOptionLength = 100;

    /// <summary>The one date format this boundary parses, and the JSON Schema assertion that
    /// matches it structurally. <c>"format": "date"</c> is an annotation most planners ignore, so
    /// the pattern carries the constraint: four-digit year, zero-padded month 01-12, zero-padded
    /// day 01-31, ASCII hyphens, nothing else. Calendar validity (2026-02-30, 2026-02-29 in a
    /// non-leap year) is not expressible in a regex and stays with <see cref="Date"/>.</summary>
    public const string IsoDateFormat = LegalOperationCatalog.IsoDateFormat;

    public const string IsoDatePattern = LegalOperationCatalog.IsoDatePattern;

    private static readonly IReadOnlyDictionary<string, HashSet<string>> ApplicationAllowed =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {

            ["legal_boundary"] = Set("reason"),
            ["clarification"] = Set("question", "options"),
            ["gap"] = Set("reason"),
        };

    /// <summary>
    /// Application-only arguments. Legal arguments, bounds, closed values, requirements and
    /// couplings live in <see cref="LegalOperationCatalog"/> and are projected from there.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ApplicationRequired =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["clarification"] = ["question", "options"],
        };

    /// <summary>The point-in-time arguments <see cref="ApplyDefaults"/> completes to today (UTC)
    /// when the planner omits them. This is the whole of the date recovery and it is deliberately
    /// the *single* date of an operation, never a comparison bound: "the law now" is the only
    /// defensible reading of a dateless request about a law, whereas today is not a window and
    /// inventing one answers a question nobody asked (which is why diff and changes_in_period are
    /// absent here and keep demanding from_date and to_date).
    ///
    /// The planner prompt already promises this substitution verbatim, the schema still asks for
    /// the date, and the effective instant is rendered back to the reader on every affected
    /// surface, so a defaulted date is visible rather than assumed. MCP answers a date no version
    /// covers with no_version_for_date, a refusal, so a repealed or not-yet-in-force work degrades
    /// to a visible gap rather than to the wrong text.
    ///
    /// search.as_of is absent because its default is conditional on time_scope=as_of; it is
    /// applied in <see cref="ApplyDefaults"/> instead.</summary>

    /// <summary>The stray argument names that are refused rather than dropped. Each of them names
    /// the instant, the law or the provision, so removing it does not leave the request
    /// under-specified, it leaves it answering something else: drop a date and the default above
    /// substitutes today for an instant the model actually supplied; drop work/work_query/lex_id
    /// and a different law resolves; drop article_number/anchor/anchors and a different provision
    /// comes back; drop works and a scan the user restricted to named laws goes corpus-wide.
    ///
    /// The standing invariant behind this set: <b>a value may never be dropped if a default would
    /// then fill the same slot.</b> Any widening of the droppable set has to re-check it.</summary>
    private static readonly HashSet<string> NeverDropped = Set(
        "date", "as_of", "from_date", "to_date", "work", "work_query", "lex_id",
        "article_number", "anchor", "anchors", "works");

    /// <summary>The closed-set arguments whose unmatched value is dropped so the default refills
    /// it, rather than aborting the plan. Every one of them governs *how* the corpus is searched
    /// or *how much* of a document is rendered, never which law or which instant: retrieval_mode
    /// and fuzzy pick a matching strategy, mode picks how much of one document to render, and
    /// time_scope drops to the all_versions default, which widens the version set and so can add
    /// a hit but never hide one.
    ///
    /// order is deliberately not here. It interacts with limit: dropping an invalid order yields
    /// by_date, so a top-20 by recency is handed to a reader who asked which laws changed most,
    /// with the rows they asked for silently outside the window. That is a plausible-looking
    /// answer to a different question, which is the one outcome this boundary exists to prevent.
    /// The prompt names order=by_churn explicitly, so a bad value here means the model misread the
    /// request rather than omitted a detail.</summary>
    private static readonly HashSet<string> RecoverableValues =
        Set("retrieval_mode", "time_scope", "fuzzy", "mode");

    /// <summary>Every argument name any operation declares. Read only by <see cref="Repair"/>, to
    /// keep a planner-invented key name off the diagnostic line it would otherwise be copied
    /// into.</summary>
    private static readonly HashSet<string> KnownArguments =
        LegalOperationCatalog.Operations.SelectMany(operation => operation.PlannerArguments)
            .Select(argument => argument.Name)
            .Concat(ApplicationAllowed.Values.SelectMany(names => names))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Every action this boundary accepts; the planner schema is generated from it.</summary>
    public static IEnumerable<string> Actions =>
        LegalOperationCatalog.ToolNames.Concat(ApplicationAllowed.Keys);

    /// <summary>One "at least one of <paramref name="Names"/>" gate and the refusal it throws.</summary>
    public sealed record RequiredChoice(IReadOnlyList<string> Names, string Message);

    /// <summary>True when the argument is a JSON integer rather than a string.</summary>
    public static bool IsInteger(string name) =>
        LegalOperationCatalog.Operations.SelectMany(operation => operation.PlannerArguments)
            .Any(argument => argument.Name == name
                && argument.Kind == LegalArgumentKind.Integer);

    /// <summary>True when the argument is parsed with <see cref="IsoDateFormat"/>.</summary>
    public static bool IsDate(string name) =>
        LegalOperationCatalog.Operations.SelectMany(operation => operation.PlannerArguments)
            .Any(argument => argument.Name == name && argument.IsDate);

    /// <summary>The longest trimmed value <see cref="Normalize"/> accepts for one string
    /// argument; the planner schema emits it as <c>maxLength</c>. Read by both, so the two
    /// cannot drift.</summary>
    public static int MaximumLengthFor(string name)
    {
        var bounds = LegalOperationCatalog.Operations
            .SelectMany(operation => operation.PlannerArguments)
            .Where(argument => argument.Name == name
                && argument.Kind == LegalArgumentKind.String)
            .Select(argument => argument.MaximumLength)
            .Distinct()
            .ToArray();
        return bounds.Length switch
        {
            0 => MaximumStringLength,
            1 => bounds[0],
            _ => throw new InvalidDataException(
                $"Planner argument '{name}' has inconsistent catalogue bounds."),
        };
    }

    /// <summary>The inclusive range <see cref="Normalize"/> accepts for one integer argument of
    /// one action; the planner schema emits it as <c>minimum</c> and <c>maximum</c>.</summary>
    public static (int Minimum, int Maximum) IntegerBoundsFor(string action, string name)
    {
        var argument = ArgumentFor(action, name);
        if (argument?.Kind != LegalArgumentKind.Integer
            || argument.Minimum is null || argument.Maximum is null)
            throw new InvalidDataException($"Argument '{name}' is not an integer for '{action}'.");
        return (argument.Minimum.Value, argument.Maximum.Value);
    }

    /// <summary>The arguments the planner schema emits as <c>required</c> for one action. Two
    /// kinds sit here: the ones <see cref="Validate"/> refuses a plan without, and the point-in-
    /// time dates <see cref="ApplyDefaults"/> completes. Both are asked for, because a date the
    /// model supplies is always better evidence of intent than a date this boundary substitutes;
    /// only the first kind aborts the plan when it is missing. <see cref="DefaultedDatesFor"/> is
    /// the split, and the fitness test asserts the schema declares exactly their union.</summary>
    public static IReadOnlyList<string> RequiredFor(string action) =>
        LegalOperationCatalog.TryGet(action)?.PlannerRequired
        ?? (ApplicationRequired.TryGetValue(action, out var required) ? required : []);

    /// <summary>The subset of one action's arguments that an omitted value is completed to today
    /// (UTC) for, rather than refused.</summary>
    public static IReadOnlyList<string> DefaultedDatesFor(string action) =>
        LegalOperationCatalog.TryGet(action)?.DefaultedPlannerDates ?? [];

    /// <summary>The "at least one of" gates for one action, narrowed to the names that action
    /// actually accepts: the work identity is work or work_query everywhere except provenance,
    /// which takes lex_id or work_query and has no work argument at all.</summary>
    public static IReadOnlyList<RequiredChoice> RequiredChoicesFor(string action)
    {
        _ = AllowedSet(action);
        return LegalOperationCatalog.TryGet(action)?.PlannerRequiredChoices?
            .Select(choice => new RequiredChoice(choice.Names, choice.Message)).ToArray() ?? [];
    }

    /// <summary>The exact values <see cref="Normalize"/> accepts for one argument, or null when
    /// the argument is not value-closed at this boundary.</summary>
    public static IReadOnlyList<string>? AllowedValuesFor(string name)
    {
        var closed = LegalOperationCatalog.Operations
            .SelectMany(operation => operation.PlannerArguments)
            .Where(argument => argument.Name == name && argument.AllowedValues is not null)
            .Select(argument => argument.AllowedValues!)
            .ToArray();
        if (closed.Length == 0) return null;
        if (closed.Skip(1).Any(values => !values.SequenceEqual(closed[0], StringComparer.Ordinal)))
            throw new InvalidDataException(
                $"Planner argument '{name}' has inconsistent catalogue values.");
        return closed[0];
    }

    /// <summary>Guidance the planner schema attaches to a value-closed argument, or null.</summary>
    public static string? GuidanceFor(string name) =>
        LegalOperationCatalog.Operations.SelectMany(operation => operation.PlannerArguments)
            .Where(argument => argument.Name == name)
            .Select(argument => argument.PlannerGuidance)
            .FirstOrDefault(guidance => guidance is not null);

    /// <summary>The exact argument names <see cref="Normalize"/> accepts for one action.</summary>
    public static IReadOnlyCollection<string> AllowedFor(string action) =>
        AllowedSet(action).ToArray();

    public static JsonObject Normalize(
        string action,
        JsonObject proposed,
        CorpusVocabulary? vocabulary = null,
        DateOnly? today = null) =>
        Normalize(action, proposed, out _, vocabulary, today);

    /// <summary>
    /// The gate, in the order the stages have to run in.
    /// <list type="number">
    /// <item>unknown action: refuse, there is no operation to recover to.</item>
    /// <item>argument names: alias, then refuse a stray that names the instant, the law or the
    /// provision, then drop the rest. One stray key used to abort all eight operations in the
    /// plan.</item>
    /// <item>per value: absent-shaped values become absent, a wrong type or an over-length string
    /// is refused, an unusable integer drops to its default.</item>
    /// <item>closed sets, <em>before</em> the defaults: a value that has to be dropped must be
    /// gone before <c>??=</c> can refill it.</item>
    /// <item>defaults, including the point in time.</item>
    /// <item>validate: every surviving check is still a hard failure.</item>
    /// </list>
    /// <paramref name="today"/> is injected rather than read from the clock inside, because the
    /// planner prompt states the same date to the model and the two must agree, and because a
    /// gate whose output depends on the wall clock cannot be tested.
    /// </summary>
    public static JsonObject Normalize(
        string action,
        JsonObject proposed,
        out IReadOnlyList<string> repairs,
        CorpusVocabulary? vocabulary = null,
        DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        vocabulary ??= CorpusVocabulary.Unconstrained;
        var repaired = new List<string>();
        repairs = repaired;
        var allowed = AllowedSet(action);

        var accepted = PartitionNames(action, proposed, allowed, repaired);
        var normalized = new JsonObject();
        foreach (var (name, value) in accepted)
        {
            if (value is null) continue;
            if (name == "options")
            {
                if (value is not JsonArray options
                    || options.Count < MinimumOptionCount || options.Count > MaximumOptionCount)
                    throw new InvalidDataException("Clarification options must contain two to four labels.");
                var bounded = new JsonArray();
                foreach (var item in options)
                {
                    if (item is not JsonValue option || !option.TryGetValue<string>(out var label))
                        throw new InvalidDataException(
                            $"Every clarification option must be a string. Received {Kind(item)}.");
                    label = RequiredString(label, "clarification option", MaximumOptionLength);
                    bounded.Add(label);
                }
                normalized[name] = bounded;
                continue;
            }
            if (IsInteger(name))
            {
                // Pagination carries no legal meaning and truncation is visible to the reader,
                // because the in-force and ranking views report the full count beside the rows
                // they show. So an unusable limit or offset drops to the action's canonical value
                // instead of aborting the plan: a numeric string is coerced, and anything else
                // ("all", a float, an object, 5000 where the action caps at 50) is dropped.
                var (minimum, maximum) = IntegerBoundsFor(action, name);
                if (!TryInteger(value, out var integer, out var coerced)
                    || integer < minimum || integer > maximum)
                {
                    repaired.Add(Repair(action, name, "dropped"));
                    continue;
                }
                // A quoted number is kept rather than dropped, but it is still the planner writing
                // the wrong JSON shape, so it is counted under its own verb: a rising coerced rate
                // is a prompt or schema problem, a rising dropped rate is a different one.
                if (coerced) repaired.Add(Repair(action, name, "coerced"));
                normalized[name] = integer;
                continue;
            }
            if (value is not JsonValue textValue || !textValue.TryGetValue<string>(out var text))
                throw new InvalidDataException(
                    $"Argument '{name}' must be a string. Received {Kind(value)}.");
            text = text.Trim();
            // An empty or whitespace-only value carries no intent, so erasing the key preserves
            // every bit of information the model actually sent; the defaults and the gates below
            // then treat it as the absence it is. A malformed but non-empty value is the other
            // case entirely and stays a refusal. JSON null already took this path above.
            if (text.Length == 0
                || (ArgumentFor(action, name)?.MaximumListValues is not null
                    && Items(text).Length == 0))
            {
                repaired.Add(Repair(action, name, "dropped"));
                continue;
            }
            // The trim happens before the measurement, which JSON Schema cannot express, so
            // maxLength there is marginally stricter than this gate. Stricter is safe; looser is
            // the outage. See MaximumLengthFor, which the planner schema reads. Over-length stays
            // fatal: truncating a work_query or an article_number looks up a different law.
            var longest = MaximumLengthFor(name);
            if (text.Length > longest)
                throw new InvalidDataException(
                    $"Argument '{name}' must contain {MinimumStringLength} to {longest} characters.");
            // "LU-Legilux" and "lu" name mounted things; the selectors behind MCP match publisher
            // ordinally, so the mounted spelling is restored before the plan is frozen. A value
            // nothing mounted matches is left alone on purpose (see CorpusVocabulary.Canonical).
            normalized[name] = vocabulary.Canonical(name, text) ?? text;
        }

        RecoverClosedSets(action, normalized, repaired);
        ApplyDefaults(action, normalized, today ?? DateOnly.FromDateTime(DateTime.UtcNow), repaired);
        Validate(action, normalized);
        return normalized;
    }

    /// <summary>Splits the proposed keys into the ones this action accepts and the strays, in the
    /// one order that keeps the two recovery rules from composing into a wrong answer: alias
    /// first, then refuse a never-dropped name, then drop.</summary>
    private static List<KeyValuePair<string, JsonNode?>> PartitionNames(
        string action, JsonObject proposed, HashSet<string> allowed, List<string> repaired)
    {
        var accepted = new List<KeyValuePair<string, JsonNode?>>();
        foreach (var (name, value) in proposed)
        {
            if (allowed.Contains(name))
            {
                accepted.Add(new KeyValuePair<string, JsonNode?>(name, value));
                continue;
            }
            if (AliasFor(action, name) is { } target)
            {
                // The one alias, and the reason it exists rather than a drop: on as_of the model
                // sometimes writes search's spelling of the same concept. Dropping that key would
                // hand the slot to the date default, and Lex would answer a 2019 question with
                // today's law, labelled today. Renaming keeps the instant the model supplied.
                string? carried = null;
                if (value is JsonValue aliased && aliased.TryGetValue<string>(out var aliasText))
                    carried = aliasText.Trim();
                else if (value is not null)
                    // A present value that is not a string is not absence. Dropping it would hand
                    // the slot to the date default and answer a 2019 question with today's law,
                    // which is the one outcome this alias exists to prevent, so it is refused
                    // exactly as the same shape is refused under the argument's own name.
                    throw new InvalidDataException(
                        $"Argument '{name}' must be a string. Received {Kind(value)}.");
                if (carried is not { Length: > 0 })
                {
                    repaired.Add(Repair(action, name, "dropped"));
                    continue;
                }
                // A blank target is absence, exactly as it is everywhere else in this gate, so the
                // alias still wins and the instant the model supplied is not lost to the default.
                var supplied = Text(proposed, target)?.Trim();
                if (supplied is null or "")
                {
                    accepted.Add(new KeyValuePair<string, JsonNode?>(target, JsonValue.Create(carried)));
                    repaired.Add(Repair(action, name, "aliased"));
                    continue;
                }
                if (supplied == carried)
                {
                    repaired.Add(Repair(action, name, "aliased"));
                    continue;
                }
                // Two different instants, no way to tell which the user meant.
                throw new InvalidDataException(
                    $"Operation '{action}' contains unsupported argument '{name}'.");
            }
            if (NeverDropped.Contains(name))
                throw new InvalidDataException(
                    $"Operation '{action}' contains unsupported argument '{name}'.");
            // Everything else is a corpus filter, a language, a tuning knob, a page bound or a
            // free-text field this action has no use for. The archetype is as_of carrying
            // publisher: as_of fetches exactly one work, identity is carried by work/work_query,
            // and the plan that results is byte-identical to the same plan written without it.
            // Where a filter was load-bearing for disambiguation, work resolution degrades to a
            // clarification with choices, so the user is asked rather than silently answered.
            repaired.Add(Repair(action, name, "dropped"));
        }
        return accepted;
    }

    /// <summary>The one stray-name alias, kept closed on purpose.</summary>
    private static string? AliasFor(string action, string name) =>
        action == "as_of" && name == "as_of" ? "date" : null;

    private static void RecoverClosedSets(
        string action, JsonObject arguments, List<string> repaired)
    {
        foreach (var argument in LegalOperationCatalog.TryGet(action)?.PlannerArguments ?? [])
        {
            if (argument.AllowedValues is not { } values) continue;
            var name = argument.Name;
            if (Text(arguments, name) is not { } value) continue;
            if (values.Contains(value, StringComparer.Ordinal)) continue;
            if (!RecoverableValues.Contains(name))
                throw new InvalidDataException($"Argument '{name}' has an unsupported value.");
            arguments.Remove(name);
            repaired.Add(Repair(action, name, "dropped"));
        }
    }

    private static void ApplyDefaults(
        string action, JsonObject arguments, DateOnly today, List<string> repaired)
    {
        void DefaultDate(string name)
        {
            if (arguments[name] is not null) return;
            arguments[name] = today.ToString(IsoDateFormat, CultureInfo.InvariantCulture);
            repaired.Add(Repair(action, name, "defaulted"));
        }
        foreach (var name in DefaultedDatesFor(action)) DefaultDate(name);
        switch (action)
        {
            case "search":
                arguments["retrieval_mode"] ??= "keyword";
                arguments["fuzzy"] ??= "auto";
                arguments["limit"] ??= 10;
                // Conditional, so it is not in DefaultedDates: only time_scope=as_of gives the
                // argument a meaning, and forcing a date onto an all_versions search would narrow
                // a search the model deliberately left open.
                if (Text(arguments, "time_scope") == "as_of") DefaultDate("as_of");
                break;
            case "as_of":
                arguments["mode"] ??=
                    arguments["anchors"] is null && arguments["article_number"] is null
                        ? "full"
                        : "select";
                // A select the model never narrowed selects nothing. Full text is a strict
                // superset of the selection it failed to specify, so widening can only show more
                // law, never hide any; refusing here showed none of it.
                if (Text(arguments, "mode") == "select"
                    && arguments["anchors"] is null
                    && arguments["article_number"] is null)
                {
                    arguments["mode"] = "full";
                    repaired.Add(Repair(action, "mode", "widened"));
                }
                break;
            case "timeline":
                arguments["limit"] ??= 100;
                arguments["offset"] ??= 0;
                break;
            case "in_force_on":
                arguments["limit"] ??= 50;
                arguments["offset"] ??= 0;
                break;
            case "cited_by":
                arguments["limit"] ??= 50;
                break;
            case "changes_in_period":
                arguments["source_class"] ??= "!RECUEIL,!CODE_RECUEIL";
                arguments["order"] ??= "by_date";
                arguments["limit"] ??= 20;
                arguments["offset"] ??= 0;
                break;
        }
    }

    private static void Validate(string action, JsonObject arguments)
    {
        foreach (var choice in RequiredChoicesFor(action))
            if (!choice.Names.Any(name => Text(arguments, name) is not null))
                throw new InvalidDataException(choice.Message);
        foreach (var name in RequiredFor(action))
        {
            if (IsDate(name)) Date(arguments, name);
            else if (name == "options")
            {
                if (arguments[name] is not JsonArray)
                    throw new InvalidDataException($"{action} requires bounded options.");
            }
            else Require(arguments, name);
        }
        // Absence and malformation used to be the same call, which is why thirteen omitted dates
        // were reported as "must be an ISO date". Absence is now settled above, by the default or
        // by the required check; what is left is a value that is present and does not parse.
        // "2024" could mean 2024-01-01 or 2024-12-31 and either choice silently selects a
        // different version of the law, so every one of them is refused, on every date argument
        // the action accepts rather than only the ones it requires.
        foreach (var name in AllowedSet(action))
            if (IsDate(name) && arguments[name] is not null) Date(arguments, name);
        if (LegalOperationCatalog.TryGet(action)?.DateRange is { } range
            && arguments[range.FromArgument] is not null
            && arguments[range.ToArgument] is not null)
        {
            var from = Date(arguments, range.FromArgument);
            var to = Date(arguments, range.ToArgument);
            // Not swapped: swapping reverses which version is the before and which is the after,
            // inverting every added and removed clause in the diff.
            if (from > to)
                throw new InvalidDataException(
                    $"{action} {range.FromArgument} must not follow {range.ToArgument}.");
        }

        // Defence in depth. Everything below has already been recovered or refused above; these
        // are the assertions that the arguments handed to MCP still satisfy every bound they
        // satisfied before this gate learned to repair anything.
        var operation = LegalOperationCatalog.TryGet(action);
        foreach (var argument in operation?.PlannerArguments ?? [])
            if (argument.AllowedValues is { } values)
                Enum(arguments, argument.Name, values.ToArray());
        foreach (var coupling in operation?.ConditionalRequirements ?? [])
        {
            if (Text(arguments, coupling.WhenArgument) != coupling.WhenValue) continue;
            if (Text(arguments, coupling.RequiredArgument) is not null) continue;
            if (coupling.PlannerAlternativeArguments?.Any(name =>
                    Text(arguments, name) is not null) == true) continue;
            throw new InvalidDataException(
                $"{action} {coupling.WhenArgument}={coupling.WhenValue} requires "
                + $"{coupling.RequiredArgument}.");
        }
        foreach (var coupling in operation?.AllowedValuesCouplings ?? [])
        {
            if (Text(arguments, coupling.Argument) is null) continue;
            var controlling = Text(arguments, coupling.WhenArgument);
            if (controlling is not null
                && coupling.AllowedValues.Contains(controlling, StringComparer.Ordinal)) continue;
            throw new InvalidDataException(
                $"{action} {coupling.Argument} requires {coupling.WhenArgument}="
                + $"{string.Join(" or ", coupling.AllowedValues)}.");
        }

        foreach (var argument in operation?.PlannerArguments ?? [])
            if (argument.MaximumListValues is { } maximum)
                CountList(arguments, argument.Name, maximum,
                    argument.MaximumListItemLength ?? MaximumStringLength);

        foreach (var argument in operation?.PlannerArguments
                     .Where(argument => argument.Kind == LegalArgumentKind.Integer) ?? [])
        {
            var (minimum, maximum) = IntegerBoundsFor(action, argument.Name);
            Bound(arguments, argument.Name, minimum, maximum);
        }
    }

    private static HashSet<string> AllowedSet(string action)
    {
        if (LegalOperationCatalog.TryGet(action) is { } operation)
            return operation.PlannerArguments.Select(argument => argument.Name)
                .ToHashSet(StringComparer.Ordinal);
        if (ApplicationAllowed.TryGetValue(action, out var allowed)) return allowed;
        throw new InvalidDataException(
            $"Unknown legal operation or application action '{action}'.");
    }

    private static LegalArgumentDefinition? ArgumentFor(string action, string name) =>
        LegalOperationCatalog.TryGet(action)?.PlannerArguments
            .SingleOrDefault(argument => argument.Name == name);

    /// <summary>One repair line: the operation, the argument, and what was done to it. Argument
    /// names and verbs only, never a value, because these are logged.
    ///
    /// A dropped stray is the one repair whose name the planner chose rather than this gate, and
    /// a model can be talked into naming a key after the question it was asked. So a name no
    /// operation declares is reported as <c>unrecognized</c>: the count is what this line exists
    /// for, and the alternative is user text on stderr.</summary>
    private static string Repair(string action, string name, string outcome) =>
        $"{action}.{(KnownArguments.Contains(name) ? name : "unrecognized")} {outcome}";

    /// <summary>A JSON integer, or a numeric string the model quoted by mistake.
    /// <paramref name="coerced"/> distinguishes the two, because the quoted spelling is a planner
    /// mistake this gate repairs silently otherwise, and an uncounted repair is the drift the
    /// repair lines exist to make visible.</summary>
    private static bool TryInteger(JsonNode value, out int integer, out bool coerced)
    {
        integer = 0;
        coerced = false;
        if (value is not JsonValue number) return false;
        if (number.TryGetValue<int>(out integer)) return true;
        coerced = number.TryGetValue<string>(out var text)
            && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
                out integer);
        return coerced;
    }

    private static string[] Items(string value) => value.Split(
        ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.Ordinal);

    private static string RequiredString(string value, string name, int maximum)
    {
        value = value.Trim();
        if (value.Length < MinimumStringLength || value.Length > maximum)
            throw new InvalidDataException(
                $"{name} must contain {MinimumStringLength} to {maximum} characters.");
        return value;
    }

    private static string Require(JsonObject arguments, string name) =>
        Text(arguments, name) ?? throw new InvalidDataException($"Argument '{name}' is required.");

    /// <summary>The JSON shape a rejected value actually arrived as, for the type-mismatch
    /// messages. It is one of eight compile-time constants chosen by the shape of the JSON and
    /// never by its content, so it carries no user data and no planner-chosen text.
    ///
    /// <para>Two sentences rather than one with a colon or a semicolon, and this is not cosmetic:
    /// these messages are echoed by <c>AskService.Diagnostic</c>, whose filter passes ASCII letters
    /// and digits plus space, underscore, apostrophe, full stop, comma and hyphen, and replaces
    /// every other character with '?'. "must be a string? received Array" reads like the log itself
    /// is unsure. Every character of "must be a string. Received Array." survives the filter
    /// unmodified.</para></summary>
    private static string Kind(JsonNode? value) =>
        value?.GetValueKind().ToString() ?? "Null";

    private static string? Text(JsonObject arguments, string name) =>
        arguments[name] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static DateOnly Date(JsonObject arguments, string name) =>
        DateOnly.TryParseExact(Text(arguments, name), IsoDateFormat, out var date)
            ? date
            : throw new InvalidDataException($"Argument '{name}' must be an ISO date.");

    private static void Enum(JsonObject arguments, string name, params string[] allowed)
    {
        if (Text(arguments, name) is not { } value) return;
        if (!allowed.Contains(value, StringComparer.Ordinal))
            throw new InvalidDataException($"Argument '{name}' has an unsupported value.");
    }

    private static void Bound(JsonObject arguments, string name, int minimum, int maximum)
    {
        if (arguments[name] is not JsonValue value || !value.TryGetValue<int>(out var number)) return;
        if (number < minimum || number > maximum)
            throw new InvalidDataException(
                $"Argument '{name}' must be between {minimum} and {maximum}.");
    }

    private static void CountList(
        JsonObject arguments,
        string name,
        int maximum,
        int maximumItemLength = MaximumStringLength)
    {
        if (Text(arguments, name) is not { } value) return;
        var items = Items(value);
        // Truncating a sixty-anchor list silently omits provisions from the answer, so the count
        // stays fatal. An empty split is absent-shaped and was already dropped before this point.
        if (items.Length is 0 || items.Length > maximum)
            throw new InvalidDataException(
                $"Argument '{name}' must contain 1 to {maximum} values.");
        if (items.Any(item => item.Length > maximumItemLength))
            throw new InvalidDataException(
                $"Every '{name}' value must contain at most {maximumItemLength} characters.");
    }
}

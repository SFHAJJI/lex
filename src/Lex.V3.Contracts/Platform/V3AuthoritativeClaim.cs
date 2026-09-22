using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform;

/// <summary>
/// What a typed fact is. Closed, because a kind that can be any string is a bare string with an
/// extra step, and the whole point of a typed fact is that a reader can tell a date from a digest
/// without parsing the sentence back apart.
/// </summary>
/// <remarks>
/// <b>A kind is what a placeholder accepts, not a description of a value.</b> S4-A05 forbids the
/// assistant relabelling derived facts and quoting without a hash-carrying citation; both become
/// things the type refuses once a quoting placeholder declares <see cref="ContentHash"/> and a
/// publisher-stated placeholder declares its own kind, rather than rules somebody has to remember.
/// </remarks>
public enum V3FactKind
{
    /// <summary>A calendar date as the publisher states it.</summary>
    [JsonStringEnumMemberName("calendar_date")]
    CalendarDate = 1,

    /// <summary>A work's key in this index.</summary>
    [JsonStringEnumMemberName("work_key")]
    WorkKey = 2,

    /// <summary>An anchor inside a work: the provision a claim is about.</summary>
    [JsonStringEnumMemberName("anchor_id")]
    AnchorId = 3,

    /// <summary>A SHA-256 of retained bytes. The kind a quotation must cite.</summary>
    [JsonStringEnumMemberName("content_hash")]
    ContentHash = 4,

    /// <summary>The publisher that stated the thing being claimed.</summary>
    [JsonStringEnumMemberName("publisher_name")]
    PublisherName = 5,

    /// <summary>The address the publisher served it from.</summary>
    [JsonStringEnumMemberName("source_uri")]
    SourceUri = 6,

    /// <summary>Which interval semantics a held state's dates are read under.</summary>
    [JsonStringEnumMemberName("interval_semantics")]
    IntervalSemantics = 7,

    /// <summary>The label a publisher gave a reference it wrote.</summary>
    [JsonStringEnumMemberName("reference_label")]
    ReferenceLabel = 8,

    /// <summary>What a publisher-written reference points at.</summary>
    [JsonStringEnumMemberName("target_iri")]
    TargetIri = 9,
}

/// <summary>
/// S4-A04's binding rule as a type: an authoritative claim is a <b>fixed template</b> from a closed
/// set, <b>bound to typed facts</b>, and rendered by substitution alone.
/// </summary>
/// <remarks>
/// <para>
/// The clause reads: <i>"`answer_dossier/1` is authoritative. Authoritative claims are fixed
/// templates bound to typed facts and `claims[]`; operations traces are exposed; model gloss remains
/// disabled."</i> It names those properties and <b>specifies no shape</b> — `answer_dossier` appears
/// nowhere under `baseline/pack/`, and the implementation gap audit records it as zero declared
/// types. This file is the first piece of that shape, proposed here and reviewed as code rather than
/// asserted as authority.
/// </para>
/// <para>
/// <b>What "model gloss remains disabled" means mechanically, and why it is a type and not a rule in
/// prose.</b> There is no way to put a sentence into a claim. A claim names a template id; the text
/// comes from the closed set; the only variation is the value of a named placeholder, and each value
/// is a typed fact with its own kind. <b>A claim with something to say that no template says cannot
/// be built</b> — it throws, rather than rendering the nearest thing. That is the difference between
/// a boundary and a request to respect one.
/// </para>
/// <para>
/// <b>Both directions of the binding are enforced.</b> Every placeholder the template names must
/// have a fact, or the claim is unbound and refuses. Every fact supplied must be named by the
/// template, or the claim carries a fact it never states and refuses. The first keeps a claim from
/// rendering a hole; <b>the second keeps a claim from carrying evidence it does not use</b>, which is
/// how a dossier comes to look better sourced than it is.
/// </para>
/// </remarks>
public sealed record V3ClaimTemplate
{
    internal static readonly Regex PlaceholderPattern = new(@"\{([a-z][a-z0-9_]*)\}", RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, V3FactKind> _kinds;

    private V3ClaimTemplate(
        string templateId,
        string text,
        ReadOnlyCollection<string> placeholders,
        IReadOnlyDictionary<string, V3FactKind> kinds)
    {
        TemplateId = templateId;
        Text = text;
        Placeholders = placeholders;
        _kinds = kinds;
    }

    /// <summary>The kind of fact a placeholder accepts. Throws for a name this template does not name.</summary>
    public V3FactKind KindOf(string placeholder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeholder);
        return _kinds.TryGetValue(placeholder, out var kind)
            ? kind
            : throw new ArgumentException(
                $"'{TemplateId}' names no placeholder '{placeholder}'.", nameof(placeholder));
    }

    /// <summary>The template's identity, which a claim names instead of carrying prose.</summary>
    public string TemplateId { get; }

    /// <summary>The fixed wording, with <c>{placeholder}</c> markers and nothing else variable.</summary>
    public string Text { get; }

    /// <summary>The placeholders this template names, in first-occurrence order, each one distinct.</summary>
    public ReadOnlyCollection<string> Placeholders { get; }

    internal static V3ClaimTemplate Define(
        string templateId, string text, params (string Name, V3FactKind Kind)[] declared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(declared);

        var placeholders = new List<string>();
        foreach (Match match in PlaceholderPattern.Matches(text))
        {
            var name = match.Groups[1].Value;
            if (!placeholders.Contains(name, StringComparer.Ordinal))
            {
                placeholders.Add(name);
            }
        }

        if (placeholders.Count == 0)
        {
            throw new ArgumentException(
                $"The claim template '{templateId}' names no placeholder. A template that binds no "
                + "typed fact states the same thing about every instrument, which is not a claim "
                + "about one.",
                nameof(text));
        }

        if (PlaceholderPattern.Replace(text, string.Empty).AsSpan().IndexOfAny('{', '}') >= 0)
        {
            throw new ArgumentException(
                $"The claim template '{templateId}' carries a brace this rule cannot read, so nothing would "
                + "bind it and it would reach a reader as literal text inside an authoritative sentence. A "
                + "placeholder is lower case, starts with a letter, and holds letters, digits and underscores.",
                nameof(text));
        }

        var kinds = new Dictionary<string, V3FactKind>(StringComparer.Ordinal);
        foreach (var (name, kind) in declared)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentException(
                    $"'{templateId}' declares '{name}' with a kind outside the closed set.", nameof(declared));
            }

            if (!kinds.TryAdd(name, kind))
            {
                throw new ArgumentException(
                    $"'{templateId}' declares '{name}' twice.", nameof(declared));
            }
        }

        // Both directions, as Bind refuses both directions: a placeholder with no declared kind
        // would accept anything, and a declared kind for a placeholder the wording does not name is
        // a rule about a sentence that is not there.
        var undeclared = placeholders.Where(name => !kinds.ContainsKey(name))
            .OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        if (undeclared.Length > 0)
        {
            throw new ArgumentException(
                $"The claim template '{templateId}' leaves {string.Join(", ", undeclared)} with no "
                + "declared kind, so the placeholder would accept a fact of any kind and the "
                + "wording could cite a digest where it meant a date.",
                nameof(declared));
        }

        var unwritten = kinds.Keys.Where(name => !placeholders.Contains(name, StringComparer.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        if (unwritten.Length > 0)
        {
            throw new ArgumentException(
                $"The claim template '{templateId}' declares a kind for {string.Join(", ", unwritten)}, "
                + "which its wording never names.",
                nameof(declared));
        }

        return new V3ClaimTemplate(templateId, text, placeholders.AsReadOnly(), kinds);
    }
}

/// <summary>
/// One typed fact a claim binds to a placeholder: a value and the kind it is, never a bare string.
/// </summary>
/// <remarks>
/// The kind is carried so a reader can tell a date from an identifier from a hash without parsing
/// the rendered sentence back apart, and <b>so that binding checks it against the kind the
/// placeholder declares</b> — which is what makes "never relabel a derived fact" and "never quote
/// without a hash-carrying citation" refusals rather than rules.
/// <para>
/// <b>The kind is still not validated against the value.</b> Nothing here confirms that a
/// <see cref="V3FactKind.ContentHash"/> is sixty-four hex characters or that a
/// <see cref="V3FactKind.CalendarDate"/> is a date; this type records what the producer says a
/// value is, and the binding requires the producer to say the thing the sentence needs. Checking
/// the value against its kind is a further slice and is stated here rather than implied.
/// </para>
/// </remarks>
public sealed record V3TypedFact(string Name, V3FactKind Kind, string Value)
{
    public static V3TypedFact Of(string name, V3FactKind kind, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is not in the closed set.");
        }

        ArgumentNullException.ThrowIfNull(value);
        return new V3TypedFact(name, kind, value);
    }
}

/// <summary>
/// The closed set of claim templates. Closed in the way the refusal registry is closed: adding one
/// is a change somebody reviews, not a string written at a call site.
/// </summary>
public static class V3ClaimTemplates
{
    /// <summary>What a held state of an instrument said on a date.</summary>
    public const string TextOnDate = "text_on_date";

    /// <summary>The interval a held state covers, in the publisher's own semantics.</summary>
    public const string StateInterval = "state_interval";

    /// <summary>That the index holds no state of a work for a date, which is an answer and not a gap.</summary>
    public const string NoStateForDate = "no_state_for_date";

    /// <summary>A reference one held article writes to another instrument, asserted by the publisher.</summary>
    public const string PublisherReference = "publisher_reference";

    private static readonly Dictionary<string, V3ClaimTemplate> ByIdValue = new[]
    {
        V3ClaimTemplate.Define(
            TextOnDate,
            "On {date}, {work} article {anchor} read as the text with content hash {text_sha256}, "
            + "as published by {publisher} at {source_uri}.",
            ("date", V3FactKind.CalendarDate),
            ("work", V3FactKind.WorkKey),
            ("anchor", V3FactKind.AnchorId),
            ("text_sha256", V3FactKind.ContentHash),
            ("publisher", V3FactKind.PublisherName),
            ("source_uri", V3FactKind.SourceUri)),
        V3ClaimTemplate.Define(
            StateInterval,
            "{work} has a held state {state_sha256} that {publisher} records as applying "
            + "from {applicable_from} to {applicable_to}, under {interval_semantics}.",
            ("work", V3FactKind.WorkKey),
            ("state_sha256", V3FactKind.ContentHash),
            ("publisher", V3FactKind.PublisherName),
            ("applicable_from", V3FactKind.CalendarDate),
            ("applicable_to", V3FactKind.CalendarDate),
            ("interval_semantics", V3FactKind.IntervalSemantics)),
        V3ClaimTemplate.Define(
            NoStateForDate,
            "This index holds no state of {work} for {date}; the nearest it holds is {nearest_date}, "
            + "and that is what this index holds rather than what exists.",
            ("work", V3FactKind.WorkKey),
            ("date", V3FactKind.CalendarDate),
            ("nearest_date", V3FactKind.CalendarDate)),
        V3ClaimTemplate.Define(
            PublisherReference,
            "In {work} article {anchor}, {publisher} wrote a reference labelled {label} to {target}; "
            + "this records that the reference was written and not what it means.",
            ("work", V3FactKind.WorkKey),
            ("anchor", V3FactKind.AnchorId),
            ("publisher", V3FactKind.PublisherName),
            ("label", V3FactKind.ReferenceLabel),
            ("target", V3FactKind.TargetIri)),
    }.ToDictionary(static template => template.TemplateId, StringComparer.Ordinal);

    /// <summary>Every template, by id, in ordinal order of the id.</summary>
    public static ReadOnlyCollection<V3ClaimTemplate> All { get; } =
        ByIdValue.Values.OrderBy(static template => template.TemplateId, StringComparer.Ordinal)
            .ToList().AsReadOnly();

    /// <summary>
    /// Whether an id names a template. A predicate answers about the id it is handed, including one
    /// that is null or blank: those are not templates, which is an answer and not a caller's error.
    /// <see cref="Get"/> throws, because there is no template to return.
    /// </summary>
    public static bool IsDefined(string? templateId) =>
        !string.IsNullOrWhiteSpace(templateId) && ByIdValue.ContainsKey(templateId);

    public static V3ClaimTemplate Get(string templateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        return ByIdValue.TryGetValue(templateId, out var template)
            ? template
            : throw new ArgumentException(
                $"'{templateId}' is not a claim template. Authoritative claims are fixed templates "
                + "from a closed set; a claim that needs wording no template carries is a change to "
                + "the set, reviewed, and never a sentence written at a call site.",
                nameof(templateId));
    }
}

/// <summary>
/// One authoritative claim: a template, the typed facts bound to its placeholders, and the rendered
/// text, which is the template with each placeholder replaced by its fact's value and nothing else.
/// </summary>
public sealed record V3AuthoritativeClaim
{
    private V3AuthoritativeClaim(
        string templateId, string rendered, ReadOnlyCollection<V3TypedFact> facts)
    {
        TemplateId = templateId;
        Rendered = rendered;
        Facts = facts;
    }

    public string TemplateId { get; }

    /// <summary>The template rendered by substitution. Never composed, never abbreviated.</summary>
    public string Rendered { get; }

    /// <summary>The bound facts, in the template's placeholder order.</summary>
    public ReadOnlyCollection<V3TypedFact> Facts { get; }

    /// <summary>
    /// Binds facts to a template, or refuses. Refuses when the template is not in the closed set,
    /// when a placeholder has no fact, when a fact is not a placeholder of this template, or when
    /// two facts claim the same name.
    /// </summary>
    public static V3AuthoritativeClaim Bind(string templateId, IReadOnlyList<V3TypedFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var template = V3ClaimTemplates.Get(templateId);

        var byName = new Dictionary<string, V3TypedFact>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            ArgumentNullException.ThrowIfNull(fact);

            // V3TypedFact.Of checks these, but Of is not the only way in: the record's primary
            // constructor is public, so a fact can reach Bind without passing through Of. Bind is the
            // door every claim comes through, so Bind is where the value has to hold up.
            if (string.IsNullOrWhiteSpace(fact.Name) || !Enum.IsDefined(fact.Kind))
            {
                throw new ArgumentException(
                    $"The claim '{templateId}' carries a fact with no name, or a kind outside the "
                    + "closed set. A fact whose kind a reader cannot see is a bare string, which is "
                    + "what typed facts exist to stop.",
                    nameof(facts));
            }

            if (string.IsNullOrWhiteSpace(fact.Value))
            {
                throw new ArgumentException(
                    $"The claim '{templateId}' binds '{fact.Name}' to an empty value. Rendering it "
                    + "would put an authoritative sentence in front of a reader with a gap where the "
                    + "evidence should be, which is the hole an unbound placeholder was refused for; "
                    + "a value nobody supplied is not different because it arrived as blank text.",
                    nameof(facts));
            }

            if (!byName.TryAdd(fact.Name, fact))
            {
                throw new ArgumentException(
                    $"The claim '{templateId}' binds '{fact.Name}' twice. A placeholder takes one "
                    + "fact, and two facts for one name means the producer held two answers and "
                    + "picked one here.",
                    nameof(facts));
            }
        }

        var unbound = template.Placeholders
            .Where(name => !byName.ContainsKey(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        if (unbound.Length > 0)
        {
            throw new ArgumentException(
                $"The claim '{templateId}' leaves {string.Join(", ", unbound)} unbound. A template "
                + "renders only when every placeholder has a typed fact; rendering a hole would put "
                + "an authoritative sentence in front of a reader with a gap inside it.",
                nameof(facts));
        }

        var unused = byName.Keys
            .Where(name => !template.Placeholders.Contains(name, StringComparer.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        if (unused.Length > 0)
        {
            throw new ArgumentException(
                $"The claim '{templateId}' carries {string.Join(", ", unused)}, which it never "
                + "states. A claim holding evidence its wording does not use reads as better "
                + "sourced than it is.",
                nameof(facts));
        }

        // ONE pass over the TEMPLATE, never over what a previous pass produced. Replacing each
        // placeholder in turn on the running text re-reads the values already substituted, so a value
        // holding another placeholder's marker was rewritten by a later pass - and whether it was
        // depended on the order the placeholders happen to be listed in. A value is publisher text; a
        // brace in one is data, not a caller's mistake. Here the match is taken from the template and
        // the replacement is returned as-is, so a value is output and never input.
        // The kind the wording needs, against the kind the producer says it has. This is where
        // "never quote without a hash-carrying citation" and "never relabel a derived fact" stop
        // being rules: a quoting placeholder declares ContentHash, and a fact that is not one
        // cannot reach the sentence.
        foreach (var name in template.Placeholders)
        {
            var wanted = template.KindOf(name);
            if (byName[name].Kind != wanted)
            {
                throw new ArgumentException(
                    $"The claim '{templateId}' binds '{name}' to a {byName[name].Kind} where its "
                    + $"wording states a {wanted}. A sentence that cites one kind of evidence and "
                    + "is given another says something its evidence does not support.",
                    nameof(facts));
            }
        }

        var rendered = V3ClaimTemplate.PlaceholderPattern.Replace(
            template.Text, match => byName[match.Groups[1].Value].Value);

        return new V3AuthoritativeClaim(
            templateId,
            rendered,
            template.Placeholders.Select(name => byName[name]).ToList().AsReadOnly());
    }
}

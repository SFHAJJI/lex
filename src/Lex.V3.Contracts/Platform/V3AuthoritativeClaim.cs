using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform;

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
    private static readonly Regex PlaceholderPattern = new(@"\{([a-z][a-z0-9_]*)\}", RegexOptions.Compiled);

    private V3ClaimTemplate(string templateId, string text, ReadOnlyCollection<string> placeholders)
    {
        TemplateId = templateId;
        Text = text;
        Placeholders = placeholders;
    }

    /// <summary>The template's identity, which a claim names instead of carrying prose.</summary>
    public string TemplateId { get; }

    /// <summary>The fixed wording, with <c>{placeholder}</c> markers and nothing else variable.</summary>
    public string Text { get; }

    /// <summary>The placeholders this template names, in first-occurrence order, each one distinct.</summary>
    public ReadOnlyCollection<string> Placeholders { get; }

    internal static V3ClaimTemplate Define(string templateId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

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

        return new V3ClaimTemplate(templateId, text, placeholders.AsReadOnly());
    }
}

/// <summary>
/// One typed fact a claim binds to a placeholder: a value and the kind it is, never a bare string.
/// </summary>
/// <remarks>
/// The kind is carried so a reader can tell a date from an identifier from a hash without parsing
/// the rendered sentence back apart, and so a later reviewer can require a template's placeholder to
/// be bound to the kind it was written for. <b>It is not validated against the value here</b>, and
/// that is stated rather than implied: this type records what the producer says a value is.
/// </remarks>
public sealed record V3TypedFact(string Name, string Kind, string Value)
{
    public static V3TypedFact Of(string name, string kind, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
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
            + "as published by {publisher} at {source_uri}."),
        V3ClaimTemplate.Define(
            StateInterval,
            "{work} has a held state {state_sha256} that {publisher} records as applying "
            + "from {applicable_from} to {applicable_to}, under {interval_semantics}."),
        V3ClaimTemplate.Define(
            NoStateForDate,
            "This index holds no state of {work} for {date}; the nearest it holds is {nearest_date}, "
            + "and that is what this index holds rather than what exists."),
        V3ClaimTemplate.Define(
            PublisherReference,
            "In {work} article {anchor}, {publisher} wrote a reference labelled {label} to {target}; "
            + "this records that the reference was written and not what it means."),
    }.ToDictionary(static template => template.TemplateId, StringComparer.Ordinal);

    /// <summary>Every template, by id, in ordinal order of the id.</summary>
    public static ReadOnlyCollection<V3ClaimTemplate> All { get; } =
        ByIdValue.Values.OrderBy(static template => template.TemplateId, StringComparer.Ordinal)
            .ToList().AsReadOnly();

    public static bool IsDefined(string templateId) => ByIdValue.ContainsKey(templateId);

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

        var rendered = template.Text;
        foreach (var name in template.Placeholders)
        {
            rendered = rendered.Replace("{" + name + "}", byName[name].Value, StringComparison.Ordinal);
        }

        return new V3AuthoritativeClaim(
            templateId,
            rendered,
            template.Placeholders.Select(name => byName[name]).ToList().AsReadOnly());
    }
}

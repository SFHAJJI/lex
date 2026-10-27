using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform;

/// <summary>Who to ask about an instrument Lex does not hold: an institution or an official service, never a person.</summary>
/// <param name="Name">The institution or service, by its official name.</param>
/// <param name="Link">Its official site.</param>
/// <param name="Role">What it is for the reader (R42's roles): <c>information</c>, the body that issues and publishes the instrument.</param>
public sealed record V3OutOfScopeWhoToAsk(string Name, string Link, string Role);

/// <summary>
/// One class of instrument Lex never holds, as the out-of-scope list states it: its token, the instrument in words, the
/// one-sentence reason, its official place and who to ask, and the forms of identifier that name it.
/// </summary>
public sealed record V3OutOfScopeClass(
    string Class,
    string Jurisdiction,
    string Instrument,
    string Reason,
    string OfficialSource,
    V3OutOfScopeWhoToAsk WhoToAsk,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> Examples);

/// <summary>
/// The checked-in list of what Lex never holds (the plan's item 6.10; the specification of record, §3, Data and coverage,
/// "Never": CSSF circulars, CNPD material, ordinary case law, communal regulations, RESA). An identifier one of its classes
/// names is refused <c>out_of_corpus_scope</c> by every operation that takes one, with the four elements
/// <c>36-ideal-evaluation.md</c> §4 mandates: the instrument asked for, its official place, a one-sentence reason and who to
/// ask, who is an institution or an official service and never a person (the owner's decision of 2026-10-19: the site names
/// no person).
/// </summary>
/// <remarks>
/// <para>
/// Each class is recognised by exact patterns over the whole identifier, never by a word inside a longer text, so a held act
/// whose title names one of these bodies (the law that organises the CNPD, a CSSF regulation, which R78 admits) is never
/// refused: a CSSF regulation is "Règlement CSSF n° 12-02", which no pattern here matches. The official places are each body's
/// own site; the list goes to the lawyer with the other named places (O4), and R42's handoff list, which a person writes and
/// checks (N2), may later add advisers.
/// </para>
/// <para>
/// Not listed yet, each for a stated reason: communal regulations (no single official place is published for them, each
/// commune publishes its own, so the entry waits for R42's list); Luxembourg court decisions (no identifier form names one
/// exactly, and the Constitutional Court's decisions are admitted later as their own class, R78); EU acts outside the EU
/// population (R80: the EU index does not yet state the rule its population follows, so an unheld EU act still answers
/// <c>identifier_unknown</c>).
/// </para>
/// </remarks>
public static class V3OutOfScope
{
    public const string Schema = "lex-v3-out-of-scope/1";

    /// <summary>The longest identifier the list is asked about; a longer one is a text, which no class names exactly.</summary>
    public const int MaximumIdentifierLength = 160;

    public static IReadOnlyList<V3OutOfScopeClass> Classes { get; } = Array.AsReadOnly(new[]
    {
        new V3OutOfScopeClass(
            "cssf_circular",
            "lu",
            "a circular of the Commission de surveillance du secteur financier (CSSF)",
            "CSSF circulars are issued and published by the CSSF, not by Legilux or EUR-Lex, and Lex does not collect them.",
            "https://www.cssf.lu/",
            new V3OutOfScopeWhoToAsk("Commission de surveillance du secteur financier (CSSF)", "https://www.cssf.lu/", "information"),
            Array.AsReadOnly(new[]
            {
                @"^(?:circulaire|circular)\s+(?:de\s+la\s+|of\s+the\s+)?CSSF\s+(?:n[°o]\.?\s*)?[0-9]{2}/[0-9]{3,4}$",
                @"^CSSF\s+(?:circulaire|circular)\s+(?:n[°o]\.?\s*)?[0-9]{2}/[0-9]{3,4}$",
                @"^CSSF\s+[0-9]{2}/[0-9]{3,4}$",
            }),
            Array.AsReadOnly(new[] { "Circulaire CSSF 20/747", "CSSF circular 22/806", "CSSF 20/747" })),
        new V3OutOfScopeClass(
            "cnpd_decision",
            "lu",
            "a decision or opinion of the Commission nationale pour la protection des données (CNPD)",
            "Decisions and opinions of the CNPD are issued and published by the CNPD, not by Legilux or EUR-Lex, and Lex does not collect them.",
            "https://cnpd.public.lu/",
            new V3OutOfScopeWhoToAsk("Commission nationale pour la protection des données (CNPD)", "https://cnpd.public.lu/", "information"),
            Array.AsReadOnly(new[]
            {
                @"^(?:d[ée]lib[ée]ration|d[ée]cision|decision|deliberation|avis|opinion)\s+(?:de\s+la\s+|of\s+the\s+)?CNPD\s+(?:n[°o]\.?\s*)?[0-9]{1,4}[A-Z]{0,3}/[0-9]{4}$",
                @"^CNPD\s+(?:(?:d[ée]lib[ée]ration|d[ée]cision|decision|deliberation|avis|opinion)\s+)?(?:n[°o]\.?\s*)?[0-9]{1,4}[A-Z]{0,3}/[0-9]{4}$",
            }),
            Array.AsReadOnly(new[] { "Délibération CNPD n° 12/2023", "CNPD decision 3/2021" })),
        new V3OutOfScopeClass(
            "eu_case_law",
            "eu",
            "a judgment, order or opinion of the Court of Justice of the European Union",
            "Decisions of the EU courts are published by the Court of Justice of the European Union; Lex holds EU legislation, not case law, and does not collect them.",
            "https://curia.europa.eu/",
            new V3OutOfScopeWhoToAsk("Court of Justice of the European Union (CJEU)", "https://curia.europa.eu/", "information"),
            Array.AsReadOnly(new[]
            {
                // The European Case Law Identifier of an EU court: ECLI:EU:{court}:{year}:{number}.
                @"^ECLI:EU:[CTF]:[0-9]{4}:[0-9]{1,6}$",
                // A CELEX of sector 6, which EUR-Lex gives case law, with or without its corrigendum suffix.
                @"^6[0-9]{4}[A-Z]{2}[0-9]{4}(?:\([0-9]{2}\))?$",
                // A case number of the Court of Justice (C-), the General Court (T-) or the Civil Service Tribunal (F-).
                @"^(?:(?:case|affaire|aff\.)\s+)?[CTF]-[0-9]{1,4}/[0-9]{2}(?:\s+P)?$",
            }),
            Array.AsReadOnly(new[] { "ECLI:EU:C:2014:317", "62012CJ0131", "C-131/12" })),
        new V3OutOfScopeClass(
            "resa",
            "lu",
            "a publication in the Recueil électronique des sociétés et associations (RESA)",
            "The RESA publishes the filings of companies and associations; it is published by Luxembourg Business Registers, not by Legilux, and Lex does not collect it.",
            "https://www.lbr.lu/",
            new V3OutOfScopeWhoToAsk("Luxembourg Business Registers (LBR)", "https://www.lbr.lu/", "information"),
            Array.AsReadOnly(new[]
            {
                @"^RESA(?:[\s_:/-]+[0-9A-Za-z][0-9A-Za-z._/-]{0,40})?$",
                @"^recueil\s+[ée]lectronique\s+des\s+soci[ée]t[ée]s\s+et\s+associations(?:\s.{0,60})?$",
            }),
            Array.AsReadOnly(new[] { "RESA_2020_123.456", "Recueil électronique des sociétés et associations" })),
    });

    private static readonly IReadOnlyList<(V3OutOfScopeClass Class, Regex[] Patterns)> Compiled = Classes
        .Select(static entry => (entry, entry.Patterns
            .Select(static pattern => new Regex(
                pattern,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
                TimeSpan.FromMilliseconds(250)))
            .ToArray()))
        .ToArray();

    /// <summary>
    /// The class the identifier names, or null: the identifier trimmed, its spaces (no-break ones too) run together and its
    /// Unicode composed, then matched whole against each class's patterns, ignoring case.
    /// </summary>
    public static V3OutOfScopeClass? Recognise(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > MaximumIdentifierLength)
        {
            return null;
        }

        var normalised = Regex.Replace(
            identifier.Normalize(NormalizationForm.FormC).Replace(' ', ' ').Replace(' ', ' '),
            @"\s+",
            " ",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250)).Trim();
        foreach (var (entry, patterns) in Compiled)
        {
            if (patterns.Any(pattern => pattern.IsMatch(normalised)))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>The list as canonical JSON (every class in order, its patterns and examples included), as the census publishes it.</summary>
    public static byte[] CanonicalUtf8() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schema = Schema,
        classes = Classes.Select(static entry => new
        {
            @class = entry.Class,
            jurisdiction = entry.Jurisdiction,
            instrument = entry.Instrument,
            reason = entry.Reason,
            official_source = entry.OfficialSource,
            who_to_ask = WhoToAskPayload(entry.WhoToAsk),
            patterns = entry.Patterns,
            examples = entry.Examples,
        }),
    });

    /// <summary>The SHA-256 of <see cref="CanonicalUtf8"/>, which every out-of-scope refusal names.</summary>
    public static string Sha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(CanonicalUtf8()));

    /// <summary>Who to ask, as a refusal's payload and the census carry it.</summary>
    public static object WhoToAskPayload(V3OutOfScopeWhoToAsk who) => new { name = who.Name, link = who.Link, role = who.Role };
}

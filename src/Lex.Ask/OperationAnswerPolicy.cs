using Lex.Mcp;

namespace Lex.Ask;

/// <summary>Where the instant Lex actually served came from. A reader told "as at 2026-08-12" has
/// no way to know they never asked for that date, and rule 7 of the answer prompt demands the
/// reading be stated in one clause; the deterministic reply path writes no model text and so
/// bypassed rule 7 entirely.</summary>
internal enum InstantSource
{
    /// <summary>The user named the instant, so nothing has to be disclosed.</summary>
    Stated,

    /// <summary>The argument gate completed an omitted date to today.</summary>
    DefaultedToToday,

    /// <summary>A bare year was widened to its calendar window rather than resolved to a day.</summary>
    WidenedFromYear,
}

/// <summary>What the deterministic reply must disclose about how it got here: which work lost when
/// the user's own words authorized more than one, and where the served instant came from. Both are
/// already computed upstream and were both being thrown away before the prose was written.</summary>
internal sealed record AnswerDisclosure(
    string? RunnerUpWork = null,
    string? RunnerUpTitle = null,
    InstantSource Instant = InstantSource.Stated);

internal static class OperationAnswerPolicy
{
    public static string Render(
        string locale,
        IReadOnlyList<OperationResult> results,
        IReadOnlyList<UiEffect> effects,
        IReadOnlyList<AnswerDisclosure?>? disclosures = null)
    {
        var lines = results.OrderBy(result => result.UserOrder)
            .Select(result => RenderOne(locale, result,
                result.UserOrder < effects.Count ? effects[result.UserOrder] : new UiEffect(),
                disclosures is not null && result.UserOrder < disclosures.Count
                    ? disclosures[result.UserOrder] : null))
            .Where(line => line.Length > 0)
            .ToArray();
        return string.Join("\n", lines);
    }

    private static string RenderOne(
        string locale, OperationResult result, UiEffect effect, AnswerDisclosure? disclosure)
    {
        var rendered = RenderOneCore(locale, result, effect);
        var evidence = effect.Provision?.Evidence ?? effect.Diff?.Evidence
            ?? effect.History?.Evidence ?? effect.Timeline?.Evidence ?? effect.Ranking?.Evidence
            ?? effect.InForce?.Evidence ?? effect.CitedBy?.Evidence ?? effect.Coverage?.Evidence
            ?? effect.Verification?.Evidence ?? effect.Gap?.Evidence ?? effect.Workspace?.Evidence;
        if (evidence?.Any(item => item.Provisional) == true)
            rendered += locale == "fr"
                ? " Cet état futur est provisoire, selon les données éditeur actuellement publiées."
                : " This future state is provisional, based on publisher data currently available.";
        return rendered + Disclose(locale, disclosure);
    }

    /// <summary>The one clause that makes a selection error correctable in a single turn. The
    /// reader is told that Lex chose, and what it chose against, in the reply itself rather than
    /// in a trace nobody opens.</summary>
    internal static string Disclose(string locale, AnswerDisclosure? disclosure)
    {
        if (disclosure is null) return "";
        var fr = locale == "fr";
        var text = "";
        if (disclosure.RunnerUpWork is { Length: > 0 } runnerUp)
        {
            var name = disclosure.RunnerUpTitle is { Length: > 0 } title
                ? $"{title} ({runnerUp})" : runnerUp;
            text += fr
                ? $" Votre formulation nommait plusieurs instruments; Lex a retenu celui qui fait l'objet de la citation plutôt que {name}."
                : $" Your wording named more than one instrument; Lex used the one the citation is about rather than {name}.";
        }
        text += disclosure.Instant switch
        {
            InstantSource.DefaultedToToday => fr
                ? " Vous n'avez pas indiqué de date: Lex a lu la question comme portant sur aujourd'hui."
                : " You gave no date, so Lex read the question as being about today.",
            InstantSource.WidenedFromYear => fr
                ? " Vous avez indiqué une année sans jour: Lex a lu la question comme portant sur l'année entière plutôt que de choisir une date."
                : " You gave a year with no day, so Lex read the question as covering the whole year rather than picking a date inside it.",
            _ => "",
        };
        return text;
    }

    private static string RenderOneCore(string locale, OperationResult result, UiEffect effect)
    {
        var fr = locale == "fr";
        if (result.TransportOutcome != TransportOutcome.Completed)
            return fr
                ? $"Cette opération n'a pas été exécutée: {Transport(fr, result.TransportOutcome)}."
                : $"This operation was not evaluated: {Transport(fr, result.TransportOutcome)}.";
        if (result.LegalOutcome == LegalOutcome.NeedsClarification)
            return Describe(locale, effect) ?? (fr
                ? "Lex a besoin d'un instrument précis avant de continuer."
                : "Lex needs a specific instrument before it can continue.");
        if (result.LegalOutcome == LegalOutcome.InvalidRequest)
            return fr ? "Cette demande ne correspond pas à une opération juridique valide."
                : "This request does not map to a valid legal operation.";
        if (result.LegalOutcome == LegalOutcome.LegalBoundary)
            return Describe(locale, effect) ?? (fr
                ? "Lex peut fournir le texte vérifié, mais pas un avis juridique."
                : "Lex can provide verified text, but it cannot provide legal advice.");
        return Describe(locale, effect) ?? (fr ? "L'opération est terminée." : "The operation is complete.");
    }

    /// <summary>
    /// The named line for one view, or null when the effect carries no view.
    ///
    /// <para>Every one of these names the instrument and the date. It is the only place that
    /// writes those sentences, because the alternative already happened: the synthesis-failure
    /// fallback in <c>AskService.ReplyFor</c> kept a second, anonymous copy of the same set
    /// ("The exact publisher text for the selected law and date is open below") and that copy is
    /// what an audit saw served for the wrong instrument. An unnamed answer makes every selection
    /// error invisible to the reader.</para>
    /// </summary>
    internal static string? Describe(string locale, UiEffect effect)
    {
        var fr = locale == "fr";
        if (effect.Gap is { } gap)
            return gap.Explanation;
        if (effect.Ranking is { } ranking)
            return fr
                ? $"Dans un périmètre sélectionné de {ranking.PopulationWorks:n0} instruments, Lex en a trouvé {ranking.WorksChanged:n0} ayant reçu {ranking.NewVersions:n0} dates de version éditeur entre le {ranking.FromDate} et le {ranking.ToDate}. Le classement vérifié est affiché ci-dessous."
                : $"Within a selected population of {ranking.PopulationWorks:n0} works, Lex found {ranking.WorksChanged:n0} instruments with {ranking.NewVersions:n0} publisher version dates between {ranking.FromDate} and {ranking.ToDate}. The verified ranking is open below.";
        if (effect.InForce is { } inForce)
        {
            var jurisdictions = string.Join(", ", (inForce.Evidence ?? [])
                .Select(item => item.Jurisdiction)
                .Where(value => value is { Length: > 0 })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
            var scope = jurisdictions.Length == 0 ? "" : fr
                ? $" pour {jurisdictions}"
                : $" across {jurisdictions}";
            return fr
                ? $"Lex a trouvé {inForce.Total:n0} états observés par les éditeurs{scope} couvrant le {inForce.Date}. Il ne s'agit pas d'un inventaire exhaustif de l'effet juridique."
                : $"Lex found {inForce.Total:n0} publisher-observed states{scope} covering {inForce.Date}. This is not an exhaustive legal-effect inventory.";
        }
        if (effect.CitedBy is { } cited)
            return fr
                ? $"Lex a trouvé {cited.CitingArticles:n0} article(s) faisant référence à {cited.CitedWork}."
                : $"Lex found {cited.CitingArticles:n0} article(s) referring to {cited.CitedWork}.";
        if (effect.Provision is { } provision)
        {
            var served = Served(fr, provision.Subject.Date, provision.ValidFrom);
            if (provision.OutlineOnly)
                return fr
                    ? $"La table des matières publiée de {Name(provision.Subject)} au {provision.ValidFrom} est affichée ci-dessous"
                      + (provision.Truncated ? "; cette vue bornée n'en montre qu'une partie." : ".") + served
                    : $"The publisher table of contents for {Name(provision.Subject)} at {provision.ValidFrom} is open below"
                      + (provision.Truncated ? "; this bounded view shows only part of it." : ".") + served;
            if (provision.TextTruncated)
                return fr
                    ? $"Lex détient le texte publié de {Name(provision.Subject)} au {provision.ValidFrom}, mais cette réponse bornée n'en affiche qu'une partie. Les liens officiels sont disponibles ci-dessous." + served
                    : $"Lex holds the publisher text for {Name(provision.Subject)} at {provision.ValidFrom}, but this bounded response shows only part of it. The official links are available below." + served;
            if (provision.Provisions.Count == 1
                && provision.Provisions[0] is { TextOmitted: false, Text.Length: > 0 } item)
            {
                var label = item.Num is { Length: > 0 } ? item.Num : item.Anchor;
                var heading = item.Heading is { Length: > 0 } ? $" — {item.Heading}" : "";
                var excerpt = Excerpt(item.Text, 600);
                return fr
                    ? $"Le texte éditeur vérifié de {label}{heading} dans {Name(provision.Subject)}, état du {provision.ValidFrom}, est : « {excerpt} »" + served
                    : $"The verified publisher text of {label}{heading} in {Name(provision.Subject)}, state from {provision.ValidFrom}, is: “{excerpt}”" + served;
            }
            return fr
                ? $"Le texte exact publié pour {Name(provision.Subject)} à la date du {provision.ValidFrom} est affiché ci-dessous." + served
                : $"The exact publisher text for {Name(provision.Subject)} at {provision.ValidFrom} is open below." + served;
        }
        if (effect.Diff is { } diff)
            // A comparison the extraction profiles make unreliable is not a comparison, and the
            // named line has to say so rather than announce a verified result that is not there.
            return diff.Status == McpStatus.ProfilesDiffer
                ? fr
                    ? $"Lex ne peut pas produire de comparaison fiable de {Name(diff.Subject)} entre le {diff.FromDate} et le {diff.ToDate}, car les deux versions utilisent des profils d'extraction incompatibles. Le motif et les deux versions vérifiées de l'éditeur sont ouverts ci-dessous."
                    : $"Lex cannot produce a reliable comparison of {Name(diff.Subject)} between {diff.FromDate} and {diff.ToDate} because the two versions use incompatible extraction profiles. The reason and both verified publisher versions are open below."
                : fr
                    ? $"La comparaison vérifiée de {Name(diff.Subject)} entre le {diff.FromDate} et le {diff.ToDate} est affichée ci-dessous."
                    : $"The verified comparison of {Name(diff.Subject)} between {diff.FromDate} and {diff.ToDate} is open below.";
        if (effect.History is { } history)
        {
            var first = history.States.Select(state => state.ValidFrom)
                .Where(value => value.Length > 0).Order(StringComparer.Ordinal).FirstOrDefault();
            var last = history.States.Select(state => state.ValidFrom)
                .Where(value => value.Length > 0).Order(StringComparer.Ordinal).LastOrDefault();
            var dates = first is null ? "" : first == last
                ? (fr ? $", daté du {first}" : $", dated {first}")
                : (fr ? $", du {first} au dernier état commençant le {last}"
                    : $", from {first} through the latest state beginning {last}");
            var semantics = history.Evidence?.Select(item => item.TimelineSemantics)
                .FirstOrDefault(value => value is { Length: > 0 });
            var kind = semantics switch
            {
                "official_consolidation_state" => fr
                    ? "états de consolidation de l'éditeur"
                    : "publisher consolidation states",
                "publisher_applicability" => fr
                    ? "états d'applicabilité de l'éditeur"
                    : "publisher applicability states",
                _ => fr ? "états de version de l'éditeur" : "publisher version states",
            };
            return fr
                ? $"L'historique vérifié de {history.Anchor} dans {Name(history.Subject)} contient {history.DistinctTexts:n0} texte(s) distinct(s){dates}. Ce sont des {kind}, pas des conclusions sur l'effet juridique."
                : $"The verified history of {history.Anchor} in {Name(history.Subject)} contains {history.DistinctTexts:n0} distinct text(s){dates}. These are {kind}, not conclusions about legal effect.";
        }
        if (effect.Timeline is { } timeline)
        {
            var first = timeline.Rows.Select(row => row.ValidFrom)
                .Where(value => value.Length > 0).Order(StringComparer.Ordinal).FirstOrDefault();
            var last = timeline.Rows.Select(row => row.ValidFrom)
                .Where(value => value.Length > 0).Order(StringComparer.Ordinal).LastOrDefault();
            var semantics = timeline.Evidence?.Select(item => item.TimelineSemantics)
                .FirstOrDefault(value => value is { Length: > 0 });
            var kind = semantics switch
            {
                "official_consolidation_state" => fr
                    ? "états de consolidation de l'éditeur"
                    : "publisher consolidation states",
                "publisher_applicability" => fr
                    ? "états d'applicabilité de l'éditeur"
                    : "publisher applicability states",
                _ => fr ? "états de version de l'éditeur" : "publisher version states",
            };
            var dates = first is null ? "" : first == last
                ? (fr ? $", daté du {first}" : $", dated {first}")
                : (fr ? $", du {first} au dernier état commençant le {last}"
                    : $", from {first} through the latest state beginning {last}");
            var bounded = timeline.Truncated
                ? (fr ? " Cette vue bornée est tronquée." : " This bounded view is truncated.")
                : "";
            return fr
                ? $"Lex détient {timeline.TotalCount:n0} {kind} pour {Name(timeline.Subject)}{dates}. Ce sont des dates éditeur, pas une conclusion sur l'effet juridique.{bounded}"
                : $"Lex holds {timeline.TotalCount:n0} {kind} for {Name(timeline.Subject)}{dates}. These are publisher dates, not a conclusion about legal effect.{bounded}";
        }
        if (effect.Coverage is { } coverage)
        {
            var works = coverage.Publishers.Sum(item => item.Works);
            var versions = coverage.Publishers.Sum(item => item.Versions);
            return fr
                ? $"Lex monte {works:n0} textes et {versions:n0} versions vérifiées. La couverture et les lacunes sont affichées ci-dessous."
                : $"Lex mounts {works:n0} works and {versions:n0} verified versions. Coverage and known gaps are open below.";
        }
        if (effect.Verification is { } verification)
            return fr
                ? $"La chaîne de preuve de {verification.LexId} est affichée ci-dessous. Signature: {Signature(fr, verification.SignatureValid)}."
                : $"The proof chain for {verification.LexId} is open below. Signature: {Signature(fr, verification.SignatureValid)}.";
        if (effect.Workspace is { Results.Count: > 0 } search)
        {
            var facts = string.Join("; ", search.Results.Take(3).Select(SearchFactLine));
            return fr
                ? $"Lex a trouvé {search.Results.Count:n0} correspondance(s) bornée(s) dans des dispositions publiées : {facts}."
                : $"Lex found {search.Results.Count:n0} bounded publisher-provision match(es): {facts}.";
        }
        if (effect.Workspace is not null)
            return fr ? "Les résultats correspondants sont affichés dans l'espace de recherche."
                : "The matching results are open in the search workspace.";
        return null;
    }

    private static string SearchFactLine(SearchFact fact)
    {
        var provision = fact.Number is { Length: > 0 } number ? number : fact.Anchor;
        if (fact.Heading is { Length: > 0 } heading) provision += $" — {heading}";
        var work = fact.Title is { Length: > 0 } title
            ? $"{title} ({fact.Work})" : fact.Work;
        if (fact.Snippet is not { Length: > 0 } snippet)
            return $"{provision} in {work}";
        snippet = string.Join(' ', snippet.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (snippet.Length > 240) snippet = snippet[..239] + "…";
        return $"{provision} in {work}: {snippet}";
    }

    /// <summary>The requested instant and the served one, whenever they differ. A version that
    /// took effect years before the date asked about is the correct answer and is also the single
    /// most misreadable line in the product, because "at 2016-05-04" beside a question about 2021
    /// looks like the wrong version rather than the one in force.</summary>
    private static string Served(bool fr, string? requested, string served) =>
        requested is { Length: > 0 } && served.Length > 0
        && !string.Equals(requested, served, StringComparison.Ordinal)
            ? fr
                ? $" Vous avez demandé le {requested}; cet état est en vigueur depuis le {served}."
                : $" You asked about {requested}; this is the state in force from {served}."
            : "";

    /// <summary>
    /// Always the title AND the lex_id.
    ///
    /// <para>A title alone is not checkable: two consolidations of the same act share it, a
    /// translated title does not match the permalink, and a title is exactly the part a wrong
    /// selection still gets plausibly right. The lex_id is what the reader verifies against the
    /// link that is open beside the prose.</para>
    /// </summary>
    internal static string Name(Subject subject) =>
        subject.Title is { Length: > 0 } title && !string.Equals(title, subject.Work, StringComparison.Ordinal)
            ? $"{title} ({subject.Work})"
            : subject.Work;

    private static string Excerpt(string text, int maximumCharacters)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..maximumCharacters].TrimEnd() + " …";
    }

    private static string Signature(bool fr, bool? valid) => (fr, valid) switch
    {
        (true, true) => "vérifiée",
        (true, false) => "échec de vérification",
        (true, null) => "indisponible",
        (_, true) => "verified",
        (_, false) => "verification failed",
        _ => "unavailable",
    };

    private static string Transport(bool fr, TransportOutcome outcome) => (fr, outcome) switch
    {
        (true, TransportOutcome.Cancelled) => "annulée",
        (true, TransportOutcome.TimedOut) => "délai dépassé",
        (true, TransportOutcome.UpstreamFailed) => "service amont indisponible",
        (true, TransportOutcome.OverQuota) => "quota épuisé",
        (_, TransportOutcome.Cancelled) => "cancelled",
        (_, TransportOutcome.TimedOut) => "timed out",
        (_, TransportOutcome.UpstreamFailed) => "upstream service failed",
        (_, TransportOutcome.OverQuota) => "quota exhausted",
        _ => "unknown transport state",
    };
}

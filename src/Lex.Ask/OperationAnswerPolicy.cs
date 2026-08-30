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
    /// <summary>How many state dates a history line may name before the span is the kinder
    /// summary. Chosen so the two histories a reader asks for most, a consolidated EU regulation
    /// and a constitutional article, are answered with their dates rather than their bounds.</summary>
    private const int MaximumListedHistoryDates = 12;

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
        return rendered + DisclosePublisherLimitations(locale, effect.PublisherLimitations)
                        + Disclose(locale, disclosure);
    }

    internal static string DisclosePublisherLimitations(
        string locale,
        IReadOnlyList<PublisherLimitationView>? source) =>
        string.Concat(PublisherLimitationSentences(locale, source));

    internal static IReadOnlyList<string> PublisherLimitationSentences(
        string locale,
        IReadOnlyList<PublisherLimitationView>? source)
    {
        var limitations = PublisherLimitationPolicy.Normalize(source);
        if (limitations.Count == 0) return [];
        var fr = locale == "fr";
        return limitations.Select(item =>
        {
            var publisher = item.Publisher is { Length: > 0 }
                ? item.Jurisdiction is { Length: > 0 }
                    ? $"{item.Publisher} ({item.Jurisdiction})"
                    : item.Publisher
                : item.Jurisdiction is { Length: > 0 }
                    ? item.Jurisdiction
                    : fr ? "un éditeur sélectionné" : "a selected publisher";
            if (item.UnsupportedFilters.Count == 0)
                return fr
                    ? $" Limite de capacité: {publisher} n'a pas exécuté cette opération, car son index a signalé qu'un filtre demandé n'était pas pris en charge pour le périmètre sélectionné. Il s'agit de la couverture de Lex, et non d'une preuve de l'absence d'une loi ou d'un record."
                    : $" Capability limitation: {publisher} did not run this operation because its index reported that a requested filter is unsupported for the selected scope. This is about Lex coverage, not evidence that a law or record is absent.";
            var filters = string.Join(", ", item.UnsupportedFilters);
            return fr
                ? $" Limite de capacité: {publisher} n'a pas exécuté le filtre [{filters}], car son index ne le décrit pas pour le périmètre demandé. Il s'agit de la couverture de Lex, et non d'une preuve de l'absence d'une loi ou d'un record."
                : $" Capability limitation: {publisher} did not run the [{filters}] filter because its index does not describe it for the requested scope. This is about Lex coverage, not evidence that a law or record is absent.";
        }).ToArray();
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
        {
            var one = cited.CitingArticles == 1;
            var recognizedScope = cited.EvidenceScope
                == "captured_cross_references_in_held_non_withdrawn_versions";
            var complete = cited.ExactComplete;
            var count = complete
                ? fr
                    ? $"Lex a trouvé au total {cited.CitingArticles:n0} {(one ? "article" : "articles")} faisant référence à {cited.CitedWork}."
                    : $"Lex found a total of {cited.CitingArticles:n0} {(one ? "article" : "articles")} referring to {cited.CitedWork}."
                : fr
                    ? $"Lex a renvoyé {cited.CitingArticles:n0} {(one ? "article" : "articles")} faisant référence à {cited.CitedWork}."
                    : $"Lex returned {cited.CitingArticles:n0} {(one ? "article" : "articles")} referring to {cited.CitedWork}.";
            var scope = recognizedScope
                ? fr
                    ? " Les éléments de preuve couvrent les renvois capturés par Lex dans les versions éditeur détenues et non retirées."
                    : " The evidence covers cross-references Lex captured in held, non-withdrawn publisher versions."
                : fr
                    ? " La réponse ne contient pas de périmètre de preuve reconnu."
                    : " The response does not carry a recognized evidence scope.";
            var legalEffect = cited.CurrentLegalEffectAssessed == false
                ? fr
                    ? one
                        ? " Lex n'a pas évalué si ce renvoi produit actuellement un effet juridique."
                        : " Lex n'a pas évalué si ces renvois produisent actuellement un effet juridique."
                    : one
                        ? " Lex did not assess whether this reference is currently legally operative."
                        : " Lex did not assess whether these references are currently legally operative."
                : "";
            var relationship = cited.RelationshipTypeAssessed == false
                ? fr
                    ? one
                        ? " Lex n'a pas classé son type de relation."
                        : " Lex n'a pas classé leurs types de relation."
                    : one
                        ? " Lex did not classify its relationship type."
                        : " Lex did not classify their relationship types."
                : "";
            return count + scope + legalEffect + relationship;
        }
        if (effect.Provision is { } provision)
        {
            var served = Served(fr, provision.Subject.Date, provision.ValidFrom);
            if (provision.OutlineOnly)
            {
                var gapTotal = provision.TotalProvisionGaps
                    ?? provision.ProvisionGaps?.Count ?? 0;
                var gapDisclosure = gapTotal == 0 ? "" : fr
                    ? $" Elle comprend {gapTotal:n0} coordonnée(s) sans libellé certifié, signalée(s) comme lacunes typées."
                    : $" It includes {gapTotal:n0} coordinate(s) without certified wording, marked as typed gaps.";
                return fr
                    ? $"La table des matières publiée de {Name(provision.Subject)} au {provision.ValidFrom} est affichée ci-dessous"
                      + (provision.Truncated == true ? "; cette vue bornée n'en montre qu'une partie." : ".")
                      + gapDisclosure + served
                    : $"The publisher table of contents for {Name(provision.Subject)} at {provision.ValidFrom} is open below"
                      + (provision.Truncated == true ? "; this bounded view shows only part of it." : ".")
                      + gapDisclosure + served;
            }
            if (provision.ProvisionGaps is { Count: > 0 } typedGaps)
            {
                var totalGaps = provision.TotalProvisionGaps ?? typedGaps.Count;
                var bounded = provision.Truncated != false || provision.TextTruncated != false
                    || totalGaps > typedGaps.Count;
                if (bounded)
                    return fr
                        ? $"Lex détient un état éditeur partiel de {Name(provision.Subject)} au {provision.ValidFrom} : {totalGaps:n0} coordonnée(s) publiée(s) n'ont pas de libellé certifié. Cette réponse bornée affiche {typedGaps.Count:n0} lacune(s) typée(s)"
                          + (provision.TextTruncated == true ? " et omet une partie du texte éditeur détenu" : "")
                          + "; une source officielle accompagne chaque lacune affichée." + served
                        : $"Lex holds a partial publisher state for {Name(provision.Subject)} at {provision.ValidFrom}: {totalGaps:n0} published coordinate(s) have no certified wording. This bounded response shows {typedGaps.Count:n0} typed gap(s)"
                          + (provision.TextTruncated == true ? " and omits some held publisher text" : "")
                          + "; an official source accompanies each shown gap." + served;
                return fr
                    ? $"Lex affiche le texte éditeur certifié disponible de {Name(provision.Subject)} au {provision.ValidFrom}, mais {typedGaps.Count:n0} coordonnée(s) publiée(s) n'ont pas de libellé certifié. Les lacunes typées et leurs sources officielles sont affichées ci-dessous." + served
                    : $"Lex shows the available certified publisher text for {Name(provision.Subject)} at {provision.ValidFrom}, but {typedGaps.Count:n0} published coordinate(s) have no certified wording. The typed gaps and their official sources are shown below." + served;
            }
            if (provision.TextTruncated == true)
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
                : Comparison(fr, diff);
        if (effect.History is { } history)
        {
            // The change dates themselves, because they are the answer to "when did this change?"
            // and a count with an outer span is not one: the same six states between 1919 and 2023
            // describe a different history for every distribution of dates inside them. Listed
            // only when the view holds every state the publisher counted, so a bounded history
            // never reads as the whole set; then the span says what it honestly is.
            var starts = history.States.Select(state => state.ValidFrom)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var returnedStateCount = history.States.Count;
            var completeDates = starts.Length switch
            {
                0 => "",
                1 when returnedStateCount == 1 => fr
                    ? $", daté du {starts[0]}"
                    : $", dated {starts[0]}",
                1 => fr
                    ? $", avec des états débutant le {starts[0]}"
                    : $", with states beginning {starts[0]}",
                <= MaximumListedHistoryDates when starts.Length == returnedStateCount => fr
                    ? $", avec des états débutant les {string.Join(", ", starts)}"
                    : $", with states beginning {string.Join(", ", starts)}",
                _ => fr
                    ? $", du {starts[0]} au dernier état commençant le {starts[^1]}"
                    : $", from {starts[0]} through the latest state beginning {starts[^1]}",
            };
            var returnedDates = starts.Length switch
            {
                0 => "",
                1 when returnedStateCount == 1 => fr
                    ? $", avec un état renvoyé daté du {starts[0]}"
                    : $", with one returned state dated {starts[0]}",
                1 => fr
                    ? $", avec {returnedStateCount:n0} états renvoyés débutant le {starts[0]}"
                    : $", with {returnedStateCount:n0} returned states beginning {starts[0]}",
                _ => fr
                    ? $", avec des états renvoyés du {starts[0]} au dernier état renvoyé commençant le {starts[^1]}"
                    : $", with returned states from {starts[0]} through the last returned state beginning {starts[^1]}",
            };
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
            // The expression, because this is the one named line whose subject carries no text.
            // article_history filters to a single language and returns dates and digests only, so
            // a reader who asked for the French article cannot see from the answer which of the
            // work's expressions was read; art_11 of the Constitution exists in three, and they
            // do not share a history.
            var expression = history.Subject.Language is { Length: > 0 } language
                ? (fr ? $"l'expression {language} de " : $"the {language} expression of ")
                : "";
            // McpCore.article_history assigns ProvisionStateCount (COUNT(*)) to distinct_texts.
            // This surface therefore names publisher states, not an unproved DISTINCT text count.
            var reportedUnit = fr
                ? history.DistinctTexts == 1 ? "état de l'éditeur" : "états de l'éditeur"
                : history.DistinctTexts == 1 ? "publisher state" : "publisher states";
            var returnedUnit = fr
                ? returnedStateCount == 1 ? "état" : "états"
                : returnedStateCount == 1 ? "state" : "states";
            var historyResult = history.Truncated == false
                ? fr
                    ? $"L'historique vérifié de {history.Anchor} dans {expression}{Name(history.Subject)} contient {history.DistinctTexts:n0} {reportedUnit}{completeDates}."
                    : $"The verified history of {history.Anchor} in {expression}{Name(history.Subject)} contains {history.DistinctTexts:n0} {reportedUnit}{completeDates}."
                : fr
                    ? $"L'historique vérifié de {history.Anchor} dans {expression}{Name(history.Subject)} signale {history.DistinctTexts:n0} {reportedUnit} et renvoie {returnedStateCount:n0} {returnedUnit}{returnedDates}."
                    : $"The verified history of {history.Anchor} in {expression}{Name(history.Subject)} reports {history.DistinctTexts:n0} {reportedUnit} and returns {returnedStateCount:n0} {returnedUnit}{returnedDates}.";
            var historyCompleteness = history.Truncated switch
            {
                true => fr ? " Cette réponse bornée est tronquée."
                    : " This bounded response is truncated.",
                null => fr ? " La réponse n'indique pas si l'historique est complet."
                    : " The response does not record whether the history is complete.",
                _ => "",
            };
            return historyResult + (fr
                ? $" Ce sont des {kind}, pas des conclusions sur l'effet juridique."
                : $" These are {kind}, not conclusions about legal effect.")
                + historyCompleteness;
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
            var completeDates = first is null ? "" : first == last
                ? (fr ? $", daté du {first}" : $", dated {first}")
                : (fr ? $", du {first} au dernier état commençant le {last}"
                    : $", from {first} through the latest state beginning {last}");
            var returnedDates = first is null ? "" : first == last
                ? (fr ? $", avec un état renvoyé daté du {first}"
                    : $", with one returned state dated {first}")
                : (fr ? $", avec des états renvoyés du {first} au dernier état renvoyé commençant le {last}"
                    : $", with returned states from {first} through the last returned state beginning {last}");
            var timelineResult = timeline.Truncated switch
            {
                false => fr
                    ? $"Lex détient {timeline.TotalCount:n0} {kind} pour {Name(timeline.Subject)}{completeDates}."
                    : $"Lex holds {timeline.TotalCount:n0} {kind} for {Name(timeline.Subject)}{completeDates}.",
                true => fr
                    ? $"Lex a renvoyé {timeline.Rows.Count:n0} sur {timeline.TotalCount:n0} {kind} pour {Name(timeline.Subject)}{returnedDates}. Cette vue bornée est tronquée."
                    : $"Lex returned {timeline.Rows.Count:n0} of {timeline.TotalCount:n0} {kind} for {Name(timeline.Subject)}{returnedDates}. This bounded view is truncated.",
                null => fr
                    ? $"Lex a renvoyé {timeline.Rows.Count:n0} {kind} pour {Name(timeline.Subject)}{returnedDates}. La réponse n'indique pas si la chronologie est complète."
                    : $"Lex returned {timeline.Rows.Count:n0} {kind} for {Name(timeline.Subject)}{returnedDates}. The response does not record whether the timeline is complete.",
            };
            return timelineResult + (fr
                ? " Ce sont des dates éditeur, pas une conclusion sur l'effet juridique."
                : " These are publisher dates, not a conclusion about legal effect.");
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
            var facts = string.Join("; ", search.Results.Take(3)
                .Select(fact => SearchFactLine(fact, fr)));
            return fr
                ? $"Lex a trouvé {search.Results.Count:n0} correspondance(s) bornée(s) dans des dispositions publiées{Scope(fr, search)} : {facts}."
                : $"Lex found {search.Results.Count:n0} bounded publisher-provision match(es){Scope(fr, search)}: {facts}.";
        }
        if (effect.Workspace is { } navigation)
            return fr
                ? $"Lex a ouvert l'espace de recherche{Scope(fr, navigation)}. Les résultats correspondants y sont affichés."
                : $"Lex opened the search workspace{Scope(fr, navigation)}. The matching results are open there.";
        return null;
    }

    /// <summary>
    /// Where a navigation navigated, under the same rule as <see cref="Name"/>.
    ///
    /// <para>This is the one effect that shows no law of its own, so the scope is the whole of its
    /// identity. "The matching results are open" is unfalsifiable: a workspace opened on the wrong
    /// query, in the wrong corpus, over the wrong expression or at the wrong instant reads exactly
    /// like the one the reader asked for, and the reader has no second copy of their own question
    /// to check it against. Every coordinate the directive actually sets is named, including the
    /// filters Lex chose rather than the reader, because a silently narrowed population is the
    /// selection error this clause exists to make correctable in one turn.</para>
    ///
    /// <para>Coordinates only, and never in quotation marks. A navigation quotes nothing, and the
    /// query is the assistant's own words rather than a publisher's; the curly quotes the
    /// provision line uses are the reader's one typographic signal of verified text, so a search
    /// term wearing them would be model output dressed as law.</para>
    /// </summary>
    private static string Scope(bool fr, WorkspaceView workspace)
    {
        var terms = new List<string>(6);
        void Add(string? value, string english, string french)
        {
            if (value is { Length: > 0 })
                terms.Add($"{(fr ? french : english)} {Excerpt(value, 160)}");
        }

        if (workspace.Work is { Length: > 0 } work)
            terms.Add(workspace.Anchor is { Length: > 0 } anchor
                ? (fr ? $"{anchor} dans {work}" : $"{anchor} in {work}")
                : work);
        if (workspace.Query is { Length: > 0 } query)
            terms.Add((fr ? "requête [" : "query [") + Excerpt(query, 160) + "]");
        Add(workspace.Jurisdiction, "jurisdiction", "juridiction");
        Add(workspace.Hierarchy, "hierarchy", "hiérarchie");
        Add(workspace.Domain, "domain", "domaine");
        Add(workspace.SourceClass, "source class", "classe source");
        Add(workspace.ActForm, "act form", "forme");
        Add(workspace.BindingStatus, "binding status", "statut");
        Add(workspace.Language, "language", "langue");
        if (workspace.Date is { Length: > 0 } date)
            terms.Add(fr ? $"au {date}" : $"at {date}");
        return terms.Count == 0 ? "" : (fr ? " pour " : " for ") + string.Join(", ", terms);
    }

    /// <summary>
    /// How one comparison came out, in the reader's language, off the fields <c>diff</c> verified.
    ///
    /// <para>Every other effect states its result: the provision line quotes the text, the ranking
    /// line gives the counts, the history line gives the span. This one used to announce that a
    /// comparison existed and stop, which answers "did you compare?" rather than the question
    /// actually asked. A reader given no outcome reads the silence as "nothing moved", and that is
    /// the single reading Lex must never license.</para>
    ///
    /// <para>What is stated here is exactly what the tool verified and no more. Lex compares the
    /// two stored provision texts by hash, so it can prove THAT the wording differs; it does not
    /// compute the wording delta, and the sentence therefore never characterises the change. The
    /// two publisher versions are named so the reader can go read it.</para>
    /// </summary>
    private static string Comparison(bool fr, DiffView diff)
    {
        var name = Name(diff.Subject);
        var window = fr
            ? $"entre le {diff.FromDate} et le {diff.ToDate}"
            : $"between {diff.FromDate} and {diff.ToDate}";
        var below = fr ? " Les deux versions vérifiées de l'éditeur sont ouvertes ci-dessous."
            : " Both verified publisher versions are open below.";

        if (diff.Subject.Anchor is { Length: > 0 } anchor)
        {
            var provision = ProvisionLabel(anchor);
            // Presence is checked before wording because a provision absent on one side has no
            // wording to compare, and "changed" would be the wrong word for an article that was
            // introduced or repealed.
            if (diff.AnchorFromPresent == true && diff.AnchorToPresent == false)
                return (fr
                    ? $"{provision} de {name} est présent uniquement à la date antérieure ({diff.FromDate}), et non au {diff.ToDate}."
                    : $"{provision} of {name} is present only on the earlier date ({diff.FromDate}), not on {diff.ToDate}.") + below;
            if (diff.AnchorFromPresent == false && diff.AnchorToPresent == true)
                return (fr
                    ? $"{provision} de {name} est présent uniquement à la date ultérieure ({diff.ToDate}), et non au {diff.FromDate}."
                    : $"{provision} of {name} is present only on the later date ({diff.ToDate}), not on {diff.FromDate}.") + below;
            if (diff.AnchorTextEqual == true)
                return (fr
                    ? $"{provision} de {name} a le même libellé au {diff.FromDate} et au {diff.ToDate}."
                    : $"{provision} of {name} has the same wording on {diff.FromDate} and on {diff.ToDate}.") + below;
            if (diff.AnchorTextEqual == false)
                return (fr
                    ? $"{provision} de {name} a un libellé différent au {diff.FromDate} et au {diff.ToDate}. Lex vérifie que les deux textes publiés diffèrent; il ne caractérise pas la modification."
                    : $"{provision} of {name} has different wording on {diff.FromDate} and on {diff.ToDate}. Lex verifies that the two publisher texts differ; it does not characterise the change.") + below;
            // Both sides held the provision but the texts could not be compared, which is a
            // coverage limit rather than an outcome, and saying "changed" here would invent one.
            return (fr
                ? $"Lex ne peut pas comparer le libellé de {provision} de {name} {window}, car le texte publié n'est pas comparable des deux côtés."
                : $"Lex cannot compare the wording of {provision} of {name} {window} because the publisher text is not comparable on both sides.") + below;
        }

        if (diff.Changed == false)
            return (fr
                ? $"La même version éditeur de {name} s'applique au {diff.FromDate} et au {diff.ToDate}."
                : $"The same publisher version of {name} applies on {diff.FromDate} and on {diff.ToDate}.") + below;
        if (diff.Changed == true)
            return (fr
                ? $"Une version éditeur différente de {name} s'applique au {diff.FromDate} et au {diff.ToDate}. Lex n'a pas comparé de disposition précise; indiquez un article pour obtenir un résultat au niveau de la disposition."
                : $"A different publisher version of {name} applies on {diff.FromDate} and on {diff.ToDate}. Lex compared no single provision here; name an article for a provision-level outcome.") + below;
        return fr
            ? $"La comparaison vérifiée de {name} {window} est affichée ci-dessous."
            : $"The verified comparison of {name} {window} is open below.";
    }

    /// <summary>
    /// A reader's name for a provision anchor, and the anchor itself when there is no safe reading.
    ///
    /// <para>Anchors are minted per extraction profile, so only the plain <c>art_92</c> shape can
    /// be turned into "Article 92" without guessing. Anything else is printed verbatim: it is what
    /// <c>search</c> returned and what the reader can check, and inventing a prettier label for a
    /// scheme this code does not own is how a comparison starts describing the parser.</para>
    /// </summary>
    internal static string ProvisionLabel(string anchor) =>
        anchor.StartsWith("art_", StringComparison.Ordinal)
        && System.Text.RegularExpressions.Regex.IsMatch(
            anchor["art_".Length..], "^[0-9]+[a-z]?$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            ? $"Article {anchor["art_".Length..]}"
            : anchor;

    private static string SearchFactLine(SearchFact fact, bool french)
    {
        var provision = fact.Number is { Length: > 0 } number ? number : fact.Anchor;
        if (fact.Heading is { Length: > 0 } heading) provision += $" — {heading}";
        var work = fact.Title is { Length: > 0 } title
            ? $"{title} ({fact.Work})" : fact.Work;
        var separator = french ? "dans" : "in";
        if (fact.Snippet is not { Length: > 0 } snippet)
            return $"{provision} {separator} {work}";
        snippet = string.Join(' ', snippet.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (snippet.Length > 240) snippet = snippet[..239] + "…";
        return $"{provision} {separator} {work}: {snippet}";
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

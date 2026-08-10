namespace Lex.Ask;

internal static class OperationAnswerPolicy
{
    public static string Render(
        string locale,
        IReadOnlyList<OperationResult> results,
        IReadOnlyList<UiEffect> effects)
    {
        var lines = results.OrderBy(result => result.UserOrder)
            .Select(result => RenderOne(locale, result,
                result.UserOrder < effects.Count ? effects[result.UserOrder] : new UiEffect()))
            .Where(line => line.Length > 0)
            .ToArray();
        return string.Join("\n", lines);
    }

    private static string RenderOne(string locale, OperationResult result, UiEffect effect)
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
        return rendered;
    }

    private static string RenderOneCore(string locale, OperationResult result, UiEffect effect)
    {
        var fr = locale == "fr";
        if (result.TransportOutcome != TransportOutcome.Completed)
            return fr
                ? $"Cette opération n'a pas été exécutée: {Transport(fr, result.TransportOutcome)}."
                : $"This operation was not evaluated: {Transport(fr, result.TransportOutcome)}.";
        if (result.LegalOutcome == LegalOutcome.NeedsClarification)
            return fr ? "Lex a besoin d'un instrument précis avant de continuer."
                : "Lex needs a specific instrument before it can continue.";
        if (result.LegalOutcome == LegalOutcome.InvalidRequest)
            return fr ? "Cette demande ne correspond pas à une opération juridique valide."
                : "This request does not map to a valid legal operation.";
        if (result.LegalOutcome == LegalOutcome.LegalBoundary)
            return fr ? "Lex peut fournir le texte vérifié, mais pas un avis juridique."
                : "Lex can provide verified text, but it cannot provide legal advice.";
        if (effect.Gap is { } gap)
            return gap.Explanation;
        if (effect.Ranking is { } ranking)
            return fr
                ? $"Dans un périmètre sélectionné de {ranking.PopulationWorks:n0} instruments, Lex en a trouvé {ranking.WorksChanged:n0} ayant reçu {ranking.NewVersions:n0} dates de version éditeur entre le {ranking.FromDate} et le {ranking.ToDate}. Le classement vérifié est affiché ci-dessous."
                : $"Within a selected population of {ranking.PopulationWorks:n0} works, Lex found {ranking.WorksChanged:n0} instruments with {ranking.NewVersions:n0} publisher version dates between {ranking.FromDate} and {ranking.ToDate}. The verified ranking is open below.";
        if (effect.InForce is { } inForce)
            return fr
                ? $"Lex a trouvé {inForce.Total:n0} états éditeur couvrant le {inForce.Date}. La liste est affichée ci-dessous."
                : $"Lex found {inForce.Total:n0} publisher states covering {inForce.Date}. The list is open below.";
        if (effect.CitedBy is { } cited)
            return fr
                ? $"Lex a trouvé {cited.CitingArticles:n0} article(s) faisant référence à {cited.CitedWork}."
                : $"Lex found {cited.CitingArticles:n0} article(s) referring to {cited.CitedWork}.";
        if (effect.Provision is { } provision)
        {
            if (provision.TextTruncated)
                return fr
                    ? $"Lex détient le texte publié de {Name(provision.Subject)} au {provision.ValidFrom}, mais cette réponse bornée n'en affiche qu'une partie. Les liens officiels sont disponibles ci-dessous."
                    : $"Lex holds the publisher text for {Name(provision.Subject)} at {provision.ValidFrom}, but this bounded response shows only part of it. The official links are available below.";
            return fr
                ? $"Le texte exact publié pour {Name(provision.Subject)} à la date du {provision.ValidFrom} est affiché ci-dessous."
                : $"The exact publisher text for {Name(provision.Subject)} at {provision.ValidFrom} is open below.";
        }
        if (effect.Diff is { } diff)
            return fr
                ? $"La comparaison vérifiée de {Name(diff.Subject)} entre le {diff.FromDate} et le {diff.ToDate} est affichée ci-dessous."
                : $"The verified comparison of {Name(diff.Subject)} between {diff.FromDate} and {diff.ToDate} is open below.";
        if (effect.History is { } history)
            return fr
                ? $"L'historique vérifié de {history.Anchor} contient {history.DistinctTexts:n0} texte(s) distinct(s)."
                : $"The verified history of {history.Anchor} contains {history.DistinctTexts:n0} distinct text(s).";
        if (effect.Timeline is { } timeline)
            return fr
                ? $"La chronologie des versions de {Name(timeline.Subject)} est affichée ci-dessous."
                : $"The version timeline for {Name(timeline.Subject)} is open below.";
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
        if (effect.Workspace is not null)
            return fr ? "Les résultats correspondants sont affichés dans l'espace de recherche."
                : "The matching results are open in the search workspace.";
        return fr ? "L'opération est terminée." : "The operation is complete.";
    }

    private static string Name(Subject subject) => subject.Title ?? subject.Work;

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

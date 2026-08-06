using System.Diagnostics;
using System.Text.Json;

namespace Lex.Index;

public sealed record RetrievalBenchmarkCase(
    string Id,
    string Category,
    string Query,
    string Language,
    string TimeScope,
    string? AsOf,
    IReadOnlyList<string> RelevantWorks,
    string Explanation,
    string ReviewStatus,
    string? Hierarchy = null,
    string? Domain = null);

public sealed record RetrievalMetrics(
    double Mrr,
    double RecallAt10,
    double NdcgAt10,
    double ExactFirstAccuracy,
    int TemporalLeakageFailures,
    double P50Ms,
    double P95Ms,
    double P99Ms);

public sealed record RetrievalBenchmarkReport(
    string Schema,
    string Timestamp,
    int SampleCount,
    string ReviewStatus,
    string CodeCommit,
    string CorpusCommit,
    string ManifestId,
    string ModelId,
    string ModelRevision,
    string Machine,
    string ResourceConfiguration,
    double ModelLoadMs,
    double ColdQueryMs,
    long ProcessMemoryBytes,
    long MemoryLimitBytes,
    long IndexBytes,
    long VectorBytes,
    RetrievalMetrics Keyword,
    RetrievalMetrics Hybrid,
    bool ActivationGatePassed,
    IReadOnlyList<string> GateFailures);

public static class RetrievalBenchmarkCatalog
{
    private sealed record Work(
        string Celex, string Topic, string French, string Hierarchy, string Domain,
        string HistoricalAsOf,
        IReadOnlyList<string> Concepts, IReadOnlyList<string> FrenchConcepts);

    private static readonly Work[] Works =
    [
        new("32013R0575", "capital requirements regulation", "exigences de fonds propres", "secondary_eu_law", "financial-services", "2013-06-28", ["bank capital adequacy", "prudential own funds", "credit institution exposures", "banking risk weights"], ["fonds propres bancaires", "risque de credit"]),
        new("32014L0065", "markets in financial instruments", "marches d instruments financiers", "secondary_eu_law", "financial-services", "2014-09-17", ["investment firm conduct", "trading venue transparency", "investor protection rules"], ["protection des investisseurs", "entreprises d investissement"]),
        new("32014R0910", "electronic identification and trust services", "identification electronique", "secondary_eu_law", "judicial-cooperation", "2014-09-17", ["qualified electronic signature", "digital identity trust", "electronic seal recognition"], ["signature electronique qualifiee", "services de confiance"]),
        new("32015L2366", "payment services directive", "services de paiement", "secondary_eu_law", "financial-services", "2015-12-23", ["strong customer authentication", "payment initiation service", "unauthorised payment liability"], ["authentification forte du client", "paiement non autorise"]),
        new("32016R0679", "general data protection regulation", "protection des donnees", "secondary_eu_law", "judicial-cooperation", "2016-05-04", ["personal data breach notice", "lawful processing consent", "data subject access right", "privacy impact assessment"], ["violation de donnees personnelles", "droit d acces de la personne"]),
        new("32018L2001", "renewable energy directive", "energies renouvelables", "secondary_eu_law", "consumer-environment", "2018-12-21", ["renewable energy target", "guarantee of origin", "renewable self consumer"], ["objectif energie renouvelable", "garantie d origine"]),
        new("32019L0944", "electricity market directive", "marche de l electricite", "secondary_eu_law", "consumer-environment", "2019-06-14", ["electricity consumer switching", "distribution system operator", "dynamic electricity price"], ["changement de fournisseur electricite", "gestionnaire de reseau"]),
        new("32019R2088", "sustainable finance disclosure regulation", "publication finance durable", "secondary_eu_law", "financial-services", "2020-07-12", ["sustainability risk disclosure", "principal adverse impacts", "financial product environmental characteristics"], ["risques en matiere de durabilite", "incidences negatives"]),
        new("32022L2555", "network and information security directive", "securite des reseaux", "secondary_eu_law", "judicial-cooperation", "2022-12-27", ["cyber incident notification", "essential entity security", "supply chain cyber risk"], ["notification incident cyber", "entite essentielle"]),
        new("32022R2065", "digital services act", "services numeriques", "secondary_eu_law", "consumer-environment", "2022-10-27", ["online platform notice action", "systemic platform risk", "illegal online content"], ["contenu illicite en ligne", "tres grande plateforme"]),
        new("32022R2554", "digital operational resilience", "resilience operationnelle numerique", "secondary_eu_law", "financial-services", "2022-12-27", ["ict incident reporting finance", "digital resilience testing", "third party technology risk"], ["incident tic financier", "test de resilience numerique"]),
        new("32023R1114", "markets in crypto assets", "marches de crypto actifs", "secondary_eu_law", "financial-services", "2023-06-09", ["crypto asset white paper", "stablecoin issuer reserve", "crypto service provider authorisation"], ["livre blanc crypto actif", "prestataire de services crypto"]),
        new("32023R2854", "data act", "reglement sur les donnees", "secondary_eu_law", "consumer-environment", "2023-12-22", ["fair access to connected product data", "business to government data sharing", "switching data processing services"], ["acces equitable donnees produit connecte", "changement service traitement donnees"]),
        new("32024R1689", "artificial intelligence act", "reglement intelligence artificielle", "secondary_eu_law", "consumer-environment", "2024-07-12", ["high risk ai system", "prohibited artificial intelligence practice", "general purpose ai model"], ["systeme ia a haut risque", "pratique ia interdite"]),
        new("32024R2847", "cyber resilience act", "reglement cyberresilience", "secondary_eu_law", "consumer-environment", "2024-11-20", ["cybersecurity requirements for connected products", "product vulnerability handling", "security updates for digital products"], ["cybersecurite des produits connectes", "traitement des vulnerabilites"]),
        new("32024R1620", "anti money laundering authority", "autorite lutte blanchiment", "secondary_eu_law", "aml-corporate", "2024-06-19", ["money laundering authority supervision", "aml direct supervision", "financial intelligence coordination"], ["supervision lutte blanchiment", "renseignement financier"]),
        new("32003R0001", "competition rules enforcement", "mise en oeuvre des regles de concurrence", "secondary_eu_law", "competition", "2004-05-01", ["antitrust investigation powers", "competition authority cooperation", "articles 101 and 102 enforcement"], ["pouvoirs enquete concurrence", "cooperation autorites concurrence"]),
        new("32006L0112", "common value added tax system", "systeme commun taxe valeur ajoutee", "secondary_eu_law", "tax", "2007-01-01", ["value added tax taxable transaction", "vat place of supply", "input tax deduction"], ["operation imposable tva", "deduction taxe en amont"]),
        new("32003L0088", "working time directive", "directive temps de travail", "secondary_eu_law", "employment", "2003-11-04", ["maximum weekly working time", "minimum daily rest", "paid annual leave"], ["duree maximale hebdomadaire travail", "conge annuel paye"]),
        new("32014L0024", "public procurement directive", "directive marches publics", "secondary_eu_law", "procurement-and-ip", "2014-03-28", ["public contract award procedure", "procurement exclusion grounds", "most economically advantageous tender"], ["procedure attribution marche public", "motifs exclusion soumissionnaire"]),
        new("32017R1001", "european union trade mark", "marque de l union europeenne", "secondary_eu_law", "procurement-and-ip", "2017-06-16", ["eu trade mark registration", "trade mark infringement remedy", "absolute grounds for refusal"], ["enregistrement marque union", "contrefacon de marque"]),
        new("12012E/TXT", "treaty on the functioning of the european union", "traite fonctionnement union europeenne", "primary_eu_law", "primary-eu-law", "2012-10-26", ["free movement internal market", "competition treaty legal basis", "preliminary ruling jurisdiction"], ["libre circulation marche interieur", "base juridique concurrence"]),
        new("12012M/TXT", "treaty on european union", "traite sur l union europeenne", "primary_eu_law", "primary-eu-law", "2012-10-26", ["union values rule of law", "common foreign security policy", "principle of conferral"], ["valeurs de l union", "principe d attribution"]),
        new("12012P/TXT", "charter of fundamental rights", "charte des droits fondamentaux", "primary_eu_law", "primary-eu-law", "2012-10-26", ["right to effective remedy", "personal data fundamental right", "freedom of expression charter"], ["droit a un recours effectif", "liberte d expression"]),
    ];

    public static IReadOnlyList<RetrievalBenchmarkCase> Create()
    {
        var cases = new List<RetrievalBenchmarkCase>();
        void Add(string category, string query, Work work, string language = "en", string timeScope = "all_versions",
                 string? asOf = null, string? hierarchy = null, string? domain = null) => cases.Add(new(
            $"{category}-{cases.Count(c => c.Category == category) + 1:000}", category, query, language,
            timeScope, asOf, [work.Celex.ToLowerInvariant()],
            $"The query identifies {work.Topic}; the relevant work judgment is document-level and does not invent an article match.",
            "generated-unreviewed", hierarchy, domain));

        for (var i = 0; i < 30; i++)
        {
            var work = Works[i % Works.Length];
            Add("exact", i < Works.Length ? work.Celex : "CELEX " + work.Celex, work);
        }

        for (var i = 0; i < 40; i++)
        {
            var work = Works[i % Works.Length];
            var asOf = i < Works.Length ? "2026-08-06" : work.HistoricalAsOf;
            Add("temporal", $"{work.Topic} rules as of {asOf}", work, timeScope: "as_of", asOf: asOf);
        }

        for (var i = 0; i < 60; i++)
        {
            var work = Works[i % Works.Length];
            Add("conceptual", work.Concepts[(i / Works.Length) % work.Concepts.Count], work);
        }

        for (var i = 0; i < 30; i++)
        {
            var work = Works[i % Works.Length];
            Add("bilingual", work.FrenchConcepts[(i / Works.Length) % work.FrenchConcepts.Count], work, "fr");
        }

        for (var i = 0; i < 20; i++)
        {
            var work = Works[i % Works.Length];
            var phrase = work.Concepts[0];
            var word = phrase.Split(' ').OrderByDescending(w => w.Length).First();
            var typo = word.Length < 5 ? word + "x" : word.Remove(word.Length / 2, 1);
            Add("fuzzy", phrase.Replace(word, typo, StringComparison.Ordinal), work);
        }

        var hierarchyWorks = Works
            .GroupBy(w => $"{w.Hierarchy}|{w.Domain}", StringComparer.Ordinal)
            .Select(g => g.First())
            .Concat(Works)
            .DistinctBy(w => w.Celex, StringComparer.Ordinal)
            .Take(20)
            .ToArray();
        for (var i = 0; i < 20; i++)
        {
            var work = hierarchyWorks[i];
            Add("hierarchy", work.Concepts[0], work, hierarchy: work.Hierarchy, domain: work.Domain);
        }

        var required = new Dictionary<string, int>
        {
            ["exact"] = 30,
            ["temporal"] = 40,
            ["conceptual"] = 60,
            ["bilingual"] = 30,
            ["fuzzy"] = 20,
            ["hierarchy"] = 20,
        };
        foreach (var (category, count) in required)
            if (cases.Count(c => c.Category == category) != count)
                throw new InvalidOperationException($"Benchmark category '{category}' is not {count} cases.");
        if (cases.Count != 200 || cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != 200)
            throw new InvalidOperationException("Retrieval benchmark must contain 200 uniquely identified cases.");
        return cases;
    }
}

public static class RetrievalBenchmarkRunner
{
    public static RetrievalBenchmarkReport Run(
        LexIndexReader reader, string indexPath, string? vectorPath,
        string codeCommit, string manifestId, string machine, string resourceConfiguration,
        long memoryLimitBytes, double modelLoadMs, double coldQueryMs, DateTimeOffset timestamp)
    {
        var cases = RetrievalBenchmarkCatalog.Create();
        _ = reader.SearchHybrid("benchmark warmup", FilterSet.All, 1);
        var keyword = Evaluate(cases, c => reader.SearchKeyword(c.Query, Filters(c), 10, c.Category == "fuzzy"));
        var hybrid = Evaluate(cases, c => reader.SearchHybrid(c.Query, Filters(c), 10));
        var failures = new List<string>();
        if (cases.Any(c => c.ReviewStatus is not ("engineer-reviewed" or "lawyer-reviewed")))
            failures.Add("relevance judgments have not been reviewed");
        if (hybrid.ExactFirstAccuracy < 1) failures.Add("exact legal identifier accuracy is below 100 percent");
        if (hybrid.TemporalLeakageFailures != 0) failures.Add("temporal leakage is not zero");
        var conceptualKeyword = Evaluate(cases.Where(c => c.Category == "conceptual").ToList(),
            c => reader.SearchKeyword(c.Query, Filters(c), 10, false));
        var conceptualHybrid = Evaluate(cases.Where(c => c.Category == "conceptual").ToList(),
            c => reader.SearchHybrid(c.Query, Filters(c), 10));
        if (conceptualKeyword.NdcgAt10 == 0 || conceptualHybrid.NdcgAt10 < conceptualKeyword.NdcgAt10 * 1.10)
            failures.Add("conceptual nDCG@10 did not improve by at least 10 percent");
        if (hybrid.NdcgAt10 + 0.000001 < keyword.NdcgAt10 * 0.98)
            failures.Add("full-suite nDCG@10 regressed by more than 2 percent");
        if (hybrid.P95Ms > 250) failures.Add("warm p95 exceeds 250 ms");
        var workingSet = Process.GetCurrentProcess().WorkingSet64;
        if (memoryLimitBytes <= 0) failures.Add("configured memory limit was not supplied");
        else if (workingSet >= memoryLimitBytes * 0.75) failures.Add("process memory is not below 75 percent of the configured limit");

        return new RetrievalBenchmarkReport(
            "lex-retrieval-benchmark/1", timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            cases.Count, AggregateReviewStatus(cases), codeCommit,
            reader.Stamp.GetValueOrDefault("corpus_commit", "unknown"), manifestId,
            reader.Stamp.GetValueOrDefault("embedding_model", "none"),
            reader.Stamp.GetValueOrDefault("embedding_revision", "none"), machine, resourceConfiguration,
            modelLoadMs, coldQueryMs, workingSet, memoryLimitBytes, new FileInfo(indexPath).Length,
            vectorPath is not null && File.Exists(vectorPath) ? new FileInfo(vectorPath).Length : 0,
            keyword, hybrid, failures.Count == 0, failures);
    }

    private static string AggregateReviewStatus(IReadOnlyList<RetrievalBenchmarkCase> cases)
    {
        if (cases.All(c => c.ReviewStatus == "lawyer-reviewed")) return "lawyer-reviewed";
        if (cases.All(c => c.ReviewStatus is "engineer-reviewed" or "lawyer-reviewed")) return "reviewed";
        return "generated-unreviewed";
    }

    private static FilterSet Filters(RetrievalBenchmarkCase c) => new(
        c.AsOf is null ? null : DateOnly.Parse(c.AsOf), null, null, c.Language,
        null, c.Hierarchy, null, null, c.Domain);

    private static RetrievalMetrics Evaluate(
        IReadOnlyList<RetrievalBenchmarkCase> cases, Func<RetrievalBenchmarkCase, SearchExecution> search)
    {
        var reciprocal = 0d;
        var recall = 0d;
        var ndcg = 0d;
        var exactCorrect = 0;
        var exactCount = 0;
        var leakage = 0;
        var latency = new List<double>(cases.Count);
        foreach (var c in cases)
        {
            var sw = Stopwatch.StartNew();
            var result = search(c);
            sw.Stop();
            latency.Add(sw.Elapsed.TotalMilliseconds);
            var works = result.Hits.Select(h => h.Doc.GroupKey.ToLowerInvariant()).ToList();
            var first = works.FindIndex(w => c.RelevantWorks.Contains(w, StringComparer.Ordinal));
            if (first >= 0) reciprocal += 1d / (first + 1);
            var relevantAt10 = works.Take(10).Distinct(StringComparer.Ordinal)
                .Count(w => c.RelevantWorks.Contains(w, StringComparer.Ordinal));
            recall += (double)relevantAt10 / c.RelevantWorks.Count;
            if (first is >= 0 and < 10) ndcg += 1d / Math.Log2(first + 2);
            if (c.Category == "exact")
            {
                exactCount++;
                if (first == 0) exactCorrect++;
            }
            if (c.AsOf is not null)
                leakage += result.Hits.Count(h => string.CompareOrdinal(h.Doc.ValidFrom, c.AsOf) > 0
                    || h.Doc.ValidTo is not null && string.CompareOrdinal(h.Doc.ValidTo, c.AsOf) < 0);
        }
        latency.Sort();
        double Percentile(double p) => latency.Count == 0 ? 0 : latency[(int)Math.Ceiling(p * latency.Count) - 1];
        return new RetrievalMetrics(
            reciprocal / cases.Count, recall / cases.Count, ndcg / cases.Count,
            exactCount == 0 ? 1 : (double)exactCorrect / exactCount, leakage,
            Percentile(.50), Percentile(.95), Percentile(.99));
    }
}

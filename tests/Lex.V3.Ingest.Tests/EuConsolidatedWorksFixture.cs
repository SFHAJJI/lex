using System.IO.Compression;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The consolidated fixture generalised for the EU time view's tests: the GDPR's original wording and any number of
/// consolidated works, each with its own consolidation date (or none), its own CELEX (or none) and its own English and French
/// packages (or none, so its text is not held in that language). A package may differ from the original's text.
/// </summary>
public sealed partial class EuFirstMountAcquisitionTests
{
    /// <summary>One consolidated work of the census: its IRI, date, CELEX and the package served for each language.</summary>
    internal sealed record ConsolidatedWorkSpec(string WorkIri, string? Date, string? Celex, byte[]? EnglishPackage, byte[]? FrenchPackage);

    /// <summary>The GDPR's English Formex package as the fixture holds it.</summary>
    internal static byte[] GdprEnglishPackage() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", "gdpr-fmx4-200-body.bin"));

    /// <summary>The French synthetic package for a given English one (a language-binding fixture, not a claimed French text).</summary>
    internal static byte[] FrenchOf(byte[] english) => SyntheticFrenchPackage(english);

    /// <summary>
    /// A package whose text differs from <paramref name="bytes"/>: every <paramref name="find"/> in its XML entries replaced. Throws
    /// when nothing changed, so a test that needs two different texts cannot pass on two identical ones.
    /// </summary>
    internal static byte[] RewrittenPackage(byte[] bytes, string find, string replace)
    {
        using var input = new MemoryStream(bytes);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        var changed = false;
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName);
                copy.LastWriteTime = entry.LastWriteTime;
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var text = reader.ReadToEnd();
                var rewritten = text.Replace(find, replace, StringComparison.Ordinal);
                changed |= !string.Equals(text, rewritten, StringComparison.Ordinal);
                using var writer = new StreamWriter(copy.Open(), new UTF8Encoding(false));
                writer.Write(rewritten);
            }

        if (!changed) throw new InvalidOperationException($"The package holds no '{find}' to rewrite.");
        return output.ToArray();
    }

    /// <summary>The census and acquisition of the GDPR's original wording and the consolidated works given.</summary>
    internal static async Task<EuFirstMountAcquisitionResult> AcquireConsolidatedWorksAsync(ICustodyStore store, params ConsolidatedWorkSpec[] consolidated)
    {
        var root = EuAxiomWiringHarness.SeedRoot(ConsolidatedSeed);
        var byWork = consolidated.ToDictionary(static work => work.WorkIri, StringComparer.Ordinal);
        var works = new[] { root }.Concat(consolidated.Select(static work => work.WorkIri)).Order(StringComparer.Ordinal).ToArray();
        var scripts = EuAxiomWiringHarness.Scripts(root, work => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(work));
        var census = consolidated.Select(work => EuAcquisitionTestFixture.CensusFamilyRow(ConsolidatedSeed, root, work.WorkIri)).ToArray();
        scripts["Census"] = EuAcquisitionTestFixture.ScriptFor("Census", census.Length, census, EuAcquisitionTestFixture.CensusFamilyProjection);
        var p = works.SelectMany(work => EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Concat(EuAcquisitionTestFixture.RelationPredicates).Order(StringComparer.Ordinal).Select(predicate =>
            {
                var spec = work == root ? null : byWork[work];
                if (predicate == EuAcquisitionTestFixture.ResourceLegalIdCelex)
                {
                    var celex = spec is null ? ConsolidatedSeed : spec.Celex;
                    return celex is null
                        ? EuAcquisitionTestFixture.ObjectFactRow(work, predicate, null)
                        : EuAcquisitionTestFixture.ObjectFactLiteralRow(work, predicate, celex, "http://www.w3.org/2001/XMLSchema#string");
                }

                if (predicate == EuAcquisitionTestFixture.ActConsolidatedDate && spec is not null)
                    return spec.Date is null
                        ? EuAcquisitionTestFixture.ObjectFactRow(work, predicate, null)
                        : EuAcquisitionTestFixture.ObjectFactLiteralRow(work, predicate, spec.Date, "http://www.w3.org/2001/XMLSchema#date");
                var value = predicate == EuAcquisitionTestFixture.WorkHasResourceType
                    ? spec is null ? EuAcquisitionTestFixture.RegulationResourceType : "http://publications.europa.eu/resource/authority/resource-type/CONSOLID_ACT"
                    : predicate == EuAcquisitionTestFixture.ConsolidatedBasedOnPredicate && spec is not null ? root : null;
                return EuAcquisitionTestFixture.ObjectFactRow(work, predicate, value);
            })).ToArray();
        scripts["P"] = EuAcquisitionTestFixture.ScriptFor("P", p.Length, p, EuAcquisitionTestFixture.ObjectFactsProjection);
        var x = works.SelectMany(work => new[] { (Suffix: ".0001", Language: "ENG"), (Suffix: ".0002", Language: "FRA") }
            .SelectMany(language => new[]
            {
                EuAcquisitionTestFixture.ExpressionFactRow(work, work + language.Suffix),
                EuAcquisitionTestFixture.ExpressionLanguageRow(work, work + language.Suffix,
                    "http://publications.europa.eu/resource/authority/language/" + language.Language),
            })).ToArray();
        scripts["X"] = EuAcquisitionTestFixture.ScriptFor("X", x.Length, x, EuAcquisitionTestFixture.ExpressionFactsProjection);
        var m = works.SelectMany(work => EuAcquisitionTestFixture.RealBandListedTypes.Order(StringComparer.Ordinal)
            .Select(type => EuAcquisitionTestFixture.ManifestationFactsRow(work, type))).ToArray();
        scripts["M"] = EuAcquisitionTestFixture.ScriptFor("M", m.Length, m, EuAcquisitionTestFixture.ManifestationFactsProjection);
        scripts["A"] = EuAcquisitionTestFixture.AxiomAbsenceScriptFor(works);
        scripts["L"] = EuAcquisitionTestFixture.LocatedAmendmentAbsenceScriptFor(works);

        var english = GdprEnglishPackage();
        var french = SyntheticFrenchPackage(english);
        // A language a work holds no package in lists no Formex manifestation for it, so its text is not held there.
        var listed = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var work in works)
        {
            var spec = work == root ? null : byWork[work];
            if (spec is null || spec.EnglishPackage is not null) listed[work + ".0001"] = ["fmx4"];
            if (spec is null || spec.FrenchPackage is not null) listed[work + ".0002"] = ["fmx4"];
        }

        byte[] Body(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var isFrench = path.Contains(".0002.", StringComparison.Ordinal);
            var spec = consolidated.FirstOrDefault(work => path.Contains(work.WorkIri[(work.WorkIri.LastIndexOf('/') + 1)..], StringComparison.Ordinal));
            if (spec is null) return isFrench ? french : english;
            return (isFrench ? spec.FrenchPackage : spec.EnglishPackage)
                ?? throw new InvalidOperationException("The fixture lists no package for " + path);
        }

        var handler = new CompositeHandler(scripts, listed, packageBody: Body);
        var renderers = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
        return await Acquisition(store, handler).RunAsync([ConsolidatedSeed], renderers,
            EuAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
    }
}

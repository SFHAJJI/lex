using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lex.V3.Tests.Contracts.Source.Luxembourg.LuxembourgGazetteBodyFixtures;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// #419 slice 6a: the body ledger. Every population act of a slice-5 coverage gets exactly one
/// outcome - its typed bodies, the typed reason it has none, or the fact that nobody looked - and
/// a set for an act outside the population, or two for one act, refuses the whole ledger.
/// </summary>
[TestClass]
public sealed class LuxembourgNeverConsolidatedBodyLedgerTests
{
    private static readonly string LoiClass = LuxembourgActClassManifest.LoiClassIri;
    private static readonly string RgdClass = LuxembourgActClassManifest.RgdClassIri;
    private const string AminClass = "http://data.legilux.public.lu/resource/authority/resource-type/AMIN";

    [TestMethod]
    public void EveryPopulationActGetsExactlyOneLineAndExclusionsGetNone()
    {
        var coverage = Coverage();
        var sets = new[]
        {
            SetWithBodies(ActLoi1),
            SetWithActGap(ActRgd2),
        };

        var ledger = Fold(coverage, sets);

        // ORDINAL ACT ORDER, not the admission order (a3, a5, a2, a1) and not class order:
        // .../loi/.../a1 < .../loi/.../a3 < .../rgd/.../a2.
        CollectionAssert.AreEqual(
            new[] { ActLoi1, ActLoi3, ActRgd2 },
            ledger.Entries.Select(static e => e.PublisherActIri).ToArray(),
            "one line per population act in ordinal order; the AMIN exclusion has none.");
        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgNeverConsolidatedBodyActOutcome.BodiesDisposed,
                LuxembourgNeverConsolidatedBodyActOutcome.BodyNotDiscovered,
                LuxembourgNeverConsolidatedBodyActOutcome.ActGap,
            },
            ledger.Entries.Select(static e => e.Outcome).ToArray());
        Assert.AreEqual(3, ledger.PopulationActCount);
        Assert.AreEqual(1, ledger.ActsWithBodiesCount);
        Assert.AreEqual(1, ledger.ActGapCount);
        Assert.AreEqual(1, ledger.NotDiscoveredCount);
        Assert.AreEqual(2, ledger.BodyCount);
        Assert.AreEqual(1, ledger.AdmittedBodyCount);
        Assert.AreEqual(1, ledger.RejectedBodyCount);
        Assert.AreEqual(0, ledger.GapBodyCount);
        Assert.IsFalse(ledger.AllPopulationActsDisposed);
        Assert.IsNull(ledger.EntryFor(ActAmin5), "an exclusion has no line.");
        Assert.IsNull(ledger.EntryFor(ActNeverHeld));
        Assert.AreEqual(LuxembourgGazetteActGapReason.NoGazettePdfCandidate, ledger.EntryFor(ActRgd2)!.ActGap);
        Assert.AreEqual(
            "population=3 acts_with_bodies=1 act_gaps=1 not_discovered=1 bodies=2 (admitted=1 rejected=1 gaps=0)",
            ledger.Describe());
    }

    [TestMethod]
    public void ASetForAnActOutsideThePopulationRefusesTheLedger()
    {
        var coverage = Coverage();

        foreach (var outside in new[] { ActAmin5, ActNeverHeld })
        {
            var ledger = LuxembourgNeverConsolidatedBodyLedger.TryComplete(
                coverage, [SetWithBodies(ActLoi1), SetWithBodies(outside)], out var refusal, out var detail);

            Assert.IsNull(ledger, outside);
            Assert.AreEqual(LuxembourgNeverConsolidatedBodyLedgerRefusal.SetForActNotInPopulation, refusal);
            StringAssert.Contains(detail, outside);
        }
    }

    [TestMethod]
    public void TwoSetsForOneActRefuseTheLedger()
    {
        var ledger = LuxembourgNeverConsolidatedBodyLedger.TryComplete(
            Coverage(), [SetWithBodies(ActLoi1), SetWithActGap(ActLoi1)], out var refusal, out var detail);

        Assert.IsNull(ledger);
        Assert.AreEqual(LuxembourgNeverConsolidatedBodyLedgerRefusal.SetDeliveredTwice, refusal);
        StringAssert.Contains(detail, ActLoi1);
    }

    [TestMethod]
    public void TheRefusalNamesEveryOffenderInOrdinalOrderWhateverOrderDeliveredThem()
    {
        var coverage = Coverage();
        var forward = LuxembourgNeverConsolidatedBodyLedger.TryComplete(
            coverage, [SetWithBodies(ActAmin5), SetWithBodies(ActNeverHeld)], out _, out var first);
        var reversed = LuxembourgNeverConsolidatedBodyLedger.TryComplete(
            coverage, [SetWithBodies(ActNeverHeld), SetWithBodies(ActAmin5)], out _, out var second);

        Assert.IsNull(forward);
        Assert.IsNull(reversed);
        Assert.AreEqual(first, second);
        Assert.IsTrue(
            first!.IndexOf(ActAmin5, StringComparison.Ordinal) < first.IndexOf(ActNeverHeld, StringComparison.Ordinal),
            "ordinal: /amin/ before /loi/.");
    }

    [TestMethod]
    public void NotDiscoveredIsNotNoneFound()
    {
        var coverage = Coverage();

        var partial = Fold(coverage, [SetWithActGap(ActLoi1)]);
        Assert.AreEqual(LuxembourgNeverConsolidatedBodyActOutcome.ActGap, partial.EntryFor(ActLoi1)!.Outcome);
        Assert.AreEqual(LuxembourgNeverConsolidatedBodyActOutcome.BodyNotDiscovered, partial.EntryFor(ActRgd2)!.Outcome);
        Assert.IsNull(partial.EntryFor(ActRgd2)!.Set);
        Assert.IsFalse(partial.AllPopulationActsDisposed);

        var complete = Fold(coverage, [SetWithActGap(ActLoi1), SetWithActGap(ActRgd2), SetWithActGap(ActLoi3)]);
        Assert.IsTrue(complete.AllPopulationActsDisposed, "every act has a set, even though none has a body.");
        Assert.AreEqual(0, complete.BodyCount);
    }

    [TestMethod]
    public void TwoDeliveryOrdersAssembleOneLedger()
    {
        var coverage = Coverage();
        var sets = new[] { SetWithBodies(ActLoi1), SetWithActGap(ActRgd2), SetWithBodies(ActLoi3) };

        var forward = Fold(coverage, sets);
        var reversed = Fold(coverage, sets.Reverse().ToArray());

        Assert.AreEqual(forward.Describe(), reversed.Describe());
        CollectionAssert.AreEqual(
            forward.Entries.Select(static e => $"{e.PublisherActIri}|{e.Outcome}").ToArray(),
            reversed.Entries.Select(static e => $"{e.PublisherActIri}|{e.Outcome}").ToArray());
    }

    [TestMethod]
    public void EveryVocabularyMemberHasItsExactWireToken()
    {
        CollectionAssert.AreEqual(
            new[] { "\"none\"", "\"set_for_act_not_in_population\"", "\"set_delivered_twice\"" },
            Enum.GetValues<LuxembourgNeverConsolidatedBodyLedgerRefusal>().Select(static m => ContractJson.Serialize(m)).ToArray());
        CollectionAssert.AreEqual(
            new[] { "\"bodies_disposed\"", "\"act_gap\"", "\"body_not_discovered\"" },
            Enum.GetValues<LuxembourgNeverConsolidatedBodyActOutcome>().Select(static m => ContractJson.Serialize(m)).ToArray());
    }

    [TestMethod]
    public void NullsAreCallerContractViolations()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgNeverConsolidatedBodyLedger.TryComplete(null!, [], out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgNeverConsolidatedBodyLedger.TryComplete(Coverage(), null!, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => Fold(Coverage(), []).EntryFor(null!));
    }

    // ---- fixtures ----

    /// <summary>LOI a1, RGD a2, LOI a3 in the population (unproven, so no proof fixture is needed); AMIN a5 excluded.</summary>
    private static LuxembourgNeverConsolidatedCoverage Coverage()
    {
        var frame = new LuxembourgNeverConsolidatedFrame();
        foreach (var (act, classIri) in new[] { (ActLoi3, LoiClass), (ActAmin5, AminClass), (ActRgd2, RgdClass), (ActLoi1, LoiClass) })
        {
            Assert.IsTrue(frame.TryAdmit(
                new LuxembourgNeverConsolidatedEntry(
                    act, new LuxembourgActClassRef(classIri), LuxembourgNeverConsolidatedDisposition.NoEnumerationCited, null),
                out var refusal), refusal.ToString());
        }

        var coverage = LuxembourgNeverConsolidatedCoverage.TryComplete(frame, out var why, out var detail);
        Assert.IsNotNull(coverage, $"{why} {detail}");
        return coverage!;
    }

    private static LuxembourgNeverConsolidatedBodyLedger Fold(
        LuxembourgNeverConsolidatedCoverage coverage, IReadOnlyList<LuxembourgGazetteBodySet> sets)
    {
        var ledger = LuxembourgNeverConsolidatedBodyLedger.TryComplete(coverage, sets, out var refusal, out var detail);
        Assert.IsNotNull(ledger, $"{refusal} {detail}");
        return ledger!;
    }

    /// <summary>One admitted pdfa body and one rejected (licenceSCL) pdf body.</summary>
    private static LuxembourgGazetteBodySet SetWithBodies(string act)
    {
        var pdfa = Candidate(act, "fr", "pdfa");
        var admittedJoin = JoinAgreedCcBy(act, pdfa);
        var admittedListing = LuxembourgGazetteBodySet.GazetteCandidatesOf(admittedJoin).Single();
        var admitted = LuxembourgGazetteBodyDisposition.Create(admittedListing, Retention(admittedListing));

        var pdf = Candidate(act, "fr", "pdf");
        var rejectedJoin = JoinLicenceScl(act, pdf);
        var rejected = LuxembourgGazetteBodyDisposition.Create(
            LuxembourgGazetteBodySet.GazetteCandidatesOf(rejectedJoin).Single(), null);

        // One join listing both, so the set is complete over it. Rights are per manifestation, so
        // the two channels' observations name each listing with its own licence.
        var join = LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(pdfa, pdf),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration, [Sparql(pdfa.ManifestationIri, CcBy40), Sparql(pdf.ManifestationIri, LicenceScl)]),
            new LuxembourgInFileRightsChannelObservations(
                Run, InFileEnumeration, [InFileRead(pdfa.ManifestationIri, CcBy40), InFileRead(pdf.ManifestationIri, LicenceScl)]));
        var listings = LuxembourgGazetteBodySet.GazetteCandidatesOf(join);
        var bodies = listings.Select(l => l.WemiCandidate.FormatIri == FormatPdfA
            ? LuxembourgGazetteBodyDisposition.Create(l, Retention(l))
            : LuxembourgGazetteBodyDisposition.Create(l, null)).ToArray();
        Assert.AreEqual(admitted.Outcome, bodies.Single(static b => b.Format == Lex.V3.Contracts.Source.Http.LuxembourgUserFormatToken.PdfA).Outcome);
        Assert.AreEqual(rejected.Outcome, bodies.Single(static b => b.Format == Lex.V3.Contracts.Source.Http.LuxembourgUserFormatToken.Pdf).Outcome);
        return LuxembourgGazetteBodySet.Create(join, bodies);
    }

    /// <summary>Tuples exist (an xml listing) but no Gazette PDF.</summary>
    private static LuxembourgGazetteBodySet SetWithActGap(string act) =>
        LuxembourgGazetteBodySet.Create(JoinAgreedCcBy(act, Candidate(act, "fr", "xml")), []);
}

using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Stage 1 acceptance canary for the Union acquisition route, the EU twin of
/// <c>LuxembourgCodeCivilAcquisitionCanary</c>. Its standard is NOT parity with any deployed
/// service, which stays withdrawn: every object in the closure is either Held with a real receipt
/// or refused for exactly one of the four legitimate reasons, stated per object, with the accepted
/// fraction reported as a number.
/// </summary>
/// <remarks>
/// <para>
/// OPT IN, AND DELIBERATELY SO. It sends real requests to publications.europa.eu, so it is skipped
/// unless LEX_EU_CANARY=1 is set. A network test that ran by default would make the suite depend on
/// a third party's uptime and would send traffic nobody asked for. The test EXISTING and being
/// GATED is a fact about this repository; it is not evidence that any run passed, and the two are
/// reported separately.
/// </para>
/// <para>
/// It uses the REAL <see cref="FileSystemCustodyStore"/>, never a synthetic one: a canary that
/// proved bodies were held against a synthetic store would prove nothing. That store publishes
/// CustodyVerificationProfile.FileSystemUnenforced1 with CustodyProtection.NotEnforced, so every
/// artifact this run holds is RetainedUnenforced and says so, which is what Decision 71's
/// interpretation (RULING lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8 and its
/// gate extension lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c) made possible.
/// Before those, this run refused before its first product request.
/// </para>
/// <para>
/// A TEST ASSEMBLED RUN, not a production path. Per RULING
/// lex-event-20260904T231236855Z-8c7a540fc4d2420f859f9d92fdfc733a, no shipped code chooses a
/// production implementation for any door <see cref="EuQueryExecutionAdapter.RunAsync"/> takes:
/// nothing in src constructs either acquisition adapter, and the first assembled production run is
/// Stage 6 R6-01. So every statement about which implementation a door uses is about THIS TEST's
/// assembly. Door by door: the custody store is the production
/// <see cref="FileSystemCustodyStore"/>; the executor is the production
/// <see cref="EuRepeatedEnumerationExecutor"/> on its live constructor; the plans
/// (<see cref="EuConsolidationDiscoveryPlan"/>, <see cref="EuObjectFactsDiscoveryPlan"/>) and the
/// bound requests through their own binders are production types; the renderer sources are
/// production <see cref="MachineQueryRendererSource"/> values minted from bytes this test retains;
/// and the evidence resolver has NO EU PRODUCTION IMPLEMENTATION AT ALL, which is residue R0.
/// </para>
/// <para>
/// THE BOUNDARY THIS RUN DOES NOT CROSS, stated as plainly as the LU canary states its own. The
/// evidence resolver is <see cref="CanaryPermissiveEvidenceResolver"/>, a TEST DOUBLE with the same
/// behaviour as the adapter tests' own. Its <c>IsSelectorObservationAdmitted</c>,
/// <c>IsSelectorNotApplicableAdmitted</c> and <c>IsRuleEvaluationAdmitted</c> admit ON SHA-256 SHAPE
/// ALONE: they check that the binding's digest fields are 64 lowercase hex characters and nothing
/// else. Its <c>IsCompleteEnumerationAdmitted</c> compares against the ref THE CALLER HANDED ITS
/// CONSTRUCTOR. So THIS RUN DOES NOT PROVE THE REDUCTION STEP. Any sentence saying the manifest was
/// reduced from the enumeration carries that qualification. What the run checks about the reduction
/// is its OUTPUT, against an independent census obtained outside this lane, which is a materially
/// different and weaker claim than a proven admission policy.
/// </para>
/// <para>
/// CONDITION TWO, AND WHAT IS ACTUALLY ASSERTED TODAY. The census of RULING
/// lex-event-20260904T232128757Z-e21b4aedbfc4412dba8e6533ab2499d0, measured at the endpoint outside
/// this lane, is carried as DATA on <see cref="Census"/>, per type, rather than as prose: the GDPR
/// at 24 expressions and 72 manifestations with fmx4 24, pdfa1a 24 and xhtml 24; 32003L0088 at 23
/// expressions and 71 manifestations with fmx4 1, html 20, pdf 22, pdfa1a 1, print 23 and xhtml 4.
/// The second seed is IRREGULAR, so nothing here assumes one manifestation of each type per
/// expression; that assumption would pass on the GDPR and be wrong on the other seed.
/// </para>
/// <para>
/// WHAT THIS TEST ASSERTS, today, as opposed to what it discusses. That the census table's own per
/// type counts sum to its row totals. That both census families PROVED and that each carries
/// <see cref="CustodyMembership.RetainedUnenforced"/>. That the manifest was written AND reopened,
/// the two digests being distinct. That the record set was written and every record is held with
/// its custody class and membership, or typed by reason and named. That every MINTED manifest row
/// carries a row, held or typed. That expressions match the census PER ROOT WORK and that the per
/// root and per state counts sum to the closure's distinct total. And that the manifestation TYPE
/// SET per root work equals the census's, taken from family M's own listing. WHEN THE RUN DOES NOT
/// REACH THE MANIFEST IT CALLS <c>Assert.Fail</c> WITH THE REFUSAL IN WORDS.
/// </para>
/// <para>
/// AND AT D1-05g IT PASSES. It did not for the whole of D1-05f, and the superseded sentence here
/// named resource_legal_type as the predicate that stopped it, which was itself the defect: the
/// act form is read from work_has_resource-type. Re-read the run's own wholeRunRefusalCode rather
/// than this paragraph, which is a summary and will age again.
/// </para>
/// <para>
/// ONE COMPARISON THE CENSUS MAKES POSSIBLE IS NOT YET ASSERTABLE, and it is failed loudly rather
/// than discussed. Family M as this route queries it returns the DISTINCT manifestation types per
/// work, not one row per manifestation, so the run observes three tokens for the GDPR where the
/// census counts 72 manifestations. Asserting 72 against a number this route cannot produce would
/// be a fabricated check, so the totals are carried as data and the gap is stated by a failing
/// assertion rather than by a sentence a reader may not reach. D1-05g SETTLED IT: the comparison
/// is over the TYPE SET, because family M lists manifestation types per Work and emits no per
/// format row, so there is no row to count. The per type counts stay recorded as the publisher's
/// inventory, and counting that inventory is residue R8.
/// </para>
/// <para>
/// The fetch half is separately corroborated by RULING
/// lex-event-20260904T232443443Z-e3a024f2bda04e1ba90ae14a02848068: three independent observations of
/// the publisher agree byte for byte on 32003L0088's text/html body,
/// 0d23ad4953be900de8a614fea4022aa46086e0bdc2fdfd6d0fde0cd84429e4b6 at 37,616 bytes. The PUBLISHER
/// is the authority; that agreement is evidence this route's fetch is correct and that the pinned
/// constant is the publisher's real bytes rather than a value recorded once and never rechecked. It
/// proves nothing about the reduction step, nothing about custody, and nothing about the other seed.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuStageOneAcquisitionCanary
{
    private const string EnableVariable = "LEX_EU_CANARY";

    private const string Gdpr = "32016R0679";
    private const string WorkingTime = "32003L0088";

    /// <summary>
    /// The independent census, RULING lex-event-20260904T232128757Z-e21b4aedbfc4412dba8e6533ab2499d0,
    /// measured at the endpoint outside this lane. Carried as DATA, per type, so the comparison is a
    /// check rather than a sentence. Manifestations is the row total and must equal the sum of the
    /// per type counts, which the test asserts of the table itself before using it.
    /// </summary>
    private static readonly (string Celex, int Expressions, int Manifestations,
        (string Token, int Count)[] Types)[] Census =
    [
        (Gdpr, 24, 72, [("fmx4", 24), ("pdfa1a", 24), ("xhtml", 24)]),
        (WorkingTime, 23, 71,
            [("fmx4", 1), ("html", 20), ("pdf", 22), ("pdfa1a", 1), ("print", 23), ("xhtml", 4)]),
    ];

    [TestMethod]
    public async Task TheCensusFamiliesProveAndTheRunEitherReachesTheManifestOrFailsNamingWhy()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Live publisher canary. Set {EnableVariable}=1 to run it; it is skipped by default "
                + "so the suite does not depend on a third party's uptime or send unasked traffic.");
            return;
        }

        foreach (var (celex, expressions, manifestations, types) in Census)
        {
            // The table checks itself before anything is compared against it: a census whose per
            // type counts do not sum to its own row total is a transcription error, and finding that
            // out here is better than finding it out as a mismatch against the run.
            Assert.AreEqual(
                manifestations,
                types.Sum(static entry => entry.Count),
                $"{celex}'s census per type counts must sum to its own manifestation total.");
            Console.WriteLine(
                $"CANARY|census|{celex}|expressions={expressions}|manifestations={manifestations}|"
                + string.Join(",", types.Select(static entry => $"{entry.Token}={entry.Count}")));
        }

        // Retained where a later slice can read it. LEX_EU_CANARY_ROOT lets a run keep its
        // custody store somewhere durable; the default stays under the system temp directory so no
        // path literal is committed.
        var root = Environment.GetEnvironmentVariable("LEX_EU_CANARY_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "lex-eu-canary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine($"CANARY|custodyRoot|{root}");

        var store = new FileSystemCustodyStore(root);
        var executor = new EuRepeatedEnumerationExecutor(store, TimeProvider.System);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var seeds = EuAppendixASeedMap.SeedsInCelexOrder
            .Where(seed => seed.Celex == Gdpr || seed.Celex == WorkingTime)
            .ToArray();
        Assert.HasCount(2, seeds, "both canary seeds must be Appendix A members.");

        var roots = seeds
            .Select(seed => EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
                ?? throw new AssertFailedException($"{seed.Celex}'s own seed root failed to canonicalize."))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        foreach (var (seed, index) in seeds.Select(static (seed, index) => (seed, index)))
        {
            Console.WriteLine($"CANARY|seed|{index}|{seed.Celex}|{seed.WorkRoot}");
        }

        var censusRequests = seeds
            .Select(seed =>
            {
                var (plan, planId) = EuAcquisitionTestFixture.BuildCensusPlan();
                return (
                    Request: new EuCensusPartitionRunRequest(
                        plan, planId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(6100)),
                    Witness: EuAcquisitionTestFixture.SourceWitness());
            })
            .ToArray();

        // D1-05g: the canary supplies the PLAN and the POLICY and never the object list. It used
        // to build four requests over `roots`, which is why family P was only ever asked about the
        // two seed roots while the decoder walked root plus every state the census discovered.
        var (objectFactsPlan, objectFactsPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var objectFactsPolicy = new EuObjectFactsBatchPolicy(
            objectFactsPlan,
            objectFactsPlanId,
            EuAcquisitionTestFixture.BuildRendererSource(6200),
            EuAcquisitionTestFixture.SourceWitness());

        var completeEnumerationRef = new SourceArtifactRef(
            $"urn:uuid:{Guid.NewGuid():D}",
            System.Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("eu-canary-complete-enumeration"))));

        var result = await adapter.RunAsync(
            censusRequests,
            objectFactsPolicy,
            EuAcquisitionTestFixture.BuildRendererSource(6300),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(6400),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new CanaryPermissiveEvidenceResolver(completeEnumerationRef),
            CancellationToken.None);

        Console.WriteLine($"CANARY|refusal|{result.Refusal?.Code}|{result.Refusal?.Detail}");
        Console.WriteLine($"CANARY|completion|{result.Completion}");
        Console.WriteLine($"CANARY|observedObjects|{result.ObservedObjectCount}");
        Console.WriteLine($"CANARY|observedExpressions|{result.ObservedExpressionCount}");
        Console.WriteLine($"CANARY|decodeRefusal|{result.DecodeRefusal}|{result.DecodeOffendingIri}");

        foreach (var outcome in result.FamilyOutcomes)
        {
            // Every field of the executor's refusal, because this run's retained evidence is the
            // only place D1-05f's scoping inputs exist and whoever opens it should not have to
            // re-run a live enumeration to find out what it is fixing. OffendingKey carries the
            // cursor that did not advance; ResponseBodySha256 carries the digest of a malformed
            // page body, whose bytes are under the custody root printed above.
            var refusal = outcome.ExecutorRefusal;
            Console.WriteLine(
                $"CANARY|family|{outcome.FamilyKey}|{outcome.Kind}|floor={outcome.RetainedFloor}|"
                + $"executorRefusal={refusal?.Code}|proof={outcome.ProofRefusal}|"
                + $"offendingKey={refusal?.OffendingKey}|"
                + $"responseBodySha256={refusal?.ResponseBodySha256}|"
                + $"terminalStatus={refusal?.TerminalStatus}|"
                + $"observedMediaType={refusal?.ObservedMediaType}|"
                + $"observedCount={refusal?.ObservedCount}|"
                + $"requestOrdinal={refusal?.RequestOrdinal}|"
                + $"attemptOrdinalReached={refusal?.AttemptOrdinalReached}|"
                + $"detail={refusal?.CoreRefusalDetail}");
        }

        if (result.DocumentAcquisitionOutcomesByOrdinal is { } bodies)
        {
            var held = 0;
            foreach (var (ordinal, outcome) in bodies.OrderBy(static entry => entry.Key))
            {
                var floor = outcome.Receipt is null
                    ? null
                    : (CustodyMembership?)CorpusBodyRecord.Held(outcome.Receipt).Floor;
                Console.WriteLine(
                    $"CANARY|body|{ordinal}|held={outcome.Receipt is not null}|floor={floor}|"
                    + $"digest={outcome.Receipt?.Reference.ContentSha256}|"
                    + $"bytes={outcome.Receipt?.Reference.ByteLength}|refusal={outcome.Refusal}");
                if (outcome.Receipt is not null)
                {
                    held++;
                }
            }

            var fraction = bodies.Count == 0 ? 0d : (double)held / bodies.Count;
            Console.WriteLine($"CANARY|acceptedFraction|{held}/{bodies.Count}|{fraction:F4}");
        }

        if (result.DocumentLadderResultsByOrdinal is { } ladders)
        {
            foreach (var (ordinal, ladder) in ladders.OrderBy(static entry => entry.Key))
            {
                Console.WriteLine(
                    $"CANARY|ladder|{ordinal}|attempted={string.Join(",", ladder.Attempted)}|"
                    + $"served={ladder.Served}");
            }
        }

        if (result.CorpusRecordSet is { } set)
        {
            var records = set.Set.Records;
            Console.WriteLine(
                $"CANARY|records|{records.Count}|"
                + $"held={records.Count(static r => r.Body.Kind == CorpusBodyRecordKind.Held)}");
            foreach (var record in records)
            {
                Console.WriteLine(
                    $"CANARY|record|{record.ObjectRef.CanonicalKey}|{record.Body.Kind}|"
                    + $"floor={record.Body.Floor}|notHeld={record.Body.NotHeldReason}|"
                    + $"pending={record.Body.PendingAcquisitionReason}");
            }
        }

        await WriteEvidenceIndexAsync(store, root, result, CancellationToken.None);

        // CONDITION TWO, first half, reachable today and asserted unconditionally: EACH CENSUS
        // SEED's OWN family proved, BY KEY, and says which of the three custody classes its run was.
        //
        // By key rather than by count. An arity check passes when the right number of families prove
        // whether or not they are these two, and it FAILS the moment a third family proves, which is
        // precisely what D1-05f is meant to achieve. This assertion grows correct instead.
        // A LOOKUP RATHER THAN A DICTIONARY, and the reason is a real crash this canary hit the
        // moment D1-05f started working. FamilyKey is a BATCH PARTITION key, not a query-set key,
        // so all four object-facts sets report the SAME key with different row counts and different
        // canonical key digests. While those sets were refusing, only the two census families
        // proved and the keys happened to be unique; once they proved, ToDictionary threw
        // ArgumentException on the duplicate. That broke this file's own promise, which is that
        // every path through it either proves something or FAILS IN WORDS: it failed in a LINQ
        // stack trace instead, above every assertion below and telling a reader nothing about the
        // run. The lookup keeps the by-key assertion the comment above argues for.
        var proven = result.FamilyOutcomes
            .Where(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven)
            .ToLookup(static outcome => outcome.FamilyKey, StringComparer.Ordinal);
        var everyOutcome = string.Join("; ", result.FamilyOutcomes.Select(
            static outcome => $"{outcome.FamilyKey} {outcome.Kind} {outcome.ExecutorRefusal?.Code}"));

        foreach (var row in Census)
        {
            var familyKey = CensusFamilyKey(row.Celex);
            var matches = proven[familyKey].ToArray();

            // Exactly one, asserted rather than assumed. A census key is minted per CELEX, so a
            // second outcome under one census key would mean two runs of one seed's own family
            // collapsed into one answer, which is worth a named failure rather than a Single()
            // throwing out of LINQ the way the dictionary did.
            Assert.HasCount(
                1,
                matches,
                $"{row.Celex}'s own census family {familyKey} must be among the proved families "
                + $"exactly once. Outcomes were: {everyOutcome}");
            Assert.AreEqual(
                CustodyMembership.RetainedUnenforced,
                matches[0].RetainedFloor,
                $"{row.Celex}'s family ran over a filesystem store and must say so.");
        }

        // Either the run reached the manifest and the census comparison happens, or it did not and
        // this FAILS IN WORDS. There is no third path and none of them is a silent pass.
        if (result.Refusal is { } wholeRunRefusal)
        {
            Assert.Fail(
                "the run did not reach the manifest, so the census comparison never ran. It refused as "
                + $"{wholeRunRefusal.Code}: {wholeRunRefusal.Detail} Per family: "
                + string.Join("; ", result.FamilyOutcomes.Select(
                    static outcome => $"{outcome.FamilyKey} {outcome.Kind} "
                        + $"{outcome.ExecutorRefusal?.Code} body={outcome.ExecutorRefusal?.ResponseBodySha256}"))
                + " This is D1-05f.");
        }


        // ---- D1-05g: THE THREE STEPS THE ACCEPTANCE NEVER CHECKED. ----
        //
        // The manifest write and reopen, the record set write, and the body acquisition all worked
        // against live data before this block existed, and they worked SILENTLY: the canary's
        // assertions stopped at the type set comparison, so a run could have gone green having
        // held nothing at all, which is the one thing the product exists to do. These assertions
        // close that, and they make the acceptance stronger rather than weaker.

        // The manifest went to custody AND came back. The receipt digest and the canonical digest
        // being DISTINCT is the pair that proves a reopen rather than a write: the receipt names
        // the stored bytes, the canonical digest names the manifest's own content address.
        Assert.IsNotNull(result.ScopeManifestReceipt, "the manifest must have been written.");
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(result.ScopeManifestCanonicalSha256),
            "the manifest must carry its own canonical digest.");
        Assert.AreNotEqual(
            result.ScopeManifestReceipt!.Reference.ContentSha256,
            result.ScopeManifestCanonicalSha256,
            "the receipt digest and the manifest's canonical digest are the same value, so nothing "
                + "here distinguishes a reopen from a write.");

        // The record set was written and is addressable by its own content.
        Assert.IsNotNull(result.CorpusRecordSetRef, "the corpus record set must have been written.");
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(result.CorpusRecordSetRef!.Sha256),
            "the record set must be addressable by its own canonical digest.");
        Assert.IsNotNull(result.CorpusRecordSet);

        // EVERY RECORD IS HELD WITH ITS EVIDENCE OR TYPED BY REASON. The RULE is asserted and the
        // number falls out of it: pinning "four of eight" would fix a count nobody has explained,
        // and the run that produced it showed the assignment of bodies to manifest ORDINALS is not
        // stable across runs, so a count keyed to position would be true of one run and false of
        // the next. This is keyed to the RECORD, which is stable.
        var heldKeys = new List<string>();
        var typedKeys = new List<string>();
        foreach (var record in result.CorpusRecordSet!.Set.Records)
        {
            var key = record.ObjectRef.CanonicalKey;
            if (record.Body.Kind == CorpusBodyRecordKind.Held)
            {
                Assert.IsNotNull(
                    record.Body.Floor,
                    $"{key} is held, so it must say which custody class its bytes were retained "
                        + "under.");
                Assert.AreEqual(
                    CustodyMembership.RetainedUnenforced,
                    record.Body.Floor,
                    $"{key} ran over a filesystem store and must say so.");
                heldKeys.Add(key);
                continue;
            }

            Assert.IsTrue(
                record.Body.NotHeldReason is not null || record.Body.PendingAcquisitionReason is not null,
                $"{key} holds no body and gives NO REASON. A record that is neither held nor typed "
                    + "is a silent absence, and a reader cannot tell it from a body we simply "
                    + "failed to fetch.");
            typedKeys.Add(key);
        }

        Assert.AreEqual(
            result.CorpusRecordSet.Set.Records.Count,
            heldKeys.Count + typedKeys.Count,
            "every record must be either held or typed; these are the only two dispositions.");
        Assert.IsTrue(
            heldKeys.Count > 0,
            "no record held a body at all. The run reached the record set, so a corpus with "
                + "nothing held in it is the failure this acceptance exists to catch.");

        // EVERY MINTED ROW CARRIES A ROW, held or typed. An ABSENT row is the one answer a reader
        // cannot act on, and it is what this replaces: the index used to emit a row only for
        // ordinals a fetch was attempted for, so a row the body axis excluded simply vanished.
        // The RULE is asserted and the number falls out; pinning "four of eight" would turn an
        // unexplained state into a permanent expectation, and the ordinal a body lands on is not
        // even stable across runs.
        Assert.IsNotNull(result.MintedRowsByOrdinal);
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.IsTrue(
            result.MintedRowsByOrdinal!.Count > 0,
            "a run that reached the record set minted at least one manifest row.");

        foreach (var (ordinal, accounting) in result.MintedRowsByOrdinal)
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(accounting.CanonicalKey),
                $"minted row {ordinal} must name the object it is about.");

            var hasOutcome = result.DocumentAcquisitionOutcomesByOrdinal!.TryGetValue(
                ordinal, out var outcome);
            if (!accounting.SelectedByBodyAxis)
            {
                Assert.IsFalse(
                    hasOutcome,
                    $"{accounting.CanonicalKey} was not selected by the body axis, so no fetch can "
                        + "have been attempted for it.");
                continue;
            }

            Assert.IsTrue(
                hasOutcome,
                $"{accounting.CanonicalKey} was selected by the body axis and carries no outcome "
                    + "at all, which is the silent absence this rule forbids.");
            Assert.IsTrue(
                outcome!.Receipt is not null || outcome.Refusal is not null,
                $"{accounting.CanonicalKey} carries neither a receipt nor a reason.");
            if (outcome.Receipt is not null)
            {
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(outcome.Receipt.Reference.ContentSha256),
                    $"{accounting.CanonicalKey} is held and must carry its digest.");
                Assert.IsTrue(
                    outcome.Receipt.Reference.ByteLength > 0,
                    $"{accounting.CanonicalKey} is held and must carry its byte length.");

                // AND WHAT THE HOLD MEANS, which the digest and the length do not say. Asserted on
                // the receipt because that is what the index row is built from, so a row that
                // stopped carrying either field would have to stop carrying it here first.
                Assert.AreEqual(
                    CustodyClass.NightlyFloor90d,
                    outcome.Receipt.Reference.CustodyClass,
                    $"{accounting.CanonicalKey} is held and must state the storage class it was "
                        + "written under, which is the field the Luxembourg index carries per "
                        + "expression and the one that makes the two indexes comparable.");
                Assert.AreEqual(
                    CustodyMembership.RetainedUnenforced,
                    CustodyMembershipClassifier.Classify(outcome.Receipt),
                    $"{accounting.CanonicalKey} ran over a filesystem store, so Decision 71's own "
                        + "distinction must read retained_unenforced rather than floored. A held "
                        + "row that cannot say which is the silence this field exists to remove.");
            }
        }

        // ---- D1-05g: the expression comparison is PER ROOT WORK. ----
        // ObservedExpressionCount is the whole closure, roots AND the consolidated states this
        // run's own census discovered, which is the right number for the manifest and the wrong
        // one to compare against a census of ROOT Works. They agreed only while family X was asked
        // about roots alone. The states' expressions are REPORTED beside the comparison rather
        // than dropped: they are real Works this run acquired, and discarding them to make a total
        // line up would be the fabrication this split exists to avoid.
        Assert.IsNotNull(
            result.ObservedExpressionsByCelex,
            "a run that reached the manifest must carry its expressions split by closure position.");

        foreach (var row in Census)
        {
            Assert.IsTrue(
                result.ObservedExpressionsByCelex!.TryGetValue(row.Celex, out var split),
                $"{row.Celex} has no expression split at all.");
            Assert.AreEqual(
                row.Expressions,
                split!.OfRootWork,
                $"{row.Celex}'s own ROOT WORK must carry exactly the census's expression count. "
                + $"Its consolidated states carried a further {split.OfConsolidatedStates}, which "
                + "is an observation this run made and not part of this comparison.");
        }

        // THE ARITHMETIC IDENTITY, ASSERTED RATHER THAN ONLY WRITTEN TO THE INDEX. The split is
        // only trustworthy if the parts add up to the whole, and this is the ONE check that would
        // catch an expression counted under BOTH a root and one of its states: double counting
        // leaves every per-root number looking right while the total silently disagrees.
        var splitTotal = result.ObservedExpressionsByCelex!.Values
            .Sum(split => split.OfRootWork + split.OfConsolidatedStates);
        Assert.AreEqual(
            result.ObservedExpressionCount,
            splitTotal,
            "the per root and per state expression counts must sum to the closure's own DISTINCT "
                + "total. They disagree, which means at least one expression was counted under "
                + "both a root and a state, or one was counted under neither.");


        // ---- D1-05g: the two witness facts, asserted from THIS RUN'S OWN terminations. ----
        // Both were measured from retained bytes before being asserted, so neither is a guess
        // about what the endpoint might do.
        Assert.IsNotNull(
            result.WitnessTerminations,
            "a run that reached the manifest ran the witness and must carry its terminations.");
        Assert.IsNotNull(result.WatermarkWitnessPlan);

        var terminations = result.WitnessTerminations!;
        var bound = result.WatermarkWitnessPlan!.StartPosition;

        // ONE: THE BOUNDARY GROUP IS ACCOUNTED EXACTLY ONCE, WHICH MEANS NOT AGAIN.
        //
        // THIS ASSERTION WAS WRONG THE FIRST TIME AND THE RUN CORRECTED IT. It required an entry to
        // SIT ON the bound, reasoning that the boundary rule re-reads that watermark inclusively so
        // the group must reappear. The run delivered one entry and it was not the boundary one, and
        // the code is right: the previous cut ENDED at that position and retained the group sharing
        // its watermark, so re-reading it proves nothing was skipped while the crossing accounts it
        // through RetainedTieSet and CarriedForward rather than TERMINATING it a second time. An
        // entry on the bound appearing in THIS cut's terminations would be the double accounting
        // the crossing exists to prevent, so the correct assertion is the opposite of the first.
        //
        // Measured: bound 2024-12-31T20:10:26.804+01:00 at cellar/3e485e15, one entry delivered.
        Assert.IsFalse(
            terminations.Any(entry => string.Equals(
                entry.Entry.CanonicalEntryKey, bound.CanonicalEntryKey, StringComparison.Ordinal)
                && string.Equals(
                    entry.Entry.WatermarkLexical, bound.WatermarkLexical, StringComparison.Ordinal)),
            "the entry ON the bound was terminated again in this cut. The previous cut ended at "
            + "that position and accounted it; the inclusive re-read is there to prove nothing was "
            + "skipped, not to account it twice.");

        Assert.AreEqual(
            terminations.Select(entry => entry.Entry.CanonicalEntryKey)
                .Distinct(StringComparer.Ordinal).Count(),
            terminations.Count,
            "every entry the witness delivered must be accounted exactly once across the whole "
            + "traversal, which is what the boundary crossing proves and what re-reading a tie "
            + "group inclusively puts at risk.");

        // TWO: THE POST BOUND CHANGE IS DELIVERED. This state's watermark is later than either
        // root's, which is the whole reason the witness watches the closure rather than the roots:
        // a roots-only witness would have been blind to it while reporting a clean cut.
        const string movedState =
            "http://publications.europa.eu/resource/cellar/5f2552c2-cc45-11e6-ad7c-01aa75ed71a1";
        var moved = terminations
            .Where(entry => string.Equals(
                entry.Entry.CanonicalEntryKey, movedState, StringComparison.Ordinal))
            .ToArray();
        Assert.AreEqual(
            1,
            moved.Length,
            "the post-bound change on GDPR's consolidated state was not delivered. Entries "
            + $"delivered were: {string.Join(", ", terminations.Select(
                entry => entry.Entry.CanonicalEntryKey + "@" + entry.Entry.WatermarkLexical))}");
        Assert.IsTrue(
            string.CompareOrdinal(moved[0].Entry.WatermarkLexical, bound.WatermarkLexical) > 0,
            $"the moved state's watermark {moved[0].Entry.WatermarkLexical} must be later than the "
            + $"bound {bound.WatermarkLexical}, or it is not the post-bound change this asserts.");

        // ---- D1-05g: THE TYPE SET COMPARISON, PER ROOT WORK. ----
        // This replaces an unconditional Assert.Fail that said the per type totals were not
        // comparable. They are not, and that has been settled rather than worked around: family M
        // lists manifestation TYPES per Work and emits NO PER FORMAT ROW, so there is no row to
        // count and a count comparison would have to invent one. The census per type counts stay
        // recorded as the publisher's inventory and the comparison is over type SETS. Counting the
        // inventory needs its own acquisition and is residue R8.
        //
        // WHY THIS IS EQUALITY AND NOT CONTAINMENT. A directional form was ruled and then withdrawn
        // as moot, and the reason is worth keeping: the run once listed pdfa2a where the census
        // does not, which looked like the publisher having added a format since the census was
        // taken. It was not. pdfa2a was a RUNG OF OUR OWN FETCH LADDER, and the observed side was
        // being built from the ladder rather than from what the office listed. Fixing the SOURCE
        // dissolved the divergence instead of a relaxation absorbing it, and equality survived.
        //
        // WHAT EQUALITY COSTS, stated so it is a choice rather than an oversight: a format the
        // publisher genuinely adds later WILL FAIL THIS RED rather than surface as news. That is
        // acceptable while census rows exist for two seeds and a red is cheap to read; it is R8's
        // to revisit when the census covers more.
        //
        // THE ROW SOURCE IS STATED AND IT IS NOT THE LADDER. These tokens come from
        // ObservedManifestationTypesByCelex, which the adapter fills from family M's OWN rows: the
        // publisher's listing. DocumentLadderResultsByOrdinal is the ladder, what this run
        // attempted and was served, and comparing that against a census would report OUR coverage
        // as the OFFICE'S holdings.
        Assert.IsNotNull(
            result.ObservedManifestationTypesByCelex,
            "a run that reached the manifest must carry family M's own listing per Work.");

        foreach (var row in Census)
        {
            Assert.IsTrue(
                result.ObservedManifestationTypesByCelex!.TryGetValue(row.Celex, out var observed),
                $"{row.Celex} has no observed manifestation listing at all. Listings were for: "
                + string.Join(", ", result.ObservedManifestationTypesByCelex.Keys));

            var expected = row.Types
                .Select(static entry => entry.Token)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static token => token, StringComparer.Ordinal)
                .ToArray();

            // Joined rather than compared element-wise so a diff names the token that moved.
            Assert.AreEqual(
                string.Join(",", expected),
                string.Join(",", observed!),
                $"{row.Celex}'s listed manifestation TYPE SET must equal the census's own. The "
                + $"census also records per type counts ({string.Join(",", row.Types.Select(
                    static entry => $"{entry.Token}={entry.Count}"))}) as the publisher's "
                + "inventory; those are NOT compared here, because family M lists types and not "
                + "rows, and asserting a count this route cannot produce would be a fabricated "
                + "check.");
        }
    }

    /// <summary>
    /// The run's own EVIDENCE INDEX: a manifest of what it retained, BY ROLE, written into custody
    /// as a content-addressed artifact and beside the store as plain JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A content-addressed store is files named by their own digests and nothing else, so a later
    /// reader can verify any byte they can already name and cannot discover WHICH byte is which.
    /// Filling an evidence event by pattern-matching a directory would be presenting a guess as a
    /// digest. The index is therefore built BY THE RUN, which knows the roles, and never
    /// reconstructed afterwards.
    /// </para>
    /// <para>
    /// It records the run's own git sha and whether the tree was clean, because for the first
    /// retained run that pair had to be reconstructed from file timestamps, and a reconstruction is
    /// not evidence. A run that cannot say which source produced it has retained bytes without
    /// retaining their provenance.
    /// </para>
    /// <para>
    /// THE PER-FIELD CROSS-RUN AUDIT, MEASURED. Two clean checkouts at one sha, built and run
    /// separately and sequentially so neither doubled the publisher traffic of the other, each
    /// with its own custody root. Both indexes reported runTreeClean true and the same runGitSha,
    /// which is the check that they really were the same source. 126 leaf fields compared.
    /// </para>
    /// <para>
    /// SIX FIELDS DIFFERED and they were the SAME field in each of the six families,
    /// proofAcquisitionRunSha256. Nothing else moved. That settles it as a property of the run
    /// identity rather than a suspicion about the corpus, and the cause is in
    /// <c>RoutedHttpAcquisitionSession.CreateRunIdentity</c>, which hashes a fresh urn:uuid AND
    /// the start instant to 100 nanoseconds; either term alone would be enough.
    /// </para>
    /// <para>
    /// WHAT THE OTHER 120 ACTUALLY PROVE, stated in the shape that stops a reader over-reading
    /// them. 46 were stable AND non-trivial: every family's familyKey, kind,
    /// proofDeliveredRowCount, proofCanonicalKeyDigest, proofInterpretationProfileSha256,
    /// proofSourceProfileSha256 and retainedFloor, plus runGitSha, runTreeClean and the whole-run
    /// refusal code and detail. 18 were static text this file writes on every run. 3 were zero or
    /// empty in both. And 53 WERE NULL IN BOTH AND PROVE NOTHING: both runs refused at
    /// RecordFormNotResolved, before a manifest or a record set existed, so scopeManifest's three
    /// fields, corpusRecordSet.setRefSha256 and every documentBodies entry were absent rather than
    /// stable. A null equal to a null is not a measurement, and counting it as one is how an audit
    /// reports coverage it does not have.
    /// </para>
    /// <para>
    /// AND THE RUN ITSELF MOVED. All six families PROVED, with delivered row counts 2, 4, 41, 166,
    /// 2 and 9. The four object-facts counts are exactly the out-of-band probe totals recorded in
    /// the countAnswerBesideARefusedPage gap below (ObjectFacts 41, ExpressionFacts 166,
    /// RootWatermark 2, ManifestationFacts 9), measured independently and months apart from this
    /// run, so D1-05f's COALESCE and short-page-terminal fixes are confirmed against the live
    /// publisher rather than against fixtures. The whole run now refuses LATER, at
    /// RecordFormNotResolved: seed 32003L0088's root carries no admitted resource_legal_type this
    /// adapter maps to a closed EuActForm. Identical detail in both runs.
    /// </para>
    /// <para>
    /// WHAT IT CANNOT YET REACH, recorded IN the index rather than quietly omitted. Two roles are
    /// not on <see cref="EuQueryExecutionResult"/> at all: each family's pass A and pass B page
    /// bodies with their cursor values, which live on the delivery receipt that
    /// <see cref="EuFamilyEnumerationOutcome"/> does not carry, and the robots bootstrap artifact,
    /// which the routed session writes inside the executor. D1-05g has to surface the receipt
    /// before the index can carry the first, and the file says so where a reader will see it. The
    /// same sentence named D1-05f until D1-05f was about to merge, which is how a gap starts
    /// reading as done: the item it points at closes and nothing repoints it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The roles this index CANNOT yet carry, declared as data so a test can hold them.
    /// </summary>
    /// <remarks>
    /// Extracted from the writer for one reason: until it was, NOTHING IN EITHER LANE ASSERTED
    /// THESE ROLES. They are built inside a canary that is skipped unless LEX_EU_CANARY=1, so a
    /// gap could have been deleted, renamed, or silently never added and the whole suite would
    /// have stayed green. A declaration nothing holds is a comment with JSON syntax. The pin over
    /// this lives in <c>EuCanaryEvidenceIndexGapTests</c> and covers the ROLE NAMES rather than
    /// the prose, because the prose is expected to be edited as each gap is understood better and
    /// the role set is the part that is a contract with a reader diffing two runs.
    /// </remarks>
    internal static System.Text.Json.Nodes.JsonArray RolesThisIndexCannotYetCarry() =>
        new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "familyPassBodiesAndCursors",
                ["why"] = "EuFamilyEnumerationOutcome carries the proof but not the delivery receipt, "
                    + "so pass A and pass B page bodies and their cursor values are not reachable "
                    + "from EuQueryExecutionResult. Surfacing the receipt is R3's to carry, since the pass "
                    + "bodies are what a reader would reopen to check a delivery proof.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "countAnswerBesideARefusedPage",
                ["why"] = "countAnswerItShouldHaveMatched comes from EuEnumerationRefusalDetail"
                    + ".ObservedCount, which this refusal path leaves null, so the count a refused "
                    + "page should have matched is not stated beside it. Measured out of band by "
                    + "direct probes of the four families: ObjectFacts 41, ExpressionFacts 166, "
                    + "RootWatermark 2, ManifestationFacts 9. Populating it on the refusal is a "
                    + "prerequisite of R3, so the index carries the count rather than a "
                    + "reader importing it from this note.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "robotsBootstrapArtifact",
                ["why"] = "The routed session writes robots inside the executor and the adapter's "
                    + "result does not name it, so its digest cannot be stated by role from here.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "crossRunCorpusRecordSetIdentity",
                ["why"] = "setRefSha256 above identifies THIS RUN'S record set and IS NOT "
                    + "COMPARABLE ACROSS RUNS. CorpusRecordCanonicalWriter digests manifest_ref "
                    + "and run_identity through ScopeManifestCanonicalWriter.WriteArtifact, which "
                    + "emits each ref's resource_id, and EuQueryExecutionAdapter mints both of "
                    + "those resource_ids as a fresh urn:uuid per run (the manifest ref paired "
                    + "with the manifest's canonical digest, the run identity paired with the "
                    + "manifest's custody-write digest), so the set digest cannot be stable by "
                    + "construction. Stated here FROM THE CODE PATH rather than by analogy with "
                    + "Luxembourg, whose canary mints the same two resource_ids in the canary "
                    + "itself. THE PER-FIELD AUDIT COULD NOT CONFIRM THIS ONE AT THE TIME: both "
                    + "clean-checkout runs then refused before any record set existed, so "
                    + "setRefSha256 was null in both and being equal proved nothing. D1-05g's "
                    + "run DOES reach the record set and mints a setRefSha256, so the claim is "
                    + "now testable by two runs and remains, for the moment, a reading of the "
                    + "code path rather than a measured pair. It sits beside heldContentSha256 "
                    + "values that ARE content addresses, so without this note a reader diffing "
                    + "two runs sees a moved corpus where nothing moved, and, worse, might take a "
                    + "match as evidence two runs produced the same corpus. WHAT TO DIFF INSTEAD, "
                    + "stable across runs by the same reading of the code: every documentBodies[] "
                    + "heldContentSha256 and heldByteLength, scopeManifest.canonicalSha256 (the "
                    + "manifest's own content address, which no resource_id enters), and "
                    + "observedObjectCount and observedExpressionCount. The custody root is a "
                    + "fresh temp directory per run and is not comparable either; it is printed "
                    + "on the CANARY|custodyRoot line rather than carried in this index. Every "
                    + "one of those is a claim the per-field audit must MEASURE, not inherit from "
                    + "this note.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "crossRunAcquisitionRunIdentity",
                ["why"] = "proofAcquisitionRunSha256 above identifies THIS RUN and IS NOT "
                    + "COMPARABLE ACROSS RUNS, and unlike the other gaps here this one is "
                    + "MEASURED rather than reasoned. "
                    + "RoutedHttpAcquisitionSession.CreateRunIdentity hashes a canonical block "
                    + "carrying a fresh urn:uuid resource id AND the run's start instant to 100 "
                    + "nanoseconds, so TWO independent terms make it unrepeatable; a run identity "
                    + "that DID repeat across two runs would be the defect. Observed: two "
                    + "clean-checkout runs at one sha, both reporting runTreeClean true, gave six "
                    + "different values, one per family, while every other family field held: "
                    + "familyKey, kind, proofDeliveredRowCount, proofCanonicalKeyDigest, "
                    + "proofInterpretationProfileSha256, proofSourceProfileSha256 and "
                    + "retainedFloor were identical in all six. It sits beside those stable "
                    + "digests, so a reader diffing two runs sees six moved values and can read a "
                    + "stable corpus as a changed one. WHAT TO DIFF INSTEAD for a family: "
                    + "proofCanonicalKeyDigest, which is the content address of the delivered "
                    + "keys, and proofDeliveredRowCount beside it.",
            },
        };

    /// <summary>
    /// One enum value as the token a WIRE CONSUMER would see, never as its C# member name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// APPLIED TO EVERY ENUM VALUED FIELD IN THIS DOCUMENT, which it was not at first. Two fields
    /// were converted and six were left on <c>ToString()</c>, so the shipped index carried
    /// PascalCase member names beside snake_case tokens IN ONE SCHEMA DECLARING DOCUMENT, which is
    /// worse for a machine reading it than either convention would have been alone. A half applied
    /// rule is a rule a reader cannot use.
    /// </para>
    /// <para>
    /// THE DEFECT THIS REPLACES, which was live and is not hypothetical. Both call sites used
    /// <c>ToString()</c>, which returns the C# member name and BYPASSES <c>ContractJson</c>
    /// entirely. This index declares a schema, <c>lex-eu-canary-evidence-index/1</c>, so it is a
    /// MACHINE READ document, and a machine reading it has to see the same token a wire consumer
    /// sees. It did not: the acceptance run at the head that found this recorded
    /// <c>RecordFormNotResolved</c> where the wire says <c>record_form_not_resolved</c>.
    /// </para>
    /// <para>
    /// HOW WIDE IT WAS, exactly. <c>ExactStringEnumConverter</c> resolves a member's wire name as
    /// its <c>JsonStringEnumMemberName</c> attribute FALLING BACK to the member name, so today the
    /// index disagreed with the wire only for the members that carry an attribute, and agreed for
    /// the rest by both being PascalCase. That agreement is not a comfort: the members without an
    /// attribute are the separate defect queued as R4, and giving them attributes would widen this
    /// disagreement from a few members to every one of them. Fixing the conversion first means R4
    /// can proceed without dragging this along.
    /// </para>
    /// </remarks>
    private static System.Text.Json.Nodes.JsonNode? WireToken<T>(T? value)
        where T : struct, Enum =>
        value is { } present
            ? System.Text.Json.Nodes.JsonNode.Parse(Lex.V3.Contracts.ContractJson.Serialize(present))
            : null;

    /// <summary>
    /// The evidence index this run would write, built as data so a synthetic result can be
    /// asserted against it without a store, a custody root or a byte of publisher traffic.
    /// </summary>
    /// <remarks>
    /// Split from the write for the same reason the gap array was: the fields it produces were
    /// only ever exercised behind LEX_EU_CANARY=1, so a field could disagree with the wire, as
    /// two of them did, and every test would still pass. The write stays in
    /// <see cref="WriteEvidenceIndexAsync"/>; only the document construction moved.
    /// </remarks>
    internal static System.Text.Json.Nodes.JsonObject BuildEvidenceIndex(
        EuQueryExecutionResult result)
    {
        var dirty = TryGit("status --porcelain");
        var index = new System.Text.Json.Nodes.JsonObject
        {
            ["schema"] = "lex-eu-canary-evidence-index/1",
            ["runGitSha"] = TryGit("rev-parse HEAD"),
            ["runTreeClean"] = dirty is null ? null : dirty.Length == 0,
            ["runTreeDirtyPaths"] = dirty,
            ["custodyClassSegment"] = "nightly-floor-90d",
            ["wholeRunRefusalCode"] = WireToken(result.Refusal?.Code),
            ["wholeRunRefusalDetail"] = result.Refusal?.Detail,
            ["completion"] = WireToken(result.Completion),
            ["observedObjectCount"] = result.ObservedObjectCount,
            ["observedExpressionCount"] = result.ObservedExpressionCount,
        };

        var families = new System.Text.Json.Nodes.JsonArray();
        foreach (var outcome in result.FamilyOutcomes)
        {
            var refusal = outcome.ExecutorRefusal;
            families.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "family",
                ["familyKey"] = outcome.FamilyKey,
                ["kind"] = WireToken<EuFamilyEnumerationOutcomeKind>(outcome.Kind),
                ["retainedFloor"] = WireToken(outcome.RetainedFloor),
                ["proofDeliveredRowCount"] = outcome.Proof?.DeliveredRowCount,
                ["proofCanonicalKeyDigest"] = outcome.Proof?.CanonicalKeyDigest,
                ["proofAcquisitionRunSha256"] = outcome.Proof?.AcquisitionRunRef.Sha256,
                ["proofInterpretationProfileSha256"] = outcome.Proof?.InterpretationProfileRef.Sha256,
                ["proofSourceProfileSha256"] = outcome.Proof?.SourceProfileRef.Sha256,
                ["refusalKind"] = WireToken(refusal?.Code),
                ["refusedBodySha256"] = refusal?.ResponseBodySha256,
                ["refusedBodyTerminalStatus"] = refusal?.TerminalStatus,
                ["refusedBodyObservedMediaType"] = refusal?.ObservedMediaType,
                ["countAnswerItShouldHaveMatched"] = refusal?.ObservedCount,
                ["offendingKey"] = refusal?.OffendingKey,
                ["requestOrdinal"] = refusal?.RequestOrdinal,
                ["proofRefusal"] = WireToken(outcome.ProofRefusal),
            });
        }

        index["families"] = families;

        index["scopeManifest"] = new System.Text.Json.Nodes.JsonObject
        {
            ["role"] = "scopeManifest",
            ["receiptContentSha256"] = result.ScopeManifestReceipt?.Reference.ContentSha256,
            ["receiptByteLength"] = result.ScopeManifestReceipt?.Reference.ByteLength,
            ["canonicalSha256"] = result.ScopeManifestCanonicalSha256,
        };

        index["corpusRecordSet"] = new System.Text.Json.Nodes.JsonObject
        {
            ["role"] = "corpusRecordSet",
            ["setRefSha256"] = result.CorpusRecordSetRef?.Sha256,
        };

        // D1-05g: ONE ROW PER MINTED ROW, never one per attempted fetch. The absent row was the
        // worst form of the unobserved-versus-zero defect: a reader could not tell NOT SELECTED
        // from FAILED from NEVER ATTEMPTED, because there was not even a field to be wrong in.
        // Each row also carries its OBJECT KEY, since the ordinal a body lands on was measured to
        // differ between two runs of one head while the bodies held were the same. The rows are
        // therefore comparable across runs BY OBJECT rather than by position, which is what a two
        // index comparison needs and what the ordinals cannot give it.
        var bodies = new System.Text.Json.Nodes.JsonArray();
        var outcomes = result.DocumentAcquisitionOutcomesByOrdinal
            ?? new Dictionary<int, CorpusAcquisitionOutcome>();
        var minted = result.MintedRowsByOrdinal
            ?? new Dictionary<int, EuMintedRowAccounting>();
        foreach (var entry in minted.OrderBy(static entry => entry.Key))
        {
            outcomes.TryGetValue(entry.Key, out var outcome);
            bodies.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "documentBody",
                ["manifestRowOrdinal"] = entry.Key,
                ["objectCanonicalKey"] = entry.Value.CanonicalKey,
                ["selectedByBodyAxis"] = entry.Value.SelectedByBodyAxis,
                ["heldContentSha256"] = outcome?.Receipt?.Reference.ContentSha256,
                ["heldByteLength"] = outcome?.Receipt?.Reference.ByteLength,

                // WHAT A HOLD MEANS, on the row that claims the hold. A digest and a length say
                // the bytes exist and say nothing about the custody they exist under, which is the
                // same silence this run removed from the witness terminals and from the missing
                // body rows. Both come from the receipt the run already holds, so neither costs a
                // call.
                //
                // TWO FIELDS BECAUSE THEY ARE TWO FACTS, and one would not have closed the gap.
                // custodyClass is the STORAGE CLASS the object was written under, which is the
                // field the Luxembourg index carries per expression, so this is the one that makes
                // the two publishers' indexes agree. custodyMembership is DECISION 71's OWN
                // DISTINCTION, floored against retained_unenforced, which the storage class does
                // not express: an object can be written under a floor class and still be held by a
                // store that enforces nothing. Carrying only the class would have matched LU while
                // leaving the question Decision 71 actually asks unanswered.
                //
                // A row that holds nothing carries NEITHER, and keeps its typed reason. Inventing a
                // class for bytes that were never written is the fabrication these fields exist to
                // prevent.
                ["custodyClass"] = outcome?.Receipt is null
                    ? null
                    : WireToken<CustodyClass>(outcome.Receipt.Reference.CustodyClass),
                ["custodyMembership"] = outcome?.Receipt is null
                    ? null
                    : WireToken<CustodyMembership>(
                        CustodyMembershipClassifier.Classify(outcome.Receipt)),
                ["refusalReason"] = WireToken(outcome?.Refusal)?.GetValue<string>()
                    ?? (entry.Value.SelectedByBodyAxis
                        ? null
                        : "not_selected_by_the_body_axis"),
            });
        }

        index["documentBodies"] = bodies;

        // D1-05g: EVERY RECORD, WITH ITS REASON, so four of eight is readable.
        //
        // documentBodies above carries only the ordinals a fetch was ATTEMPTED for, so a record the
        // body axis quarantined has no row at all and the index simply goes quiet about it. Four
        // held out of eight objects is not readable as success or shortfall until every one of the
        // other four says what it is, and an absent row is the same unobserved-versus-zero defect
        // this run already fixed once for the witness terminals. Measured on the acceptance run:
        // the four without bodies are all consolidated states and every one carries
        // TypedQuarantine, which is a legitimate typed absence and now says so here.
        var records = new System.Text.Json.Nodes.JsonArray();
        foreach (var record in result.CorpusRecordSet?.Set.Records
                     ?? (IReadOnlyList<CorpusRecord>)[])
        {
            records.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "corpusRecord",
                ["canonicalKey"] = record.ObjectRef.CanonicalKey,
                ["bodyKind"] = WireToken<CorpusBodyRecordKind>(record.Body.Kind),
                ["retainedFloor"] = WireToken(record.Body.Floor),
                ["notHeldReason"] = WireToken(record.Body.NotHeldReason),
                ["pendingAcquisitionReason"] = record.Body.PendingAcquisitionReason is null
                    ? null
                    : WireToken<CorpusBodyPendingAcquisitionReasonKind>(
                        record.Body.PendingAcquisitionReason.Kind),
            });
        }

        index["corpusRecords"] = records;

        index["expressionsByRootWork"] = new System.Text.Json.Nodes.JsonObject(
            (result.ObservedExpressionsByCelex
                ?? new Dictionary<string, EuObservedExpressionSplit>(StringComparer.Ordinal))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KeyValuePair<string, System.Text.Json.Nodes.JsonNode?>(
                pair.Key,
                new System.Text.Json.Nodes.JsonObject
                {
                    ["ofRootWork"] = pair.Value.OfRootWork,
                    ["ofConsolidatedStates"] = pair.Value.OfConsolidatedStates,
                })));

        index["rolesThisIndexCannotYetCarry"] = RolesThisIndexCannotYetCarry();

        return index;
    }

    private static async Task WriteEvidenceIndexAsync(
        FileSystemCustodyStore store,
        string root,
        EuQueryExecutionResult result,
        CancellationToken cancellationToken)
    {
        var index = BuildEvidenceIndex(result);
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            index.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, cancellationToken)
            .ConfigureAwait(false);
        var beside = Path.Combine(root, "evidence-index.json");
        await File.WriteAllBytesAsync(beside, bytes, cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"CANARY|evidenceIndex|sha256={receipt.Reference.ContentSha256}|bytes={bytes.Length}"
            + $"|beside={beside}");
    }

    /// <summary>
    /// One census seed's own family key, as <c>EuConsolidationDiscoveryPlan</c> mints it: the literal
    /// "celex-" and the first 24 characters of the lowercase SHA-256 of the CELEX in UTF-8.
    /// </summary>
    /// <remarks>
    /// A re-derivation, because that plan's own <c>PartitionKey</c> is private, and re-derivations
    /// are how a hand-copied rule silently drifts. Two things keep this one honest. It was checked
    /// against the keys two live runs actually produced, celex-af915dcd9a57798f9c4bc881 for
    /// 32003L0088 and celex-c78e22eabda236f00b3a0548 for 32016R0679, before being relied on. And if
    /// the plan's rule ever changes, the assertion above fails loudly naming every key the run did
    /// produce, rather than passing on a coincidence.
    /// </remarks>
    private static string CensusFamilyKey(string celex) =>
        "celex-" + System.Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(celex)))[..24];

    /// <summary>
    /// One git invocation, for the run's own sha and tree state. Returns null when git is not
    /// reachable, because provenance recorded as unknown is honest and a fabricated one is not.
    /// </summary>
    private static string? TryGit(string arguments)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The same permissive shape as <c>EuQueryExecutionAdapterTests.PermissiveEvidenceResolver</c>,
    /// declared here because that one is private to its own test class. A TEST DOUBLE, and the
    /// reason this canary cannot claim the reduction step: the three admission questions below
    /// answer on SHA-256 SHAPE ALONE, and the fourth compares against the ref this constructor was
    /// handed. Residue R0 is the EU production resolver that would answer them against evidence the
    /// run independently holds, the way
    /// <c>LuxembourgProductionScopeReductionEvidenceResolver</c> already does for Luxembourg.
    /// </summary>
    private sealed class CanaryPermissiveEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) && IsSha256(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            IsSha256(binding.ObjectRefSha256);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) &&
            IsSha256(binding.SelectorSetSha256) &&
            IsSha256(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;

        private static bool IsSha256(string value) =>
            value.Length == 64 &&
            value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}

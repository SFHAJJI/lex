using System.Security.Cryptography;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Stage 1 acceptance canary for the Luxembourg document route, required by RULING
/// lex-event-20260904T210132047Z-1abc912084924d498f1d071593688065 and standing on the owner
/// principle lex-event-20260904T205636383Z-e92b888b62c24df29fe3f8c1be5016f0. Its standard is NOT
/// parity with the deployed v2 service, which was withdrawn by
/// lex-event-20260904T210521890Z-c48e8eed3a6c4af5b20eaa5fa7484ccf: every expression in the closure
/// is either Held with a real receipt or refused for exactly one of the four legitimate reasons,
/// stated per object, with the accepted fraction reported as a number.
/// </summary>
/// <remarks>
/// OPT IN, AND DELIBERATELY SO. It sends real requests to the publisher, so it is skipped unless
/// LEX_LU_CANARY=1 is set. A network test that ran by default would make the suite depend on a
/// third party's uptime and would send traffic nobody asked for.
/// <para>
/// It uses the REAL <see cref="FileSystemCustodyStore"/>, never the synthetic one: a canary that
/// proved bodies were held against a synthetic store would prove nothing, and substituting one is
/// the exact shape this project keeps catching.
/// </para>
/// <para>
/// WHAT THIS CANARY COVERS AND WHAT IT DOES NOT, stated plainly. It drives the acquisition half for
/// real: robots fetched and parsed live from legilux.public.lu, the document GET sent through
/// <c>RoutedHttpAcquisitionSession</c>, bodies written to a local custody store, and the corpus
/// record set written by <see cref="CorpusRecordSetWriter"/> and REOPENED before anything is
/// asserted. The manifest it acquires over is built from addresses minted from the publisher's own
/// SPARQL answers, retained beside this file, rather than from a live enumeration inside the test;
/// the enumeration half is covered by the adapter's own tests and by the retained closure query.
/// That boundary is named here rather than blurred.
/// </para>
/// <para>
/// FIRST LIVE RESULT, 2026-09-04, recorded because it is a finding rather than a pass. The fetch
/// half works end to end: robots was fetched and parsed live, all three evaluated paths were
/// permitted, and the publisher returned both bodies (20200101, 5,528,052 bytes, SHA-256
/// c2a66a988209a26657daa4f3f531ffddd7256dfc6e0a9ee3de1204192fbbf4d5; 20251226, 5,413,721 bytes,
/// SHA-256 0b8b50652ea31f1cad7dcae09b7eda33b19a038dd88af9f67bfcc0bb992c073f; both real Akoma Ntoso
/// in the AKN 3.0 CSD13 namespace, retained at C:/lex-v3/scratch/probe-lu-canary). The run then
/// stopped at DocumentBodyNotHeld, and the cause is NOT in the Luxembourg route: Decision 71's
/// floor check requires the body's receipt to classify as Floored, and
/// <see cref="FileSystemCustodyStore"/> declares CustodyVerificationProfile.FileSystemUnenforced1
/// with CustodyProtection.NotEnforced by design, because a local filesystem cannot enforce a WORM
/// retention floor. Src carries exactly two ICustodyStore implementations, that one and the Azure
/// one the canary ruling excludes, and both the EU and LU adapters gate on Floored, so the same
/// wall stands in front of the EU canary. The refusal is correctly typed and is a custody failure
/// on our side, which IS one of the owner's four legitimate reasons, so the product code behaved
/// correctly; what cannot both hold is the canary's own pair of constraints, a local filesystem
/// store and bodies Held. That was ruled since
/// (lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8 and its extension
/// lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c): the floor conflated retention
/// with immutability, so a body held under a weaker guarantee is HELD and says so, while a write
/// error or a digest mismatch stays a refusal.
/// </para>
/// <para>
/// WHAT THIS RUN SAYS NOTHING ABOUT, so a future reader cannot mistake a green canary for evidence
/// that these paths are sound. PROFILE RESOLUTION WAS NEVER EXERCISED: this canary acquires over a
/// manifest built from the publisher's own retained SPARQL answers rather than from a live
/// enumeration, so <c>LuxembourgScopeResolver.Resolve</c> is never called and its failure arms,
/// including the two <c>LuxembourgProfileResolutionFailureCode</c> members no production path
/// constructs (IncompleteVocabulary and SelectorConflict, residue R0 per RULING
/// lex-event-20260904T215524557Z-7cb36f1f533c4318b978a4ff97c929d7), were never reached. So this run
/// proves THE FETCH HALF ONLY. RULING
/// lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c requires the canary to exercise
/// the live enumeration path end to end before it counts, and a manifest built from pre-shaped
/// publisher answers is explicitly not that.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgCodeCivilAcquisitionCanary
{
    private const string EnableVariable = "LEX_LU_CANARY";

    /// <summary>
    /// The Code civil's own consolidation closure, read live from the publisher's SPARQL endpoint
    /// (one bounded query, User-Agent Lex/0.1) and retained: 19 consolidations, each
    /// <c>jolux:isMemberOf</c> the original act <c>loi/1804/03/21/n1</c>, each realized by one
    /// French expression embodying exactly four manifestations (docx, html, pdf, xml), all 76 with
    /// an <c>isExemplifiedBy</c> file in the filestore family and all 76 declaring
    /// <c>http://creativecommons.org/licenses/by/4.0/</c>.
    /// <para>
    /// Two facts from that answer decide this canary. Every wording manifestation here is plain
    /// <c>xml</c>, not <c>xml-akomantoso</c>, so the ladder's second token is the one that matters
    /// on the best known work in the corpus. And <c>legalValue</c> is absent from 57 of the 76,
    /// with the 19 that carry one being EXACTLY the PDFs, one per consolidation, all
    /// <c>statut-version/officiel</c>. So the pre-repair drop would have removed every xml, every
    /// html and every docx, each 100 percent absent, and left the PDFs standing: it would have
    /// stripped the preferred wording manifestation from all 19 consolidations of the best known
    /// work in the corpus while still holding a PDF for each.
    /// </para>
    /// <para>
    /// AN EARLIER VERSION OF THIS COMMENT SAID NOT ONE of the 76 carried a legalValue, and that the
    /// pre-repair code would therefore have held nothing at all for the Code civil. That was wrong,
    /// and the mechanism is worth more than the number. The closure query behind it selected
    /// <c>jolux:license</c>, the licence, and never selected <c>jolux:legalValue</c> at all; the
    /// claim about legalValue was read off the absence of a column that had never been asked for.
    /// Re-measured with userFormat and legalValue BOTH under OPTIONAL so neither can eat a row, and
    /// reconciling to 76 (CORRECTION lex-event-20260904T223038388Z-6d7cc6d87c8e446e829c3f7db93dc0b4).
    /// A held PDF would have made the original sentence look overstated to anyone checking it.
    /// </para>
    /// <para>
    /// AN INDEPENDENT DENOMINATOR, obtained outside this lane before the run so the accepted
    /// fraction can be checked rather than trusted: 19 consolidations spanning 2016-09-01 to
    /// 2025-12-26, all dated; 19 expressions, one per consolidation, all French, zero absent
    /// language; 76 manifestations, exactly four per consolidation, token set exactly docx, pdf,
    /// html and xml at 19 each, with no unexpected token and no absent bucket; licence 76 of 76
    /// CC-BY 4.0, one distinct IRI, zero absent. Both canary dates confirmed by point-in-time
    /// resolution rather than string match.
    /// </para>
    /// <para>
    /// THREE TRAPS in this work, obtained outside this lane with the denominator above and
    /// asserted by nothing here, recorded for whoever wires the live enumeration. The work carries
    /// an <c>owl:sameAs</c> alias with no trailing date to which ZERO consolidations attach, so
    /// resolving through the alias returns nothing. 18 of the 19 consolidations carry
    /// <c>inForceStatus</c> not-applicable and only 2025-12-26 is applicable, so any filter on
    /// in-force status collapses the closure to one row. And raw triple counts inflate roughly
    /// sixteenfold at the Article layer through graph replication, so counting edges rather than
    /// distinct subjects there is meaningless.
    /// </para>
    /// </summary>
    private static readonly (string Consolidation, string InForce, string XmlFile)[] Closure =
    [
        ("20200101", "2020-01-01",
            "http://data.legilux.public.lu/filestore/eli/etat/leg/code/civil/20200101/fr/xml/"
            + "eli-etat-leg-code-civil-20200101-fr-xml.xml"),
        ("20251226", "2025-12-26",
            "http://data.legilux.public.lu/filestore/eli/etat/leg/code/civil/20251226/fr/xml/"
            + "eli-etat-leg-code-civil-20251226-fr-xml.xml"),
    ];

    [TestMethod]
    public async Task TheCodeCivilsTwoCanaryExpressionsAreHeldWithRealReceipts()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Live publisher canary. Set {EnableVariable}=1 to run it; it is skipped by default "
                + "so the suite does not depend on a third party's uptime or send unasked traffic.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "lex-lu-canary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);
        var executor = new LuxembourgRepeatedEnumerationExecutor(store, TimeProvider.System);
        var adapter = new LuxembourgQueryExecutionAdapter(store, executor, BuildProfile());

        var addresses = new Dictionary<SourceObjectRef, LuxembourgDocumentFetchAddress>();
        var objectRefs = new List<SourceObjectRef>();
        foreach (var (consolidation, _, xmlFile) in Closure)
        {
            var publisherUri =
                "http://data.legilux.public.lu/eli/etat/leg/code/civil/" + consolidation;
            var objectRef = ObjectRef(publisherUri);
            objectRefs.Add(objectRef);
            addresses[objectRef] = LuxembourgDocumentFetchAddress.Create(
                LuxembourgFileUri.RequireValid(xmlFile),
                // Plain xml, exactly what the publisher lists for this work, and Unstated because
                // the publisher declares no legalValue for any of the 76.
                LuxembourgUserFormatToken.Xml,
                LuxembourgLegalValue.Unstated,
                new Uri(publisherUri, UriKind.Absolute).AbsolutePath);
        }

        var (manifest, manifestRef) = BuildAcceptedBodyManifest(objectRefs);

        var (outcomes, refusal) = await adapter.RunDocumentAcquisitionAsync(
            manifest,
            addresses,
            LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(9101),
            CancellationToken.None);

        Assert.IsNull(refusal, $"whole-run refusal: {refusal?.Code} {refusal?.Detail}");
        Assert.IsNotNull(outcomes);

        // THE ACCEPTED FRACTION, AS A NUMBER.
        var held = outcomes!.Count(pair => pair.Value.Receipt is not null);
        Console.WriteLine($"CANARY accepted fraction: {held} of {manifest.Rows.Count} held");
        foreach (var (ordinal, outcome) in outcomes.OrderBy(static pair => pair.Key))
        {
            Console.WriteLine(
                $"CANARY row {ordinal}: {(outcome.Receipt is null ? $"REFUSED {outcome.Refusal}" : $"HELD {outcome.Receipt.Reference.ContentSha256} {outcome.Receipt.Reference.ByteLength}b")}");
        }

        // THE REOPENED RECORD SET, not the in-memory one, and its EVIDENCE INDEX, BOTH BEFORE THE
        // SUBSTANTIVE ASSERTIONS. The index used to be written last, so the run whose evidence is
        // worth most, a failing one, produced none at all. Nothing between here and the index is
        // asserted; the record set's own refusal is DATA the index carries rather than a reason to
        // stop before recording anything.
        var written = await new CorpusRecordSetWriter(store).WriteAsync(
            manifest, manifestRef, RunIdentityRef(), outcomes, CancellationToken.None);

        var indexHoldFailure = await WriteEvidenceIndexAsync(
            store,
            new LuxembourgCanaryEvidence(
                TryGit("rev-parse HEAD"),
                TryGit("status --porcelain"),
                root,
                manifest.Rows.Count,
                manifestRef.Sha256,
                outcomes.OrderBy(static pair => pair.Key).Select(pair => new LuxembourgCanaryExpressionRow(
                    pair.Key,
                    manifest.ObservedObjects[pair.Key].ObjectRef.PublisherUri,
                    pair.Value.Receipt?.Reference.ContentSha256,
                    pair.Value.Receipt?.Reference.ByteLength,
                    pair.Value.Receipt is null
                        ? null
                        : CustodyMembershipClassifier.Classify(pair.Value.Receipt).ToString(),
                    pair.Value.Receipt?.Reference.CustodyClass.ToString(),
                    pair.Value.Refusal?.ToString())).ToArray(),
                written.SetRef?.Sha256,
                written.RetainedFloor?.ToString(),
                written.VerifiedSet?.Set.Records.Count,
                written.Refusal?.Kind.ToString()),
            CancellationToken.None);

        // Every row is Held with a real receipt, or typed. Nothing here is untyped.
        foreach (var outcome in outcomes.Values)
        {
            Assert.IsTrue(
                outcome.Receipt is not null || outcome.Refusal is not null,
                "every object is Held or carries a typed refusal, never neither.");
        }

        Assert.AreEqual(
            Closure.Length,
            held,
            "both canary expressions must be Held with real receipts; a refusal here is only "
            + "correct if it names one of the four legitimate reasons, which this assertion's "
            + "failure message must then be read against.");

        Assert.IsNull(written.Refusal, written.Refusal?.Detail);
        Assert.IsNotNull(written.VerifiedSet);

        var records = written.VerifiedSet!.Set.Records;
        Assert.HasCount(Closure.Length, records);
        foreach (var record in records)
        {
            Assert.AreEqual(
                CorpusBodyRecordKind.Held,
                record.Body.Kind,
                $"'{record.ObjectRef.PublisherUri}' must be Held in the REOPENED record set.");
        }

        // The index is held like every other artifact, so a failure to hold IT is a custody failure
        // too. Asserted last, because it must not pre-empt the assertions above: the evidence is
        // already on disk by the time this runs.
        Assert.IsNull(
            indexHoldFailure,
            $"the evidence index itself was not retained: {indexHoldFailure}");
    }

    /// <summary>
    /// THE INDEX SHAPE, DRIVEN WITHOUT THE NETWORK. Every field is pinned by its dotted path, so a
    /// removed or renamed field reddens here instead of first appearing in a live run.
    /// </summary>
    /// <remarks>
    /// The defect this answers, measured rather than supposed: removing runTreeClean outright left
    /// the whole suite green, because WriteEvidenceIndexAsync ran only under the canary gate and
    /// nothing else executed a line of it. An artifact used as ACCEPTANCE that no test exercises is
    /// evidence about the publisher resting on code nobody checks.
    /// <para>
    /// It runs unconditionally and touches no publisher, no store and no git: the evidence is
    /// literals. Values are asserted only where the document DERIVES something rather than copying
    /// it, since a pin that restates every input would fail for any change and so say nothing.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheEvidenceIndexCarriesExactlyTheseFieldsFromASyntheticResult()
    {
        var index = BuildEvidenceIndex(new LuxembourgCanaryEvidence(
            "0000000000000000000000000000000000000000",
            string.Empty,
            "synthetic-root",
            2,
            "1111111111111111111111111111111111111111111111111111111111111111",
            [
                new LuxembourgCanaryExpressionRow(
                    0, "http://example.invalid/a", "aa", 1, "RetainedUnenforced", "NightlyFloor90d", null),
                new LuxembourgCanaryExpressionRow(
                    1, "http://example.invalid/b", null, null, null, null, "NotFound"),
            ],
            "cc",
            "RetainedUnenforced",
            2,
            null));

        CollectionAssert.AreEqual(
            new[]
            {
                "acceptedFraction.censusDenominator.consolidations",
                "acceptedFraction.censusDenominator.expressionsAllFrench",
                "acceptedFraction.censusDenominator.licencesCcBy40",
                "acceptedFraction.censusDenominator.manifestations",
                "acceptedFraction.censusDenominator.manifestationsPerConsolidation",
                "acceptedFraction.censusDenominator.manifestationsWithoutLegalValue",
                "acceptedFraction.censusDenominator.note",
                "acceptedFraction.censusDenominator.obtainedOutsideThisLane",
                "acceptedFraction.censusDenominator.source",
                "acceptedFraction.fraction",
                "acceptedFraction.heldExpressions",
                "acceptedFraction.manifestRowCount",
                "corpusRecordSet.recordCount",
                "corpusRecordSet.refusalKind",
                "corpusRecordSet.retainedFloor",
                "corpusRecordSet.role",
                "corpusRecordSet.setRefSha256",
                "custodyClassSegment",
                "custodyRoot",
                "expressions[].custodyClass",
                "expressions[].custodyClassSegment",
                "expressions[].heldByteLength",
                "expressions[].heldContentSha256",
                "expressions[].manifestRowOrdinal",
                "expressions[].publisherUri",
                "expressions[].refusalReason",
                "expressions[].role",
                "rolesThisIndexCannotYetCarry[].role",
                "rolesThisIndexCannotYetCarry[].why",
                "runGitSha",
                "runTreeClean",
                "runTreeDirtyPaths",
                "schema",
                "scopeManifest.canonicalSha256",
                "scopeManifest.role",
            },
            FieldPaths(index).ToArray(),
            "a field was added, removed or renamed in the evidence index");

        // The derived values, which are the only ones worth asserting: one of the two rows carries
        // no receipt, so the fraction is a half and not a one.
        Assert.AreEqual(1, index["acceptedFraction"]!["heldExpressions"]!.GetValue<int>());
        Assert.AreEqual(0.5, index["acceptedFraction"]!["fraction"]!.GetValue<double>());
        Assert.IsTrue(
            index["runTreeClean"]!.GetValue<bool>(),
            "an empty dirty-path string is a CLEAN tree, not an unknown one.");

        // The four declared gaps, by role, in order. This is the half of the index that says what
        // it cannot support, and it is the half a reader is most likely to be misled without.
        CollectionAssert.AreEqual(
            new[]
            {
                "robotsBootstrapArtifact",
                "scopeManifestCustodyReceipt",
                "crossRunCorpusRecordSetIdentity",
                "enumerationDeliveryProof",
            },
            index["rolesThisIndexCannotYetCarry"]!.AsArray()
                .Select(entry => entry!["role"]!.GetValue<string>()).ToArray(),
            "the declared gaps are part of the contract, not commentary");
    }

    /// <summary>
    /// Every field path in the document, sorted, with array members collapsed to one "[]" entry so
    /// the pin describes a SHAPE rather than this run's row count.
    /// </summary>
    private static IEnumerable<string> FieldPaths(System.Text.Json.Nodes.JsonNode node)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);

        void Walk(System.Text.Json.Nodes.JsonNode? current, string prefix)
        {
            switch (current)
            {
                case System.Text.Json.Nodes.JsonObject o:
                    foreach (var (name, value) in o)
                    {
                        var path = prefix.Length == 0 ? name : prefix + "." + name;
                        if (value is System.Text.Json.Nodes.JsonObject
                            or System.Text.Json.Nodes.JsonArray)
                        {
                            Walk(value, path);
                        }
                        else
                        {
                            paths.Add(path);
                        }
                    }

                    break;
                case System.Text.Json.Nodes.JsonArray a:
                    foreach (var item in a)
                    {
                        Walk(item, prefix + "[]");
                    }

                    break;
            }
        }

        Walk(node, string.Empty);
        return paths;
    }

    /// <summary>
    /// THE INDEX AS A PURE FUNCTION OF A RESULT. It takes the run's evidence and returns the
    /// document; it writes nothing, reads no environment and touches no store, so the shape below
    /// can be driven from a test with no network and no custody at all.
    /// </summary>
    /// <remarks>
    /// It used to be a side effect of a passing test: built inline, written at the end, and reached
    /// only under the canary gate. Two consequences, both real. Removing a field left the suite
    /// green, because nothing but a live run ever executed this code. And the index was written
    /// AFTER the assertions, so a FAILING run produced no index at all, which is precisely the run
    /// whose evidence is worth most. Separating construction from writing fixes the first;
    /// <see cref="TheEvidenceIndexCarriesExactlyTheseFieldsFromASyntheticResult"/> pins the field
    /// set so a removed or renamed field reddens; and the caller now writes it BEFORE it asserts.
    /// <para>
    /// WHAT THIS STILL DOES NOT COVER, said plainly: the mapping from CorpusAcquisitionOutcome to
    /// <see cref="LuxembourgCanaryExpressionRow"/> happens in the live path, so the synthetic test
    /// pins the DOCUMENT's shape and not that mapping. Closing that needs a constructible outcome,
    /// which needs a receipt, which needs custody.
    /// </para>
    /// </remarks>
    internal static System.Text.Json.Nodes.JsonObject BuildEvidenceIndex(
        LuxembourgCanaryEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var index = new System.Text.Json.Nodes.JsonObject
        {
            ["schema"] = "lex-lu-canary-evidence-index/1",
            ["runGitSha"] = evidence.RunGitSha,
            ["runTreeClean"] = evidence.DirtyPaths is null ? null : evidence.DirtyPaths.Length == 0,
            ["runTreeDirtyPaths"] = evidence.DirtyPaths,
            ["custodyClassSegment"] = "nightly-floor-90d",
            ["custodyRoot"] = evidence.CustodyRoot,
        };

        // THE ACCEPTED FRACTION, AS A NUMBER, beside the census it is a fraction OF.
        var held = evidence.Expressions.Count(row => row.HeldContentSha256 is not null);
        index["acceptedFraction"] = new System.Text.Json.Nodes.JsonObject
        {
            ["heldExpressions"] = held,
            ["manifestRowCount"] = evidence.ManifestRowCount,
            ["fraction"] = evidence.ManifestRowCount == 0
                ? null
                : (double)held / evidence.ManifestRowCount,
            ["censusDenominator"] = new System.Text.Json.Nodes.JsonObject
            {
                ["source"] = "lex-event-20260904T223038388Z-6d7cc6d87c8e446e829c3f7db93dc0b4",
                ["obtainedOutsideThisLane"] = true,
                ["consolidations"] = 19,
                ["expressionsAllFrench"] = 19,
                ["manifestations"] = 76,
                ["manifestationsPerConsolidation"] = 4,
                ["licencesCcBy40"] = 76,
                ["manifestationsWithoutLegalValue"] = 57,
                ["note"] = "The counts are labelled because a bare 19 and 76 do not say what they "
                    + "count: the 19 expressions are ALL FRENCH with zero absent language, and the "
                    + "76 licences are ALL CC BY 4.0, one distinct IRI, zero absent. This run "
                    + "acquires the two canary consolidations of the 19, so heldExpressions is a "
                    + "fraction of manifestRowCount and NOT of the 19.",
            },
        };

        var expressions = new System.Text.Json.Nodes.JsonArray();
        foreach (var row in evidence.Expressions.OrderBy(static row => row.ManifestRowOrdinal))
        {
            expressions.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "expressionBody",
                ["manifestRowOrdinal"] = row.ManifestRowOrdinal,
                ["publisherUri"] = row.PublisherUri,
                ["heldContentSha256"] = row.HeldContentSha256,
                ["heldByteLength"] = row.HeldByteLength,
                ["custodyClass"] = row.CustodyClass,
                ["custodyClassSegment"] = row.CustodyClassSegment,
                ["refusalReason"] = row.RefusalReason,
            });
        }

        index["expressions"] = expressions;

        index["scopeManifest"] = new System.Text.Json.Nodes.JsonObject
        {
            ["role"] = "scopeManifest",
            ["canonicalSha256"] = evidence.ScopeManifestCanonicalSha256,
        };

        index["corpusRecordSet"] = new System.Text.Json.Nodes.JsonObject
        {
            ["role"] = "corpusRecordSet",
            ["setRefSha256"] = evidence.RecordSetRefSha256,
            ["retainedFloor"] = evidence.RecordSetRetainedFloor,
            ["recordCount"] = evidence.RecordSetRecordCount,
            ["refusalKind"] = evidence.RecordSetRefusalKind,
        };

        index["rolesThisIndexCannotYetCarry"] = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "robotsBootstrapArtifact",
                ["why"] = "The routed session writes robots.txt into custody inside the executor, "
                    + "and RunDocumentAcquisitionAsync returns only the per-object outcomes and an "
                    + "optional whole-run refusal, so its digest cannot be stated by role from "
                    + "here. This is the same gap the EU index declares, unclosed for the same "
                    + "reason, and it is named rather than omitted.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "scopeManifestCustodyReceipt",
                ["why"] = "This canary drives the document-acquisition phase directly and hands it "
                    + "a manifest it built itself, so nothing in this run HOLDS that manifest: the "
                    + "custody receipt exists only on the RunAsync path. The canonicalSha256 above "
                    + "is the manifest's own content address, NOT a receipt, and the two must not "
                    + "be read as one.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "crossRunCorpusRecordSetIdentity",
                ["why"] = "setRefSha256 above identifies THIS RUN'S record set and IS NOT COMPARABLE "
                    + "ACROSS RUNS. CorpusRecordCanonicalWriter digests manifest_ref and run_identity "
                    + "through ScopeManifestCanonicalWriter.WriteArtifact, which emits each ref's "
                    + "resource_id, and this canary mints both of those resource_ids as a fresh "
                    + "urn:uuid per run, so the set digest cannot be stable by construction. Two runs "
                    + "of identical code against an identical tree produce different values, which "
                    + "was OBSERVED rather than reasoned about: two clean-checkout runs at one sha "
                    + "gave d127d91e.. and 1b2340e1.. with every held digest identical. It sits "
                    + "beside heldContentSha256 values that ARE content addresses, so without this "
                    + "note a reader diffing two runs sees a moved corpus where nothing moved, and, "
                    + "worse, might take a match as evidence two runs produced the same corpus. "
                    + "WHAT TO DIFF INSTEAD, all stable across runs: every expressions[] "
                    + "heldContentSha256 and heldByteLength, scopeManifest.canonicalSha256 (the "
                    + "manifest's own content address, which no resource_id enters), and the "
                    + "acceptedFraction counts. custodyRoot is a fresh temp directory per run and is "
                    + "not comparable either.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "enumerationDeliveryProof",
                ["why"] = "The closure this canary acquires is a fixed list checked in beside it, "
                    + "not a live enumeration, so no AbsenceFamilyEnumerationProof exists for it "
                    + "and completeness of the 19 is asserted by nothing here. The census above is "
                    + "an independent reading, not this run's own proof.",
            },
        };

        return index;
    }

    /// <summary>
    /// Writes the document and HOLDS IT THROUGH <see cref="CustodyHold.TryHoldAsync"/>, like every
    /// other artifact this run retains rather than being the one artifact written without holding.
    /// The file lands beside the store BEFORE the hold, so evidence survives even a custody failure,
    /// and the hold outcome is returned for the caller to assert AFTER its substantive assertions.
    /// </summary>
    private static async Task<string?> WriteEvidenceIndexAsync(
        ICustodyStore store,
        LuxembourgCanaryEvidence evidence,
        CancellationToken cancellationToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            BuildEvidenceIndex(evidence).ToJsonString(
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var beside = Path.Combine(evidence.CustodyRoot, "evidence-index.json");
        await File.WriteAllBytesAsync(beside, bytes, cancellationToken).ConfigureAwait(false);

        var (indexReceipt, holdFailure) = await CustodyHold
            .TryHoldAsync(store, bytes, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine(
            $"CANARY|evidenceIndex|sha256={indexReceipt?.Reference.ContentSha256}"
            + $"|bytes={bytes.Length}|beside={beside}|holdFailure={holdFailure}");
        return holdFailure;
    }

    /// <summary>One expression's row in the index, as plain data a test can build without custody.</summary>
    internal sealed record LuxembourgCanaryExpressionRow(
        int ManifestRowOrdinal,
        string PublisherUri,
        string? HeldContentSha256,
        long? HeldByteLength,
        string? CustodyClass,
        string? CustodyClassSegment,
        string? RefusalReason);

    /// <summary>
    /// Everything the index states, as plain data. The live path fills it from the run; the pin test
    /// fills it with literals.
    /// </summary>
    internal sealed record LuxembourgCanaryEvidence(
        string? RunGitSha,
        string? DirtyPaths,
        string CustodyRoot,
        int ManifestRowCount,
        string ScopeManifestCanonicalSha256,
        IReadOnlyList<LuxembourgCanaryExpressionRow> Expressions,
        string? RecordSetRefSha256,
        string? RecordSetRetainedFloor,
        int? RecordSetRecordCount,
        string? RecordSetRefusalKind);

    /// <summary>
    /// Declared here rather than shared with <see cref="EuStageOneAcquisitionCanary"/>, whose copy
    /// is private to its own class, exactly as that canary declares its own resolver for the same
    /// reason. Returns null rather than throwing when git is absent or answers non-zero, so a
    /// canary run never fails on the provenance fields.
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

    private static (ScopeManifest Manifest, SourceArtifactRef ManifestRef) BuildAcceptedBodyManifest(
        IReadOnlyList<SourceObjectRef> objectRefs)
    {
        var binding = BuildProfile().ScopeBinding;
        var inputs = objectRefs
            .Select(objectRef => new ScopeObjectReductionInput(
                objectRef,
                Enumerable.Range(0, binding.OrderedSelectorMemberOrdinals.Count)
                    .Select(_ => new ScopeSelectorEvidence(
                        ScopeSelectorState.SelectorNotApplicable, [], null, null,
                        RuleOrdinal(binding, ScopeAxis.Record), null))
                    .ToArray(),
                new[]
                {
                    Evaluation(binding, ScopeAxis.Record, ScopeDisposition.AcceptedSelected),
                    Evaluation(binding, ScopeAxis.Body, ScopeDisposition.AcceptedSelected),
                    Evaluation(binding, ScopeAxis.Relation, ScopeDisposition.Point),
                    Evaluation(binding, ScopeAxis.SupportingDocument, ScopeDisposition.Point),
                },
                ScopeManifestFetchAddress.MintedWithoutNegotiation(
                    "legilux.public.lu",
                    new Uri(
                        "http://data.legilux.public.lu/filestore/eli/etat/leg/code/civil/x/fr/xml/x.xml",
                        UriKind.Absolute).AbsolutePath)))
            .ToArray();

        var verified = ScopeReducer.Reduce(
            binding, [], objectRefs, inputs, new PermissiveResolver(CompleteEnumerationRef));
        using var buffer = new MemoryStream();
        var canonical = ScopeManifestCanonicalWriter.Write(buffer, verified);
        return (verified.Manifest, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", canonical));
    }

    private static ScopeRuleEvaluation Evaluation(
        ScopeProfileBinding binding, ScopeAxis axis, ScopeDisposition disposition) =>
        new(
            RuleOrdinal(binding, axis),
            ScopeRuleEvaluationState.Matched,
            ScopeRuleEffect.Positive,
            disposition,
            axis == ScopeAxis.Body && disposition == ScopeDisposition.AcceptedSelected
                ? [binding.BodyCandidateRoleMemberOrdinal]
                : [],
            []);

    private static int RuleOrdinal(ScopeProfileBinding binding, ScopeAxis axis)
    {
        for (var index = 0; index < binding.OrderedRules.Count; index++)
        {
            if (binding.OrderedRules[index].Axis == axis)
            {
                return index;
            }
        }

        throw new AssertFailedException($"no rule for axis {axis}.");
    }

    private static readonly SourceArtifactRef CompleteEnumerationRef = new(
        "urn:uuid:3a7c1e05-6b48-4d29-9f13-84be2c05d7a1",
        Convert.ToHexStringLower(SHA256.HashData("lu-canary-enumeration"u8.ToArray())));

    private static SourceObjectRef ObjectRef(string publisherUri)
    {
        var key = "lu-canary:" + publisherUri;
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(CompleteEnumerationRef, "lu_canary_root"),
            publisherUri,
            key,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))),
            CompleteEnumerationRef,
            null);
    }

    private static SourceArtifactRef RunIdentityRef() => new(
        $"urn:uuid:{Guid.NewGuid():D}",
        Convert.ToHexStringLower(SHA256.HashData("lu-canary-run"u8.ToArray())));

    private static VerifiedLuxembourgSourceProfile BuildProfile() =>
        LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            new SourceArtifactRef("urn:uuid:10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0", new string('1', 64)),
            CompleteEnumerationRef,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
            []));

    private sealed class PermissiveResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) => true;

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) => true;

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) => true;

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;
    }
}

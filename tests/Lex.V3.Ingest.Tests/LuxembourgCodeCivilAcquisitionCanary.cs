using System.Security.Cryptography;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

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

        // THE REOPENED RECORD SET, not the in-memory one.
        var written = await new CorpusRecordSetWriter(store).WriteAsync(
            manifest, manifestRef, RunIdentityRef(), outcomes, CancellationToken.None);
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

        Console.WriteLine($"CANARY custody root: {root}");
        Console.WriteLine($"CANARY record set ref: {written.SetRef!.Sha256}");
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
        VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
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

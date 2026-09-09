using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E6, live half, slice two: the executor entry point that lets the case-law family be
/// run at all.
/// </summary>
/// <remarks>
/// <para>
/// Before this, the merged case-law plan was consumed by nothing. Every other family binds a closed
/// predicate list containing none of E6's five, which is why <c>EuScopeDimensions</c> could record
/// that "all three case-law families" are read by nothing today.
/// </para>
/// <para>
/// These guards cover the part of the entry point that is genuinely new logic rather than a repeat
/// of the accepted two-pass core: which cursor position the batch membership check reads. Getting
/// that wrong does not fail loudly — it verifies membership against the wrong term and admits rows
/// for acts nobody asked about, which is a false publisher claim about what was requested.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuCaseLawExecutorEntryPointTests
{
    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("eu-case-law-executor-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:2c7f1a94-3e6b-4d58-9a02-7fb14c6e58d3",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }

    /// <summary>
    /// The batch membership check reads the act, which the plan's cursor carries at <c>key_3</c>.
    /// </summary>
    /// <remarks>
    /// <c>key_1</c> is the case — the row's own discovered subject — so an ordinal pointing there
    /// would check each delivered row against a case IRI that was never in any batch. The ordinal is
    /// resolved by name against the live profile rather than written as a literal, so this asserts
    /// the resolution rather than restating a constant.
    /// </remarks>
    [TestMethod]
    public void TheBatchMembershipOrdinalPointsAtTheRequestedActNotTheDiscoveredCase()
    {
        var profile = EuCaseLawDiscoveryPlan.Create().CreateDeliveryProfile();

        var ordinal = EuRepeatedEnumerationExecutor.CaseLawBatchMembershipKeyOrdinal(profile);

        Assert.AreEqual("key_3", profile.CursorVariables[ordinal],
            "membership is checked against the requested act.");
        Assert.AreNotEqual("key_1", profile.CursorVariables[ordinal],
            "key_1 is the discovered case and was never part of any batch.");
    }

    /// <summary>A profile that does not carry the act at <c>key_3</c> is refused, not guessed at.</summary>
    /// <remarks>
    /// If the plan's cursor is ever reordered, this fails loudly here rather than silently verifying
    /// batch membership against whichever term happens to sit at the old index.
    /// </remarks>
    [TestMethod]
    public void AProfileWithoutTheActAtKeyThreeIsRefusedRatherThanDefaulted()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();
        var real = plan.CreateDeliveryProfile();
        var reordered = new RepeatedEnumerationInterpretationProfile(
            RepeatedEnumerationInterpretationProfile.SchemaId,
            real.Dialect,
            real.ExpectedMediaType,
            real.CursorEnvelopeIdentity,
            real.MaximumDeliverableRows,
            real.ThresholdDetectorIdentity,
            real.CountQueryFamilyRef,
            real.PageQueryFamilyRef,
            real.CountVariable,
            real.ProjectionVariables,
            ["key_1", "key_2"],
            ["key_1", "key_2"],
            real.SelectionParameterNames,
            real.PassParameterName,
            ["last_key_1", "last_key_2"],
            real.HasCursorParameterName,
            real.TerminalPagePolicy);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => EuRepeatedEnumerationExecutor.CaseLawBatchMembershipKeyOrdinal(reordered));
    }

    /// <summary>
    /// The run request carries the batch, because this family's scope is the caller's acts.
    /// </summary>
    /// <remarks>
    /// The NIM and Legilux identity requests carry no selection: their plans fix scope entirely. This
    /// one does, and that difference is deliberate — the proven counts are inbound on the act, so the
    /// family is asked about named acts rather than swept. The plan still fixes the five predicates
    /// and the batch capacity, so a caller chooses which acts are asked about and nothing else.
    /// </remarks>
    [TestMethod]
    public void TheRunRequestCarriesTheBatchAndNothingElseThePlanAlreadyFixes()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();
        var request = new EuCaseLawRunRequest(
            plan,
            ["http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1"],
            "urn:uuid:6b1f0e2d-84a7-4c39-b5de-90f3a7c21e46",
            Source());

        Assert.HasCount(1, request.BatchWorks);
        Assert.AreSame(plan, request.Plan);

        var properties = typeof(EuCaseLawRunRequest)
            .GetProperties()
            .Select(static property => property.Name)
            .Order()
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { "BatchWorks", "Plan", "PlanResourceId", "RendererSource" },
            properties,
            "the request names the batch and the plan, and carries no second way to widen scope.");
    }

    /// <summary>
    /// A delivered row naming an act nobody requested is refused, driven end to end through the
    /// executor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the guard that makes the wiring real rather than plausible. The three tests above
    /// prove the ordinal is <b>computed</b> correctly; none of them proves it is <b>used</b>. I found
    /// that by mutation: dropping <c>batchObjects</c> to null at the call site disables the
    /// membership check entirely, and every other guard here still passed. The executor's check is
    /// conditional on both the ordinal and a non-null batch, so a null batch silently admits rows for
    /// acts nobody asked about.
    /// </para>
    /// <para>
    /// No other executor entry point in this repository is exercised end to end — they are reached
    /// only through their producers — so this drives <see cref="EuRepeatedEnumerationExecutor"/>
    /// directly with a scripted transport, and asserts the refusal the publisher-facing check
    /// actually produces.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ARowNamingAnActNobodyRequestedIsRefusedByThePartitionCheck()
    {
        const string Requested = "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
        const string NeverRequested = "http://publications.europa.eu/resource/cellar/00000000-1111-2222-3333-444444444444";

        var row = EuAcquisitionTestFixture.CaseLawRow(
            "http://publications.europa.eu/resource/cellar/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
            NeverRequested,
            "ECLI:EU:C:2020:559");

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["CaseLaw"] = EuAcquisitionTestFixture.ScriptFor(
                "CaseLaw", 1, [row], EuAcquisitionTestFixture.CaseLawProjection),
        };

        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(scripts));

        var result = await executor.RunCaseLawLinksAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                [Requested],
                "urn:uuid:b06e5d72-d9fc-4b8e-a02d-45e8fc276d9b",
                Source()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuEnumerationRefusal.DeliveredRowOutsidePartition, result.Refusal?.Code,
            "a row for an unrequested act must not be admitted.");
        Assert.AreEqual(NeverRequested, result.Refusal?.OffendingKey,
            "the refusal names the act that was never requested.");
    }

    /// <summary>
    /// An act requested in a spelling the plan canonicalises is still that caller's own act.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The membership check compares the publisher's delivered key against the caller's batch
    /// ordinally, but the two are not in the same lexical form. <c>EuCaseLawDiscoveryPlan.Bind</c>
    /// sends <c>PadBatch(CanonicalizeBatch(batchWorks))</c>, and
    /// <c>EuPackRootCanonicalForm.TryCanonicalize</c> returns <c>HttpScheme + trimmed</c> — it
    /// rewrites <c>https://</c> to <c>http://</c> and drops one trailing slash. So the publisher is
    /// asked about the canonical form and answers with it, while the executor was handed the raw
    /// <c>request.BatchWorks</c> to check against.
    /// </para>
    /// <para>
    /// The consequence is not a wrong row admitted but a whole family that cannot enumerate: a
    /// caller spelling its acts with <c>https://</c> — a spelling the plan deliberately accepts
    /// rather than refuses — has every legitimately delivered row refused as outside its own
    /// partition. The head's other end-to-end guard only ever exercises the refusing direction, so
    /// nothing observed the admit path at all.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task AnActRequestedInASpellingThePlanCanonicalisesIsStillItsOwnAct()
    {
        const string RequestedHttps = "https://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
        const string Canonical = "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";

        // The publisher answers in the form it was asked about, which is the canonical one.
        Assert.AreEqual(
            Canonical, EuCaseLawDiscoveryPlan.CanonicalizeBatch([RequestedHttps])[0],
            "the plan rewrites the scheme, so the delivered key cannot equal the raw request.");

        var row = EuAcquisitionTestFixture.CaseLawRow(
            "http://publications.europa.eu/resource/cellar/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
            Canonical,
            "ECLI:EU:C:2020:559");

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["CaseLaw"] = EuAcquisitionTestFixture.ScriptFor(
                "CaseLaw", 1, [row], EuAcquisitionTestFixture.CaseLawProjection),
        };

        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(scripts));

        var result = await executor.RunCaseLawLinksAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                [RequestedHttps],
                "urn:uuid:c17f6e83-2a45-4d91-b8e0-56f9ad381c72",
                Source()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreNotEqual(
            EuEnumerationRefusal.DeliveredRowOutsidePartition, result.Refusal?.Code,
            "the act the caller asked about must not be refused as outside its own partition.");
    }

    /// <summary>The entry point binds the plan's own count and page, sending no placeholder.</summary>
    /// <remarks>
    /// Exercised through the plan rather than through a live session, because the executor's other
    /// entry points are reached only by their producers and no session is available here. This
    /// asserts the binding the entry point performs, which is the part it owns.
    /// </remarks>
    [TestMethod]
    public void TheBoundRequestsCarryTheBatchAndLeaveNoRendererSlotUnfilled()
    {
        var plan = EuCaseLawDiscoveryPlan.Create();
        var source = Source();
        string[] batch = ["http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1"];

        var count = plan.BindCount(
            EuCaseLawQueryPass.Pass1, batch,
            "urn:uuid:7c2a1f3e-95b8-4d4a-c6ef-01a4b8d32f57",
            "urn:uuid:8d3b2a4f-a6c9-4e5b-d7fa-12b5c9e43a68",
            source);
        var page = plan.BindPage(
            EuCaseLawQueryPass.Pass2, batch, null, 0, count.InputArtifact.ArtifactRef,
            "urn:uuid:9e4c3b50-b7da-4f6c-e80b-23c6da054b79",
            "urn:uuid:af5d4c61-c8eb-4a7d-f91c-34d7eb165c8a",
            source);

        foreach (var body in new[]
                 {
                     Encoding.UTF8.GetString(count.Request.CopyRequestBody()),
                     Encoding.UTF8.GetString(page.Request.CopyRequestBody()),
                 })
        {
            StringAssert.Contains(body, "3e485e15-11bd-11e6-ba9a-01aa75ed71a1");
            Assert.IsFalse(body.Contains(":iri}", StringComparison.Ordinal), "every batch slot is filled.");
            Assert.IsFalse(body.Contains("{pass_id:uint}", StringComparison.Ordinal));
            StringAssert.Contains(body, "SELECT DISTINCT ?eu_work",
                "the dedup boundary must survive into the sent body.");
        }
    }
}

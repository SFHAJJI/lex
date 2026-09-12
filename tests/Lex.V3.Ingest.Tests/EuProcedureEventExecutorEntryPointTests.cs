using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E8, live half for procedure events, slice two: the executor entry point that lets
/// the family be run at all.
/// </summary>
/// <remarks>
/// <para>
/// Before this, <see cref="EuProcedureEventDiscoveryPlan"/> was consumed by nothing and
/// <c>EuProcedureEventObservation</c> was a merged contract reached by no live path — the same gap
/// the case-law and Conseil d'Etat opinion families each had before their own entry points.
/// </para>
/// <para>
/// THE ADMIT PATH IS ASSERTED FIRST AND DELIBERATELY. The case-law family's own entry point shipped
/// with four end-to-end guards that all drove refusals, and it could not succeed at all: its bind
/// ordered <c>pass_id</c> ahead of the selection while the delivery proof compares the ordered roles
/// as selection-then-pass, so every honest row reached <c>DeliveryProofRefused</c>. A family that can
/// only say no is not one anybody has shown to work.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuProcedureEventExecutorEntryPointTests
{
    private const string Dossier =
        "http://publications.europa.eu/resource/cellar/1f7ba2c8-4d59-11ec-91ac-01aa75ed71a1";

    private const string Event =
        "http://publications.europa.eu/resource/cellar/a3d90b16-4d59-11ec-91ac-01aa75ed71a1";

    // Two distinct declared-type IRIs. They are fixture terms and no claim is made here about which
    // types the publisher actually declares: this contract RETAINS every declared type rather than
    // interpreting any, so what these guards exercise is that each one arrives as its own term.
    private const string FirstType = "http://publications.europa.eu/ontology/cdm#event_legal_type_one";
    private const string SecondType = "http://publications.europa.eu/ontology/cdm#event_legal_type_two";

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("eu-procedure-event-executor-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:8f41c2b7-6a30-4e19-b5cd-27e9f0a4d316",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }

    private static EuRepeatedEnumerationExecutor ExecutorFor(params string[] rows)
    {
        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["ProcedureEvent"] = EuAcquisitionTestFixture.ScriptFor(
                "ProcedureEvent", rows.Length, rows, EuAcquisitionTestFixture.ProcedureEventProjection),
        };

        return new EuRepeatedEnumerationExecutor(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(),
            new EuAcquisitionTestFixture.FixedTimeProvider(),
            new EuAcquisitionTestFixture.ClassifyingHandler(scripts));
    }

    private static Task<EuEnumerationRunResult> RunAsync(
        EuRepeatedEnumerationExecutor executor,
        string inputResourceId,
        params string[] batch) =>
        executor.RunEuProcedureEventsAsync(
            new EuProcedureEventRunRequest(
                EuProcedureEventDiscoveryPlan.Create(),
                batch,
                inputResourceId,
                Source(),
                EuAcquisitionTestFixture.TestWireBudget()),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

    /// <summary>An honest delivery for a requested dossier is admitted and receipted.</summary>
    /// <remarks>
    /// The guard that makes this family real rather than plausible. Every refusal test below would
    /// still pass on an entry point that refuses everything, so this is written first: it is the only
    /// one that fails if the selection and <c>pass_id</c> are bound in the order the case-law family
    /// originally bound them.
    /// </remarks>
    [TestMethod]
    public async Task AFullyRequestedConsistentDeliveryIsAdmitted()
    {
        var executor = ExecutorFor(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"));

        var result = await RunAsync(executor, "urn:uuid:5d18e7a2-9b04-4c76-8fa3-61b2c0d94e58", Dossier);

        Assert.IsNull(
            result.Refusal,
            $"the family must be able to succeed: {result.Refusal?.Code} {result.Refusal?.CoreRefusalDetail}");
        Assert.IsNotNull(result.Receipt);
        Assert.IsGreaterThan(0, result.ProductRequestCount);
    }

    /// <summary>
    /// One event's two declared types arrive as two rows and both are admitted.
    /// </summary>
    /// <remarks>
    /// This is the shape the plan exists to preserve, and it is exercised through the real keyset
    /// rather than asserted about. Two rows sharing <c>key_1</c> and <c>key_3</c> differ only at
    /// <c>key_2</c>, so a cursor that omitted the declared type could not advance past the first of
    /// them; the contract's per-type judgement would then be unreachable from any real delivery.
    /// </remarks>
    [TestMethod]
    public async Task OneEventsTwoDeclaredTypesBothArriveAsTheirOwnRows()
    {
        var executor = ExecutorFor(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"),
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, SecondType, "2021-11-24"));

        var result = await RunAsync(executor, "urn:uuid:6e29f8b3-ac15-4d87-9ab4-72c3d1ea5f69", Dossier);

        Assert.IsNull(
            result.Refusal,
            $"two types of one event are two honest rows: {result.Refusal?.Code} {result.Refusal?.CoreRefusalDetail}");
        Assert.IsNotNull(result.Receipt);
    }

    /// <summary>
    /// An event the publisher holds no date for is delivered, not silently absent.
    /// </summary>
    /// <remarks>
    /// The plan ASKS for this case by name with <c>FILTER NOT EXISTS</c> and binds <c>unbound</c>,
    /// rather than inferring a missing date from a row that never came. A row carrying no
    /// <c>event_date</c> and the <c>unbound</c> marker must therefore reach the executor and be
    /// admitted like any other; an entry point that refused it would turn an evidenced absence into a
    /// transport error.
    /// </remarks>
    [TestMethod]
    public async Task AnEventTheEndpointHoldsNoDateForIsDeliveredRatherThanAbsent()
    {
        var executor = ExecutorFor(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, null));

        var result = await RunAsync(executor, "urn:uuid:7f3a09c4-bd26-4e98-8bc5-83d4e2fb6a70", Dossier);

        Assert.IsNull(
            result.Refusal,
            $"an asked-for absence is an answer: {result.Refusal?.Code} {result.Refusal?.CoreRefusalDetail}");
        Assert.IsNotNull(result.Receipt);
    }

    /// <summary>
    /// An event the publisher declared no type for is delivered, not silently absent.
    /// </summary>
    /// <remarks>
    /// The companion to the undated case, and it exists because the untyped one was NOT asked for on
    /// the first head: <c>?event a ?event_type</c> was mandatory, so an untyped event contributed no
    /// row at all and the accepted <c>EuProcedureEventRefusal.EventTypeMissing</c> was a production
    /// path no delivery could reach. Codex found that in review. The plan now asks for the branch;
    /// this is the guard that the row it produces survives the whole transport rather than only the
    /// query.
    /// </remarks>
    [TestMethod]
    public async Task AnEventTheEndpointDeclaredNoTypeForIsDeliveredRatherThanAbsent()
    {
        var executor = ExecutorFor(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, null, "2021-11-24"));

        var result = await RunAsync(executor, "urn:uuid:ab6c3ce7-e059-4b1c-9de8-b6072e5d0c93", Dossier);

        Assert.IsNull(
            result.Refusal,
            $"an asked-for absence is an answer: {result.Refusal?.Code} {result.Refusal?.CoreRefusalDetail}");
        Assert.IsNotNull(result.Receipt);
    }

    /// <summary>
    /// A row naming a dossier nobody requested is refused by the partition check.
    /// </summary>
    /// <remarks>
    /// Proves the membership check is <b>used</b>, not merely computed. The executor's check is
    /// conditional on both the ordinal and a non-null batch, so dropping <c>batchObjects</c> at the
    /// call site disables it entirely and admits events for dossiers nobody asked about — a false
    /// claim about what was requested, and one no other guard here would notice.
    /// </remarks>
    [TestMethod]
    public async Task ARowNamingADossierNobodyRequestedIsRefusedByThePartitionCheck()
    {
        const string NeverRequested =
            "http://publications.europa.eu/resource/cellar/00000000-1111-2222-3333-444444444444";

        var executor = ExecutorFor(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, NeverRequested, FirstType, "2021-11-24"));

        var result = await RunAsync(executor, "urn:uuid:8a4b1ad5-ce37-4fa9-9cd6-94e5f30c7b81", Dossier);

        Assert.AreEqual(
            EuEnumerationRefusal.DeliveredRowOutsidePartition, result.Refusal?.Code,
            "an event for an unrequested dossier must not be admitted.");
        Assert.AreEqual(NeverRequested, result.Refusal?.OffendingKey,
            "the refusal names the dossier that was never requested.");
    }

    /// <summary>
    /// A dossier requested in a spelling the plan canonicalises is still that caller's own dossier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The membership check compares the delivered key against the caller's batch ordinally, and the
    /// two are not in the same lexical form: <c>Bind</c> sends
    /// <c>PadBatch(CanonicalizeBatch(batchDossiers))</c>, and
    /// <c>EuPackRootCanonicalForm.TryCanonicalize</c> rewrites <c>https://</c> to <c>http://</c> and
    /// drops one trailing slash. The publisher is asked about the canonical form and answers in it.
    /// </para>
    /// <para>
    /// The consequence of handing the executor the raw batch instead is not a wrong row admitted but
    /// a whole family that cannot enumerate: a caller spelling its dossiers with <c>https://</c> — a
    /// spelling the plan deliberately accepts rather than refuses — has every legitimately delivered
    /// row refused as outside its own partition. This is the defect the case-law family shipped with,
    /// caught here before the first live run rather than after it.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ADossierRequestedInASpellingThePlanCanonicalisesIsStillItsOwnDossier()
    {
        const string RequestedHttps =
            "https://publications.europa.eu/resource/cellar/1f7ba2c8-4d59-11ec-91ac-01aa75ed71a1";

        Assert.AreEqual(
            Dossier, EuProcedureEventDiscoveryPlan.CanonicalizeBatch([RequestedHttps])[0],
            "the plan rewrites the scheme, so the delivered key cannot equal the raw request.");

        var executor = ExecutorFor(
            EuAcquisitionTestFixture.ProcedureEventRow(Event, Dossier, FirstType, "2021-11-24"));

        var result = await RunAsync(executor, "urn:uuid:9b5c2be6-df48-4a0b-8de7-a5f6041d8c92", RequestedHttps);

        Assert.IsNull(
            result.Refusal,
            $"the publisher answered in the form it was asked about: {result.Refusal?.Code} {result.Refusal?.CoreRefusalDetail}");
        Assert.IsNotNull(result.Receipt);
    }

    /// <summary>
    /// The batch membership check reads the dossier, which the plan's cursor carries at <c>key_3</c>.
    /// </summary>
    /// <remarks>
    /// <c>key_1</c> is the event — the row's own discovered subject — and <c>key_2</c> is the declared
    /// type, so an ordinal pointing at either would check each delivered row against a term that was
    /// never in any batch. The ordinal is resolved by name against the live profile rather than
    /// written as a literal, so this asserts the resolution rather than restating a constant.
    /// </remarks>
    [TestMethod]
    public void TheBatchMembershipOrdinalPointsAtTheRequestedDossierNotTheDiscoveredEvent()
    {
        var profile = EuProcedureEventDiscoveryPlan.Create().CreateDeliveryProfile();

        var ordinal = EuRepeatedEnumerationExecutor.ProcedureEventBatchMembershipKeyOrdinal(profile);

        Assert.AreEqual("key_7", profile.CursorVariables[ordinal],
            "membership is checked against the requested dossier.");
        Assert.AreNotEqual("key_1", profile.CursorVariables[ordinal],
            "key_1 is the discovered event and was never part of any batch.");
        Assert.AreNotEqual("key_3", profile.CursorVariables[ordinal],
            "key_3 is the declared type and was never part of any batch either.");
    }

    /// <summary>
    /// A profile that does not carry the dossier at <c>key_7</c> is refused, not guessed at.
    /// </summary>
    /// <remarks>
    /// If the plan's cursor is ever reordered, this fails loudly here rather than silently verifying
    /// batch membership against whichever term happens to sit at the old index.
    /// </remarks>
    [TestMethod]
    public void AProfileWithoutTheDossierAtKeySevenIsRefusedRatherThanDefaulted()
    {
        var real = EuProcedureEventDiscoveryPlan.Create().CreateDeliveryProfile();
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
            () => EuRepeatedEnumerationExecutor.ProcedureEventBatchMembershipKeyOrdinal(reordered));
    }
}

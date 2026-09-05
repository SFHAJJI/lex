using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Tests.Census;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Custody;

/// <summary>
/// Residue R2 for the assemblies only this project can see: every <see cref="ICustodyStore"/>
/// implementation here, driven against the obligations the interface states.
/// </summary>
/// <remarks>
/// <para>
/// Ruling lex-event-20260905T024522086Z-4e06a0f56d5240b09ee9e3eba3b661d0 on a0ac0499. The design
/// and the stated ceiling are the same as the sibling suite in Lex.V3.Tests, which owns the src
/// assemblies and the storage-level mismatch test against the production store. This file owns the
/// fourteen doubles that live in Lex.V3.Ingest.Tests, which no other project can reach.
/// </para>
/// <para>
/// Conforming by default, never by a list. Any implementation with a public parameterless
/// constructor is driven without being opted in; everything else must be declared exempt with a
/// reason, and the partition asserts driven plus exempt equals swept. The seven driven here are the
/// adapters in-memory stores, which is exactly the population that had no contract test before.
/// </para>
/// <para>
/// WHAT THIS PROVES, IN EFFECT RATHER THAN MECHANISM. Obligation two is driven directly: after a
/// receipt, the digest alone returns the bytes. Obligation one is proven BY ITS EFFECT, because a
/// contract test binds observable behaviour and the readback is internal to a store: a receipt
/// implies the bytes are retrievable at the reference the write returned, the receipt describes
/// those bytes exactly, and its policy evidence describes the same object under a defined
/// protection. The mismatch clause is driven in the sibling suite, where a store writes to a
/// filesystem the test can reach; none of the doubles here writes to a surface a test can alter
/// underneath them, so it is not driven here and that is said rather than left to be assumed.
/// </para>
/// <para>
/// The swept count moves when a lane merges, and that is the design working rather than churn.
/// Lane A carries two more doubles in a file this project owns. When they arrive they will be
/// neither driven nor exempt, this partition will fail, and somebody will decide about each.
/// </para>
/// </remarks>
[TestClass]
public sealed class CustodyStoreConformanceTests
{
    private static string[] Scope => [.. CensusScope.SweptHere, "Lex.V3.Ingest.Tests"];

    /// <summary>
    /// Implementations this harness cannot construct, each with the reason. Membership is enforced
    /// by <see cref="EveryImplementationIsDrivenOrExemptWithAReason"/>; the reasons are the part a
    /// person keeps true.
    /// </summary>
    private static readonly string[] Exempt =
    [
        "Lex.V3.Ingest.Tests.CorpusRecordSetWriterTests+HoldFailingCustodyStore: decorates an "
            + "inner store in order to fail the hold",
        "Lex.V3.Ingest.Tests.EuAcquisitionTestFixture+EuInMemoryCustodyStore: takes six "
            + "configuration delegates that decide what it holds and what it refuses",
        "Lex.V3.Ingest.Tests.LuxembourgQueryExecutionAdapterTests+DigestSubstitutingCustodyStore: "
            + "decorates an inner store in order to substitute one digest",
        "Lex.V3.Ingest.Tests.LuxembourgQueryExecutionAdapterTests+EnforcingCustodyStore: "
            + "decorates an inner store",
        "Lex.V3.Ingest.Tests.LuxembourgRepeatedEnumerationExecutorTests+EnforcingCustodyStore: "
            + "decorates an inner store",
        "Lex.V3.Ingest.Tests.LuxembourgRepeatedEnumerationExecutorTests+EvictingCustodyStore: "
            + "decorates an inner store in order to evict what it held",
        "Lex.V3.Ingest.Tests.RoutedHttpArtifactDurabilityTests+RecordingCustodyStore: takes the "
            + "artifact kind and the fault it records",
    ];

    [TestMethod]
    public void EveryImplementationIsDrivenOrExemptWithAReason()
    {
        var driven = CustodyStoreConformance.ImplementationTypes(Scope)
            .Where(static type =>
                CustodyStoreConformance.IsDrivenByDefault(type)
                || CustodyStoreConformance.HasRecipe(type))
            .Select(static type => type.FullName!);

        CollectionAssert.AreEqual(
            CustodyStoreConformance.Implementations(Scope).ToArray(),
            driven
                .Concat(Exempt.Select(NameOf))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray(),
            "a custody store is neither driven nor declared exempt, so nothing records whether it "
                + "meets the contract");
    }

    [TestMethod]
    public void TheImplementationCountsAreExactlyThese()
    {
        var types = CustodyStoreConformance.ImplementationTypes(Scope);
        Assert.AreEqual(14, types.Count, "implementations swept");
        Assert.AreEqual(
            7,
            types.Count(static type =>
                CustodyStoreConformance.IsDrivenByDefault(type)
                || CustodyStoreConformance.HasRecipe(type)),
            "implementations driven");
        Assert.AreEqual(7, Exempt.Length, "implementations exempt");
    }

    [TestMethod]
    public async Task EveryDrivenImplementationSatisfiesBothObligations()
    {
        var outcome = await ConformanceRun.RunAsync(Scope, TestContext.CancellationToken);

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            outcome.Failures.ToArray(),
            "a custody store did not meet the obligations its interface states: "
                + string.Join(" | ", outcome.Failures));
    }

    /// <summary>
    /// The lanes a driven store refuses to write, pinned so a refusal stays a stated fact.
    /// </summary>
    /// <remarks>
    /// A refusal is not a failure: a store that cannot observe the protection a lane demands is
    /// right to issue no receipt, and CustodyPolicyEvidence enforces that by refusing to describe a
    /// lane its observed protection does not satisfy. Pinning the set is what keeps that honest, so
    /// a store quietly ceasing to serve a lane it once served is a visible diff rather than a
    /// tolerance nobody notices.
    /// </remarks>
    [TestMethod]
    public async Task TheLanesADrivenStoreDeclinesAreExactlyThese()
    {
        var outcome = await ConformanceRun.RunAsync(Scope, TestContext.CancellationToken);

        CollectionAssert.AreEqual(
            new[]
            {
                "Lex.V3.Ingest.Tests.CorpusRecordSetWriterTests+EnforcingInMemoryCustodyStore declines "
                    + "LegalHoldEvidence: ArgumentException",
                "Lex.V3.Ingest.Tests.LuxembourgDocumentFetchRobotsBootstrapTests+InMemoryCustodyStore declines "
                    + "LegalHoldEvidence: ArgumentException",
                "Lex.V3.Ingest.Tests.LuxembourgQueryExecutionAdapterTests+InMemoryCustodyStore declines "
                    + "LegalHoldEvidence: ArgumentException",
                "Lex.V3.Ingest.Tests.RoutedHttpAcquisitionSessionAuditTests+RecordingCustodyStore declines "
                    + "LegalHoldEvidence: ArgumentException",
                "Lex.V3.Ingest.Tests.RoutedHttpAcquisitionSessionTests+MultiObjectCustodyStore declines "
                    + "LegalHoldEvidence: ArgumentException",
                "Lex.V3.Ingest.Tests.RoutedHttpRedirectCapabilityTests+TestCustodyStore declines "
                    + "LegalHoldEvidence: ArgumentException",
                "Lex.V3.Ingest.Tests.RoutedHttpRequestPolicyAuditTests+MemoryCustodyStore declines "
                    + "LegalHoldEvidence: ArgumentException",
            },
            outcome.DeclinedLanes.ToArray(),
            "the lanes a store declines changed: " + string.Join(" | ", outcome.DeclinedLanes));
    }

    private static string NameOf(string entry) =>
        entry[..entry.IndexOf(':', StringComparison.Ordinal)];

    public TestContext TestContext { get; set; } = null!;
}

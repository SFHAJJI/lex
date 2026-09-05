using Lex.V3.Contracts.Custody;
using Lex.V3.TestSupport;
using Lex.V3.Tests.Census;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Custody;

/// <summary>
/// Residue R2: every <see cref="ICustodyStore"/> implementation this project can see, driven
/// against the obligations the interface states rather than assumed to meet them.
/// </summary>
/// <remarks>
/// <para>
/// Ruling lex-event-20260905T024522086Z-4e06a0f56d5240b09ee9e3eba3b661d0 on a0ac0499, which set
/// both the design and the ceiling recorded below.
/// </para>
/// <para>
/// Conforming by default, never by a list. The sweep selects on the type implementing the
/// interface and nothing else, and any implementation with a public parameterless constructor is
/// driven without being opted in. One named recipe builds
/// <see cref="Lex.V3.Artifacts.FileSystemCustodyStore"/>, because leaving the production store out
/// on the grounds that its constructor takes a root would have gutted the exercise. Everything
/// else must be declared exempt with a reason, and the partition test asserts driven plus exempt
/// equals swept. So a double written tomorrow is driven unless somebody says why not, and a new
/// exemption is a visible diff rather than a silent one.
/// </para>
/// <para>
/// The exemptions are declared here rather than as attributes on the doubles, and the cost is
/// stated rather than hidden: a renamed or moved double loses its exemption and reappears as
/// undriven, which fails loudly. Loud is the right direction for that failure. Attributes are a
/// follow-up once the two lanes holding those files close.
/// </para>
/// <para>
/// WHAT THIS PROVES, IN EFFECT RATHER THAN MECHANISM. Obligation two is driven directly: after a
/// receipt, the digest alone returns the bytes. Obligation one is proven BY ITS EFFECT, because a
/// contract test binds observable behaviour and the readback is internal to a store: a receipt
/// implies the bytes are retrievable at the reference the write returned, the receipt describes
/// those bytes exactly, and its policy evidence describes the same object under a defined
/// protection. The interface documentation keeps the mechanism.
/// </para>
/// <para>
/// The mismatch clause IS driven, against the production store, by injecting the fault at the
/// storage rather than at the implementation. See
/// <see cref="AlteringTheStoredBytesMakesTheProductionStoreRefuseThem"/>. An earlier draft of this
/// file recorded that clause as undrivable; that ceiling was wrong, and
/// <see cref="CustodyStoreConformance"/> keeps the reasoning because the mistake generalises.
/// </para>
/// <para>
/// The swept count moves when a lane merges, and that is the design working rather than churn.
/// Lane A carries two more doubles today. When they arrive they will be neither driven nor exempt,
/// this partition will fail, and somebody will have to decide about each one.
/// </para>
/// </remarks>
[TestClass]
public sealed class CustodyStoreConformanceTests
{
    private static string[] Scope => [.. CensusScope.SweptHere, "Lex.V3.Tests"];

    /// <summary>
    /// Implementations this harness cannot construct, each with the reason. Membership is enforced
    /// by <see cref="EveryImplementationIsDrivenOrExemptWithAReason"/>; the reasons are the part a
    /// person keeps true.
    /// </summary>
    private static readonly string[] Exempt =
    [
        "Lex.V3.Custody.Azure.AzureBlobCustodyStore: needs a live storage account and container "
            + "clients, so neither obligation nor the mismatch clause is drivable in a local suite; "
            + "the Lex.V3.Custody.Probe executable drives it against a real account",
        "Lex.V3.Tests.Custody.AzureCustodyProbeContractTests+MismatchingWriteStore: takes the "
            + "receipt mismatch it exists to produce",
        "Lex.V3.Tests.Custody.AzureCustodyProbeContractTests+ProbeStore: takes the bytes it will "
            + "restore",
        "Lex.V3.Tests.Custody.ContentAddressedCustodyReopeningTests+DigestOnlyStore: takes the "
            + "bytes it serves and withholds everything else",
        "Lex.V3.Tests.Custody.CustodyRestoreTests+LyingReadStore: takes the wrong bytes, or the "
            + "exception, that it exists to return",
        "Lex.V3.Tests.Custody.CustodyTests+RecordingStore: takes the fault and the callback it "
            + "records",
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
        Assert.AreEqual(7, types.Count, "implementations swept");
        Assert.AreEqual(
            1,
            types.Count(static type =>
                CustodyStoreConformance.IsDrivenByDefault(type)
                || CustodyStoreConformance.HasRecipe(type)),
            "implementations driven");
        Assert.AreEqual(6, Exempt.Length, "implementations exempt");
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
            Array.Empty<string>(),
            outcome.DeclinedLanes.ToArray(),
            "the lanes a store declines changed: " + string.Join(" | ", outcome.DeclinedLanes));
    }

    /// <summary>
    /// The mismatch clause, driven against the production store by altering what it wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fault is injected at the storage, not at the implementation, so the store under test
    /// stays a conforming store rather than a double that simulates one. That is possible because
    /// the store writes to a filesystem and a filesystem is reachable from a test.
    /// </para>
    /// <para>
    /// The alteration keeps the file length identical, and the test asserts that it did. Truncating
    /// or deleting the file would drive the length check or the missing-file path instead, and the
    /// test would pass while proving a different clause than the one it names. With the length
    /// held equal, the length comparison in <see cref="CustodyRestore.ReadCheckedAsync"/> cannot
    /// fire, so the digest comparison is the only thing left that can raise.
    /// </para>
    /// <para>
    /// Both halves are required and they are different. The checked read must raise
    /// <see cref="CustodyIntegrityException"/> by its own identity, not merely throw. And
    /// <see cref="ICustodyStore.ReadByDigestAsync"/> must not hand the altered bytes back as the
    /// object for that digest, which is the half that actually protects the corpus, because a
    /// content address resolving to content it does not name is the failure the store design exists
    /// to prevent.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task AlteringTheStoredBytesMakesTheProductionStoreRefuseThem()
    {
        var root = ConformanceRun.NewScratch();
        try
        {
            var store = CustodyStoreConformance.Construct(
                typeof(Lex.V3.Artifacts.FileSystemCustodyStore), root);

            var failure = await CustodyStoreConformance.RunStorageMismatchAsync(
                store,
                (receipt, original) => Task.FromResult(FlipOneStoredByte(root, original)),
                TestContext.CancellationToken);

            Assert.IsNull(failure, failure);
        }
        finally
        {
            ConformanceRun.Delete(root);
        }
    }

    /// <summary>
    /// Finds the file holding exactly <paramref name="original"/> and flips one byte in place,
    /// leaving the length identical. Returns false when no such file is found, so a harness that
    /// stopped reaching the storage reports that rather than passing silently.
    /// </summary>
    private static bool FlipOneStoredByte(string root, ReadOnlyMemory<byte> original)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var stored = File.ReadAllBytes(path);
            if (!stored.AsSpan().SequenceEqual(original.Span))
            {
                continue;
            }

            var lengthBefore = new FileInfo(path).Length;
            stored[0] ^= 0xFF;
            File.WriteAllBytes(path, stored);
            Assert.AreEqual(
                lengthBefore,
                new FileInfo(path).Length,
                "the alteration changed the file length, so this would drive the length check "
                    + "rather than the digest mismatch");
            return true;
        }

        return false;
    }

    private static string NameOf(string entry) =>
        entry[..entry.IndexOf(':', StringComparison.Ordinal)];

    public TestContext TestContext { get; set; } = null!;
}

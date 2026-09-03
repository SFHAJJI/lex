using System.Collections;
using System.Reflection;
using Lex.V3.Contracts.Custody;
using Lex.V3.Custody.Azure;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class AzureContentAddressedCustodyReopeningTests
{
    private static readonly byte[] Artifact =
        "content-addressed HTTP evidence"u8.ToArray();

    [TestMethod]
    [DataRow("nightly")]
    [DataRow("legal_hold")]
    public async Task DigestAloneReopensEitherDurableContainer(string lane)
    {
        var harness = new ExistingAzureHarness();
        var digest = CustodyDigest.Of(Artifact);
        harness.AddGeneration(lane, digest, Artifact, lane == "nightly" ? 'a' : 'b');

        var direct = await harness.Store.ReadByDigestAsync(
            digest, CancellationToken.None);
        var checkedBytes = await CustodyRestore.ReadByDigestCheckedAsync(
            harness.Store, digest, CancellationToken.None);

        CollectionAssert.AreEqual(Artifact, direct.ToArray());
        CollectionAssert.AreEqual(Artifact, checkedBytes.ToArray());
        Assert.AreEqual($"{digest}/", harness.LastPrefix("nightly"));
        Assert.AreEqual($"{digest}/", harness.LastPrefix("legal_hold"));
    }

    [TestMethod]
    public async Task MissingDigestRefusesWithoutAClassOrLengthFallback()
    {
        var harness = new ExistingAzureHarness();
        var digest = CustodyDigest.Of(Artifact);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            CustodyRestore.ReadByDigestCheckedAsync(
                harness.Store, digest, CancellationToken.None));

        Assert.AreEqual($"{digest}/", harness.LastPrefix("nightly"));
        Assert.AreEqual($"{digest}/", harness.LastPrefix("legal_hold"));
    }

    [TestMethod]
    public async Task BytesThatDoNotCarryTheNamedDigestRefuse()
    {
        var harness = new ExistingAzureHarness();
        var digest = CustodyDigest.Of(Artifact);
        harness.AddGeneration("nightly", digest, Corrupt(Artifact), 'c');

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            CustodyRestore.ReadByDigestCheckedAsync(
                harness.Store, digest, CancellationToken.None));
    }

    [TestMethod]
    public async Task AgreeingCopiesAcrossBothDurableContainersReopenOnce()
    {
        var harness = new ExistingAzureHarness();
        var digest = CustodyDigest.Of(Artifact);
        harness.AddGeneration("nightly", digest, Artifact, 'd');
        harness.AddGeneration("legal_hold", digest, Artifact, 'e');

        var reopened = await CustodyRestore.ReadByDigestCheckedAsync(
            harness.Store, digest, CancellationToken.None);

        CollectionAssert.AreEqual(Artifact, reopened.ToArray());
        Assert.AreEqual(1, harness.DownloadCount("nightly"));
        Assert.AreEqual(1, harness.DownloadCount("legal_hold"));
    }

    [TestMethod]
    public async Task AConflictingSecondCopyCannotBeHiddenByTheFirst()
    {
        var harness = new ExistingAzureHarness();
        var digest = CustodyDigest.Of(Artifact);
        harness.AddGeneration("nightly", digest, Artifact, 'f');
        harness.AddGeneration("legal_hold", digest, Corrupt(Artifact), '0');

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            harness.Store.ReadByDigestAsync(digest, CancellationToken.None));

        Assert.AreEqual(1, harness.DownloadCount("nightly"));
        Assert.AreEqual(1, harness.DownloadCount("legal_hold"));
    }

    private static byte[] Corrupt(byte[] source)
    {
        var result = source.ToArray();
        result[0] ^= 0xff;
        return result;
    }

    /// <summary>
    /// Reuses the established Azure SDK fake clients without widening their production surface or
    /// copying a second implementation of the Blob protocol into this focused test file.
    /// </summary>
    private sealed class ExistingAzureHarness
    {
        private const BindingFlags PublicInstance =
            BindingFlags.Instance | BindingFlags.Public;
        private readonly object _inner;
        private readonly Type _type;

        public ExistingAzureHarness()
        {
            _type = typeof(AzureBlobCustodyStoreTests).GetNestedType(
                    "Harness", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The Azure custody test harness is absent.");
            _inner = Activator.CreateInstance(
                    _type,
                    PublicInstance | BindingFlags.NonPublic,
                    binder: null,
                    args: [null],
                    culture: null)
                ?? throw new InvalidOperationException("The Azure custody test harness did not construct.");
        }

        public AzureBlobCustodyStore Store =>
            (AzureBlobCustodyStore)GetProperty(_inner, _type, "Store");

        public void AddGeneration(string lane, string digest, byte[] bytes, char suffix)
        {
            var container = Container(lane);
            var containerType = container.GetType();
            var name = $"{digest}/g/{new string(suffix, 32)}";
            var addExisting = containerType.GetMethod("AddExisting", PublicInstance)
                ?? throw new InvalidOperationException("The Azure fake cannot add a generation.");
            _ = addExisting.Invoke(container, [name, bytes, null]);

            var pages = (IList)GetProperty(container, containerType, "Pages");
            _ = pages.Add(new[] { name });
        }

        public string? LastPrefix(string lane)
        {
            var container = Container(lane);
            return (string?)GetProperty(container, container.GetType(), "LastPrefix");
        }

        public int DownloadCount(string lane) => Events.Count(value =>
            string.Equals(value, $"{lane}.download", StringComparison.Ordinal));

        private IReadOnlyList<string> Events =>
            (IReadOnlyList<string>)GetProperty(_inner, _type, "Events");

        private object Container(string lane) => lane switch
        {
            "nightly" => GetProperty(_inner, _type, "Nightly"),
            "legal_hold" => GetProperty(_inner, _type, "LegalHold"),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "Unknown durable lane."),
        };

        private static object GetProperty(object instance, Type type, string name) =>
            type.GetProperty(name, PublicInstance)?.GetValue(instance)
            ?? throw new InvalidOperationException($"The Azure test harness has no {name} value.");
    }
}

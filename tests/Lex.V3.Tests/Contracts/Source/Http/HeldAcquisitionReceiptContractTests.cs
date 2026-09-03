using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class HeldAcquisitionReceiptContractTests
{
    [TestMethod]
    public void ReceiptHasNoPublicConstructionParsingOrFactoryBoundaryBeforeTheVerifiedProducerExists()
    {
        // The whole construction surface of the receipt, every scope, by-ref parameters included,
        // pinned entry by entry: one private constructor and two internal producers.
        const string Receipt = "Lex.V3.Contracts.Source.Http.HeldAcquisitionReceipt";
        const string Arguments =
            "Lex.V3.Contracts.Source.Http.HeldAcquisitionPublisher, " +
            "Lex.V3.Contracts.Source.Core.SourceArtifactRef, System.UInt64, System.UInt64, " +
            "Lex.V3.Contracts.Source.Http.HeldAcquisitionCoordinate, " +
            "Lex.V3.Contracts.Source.Http.HeldAcquisitionRequestBinding, " +
            "Lex.V3.Contracts.Source.Http.HeldAcquisitionTransportBinding, " +
            "Lex.V3.Contracts.Source.Http.HeldAcquisitionPayload, System.String";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Receipt + "::.ctor(" + Arguments + ") -> " + Receipt,
                "method internal static " + Receipt + "::Create(" + Arguments + ") -> " + Receipt,
                "method internal static " + Receipt + "::ParseAndVerify(System.ReadOnlySpan<System.Byte>) -> " + Receipt,
            },
            ConstructionSurface.Of(typeof(HeldAcquisitionReceipt)).ToArray());

        // Nothing public on the receipt beyond its readers: no public static member at all.
        Assert.AreEqual(
            0,
            ConstructionSurface.DeclaredMembersTransitive(typeof(HeldAcquisitionReceipt))
                .OfType<MethodBase>()
                .Count(static member => member.IsPublic && member.IsStatic));

        // The one producer elsewhere in the assembly is the canonical parser on an internal type;
        // nothing reachable from outside the assembly yields a receipt, directly or wrapped.
        var assembly = typeof(HeldAcquisitionReceipt).Assembly;
        CollectionAssert.AreEqual(
            new[]
            {
                "method public static Lex.V3.Contracts.Source.Http.RoutedHttpCanonicalJson::ParseHeldReceipt(System.ReadOnlySpan<System.Byte>) -> " + Receipt,
            },
            ConstructionSurface.ProducersIn(assembly, typeof(HeldAcquisitionReceipt), includeNonPublic: true).ToArray());
        Assert.AreEqual(
            0,
            ConstructionSurface.ProducersIn(assembly, typeof(HeldAcquisitionReceipt), includeNonPublic: false).Count);
    }

    [TestMethod]
    public void ReceiptHasTheExactClosedShapeAndRoundTripsAcrossCultures()
    {
        byte[]? baseline = null;
        foreach (var cultureName in new[] { "en-US", "fr-FR" })
        {
            using var culture = new CultureScope(cultureName);
            var receipt = Receipt();
            var bytes = receipt.CopyCanonicalBytes();
            baseline ??= bytes;
            CollectionAssert.AreEqual(baseline, bytes);
            CollectionAssert.AreEqual(bytes, HeldAcquisitionReceipt.ParseAndVerify(bytes).CopyCanonicalBytes());
        }

        var expected =
            "{\"schema\":\"lex-held-acquisition-receipt/4\",\"publisher\":\"eu-eurlex\"," +
            "\"run_identity\":{\"resource_id\":\"urn:uuid:11111111-1111-4111-8111-111111111111\"," +
            $"\"sha256\":\"{Digest('1')}\"}},\"request_ordinal\":7,\"attempt_ordinal\":2," +
            "\"coordinate\":{\"work\":\"32016R0679\",\"version\":\"2026-01-01\"," +
            "\"language\":\"fr\",\"manifestation\":\"DOC_1\"},\"request\":{" +
            $"\"enumeration_completion_sha256\":\"{Digest('2')}\"," +
            $"\"acquisition_plan_sha256\":\"{Digest('3')}\"," +
            $"\"logical_request_sha256\":\"{Digest('4')}\"}},\"transport\":{{" +
            $"\"http_evidence_sha256\":\"{Digest('5')}\",\"terminal_hop_ordinal\":1}}," +
            $"\"payload\":{{\"length\":3,\"sha256\":\"{Digest('a')}\"," +
            $"\"durable_write_receipt_sha256\":\"{Digest('b')}\",\"readback_byte_length\":3," +
            $"\"readback_sha256\":\"{Digest('a')}\"}}," +
            "\"created_at\":\"2026-09-02T20:00:02.0000000Z\"}\n";
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), baseline!);

        var canonical = baseline!;
        using var document = JsonDocument.Parse(canonical.AsMemory(0, canonical.Length - 1));
        CollectionAssert.AreEqual(
            new[]
            {
                "schema", "publisher", "run_identity", "request_ordinal", "attempt_ordinal",
                "coordinate", "request", "transport", "payload", "created_at",
            },
            document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "work", "version", "language", "manifestation" },
            document.RootElement.GetProperty("coordinate").EnumerateObject()
                .Select(static property => property.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "enumeration_completion_sha256", "acquisition_plan_sha256", "logical_request_sha256" },
            document.RootElement.GetProperty("request").EnumerateObject()
                .Select(static property => property.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "http_evidence_sha256", "terminal_hop_ordinal" },
            document.RootElement.GetProperty("transport").EnumerateObject()
                .Select(static property => property.Name).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "length", "sha256", "durable_write_receipt_sha256",
                "readback_byte_length", "readback_sha256",
            },
            document.RootElement.GetProperty("payload").EnumerateObject()
                .Select(static property => property.Name).ToArray());
    }

    [TestMethod]
    public void PublisherCoordinateAndPayloadAreClosedAndBounded()
    {
        _ = new HeldAcquisitionCoordinate("32016R0679", "2026-01-01", "fr", "DOC_1");
        Assert.ThrowsExactly<ArgumentException>(() => new HeldAcquisitionCoordinate("", "v", "fr", "m"));
        Assert.ThrowsExactly<ArgumentException>(() => new HeldAcquisitionCoordinate("w", "v", "FR", "m"));
        Assert.ThrowsExactly<ArgumentException>(() => new HeldAcquisitionCoordinate(
            "w", "v", new string('a', 17), "m"));
        Assert.ThrowsExactly<ArgumentException>(() => new HeldAcquisitionCoordinate("w", "v", "-fr", "m"));
        Assert.ThrowsExactly<ArgumentException>(() => new HeldAcquisitionCoordinate(
            new string('é', 1025), "v", "fr", "m"));

        Assert.ThrowsExactly<ArgumentException>(() => new HeldAcquisitionPayload(
            3,
            Digest('a'),
            Digest('b'),
            2,
            Digest('a')));
        Assert.ThrowsExactly<ArgumentException>(() => new HeldAcquisitionPayload(
            3,
            Digest('a'),
            Digest('b'),
            3,
            Digest('c')));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HeldAcquisitionPayload(
            268_435_456,
            Digest('a'),
            Digest('b'),
            268_435_456,
            Digest('a')));
        Assert.ThrowsExactly<ArgumentException>(() => new HeldAcquisitionPayload(
            0,
            Digest('a'),
            Digest('b'),
            0,
            Digest('a')));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new HeldAcquisitionTransportBinding(Digest('a'), 6));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HeldAcquisitionReceipt.Create(
            HeldAcquisitionPublisher.EuEurLex,
            new SourceArtifactRef(
                "urn:uuid:11111111-1111-4111-8111-111111111111",
                Digest('1')),
            requestOrdinal: 0,
            attemptOrdinal: 2,
            new HeldAcquisitionCoordinate("32016R0679", "2026-01-01", "fr", "DOC_1"),
            new HeldAcquisitionRequestBinding(Digest('2'), Digest('3'), Digest('4')),
            new HeldAcquisitionTransportBinding(Digest('5'), terminalHopOrdinal: 1),
            new HeldAcquisitionPayload(3, Digest('a'), Digest('b'), 3, Digest('a')),
            "2026-09-02T20:00:02.0000000Z"));
    }

    [TestMethod]
    public void PublisherVocabularyAndWireTokensAreExact()
    {
        CollectionAssert.AreEqual(
            new[] { "EuEurLex=2", "LuLegilux=1" },
            Enum.GetValues<HeldAcquisitionPublisher>()
                .Select(static publisher => $"{publisher}={Convert.ToInt32(publisher)}")
                .OrderBy(static identity => identity, StringComparer.Ordinal)
                .ToArray());

        foreach (var (publisher, token) in new[]
                 {
                     (HeldAcquisitionPublisher.LuLegilux, "lu-legilux"),
                     (HeldAcquisitionPublisher.EuEurLex, "eu-eurlex"),
                 })
        {
            var bytes = Receipt(publisher).CopyCanonicalBytes();
            using var document = JsonDocument.Parse(bytes);
            Assert.AreEqual(token, document.RootElement.GetProperty("publisher").GetString());
            var reopened = HeldAcquisitionReceipt.ParseAndVerify(bytes);
            Assert.AreEqual(publisher, reopened.Publisher);
            CollectionAssert.AreEqual(bytes, reopened.CopyCanonicalBytes());
        }
    }

    [TestMethod]
    public void ReaderRefusesAlternateSpellingsAndFieldSubstitution()
    {
        var canonical = Encoding.UTF8.GetString(Receipt().CopyCanonicalBytes());
        foreach (var mutation in new[]
        {
            canonical.Replace("\"publisher\":\"eu-eurlex\"", "\"publisher\":\"EU-EURLEX\"", StringComparison.Ordinal),
            canonical.Replace("\"language\":\"fr\"", "\"language\":\"FR\"", StringComparison.Ordinal),
            canonical.Replace("\"request_ordinal\":7", "\"request_ordinal\":7.0", StringComparison.Ordinal),
            canonical.Replace("\"readback_byte_length\":3", "\"readback_byte_length\":2", StringComparison.Ordinal),
            canonical.Replace("\"created_at\":", "\"created_at\": \"ignored\",\"old_created_at\":", StringComparison.Ordinal),
            canonical.TrimEnd('\n'),
        })
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                HeldAcquisitionReceipt.ParseAndVerify(Encoding.UTF8.GetBytes(mutation)));
        }

        var zeroOrdinal = canonical.Replace(
            "\"request_ordinal\":7",
            "\"request_ordinal\":0",
            StringComparison.Ordinal);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            HeldAcquisitionReceipt.ParseAndVerify(Encoding.UTF8.GetBytes(zeroOrdinal)));
    }

    private static HeldAcquisitionReceipt Receipt(
        HeldAcquisitionPublisher publisher = HeldAcquisitionPublisher.EuEurLex) =>
        HeldAcquisitionReceipt.Create(
        publisher,
        new SourceArtifactRef(
            "urn:uuid:11111111-1111-4111-8111-111111111111",
            Digest('1')),
        requestOrdinal: 7,
        attemptOrdinal: 2,
        new HeldAcquisitionCoordinate("32016R0679", "2026-01-01", "fr", "DOC_1"),
        new HeldAcquisitionRequestBinding(Digest('2'), Digest('3'), Digest('4')),
        new HeldAcquisitionTransportBinding(Digest('5'), terminalHopOrdinal: 1),
        new HeldAcquisitionPayload(3, Digest('a'), Digest('b'), 3, Digest('a')),
        "2026-09-02T20:00:02.0000000Z");

    private static string Digest(char value) => new(value, 64);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}

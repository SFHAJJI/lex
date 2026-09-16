using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;

namespace Lex.V3.Tests.Contracts.Index;

[TestClass]
public sealed class V3IndexCapabilityManifestTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string OtherDigest =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [TestMethod]
    public void SupportedCellsAreCanonicalAndBoundToTheExactIndex()
    {
        var later = Cell(periodFrom: new DateOnly(2020, 1, 1), periodTo: new DateOnly(2024, 12, 31), population: 41);
        var earlier = Cell(periodFrom: new DateOnly(2010, 1, 1), periodTo: new DateOnly(2019, 12, 31), population: 17);
        var otherOperation = Cell(operation: "resolve", column: "objects", population: 2);
        var otherColumn = Cell(column: "fts_body", population: 3);
        var otherField = Cell(field: "short_title", population: 4);
        var otherLanguage = Cell(language: "deu", population: 5);

        var manifest = Create(
            PublisherId.LuLegilux,
            Digest,
            [later, otherLanguage, earlier, otherField, otherColumn, otherOperation]);

        Assert.AreEqual(Digest, manifest.IndexSha256);
        Assert.AreEqual("day", V3IndexCapabilityManifest.PeriodGranularity);
        CollectionAssert.AreEqual(
            new[] { otherOperation, otherColumn, otherField, otherLanguage, earlier, later },
            manifest.Cells.ToArray());
    }

    [TestMethod]
    public void CellRequiresKnownPublisherIndexOperationDimensionsPeriodAndPopulation()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Cell(publisher: (PublisherId)99));
        Assert.ThrowsExactly<ArgumentException>(() => Cell(indexSha256: "not-a-digest"));
        Assert.ThrowsExactly<ArgumentException>(() => Cell(operation: "not_an_operation"));
        Assert.ThrowsExactly<ArgumentException>(() => Cell(column: " "));
        Assert.ThrowsExactly<ArgumentException>(() => Cell(field: " "));
        Assert.ThrowsExactly<ArgumentException>(() => Cell(language: " "));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Cell(periodFrom: new DateOnly(2020, 1, 2), periodTo: new DateOnly(2020, 1, 1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Cell(population: 0));
    }

    [TestMethod]
    public void ManifestRefusesUnknownPublisherMismatchedPublisherIndexAndMalformedCell()
    {
        Assert.IsFalse(V3IndexCapabilityManifest.TryCreate(
            (PublisherId)99,
            Digest,
            [],
            out _,
            out var unknownRefusal));
        Assert.AreEqual(V3IndexCapabilityManifestRefusal.UnknownPublisher, unknownRefusal);

        Assert.IsFalse(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            Digest,
            [Cell(publisher: PublisherId.EuEurLex)],
            out _,
            out var publisherRefusal));
        Assert.AreEqual(V3IndexCapabilityManifestRefusal.PublisherMismatch, publisherRefusal);

        Assert.IsFalse(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            Digest,
            [Cell(indexSha256: OtherDigest)],
            out _,
            out var indexRefusal));
        Assert.AreEqual(V3IndexCapabilityManifestRefusal.IndexMismatch, indexRefusal);

        Assert.IsFalse(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            Digest,
            new V3IndexCapabilityCell[] { null! },
            out _,
            out var malformedRefusal));
        Assert.AreEqual(V3IndexCapabilityManifestRefusal.MalformedCell, malformedRefusal);
    }

    [TestMethod]
    public void ManifestRefusesDuplicateCellEvenWhenItsPopulationDiffers()
    {
        var first = Cell(population: 7);
        var differentPopulation = Cell(population: 8);

        Assert.IsFalse(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            Digest,
            [first, differentPopulation],
            out _,
            out var refusal));
        Assert.AreEqual(V3IndexCapabilityManifestRefusal.DuplicateCell, refusal);
    }

    [TestMethod]
    public void ManifestRefusesOverlappingInclusivePeriodsForTheSameDimensions()
    {
        var first = Cell(periodFrom: new DateOnly(2020, 1, 1), periodTo: new DateOnly(2020, 6, 30));
        var overlap = Cell(periodFrom: new DateOnly(2020, 6, 30), periodTo: new DateOnly(2020, 12, 31));

        Assert.IsFalse(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            Digest,
            [overlap, first],
            out _,
            out var refusal));
        Assert.AreEqual(V3IndexCapabilityManifestRefusal.OverlappingPeriod, refusal);
    }

    [TestMethod]
    public void ContiguousPositiveCellsSupportTheWholeRequestedRange()
    {
        var first = Cell(periodFrom: new DateOnly(2020, 1, 1), periodTo: new DateOnly(2020, 3, 31));
        var second = Cell(periodFrom: new DateOnly(2020, 4, 1), periodTo: new DateOnly(2020, 6, 30));
        var third = Cell(periodFrom: new DateOnly(2020, 7, 1), periodTo: new DateOnly(2020, 12, 31));
        var manifest = Create(PublisherId.LuLegilux, Digest, [third, first, second]);

        var outcome = manifest.Lookup(
            "search",
            "fts_title",
            "title",
            "fra",
            new DateOnly(2020, 2, 1),
            new DateOnly(2020, 10, 1),
            out var cells);

        Assert.AreEqual(V3IndexCapabilityLookupOutcome.Supported, outcome);
        CollectionAssert.AreEqual(new[] { first, second, third }, cells.ToArray());
    }

    [TestMethod]
    public void AGapOrDifferentDimensionIsTypedUnsupportedAndCarriesNoPartialCells()
    {
        var first = Cell(periodFrom: new DateOnly(2020, 1, 1), periodTo: new DateOnly(2020, 3, 31));
        var afterGap = Cell(periodFrom: new DateOnly(2020, 5, 1), periodTo: new DateOnly(2020, 12, 31));
        var manifest = Create(PublisherId.LuLegilux, Digest, [first, afterGap]);

        AssertUnsupported(manifest, "resolve", "fts_title", "title", "fra", new DateOnly(2020, 1, 1), new DateOnly(2020, 3, 31));
        AssertUnsupported(manifest, "search", "fts_body", "title", "fra", new DateOnly(2020, 1, 1), new DateOnly(2020, 3, 31));
        AssertUnsupported(manifest, "search", "fts_title", "short_title", "fra", new DateOnly(2020, 1, 1), new DateOnly(2020, 3, 31));
        AssertUnsupported(manifest, "search", "fts_title", "title", "deu", new DateOnly(2020, 1, 1), new DateOnly(2020, 3, 31));
        AssertUnsupported(manifest, "search", "fts_title", "title", "fra", new DateOnly(2019, 12, 31), new DateOnly(2020, 3, 31));
        AssertUnsupported(manifest, "search", "fts_title", "title", "fra", new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1));
        AssertUnsupported(manifest, "search", "fts_title", "title", "fra", new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31));
    }

    [TestMethod]
    public void LookupRejectsMalformedOrReversedFilterInsteadOfTreatingItAsUnsupported()
    {
        var manifest = Create(PublisherId.LuLegilux, Digest, []);

        Assert.ThrowsExactly<ArgumentException>(() => manifest.Lookup(
            " ", "fts_title", "title", "fra", new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1), out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => manifest.Lookup(
            "search", "fts_title", "title", "fra", new DateOnly(2020, 1, 2), new DateOnly(2020, 1, 1), out _));
    }

    private static V3IndexCapabilityManifest Create(
        PublisherId publisher,
        string indexSha256,
        IEnumerable<V3IndexCapabilityCell> cells)
    {
        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            publisher,
            indexSha256,
            cells,
            out var manifest,
            out var refusal));
        Assert.AreEqual(V3IndexCapabilityManifestRefusal.None, refusal);
        return manifest!;
    }

    private static void AssertUnsupported(
        V3IndexCapabilityManifest manifest,
        string operation,
        string column,
        string field,
        string language,
        DateOnly periodFrom,
        DateOnly periodTo)
    {
        var outcome = manifest.Lookup(operation, column, field, language, periodFrom, periodTo, out var cells);
        Assert.AreEqual(V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex, outcome);
        Assert.IsEmpty(cells);
    }

    private static V3IndexCapabilityCell Cell(
        PublisherId publisher = PublisherId.LuLegilux,
        string indexSha256 = Digest,
        string operation = "search",
        string column = "fts_title",
        string field = "title",
        string language = "fra",
        DateOnly periodFrom = default,
        DateOnly periodTo = default,
        long population = 7)
    {
        periodFrom = periodFrom == default ? new DateOnly(2020, 1, 1) : periodFrom;
        periodTo = periodTo == default ? new DateOnly(2020, 12, 31) : periodTo;
        return new V3IndexCapabilityCell(
            publisher,
            indexSha256,
            operation,
            column,
            field,
            language,
            periodFrom,
            periodTo,
            population);
    }
}

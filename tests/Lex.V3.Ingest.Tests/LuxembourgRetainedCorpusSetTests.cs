using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A finished Luxembourg run keeps the address of its own corpus/6 record set, and that address is
/// usable: the set reopens from custody through the door alone, with nothing this process still
/// happened to be holding.
/// </summary>
[TestClass]
public sealed class LuxembourgRetainedCorpusSetTests
{
    [TestMethod]
    public async Task ARunsRetainedRecordSetReopensFromTheAddressTheRunItselfCarries()
    {
        var (run, store) = await LuxembourgQueryExecutionAdapterTests
            .RunTwoSubjectDeliveredWithStoreAsync();
        Assert.IsNull(run.Refusal, $"code={run.Refusal?.Code} detail={run.Refusal?.Detail}");
        Assert.IsNotNull(run.CorpusRecordSetReceipt,
            "a delivered run must name where its own record set was retained.");

        // Only what the result carries: the receipt and the reference. No handle to the writer, and
        // no reuse of the in-memory set the run returned.
        var read = await new CorpusRecordSetReader(store).ReadAsync(
            run.CorpusRecordSetReceipt!, run.CorpusRecordSetRef!, CancellationToken.None);

        Assert.IsNull(read.Refusal, read.Refusal?.Detail);
        CollectionAssert.AreEqual(
            Canonical(run.CorpusRecordSet!),
            Canonical(read.VerifiedSet!),
            "the reopened set must be this run's own set byte for byte.");
    }

    /// <summary>
    /// The address is not the reference, which is the whole reason the result has to carry both.
    /// </summary>
    [TestMethod]
    public async Task TheRetainedAddressIsNotTheSetReference()
    {
        var run = await LuxembourgQueryExecutionAdapterTests
            .RunTwoSubjectDeliveredForPopulationAsync();

        Assert.AreNotEqual(
            run.CorpusRecordSetRef!.Sha256,
            run.CorpusRecordSetReceipt!.Reference.ContentSha256,
            "the set's canonical digest is domain separated and custody addresses the plain bytes, "
                + "so a consumer holding only the reference could never fetch the set.");
    }

    private static byte[] Canonical(VerifiedCorpusRecordSet set)
    {
        using var buffer = new MemoryStream();
        CorpusRecordSetCanonicalWriter.Write(buffer, set.Set);
        return buffer.ToArray();
    }
}

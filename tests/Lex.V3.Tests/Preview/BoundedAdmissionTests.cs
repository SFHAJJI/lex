using Lex.V3.Artifacts;
using Lex.V3.Contracts;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class BoundedAdmissionTests
{
    [TestMethod]
    public async Task ReaderAllocatesTheExactCapAndUsesAOneByteOverflowProbe()
    {
        var stream = new RecordingReadStream(new byte[9]);

        var result = await BoundedStreamReader.ReadAsync(stream, 8, CancellationToken.None);

        Assert.IsTrue(result.ExceededLimit);
        CollectionAssert.AreEqual(new[] { 8, 1 }, stream.RequestedReadSizes.ToArray());
    }

    [TestMethod]
    public async Task ReaderAcceptsTheExactCapWithoutAnExtraRetainedByte()
    {
        var stream = new RecordingReadStream(new byte[8]);

        var result = await BoundedStreamReader.ReadAsync(stream, 8, CancellationToken.None);

        Assert.IsFalse(result.ExceededLimit);
        Assert.AreEqual(8, result.Bytes.Length);
        CollectionAssert.AreEqual(new[] { 8, 1 }, stream.RequestedReadSizes.ToArray());
    }

    [TestMethod]
    public void SchemaTableRejectsBeforeEncodingBeyondTheRemainingBudget()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            SyntheticSliceSchemaExporter.ExportSchemaTable(maximumTrackedBytes: 1));
    }

    private sealed class RecordingReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public List<int> RequestedReadSizes { get; } = new();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            RequestedReadSizes.Add(buffer.Length);
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}

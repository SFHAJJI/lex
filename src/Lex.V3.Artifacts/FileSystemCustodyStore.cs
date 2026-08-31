using Lex.V3.Contracts.Custody;

namespace Lex.V3.Artifacts;

/// <summary>
/// A create-only content-addressed custody store on a local filesystem.
/// </summary>
/// <remarks>
/// <para>
/// This is the store a source build uses. It is not a stand-in for the durable provider: it holds
/// the same contract, so the ordering property and its tests are exercised by the code that ships
/// rather than by a mock written to agree with them.
/// </para>
/// <para>
/// Create-only is enforced by <see cref="FileMode.CreateNew"/> rather than by checking existence
/// first, because a check followed by a create is two operations and the interval between them is
/// where a substitution goes. When the address already exists the stored bytes are read back and
/// their digest recomputed, so an address holding the wrong bytes is a detected fault rather than
/// a silent success.
/// </para>
/// </remarks>
public sealed class FileSystemCustodyStore : ICustodyStore
{
    private readonly string _root;
    private readonly TimeProvider _time;

    public FileSystemCustodyStore(string root, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _time = time ?? TimeProvider.System;
    }

    public DurableBlobWriteReceipt Create(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass)
    {
        if (bytes.Length > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytes),
                "A body above the admitted bound is refused before the filesystem is touched.");
        }

        var digest = CustodyDigest.Of(bytes.Span);
        var directory = Path.Combine(_root, ClassSegment(custodyClass));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, digest);

        try
        {
            using var stream = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes.Span);
        }
        catch (IOException) when (File.Exists(path))
        {
            // The address is already held, which content addressing makes idempotent. Whether the
            // held bytes really are these bytes is decided by the single verification below, not
            // here: two verification points mean removing either one leaves the other, and a
            // mutation that deletes a check nothing else duplicates is the only one a test can
            // catch. This branch therefore falls through deliberately.
        }

        var readBack = File.ReadAllBytes(path);
        if (!string.Equals(CustodyDigest.Of(readBack), digest, StringComparison.Ordinal)
            || readBack.LongLength != bytes.Length)
        {
            throw new CustodyIntegrityException(
                $"The object read back from content address {digest} is not the object written.");
        }

        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, digest, bytes.Length, custodyClass),
            _time.GetUtcNow().ToUniversalTime());
    }

    private static string ClassSegment(CustodyClass custodyClass) => custodyClass switch
    {
        CustodyClass.NightlyFloor90d => "nightly-floor-90d",
        CustodyClass.EvidenceIndefinite => "evidence-indefinite",
        _ => throw new ArgumentOutOfRangeException(
            nameof(custodyClass), custodyClass, "Unknown custody class."),
    };
}

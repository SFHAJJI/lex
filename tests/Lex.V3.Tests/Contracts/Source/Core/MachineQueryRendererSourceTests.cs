using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class MachineQueryRendererSourceTests
{
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("lu-sparql-renderer-source/1\n");

    [TestMethod]
    public void OpenIsTheOnlyWayIn()
    {
        // The construction surface, not a convention. A public constructor here would let a
        // caller pair any reference with any bytes and the digest would stop being the authority.
        var constructors = typeof(MachineQueryRendererSource).GetConstructors();

        Assert.AreEqual(0, constructors.Length, "no public constructor may exist");
        Assert.IsNotNull(
            typeof(MachineQueryRendererSource).GetMethod(
                nameof(MachineQueryRendererSource.Open)),
            "Open is the single entry");
    }

    [TestMethod]
    public void BytesCarryingTheDigestOpen()
    {
        var reference = Reference(Bytes);

        var source = MachineQueryRendererSource.Open(reference, Bytes);

        Assert.AreEqual(reference, source.Reference);
        CollectionAssert.AreEqual(Bytes, source.CopyBytes().ToArray());
    }

    [TestMethod]
    public void BytesThatDoNotCarryTheDigestAreRefused()
    {
        var reference = Reference(Bytes);
        var other = Encoding.UTF8.GetBytes("lu-sparql-renderer-source/2\n");

        Assert.ThrowsExactly<ArgumentException>(
            () => MachineQueryRendererSource.Open(reference, other));
    }

    [TestMethod]
    public void ASingleFlippedBitIsRefused()
    {
        // The near miss rather than the obvious one. A length-equal, mostly-equal artifact is what
        // a truncated read or a swapped fixture actually produces, and a check written against
        // length or a prefix would pass it.
        var reference = Reference(Bytes);
        var nearly = Bytes.ToArray();
        nearly[^1] ^= 0x01;

        Assert.ThrowsExactly<ArgumentException>(
            () => MachineQueryRendererSource.Open(reference, nearly));
    }

    [TestMethod]
    public void EmptyBytesDoNotOpenANonEmptyReference()
    {
        // Named because the defect this whole change chased was a null that had silently become an
        // empty ReadOnlyMemory. If empty were ever accepted here, that conversion would come back
        // as a retained empty artifact under a true digest.
        var reference = Reference(Bytes);

        Assert.ThrowsExactly<ArgumentException>(
            () => MachineQueryRendererSource.Open(reference, ReadOnlySpan<byte>.Empty));
    }

    [TestMethod]
    public void TheHeldBytesAreCopiedOnTheWayInAndOut()
    {
        var mutable = Bytes.ToArray();
        var source = MachineQueryRendererSource.Open(Reference(Bytes), mutable);

        mutable[0] ^= 0xff;

        CollectionAssert.AreEqual(
            Bytes,
            source.CopyBytes().ToArray(),
            "the caller's array cannot change what was verified");

        // The outbound half has to be asserted on the memory itself, not on ToArray() of it.
        // The first version of this test mutated source.CopyBytes().ToArray() and compared the
        // result, which copies at the call site and so passed whether or not CopyBytes copied
        // anything: it survived the mutation that returns the held array directly. ReadOnlyMemory
        // is read-only to its holder but still hands out the underlying array through
        // MemoryMarshal, so aliasing is what has to be measured.
        Assert.IsTrue(MemoryMarshal.TryGetArray(source.CopyBytes(), out var first));
        Assert.IsTrue(MemoryMarshal.TryGetArray(source.CopyBytes(), out var second));
        Assert.AreNotSame(
            first.Array,
            second.Array,
            "each call must hand back its own array, never the one being held");
    }

    private static SourceArtifactRef Reference(ReadOnlySpan<byte> bytes) => new(
        "urn:uuid:9f1c2d3e-4a5b-4c6d-8e7f-0a1b2c3d4e5f",
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
}

using System.Buffers.Binary;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A zip a test writes, made the same bytes on every system. <see cref="System.IO.Compression.ZipArchive"/> records the
/// system that wrote each entry in its central-directory header ("version made by": MS-DOS on Windows, Unix elsewhere) and
/// gives the entry that system's default external attributes (none on Windows; a regular file's mode on Unix), so a package a
/// test rewrote on Linux had another digest than the one recorded on Windows: the envelope samples' <c>package_sha256</c> of
/// the EU act of 2013-06-26 (CI on #989). Every header is set to what Windows writes: made by MS-DOS, with the zip version the
/// entry needs (the header's low byte, which depends on the entry alone), and no external attributes. Nothing else in a zip
/// .NET writes depends on the system: the local headers carry no system, and the times are the ones the test sets.
/// </summary>
internal static class PortableZip
{
    private const uint CentralDirectoryHeader = 0x02014b50;
    private const uint EndOfCentralDirectory = 0x06054b50;

    /// <summary>The same zip, each central-directory header made by MS-DOS with no external attributes.</summary>
    internal static byte[] Normalise(byte[] zip)
    {
        var bytes = (byte[])zip.Clone();
        var end = EndOfCentralDirectoryOffset(bytes);
        var span = bytes.AsSpan();
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(span[(end + 10)..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(span[(end + 12)..]);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(span[(end + 16)..]);
        if (entries == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
            throw new NotSupportedException("A zip64 archive is not normalised here; a test's package is small.");

        var position = checked((int)offset);
        for (var index = 0; index < entries; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(span[position..]) != CentralDirectoryHeader)
                throw new InvalidDataException($"Entry {index}'s central-directory header is not at offset {position}.");
            // Bytes 4 and 5: version made by. Byte 4 is the zip version, byte 5 the system: 0, MS-DOS, as Windows writes.
            span[position + 5] = 0;
            // Bytes 38 to 41: the external attributes, none, as Windows writes.
            BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 38)..], 0);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(position + 28)..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(position + 30)..]);
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(position + 32)..]);
            position += 46 + nameLength + extraLength + commentLength;
        }

        if (position != checked((int)(offset + size)))
            throw new InvalidDataException($"The central directory ends at {position}, not at {offset + size} as its end record says.");
        return bytes;
    }

    private static int EndOfCentralDirectoryOffset(byte[] bytes)
    {
        // The end record is 22 bytes and may be followed by a comment of up to 65,535 bytes.
        for (var position = bytes.Length - 22; position >= Math.Max(0, bytes.Length - 22 - ushort.MaxValue); position--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position)) == EndOfCentralDirectory)
                return position;
        }

        throw new InvalidDataException("The zip has no end-of-central-directory record.");
    }
}

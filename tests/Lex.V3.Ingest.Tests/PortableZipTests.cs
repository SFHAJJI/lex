using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The packages the tests rewrite are the same bytes on every system (<see cref="PortableZip"/>). The envelope samples record
/// the digest of the GDPR's package rewritten to the date of 2013-06-26, recorded on Windows; on Linux the same rewrite had
/// another digest, since .NET writes the system into each central-directory header (CI on #989). Each digest below is the one
/// this test reads on Windows and on Linux alike.
/// </summary>
[TestClass]
public sealed class PortableZipTests
{
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [TestMethod]
    public void TheRewrittenPackagesAreTheSameBytesOnEverySystem()
    {
        var english = EuFirstMountAcquisitionTests.GdprEnglishPackage();
        var dated = EuFirstMountAcquisitionTests.RewrittenPackage(english, "ISO=\"20160427\"", "ISO=\"20130626\"");
        // The envelope samples' package_sha256 of the EU act of 2013-06-26 (schemas/v3-platform/envelope-samples.json).
        Assert.AreEqual("7ac5d154ce86b2f3bac7092c98dec909fe6c2c0f1500376f19d27d42214b8680", Sha(dated));
        Assert.AreEqual("0decc574cd11b2fc65bc49fe2193960d63ed3925e5c39d8e9183199ffc7bb50a", Sha(EuFirstMountAcquisitionTests.RewrittenPackage(english, "natural persons", "natural humans")));
        Assert.AreEqual("57cb1a499c97e8bfa7e90bdda594a53f8370b9809387129f0ba7deb891e86484", Sha(EuFirstMountAcquisitionTests.FrenchOf(english)));
        Assert.AreEqual("c13a381bab7da6c522e00a9091e39e00d05e05509d842e194001284535dc7e53", Sha(EuAcquisitionTestFixture.SyntheticFrenchMainBodyPackage()));
        foreach (var package in new[] { dated, EuFirstMountAcquisitionTests.FrenchOf(english), EuAcquisitionTestFixture.SyntheticFrenchMainBodyPackage() })
            AssertMadeByMsDosWithNoAttributes(package);
    }

    [TestMethod]
    public void AZipWrittenOnUnixIsNormalisedToTheBytesWindowsWrites()
    {
        // Each system writes its own headers, so both are made from the one written here: each central-directory header said to
        // be made by Unix (3) with a regular file's mode 0644 as its attributes, as .NET writes on Linux, or by MS-DOS (0) with
        // none, as it writes on Windows.
        var written = SmallZip();
        byte[] MadeBy(byte system, uint attributes)
        {
            var bytes = (byte[])written.Clone();
            foreach (var header in CentralDirectoryHeaders(bytes))
            {
                bytes[header + 5] = system;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(header + 38), attributes);
            }

            return bytes;
        }

        var unix = MadeBy(3, 0x81A4u << 16);
        var windows = MadeBy(0, 0);
        Assert.AreNotEqual(Sha(windows), Sha(unix));
        var normalised = PortableZip.Normalise(unix);
        CollectionAssert.AreEqual(windows, normalised, "a zip written on Unix is normalised to the bytes Windows writes");
        CollectionAssert.AreEqual(windows, PortableZip.Normalise(windows), "and Windows's bytes are left as they are");
        CollectionAssert.AreEqual(windows, PortableZip.Normalise(written), "whichever system wrote it");
        AssertMadeByMsDosWithNoAttributes(normalised);
        // Only the system and the attributes change: the entries read back as written.
        using var archive = new ZipArchive(new MemoryStream(normalised), ZipArchiveMode.Read);
        CollectionAssert.AreEqual(new[] { "a.xml", "b.xml" }, archive.Entries.Select(static entry => entry.FullName).ToArray());
        using var reader = new StreamReader(archive.Entries[1].Open(), Encoding.UTF8);
        Assert.AreEqual("<b>second</b>", reader.ReadToEnd());
    }

    [TestMethod]
    public void WhatIsNotAZipIsRefused()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => PortableZip.Normalise(Encoding.UTF8.GetBytes(new string('x', 64))));
        var truncated = SmallZip();
        // The end record names a central directory that is not where it says.
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(truncated.Length - 22 + 16), 1);
        Assert.ThrowsExactly<InvalidDataException>(() => PortableZip.Normalise(truncated));
    }

    private static byte[] SmallZip()
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in new[] { ("a.xml", "<a>first</a>"), ("b.xml", "<b>second</b>") })
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(text);
            }

        return bytes.ToArray();
    }

    private static void AssertMadeByMsDosWithNoAttributes(byte[] zip)
    {
        var headers = CentralDirectoryHeaders(zip).ToArray();
        Assert.IsNotEmpty(headers);
        foreach (var header in headers)
        {
            Assert.AreEqual(0, zip[header + 5], $"the header at {header} names the system that made it");
            Assert.AreEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(header + 38)), $"the header at {header} carries attributes");
        }
    }

    private static IEnumerable<int> CentralDirectoryHeaders(byte[] zip)
    {
        var end = zip.Length - 22;
        while (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end)) != 0x06054b50) end--;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(end + 10));
        var position = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end + 16));
        for (var index = 0; index < count; index++)
        {
            yield return position;
            position += 46 + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(position + 28))
                + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(position + 30))
                + BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(position + 32));
        }
    }
}

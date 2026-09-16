using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Rights;

namespace Lex.V3.Tests.Contracts.Source.Rights;

[TestClass]
public sealed class LicenceCensusExecutionReceiptTests
{
    private const string EmptySha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [TestMethod]
    public void CanonicalReceiptRoundTripsAndBindsBothDerivedIdentities()
    {
        var receipt = Fixture.Create();
        var bytes = receipt.CopyCanonicalBytes();

        var read = LicenceCensusExecutionReceipt.ParseAndVerify(bytes);

        CollectionAssert.AreEqual(bytes, read.CopyCanonicalBytes());
        Assert.AreEqual(Fixture.InputSha256, read.Inventory.CensusInputSha256);
        Assert.AreEqual(Fixture.OutputSha256, read.CensusOutputSha256);
        Assert.AreEqual((ulong)2, read.Inventory.RecordCount);
        Assert.AreEqual(7, read.Runs.Count);
        Assert.AreEqual((byte)'\n', bytes[^1]);
    }

    private static class Fixture
    {
        private const string Commit = "1111111111111111111111111111111111111111";
        private const string Tree = "2222222222222222222222222222222222222222";
        private const string Works = "3333333333333333333333333333333333333333";
        private const string StructuralVocabulary =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string StructuralCounts =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private static readonly string[] RunNames =
        [
            "probe_akn_licence.py",
            "probe_archive.py",
            "probe_block_repeats.py",
            "probe_edge_cases.py",
            "probe_manifestation_licence.py",
            "probe_schema_binding.py",
            "probe_scl_names.py",
        ];

        public static string InputSha256 => CensusInput(
            Commit,
            [
                new LicenceCensusGitBlob("works/a.xml", Filled('1')),
                new LicenceCensusGitBlob("works/b.xml", Filled('2')),
            ]);

        public static string OutputSha256
        {
            get
            {
                var runs = Runs();
                return CensusOutput(InputSha256, runs, StructuralVocabulary, StructuralCounts);
            }
        }

        public static LicenceCensusExecutionReceipt Create()
        {
            var selection = new[]
            {
                new LicenceCensusGitBlob("works/a.xml", Filled('1')),
                new LicenceCensusGitBlob("works/b.xml", Filled('2')),
            };
            var runs = Runs();
            return LicenceCensusExecutionReceipt.Create(
                [new LicenceCensusSourceArtifact("probes/probe.py", 3, Filled('9'))],
                new LicenceCensusCorpus(
                    "https://github.com/example/corpus.git",
                    Commit,
                    Tree,
                    Works,
                    "2026-09-16T00:00:00.0000000Z"),
                selection,
                InputSha256,
                runs,
                StructuralVocabulary,
                StructuralCounts,
                OutputSha256,
                "2026-09-16T00:01:00.0000000Z");
        }

        private static IReadOnlyList<LicenceCensusRun> Runs() => RunNames
            .Select((name, index) => new LicenceCensusRun(
                name,
                Filled((char)('3' + index)),
                "python3",
                Filled('c'),
                "3.13.7",
                "python",
                "3.13.7",
                "windows",
                "x64",
                "corpus-root",
                [name],
                LicenceCensusStdinMode.None,
                0,
                EmptySha256,
                [new LicenceCensusEnvironmentVariable("PYTHONUTF8", "1")],
                "Invariant",
                "UTC",
                $"2026-09-16T00:00:{index:00}.0000000Z",
                $"2026-09-16T00:00:{index + 1:00}.0000000Z",
                0,
                0,
                EmptySha256,
                0,
                EmptySha256))
            .ToArray();

        private static string CensusInput(
            string commit,
            IReadOnlyList<LicenceCensusGitBlob> selection)
        {
            using var stream = new MemoryStream();
            WriteLp(stream, Encoding.ASCII.GetBytes("lex-license-census-input/1"));
            WriteLp(stream, Encoding.ASCII.GetBytes(commit));
            WriteLp(stream, U64((ulong)selection.Count));
            foreach (var blob in selection)
            {
                using var record = new MemoryStream();
                WriteLp(record, Encoding.UTF8.GetBytes(blob.Path));
                WriteLp(record, Convert.FromHexString(blob.Sha256));
                WriteLp(stream, record.ToArray());
            }

            return Sha(stream.ToArray());
        }

        private static string CensusOutput(
            string input,
            IReadOnlyList<LicenceCensusRun> runs,
            string vocabulary,
            string counts)
        {
            using var stream = new MemoryStream();
            WriteLp(stream, Encoding.ASCII.GetBytes("lex-license-census-output/1"));
            WriteLp(stream, Convert.FromHexString(input));
            WriteLp(stream, U64((ulong)runs.Count));
            foreach (var run in runs)
            {
                var runBytes = LicenceCensusExecutionReceipt.CopyCanonicalRunBytes(run);
                using var preimage = new MemoryStream();
                WriteLp(preimage, Encoding.ASCII.GetBytes("lex-license-census-run/1"));
                WriteLp(preimage, runBytes);
                WriteLp(stream, SHA256.HashData(preimage.ToArray()));
            }

            WriteLp(stream, Convert.FromHexString(vocabulary));
            WriteLp(stream, Convert.FromHexString(counts));
            return Sha(stream.ToArray());
        }

        private static byte[] U64(ulong value)
        {
            var bytes = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            return bytes;
        }

        private static void WriteLp(Stream stream, byte[] value)
        {
            stream.Write(U64((ulong)value.Length));
            stream.Write(value);
        }

        private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        private static string Filled(char value) => new(value, 64);
    }
}

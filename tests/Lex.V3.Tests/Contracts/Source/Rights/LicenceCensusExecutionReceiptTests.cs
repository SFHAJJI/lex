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

    [TestMethod]
    public void ClosedJsonRejectsDuplicateUnknownNullWrongTypeAndNoncanonicalOrder()
    {
        var json = Encoding.UTF8.GetString(Fixture.Create().CopyCanonicalBytes());
        var mutations = new[]
        {
            json.Replace("{\"schema\":", "{\"schema\":\"lex-census-execution-receipt/1\",\"schema\":", StringComparison.Ordinal),
            json.Replace("{\"schema\":", "{\"unknown\":0,\"schema\":", StringComparison.Ordinal),
            json.Replace("\"source_artifacts\":[", "\"source_artifacts\":null,\"discarded\":[", StringComparison.Ordinal),
            json.Replace("\"record_count\":2", "\"record_count\":\"2\"", StringComparison.Ordinal),
            json.Replace("\"executor_token\":\"python3\"", "\"executor_token\":\"\\u0070ython3\"", StringComparison.Ordinal),
            json.Replace(
                "\"structural_vocabulary_sha256\":\"" + new string('a', 64) + "\",\"structural_counts_sha256\":\"" + new string('b', 64) + "\"",
                "\"structural_counts_sha256\":\"" + new string('b', 64) + "\",\"structural_vocabulary_sha256\":\"" + new string('a', 64) + "\"",
                StringComparison.Ordinal),
        };

        foreach (var mutation in mutations)
        {
            Assert.Throws<ArgumentException>(() =>
                LicenceCensusExecutionReceipt.ParseAndVerify(Encoding.UTF8.GetBytes(mutation)));
        }
    }

    [TestMethod]
    public void DerivedDigestsCannotBeSuppliedByTheCaller()
    {
        Assert.Throws<ArgumentException>(() => Fixture.Create(censusInputSha256: new string('f', 64)));
        Assert.Throws<ArgumentException>(() => Fixture.Create(censusOutputSha256: new string('f', 64)));
    }

    [TestMethod]
    public void EveryRequiredRunAppearsExactlyOnceInAsciiOrder()
    {
        Assert.Throws<ArgumentException>(() => Fixture.Create(runs: Fixture.Runs().Reverse().ToArray()));
        Assert.Throws<ArgumentException>(() => Fixture.Create(runs: Fixture.Runs().Take(6).ToArray()));
    }

    [TestMethod]
    public void OrderedCollectionsRefuseDuplicatesAndReordering()
    {
        Assert.Throws<ArgumentException>(() => Fixture.Create(sourceArtifacts:
        [
            new LicenceCensusSourceArtifact("z", 1, new string('8', 64)),
            new LicenceCensusSourceArtifact("a", 1, new string('9', 64)),
        ]));
        Assert.Throws<ArgumentException>(() => Fixture.Create(selection:
        [
            new LicenceCensusGitBlob("works/b.xml", new string('2', 64)),
            new LicenceCensusGitBlob("works/a.xml", new string('1', 64)),
        ]));
        Assert.Throws<ArgumentException>(() => Fixture.Run(0, environment:
        [
            new LicenceCensusEnvironmentVariable("TZ", "UTC"),
            new LicenceCensusEnvironmentVariable("LANG", "C"),
        ]));
    }

    [TestMethod]
    public void PathOrderIsUnsignedUtf8RatherThanUtf16CodeUnitOrder()
    {
        var receipt = Fixture.Create(sourceArtifacts:
        [
            new LicenceCensusSourceArtifact("artifacts/\uE000", 1, new string('8', 64)),
            new LicenceCensusSourceArtifact("artifacts/\U00010000", 1, new string('9', 64)),
        ]);

        Assert.AreEqual("artifacts/\uE000", receipt.SourceArtifacts[0].Path);
        Assert.AreEqual("artifacts/\U00010000", receipt.SourceArtifacts[1].Path);
    }

    [TestMethod]
    public void ExecutionBoundaryRefusesPhysicalPathsAmbientStateAndUnboundedOutput()
    {
        Assert.Throws<ArgumentException>(() => Fixture.Run(0, executorToken: "C:\\Python\\python.exe"));
        Assert.Throws<ArgumentException>(() => Fixture.Run(0, argv: ["C:\\corpus\\probe.py"]));
        Assert.Throws<ArgumentException>(() => Fixture.Run(0, logicalCwd: "C:\\corpus"));
        Assert.Throws<ArgumentException>(() =>
            new LicenceCensusEnvironmentVariable("PATH", "anything"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixture.Run(0, stdoutLength: 16_777_217));
    }

    [TestMethod]
    public void NoneStdinAndZeroLengthStreamsBindTheEmptyDigest()
    {
        Assert.Throws<ArgumentException>(() => Fixture.Run(0, stdinSha256: new string('d', 64)));
        Assert.Throws<ArgumentException>(() => Fixture.Run(0, stdoutSha256: new string('d', 64)));
    }

    private static class Fixture
    {
        private const string StructuralVocabulary =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string StructuralCounts =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private static readonly string[] RunNames =
        [
            "probe_akn_licence.py", "probe_archive.py", "probe_block_repeats.py",
            "probe_edge_cases.py", "probe_manifestation_licence.py",
            "probe_schema_binding.py", "probe_scl_names.py",
        ];

        public const string InputSha256 =
            "7fd1f8c9c55a4961073cdf0a538859dcdcf1e500869d917f8c9726f604fa8fa8";
        public const string OutputSha256 =
            "a63e64b07262a048cf2ca3b01ae52749c48bf76c2602718b8385cd5ccadbbbf3";

        public static LicenceCensusExecutionReceipt Create(
            IReadOnlyList<LicenceCensusSourceArtifact>? sourceArtifacts = null,
            IReadOnlyList<LicenceCensusGitBlob>? selection = null,
            string? censusInputSha256 = null,
            IReadOnlyList<LicenceCensusRun>? runs = null,
            string? censusOutputSha256 = null)
        {
            selection ??=
            [
                new LicenceCensusGitBlob("works/a.xml", Filled('1')),
                new LicenceCensusGitBlob("works/b.xml", Filled('2')),
            ];
            runs ??= Runs();
            return LicenceCensusExecutionReceipt.Create(
                sourceArtifacts ?? [new LicenceCensusSourceArtifact("probes/probe.py", 3, Filled('9'))],
                new LicenceCensusCorpus(
                    "https://github.com/example/corpus.git",
                    new string('1', 40), new string('2', 40), new string('3', 40),
                    "2026-09-16T00:00:00.0000000Z"),
                selection,
                censusInputSha256 ?? InputSha256,
                runs,
                StructuralVocabulary,
                StructuralCounts,
                censusOutputSha256 ?? OutputSha256,
                "2026-09-16T00:01:00.0000000Z");
        }

        public static IReadOnlyList<LicenceCensusRun> Runs() => RunNames
            .Select((_, index) => Run(index)).ToArray();

        public static LicenceCensusRun Run(
            int index,
            string executorToken = "python3",
            string logicalCwd = "corpus-root",
            IReadOnlyList<string>? argv = null,
            IReadOnlyList<LicenceCensusEnvironmentVariable>? environment = null,
            ulong stdoutLength = 0,
            string? stdinSha256 = null,
            string? stdoutSha256 = null) => new(
                RunNames[index], Filled((char)('3' + index)), executorToken, Filled('c'),
                "3.13.7", "python", "3.13.7", "windows", "x64", logicalCwd,
                argv ?? [RunNames[index]], LicenceCensusStdinMode.None, 0,
                stdinSha256 ?? EmptySha256,
                environment ?? [new LicenceCensusEnvironmentVariable("PYTHONUTF8", "1")],
                "Invariant", "UTC", $"2026-09-16T00:00:{index:00}.0000000Z",
                $"2026-09-16T00:00:{index + 1:00}.0000000Z", 0, stdoutLength,
                stdoutSha256 ?? EmptySha256, 0, EmptySha256);

        private static string Filled(char value) => new(value, 64);
    }
}

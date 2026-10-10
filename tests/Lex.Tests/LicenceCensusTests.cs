using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Ingest;

namespace Lex.Tests;

public sealed class LicenceCensusTests : IDisposable
{
    private const string RunIdentity = "gha:2026-08-30T131500Z";
    private const string CodeCommit =
        "b910816255a0099e9758b84b0dcccc6c76418b5f";
    private const string CorpusCommit =
        "c087f9153a8cde5429965ffa897db001f3acdf09";
    private const string CorpusTree =
        "cb80a698f47f98add651d090af53e4a095d5fdf5";
    private const string WorksTree =
        "0123456789abcdef0123456789abcdef01234567";
    private const string EmptySha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private static readonly DateTimeOffset StartedAt =
        DateTimeOffset.Parse("2026-08-30T00:00:00Z");
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-licence-census-{Guid.NewGuid():N}");

    public LicenceCensusTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Fresh_run_retains_both_channels_before_sealing()
    {
        var handler = new CensusHandler();
        using var http = new HttpClient(handler);
        var privateRoot = EmptyDirectory("complete");

        var result = await LicenceCensus.AcquireLuObservationsAsync(
            privateRoot,
            RunIdentity,
            CodeCommit,
            StartedAt,
            http,
            new LicenceCensusAcquisitionOptions(
                PageSize: 2,
                MaximumRows: 8,
                Pause: TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(RunIdentity, result.RunIdentity);
        Assert.Equal(2, result.CoordinateCount);
        Assert.Equal(4, result.ObservationCount);
        Assert.Equal("lex-private-lu-dual-channel-observation-run/1",
            JsonDocument.Parse(result.ManifestBytes).RootElement
                .GetProperty("schema").GetString());
        Assert.Equal((byte)'\n', result.ManifestBytes[^1]);
        Assert.Equal(Sha256(result.ManifestBytes), result.ManifestSha256);
        Assert.DoesNotContain("<akomaNtoso", Encoding.UTF8.GetString(
            result.ManifestBytes), StringComparison.Ordinal);

        foreach (var observation in result.Observations)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(
                privateRoot,
                observation.ObjectRelativePath.Replace('/',
                    Path.DirectorySeparatorChar)));
            Assert.Equal(observation.Length, checked((ulong)bytes.Length));
            Assert.Equal(observation.Sha256, Sha256(bytes));
        }
        Assert.Equal(3, handler.PostCount);
        Assert.Equal(2, handler.GetCount);
    }

    [Fact]
    public async Task Count_data_count_drift_retains_raw_evidence_and_blocks_files()
    {
        var handler = new CensusHandler(countDrift: true);
        using var http = new HttpClient(handler);
        var privateRoot = EmptyDirectory("count-drift");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LicenceCensus.AcquireLuObservationsAsync(
                privateRoot,
                RunIdentity,
                CodeCommit,
                StartedAt,
                http,
                new LicenceCensusAcquisitionOptions(
                    PageSize: 2,
                    MaximumRows: 8,
                    Pause: TimeSpan.Zero),
                CancellationToken.None));

        Assert.Contains("count-data-count", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(3, handler.PostCount);
        Assert.Equal(0, handler.GetCount);
        Assert.False(File.Exists(Path.Combine(
            privateRoot, LicenceCensus.ObservationManifestFileName)));
        Assert.Equal(3, Directory.EnumerateFiles(
            Path.Combine(privateRoot, "http"), "*.json",
            SearchOption.TopDirectoryOnly).Count());
    }

    [Fact]
    public async Task Partial_file_channel_fails_closed_but_retains_raw_response()
    {
        var handler = new CensusHandler(failSecondFile: true);
        using var http = new HttpClient(handler);
        var privateRoot = EmptyDirectory("partial");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LicenceCensus.AcquireLuObservationsAsync(
                privateRoot,
                RunIdentity,
                CodeCommit,
                StartedAt,
                http,
                new LicenceCensusAcquisitionOptions(
                    PageSize: 2,
                    MaximumRows: 8,
                    Pause: TimeSpan.Zero),
                CancellationToken.None));

        Assert.Contains("complete same-run SPARQL and file observations",
            error.Message, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.EnumerateFiles(
            Path.Combine(privateRoot, "sha256"), "*",
            SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(
            privateRoot, LicenceCensus.ObservationManifestFileName)));
    }

    [Fact]
    public async Task Redirect_or_stale_response_is_rejected_before_it_can_seal()
    {
        var handler = new CensusHandler(redirectEnumeration: true);
        using var http = new HttpClient(handler);
        var privateRoot = EmptyDirectory("redirect");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LicenceCensus.AcquireLuObservationsAsync(
                privateRoot,
                RunIdentity,
                CodeCommit,
                StartedAt,
                http,
                new LicenceCensusAcquisitionOptions(
                    PageSize: 2,
                    MaximumRows: 8,
                    Pause: TimeSpan.Zero),
                CancellationToken.None));

        Assert.Contains("effective URI", error.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(
            privateRoot, LicenceCensus.ObservationManifestFileName)));
    }

    [Fact]
    public async Task File_overflow_retains_exact_cap_plus_one_and_never_seals()
    {
        using var http = new HttpClient(new CensusHandler());
        var privateRoot = EmptyDirectory("file-overflow");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LicenceCensus.AcquireLuObservationsAsync(
                privateRoot,
                RunIdentity,
                CodeCommit,
                StartedAt,
                http,
                new LicenceCensusAcquisitionOptions(
                    PageSize: 2,
                    MaximumRows: 8,
                    Pause: TimeSpan.Zero,
                    FileMaximumBytes: 3),
                CancellationToken.None));

        Assert.Contains("partial or invalid", error.Message,
            StringComparison.Ordinal);
        Assert.Contains(Directory.EnumerateFiles(
                Path.Combine(privateRoot, "sha256"), "*",
                SearchOption.AllDirectories),
            path => new FileInfo(path).Length == 4);
        Assert.False(File.Exists(Path.Combine(
            privateRoot, LicenceCensus.ObservationManifestFileName)));
    }

    [Fact]
    public async Task Duplicate_sparql_property_is_rejected_after_raw_capture()
    {
        using var http = new HttpClient(new CensusHandler(duplicateData: true));
        var privateRoot = EmptyDirectory("duplicate-sparql");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LicenceCensus.AcquireLuObservationsAsync(
                privateRoot,
                RunIdentity,
                CodeCommit,
                StartedAt,
                http,
                new LicenceCensusAcquisitionOptions(
                    PageSize: 2,
                    MaximumRows: 8,
                    Pause: TimeSpan.Zero),
                CancellationToken.None));

        Assert.Contains("duplicate property", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(2, Directory.EnumerateFiles(
            Path.Combine(privateRoot, "http"), "*.json",
            SearchOption.TopDirectoryOnly).Count());
        Assert.False(File.Exists(Path.Combine(
            privateRoot, LicenceCensus.ObservationManifestFileName)));
    }

    [Fact]
    public async Task Missing_effective_uri_is_not_invented_from_the_request()
    {
        using var http = new HttpClient(new CensusHandler(omitEffectiveUri: true));
        var privateRoot = EmptyDirectory("missing-effective-uri");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LicenceCensus.AcquireLuObservationsAsync(
                privateRoot,
                RunIdentity,
                CodeCommit,
                StartedAt,
                http,
                new LicenceCensusAcquisitionOptions(
                    PageSize: 2,
                    MaximumRows: 8,
                    Pause: TimeSpan.Zero),
                CancellationToken.None));

        Assert.Contains("effective URI", error.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(
            privateRoot, LicenceCensus.ObservationManifestFileName)));
    }

    [Fact]
    public async Task Repeated_offset_page_cannot_pass_as_a_complete_enumeration()
    {
        using var http = new HttpClient(new CensusHandler(repeatDataPage: true));
        var privateRoot = EmptyDirectory("repeated-page");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LicenceCensus.AcquireLuObservationsAsync(
                privateRoot,
                RunIdentity,
                CodeCommit,
                StartedAt,
                http,
                new LicenceCensusAcquisitionOptions(
                    PageSize: 2,
                    MaximumRows: 8,
                    Pause: TimeSpan.Zero),
                CancellationToken.None));

        Assert.Contains("duplicate exact row", error.Message,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            privateRoot, LicenceCensus.ObservationManifestFileName)));
    }

    [Fact]
    public async Task One_manifestation_may_not_cross_observation_pages()
    {
        using var http = new HttpClient(new CensusHandler(splitManifestation: true));
        var privateRoot = EmptyDirectory("split-manifestation");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LicenceCensus.AcquireLuObservationsAsync(
                privateRoot,
                RunIdentity,
                CodeCommit,
                StartedAt,
                http,
                new LicenceCensusAcquisitionOptions(
                    PageSize: 1,
                    MaximumRows: 8,
                    Pause: TimeSpan.Zero),
                CancellationToken.None));

        Assert.Contains("crossed data page boundaries", error.Message,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            privateRoot, LicenceCensus.ObservationManifestFileName)));
    }

    [Fact]
    public async Task Receipt_is_exact_obs02_identity_and_canonical_order()
    {
        var observation = await ObservationReceipt("receipt");
        var artifacts = ProbeNames().Select((name, index) =>
            new LicenceCensusSourceArtifact(
                name,
                Encoding.UTF8.GetBytes($"probe-{index}\n"))).ToArray();
        var runs = ProbeNames().Select((name, index) => ProbeRun(
            name,
            artifacts[index].Sha256,
            observation.ManifestBytes,
            StartedAt.AddMinutes(index + 1),
            StartedAt.AddMinutes(index + 1).AddSeconds(1),
            $"counter_{index}")).ToArray();
        var blobs = new[]
        {
            new LicenceCensusBlob("works/a.xml", Sha256([1, 2, 3])),
            new LicenceCensusBlob("works/b.xml", Sha256([4, 5])),
        };

        var result = LicenceCensusReceiptBuilder.Build(
            observation,
            artifacts,
            new LicenceCensusCorpus(
                "github.com/SFHAJJI/lex-corpus-lu-legilux",
                CorpusCommit,
                CorpusTree,
                WorksTree,
                StartedAt),
            blobs,
            runs,
            StartedAt.AddDays(1));

        Assert.Equal((byte)'\n', result.ReceiptBytes[^1]);
        Assert.Equal("lex-census-execution-receipt/1", result.Schema);
        using var document = JsonDocument.Parse(result.ReceiptBytes);
        Assert.Equal(new[]
        {
            "schema", "source_artifacts", "corpus", "inventory", "runs",
            "structural_vocabulary_sha256", "structural_counts_sha256",
            "census_output_sha256", "created_at",
        }, document.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(ProbeNames(), document.RootElement.GetProperty("runs")
            .EnumerateArray().Select(run => run.GetProperty("name").GetString()!));
        Assert.Equal(result.CensusInputSha256,
            document.RootElement.GetProperty("inventory")
                .GetProperty("census_input_sha256").GetString());
        Assert.Equal(result.ReceiptSha256, Sha256(result.ReceiptBytes));
        Assert.Equal("2026-08-31T00:00:00.0000000Z",
            document.RootElement.GetProperty("created_at").GetString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-stdin")]
    [InlineData("nonzero")]
    [InlineData("stale")]
    [InlineData("wrong-script")]
    public async Task Receipt_refuses_partial_old_or_mixed_run_inputs(string mutation)
    {
        var observation = await ObservationReceipt("mutation-" + mutation);
        var artifacts = ProbeNames().Select((name, index) =>
            new LicenceCensusSourceArtifact(
                name,
                Encoding.UTF8.GetBytes($"probe-{index}\n"))).ToArray();
        var runs = ProbeNames().Select((name, index) => ProbeRun(
            name,
            artifacts[index].Sha256,
            observation.ManifestBytes,
            StartedAt.AddMinutes(index + 1),
            StartedAt.AddMinutes(index + 1).AddSeconds(1),
            $"counter_{index}")).ToList();

        switch (mutation)
        {
            case "missing":
                runs.RemoveAt(0);
                break;
            case "wrong-stdin":
                runs[0] = runs[0] with { Stdin = Encoding.UTF8.GetBytes("old-run\n") };
                break;
            case "nonzero":
                runs[0] = runs[0] with { ExitCode = 1 };
                break;
            case "stale":
                runs[0] = runs[0] with
                {
                    StartedAt = StartedAt.AddSeconds(-2),
                    EndedAt = StartedAt.AddSeconds(-1),
                };
                break;
            case "wrong-script":
                runs[0] = runs[0] with { ScriptSha256 = new string('f', 64) };
                break;
        }

        Assert.Throws<InvalidDataException>(() =>
            LicenceCensusReceiptBuilder.Build(
                observation,
                artifacts,
                new LicenceCensusCorpus(
                    "github.com/SFHAJJI/lex-corpus-lu-legilux",
                    CorpusCommit,
                    CorpusTree,
                    WorksTree,
                    StartedAt),
                [new LicenceCensusBlob("works/a.xml", Sha256([1]))],
                runs,
                StartedAt.AddDays(1)));
    }

    [Fact]
    public void Inventory_digest_binds_case_sensitive_path_blob_and_commit()
    {
        var records = new[]
        {
            new LicenceCensusBlob("works/a.xml", Sha256([1, 2, 3])),
            new LicenceCensusBlob("works/A.xml", Sha256([4, 5, 6])),
        };

        var baseline = LicenceCensusReceiptBuilder.ComputeCensusInputSha256(
            CorpusCommit, records);
        var reordered = LicenceCensusReceiptBuilder.ComputeCensusInputSha256(
            CorpusCommit, records.Reverse().ToArray());
        var changedCase = LicenceCensusReceiptBuilder.ComputeCensusInputSha256(
            CorpusCommit,
            [records[0], records[1] with { Path = "works/a.XML" }]);
        var changedBlob = LicenceCensusReceiptBuilder.ComputeCensusInputSha256(
            CorpusCommit,
            [records[0], records[1] with { Sha256 = Sha256([9]) }]);
        var changedCommit = LicenceCensusReceiptBuilder.ComputeCensusInputSha256(
            new string('a', 40), records);

        Assert.Equal(baseline, reordered);
        Assert.NotEqual(baseline, changedCase);
        Assert.NotEqual(baseline, changedBlob);
        Assert.NotEqual(baseline, changedCommit);
    }

    [Fact]
    public async Task Git_inventory_reads_exact_detached_blobs_not_dirty_checkout()
    {
        var repository = EmptyDirectory("git-inventory");
        Directory.CreateDirectory(Path.Combine(repository, "works"));
        var tracked = Path.Combine(repository, "works", "a.xml");
        var upper = Path.Combine(repository, "works", "ignored.XML");
        await File.WriteAllBytesAsync(tracked, [1, 2, 3]);
        await File.WriteAllBytesAsync(upper, [4, 5, 6]);
        RunGit(repository, "init", "--quiet");
        RunGit(repository, "add", "works/a.xml", "works/ignored.XML");
        RunGit(repository,
            "-c", "user.name=Lex Test",
            "-c", "user.email=lex-test@example.invalid",
            "commit", "--quiet", "-m", "fixture");
        var commit = RunGit(repository, "rev-parse", "HEAD").Trim();
        await File.WriteAllBytesAsync(tracked, [9, 9, 9]);
        await File.WriteAllBytesAsync(
            Path.Combine(repository, "works", "untracked.xml"), [7]);

        var snapshot = await LicenceCensus.ReadGitInventoryAsync(
            repository,
            "github.com/example/private-corpus",
            commit,
            StartedAt,
            CancellationToken.None);

        var item = Assert.Single(snapshot.Inventory);
        Assert.Equal("works/a.xml", item.Path);
        Assert.Equal(Sha256([1, 2, 3]), item.Sha256);
        Assert.Equal(commit, snapshot.Corpus.Commit);
        Assert.Matches("^[0-9a-f]{40}$", snapshot.Corpus.Tree);
        Assert.Matches("^[0-9a-f]{40}$", snapshot.Corpus.WorksSubtree);
    }

    private async Task<LuLicenceObservationRunReceipt> ObservationReceipt(
        string name)
    {
        using var http = new HttpClient(new CensusHandler());
        return await LicenceCensus.AcquireLuObservationsAsync(
            EmptyDirectory(name),
            RunIdentity,
            CodeCommit,
            StartedAt,
            http,
            new LicenceCensusAcquisitionOptions(
                PageSize: 2,
                MaximumRows: 8,
                Pause: TimeSpan.Zero),
            CancellationToken.None);
    }

    private static LicenceCensusProbeRun ProbeRun(
        string name,
        string scriptSha256,
        byte[] stdin,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string counter) => new(
        name,
        scriptSha256,
        "python",
        Sha256(Encoding.UTF8.GetBytes("python-executor")),
        "Python 3.13.7",
        "cpython",
        "3.13.7",
        "windows",
        "x64",
        "corpus-root",
        [$"source-artifacts/{name}"],
        stdin,
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["LANG"] = "C.UTF-8",
            ["LC_ALL"] = "C.UTF-8",
            ["PYTHONHASHSEED"] = "0",
            ["PYTHONUTF8"] = "1",
            ["TZ"] = "UTC",
        },
        startedAt,
        endedAt,
        0,
        LicenceCensusJson.SerializeStructuralCounts(
            new SortedDictionary<string, ulong>(StringComparer.Ordinal)
            {
                [counter] = 1,
            }),
        []);

    private static string[] ProbeNames() =>
    [
        "probe_akn_licence.py",
        "probe_archive.py",
        "probe_block_repeats.py",
        "probe_edge_cases.py",
        "probe_manifestation_licence.py",
        "probe_schema_binding.py",
        "probe_scl_names.py",
    ];

    private string EmptyDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string RunGit(string repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start git fixture.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr);
        return stdout;
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(
                     _root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private sealed class CensusHandler(
        bool failSecondFile = false,
        bool redirectEnumeration = false,
        bool countDrift = false,
        bool duplicateData = false,
        bool omitEffectiveUri = false,
        bool repeatDataPage = false,
        bool splitManifestation = false) : HttpMessageHandler
    {
        private const string M1 =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/n1/consolidation/20200101/fra/xml";
        private const string M2 =
            "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/02/n1/consolidation/20200102/fra/xml";
        private const string F1 =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2020/01/01/n1.xml";
        private const string F2 =
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2020/01/02/n1.xml";

        public int PostCount { get; private set; }
        public int GetCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                PostCount++;
                var responseText = repeatDataPage
                    ? PostCount switch
                    {
                        1 or 4 => CountPage(4),
                        2 or 3 => SparqlPage(),
                        _ => throw new InvalidOperationException(
                            "Unexpected repeated-page request in the fixture."),
                    }
                    : splitManifestation
                        ? PostCount switch
                        {
                            1 or 4 => CountPage(2),
                            2 => SingleManifestationPage(
                                "http://creativecommons.org/licenses/by/4.0/"),
                            3 => SingleManifestationPage(
                                "https://creativecommons.org/licenses/by/4.0/"),
                            _ => throw new InvalidOperationException(
                                "Unexpected split-page request in the fixture."),
                        }
                        : PostCount switch
                        {
                            1 => CountPage(2),
                            2 => duplicateData
                                ? SparqlPage().Replace(
                                    "{\"head\":", "{\"head\":{},\"head\":",
                                    StringComparison.Ordinal)
                                : SparqlPage(),
                            3 => CountPage(countDrift ? 3 : 2),
                            _ => throw new InvalidOperationException(
                                "Unexpected SPARQL request in the fixture."),
                        };
                var bytes = Encoding.UTF8.GetBytes(responseText);
                return Task.FromResult(Response(
                    request,
                    HttpStatusCode.OK,
                    bytes,
                    "application/sparql-results+json",
                    redirectEnumeration
                        ? "https://attacker.invalid/sparql"
                        : request.RequestUri!.AbsoluteUri,
                    omitEffectiveUri));
            }

            GetCount++;
            var failed = failSecondFile && GetCount == 2;
            var file = request.RequestUri!.AbsoluteUri.Contains("01/n1.xml",
                StringComparison.Ordinal)
                ? M1
                : M2;
            var body = Encoding.UTF8.GetBytes($$"""
                <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" xmlns:scl="http://www.scl.lu"><act><meta><identification><FRBRManifestation><FRBRthis value="{{file}}" /></FRBRManifestation><scl:JOLUXManifestation><scl:jolux scl:name="uriThis">{{file}}</scl:jolux><scl:jolux scl:name="license">CC-BY-4.0</scl:jolux></scl:JOLUXManifestation></identification></meta></act></akomaNtoso>
                """);
            return Task.FromResult(Response(
                request,
                failed ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                body,
                "application/xml",
                request.RequestUri.AbsoluteUri,
                omitEffectiveUri));
        }

        private static HttpResponseMessage Response(
            HttpRequestMessage request,
            HttpStatusCode status,
            byte[] body,
            string mediaType,
            string effectiveUri,
            bool omitEffectiveUri)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body),
            };
            if (!omitEffectiveUri)
                response.RequestMessage = new HttpRequestMessage(
                    request.Method, effectiveUri);
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType)
                {
                    CharSet = "utf-8",
                };
            response.Headers.ETag =
                new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture\"");
            return response;
        }

        private static string SparqlPage() => $$$"""
            {"head":{"vars":["c","expr","m","fmt","file","license"]},"results":{"bindings":[
              {"c":{"type":"uri","value":"http://data.legilux.public.lu/c1"},"expr":{"type":"uri","value":"http://data.legilux.public.lu/e1"},"m":{"type":"uri","value":"{{{M1}}}"},"fmt":{"type":"uri","value":"http://data.legilux.public.lu/resource/authority/user-format/xml"},"file":{"type":"uri","value":"{{{F1}}}"},"license":{"type":"uri","value":"http://creativecommons.org/licenses/by/4.0/"}},
              {"c":{"type":"uri","value":"http://data.legilux.public.lu/c2"},"expr":{"type":"uri","value":"http://data.legilux.public.lu/e2"},"m":{"type":"uri","value":"{{{M2}}}"},"fmt":{"type":"uri","value":"http://data.legilux.public.lu/resource/authority/user-format/xml"},"file":{"type":"uri","value":"{{{F2}}}"},"license":{"type":"uri","value":"http://creativecommons.org/licenses/by/4.0/"}}
            ]}}
            """;

        private static string SingleManifestationPage(string licence) => $$$"""
            {"head":{"vars":["c","expr","m","fmt","file","license"]},"results":{"bindings":[
              {"c":{"type":"uri","value":"http://data.legilux.public.lu/c1"},"expr":{"type":"uri","value":"http://data.legilux.public.lu/e1"},"m":{"type":"uri","value":"{{{M1}}}"},"fmt":{"type":"uri","value":"http://data.legilux.public.lu/resource/authority/user-format/xml"},"file":{"type":"uri","value":"{{{F1}}}"},"license":{"type":"uri","value":"{{{licence}}}"}}
            ]}}
            """;

        private static string CountPage(int count) => $$$"""
            {"head":{"vars":["count"]},"results":{"bindings":[{"count":{"type":"literal","datatype":"http://www.w3.org/2001/XMLSchema#integer","value":"{{{count}}}"}}]}}
            """;
    }
}

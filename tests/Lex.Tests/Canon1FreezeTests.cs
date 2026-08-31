using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.Derive;

namespace Lex.Tests;

public sealed class Canon1FreezeTests
{
    private const string SdkVersion = "10.0.400";
    private const string RuntimeVersion = "10.0.11";
    private const string SdkImage = "mcr.microsoft.com/dotnet/sdk:10.0.400@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c";
    private const string AspNetImage = "mcr.microsoft.com/dotnet/aspnet:10.0.11@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94";
    private static readonly string CanonDirectory = Path.Combine(
        Golden.RepositoryRoot(), "tests", "Lex.Tests", "canon", "1");
    private static readonly string ReviewedManifest = Path.Combine(CanonDirectory, "manifest.tsv");

    [Fact]
    public void Contract_binds_the_exact_application_runtime_dependencies_and_profile_registry()
    {
        var contractPath = Path.Combine(CanonDirectory, "contract.json");
        var bytes = File.ReadAllBytes(contractPath);
        var contract = JsonNode.Parse(bytes)!.AsObject();

        Assert.Equal([
            "schema", "canon", "application_baseline", "lex_derive_tree",
            "profile_ids", "target_framework", "sdk", "runtime", "dependencies", "invariants"
        ], contract.Select(property => property.Key));
        Assert.Equal("lex-canon-freeze/1", contract["schema"]!.GetValue<string>());
        Assert.Equal("canon/1", contract["canon"]!.GetValue<string>());
        Assert.Equal("20f06c1911834a4528d57a454ea170e35a9b2444",
            contract["application_baseline"]!.GetValue<string>());
        Assert.Equal("69f0bef039a569f897e7ea81cefa6850d65606db",
            contract["lex_derive_tree"]!.GetValue<string>());
        Assert.Equal(Canon1FixtureRunner.ProfileIds,
            contract["profile_ids"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Equal("net10.0", contract["target_framework"]!.GetValue<string>());
        Assert.Equal(SdkVersion, contract["sdk"]!.GetValue<string>());
        Assert.Equal(RuntimeVersion, contract["runtime"]!.GetValue<string>());

        var dependencies = contract["dependencies"]!.AsArray()
            .Select(value => (value!["name"]!.GetValue<string>(), value["version"]!.GetValue<string>()))
            .ToArray();
        Assert.Equal([
            ("HtmlAgilityPack", "1.12.4"),
            ("PdfPig", "0.1.11")
        ], dependencies);
        var deriveProject = System.Xml.Linq.XDocument.Load(Path.Combine(
            Golden.RepositoryRoot(), "src", "Lex.Derive", "Lex.Derive.csproj"));
        Assert.Equal(contract["target_framework"]!.GetValue<string>(),
            Assert.Single(deriveProject.Descendants("TargetFramework")).Value);
        Assert.Equal([
            ("HtmlAgilityPack", "[1.12.4]"),
            ("PdfPig", "[0.1.11]")
        ], deriveProject.Descendants("PackageReference")
            .Select(reference => (
                reference.Attribute("Include")!.Value,
                reference.Attribute("Version")!.Value))
            .ToArray());

        var buildProperties = System.Xml.Linq.XDocument.Load(Path.Combine(
            Golden.RepositoryRoot(), "Directory.Build.props"));
        Assert.Equal(RuntimeVersion,
            Assert.Single(buildProperties.Descendants("RuntimeFrameworkVersion")).Value);
        Assert.Equal("Disable", Assert.Single(buildProperties.Descendants("RollForward")).Value);
        Assert.Equal("true",
            Assert.Single(buildProperties.Descendants("RestorePackagesWithLockFile")).Value);
        Assert.Equal("true", Assert.Single(buildProperties.Descendants("RestoreLockedMode")).Value);

        var projectFiles = TrackedProjectFiles(Golden.RepositoryRoot());
        Assert.All(projectFiles, project =>
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json")),
                $"Missing packages.lock.json for {Path.GetRelativePath(Golden.RepositoryRoot(), project)}"));

        var deriveLock = JsonNode.Parse(File.ReadAllBytes(Path.Combine(
            Golden.RepositoryRoot(), "src", "Lex.Derive", "packages.lock.json")))!.AsObject();
        var lockedDependencies = deriveLock["dependencies"]!["net10.0"]!.AsObject();
        AssertLockedPackage(
            lockedDependencies, "HtmlAgilityPack", "[1.12.4, 1.12.4]", "1.12.4");
        AssertLockedPackage(lockedDependencies, "PdfPig", "[0.1.11, 0.1.11]", "0.1.11");

        var invariants = contract["invariants"]!.AsObject();
        Assert.Equal([
            "culture", "encoding", "line_endings", "path_order", "hash"
        ], invariants.Select(property => property.Key));
        Assert.Equal("InvariantCulture", invariants["culture"]!.GetValue<string>());
        Assert.Equal("UTF-8 without BOM", invariants["encoding"]!.GetValue<string>());
        Assert.Equal("LF", invariants["line_endings"]!.GetValue<string>());
        Assert.Equal("ordinal", invariants["path_order"]!.GetValue<string>());
        Assert.Equal("SHA-256 lowercase hexadecimal", invariants["hash"]!.GetValue<string>());
        AssertUtf8NoBomLf(bytes);
    }

    [Fact]
    public void DirectML_configuration_has_its_own_complete_locked_graph()
    {
        var repository = Golden.RepositoryRoot();
        var buildProperties = System.Xml.Linq.XDocument.Load(Path.Combine(
            repository, "Directory.Build.props"));
        var directMlLockPath = Assert.Single(
            buildProperties.Descendants("NuGetLockFilePath"));
        Assert.Equal("'$(UseDirectML)' == 'true'",
            directMlLockPath.Attribute("Condition")?.Value);
        Assert.Equal("$(MSBuildProjectDirectory)/packages.directml.lock.json",
            directMlLockPath.Value);

        var projectFiles = TrackedProjectFiles(repository);
        Assert.All(projectFiles, project =>
            Assert.True(File.Exists(Path.Combine(
                    Path.GetDirectoryName(project)!, "packages.directml.lock.json")),
                $"Missing packages.directml.lock.json for {Path.GetRelativePath(repository, project)}"));

        var defaultIndexDependencies = LockedDependencies(Path.Combine(
            repository, "src", "Lex.Index", "packages.lock.json"));
        Assert.Contains("Microsoft.ML.OnnxRuntime",
            defaultIndexDependencies.Select(item => item.Key));
        Assert.DoesNotContain("Microsoft.ML.OnnxRuntime.DirectML",
            defaultIndexDependencies.Select(item => item.Key));

        var indexDependencies = LockedDependencies(Path.Combine(
            repository, "src", "Lex.Index", "packages.directml.lock.json"));
        AssertLockedPackage(indexDependencies, "Microsoft.ML.OnnxRuntime.DirectML",
            "[1.24.4, )", "1.24.4");
        Assert.DoesNotContain("Microsoft.ML.OnnxRuntime",
            indexDependencies.Select(item => item.Key));

        var ingestDependencies = LockedDependencies(Path.Combine(
            repository, "src", "Lex.Ingest", "packages.directml.lock.json"));
        Assert.Contains("Microsoft.ML.OnnxRuntime.DirectML",
            ingestDependencies.Select(item => item.Key));
        Assert.DoesNotContain("Microsoft.ML.OnnxRuntime",
            ingestDependencies.Select(item => item.Key));

        var ci = File.ReadAllText(Path.Combine(repository, ".github", "workflows", "ci.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "dotnet restore Lex.slnx --locked-mode --nologo -p:UseDirectML=true", ci,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet build src/Lex.Ingest/Lex.Ingest.csproj -c Release --no-restore --nologo -p:UseDirectML=true",
            ci, StringComparison.Ordinal);

        var attributes = File.ReadAllLines(Path.Combine(repository, ".gitattributes"));
        Assert.Contains("**/packages.directml.lock.json text eol=lf", attributes);
    }

    [Fact]
    public void Canon_dotnet_toolchain_and_cross_os_ci_are_exactly_pinned()
    {
        Assert.Equal(RuntimeVersion, Environment.Version.ToString());
        var repository = Golden.RepositoryRoot();
        var global = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository, "global.json")))!
            .AsObject();
        Assert.Equal(["sdk"], global.Select(property => property.Key));
        var sdk = global["sdk"]!.AsObject();
        Assert.Equal(["version", "rollForward", "allowPrerelease"],
            sdk.Select(property => property.Key));
        Assert.Equal(SdkVersion, sdk["version"]!.GetValue<string>());
        Assert.Equal("disable", sdk["rollForward"]!.GetValue<string>());
        Assert.False(sdk["allowPrerelease"]!.GetValue<bool>());

        var ci = File.ReadAllText(Path.Combine(repository, ".github", "workflows", "ci.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("  dotnet:\n    runs-on: ubuntu-24.04", ci, StringComparison.Ordinal);
        Assert.Contains("  canon-windows:\n    runs-on: windows-2025", ci, StringComparison.Ordinal);
        Assert.Contains($"test \"$(dotnet --version)\" = \"{SdkVersion}\"", ci,
            StringComparison.Ordinal);
        Assert.Contains($"(dotnet --version).Trim() -ne \"{SdkVersion}\"", ci,
            StringComparison.Ordinal);
        Assert.Contains("--filter FullyQualifiedName~Canon1FreezeTests", ci, StringComparison.Ordinal);
        Assert.Contains("dotnet restore Lex.slnx --locked-mode --nologo", ci,
            StringComparison.Ordinal);
        Assert.Contains("dotnet build -c Release --no-restore --nologo", ci,
            StringComparison.Ordinal);
        Assert.Contains("dotnet restore tests/Lex.Tests/Lex.Tests.csproj --locked-mode --nologo", ci,
            StringComparison.Ordinal);
        Assert.Contains("dotnet restore src/Lex.Web/Lex.Web.csproj --locked-mode --nologo", ci,
            StringComparison.Ordinal);

        var dockerfile = File.ReadAllText(Path.Combine(repository, "Dockerfile"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains($"FROM {SdkImage} AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"FROM {AspNetImage}\n", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN dotnet restore Lex.slnx --locked-mode --nologo", dockerfile,
            StringComparison.Ordinal);
        Assert.Contains("dotnet publish src/Lex.Web -c Release -o /app --no-restore --nologo",
            dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project src/Lex.Ingest -c Release --no-restore -- artifact verify",
            dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("mcr.microsoft.com/dotnet/sdk:10.0 AS build", dockerfile,
            StringComparison.Ordinal);
        Assert.DoesNotContain("mcr.microsoft.com/dotnet/aspnet:10.0\n", dockerfile,
            StringComparison.Ordinal);

        foreach (var workflow in Directory.EnumerateFiles(
                     Path.Combine(repository, ".github", "workflows"), "*.yml"))
        {
            var text = File.ReadAllText(workflow);
            var setupCount = text.Split("actions/setup-dotnet@", StringSplitOptions.None).Length - 1;
            if (setupCount == 0) continue;
            Assert.Equal(setupCount,
                text.Split($"dotnet-version: {SdkVersion}", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("dotnet-version: 10.0.x", text, StringComparison.Ordinal);
        }

        var attributes = File.ReadAllLines(Path.Combine(repository, ".gitattributes"));
        Assert.Contains("tests/Lex.Tests/canon/** text eol=lf", attributes);
        Assert.Contains("tests/Lex.Tests/canon/**/*.pdf binary", attributes);
        Assert.Contains("**/packages.lock.json text eol=lf", attributes);
    }

    [Fact]
    public void Invariant_culture_scope_is_enforced_and_restores_the_caller()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var hostile = CultureInfo.GetCultureInfo("tr-TR");
        try
        {
            CultureInfo.CurrentCulture = hostile;
            CultureInfo.CurrentUICulture = hostile;
            string? observedCulture = null;
            string? observedUiCulture = null;

            Canon1FixtureRunner.RunWithInvariantCulture(() =>
            {
                observedCulture = CultureInfo.CurrentCulture.Name;
                observedUiCulture = CultureInfo.CurrentUICulture.Name;
            });

            Assert.Equal(CultureInfo.InvariantCulture.Name, observedCulture);
            Assert.Equal(CultureInfo.InvariantCulture.Name, observedUiCulture);
            Assert.Equal(hostile, CultureInfo.CurrentCulture);
            Assert.Equal(hostile, CultureInfo.CurrentUICulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void Two_independent_empty_roots_match_the_same_reviewed_manifest()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        Canon1FixtureRunner.Generate(first.Path);
        Canon1FixtureRunner.Generate(second.Path);

        var reviewed = File.ReadAllBytes(ReviewedManifest);
        Assert.Equal(reviewed, File.ReadAllBytes(System.IO.Path.Combine(first.Path, "manifest.tsv")));
        Assert.Equal(reviewed, File.ReadAllBytes(System.IO.Path.Combine(second.Path, "manifest.tsv")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(CanonDirectory, "contract.json")),
            File.ReadAllBytes(System.IO.Path.Combine(first.Path, "contract.json")));
        Canon1FixtureRunner.Verify(first.Path, ReviewedManifest);
        Canon1FixtureRunner.Verify(second.Path, ReviewedManifest);

        foreach (var relativePath in ManifestPaths(reviewed))
            Assert.Equal(
                File.ReadAllBytes(System.IO.Path.Combine(first.Path, Native(relativePath))),
                File.ReadAllBytes(System.IO.Path.Combine(second.Path, Native(relativePath))));
    }

    [Fact]
    public void Checked_in_reviewed_tree_matches_its_manifest()
    {
        Canon1FixtureRunner.Verify(CanonDirectory, ReviewedManifest);
    }

    [Fact]
    public void Generate_rejects_a_nonempty_root()
    {
        using var root = new TempDirectory();
        File.WriteAllText(System.IO.Path.Combine(root.Path, "sentinel.txt"), "occupied");

        var error = Assert.Throws<InvalidDataException>(() =>
            Canon1FixtureRunner.Generate(root.Path));

        Assert.Contains("empty", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("occupied", File.ReadAllText(System.IO.Path.Combine(root.Path, "sentinel.txt")));
    }

    [Fact]
    public void Verify_rejects_mutated_missing_and_extra_files()
    {
        using var root = new TempDirectory();
        Canon1FixtureRunner.Generate(root.Path);
        var output = Directory.EnumerateFiles(
                System.IO.Path.Combine(root.Path, "cases"), "output.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).First();
        var original = File.ReadAllBytes(output);

        File.WriteAllBytes(output, [.. original, (byte)' ']);
        Assert.Throws<InvalidDataException>(() => Canon1FixtureRunner.Verify(root.Path, ReviewedManifest));
        File.WriteAllBytes(output, original);

        File.Delete(output);
        Assert.Throws<InvalidDataException>(() => Canon1FixtureRunner.Verify(root.Path, ReviewedManifest));
        File.WriteAllBytes(output, original);

        var extra = System.IO.Path.Combine(root.Path, "cases", "unexpected.txt");
        File.WriteAllText(extra, "extra");
        Assert.Throws<InvalidDataException>(() => Canon1FixtureRunner.Verify(root.Path, ReviewedManifest));
    }

    [Fact]
    public void Registry_rejects_reordered_missing_duplicate_and_empty_profile_cases()
    {
        var exact = Canon1FixtureRunner.ProfileIds.ToArray();
        Canon1FixtureRunner.ValidateProfileIds(exact);

        Assert.Throws<InvalidDataException>(() =>
            Canon1FixtureRunner.ValidateProfileIds(exact.Reverse()));
        Assert.Throws<InvalidDataException>(() =>
            Canon1FixtureRunner.ValidateProfileIds(exact.Skip(1)));
        Assert.Throws<InvalidDataException>(() =>
            Canon1FixtureRunner.ValidateProfileIds([.. exact[..^1], exact[0]]));
        Assert.Throws<InvalidDataException>(() =>
            Canon1FixtureRunner.ValidateProfileIds([.. exact[..^1], ""]));
    }

    [Fact]
    public void Registry_remains_frozen_when_later_canons_add_profiles()
    {
        var production = Canon1FixtureRunner.DiscoverProductionProfileIds();
        Assert.All(Canon1FixtureRunner.ProfileIds,
            profile => Assert.Contains(profile, production));
        Assert.DoesNotContain(AknLuProfileV3.ProfileId, Canon1FixtureRunner.ProfileIds);
        Assert.Contains(AknLuProfileV3.ProfileId, production);
    }

    [Fact]
    public void Canonical_outputs_cover_every_field_citation_hash_codepoint_span_note_and_structural_empty()
    {
        using var root = new TempDirectory();
        Canon1FixtureRunner.Generate(root.Path);
        var outputs = Directory.EnumerateFiles(
                System.IO.Path.Combine(root.Path, "cases"), "output.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(Canon1FixtureRunner.ProfileIds.Count, outputs.Length);
        var profiles = new List<string>();
        var citationCount = 0;
        var noteCount = 0;
        var structuralEmptyCount = 0;
        var sawNonBmp = false;
        foreach (var output in outputs)
        {
            var bytes = File.ReadAllBytes(output);
            AssertUtf8NoBomLf(bytes);
            var rootObject = JsonNode.Parse(bytes)!.AsObject();
            Assert.Equal([
                "profile_id", "provisions", "markdown", "notes",
                "publisher_structural_empty_articles"
            ], rootObject.Select(property => property.Key));
            profiles.Add(rootObject["profile_id"]!.GetValue<string>());
            var markdown = rootObject["markdown"]!.GetValue<string>();
            sawNonBmp |= markdown.Contains("🧭", StringComparison.Ordinal);
            var provisions = rootObject["provisions"]!.AsArray();
            Assert.NotEmpty(provisions);
            Assert.Contains(provisions, node =>
                !string.IsNullOrWhiteSpace(node!["text_md"]!.GetValue<string>()));

            foreach (var node in provisions)
            {
                var provision = node!.AsObject();
                Assert.Equal([
                    "anchor", "eli", "type", "num", "heading", "path",
                    "article_valid_from", "text_md", "text_sha256", "md_span", "citations"
                ], provision.Select(property => property.Key));
                var text = provision["text_md"]!.GetValue<string>();
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                    provision["text_sha256"]!.GetValue<string>());
                var span = provision["md_span"]!.AsObject();
                Assert.Equal(["start", "end"], span.Select(property => property.Key));
                var start = span["start"]!.GetValue<int>();
                var end = span["end"]!.GetValue<int>();
                Assert.Equal(text, string.Concat(markdown.EnumerateRunes()
                    .Skip(start).Take(end - start).Select(rune => rune.ToString())));

                foreach (var citationNode in provision["citations"]!.AsArray())
                {
                    Assert.Equal(["href", "text"],
                        citationNode!.AsObject().Select(property => property.Key));
                    citationCount++;
                }
            }

            noteCount += rootObject["notes"]!.AsArray().Count;
            foreach (var structuralNode in
                     rootObject["publisher_structural_empty_articles"]!.AsArray())
            {
                Assert.Equal(["anchor", "w_id"],
                    structuralNode!.AsObject().Select(property => property.Key));
                structuralEmptyCount++;
            }
        }

        Assert.Equal(Canon1FixtureRunner.ProfileIds.Order(StringComparer.Ordinal), profiles);
        Assert.True(citationCount > 0);
        Assert.True(noteCount > 0);
        Assert.True(structuralEmptyCount > 0);
        Assert.True(sawNonBmp);
    }

    [Fact]
    public void Pdf_cases_are_tiny_deterministic_uncompressed_files()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        Canon1FixtureRunner.Generate(first.Path);
        Canon1FixtureRunner.Generate(second.Path);

        foreach (var profile in new[] { "pdf-lu/1", "pdf-memorial-lu/1", "pdf-memorial-lu/2" })
        {
            var relativePath = $"cases/{Canon1FixtureRunner.ProfilePath(profile)}/input.pdf";
            var a = File.ReadAllBytes(System.IO.Path.Combine(first.Path, Native(relativePath)));
            var b = File.ReadAllBytes(System.IO.Path.Combine(second.Path, Native(relativePath)));
            Assert.Equal(a, b);
            Assert.InRange(a.Length, 1, 4096);
            var ascii = Encoding.ASCII.GetString(a);
            Assert.StartsWith("%PDF-1.4\n", ascii, StringComparison.Ordinal);
            Assert.DoesNotContain("/Filter", ascii, StringComparison.Ordinal);
            Assert.EndsWith("%%EOF\n", ascii, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Manifest_is_ordinal_lowercase_sha256_and_excludes_itself()
    {
        using var root = new TempDirectory();
        Canon1FixtureRunner.Generate(root.Path);
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(root.Path, "manifest.tsv"));
        AssertUtf8NoBomLf(bytes);
        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(lines.Order(StringComparer.Ordinal), lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("manifest.tsv\t", StringComparison.Ordinal));

        foreach (var line in lines)
        {
            var columns = line.Split('\t');
            Assert.Equal(3, columns.Length);
            Assert.DoesNotContain('\\', columns[0]);
            var path = System.IO.Path.Combine(root.Path, Native(columns[0]));
            var file = File.ReadAllBytes(path);
            Assert.Equal(file.LongLength.ToString(System.Globalization.CultureInfo.InvariantCulture), columns[1]);
            Assert.Matches("^[0-9a-f]{64}$", columns[2]);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file)), columns[2]);
        }
    }

    private static IEnumerable<string> ManifestPaths(byte[] manifest) =>
        Encoding.UTF8.GetString(manifest).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t')[0]);

    private static string Native(string relativePath) =>
        relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);

    private static void AssertUtf8NoBomLf(byte[] bytes)
    {
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
        _ = new UTF8Encoding(false, true).GetString(bytes);
    }

    private static JsonObject LockedDependencies(string path) =>
        JsonNode.Parse(File.ReadAllBytes(path))!["dependencies"]!["net10.0"]!.AsObject();

    /// <summary>
    /// The projects the repository contains, asked of Git rather than of the filesystem.
    /// Enumerating every directory swept in nested worktrees under <c>.claude/worktrees</c>, so the
    /// verdict depended on what happened to be on one developer's disk instead of on the frozen
    /// contract. It fails closed: if Git cannot answer, the test fails rather than scanning.
    /// </summary>
    private static string[] TrackedProjectFiles(string repository)
    {
        using var process = Process.Start(new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            ArgumentList = { "ls-files", "-z", "--", "*.csproj" },
        });
        Assert.NotNull(process);
        var listing = process!.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        var projects = listing.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(relative => Path.Combine(
                repository, relative.Replace('/', Path.DirectorySeparatorChar)))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("obj"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(projects);
        return projects;
    }

    private static void AssertLockedPackage(
        JsonObject dependencies, string name, string requested, string resolved)
    {
        var package = dependencies[name]!.AsObject();
        Assert.Equal("Direct", package["type"]!.GetValue<string>());
        Assert.Equal(requested, package["requested"]!.GetValue<string>());
        Assert.Equal(resolved, package["resolved"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(package["contentHash"]!.GetValue<string>()));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"lex-canon1-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}

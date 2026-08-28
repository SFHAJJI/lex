using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Derive;

namespace Lex.Tests;

public sealed class Canon2FreezeTests
{
    private static readonly string Repository = Golden.RepositoryRoot();
    private static readonly string Canon1Directory = Path.Combine(
        Repository, "tests", "Lex.Tests", "canon", "1");
    private static readonly string Canon2Directory = Path.Combine(
        Repository, "tests", "Lex.Tests", "canon", "2");
    private static readonly string ReviewedManifest = Path.Combine(
        Canon2Directory, "manifest.tsv");

    [Fact]
    public void Two_independent_empty_roots_are_byte_identical()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        Canon2FixtureRunner.Generate(first.Path, Canon1Directory);
        Canon2FixtureRunner.Generate(second.Path, Canon1Directory);

        var firstManifest = File.ReadAllBytes(Path.Combine(first.Path, "manifest.tsv"));
        var reviewedManifest = File.ReadAllBytes(ReviewedManifest);
        Assert.Equal(reviewedManifest, firstManifest);
        Assert.Equal(firstManifest, File.ReadAllBytes(Path.Combine(second.Path, "manifest.tsv")));
        foreach (var relativePath in ManifestPaths(firstManifest))
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first.Path, Native(relativePath))),
                File.ReadAllBytes(Path.Combine(second.Path, Native(relativePath))));
        }
    }

    [Fact]
    public void Checked_in_reviewed_tree_matches_its_manifest()
    {
        if (Environment.GetEnvironmentVariable("LEX_CANON2_UPDATE") == "1")
        {
            Assert.False(Directory.Exists(Canon2Directory));
            Canon2FixtureRunner.Generate(Canon2Directory, Canon1Directory);
        }

        Canon2FixtureRunner.Verify(Canon2Directory, ReviewedManifest, Canon1Directory);
    }

    [Fact]
    public void Canon1_reviewed_manifest_and_case_bytes_remain_bound()
    {
        Canon2FixtureRunner.VerifyCanon1Binding(Canon1Directory);
        var canon1Manifest = File.ReadAllBytes(Path.Combine(Canon1Directory, "manifest.tsv"));
        Assert.Equal(
            Canon2FixtureRunner.Canon1ReviewedManifestSha256,
            Convert.ToHexStringLower(SHA256.HashData(canon1Manifest)));

        using var generated = new TempDirectory();
        Canon2FixtureRunner.Generate(generated.Path, Canon1Directory);
        var contract = JsonNode.Parse(File.ReadAllBytes(
            Path.Combine(generated.Path, "contract.json")))!.AsObject();
        Assert.Equal(
            Canon2FixtureRunner.Canon1ReviewedManifestSha256,
            contract["canon1_reviewed_manifest_sha256"]!.GetValue<string>());
        Assert.Equal("10.0.400", contract["sdk"]!.GetValue<string>());
        var invariants = contract["invariants"]!.AsObject();
        Assert.Equal("InvariantCulture", invariants["culture"]!.GetValue<string>());
        Assert.Equal("UTF-8 without BOM", invariants["encoding"]!.GetValue<string>());
        Assert.Equal("LF", invariants["line_endings"]!.GetValue<string>());
        Assert.Equal("ordinal", invariants["path_order"]!.GetValue<string>());

        foreach (var sourcePath in Directory.EnumerateFiles(
                     Path.Combine(Canon1Directory, "cases"), "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(Canon1Directory, sourcePath);
            Assert.Equal(
                File.ReadAllBytes(sourcePath),
                File.ReadAllBytes(Path.Combine(generated.Path, relativePath)));
        }
    }

    [Fact]
    public void Registry_covers_every_canon1_profile_and_adds_only_akn_lu_3()
    {
        Assert.Equal(
            Canon1FixtureRunner.ProfileIds
                .Append(AknLuProfileV3.ProfileId)
                .Order(StringComparer.Ordinal),
            Canon2FixtureRunner.ProfileIds);
    }

    [Fact]
    public void V3_marker_gap_fixture_is_textless_and_has_no_certifying_hash()
    {
        using var generated = new TempDirectory();
        Canon2FixtureRunner.Generate(generated.Path, Canon1Directory);
        var output = JsonNode.Parse(File.ReadAllBytes(Path.Combine(
            generated.Path,
            Native(Canon2FixtureRunner.MarkerGapOutputPath))))!.AsObject();

        Assert.Equal(AknLuProfileV3.ProfileId, output["profile_id"]!.GetValue<string>());
        Assert.Equal("unavailable", output["text_completeness"]!.GetValue<string>());
        Assert.Empty(output["provisions"]!.AsArray());
        var gap = Assert.Single(output["provision_gaps"]!.AsArray())!.AsObject();
        Assert.Equal([
            "document_order", "anchor", "eli", "type", "num", "heading", "path",
            "article_valid_from", "text_unavailable_reason"
        ], gap.Select(property => property.Key));
        Assert.Equal(ProvisionGapReason.MarkerOnly,
            gap["text_unavailable_reason"]!.GetValue<string>());
        Assert.DoesNotContain("text_md", gap.Select(property => property.Key));
        Assert.DoesNotContain("text_sha256", gap.Select(property => property.Key));
        Assert.DoesNotContain("md_span", gap.Select(property => property.Key));
        Assert.DoesNotContain("citations", gap.Select(property => property.Key));
        Assert.DoesNotContain(
            Canon2FixtureRunner.MarkerGapNote,
            output["markdown"]!.GetValue<string>(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_citation_stays_akn_lu_2_byte_identical_with_v3_enabled()
    {
        var frozen = Canon2FixtureRunner.ExtractOrdinaryCitation(enableAknLuV3: false);
        var candidate = Canon2FixtureRunner.ExtractOrdinaryCitation(enableAknLuV3: true);

        Assert.Equal(AknLuProfileV2.ProfileId, frozen.ProfileId);
        Assert.Equal(AknLuProfileV2.ProfileId, candidate.ProfileId);
        Assert.Equal(
            JsonSerializer.SerializeToUtf8Bytes(frozen),
            JsonSerializer.SerializeToUtf8Bytes(candidate));
        Assert.Equal(
            Canon2FixtureRunner.CanonicalResultBytes(frozen),
            Canon2FixtureRunner.CanonicalResultBytes(candidate));
        Assert.Empty(candidate.Extraction.ProvisionGaps ?? []);
    }

    [Fact]
    public void Manifest_is_ordinal_utf8_without_bom_lf_and_self_excluding()
    {
        using var generated = new TempDirectory();
        Canon2FixtureRunner.Generate(generated.Path, Canon1Directory);
        var bytes = File.ReadAllBytes(Path.Combine(generated.Path, "manifest.tsv"));

        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
        _ = new UTF8Encoding(false, true).GetString(bytes);
        var lines = Encoding.UTF8.GetString(bytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(lines.Order(StringComparer.Ordinal), lines);
        Assert.DoesNotContain(lines,
            line => line.StartsWith("manifest.tsv\t", StringComparison.Ordinal));

        foreach (var line in lines)
        {
            var columns = line.Split('\t');
            Assert.Equal(3, columns.Length);
            Assert.DoesNotContain('\\', columns[0]);
            var file = File.ReadAllBytes(Path.Combine(generated.Path, Native(columns[0])));
            Assert.Equal(file.LongLength.ToString(
                System.Globalization.CultureInfo.InvariantCulture), columns[1]);
            Assert.Matches("^[0-9a-f]{64}$", columns[2]);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file)), columns[2]);
        }
    }

    [Fact]
    public void Verify_rejects_a_mutated_canon2_payload()
    {
        using var generated = new TempDirectory();
        Canon2FixtureRunner.Generate(generated.Path, Canon1Directory);
        var outputPath = Path.Combine(
            generated.Path, Native(Canon2FixtureRunner.MarkerGapOutputPath));
        var bytes = File.ReadAllBytes(outputPath);
        bytes[^1] = (byte)' ';
        File.WriteAllBytes(outputPath, bytes);

        Assert.Throws<InvalidDataException>(() => Canon2FixtureRunner.Verify(
            generated.Path,
            Path.Combine(generated.Path, "manifest.tsv"),
            Canon1Directory));
    }

    private static IEnumerable<string> ManifestPaths(byte[] manifest) =>
        Encoding.UTF8.GetString(manifest)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t')[0]);

    private static string Native(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"lex-canon2-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(Path);
            var prefix = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath())
                .TrimEnd(System.IO.Path.DirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal)
                && System.IO.Path.GetFileName(full).StartsWith(
                    "lex-canon2-", StringComparison.Ordinal)
                && Directory.Exists(full))
                Directory.Delete(full, recursive: true);
        }
    }
}

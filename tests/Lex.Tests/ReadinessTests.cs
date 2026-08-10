using System.Security.Cryptography;
using System.Text;
using Lex.Index;
using Lex.Web;

namespace Lex.Tests;

public sealed class ReadinessTests : IDisposable
{
    private readonly string _db = Path.Combine(
        Path.GetTempPath(), $"lex-ready-{Guid.NewGuid():N}.db");
    private readonly LexIndexReader _reader;

    public ReadinessTests()
    {
        const string issues = "[]";
        var document = new DocRow(
            "eu-eurlex:w:2024-01-01", "eu-eurlex", "w", "W", "REG", "en",
            "2024-01-01", null, "official_consolidation_state", "2024-01-01",
            false, false, false, "record", null, "https://example.test/w", "Work", "Work",
            null, "2024-01-01", null);
        IndexBuilder.Build(_db, new Dictionary<string, string>
        {
            ["collection"] = "eu-eurlex",
            ["built_at"] = "2026-08-10T00:00:00Z",
            ["corpus_commit"] = "test",
            ["scope_expected_works"] = "1",
            ["build_issues_json"] = issues,
            ["build_issues_digest"] = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(issues))),
        }, [document], [], [], [], StampSigner.CreateKeyPem());
        _reader = LexIndexReader.Open(_db);
    }

    [Fact]
    public void Exact_signed_complete_publisher_set_is_ready()
    {
        var report = ReadinessPolicy.Evaluate(
            new Dictionary<string, LexIndexReader> { ["eu-eurlex"] = _reader },
            ["eu-eurlex"], false, null, null);

        Assert.True(report.Ready);
        Assert.True(Assert.Single(report.Publishers).Complete);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Missing_or_extra_publishers_fail_the_exact_set()
    {
        var readers = new Dictionary<string, LexIndexReader> { ["eu-eurlex"] = _reader };

        var missing = ReadinessPolicy.Evaluate(
            readers, ["eu-eurlex", "lu-legilux"], false, null, null);
        var unconfigured = ReadinessPolicy.Evaluate(readers, [], false, null, null);

        Assert.False(missing.Ready);
        Assert.Contains("mounted_publisher_set_mismatch", missing.Issues);
        Assert.False(unconfigured.Ready);
        Assert.Contains("required_publishers_not_configured", unconfigured.Issues);
    }

    [Fact]
    public void Required_manifest_set_is_bound_to_the_verified_runtime_set()
    {
        var readers = new Dictionary<string, LexIndexReader> { ["eu-eurlex"] = _reader };

        var mismatch = ReadinessPolicy.Evaluate(
            readers, ["eu-eurlex"], true, new string('a', 64), new string('b', 64));
        var match = ReadinessPolicy.Evaluate(
            readers, ["eu-eurlex"], true, new string('a', 64), new string('a', 64));

        Assert.False(mismatch.Ready);
        Assert.Contains("verified_manifest_set_mismatch", mismatch.Issues);
        Assert.True(match.Ready);
    }

    public void Dispose()
    {
        _reader.Dispose();
        try { File.Delete(_db); } catch { }
    }
}

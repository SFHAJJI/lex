namespace Lex.Tests;

public sealed class TruthContractDocumentationTests
{
    [Fact]
    public void Revalidation_contract_publishes_the_three_cadences_and_fail_closed_rules()
    {
        var text = Read("docs", "corpus-revalidation.md");

        Assert.Contains("Nightly", text, StringComparison.Ordinal);
        Assert.Contains("Weekly", text, StringComparison.Ordinal);
        Assert.Contains("Monthly", text, StringComparison.Ordinal);
        Assert.Contains("every open state and every future-dated state", text,
            StringComparison.Ordinal);
        Assert.Contains("every held manifestation whose official URI still resolves", text,
            StringComparison.Ordinal);
        Assert.Contains("GET only. It never uses HEAD.", text, StringComparison.Ordinal);
        Assert.Contains("A 304 is a completed revalidation", text, StringComparison.Ordinal);
        Assert.Contains("1,500 ms", text, StringComparison.Ordinal);
        Assert.Contains("three distinct completed run identities", text, StringComparison.Ordinal);
        Assert.Contains("one-million-row truncation guard", text, StringComparison.Ordinal);
        Assert.Contains("No absence event is appended", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_contract_publishes_exact_retention_and_keeper_selection()
    {
        var text = Read("docs", "snapshot-retention.md");

        Assert.Contains("referenced by an issued evidence bundle", text, StringComparison.Ordinal);
        Assert.Contains("retained indefinitely", text, StringComparison.Ordinal);
        Assert.Contains("Nightly releases are retained for 90 days", text, StringComparison.Ordinal);
        Assert.Contains("appended latest in that UTC month", text, StringComparison.Ordinal);
        Assert.Contains("lexicographically smallest manifest-set identifier", text,
            StringComparison.Ordinal);
        Assert.Contains("no_eligible_release", text, StringComparison.Ordinal);
        Assert.Contains("cannot be replaced or deleted", text, StringComparison.Ordinal);
        Assert.Contains("canon ID", text, StringComparison.Ordinal);
        Assert.Contains("Unsigned generated prose is not replayable", text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Observation history begins August 2026; replay depth grows from here.", text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Public_entry_points_link_both_truth_contracts()
    {
        var readme = Read("README.md");
        var operations = Read("docs", "operations-retention.md");

        Assert.Contains("[Corpus revalidation](docs/corpus-revalidation.md)", readme,
            StringComparison.Ordinal);
        Assert.Contains("[Snapshot retention](docs/snapshot-retention.md)", readme,
            StringComparison.Ordinal);
        Assert.Contains("[corpus revalidation](corpus-revalidation.md)", operations,
            StringComparison.Ordinal);
        Assert.Contains("[snapshot retention and replay](snapshot-retention.md)", operations,
            StringComparison.Ordinal);
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Golden.RepositoryRoot(), .. path]));
}

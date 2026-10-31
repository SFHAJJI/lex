using System.Globalization;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusIntervalMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What the interval oracle takes from the index under test, held to the catalogue and the corpus file (the review of the R1
/// gates, 2026-10-28): the oracle reads its states, and whether each holds text, from the index, and the builder skips a
/// catalogue state it cannot date, so a builder that dropped a datable state, or held a row whose file the corpus does not
/// admit, would pass the temporal gate unseen. Two checks, restated from the catalogue's facts and the corpus file's members,
/// never from the builder's code: every datable catalogue state of a served work is a row; every held row's corpus members
/// were acquired under a disposition the rights admission policy the corpus names admits.
/// </summary>
public sealed partial class V3MountedGatesTests
{
    private const string FrenchLanguageIri = "http://publications.europa.eu/resource/authority/language/FRA";

    /// <summary>One Luxembourg member of the corpus file, by its object: its outcome and its rights disposition.</summary>
    internal sealed record CorpusMember(string Outcome, string? Disposition);

    /// <summary>
    /// The catalogue states of a served work (one with a held row) that index 9 must hold a row of, missing from its states table,
    /// each as <c>legal resource | expression</c>. A catalogue state is a subject of <c>work_facts</c> whose IRI spells an act,
    /// a consolidation or a code state, datable by the catalogue alone (a consolidation's or code state's one exact
    /// <c>dateApplicability</c>, an act's one exact <c>dateEntryInForce</c>); it is a row per French expression it is realized by,
    /// or one naming no expression when it is realized by none and holds no held row; a state realized only in another language
    /// is no row of the French chain.
    /// </summary>
    internal static IReadOnlyList<string> CatalogueStatesMissing(SqliteConnection index)
    {
        var facts = new Dictionary<(string Subject, string Predicate), SortedSet<string>>();
        foreach (var row in Rows(index, "SELECT subject_iri, predicate, object_value FROM work_facts WHERE predicate IN ('dateApplicability', 'dateEntryInForce', 'isRealizedBy', 'language')"))
        {
            if (!facts.TryGetValue((row[0], row[1]), out var values))
            {
                facts[(row[0], row[1])] = values = new SortedSet<string>(StringComparer.Ordinal);
            }

            values.Add(row[2]);
        }

        IReadOnlyCollection<string> Values(string subject, string predicate) =>
            facts.TryGetValue((subject, predicate), out var values) ? values : [];
        bool OneExactDay(string subject, string predicate) =>
            Values(subject, predicate) is { Count: 1 } values &&
            DateOnly.TryParseExact(values.First(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        var rows = Rows(index, "SELECT publisher_legal_resource_iri, expression_iri, publisher_work_iri, article_identities_json FROM states")
            .Select(static row => (Resource: row[0], Expression: row[1], Work: row[2], Held: row[3] != "[]"))
            .ToArray();
        var present = rows.Select(static row => (row.Resource, row.Expression)).ToHashSet();
        var served = rows.Where(static row => row.Held).Select(static row => row.Work).ToHashSet(StringComparer.Ordinal);
        var heldResources = rows.Where(static row => row.Held).Select(static row => row.Resource).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var subject in facts.Keys.Select(static key => key.Subject).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var kind = LuxembourgEliState.KindOf(subject);
            if (kind == "none" || LuxembourgEliState.WorkOf(subject) is not { } work || !served.Contains(work) ||
                !(kind == "act" ? OneExactDay(subject, "dateEntryInForce") : OneExactDay(subject, "dateApplicability")))
            {
                continue;
            }

            var expressions = Values(subject, "isRealizedBy");
            var french = expressions.Where(expression => Values(expression, "language").Contains(FrenchLanguageIri, StringComparer.Ordinal)).ToArray();
            var expected = expressions.Count == 0
                ? heldResources.Contains(subject) ? [] : new[] { "" }
                : french;
            missing.AddRange(expected.Where(expression => !present.Contains((subject, expression))).Select(expression => $"{subject} | {expression}"));
        }

        return missing;
    }

    /// <summary>
    /// The held rows of index 9 whose text comes from a corpus member the corpus file does not admit, each as
    /// <c>legal resource | expression | object | outcome | disposition</c>: a member the file does not hold, one not acquired, or
    /// one whose rights disposition the admission policy the corpus names does not admit (none named: policy /1).
    /// </summary>
    internal static IReadOnlyList<string> HeldRowsNotAdmitted(SqliteConnection index, IReadOnlyDictionary<string, CorpusMember> members, string? admissionPolicy)
    {
        var admitted = LuxembourgRightsAdmissionPolicies.AdmittingDispositions(admissionPolicy).Select(static disposition => ContractWire.NameOf(disposition)).ToHashSet(StringComparer.Ordinal);
        var acquired = ContractWire.NameOf(LexCorpus6OutcomeKind.Acquired);
        return Rows(index,
                "SELECT DISTINCT s.publisher_legal_resource_iri, s.expression_iri, a.object_ref_sha256 FROM states s, json_each(s.article_identities_json) j " +
                "JOIN articles a ON a.article_identity_sha256 = j.value ORDER BY 1, 2, 3")
            .Select(row => (Row: row, Member: members.GetValueOrDefault(row[2])))
            .Where(value => value.Member is null || value.Member.Outcome != acquired || value.Member.Disposition is null || !admitted.Contains(value.Member.Disposition))
            .Select(static value => $"{value.Row[0]} | {value.Row[1]} | {value.Row[2]} | {value.Member?.Outcome ?? "no member"} | {value.Member?.Disposition ?? "none"}")
            .ToArray();
    }

    /// <summary>The Luxembourg members of a corpus file, by object: each one's outcome and rights disposition.</summary>
    internal static IReadOnlyDictionary<string, CorpusMember> CorpusMembers(string corpusPath)
    {
        using var stream = File.OpenRead(corpusPath);
        using var document = JsonDocument.Parse(stream);
        var members = new Dictionary<string, CorpusMember>(StringComparer.Ordinal);
        foreach (var member in document.RootElement.GetProperty("members").EnumerateArray())
        {
            if (member.TryGetProperty("luxembourg_rights", out var rights) && rights.ValueKind == JsonValueKind.Object)
            {
                members[member.GetProperty("object_ref_sha256").GetString()!] = new CorpusMember(
                    member.GetProperty("outcome").GetString()!,
                    rights.TryGetProperty("disposition", out var disposition) && disposition.ValueKind == JsonValueKind.String ? disposition.GetString() : null);
            }
        }

        return members;
    }

    [TestMethod]
    public async Task ADroppedCatalogueStateAndAHeldRowTheCorpusDoesNotAdmitAreFound()
    {
        var harness = RepairFixture();
        await using var fixture = await IntervalMount.CreateAsync(harness);
        var copy = Path.Combine(Path.GetTempPath(), "lex-gates-catalogue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        try
        {
            var index = Path.Combine(copy, V3CorpusMount.IndexFileName);
            File.Copy(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), index);
            Dictionary<string, CorpusMember> members;
            using (var connection = LuxembourgIndexBuilder.Open(index, SqliteOpenMode.ReadOnly))
            {
                Assert.IsEmpty(CatalogueStatesMissing(connection), "the builder's index holds every datable catalogue state of its served works");
                members = Rows(connection, "SELECT DISTINCT object_ref_sha256 FROM articles")
                    .ToDictionary(static row => row[0], static _ => new CorpusMember("acquired", "agreed_same_run_cc_by"), StringComparer.Ordinal);
                Assert.IsEmpty(HeldRowsNotAdmitted(connection, members, null));
                var one = members.Keys.Order(StringComparer.Ordinal).First();
                members[one] = new CorpusMember("acquired", "agreed_cc_by_rederived");
                Assert.HasCount(1, HeldRowsNotAdmitted(connection, members, null).Select(static line => line.Split(" | ")[2]).Distinct().ToArray(), "policy /1 admits agreed_same_run_cc_by alone");
                Assert.IsEmpty(HeldRowsNotAdmitted(connection, members, LuxembourgRightsAdmissionPolicies.PolicyASha256), "policy A admits a re-derived agreement");
                members[one] = new CorpusMember("rights_withheld", "conflict");
                StringAssert.Contains(string.Join("\n", HeldRowsNotAdmitted(connection, members, LuxembourgRightsAdmissionPolicies.PolicyASha256)), one);
                members.Remove(one);
                StringAssert.Contains(string.Join("\n", HeldRowsNotAdmitted(connection, members, null)), "no member");
            }

            // A builder that drops the withheld consolidation of the 1798 act (a datable textless state) is found.
            using (var connection = LuxembourgIndexBuilder.Open(index, SqliteOpenMode.ReadWrite))
            {
                using var drop = connection.CreateCommand();
                drop.CommandText = "DELETE FROM states WHERE publisher_legal_resource_iri = $resource";
                drop.Parameters.AddWithValue("$resource", LuxembourgIndexCatalogueStatesTests.W("loi/1798/11/03/n1/consolide/20200101"));
                Assert.AreEqual(1, drop.ExecuteNonQuery());
                var missing = CatalogueStatesMissing(connection);
                Assert.HasCount(1, missing, string.Join("\n", missing));
                StringAssert.StartsWith(missing[0], LuxembourgIndexCatalogueStatesTests.W("loi/1798/11/03/n1/consolide/20200101") + " | ");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(copy, recursive: true);
        }
    }

    [TestMethod]
    public void TheMountHoldsEveryDatableCatalogueStateOfAServedWorkAndAdmitsEveryHeldRowsMember()
    {
        var directory = Environment.GetEnvironmentVariable(MountVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            Assert.Inconclusive($"{MountVariable} names no mount.");
        }

        var path = Path.Combine(directory, V3CorpusMount.IndexFileName);
        using var connection = LuxembourgIndexBuilder.Open(path, SqliteOpenMode.ReadOnly);
        if (Rows(connection, "SELECT COUNT(*) FROM pragma_table_info('states') WHERE name = 'state_kind'")[0][0] == "0")
        {
            Assert.Inconclusive($"{path} is not index 9: its states are not the catalogue's.");
        }

        var missing = CatalogueStatesMissing(connection);
        var notAdmitted = HeldRowsNotAdmitted(connection, CorpusMembers(Path.Combine(directory, V3CorpusMount.CorpusFileName)), CorpusAdmissionPolicy(directory));
        Assert.IsEmpty(missing, $"{missing.Count} datable catalogue states of served works are not rows:\n" + string.Join("\n", missing.Take(20)));
        Assert.IsEmpty(notAdmitted, $"{notAdmitted.Count} held rows' members are not admitted:\n" + string.Join("\n", notAdmitted.Take(20)));
    }
}

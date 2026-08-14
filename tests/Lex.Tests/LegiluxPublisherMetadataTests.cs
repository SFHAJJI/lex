using System.Reflection;
using System.Runtime.ExceptionServices;
using Lex.Law;
using Lex.Sources.Legilux;

namespace Lex.Tests;

public sealed class LegiluxPublisherMetadataTests
{
    private const string Base = "http://data.legilux.public.lu/resource/authority/";

    [Fact]
    public void Subject_query_is_bounded_to_held_works_without_a_Virtuoso_offset_window()
    {
        var works = Enumerable.Range(1, 8)
            .Select(index => $"https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/{index:00}/n1")
            .ToArray();
        var query = LegiluxPublisherMetadata.Query(works);

        Assert.Contains("SELECT DISTINCT ?work ?level ?subject ?label ?scheme", query);
        Assert.Contains("jolux:subjectLevel1", query);
        Assert.Contains("jolux:subjectLevel2", query);
        Assert.Contains("?act a jolux:Act", query);
        Assert.Contains("VALUES ?work", query);
        Assert.Contains("OPTIONAL { ?subject skos:prefLabel ?label", query);
        Assert.Contains("OPTIONAL { ?subject skos:inScheme ?scheme", query);
        Assert.Contains("ORDER BY ?work ?level ?subject ?scheme ?label", query);
        Assert.Contains("LIMIT 8193", query);
        Assert.DoesNotContain("OFFSET", query, StringComparison.Ordinal);
        Assert.All(works, work => Assert.Contains($"<{work}>", query, StringComparison.Ordinal));
    }

    [Fact]
    public void Subjects_are_canonical_deduplicated_and_preserve_all_closed_level_scheme_kinds()
    {
        var schemes = new[]
        {
            ("theme", "theme"),
            ("organisation", "organisation"),
            ("place", "place"),
            ("legal-resource", "legal_resource"),
            ("country", "country"),
        };
        var rows = new List<Dictionary<string, string>>();
        foreach (var level in new[] { "1", "2" })
            foreach (var (scheme, _) in schemes)
            {
                var subject = $"https://data.legilux.public.lu/resource/authority/subject/{level}-{scheme}";
                rows.Add(Row("https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/n1",
                    level, subject, $"Label {level} {scheme}", Base + "legal-subject-" + scheme));
                rows.Add(new Dictionary<string, string>(rows[^1], StringComparer.Ordinal));
            }

        var byWork = Parse("ParseSubjects", rows);
        var metadata = Assert.Single(byWork).Value;

        Assert.Equal(10, metadata.Count);
        Assert.Equal(schemes.SelectMany(_ => new[] { 1, 2 }).Count(), metadata.Count);
        foreach (var level in new[] { 1, 2 })
            foreach (var (_, suffix) in schemes)
                Assert.Contains(metadata, item =>
                    item.Kind == $"legilux_subject_level{level}_{suffix}"
                    && item.Language == "fr"
                    && item.Identifier == item.SourceUri);
        Assert.Equal(metadata.OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Identifier, StringComparer.Ordinal), metadata);
    }

    [Fact]
    public void Missing_ambiguous_or_unknown_subject_authority_fails_closed()
    {
        var work = "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/n1";
        var subject = Base + "subject/example";
        Assert.Throws<InvalidDataException>(() => Parse("ParseSubjects",
            [new() { ["work"] = work, ["level"] = "1", ["subject"] = subject,
                ["scheme"] = Base + "legal-subject-theme" }]));
        Assert.Throws<InvalidDataException>(() => Parse("ParseSubjects",
            [new() { ["work"] = work, ["level"] = "1", ["subject"] = subject,
                ["label"] = "Example" }]));
        Assert.Throws<InvalidDataException>(() => Parse("ParseSubjects",
            [Row(work, "1", subject, "Example", Base + "legal-subject-theme"),
             Row(work, "1", subject, "Example", Base + "legal-subject-place")]));
        Assert.Throws<InvalidDataException>(() => Parse("ParseSubjects",
            [Row(work, "1", subject, "Example", Base + "legal-subject-unknown")]));
        Assert.Throws<InvalidDataException>(() => Parse("ParseSubjects",
            [Row(work, "3", subject, "Example", Base + "legal-subject-theme")]));
    }

    [Fact]
    public void A_work_cannot_exceed_the_existing_publisher_metadata_bound()
    {
        var rows = Enumerable.Range(0, 513).Select(index => Row(
            "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/n1", "1",
            $"{Base}subject/{index}", $"Subject {index}",
            Base + "legal-subject-theme")).ToArray();

        Assert.Throws<InvalidDataException>(() => Parse("ParseSubjects", rows));
    }

    [Fact]
    public void Official_same_as_query_and_parser_derive_all_six_code_identities()
    {
        var heldWorks = new[]
        {
            "https://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1",
            "https://data.legilux.public.lu/eli/etat/leg/loi/1879/06/18/n1",
        };
        var query = LegiluxOfficialIdentities.Query(heldWorks);
        Assert.Contains("owl:sameAs", query);
        Assert.Contains("VALUES ?work", query);
        Assert.Contains("ORDER BY ?work ?identifier", query);
        Assert.Contains("LIMIT 1025", query);
        Assert.DoesNotContain("OFFSET", query, StringComparison.Ordinal);

        var pairs = new[]
        {
            ("code/civil", "loi/1804/03/21/n1", "code-civil"),
            ("code/penal", "loi/1879/06/18/n1", "code-penal"),
            ("code/securite_sociale", "loi/1925/12/17/n1", "code-securite_sociale"),
            ("code/procedure_penale", "loi/1808/11/17/n1", "code-procedure_penale"),
            ("code/procedure_civile", "rgd/1998/08/03/n4", "code-procedure_civile"),
            ("code/travail", "loi/2006/07/31/n2", "code-travail"),
        };
        const string Eli = "https://data.legilux.public.lu/eli/etat/leg/";
        var rows = pairs.Select(pair => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["work"] = Eli + pair.Item2,
            ["identifier"] = Eli + pair.Item1,
        }).ToArray();

        var parsed = Parse("Parse", rows);

        Assert.Equal(6, parsed.Count);
        foreach (var (alias, held, slug) in pairs)
        {
            var item = Assert.Single(parsed[Eli + held]);
            Assert.Equal("legilux_same_as", item.Kind);
            Assert.Equal(Eli + alias, item.Identifier);
            Assert.Equal(slug, item.Label);
            Assert.Null(item.Language);
            Assert.Equal(item.Identifier, item.SourceUri);
        }
    }

    [Fact]
    public void Held_work_batches_cover_each_work_once_and_never_cross_Virtuoso_sorted_top_10000()
    {
        var works = Enumerable.Range(1, 19)
            .Select(index => $"https://data.legilux.public.lu/eli/etat/leg/loi/2020/02/{index:00}/n1")
            .Reverse()
            .Append("https://data.legilux.public.lu/eli/etat/leg/loi/2020/02/01/n1")
            .ToArray();

        var batches = LegiluxAdapter.HeldWorkMetadataBatches(works).ToArray();
        var subjects = batches.Select(LegiluxPublisherMetadata.Query).ToArray();
        var identities = batches.Select(LegiluxOfficialIdentities.Query).ToArray();

        Assert.Equal([8, 8, 3], batches.Select(batch => batch.Length));
        Assert.Equal(works.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            batches.SelectMany(batch => batch));
        Assert.All(subjects.Concat(identities), query =>
        {
            var limit = QueryLimit(query);
            Assert.InRange(limit, 1, 10_000);
            Assert.DoesNotContain("OFFSET", query, StringComparison.Ordinal);
        });
        Assert.All(works.Distinct(StringComparer.Ordinal), work =>
            Assert.Equal(1, subjects.Count(query => query.Contains($"<{work}>", StringComparison.Ordinal))));
    }

    private static Dictionary<string, string> Row(
        string work, string level, string subject, string label, string scheme) => new(StringComparer.Ordinal)
    {
        ["work"] = work,
        ["level"] = level,
        ["subject"] = subject,
        ["label"] = label,
        ["scheme"] = scheme,
    };

    private static IReadOnlyDictionary<string, IReadOnlyList<PublisherMetadataRecord>> Parse(
        string method, IReadOnlyList<Dictionary<string, string>> rows) =>
        Invoke<IReadOnlyDictionary<string, IReadOnlyList<PublisherMetadataRecord>>>(
            method == "Parse" ? "LegiluxOfficialIdentities" : "LegiluxPublisherMetadata",
            method, rows);

    private static T Invoke<T>(string typeName, string method, params object[] arguments)
    {
        var type = typeof(LegiluxAdapter).Assembly.GetType(
            $"Lex.Sources.Legilux.{typeName}", throwOnError: true)!;
        var target = type.GetMethod(method,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, method);
        try
        {
            return (T)target.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static int QueryLimit(string query)
    {
        var line = query.Split('\n').Single(value => value.TrimStart().StartsWith("LIMIT ", StringComparison.Ordinal));
        return int.Parse(line.AsSpan(line.IndexOf("LIMIT ", StringComparison.Ordinal) + 6),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}

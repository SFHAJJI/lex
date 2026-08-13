using Lex.Index;

namespace Lex.Tests;

/// <summary>
/// The demotion where it is actually applied: work resolution.
///
/// <para>The predicate having the right answer is not the property that matters. What matters is
/// that a quoted title ending "and amending Regulation (EU) No 648/2012" no longer resolves BOTH
/// works as identity, because that is the state in which nothing downstream looks ambiguous and
/// the wrong instrument can be served silently.</para>
/// </summary>
public sealed class AmendingClauseResolutionTests : IDisposable
{
    private const string CrrTitle =
        "Regulation (EU) No 575/2013 of the European Parliament and of the Council of 26 June "
        + "2013 on prudential requirements for credit institutions and investment firms and "
        + "amending Regulation (EU) No 648/2012";
    private const string EmirTitle = "Regulation (EU) No 648/2012";

    private readonly List<string> _files = [];

    private LexIndexReader Build()
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-{Guid.NewGuid():N}.db");
        _files.Add(db);
        IndexBuilder.Build(db, new Dictionary<string, string>
        {
            ["collection"] = "eu-eurlex",
            ["jurisdiction"] = "EU",
            ["built_at"] = "2026-08-13T00:00:00Z",
            ["corpus_commit"] = "test",
        }, [Doc("eu-eurlex:32013r0575:2021-01-01", "32013r0575", CrrTitle),
            Doc("eu-eurlex:32012r0648:2019-06-17", "32012r0648", EmirTitle)],
            [], [], [], null);
        return LexIndexReader.Open(db);
    }

    // The audited citation. Both works are held, both names are in the sentence, and only the
    // subject of the citation may resolve as identity.
    [Fact]
    public void A_quoted_title_resolves_only_the_work_it_is_about()
    {
        using var reader = Build();

        var plan = reader.SearchKeyword(
            $"Under {CrrTitle}, what does Article 26 require?",
            FilterSet.All, 10, fuzzyAuto: false).QueryPlan!;

        Assert.Equal(["32013r0575"], plan.WorkConstraints);
        Assert.DoesNotContain("32012r0648", plan.WorkConstraints);
    }

    // The half that keeps this a demotion rather than a filter: asked about the amended
    // instrument on its own, it still resolves. A question whose only named work sits in such a
    // clause is asking about that work.
    [Fact]
    public void The_amended_instrument_still_resolves_when_it_is_the_subject()
    {
        using var reader = Build();

        var plan = reader.SearchKeyword(
            $"What does {EmirTitle} require?", FilterSet.All, 10, fuzzyAuto: false).QueryPlan!;

        Assert.Equal(["32012r0648"], plan.WorkConstraints);
    }

    [Fact]
    public void A_lone_work_inside_an_amending_clause_is_not_dropped()
    {
        using var reader = Build();

        var plan = reader.SearchKeyword(
            $"Which act is amending {EmirTitle}?", FilterSet.All, 10, fuzzyAuto: false).QueryPlan!;

        // Nothing else was named, so demoting it would leave the question unanswerable rather
        // than better answered.
        Assert.Equal(["32012r0648"], plan.WorkConstraints);
    }

    private static DocRow Doc(string key, string work, string title) => new(
        key, "eu-eurlex", work, $"urn:celex:{work}", "REG", "en", key[^10..], null,
        "official_consolidation_state", "2026-08-13T00:00:00Z", false, true, true,
        "record-sha", null, "https://example.invalid", title, title, null, key[^10..], null);

    public void Dispose()
    {
        foreach (var file in _files)
            try { File.Delete(file); } catch (IOException) { }
    }
}

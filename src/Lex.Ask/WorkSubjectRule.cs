using Lex.Index;

namespace Lex.Ask;

/// <summary>One stored name the deterministic resolver matched inside the user's own words, and
/// the works that name resolved to. The resolver already computes it and puts it on the wire as
/// <c>mention</c>; nothing in the answer layer read it until this rule.</summary>
/// <param name="Kind">Which stored name form matched: <c>title</c>, <c>identifier</c> or
/// <c>alias</c>, or null when the resolver did not report one.</param>
internal sealed record WorkMention(
    string Mention, IReadOnlyList<string> Works, string? Kind = null);

/// <summary>Why one work was chosen out of several the user's own words authorized. The reason
/// travels with the choice so a caller cannot confuse "decided" with "guessed", and so the reply
/// can disclose that a choice was made at all.</summary>
internal enum WorkSubjectReason
{
    /// <summary>One mention's span strictly contains every other authorized mention's span, so the
    /// others are named INSIDE it. A string fact about what the user wrote, not a heuristic.</summary>
    Contained,

    /// <summary>Every other authorized mention sits immediately after a trailing-clause verb
    /// ("repealing", "amending", "modifiant"), and the survivor is also the leftmost mention.</summary>
    Demoted,

    /// <summary>Exactly one work carries the requested article, AND every candidate work has held
    /// provision text, so the absence of the article in the others is a fact about the law rather
    /// than about what Lex happens to hold.</summary>
    UniqueHeldAnchor,

    /// <summary>Exactly one mention matched a stored official TITLE and every other matched only
    /// a stored identifier. The weakest signal here and the last one tried: quoting a title is a
    /// stronger claim about which instrument you mean than repeating a number that appears inside
    /// it, but it is a claim about citation habits rather than a fact about the string.</summary>
    TitleOverIdentifier,
}

/// <summary>
/// The outcome of choosing a subject among the works one citation named. Two shapes, and
/// "undecided" is one of them: there is deliberately no way to read a work out of an undecided
/// outcome, because the defect this replaces was a <c>string?</c> whose caller could not tell a
/// decision from a fall-through and so served EMIR for a CRR question.
/// </summary>
internal abstract record WorkSubject
{
    private WorkSubject() { }

    /// <summary>A work chosen for a stated reason. <paramref name="RunnerUp"/> is the strongest
    /// work that was NOT chosen, so the reply can name it in one clause and the reader can correct
    /// the choice in one turn instead of never noticing it.</summary>
    internal sealed record Decided(string Work, WorkSubjectReason Reason, string? RunnerUp)
        : WorkSubject;

    /// <summary>No signal was decisive. <paramref name="Ordered"/> is every authorized work,
    /// leftmost mention first, which is the order the clarification menu offers them in. Start
    /// position orders the menu; it never selects.</summary>
    internal sealed record Undecided(IReadOnlyList<string> Ordered) : WorkSubject;
}

/// <summary>
/// Choosing the SUBJECT of a citation among the works that citation named.
///
/// <para>An official title names other instruments inside itself: the CRR's own title ends
/// "...and amending Regulation (EU) No 648/2012", so a lawyer quoting it in full has, by the
/// resolver's correct reckoning, named both the CRR and EMIR. Both are authorized by the user's
/// own words. Exactly one of them is the subject.</para>
///
/// <para>The signal that settles it is span containment, and it is the only one here that is not
/// a heuristic: it needs no keyword list, no language, no publisher and no coverage assumption,
/// because it is a fact about the string the user typed. Everything else in this file is a
/// heuristic and is therefore allowed to DEMOTE or to ORDER, never to pick on its own.</para>
/// </summary>
internal static class WorkSubjectRule
{
    /// <summary>The verbs that introduce the amended, repealed or replaced instrument at the tail
    /// of an official citation. Language- and publisher-specific by construction, which is exactly
    /// why a match here only demotes: if it demotes everything the caller lands in clarification,
    /// whereas promoting on the ABSENCE of a keyword would silently pick.</summary>
    private static readonly HashSet<string> TrailingClauseVerbs = new(StringComparer.Ordinal)
    {
        "repealing", "amending", "replacing", "supplementing",
        "abrogeant", "modifiant", "remplacant", "completant",
    };

    /// <summary>How far back from a mention the trailing-clause verb is looked for. Three tokens
    /// covers "and repealing", "et modifiant le" and "as amended by"; anywhere in the query would
    /// demote a work named at the head of a sentence that happens to contain "amending" later.
    /// </summary>
    private const int TrailingClauseWindow = 3;

    internal static WorkSubject Select(
        string rawUserQuery,
        IReadOnlyList<WorkMention> mentions,
        IReadOnlyCollection<string> authorized,
        bool articleRequested,
        Func<string, bool> carriesRequestedArticle,
        Func<string, bool> holdsProvisionText)
    {
        var works = authorized.Distinct(StringComparer.Ordinal)
            .OrderBy(work => work, StringComparer.Ordinal).ToArray();
        var query = WorkSearch.Normalize(rawUserQuery);
        var located = Locate(query, mentions, authorized);
        var ordered = Order(located, works);

        // 1. Containment. Decisive, and it wins over the anchor always: containment is a fact
        // about the user's words, the anchor is a fact about what Lex holds. When containment
        // selects the CRR and only EMIR has a held art_26, the right behaviour is to serve the CRR
        // and report the provision as not found in it, exactly as the answer prompt already
        // commands for a missing ARTICLE.
        if (located.Count >= 2)
        {
            var containers = located
                .Where(mention => located.All(other => Same(mention, other) || Contains(mention, other)))
                .ToArray();
            if (containers is [var container] && container.Works is [var subject]
                && works.Length > 1)
                return new WorkSubject.Decided(
                    subject, WorkSubjectReason.Contained, RunnerUp(ordered, subject));
        }

        // 2. Trailing-clause demotion, over the already-authorized set only. It must also agree
        // with start position: if the surviving work is not the one named first, the two heuristics
        // disagree ("In the regulation amending EMIR, namely the CRR, ...") and there is no
        // decisive signal left.
        if (located.Count >= 2)
        {
            var demoted = located
                .Where(mention => FollowsTrailingClause(query, mention.Start))
                .SelectMany(mention => mention.Works)
                .ToHashSet(StringComparer.Ordinal);
            var surviving = works.Where(work => !demoted.Contains(work)).ToArray();
            var leftmost = located.MinBy(mention => mention.Start);
            if (surviving is [var kept] && demoted.Count > 0
                && leftmost is not null && leftmost.Works.Contains(kept, StringComparer.Ordinal))
                return new WorkSubject.Decided(
                    kept, WorkSubjectReason.Demoted, RunnerUp(ordered, kept));
        }

        // 3. The article anchor, and only when Lex holds provision text for EVERY candidate. When
        // it does not, "only work W has an art_26 row" is an artefact of coverage rather than of
        // the law, and trusting it selects whichever work Lex happens to have derived articles for.
        if (articleRequested && works.Length > 1 && works.All(holdsProvisionText))
        {
            var anchored = works.Where(carriesRequestedArticle).ToArray();
            if (anchored is [var held])
                return new WorkSubject.Decided(
                    held, WorkSubjectReason.UniqueHeldAnchor, RunnerUp(ordered, held));
        }

        // 4. Match kind, last and weakest. The resolver knows which stored name form each mention
        // matched and used to discard it. Within ONE citation that contains both forms, a quoted
        // official title is a stronger claim about the subject than a bare number, because the
        // number is what an amending tail is made of. Narrow on purpose: exactly one title, and
        // every other mention an identifier. An alias anywhere in the set stands it down, since a
        // reviewed short name is its own strong identity claim and not a weaker title.
        if (located.Count >= 2)
        {
            var titled = located.Where(mention => mention.Kind == "title").ToArray();
            var others = located.Where(mention => mention.Kind != "title").ToArray();
            if (titled is [{ Works: [var named] }] && others.Length > 0
                && others.All(mention => mention.Kind == "identifier")
                && works.Length > 1)
                return new WorkSubject.Decided(
                    named, WorkSubjectReason.TitleOverIdentifier, RunnerUp(ordered, named));
        }

        return new WorkSubject.Undecided(ordered);
    }

    private sealed record Located(
        string Mention, int Start, int End, string[] Works, string? Kind);

    private static List<Located> Locate(
        string query, IReadOnlyList<WorkMention> mentions, IReadOnlyCollection<string> authorized)
    {
        var located = new List<Located>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mention in mentions)
        {
            var works = mention.Works
                .Where(authorized.Contains)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(work => work, StringComparer.Ordinal)
                .ToArray();
            if (works.Length == 0) continue;
            var normalized = WorkSearch.Normalize(mention.Mention);
            if (normalized.Length == 0 || !seen.Add(normalized)) continue;
            var start = IndexOfWord(query, normalized);
            // A mention the user's normalized words do not contain cannot carry a span, so it
            // takes no part in containment, demotion or ordering. It is not evidence of anything
            // about where the subject sits in the sentence.
            if (start < 0) continue;
            located.Add(new Located(
                normalized, start, start + normalized.Length, works, mention.Kind));
        }
        return located;
    }

    private static IReadOnlyList<string> Order(IReadOnlyList<Located> located, string[] works) =>
        located.OrderBy(mention => mention.Start).SelectMany(mention => mention.Works)
            .Concat(works)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string? RunnerUp(IReadOnlyList<string> ordered, string selected) =>
        ordered.FirstOrDefault(work => !string.Equals(work, selected, StringComparison.Ordinal));

    private static bool Same(Located first, Located second) =>
        first.Start == second.Start && first.End == second.End;

    private static bool Contains(Located outer, Located inner) =>
        outer.Start <= inner.Start && inner.End <= outer.End
        && (outer.Start < inner.Start || inner.End < outer.End);

    /// <summary>The mention's own start, in the normalized query, on a word boundary. Substring
    /// position would let "648 2012" match inside "1648 20123"; the padded form cannot.</summary>
    private static int IndexOfWord(string query, string value)
    {
        var index = (" " + query + " ").IndexOf(" " + value + " ", StringComparison.Ordinal);
        return index;
    }

    private static bool FollowsTrailingClause(string query, int start) =>
        query[..Math.Max(start, 0)]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(TrailingClauseWindow)
            .Any(TrailingClauseVerbs.Contains);
}

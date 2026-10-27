using System.Text.Json;
using Lex.V3.Ingest.Names;

namespace Lex.V3.Ingest.Releases;

/// <summary>
/// One change the release-over-release check lists: what changed (<see cref="Change"/>), where (the publisher, the class
/// and the item's key and address), what each release serves there, and the typed reason that explains it, if one does.
/// </summary>
public sealed record V3ReleaseChangeItem(
    string Change,
    string Publisher,
    string Family,
    string Kind,
    string Key,
    string Address,
    IReadOnlyDictionary<string, string?> Serving,
    IReadOnlyDictionary<string, string?> Candidate,
    string? Reason,
    string? ReasonSource,
    int? Entry)
{
    /// <summary>Whether a typed reason explains the change.</summary>
    public bool Explained => Reason is not null;
}

/// <summary>What one population did between two releases: kept, added, changed and lost.</summary>
public sealed record V3ReleaseCounts(long Kept, long Added, long Changed, long Lost);

/// <summary>One class's counts (a Luxembourg family and state kind, or an EU content class), and its served totals in each release.</summary>
public sealed record V3ReleaseClassCounts(
    string Publisher,
    string Family,
    string Kind,
    V3ReleaseCounts Works,
    V3ReleaseCounts Versions,
    V3ReleaseCounts Articles,
    V3ReleaseCounts? Annexes,
    long ServingVersionsWithText,
    long CandidateVersionsWithText,
    long ServingArticles,
    long CandidateArticles);

/// <summary>A mount as the comparison names it: which release's, its corpus, its index schemas and the files verified against its report.</summary>
public sealed record V3ComparedMount(
    string Label,
    string CorpusSha256,
    string? LuxembourgIndexSchema,
    string? EuropeIndexSchema,
    IReadOnlyList<KeyValuePair<string, string>> Files);

/// <summary>
/// The release-over-release check (R106; the audit's gap 6): the candidate release's mount compared with the mount of the
/// release serving now, per publisher and class, for works, versions, articles and annexes kept, added, changed and lost.
/// Served identities and text digests stay stable unless a typed reason explains the change: a version lost, a version whose
/// text is lost, a version whose served identity (its permalink's digest) changed, an article whose text digest changed, an
/// article lost from a version both serve, and an annex lost, are each listed, and each must be explained by the data (the
/// publisher's own file changed: <see cref="PublisherBodyChanged"/>) or by an entry of the release's explanations file
/// (<see cref="V3ReleaseExplanations"/>); any one left unexplained refuses the release. Additions, text a version gains and a
/// textless version's changed reason are counted and never refuse.
/// </summary>
public sealed record V3ReleaseComparison(
    IReadOnlyList<V3ReleaseClassCounts> Classes,
    IReadOnlyList<V3ReleaseChangeItem> Items,
    IReadOnlyList<V3ReleaseChangeExplanation> UnusedExplanations,
    IReadOnlyList<string> Drops)
{
    /// <summary>The report's schema.</summary>
    public const string SchemaId = "lex-v3-release-comparison/1";

    /// <summary>The report's file name, as the release publishes it.</summary>
    public const string FileName = "release-comparison.json";

    /// <summary>The reason the data itself gives: the publisher body the item is read from is not the one the serving release read.</summary>
    public const string PublisherBodyChanged = "publisher_body_changed";

    /// <summary>A version the serving release lists that the candidate does not.</summary>
    public const string VersionLost = "version_lost";

    /// <summary>A version served with text that the candidate lists without text.</summary>
    public const string VersionTextLost = "version_text_lost";

    /// <summary>A version both list whose served identity (its permalink's digest) changed.</summary>
    public const string VersionIdentityChanged = "version_identity_changed";

    /// <summary>An article at a coordinate both serve whose text digest changed.</summary>
    public const string ArticleTextChanged = "article_text_changed";

    /// <summary>An article the serving release serves in a version the candidate serves with text, which the candidate does not serve.</summary>
    public const string ArticleLost = "article_lost";

    /// <summary>An annex outcome of an EU file the serving release held that the candidate does not hold.</summary>
    public const string AnnexLost = "annex_lost";

    /// <summary>What the check holds, as the report states it.</summary>
    public const string Rule =
        "R106: the candidate's mount against the mount of the release serving now, per publisher and class. A served version, " +
        "a version's text, a version's served identity (its permalink's digest), an article's text digest, an article of a version " +
        "both serve with text, and an EU annex outcome stay as the serving release has them, unless a typed reason explains the " +
        "change: the data's own (publisher_body_changed: the publisher body it is read from changed) or an entry of the release's " +
        "explanations, each scope checked against both mounts. Any change left unexplained refuses the release. Additions are " +
        "counted and never refuse.";

    /// <summary>How many listed changes no typed reason explains.</summary>
    public long Unexplained => Items.LongCount(static item => !item.Explained);

    /// <summary>Whether the release is refused: any listed change is unexplained.</summary>
    public bool Refuses => Unexplained > 0;

    private sealed class Tally
    {
        public long[] Versions = new long[4];
        public long[] Articles = new long[4];
        public long[]? Annexes;
        public long ServingWithText;
        public long CandidateWithText;
        public long ServingArticles;
        public long CandidateArticles;
        public HashSet<string> ServingWorks = new(StringComparer.Ordinal);
        public HashSet<string> CandidateWorks = new(StringComparer.Ordinal);
    }

    private const int Kept = 0, Added = 1, Changed = 2, Lost = 3;

    /// <summary>Compares <paramref name="candidate"/> with <paramref name="serving"/> under <paramref name="explanations"/> (none: only the data explains).</summary>
    public static V3ReleaseComparison Compare(V3MountInventory serving, V3MountInventory candidate, V3ReleaseExplanations? explanations)
    {
        ArgumentNullException.ThrowIfNull(serving);
        ArgumentNullException.ThrowIfNull(candidate);
        var entries = explanations?.Changes ?? [];
        var used = new HashSet<int>();
        var tallies = new SortedDictionary<string, (string Publisher, string Family, string Kind, Tally Tally)>(StringComparer.Ordinal);
        Tally TallyOf(V3ServedVersion version)
        {
            var key = version.Publisher + "|" + version.Family + "|" + version.Kind;
            if (!tallies.TryGetValue(key, out var found))
            {
                found = (version.Publisher, version.Family, version.Kind, new Tally());
                tallies.Add(key, found);
            }

            return found.Tally;
        }

        var items = new List<V3ReleaseChangeItem>();
        void Add(string change, V3ServedVersion version, string key, string address, Dictionary<string, string?> before, Dictionary<string, string?> after, (string Reason, string Source, int? Entry)? reason)
        {
            if (reason is { Entry: { } entry })
            {
                used.Add(entry);
            }

            items.Add(new V3ReleaseChangeItem(change, version.Publisher, version.Family, version.Kind, key, address, before, after, reason?.Reason, reason?.Source, reason?.Entry));
        }

        (string, string, int?)? ByEntry(Func<V3ReleaseChangeExplanation, bool> covers)
        {
            foreach (var entry in entries)
            {
                if (covers(entry))
                {
                    return (entry.Reason, "explanations", entry.Index);
                }
            }

            return null;
        }

        static bool ItemListed(V3ReleaseChangeExplanation entry, string publisher, string key) =>
            entry.Publisher == publisher && entry.Items.Contains(key, StringComparer.Ordinal);

        static Dictionary<string, string?> VersionFacts(V3ServedVersion? version) => version is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["address"] = version.Address,
                ["identity_sha256"] = version.IdentitySha256,
                ["text_status"] = version.TextStatus,
                ["text_status_reason"] = version.TextStatusReason,
                ["articles"] = version.Articles.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["rule_profiles"] = string.Join(",", version.RuleProfiles),
                ["bodies"] = string.Join(",", version.BodySha256s),
            };

        // Versions, by served identity.
        foreach (var key in serving.Versions.Keys.Union(candidate.Versions.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var before = serving.Versions.GetValueOrDefault(key);
            var after = candidate.Versions.GetValueOrDefault(key);
            var version = after ?? before!;
            var tally = TallyOf(version);
            if (before is not null)
            {
                tally.ServingWorks.Add(before.Work);
                tally.ServingWithText += before.HasText ? 1 : 0;
            }

            if (after is not null)
            {
                tally.CandidateWorks.Add(after.Work);
                tally.CandidateWithText += after.HasText ? 1 : 0;
            }

            if (before is null)
            {
                tally.Versions[Added]++;
            }
            else if (after is null)
            {
                tally.Versions[Lost]++;
                Add(VersionLost, before, key, before.Address, VersionFacts(before), VersionFacts(null),
                    ByEntry(entry => ItemListed(entry, before.Publisher, key)));
            }
            else if (before.HasText && !after.HasText)
            {
                tally.Versions[Changed]++;
                Add(VersionTextLost, after, key, before.Address, VersionFacts(before), VersionFacts(after),
                    ByEntry(entry => ItemListed(entry, after.Publisher, key) ||
                        (entry.Publisher == after.Publisher && entry.Dispositions.Count > 0 &&
                         after.TextStatus == "withheld_rights" && after.TextStatusReason is { } withheld &&
                         entry.Dispositions.Any(disposition => withheld.StartsWith(disposition, StringComparison.Ordinal)))));
            }
            else if (!before.HasText && after.HasText)
            {
                tally.Versions[Changed]++;
            }
            else if (!string.Equals(before.IdentitySha256, after.IdentitySha256, StringComparison.Ordinal))
            {
                tally.Versions[Changed]++;
                var bodyChanged = !before.BodySha256s.SequenceEqual(after.BodySha256s, StringComparer.Ordinal);
                Add(VersionIdentityChanged, after, key, after.Address, VersionFacts(before), VersionFacts(after),
                    bodyChanged
                        ? (PublisherBodyChanged, "data", null)
                        : ByEntry(entry => ItemListed(entry, after.Publisher, key) ||
                            (entry.Publisher == after.Publisher && entry.FromRuleProfileSha256 is { } from && entry.ToRuleProfileSha256 is { } to &&
                             before.RuleProfiles.Contains(from, StringComparer.Ordinal) && after.RuleProfiles.Contains(to, StringComparer.Ordinal) &&
                             !after.RuleProfiles.Contains(from, StringComparer.Ordinal))));
            }
            else if (!string.Equals(before.TextStatus, after.TextStatus, StringComparison.Ordinal) ||
                     !string.Equals(before.TextStatusReason, after.TextStatusReason, StringComparison.Ordinal))
            {
                tally.Versions[Changed]++;
            }
            else
            {
                tally.Versions[Kept]++;
            }
        }

        // Articles, by coordinate: the served text digests at each, as a multiset.
        foreach (var key in serving.Articles.Keys.Union(candidate.Articles.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var before = serving.Articles.GetValueOrDefault(key);
            var after = candidate.Articles.GetValueOrDefault(key);
            var versionKey = (after ?? before)!.VersionKey;
            var servingVersion = serving.Versions.GetValueOrDefault(versionKey);
            var candidateVersion = candidate.Versions.GetValueOrDefault(versionKey);
            var version = (candidateVersion ?? servingVersion)!;
            var tally = TallyOf(version);
            var remaining = (after?.Articles ?? []).ToList();
            var unmatched = new List<V3ServedArticle>();
            tally.ServingArticles += before?.Articles.Count ?? 0;
            tally.CandidateArticles += remaining.Count;
            foreach (var article in before?.Articles ?? [])
            {
                var at = remaining.FindIndex(other => string.Equals(other.TextSha256, article.TextSha256, StringComparison.Ordinal));
                if (at >= 0)
                {
                    tally.Articles[Kept]++;
                    remaining.RemoveAt(at);
                }
                else
                {
                    unmatched.Add(article);
                }
            }

            var address = version.Address + "#" + (after ?? before)!.Coordinate;
            var pairs = Math.Min(unmatched.Count, remaining.Count);
            for (var index = 0; index < pairs; index++)
            {
                var was = unmatched[index];
                var now = remaining[index];
                tally.Articles[Changed]++;
                Add(ArticleTextChanged, version, key, address, ArticleFacts(was), ArticleFacts(now),
                    !string.Equals(was.BodySha256, now.BodySha256, StringComparison.Ordinal)
                        ? (PublisherBodyChanged, "data", null)
                        : ByEntry(entry => ItemListed(entry, version.Publisher, key) ||
                            (entry.Publisher == version.Publisher && entry.FromRuleProfileSha256 is { } from && entry.ToRuleProfileSha256 is { } to &&
                             was.RuleProfileSha256 == from && now.RuleProfileSha256 == to)));
            }

            for (var index = pairs; index < unmatched.Count; index++)
            {
                tally.Articles[Lost]++;
                // An article of a version lost, or whose text is lost, is that version's item; one lost from a version both serve
                // with text is its own.
                if (servingVersion is not null && candidateVersion is { HasText: true })
                {
                    var bodyChanged = !servingVersion.BodySha256s.SequenceEqual(candidateVersion.BodySha256s, StringComparer.Ordinal);
                    Add(ArticleLost, version, key, address, ArticleFacts(unmatched[index]), VersionFacts(null),
                        bodyChanged ? (PublisherBodyChanged, "data", null) : ByEntry(entry => ItemListed(entry, version.Publisher, key)));
                }
            }

            tally.Articles[Added] += remaining.Count - pairs;
        }

        // EU annexes, by the publisher file they are an outcome of: each file's annex dispositions, as a multiset.
        var servingAnnexes = AnnexesByBody(serving);
        var candidateAnnexes = AnnexesByBody(candidate);
        foreach (var body in servingAnnexes.Keys.Union(candidateAnnexes.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var before = servingAnnexes.GetValueOrDefault(body);
            var after = candidateAnnexes.GetValueOrDefault(body);
            var file = (after ?? before)!.Value.File;
            var key = V3MountInventory.Europe + "|" + file.Family + "|" + file.Kind;
            if (!tallies.TryGetValue(key, out var found))
            {
                found = (V3MountInventory.Europe, file.Family, file.Kind, new Tally());
                tallies.Add(key, found);
            }

            var tally = found.Tally;
            tally.Annexes ??= new long[4];
            var remaining = (after?.Dispositions ?? []).ToList();
            var unmatched = new List<string>();
            foreach (var disposition in before?.Dispositions ?? [])
            {
                if (remaining.Remove(disposition))
                {
                    tally.Annexes[Kept]++;
                }
                else
                {
                    unmatched.Add(disposition);
                }
            }

            var pairs = Math.Min(unmatched.Count, remaining.Count);
            tally.Annexes[Changed] += pairs;
            tally.Annexes[Added] += remaining.Count - pairs;
            var lost = unmatched.Count - pairs;
            if (lost > 0)
            {
                tally.Annexes[Lost] += lost;
                var itemKey = V3MountInventory.Europe + "|annexes|" + body;
                var facts = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["body_sha256"] = body,
                    ["annexes"] = string.Join(",", before!.Value.Dispositions),
                };
                var now = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["body_sha256"] = after is null ? null : body,
                    ["annexes"] = after is null ? null : string.Join(",", after.Value.Dispositions),
                    ["lost"] = lost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
                var reason = ByEntry(entry => entry.Publisher == V3MountInventory.Europe && entry.Items.Contains(itemKey, StringComparer.Ordinal));
                if (reason is { Item3: { } entryIndex })
                {
                    used.Add(entryIndex);
                }

                items.Add(new V3ReleaseChangeItem(AnnexLost, V3MountInventory.Europe, file.Family, file.Kind, itemKey, "body " + body, facts, now,
                    reason?.Item1, reason?.Item2, reason?.Item3));
            }
        }

        var classes = new List<V3ReleaseClassCounts>();
        var drops = new List<string>();
        foreach (var (publisher, family, kind, tally) in tallies.Values)
        {
            var works = new V3ReleaseCounts(
                tally.ServingWorks.Count(tally.CandidateWorks.Contains),
                tally.CandidateWorks.Count(work => !tally.ServingWorks.Contains(work)),
                0,
                tally.ServingWorks.Count(work => !tally.CandidateWorks.Contains(work)));
            classes.Add(new V3ReleaseClassCounts(
                publisher, family, kind, works, Counts(tally.Versions), Counts(tally.Articles), tally.Annexes is null ? null : Counts(tally.Annexes),
                tally.ServingWithText, tally.CandidateWithText, tally.ServingArticles, tally.CandidateArticles));
            Drop(drops, $"{publisher} {family}/{kind}", "versions with text", tally.ServingWithText, tally.CandidateWithText);
            Drop(drops, $"{publisher} {family}/{kind}", "articles served", tally.ServingArticles, tally.CandidateArticles);
        }

        items.Sort(static (left, right) =>
            string.CompareOrdinal(left.Publisher, right.Publisher) is var publisher and not 0 ? publisher
            : string.CompareOrdinal(left.Change, right.Change) is var change and not 0 ? change
            : string.CompareOrdinal(left.Key, right.Key));
        return new V3ReleaseComparison(classes, items, entries.Where(entry => !used.Contains(entry.Index)).ToArray(), drops);
    }

    private static Dictionary<string, string?> ArticleFacts(V3ServedArticle article) => new(StringComparer.Ordinal)
    {
        ["text_sha256"] = article.TextSha256,
        ["body_sha256"] = article.BodySha256,
        ["rule_profile_sha256"] = article.RuleProfileSha256,
    };

    private static SortedDictionary<string, (V3HeldFile File, List<string> Dispositions)?> AnnexesByBody(V3MountInventory inventory)
    {
        var byBody = new SortedDictionary<string, (V3HeldFile File, List<string> Dispositions)?>(StringComparer.Ordinal);
        foreach (var file in inventory.Files.Where(static file => file.Publisher == V3MountInventory.Europe && file.AnnexDispositions.Count > 0))
        {
            if (byBody.TryGetValue(file.BodySha256, out var held) && held is { } existing)
            {
                existing.Dispositions.AddRange(file.AnnexDispositions);
            }
            else
            {
                byBody[file.BodySha256] = (file, file.AnnexDispositions.ToList());
            }
        }

        return byBody;
    }

    private static V3ReleaseCounts Counts(long[] counts) => new(counts[Kept], counts[Added], counts[Changed], counts[Lost]);

    private static void Drop(List<string> drops, string where, string what, long before, long after)
    {
        if (before > 0 && after * 10 < before * 9)
        {
            drops.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{where}: {what} {before} to {after} ({(after - before) * 100.0 / before:0.0} per cent)"));
        }
    }

    /// <summary>The report, one form for one comparison: indented, LF line ends, every list in ordinal order.</summary>
    public byte[] Write(V3ComparedMount serving, V3ComparedMount candidate, V3ReleaseExplanations? explanations)
    {
        ArgumentNullException.ThrowIfNull(serving);
        ArgumentNullException.ThrowIfNull(candidate);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, NamesRecord.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SchemaId);
            writer.WriteString("rule", Rule);
            WriteMount(writer, "serving", serving);
            WriteMount(writer, "candidate", candidate);
            NamesRecord.WriteNullable(writer, "explanations_sha256", explanations?.Sha256);
            NamesRecord.WriteNullable(writer, "explanations_serving_release", explanations?.ServingRelease);
            writer.WriteString("verdict", Refuses ? "refuse" : "pass");
            writer.WriteNumber("items_listed", Items.Count);
            writer.WriteNumber("unexplained", Unexplained);
            writer.WriteStartArray("classes");
            foreach (var row in Classes)
            {
                writer.WriteStartObject();
                writer.WriteString("publisher", row.Publisher);
                writer.WriteString("family", row.Family);
                writer.WriteString("kind", row.Kind);
                WriteCounts(writer, "works", row.Works);
                WriteCounts(writer, "versions", row.Versions);
                WriteCounts(writer, "articles", row.Articles);
                if (row.Annexes is null)
                {
                    writer.WriteNull("annexes");
                }
                else
                {
                    WriteCounts(writer, "annexes", row.Annexes);
                }

                writer.WriteStartObject("served");
                writer.WriteNumber("serving_versions_with_text", row.ServingVersionsWithText);
                writer.WriteNumber("candidate_versions_with_text", row.CandidateVersionsWithText);
                writer.WriteNumber("serving_articles", row.ServingArticles);
                writer.WriteNumber("candidate_articles", row.CandidateArticles);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("drops_beyond_ten_percent");
            foreach (var drop in Drops)
            {
                writer.WriteStringValue(drop);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("items");
            foreach (var item in Items)
            {
                writer.WriteStartObject();
                writer.WriteString("change", item.Change);
                writer.WriteString("publisher", item.Publisher);
                writer.WriteString("family", item.Family);
                writer.WriteString("kind", item.Kind);
                writer.WriteString("key", item.Key);
                writer.WriteString("address", item.Address);
                WriteFacts(writer, "serving", item.Serving);
                WriteFacts(writer, "candidate", item.Candidate);
                if (item.Reason is null)
                {
                    writer.WriteNull("explained_by");
                }
                else
                {
                    writer.WriteStartObject("explained_by");
                    writer.WriteString("reason", item.Reason);
                    writer.WriteString("source", item.ReasonSource);
                    if (item.Entry is { } entry)
                    {
                        writer.WriteNumber("entry", entry);
                    }
                    else
                    {
                        writer.WriteNull("entry");
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("unused_explanations");
            foreach (var entry in UnusedExplanations)
            {
                writer.WriteStartObject();
                writer.WriteNumber("entry", entry.Index);
                writer.WriteString("reason", entry.Reason);
                writer.WriteString("publisher", entry.Publisher);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    internal static void WriteMount(Utf8JsonWriter writer, string name, V3ComparedMount mount)
    {
        writer.WriteStartObject(name);
        writer.WriteString("label", mount.Label);
        writer.WriteString("corpus_sha256", mount.CorpusSha256);
        NamesRecord.WriteNullable(writer, "luxembourg_index_schema", mount.LuxembourgIndexSchema);
        NamesRecord.WriteNullable(writer, "europe_index_schema", mount.EuropeIndexSchema);
        writer.WriteStartArray("files_verified");
        foreach (var (file, sha256) in mount.Files)
        {
            writer.WriteStartObject();
            writer.WriteString("name", file);
            writer.WriteString("sha256", sha256);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCounts(Utf8JsonWriter writer, string name, V3ReleaseCounts counts)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("kept", counts.Kept);
        writer.WriteNumber("added", counts.Added);
        writer.WriteNumber("changed", counts.Changed);
        writer.WriteNumber("lost", counts.Lost);
        writer.WriteEndObject();
    }

    private static void WriteFacts(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, string?> facts)
    {
        if (facts.Count == 0)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        foreach (var (key, value) in facts.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            NamesRecord.WriteNullable(writer, key, value);
        }

        writer.WriteEndObject();
    }
}

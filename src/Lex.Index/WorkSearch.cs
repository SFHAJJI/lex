using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Lex.Index;

public sealed record WorkSearchHit(
    DocRow Doc, string Reason, double Score, string? MatchedValue = null,
    MatchedPublisherMetadata? MatchedPublisherMetadata = null);

internal sealed record WorkMatch(
    long WorkId, string Reason, double Score, string? MatchedValue = null,
    MatchedPublisherMetadata? MatchedPublisherMetadata = null);

/// <summary>
/// Work identity and discovery metadata. This layer never stores or rewrites legal text.
/// </summary>
public static class WorkSearch
{
    private static readonly HashSet<string> AllowedDocumentRoles = new(StringComparer.Ordinal)
    {
        "amending", "consolidated", "corrigendum", "delegated", "implementing",
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "and", "avec", "aux", "dans", "des", "du", "elle", "est", "et", "for", "les",
        "leur", "leurs", "par", "pour", "que", "qui", "sur", "the", "une", "vers",
    };

    // Some publishers title a consolidated manifestation with a banner in front of the work's own
    // name: "Version consolidée applicable au 31/10/2002 : Loi du 5 avril 1993 relative au secteur
    // financier". The name is the part after the colon; no citation a lawyer writes carries the
    // banner, so without the stripped form the statute has no findable name at all.
    private static readonly Regex ConsolidationBanner = new(
        @"^\s*version\s+\p{L}+\s+applicable\s+au\s+\d{2}/\d{2}/\d{4}\s*:\s*(?<name>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] CitationQualifiers =
        ["modifiee", "modifiees", "modifie", "modifies", "coordonnee", "coordonnees"];

    private static readonly Regex CitationQualifier = new(
        @"\b(loi|lois|reglement|reglements|arrete|arretes|code|constitution)\s+(?:"
        + string.Join('|', CitationQualifiers) + @")\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The names a work is catalogued under. Additive: the publisher's own string is always kept,
    /// and a consolidation banner adds the bare title beside it rather than replacing it.
    /// </summary>
    internal static IEnumerable<string> NameForms(string title) =>
        ConsolidationBanner.Match(title) is { Success: true } banner
            ? [title, banner.Groups["name"].Value.Trim()]
            : [title];

    /// <summary>
    /// A French citation names an amended act as "loi modifiée du 12 novembre 2004 ..."; the
    /// official title reads "Loi du 12 novembre 2004 ...". One inserted token defeats a contiguous
    /// contained match, so the standard way of citing a French-language statute never resolved.
    /// </summary>
    internal static string NormalizeCitation(string normalized) =>
        CitationQualifier.Replace(normalized, "$1");

    /// <summary>The participles that turn what follows them into the OBJECT of an amendment
    /// rather than the subject of the question. An official EU title routinely ends by naming
    /// another instrument this way, which is how a quoted CRR title also names EMIR.</summary>
    private static readonly HashSet<string> AmendingClauseVerbs = new(StringComparer.Ordinal)
    {
        "repealing", "amending", "replacing", "supplementing",
        "abrogeant", "modifiant", "remplacant", "completant",
    };

    /// <summary>How far back from a mention the clause verb is looked for. Three tokens covers
    /// "and amending Regulation" and "modifiant le reglement" without reaching into an unrelated
    /// earlier phrase.</summary>
    private const int AmendingClauseWindow = 3;

    /// <summary>
    /// Whether the mention beginning at <paramref name="start"/> in a NORMALIZED query sits
    /// immediately after an amending or repealing participle.
    ///
    /// <para>Defined here, in the layer that produces the conflation, so the search side and
    /// <c>WorkSubjectRule</c> cannot drift apart on what counts as a trailing clause. It is only
    /// ever safe as a demotion over an already-identified set: a question whose ONLY named work
    /// sits in such a clause ("what repealed Directive 95/46/EC?") is asking about that work, so
    /// a caller must not use this to reject a lone match.</para>
    /// </summary>
    public static bool FollowsAmendingClause(string normalizedQuery, int start)
    {
        ArgumentNullException.ThrowIfNull(normalizedQuery);
        if (start <= 0 || start > normalizedQuery.Length) return false;
        return normalizedQuery[..start]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(AmendingClauseWindow)
            .Any(AmendingClauseVerbs.Contains);
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && output.Length > 0) output.Append(' ');
                output.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else pendingSpace = output.Length > 0;
        }
        var normalized = output.ToString();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 1 && tokens.All(token => token.Length == 1 && char.IsLetter(token[0]))
            ? string.Concat(tokens)
            : normalized;
    }

    internal static void Populate(
        SqliteConnection connection,
        IReadOnlyList<DocRow> docs,
        SemanticBuildOptions? semantic,
        SemanticVectorWriter? vectorWriter,
        SemanticEmbeddingCache? embeddingCache)
    {
        var sources = docs.GroupBy(doc => (doc.GroupKey, doc.Language))
            .Select(group => new Source(
                group.Key.GroupKey,
                group.Key.Language,
                group.OrderByDescending(doc => doc.ValidFrom, StringComparer.Ordinal).First(),
                group.OrderBy(doc => doc.ValidFrom, StringComparer.Ordinal).ToArray(),
                group.Select(doc => doc.GroupIdentifier).Where(NotBlank).Distinct(StringComparer.Ordinal).ToArray(),
                group.Select(doc => doc.Title).Where(NotBlank).Distinct(StringComparer.Ordinal).Cast<string>().ToArray(),
                group.Select(doc => doc.TitleShort).Where(NotBlank).Distinct(StringComparer.Ordinal).Cast<string>().ToArray()))
            .OrderBy(source => source.Work, StringComparer.Ordinal)
            .ThenBy(source => source.Language, StringComparer.Ordinal)
            .ToArray();
        if (docs.Any(doc => (doc.DocumentRoles ?? []).Any(role => !AllowedDocumentRoles.Contains(role))))
            throw new InvalidDataException("Document metadata contains an unsupported role.");
        if (docs.Any(doc => (doc.PublisherMetadata?.Count ?? 0) > 512))
            throw new InvalidDataException("A document exceeds the publisher metadata limit.");
        var vectorInputs = new List<(long WorkId, string Kind, string? Value, string Text)>();
        foreach (var source in sources)
        {
            using var record = connection.CreateCommand();
            record.CommandText = """
                INSERT INTO work_records(
                  group_key,language,title,title_short,group_identifier,hierarchy,domains,act_form)
                VALUES ($work,$language,$title,$title_short,$identifier,$hierarchy,$domains,$act_form)
                """;
            Add(record, "$work", source.Work);
            Add(record, "$language", source.Language);
            Add(record, "$title", source.Latest.Title);
            Add(record, "$title_short", source.Latest.TitleShort);
            Add(record, "$identifier", source.Latest.GroupIdentifier);
            Add(record, "$hierarchy", source.Latest.Hierarchy);
            Add(record, "$domains", source.Latest.Domains);
            Add(record, "$act_form", source.Latest.ActForm);
            record.ExecuteNonQuery();
            using var identity = connection.CreateCommand();
            identity.CommandText = "SELECT last_insert_rowid()";
            var workId = Convert.ToInt64(identity.ExecuteScalar(), CultureInfo.InvariantCulture);

            foreach (var identifier in source.Identifiers.Append(source.Work).Distinct(StringComparer.Ordinal))
                InsertName(connection, workId, "official_identifier", identifier);
            foreach (var title in source.Titles.Concat(source.ShortTitles)
                         .SelectMany(NameForms).Where(NotBlank)
                         .Distinct(StringComparer.Ordinal))
                InsertName(connection, workId, "official_title", title);
            foreach (var doc in source.Docs)
                foreach (var metadata in (doc.PublisherMetadata ?? []).Distinct())
                {
                    InsertPublisherMetadata(connection, workId, doc, metadata);
                    if (metadata.Kind == "publisher_short_title")
                        foreach (var segment in PublisherShortTitleSegments(metadata.Value!))
                            InsertName(connection, workId, "publisher_short_title", segment);
                }

            var publisherValues = source.Docs
                .SelectMany(doc => doc.PublisherMetadata ?? [])
                .Where(value => !value.CitationIdentity)
                .Select(value => value.Value)
                .Where(NotBlank).Distinct(StringComparer.Ordinal).Cast<string>().ToArray();
            var roles = source.Latest.DocumentRoles ?? [];

            using var fts = connection.CreateCommand();
            fts.CommandText = """
                INSERT INTO work_fts(
                  rowid,group_key,language,identifiers,aliases,titles,facets,publisher,discovery)
                VALUES ($id,$work,$language,$identifiers,$aliases,$titles,$facets,$publisher,$discovery)
                """;
            Add(fts, "$id", workId);
            Add(fts, "$work", source.Work);
            Add(fts, "$language", source.Language);
            Add(fts, "$identifiers", string.Join(' ', source.Identifiers.Append(source.Work)));
            Add(fts, "$aliases", "");
            Add(fts, "$titles", string.Join(' ', source.Titles.Concat(source.ShortTitles)
                .SelectMany(NameForms).Distinct(StringComparer.Ordinal)));
            Add(fts, "$facets", string.Join(' ', new[]
                { source.Latest.Hierarchy, source.Latest.Domains, source.Latest.ActForm }
                .Where(NotBlank).Concat(roles)));
            Add(fts, "$publisher", string.Join(' ', publisherValues));
            Add(fts, "$discovery", "");
            fts.ExecuteNonQuery();

            vectorInputs.Add((workId, "work", null,
                "subjects: " + string.Join(' ', new[]
                    { source.Latest.Hierarchy, source.Latest.Domains, source.Latest.ActForm }
                    .Where(NotBlank).Concat(roles))
                + "\nnames: " + string.Join(' ', new[] { source.Latest.Title, source.Latest.TitleShort }
                    .Concat(source.Titles).Concat(source.ShortTitles)
                    .Where(NotBlank).SelectMany(name => NameForms(name!))
                    .Distinct(StringComparer.Ordinal))));
        }
        if (semantic is not null && vectorWriter is not null)
            InsertVectors(connection, semantic, vectorWriter, embeddingCache, vectorInputs);
    }

    internal static IReadOnlyList<WorkMatch> Find(
        SqliteConnection connection, string query, string? language, DateOnly? asOf,
        int limit, bool hasPublisherMetadata)
    {
        if (limit <= 0) return [];
        var normalized = Normalize(query);
        if (normalized.Length == 0) return [];
        var hits = new List<WorkMatch>();
        var seen = new HashSet<long>();
        var seenIdentities = new HashSet<(long WorkId, string Value)>();

        Identity(normalized);
        // Second pass, never a destructive strip: "Loi modifiée du 7 juillet 1971 ..." is itself a
        // genuine stored title, so the citation form is offered beside the raw query rather than
        // replacing it. Nothing that matched before can stop matching.
        var citation = NormalizeCitation(normalized);
        if (!string.Equals(citation, normalized, StringComparison.Ordinal)) Identity(citation);

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => (token.Length >= 3 || token.All(char.IsDigit)) && !StopWords.Contains(token))
            .Distinct(StringComparer.Ordinal).Take(12).ToArray();
        if (tokens.Length == 0 || hits.Count >= limit) return hits;
        var tokenQuery = string.Join(" OR ", tokens.Select(token => $"\"{token.Replace("\"", "\"\"")}\""));
        AddFtsMatches("{identifiers aliases titles facets} : (" + tokenQuery + ")", "work_metadata");
        if (hasPublisherMetadata)
            AddPublisherMatches(tokens);
        if (hits.Count >= limit) return hits;
        return hits;

        void Identity(string value)
        {
            var exactCandidates = new List<(long WorkId, string Kind, string MatchedValue)>();
            using (var exact = connection.CreateCommand())
            {
                exact.CommandText = """
                    SELECT n.work_id,n.kind,n.normalized
                    FROM work_names n
                    JOIN work_records r ON r.work_id=n.work_id
                    WHERE n.normalized=$normalized AND ($language IS NULL OR r.language=$language)
                    ORDER BY CASE n.kind
                      WHEN 'official_identifier' THEN 0
                      WHEN 'publisher_short_title' THEN 1
                      ELSE 3 END,n.work_id
                    """;
                Add(exact, "$normalized", value);
                Add(exact, "$language", language);
                using var rows = exact.ExecuteReader();
                while (rows.Read())
                    exactCandidates.Add((rows.GetInt64(0), rows.GetString(1), rows.GetString(2)));
            }
            foreach (var candidate in exactCandidates)
            {
                var metadata = candidate.Kind == "publisher_short_title"
                    ? EffectivePublisherShortTitle(candidate.WorkId, candidate.MatchedValue)
                    : null;
                if (candidate.Kind == "publisher_short_title" && metadata is null) continue;
                AddExact(candidate.WorkId, candidate.Kind, candidate.MatchedValue, "exact", metadata);
                if (hits.Count >= limit) break;
            }

            var containedCandidates = new List<(long WorkId, string Kind, string MatchedValue)>();
            using (var contained = connection.CreateCommand())
            {
                contained.CommandText = """
                    SELECT n.work_id,n.kind,n.normalized,length(n.normalized) AS name_length
                    FROM work_names n
                    JOIN work_records r ON r.work_id=n.work_id
                    WHERE instr(' ' || $normalized || ' ',' ' || n.normalized || ' ') > 0
                      AND ((n.kind='publisher_short_title' AND length(n.normalized) >= 3)
                           OR (n.kind='official_identifier' AND length(n.normalized) >= 4
                               AND n.normalized GLOB '*[0-9]*')
                           OR (n.kind='official_title' AND length(n.normalized) >= 12
                               AND instr(n.normalized,' ') > 0))
                      AND ($language IS NULL OR r.language=$language)
                    ORDER BY CASE n.kind
                      WHEN 'official_identifier' THEN 0
                      WHEN 'publisher_short_title' THEN 1
                      ELSE 3 END,name_length DESC,n.work_id
                    """;
                Add(contained, "$normalized", value);
                Add(contained, "$language", language);
                using var rows = contained.ExecuteReader();
                while (rows.Read())
                    containedCandidates.Add((rows.GetInt64(0), rows.GetString(1), rows.GetString(2)));
            }
            foreach (var candidate in containedCandidates)
            {
                var metadata = candidate.Kind == "publisher_short_title"
                    ? EffectivePublisherShortTitle(candidate.WorkId, candidate.MatchedValue)
                    : null;
                if (candidate.Kind == "publisher_short_title" && metadata is null) continue;
                AddExact(candidate.WorkId, candidate.Kind, candidate.MatchedValue, "contained", metadata);
                if (hits.Count >= limit) break;
            }
        }

        MatchedPublisherMetadata? EffectivePublisherShortTitle(
            long workId, string normalizedSegment)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT identifier,value,language,source_uri
                FROM work_publisher_metadata
                WHERE work_id=$work AND kind='publisher_short_title'
                  AND (($as_of IS NULL AND valid_to IS NULL)
                       OR ($as_of IS NOT NULL AND valid_from <= $as_of
                           AND (valid_to IS NULL OR valid_to >= $as_of)))
                ORDER BY valid_from DESC,identifier,value,language,source_uri
                """;
            Add(command, "$work", workId);
            Add(command, "$as_of", asOf?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                var label = rows.GetString(1);
                var segment = PublisherShortTitleSegments(label)
                    .FirstOrDefault(candidate => Normalize(candidate) == normalizedSegment);
                if (segment is null) continue;
                return new MatchedPublisherMetadata(
                    "publisher_short_title", rows.GetString(0), label,
                    rows.IsDBNull(2) ? null : rows.GetString(2), rows.GetString(3), segment);
            }
            return null;
        }

        void AddFtsMatches(string ftsQuery, string reason)
        {
            if (hits.Count >= limit) return;
            using var search = connection.CreateCommand();
            var rank = hasPublisherMetadata
                ? "bm25(work_fts,0,0,12,10,8,3,5,2)"
                : "bm25(work_fts,0,0,12,10,8,3,2)";
            search.CommandText = $"""
                SELECT rowid,{rank} AS score
                FROM work_fts
                WHERE work_fts MATCH $query AND ($language IS NULL OR language=$language)
                ORDER BY score,rowid
                LIMIT $limit
                """;
            Add(search, "$query", ftsQuery);
            Add(search, "$language", language);
            Add(search, "$limit", Math.Max(20, limit * 2));
            using var matches = search.ExecuteReader();
            while (matches.Read() && hits.Count < limit)
            {
                var workId = matches.GetInt64(0);
                if (!seen.Add(workId)) continue;
                hits.Add(new WorkMatch(workId, reason, matches.GetDouble(1)));
            }
        }

        void AddPublisherMatches(IReadOnlyList<string> searchTokens)
        {
            if (hits.Count >= limit) return;
            var tokenQuery = string.Join(" OR ", searchTokens.Select(token =>
                $"\"{token.Replace("\"", "\"\"")}\""));
            var candidateIds = new List<long>();
            using (var candidates = connection.CreateCommand())
            {
                candidates.CommandText = """
                    SELECT rowid FROM work_fts
                    WHERE work_fts MATCH $query AND ($language IS NULL OR language=$language)
                    ORDER BY bm25(work_fts),rowid
                    LIMIT $candidate_limit
                    """;
                Add(candidates, "$query", "publisher : (" + tokenQuery + ")");
                Add(candidates, "$language", language);
                Add(candidates, "$candidate_limit", (int)Math.Clamp((long)limit * 20, 100, 500));
                using var candidateRows = candidates.ExecuteReader();
                while (candidateRows.Read()) candidateIds.Add(candidateRows.GetInt64(0));
            }
            if (candidateIds.Count == 0) return;
            using var search = connection.CreateCommand();
            var tokenPredicates = searchTokens.Select((_, index) =>
                $"instr(' ' || m.normalized || ' ',' ' || $token{index} || ' ') > 0").ToArray();
            var tokenScore = string.Join(" + ", searchTokens.Select((_, index) =>
                $"CASE WHEN {tokenPredicates[index]} THEN 1 ELSE 0 END"));
            var workParameters = candidateIds.Select((_, index) => $"$publisher_work{index}").ToArray();
            search.CommandText = $"""
                SELECT m.work_id,({tokenScore}) AS matched_tokens,m.normalized,
                       m.kind,m.identifier,m.value,m.language,m.source_uri
                FROM work_publisher_metadata m
                JOIN work_records r ON r.work_id=m.work_id
                WHERE m.work_id IN ({string.Join(',', workParameters)}) AND m.normalized<>''
                  AND m.citation_identity=0
                  AND ($language IS NULL OR r.language=$language)
                  AND (( $as_of IS NULL AND m.valid_to IS NULL)
                       OR ($as_of IS NOT NULL AND m.valid_from <= $as_of
                           AND (m.valid_to IS NULL OR m.valid_to >= $as_of)))
                  AND ({string.Join(" OR ", tokenPredicates)})
                ORDER BY CASE WHEN m.normalized=$normalized THEN 0 ELSE 1 END,
                         matched_tokens DESC,m.work_id,m.kind,m.identifier
                LIMIT $limit
                """;
            Add(search, "$normalized", normalized);
            Add(search, "$language", language);
            Add(search, "$as_of", asOf?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(search, "$limit", Math.Max(20, limit * 2));
            for (var index = 0; index < searchTokens.Count; index++)
                Add(search, $"$token{index}", searchTokens[index]);
            for (var index = 0; index < candidateIds.Count; index++)
                Add(search, workParameters[index], candidateIds[index]);
            using var matches = search.ExecuteReader();
            while (matches.Read() && hits.Count < limit)
            {
                var workId = matches.GetInt64(0);
                if (!seen.Add(workId)) continue;
                hits.Add(new WorkMatch(workId, "work_metadata", -10 - matches.GetInt32(1),
                    matches.GetString(2), new MatchedPublisherMetadata(
                        matches.GetString(3), matches.GetString(4),
                        matches.IsDBNull(5) ? null : matches.GetString(5),
                        matches.IsDBNull(6) ? null : matches.GetString(6),
                        matches.GetString(7))));
            }
        }

        void AddExact(long workId, string kind, string matchedValue, string prefix,
            MatchedPublisherMetadata? matchedPublisherMetadata = null)
        {
            // Resolution is per mention, not per work. "GDPR and RGPD" deliberately yields
            // two identity matches even though both names resolve to the same work. The work is
            // still marked seen so weaker FTS metadata cannot add a third duplicate hit.
            if (!seenIdentities.Add((workId, matchedValue))) return;
            seen.Add(workId);
            var suffix = kind switch
            {
                "official_identifier" => "identifier",
                "publisher_short_title" => "publisher_short_title",
                _ => "title",
            };
            hits.Add(new WorkMatch(
                workId, $"{prefix}_{suffix}", -1000 + hits.Count, matchedValue,
                matchedPublisherMetadata));
        }
    }

    internal static IReadOnlyList<WorkMatch> FindSemantic(
        SqliteConnection connection, SemanticVectorReader vectors, float[] queryVector,
        int limit)
    {
        if (limit <= 0) return [];
        using (var table = connection.CreateCommand())
        {
            table.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='work_vectors'";
            if (table.ExecuteScalar() is null) return [];
        }
        using var range = connection.CreateCommand();
        range.CommandText = "SELECT MIN(vector_ordinal),MAX(vector_ordinal),COUNT(*) FROM work_vectors";
        using var rangeRow = range.ExecuteReader();
        if (!rangeRow.Read() || rangeRow.GetInt64(2) == 0) return [];
        var start = rangeRow.GetInt64(0);
        var end = rangeRow.GetInt64(1);
        var count = rangeRow.GetInt64(2);
        if (end - start + 1 != count)
            throw new InvalidDataException("Work vector ordinals are not contiguous.");
        var queryBits = SemanticVectorReader.Binary(queryVector);
        var queryInt8 = SemanticVectorReader.Int8(queryVector);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.work_id,v.evidence_kind,v.vector_ordinal
            FROM work_vectors v
            WHERE v.evidence_kind='work'
            """;
        using var rows = command.ExecuteReader();
        var candidateLimit = Math.Max(500, limit * 20);
        var candidates = new PriorityQueue<
            (long WorkId, string Kind, long Ordinal, int Distance), long>();
        while (rows.Read())
        {
            var workId = rows.GetInt64(0);
            var ordinal = rows.GetInt64(2);
            var distance = vectors.HammingDistance(ordinal, queryBits);
            var candidate = (workId, rows.GetString(1), ordinal, distance);
            // The smallest priority leaves first. Negating the distance and ordinal removes
            // the worst retained candidate and makes the cutoff deterministic on ties.
            var priority = -(((long)distance << 32) | (uint)ordinal);
            candidates.Enqueue(candidate, priority);
            if (candidates.Count > candidateLimit) candidates.Dequeue();
        }
        return candidates.UnorderedItems.Select(item => item.Element)
            .OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Ordinal)
            .Select(candidate => new WorkMatch(
                candidate.WorkId,
                candidate.Kind == "work" ? "semantic_work" : "semantic_concept",
                vectors.Int8Dot(candidate.Ordinal, queryInt8) / (127d * 127d)))
            .GroupBy(candidate => candidate.WorkId)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.WorkId)
            .Take(limit)
            .ToArray();
    }

    private static void InsertPublisherMetadata(
        SqliteConnection connection, long workId, DocRow doc, PublisherMetadataRow metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Kind) || metadata.Kind.Length > 128
            || string.IsNullOrWhiteSpace(metadata.Identifier)
            || string.IsNullOrWhiteSpace(metadata.SourceUri)
            || metadata.Identifier.Length > 2048 || metadata.SourceUri.Length > 2048
            || metadata.Value?.Length > 4096
            || (metadata.Kind == "publisher_short_title" && string.IsNullOrWhiteSpace(metadata.Value))
            || !Uri.TryCreate(metadata.SourceUri, UriKind.Absolute, out var source)
            || source.Scheme is not ("http" or "https")
            || !Uri.TryCreate(metadata.Identifier, UriKind.Absolute, out var identifier)
            || identifier.Scheme is not ("http" or "https")
            || (!metadata.CitationIdentity
                && (string.IsNullOrWhiteSpace(metadata.Value)
                    || string.IsNullOrWhiteSpace(metadata.Language)
                    || metadata.Language != doc.Language))
            || (metadata.CitationIdentity && metadata.Language is not null))
            throw new InvalidDataException("Publisher metadata is invalid or has the wrong language.");
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO work_publisher_metadata(
              work_id,kind,identifier,value,normalized,language,valid_from,valid_to,source_uri,
              citation_identity)
            VALUES ($work,$kind,$identifier,$value,$normalized,$language,$from,$to,$source,$citation)
            """;
        Add(command, "$work", workId);
        Add(command, "$kind", metadata.Kind);
        Add(command, "$identifier", metadata.Identifier);
        Add(command, "$value", metadata.Value);
        Add(command, "$normalized", Normalize(metadata.Value ?? ""));
        Add(command, "$language", metadata.Language);
        Add(command, "$from", doc.ValidFrom);
        Add(command, "$to", doc.ValidTo);
        Add(command, "$source", metadata.SourceUri);
        Add(command, "$citation", metadata.CitationIdentity ? 1 : 0);
        command.ExecuteNonQuery();
    }

    internal static IReadOnlyList<string> PublisherShortTitleSegments(string value)
    {
        var segments = value.Split(',').Select(segment => segment.Trim()).ToArray();
        if (segments.Length is 0 or > 16 || segments.Any(segment =>
                segment.Length is 0 or > 256 || Normalize(segment).Length == 0))
            throw new InvalidDataException(
                "Publisher short title must contain 1 to 16 non-empty comma segments of at most 256 characters.");
        return segments.Distinct(StringComparer.Ordinal)
            .OrderBy(segment => Normalize(segment), StringComparer.Ordinal)
            .ThenBy(segment => segment, StringComparer.Ordinal).ToArray();
    }

    private static void InsertName(
        SqliteConnection connection, long workId, string kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO work_names(work_id,kind,value,normalized,reviewed_by)
            VALUES ($id,$kind,$value,$normalized,$reviewed_by)
            """;
        Add(command, "$id", workId);
        Add(command, "$kind", kind);
        Add(command, "$value", value.Trim());
        Add(command, "$normalized", Normalize(value));
        Add(command, "$reviewed_by", null);
        command.ExecuteNonQuery();
    }

    private static void InsertVectors(
        SqliteConnection connection,
        SemanticBuildOptions semantic,
        SemanticVectorWriter writer,
        SemanticEmbeddingCache? cache,
        IReadOnlyList<(long WorkId, string Kind, string? Value, string Text)> inputs)
    {
        // Work semantics is a bounded recall aid; the complete names remain in work_names and
        // work_fts. Keep exactly one authoritative base vector per work, prioritizing the subject
        // and current-identity prefix assembled above when historical titles exceed the encoder.
        var chunks = inputs.Select((input, inputOrder) =>
            {
                var chunk = SemanticChunker.Split(input.Text, semantic.Encoder)[0];
                return new
                {
                    Input = (input.WorkId, input.Kind, input.Value, Text: chunk.Text),
                    InputOrder = inputOrder,
                    chunk.Sha256,
                    PaddingTokens = IndexBuilder.EmbeddingTokenBucket(chunk.TokenCount),
                };
            })
            .ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        long completed = 0;
        semantic.Progress?.Invoke(new SemanticBuildProgress(
            0, chunks.Length, watch.Elapsed, null, SemanticBuildStage.WorkEmbeddings));
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO work_vectors(
              work_id,evidence_kind,evidence_value,vector_ordinal)
            VALUES ($work,$kind,$value,$ordinal)
            """;
        command.Parameters.Add(new SqliteParameter("$work", SqliteType.Integer));
        command.Parameters.Add(new SqliteParameter("$kind", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$value", SqliteType.Text));
        command.Parameters.Add(new SqliteParameter("$ordinal", SqliteType.Integer));
        var buckets = chunks.GroupBy(item => item.PaddingTokens).OrderBy(group => group.Key);
        foreach (var bucket in buckets)
        {
            if (bucket.Key > semantic.MaxBatchTokens)
                throw new InvalidDataException("A work-vector input exceeds the embedding token budget.");
            var batchSize = Math.Max(1,
                Math.Min(Math.Min(32, semantic.BatchSize), semantic.MaxBatchTokens / bucket.Key));
            foreach (var items in bucket.OrderBy(item => item.InputOrder).Chunk(batchSize))
            {
                var batch = items.ToArray();
                var records = new byte[batch.Length][];
                var missing = new List<int>();
                for (var index = 0; index < batch.Length; index++)
                    if (cache is null || !cache.TryRead(batch[index].Sha256, out records[index]!))
                        missing.Add(index);
                if (missing.Count > 0)
                {
                    var vectors = semantic.Encoder.EncodeBatch(
                        missing.Select(index => batch[index].Input.Text).ToArray(),
                        EmbeddingInputKind.Passage, bucket.Key);
                    if (vectors.Count != missing.Count)
                        throw new InvalidDataException("Work embedding batch returned the wrong vector count.");
                    var additions = new List<(string ChunkSha, byte[] Record)>(missing.Count);
                    for (var index = 0; index < missing.Count; index++)
                    {
                        var batchIndex = missing[index];
                        records[batchIndex] = SemanticVectorWriter.Quantize(vectors[index]);
                        additions.Add((batch[batchIndex].Sha256, records[batchIndex]));
                    }
                    cache?.Store(additions);
                }
                for (var index = 0; index < batch.Length; index++)
                {
                    var input = batch[index].Input;
                    command.Parameters["$work"].Value = input.WorkId;
                    command.Parameters["$kind"].Value = input.Kind;
                    command.Parameters["$value"].Value = (object?)input.Value ?? DBNull.Value;
                    command.Parameters["$ordinal"].Value = writer.WriteRecord(records[index]);
                    command.ExecuteNonQuery();
                }
                completed += batch.Length;
                var remaining = TimeSpan.FromTicks(
                    (long)(watch.Elapsed.Ticks * (chunks.Length - completed) / (double)completed));
                semantic.Progress?.Invoke(new SemanticBuildProgress(
                    completed, chunks.Length, watch.Elapsed, remaining,
                    SemanticBuildStage.WorkEmbeddings));
            }
        }
    }

    private static bool IsSha(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private sealed record Source(
        string Work,
        string Language,
        DocRow Latest,
        IReadOnlyList<DocRow> Docs,
        IReadOnlyList<string> Identifiers,
        IReadOnlyList<string> Titles,
        IReadOnlyList<string> ShortTitles);
}

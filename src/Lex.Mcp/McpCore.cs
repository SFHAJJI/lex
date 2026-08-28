using System.Text.Json.Nodes;
using Lex.Index;

namespace Lex.Mcp;

/// <summary>
/// The MCP tools (spec §9) as a transport-agnostic core: the stdio server and the
/// public HTTP endpoint both dispatch through this class. Retrieves, filters, diffs,
/// reports — never summarises or advises (F10).
/// </summary>
public sealed class McpCore
{
    public const string ActivitySourceName = McpTelemetry.ActivitySourceName;
    private readonly IReadOnlyDictionary<string, LexIndexReader> readers;

    /// <summary>
    /// Additive unknown_work candidates (Decision 41): the nearest held records, attached only
    /// when at least one exists so an empty search changes no envelope byte. Discovery obeys the
    /// operation's own selection contract via SelectReaders: the same ordinal reader order and
    /// the same MaximumPublisherRows execution bound, so a publisher-scoped call never receives
    /// candidates from an unselected publisher and an unknown identifier never fans out past the
    /// bounded reader set. Absence of a record is never presented as absence of law; the copy
    /// obligations live on the rendering surfaces, this field carries only coordinates.
    /// </summary>
    /// <summary>
    /// The one publisher-scope rule (B1 round-two O5): an explicit publisher argument wins;
    /// otherwise a publisher-qualified work or lex_id contributes its prefix; otherwise null,
    /// which the input contract permits only where an operation is legitimately unscoped.
    /// ReadersFor and candidate discovery both use this, so an unmounted prefix selects zero
    /// readers in both places and a typo in a publisher prefix never becomes cross-publisher
    /// discovery without a recorded product ruling.
    /// </summary>
    private static string? PublisherScopeOf(JsonObject arguments)
    {
        static string? Text(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<string>(out var text) ? text : null;
        var publisher = Text(arguments["publisher"]);
        if (publisher is not null) return publisher;
        var work = Text(arguments["work"]) ?? Text(arguments["lex_id"]);
        return work is not null && work.Contains(':') && !work.Contains("://")
            ? work.Split(':', 2)[0]
            : null;
    }

    private JsonObject WithWorkCandidates(JsonObject payload, string requested, string? publisher)
    {
        var list = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reader in SelectReaders(publisher).Readers)
        {
            foreach (var candidate in WorkCandidateFinder.Nearest(reader, requested))
            {
                if (list.Count >= WorkCandidateFinder.Limit) break;
                if (!seen.Add($"{candidate.Publisher}:{candidate.Work}")) continue;
                list.Add(new JsonObject
                {
                    ["work"] = candidate.Work,
                    ["title"] = candidate.Title,
                    ["publisher"] = candidate.Publisher,
                    ["permalink"] = $"/{candidate.Publisher}/{candidate.Work}",
                });
            }
            if (list.Count >= WorkCandidateFinder.Limit) break;
        }
        if (list.Count > 0) payload["work_candidates"] = list;
        return payload;
    }
    private readonly string? artifactManifestIdentity;
    private readonly bool _corpusMounted;
    private readonly IReadOnlyDictionary<string, SemaphoreSlim> _readerExecutions;
    private readonly Action<string>? _sessionOpened;
    private readonly int? _publisherSelectionTotal;
    private readonly string? _publicBase;
    private readonly IReadOnlyDictionary<string, string> _hybridStatuses;
    private const int MaximumProvisionRows = 2000;
    private const int MaximumHistoryRows = 500;
    private const int MaximumProvenanceRows = 1000;
    private const int MaximumLegalTextChars = 250_000;
    private const int MaximumCitationRows = 100;
    private const int MaximumPublisherRows = 8;
    private const int MaximumCoverageFacetRows = 100;
    private const int MaximumBuildIssueRows = 100;
    private const int GlobalChangeMergePageSize = 128;
    private static readonly System.Text.RegularExpressions.Regex CrossCollectionEli = new(
        @"/eli/(?<type>reg_ue|reg|reg_impl|reg_del|dir_ue|dir|dir_impl|dir_del|dec|dec_impl|dec_del|dec_framw|dec_entscheid)/(?<year>[0-9]{4})/(?<number>[0-9]{1,4})(?:/|$)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
        | System.Text.RegularExpressions.RegexOptions.ExplicitCapture
        | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex CrossCollectionWorkKey = new(
        @"^3(?<year>[0-9]{4})(?<type>[rld])(?<number>[0-9]{4})$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
        | System.Text.RegularExpressions.RegexOptions.ExplicitCapture
        | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly HashSet<string> KnownIncorrectEliTokens = new(StringComparer.Ordinal)
    {
        "/eli/reg_ue/2014/65", "/eli/dir_ue/2004/648",
        "/eli/dir_ue/2012/259", "/eli/dir_ue/2008/765",
    };
    private static readonly HashSet<string> PublicPublisherMetadataKinds = new(StringComparer.Ordinal)
    {
        "publisher_short_title", "eurovoc", "directory", "eurovoc_alt_label",
        "eurovoc_broader", "eurovoc_subdomain", "eurovoc_domain",
        "legilux_subject_level1_theme", "legilux_subject_level1_organisation",
        "legilux_subject_level1_place", "legilux_subject_level1_legal_resource",
        "legilux_subject_level1_country", "legilux_subject_level2_theme",
        "legilux_subject_level2_organisation", "legilux_subject_level2_place",
        "legilux_subject_level2_legal_resource", "legilux_subject_level2_country",
    };

    internal sealed record GlobalChangeItem(LexIndexReader Reader, ChangeRow Row, int Rank);
    internal sealed record GlobalChangePage(IReadOnlyList<GlobalChangeItem> Items, int ReaderRowsLoaded);

    private static JsonObject PublisherMetadataJson(MatchedPublisherMetadata metadata)
    {
        if (!PublicPublisherMetadataKinds.Contains(metadata.Kind))
            throw new InvalidDataException(
                $"Publisher metadata kind '{metadata.Kind}' is not part of the public MCP contract.");
        var result = new JsonObject
        {
            ["kind"] = metadata.Kind,
            ["identifier"] = metadata.Identifier,
            ["label"] = metadata.Label,
            ["language"] = metadata.Language,
            ["source_uri"] = metadata.SourceUri,
        };
        if (metadata.MatchedSegment is not null)
            result["matched_segment"] = metadata.MatchedSegment;
        return result;
    }

    private static JsonObject WorkResolutionJson(
        string mention, string status, string? kind, IEnumerable<string> candidates,
        IReadOnlyList<PublisherShortTitleMatch>? shortTitles)
    {
        var result = new JsonObject
        {
            ["mention"] = mention,
            ["status"] = status,
            ["kind"] = kind,
            ["candidates"] = new JsonArray(candidates.Select(candidate =>
                (JsonNode)candidate).ToArray()),
        };
        if (shortTitles is { Count: > 0 })
            result["publisher_short_title_matches"] = new JsonArray(shortTitles.Select(match =>
            (JsonNode)new JsonObject
            {
                ["work"] = match.Work,
                ["segment"] = match.Segment,
                ["identifier"] = match.Identifier,
                ["label"] = match.Label,
                ["language"] = match.Language,
                ["source_uri"] = match.SourceUri,
            }).ToArray());
        return result;
    }

    private sealed class ChangeCursor(
        LexIndexReader reader,
        string from,
        string to,
        IReadOnlyList<string>? kinds,
        bool byChurn,
        FilterSet filter)
    {
        private IReadOnlyList<ChangeRow> _page = [];
        private int _pageIndex = -1;
        private int _nextOffset;
        public LexIndexReader Reader { get; } = reader;
        public ChangeRow? Current { get; private set; }
        public int RowsLoaded { get; private set; }

        public bool MoveNext()
        {
            _pageIndex++;
            if (_pageIndex >= _page.Count)
            {
                _page = Reader.ChangesInPeriod(
                    from, to, kinds, byChurn, GlobalChangeMergePageSize, _nextOffset, filter);
                RowsLoaded += _page.Count;
                _nextOffset += _page.Count;
                _pageIndex = 0;
            }
            Current = _pageIndex < _page.Count ? _page[_pageIndex] : null;
            return Current is not null;
        }
    }

    internal static GlobalChangePage MergeGlobalChanges(
        IReadOnlyList<LexIndexReader> readers,
        string from,
        string to,
        IReadOnlyList<string>? kinds,
        bool byChurn,
        int limit,
        int offset,
        FilterSet filter)
    {
        var cursors = readers.Select(reader =>
            new ChangeCursor(reader, from, to, kinds, byChurn, filter)).ToArray();
        foreach (var cursor in cursors) cursor.MoveNext();
        var result = new List<GlobalChangeItem>(limit);
        for (var index = 0; index < offset + limit; index++)
        {
            var next = cursors.Where(cursor => cursor.Current is not null)
                .OrderBy(cursor => byChurn ? -cursor.Current!.VersionsInPeriod : 0)
                .ThenByDescending(cursor => cursor.Current!.LastChange, StringComparer.Ordinal)
                .ThenByDescending(cursor => cursor.Current!.VersionsInPeriod)
                .ThenBy(cursor => cursor.Reader.Collection, StringComparer.Ordinal)
                .ThenBy(cursor => cursor.Current!.GroupKey, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null) break;
            if (index >= offset)
                result.Add(new GlobalChangeItem(next.Reader, next.Current!, index + 1));
            next.MoveNext();
        }
        return new GlobalChangePage(result, cursors.Sum(cursor => cursor.RowsLoaded));
    }

    public McpCore(
        IReadOnlyDictionary<string, LexIndexReader> readers,
        string? artifactManifestIdentity = null,
        string? publicBase = null,
        IReadOnlyDictionary<string, string>? hybridStatuses = null)
        : this(readers, artifactManifestIdentity, readers.Count > 0, null, null,
            (publicBase ?? Environment.GetEnvironmentVariable("LEX_PUBLIC_BASE_URL"))?.TrimEnd('/'),
            hybridStatuses)
    {
    }

    internal McpCore(
        IReadOnlyDictionary<string, LexIndexReader> readers,
        Action<string> sessionOpened)
        : this(readers, null, readers.Count > 0, sessionOpened, null,
            Environment.GetEnvironmentVariable("LEX_PUBLIC_BASE_URL")?.TrimEnd('/'), null)
    {
    }

    private McpCore(
        IReadOnlyDictionary<string, LexIndexReader> readers,
        string? artifactManifestIdentity,
        bool corpusMounted,
        Action<string>? sessionOpened,
        int? publisherSelectionTotal,
        string? publicBase,
        IReadOnlyDictionary<string, string>? hybridStatuses)
    {
        this.readers = readers;
        this.artifactManifestIdentity = artifactManifestIdentity;
        _corpusMounted = corpusMounted;
        _sessionOpened = sessionOpened;
        _publisherSelectionTotal = publisherSelectionTotal;
        _publicBase = publicBase;
        _hybridStatuses = readers.ToDictionary(
            item => item.Key,
            item => item.Value.HybridReady
                ? "activated"
                : hybridStatuses?.GetValueOrDefault(item.Key) ?? "not_activated",
            StringComparer.Ordinal);
        _readerExecutions = readers.ToDictionary(
            item => item.Key, _ => new SemaphoreSlim(1, 1), StringComparer.Ordinal);
        MountedPublishers = readers.Keys.Order(StringComparer.Ordinal).ToArray();
        MountedJurisdictions = readers.Values
            .Select(reader => reader.Stamp.GetValueOrDefault("jurisdiction"))
            .OfType<string>().Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>The publisher ids this server actually mounts, ordinal-sorted so a schema
    /// generated from them is byte-identical between processes.</summary>
    public IReadOnlyList<string> MountedPublishers { get; }

    /// <summary>The jurisdiction codes this server actually mounts, ordinal-sorted.</summary>
    public IReadOnlyList<string> MountedJurisdictions { get; }

    public JsonArray ToolDefs() => LegalOperationCatalog.McpToolDefinitions();
    private JsonObject Envelope(LexIndexReader r, string status, bool provisional = false)
    {
        var envelope = new JsonObject
        {
            ["publisher"] = r.Collection,
            ["tier"] = r.Stamp.GetValueOrDefault("tier"),
            ["history_begins"] = r.Stamp.GetValueOrDefault("history_begins"),
            ["status"] = status,
            ["provisional"] = provisional,
            ["freshness"] = new JsonObject
            {
                ["corpus_commit"] = r.Stamp.GetValueOrDefault("corpus_commit"),
                ["built_at"] = r.Stamp.GetValueOrDefault("built_at"),
                ["last_confirmed_at"] = r.Stamp.GetValueOrDefault("built_at"),
                ["last_confirmed_source"] = "index-build",
                ["stamp_signature_valid"] = r.SignatureValid,
            },
        };
        var jurisdiction = r.Stamp.GetValueOrDefault("jurisdiction");
        if (!string.IsNullOrWhiteSpace(jurisdiction)) envelope["jurisdiction"] = jurisdiction;
        // Version coordinates do not all make the same legal claim. In particular, EUR-Lex
        // consolidated-expression dates are official wording-state dates, not entry-into-force
        // dates. New signed artifacts carry the field; the collection fallback keeps older
        // verified EU releases honest during rolling deployment.
        envelope["timeline_semantics"] = r.Stamp.GetValueOrDefault("timeline_semantics",
            r.Collection == "eu-eurlex" ? "official_consolidation_state" : "publisher_applicability");
        envelope["artifact"] = new JsonObject
        {
            ["manifest_set_id"] = artifactManifestIdentity,
            ["content_digest"] = r.Stamp.GetValueOrDefault("content_digest"),
            ["code_commit"] = r.Stamp.GetValueOrDefault("code_commit"),
            // Publish only the schema authenticated by the stamp signature. Do not serve an
            // unvalidated caller alias or a supported-but-tampered schema as index_format.
            ["index_format"] = r.SignatureValid
                ? r.Stamp.GetValueOrDefault("schema")
                : null,
        };
        return envelope;
    }

    private JsonObject UnsupportedFilterResult(
        LexIndexReader reader,
        IReadOnlyList<string> unsupportedFilters,
        FilterSet filters,
        string timeScope,
        DateOnly? asOf,
        string emptyResultField)
    {
        var result = new JsonObject
        {
            ["envelope"] = Envelope(reader, McpStatus.FilterNotSupportedByIndex),
            ["unsupported_filters"] = new JsonArray(
                unsupportedFilters.Select(filter => (JsonNode)filter).ToArray()),
            ["requested_language"] = filters.Language,
            ["time_scope"] = timeScope,
            ["as_of"] = asOf?.ToString("yyyy-MM-dd"),
            ["capability_manifest"] = new JsonObject
            {
                ["schema"] = reader.Stamp.GetValueOrDefault("capability_manifest_schema"),
                ["sha256"] = reader.Stamp.GetValueOrDefault("capability_manifest_sha256"),
                ["policy_tier"] = reader.Stamp.GetValueOrDefault("capability_policy_tier"),
                ["policy_sha256"] = reader.Stamp.GetValueOrDefault("capability_policy_sha256"),
            },
            [emptyResultField] = new JsonArray(),
        };
        return result;
    }

    private static bool PublisherTextGateOpen(LexIndexReader reader) =>
        string.Equals(reader.Stamp.GetValueOrDefault("text_public"), "true",
            StringComparison.Ordinal);

    private static string TextStatus(LexIndexReader reader, DocRow d) =>
        !d.TextAvailable ? McpStatus.TextNotAvailable
        : d.TextPublic ? McpStatus.Ok
        : PublisherTextGateOpen(reader) ? McpStatus.TextNotAvailable
        : McpStatus.TextWithheld;

    private static string ComparisonTextStatus(LexIndexReader reader, DocRow a, DocRow b)
        => !a.TextAvailable || !b.TextAvailable ? McpStatus.TextNotAvailable
         : a.TextPublic && b.TextPublic ? McpStatus.Ok
         : PublisherTextGateOpen(reader) ? McpStatus.TextNotAvailable
         : McpStatus.TextWithheld;

    private static string WorkMatchNote(LexIndexReader reader, DocRow document) =>
        document.TextPublic
            ? "THIS WORK IS HELD by Lex, matched on its identifier or title rather than its wording. Call as_of on it for the text."
            : document.TextAvailable && !PublisherTextGateOpen(reader)
                ? "THIS WORK IS HELD by Lex; publisher text exists but is not publicly served. Versions, dates and provenance remain available. Never report the work as missing."
                : "THIS WORK IS HELD by Lex; versions, publisher dates and provenance are available via timeline / in_force_on / provenance, but no safely derived provision text is available. Never report the work as missing or unknown; report the text gap precisely.";

    private static string VersionCoordinate(DocRow document)
        => VersionCoordinate(document.Key);

    private static string VersionCoordinate(string lexId)
        => lexId[(lexId.LastIndexOf(':') + 1)..];

    internal string CanonicalCitationWork(DocRow source, string slug, string href)
    {
        if (slug.Contains(':', StringComparison.Ordinal)) return slug;
        if (readers.TryGetValue(source.Collection, out var local) && local.WorkExists(slug))
            return $"{source.Collection}:{slug}";
        var work = WorkKeyFromEli(href);
        if (work is not null)
        {
            var matches = readers.Values.Where(reader => reader.WorkExists(work)).Take(2).ToArray();
            if (matches.Length == 1) return $"{matches[0].Collection}:{work}";
        }
        return $"{source.Collection}:{slug}";
    }

    internal static string? WorkKeyFromEli(string href)
    {
        var match = CrossCollectionEli.Match(href);
        if (!match.Success || KnownIncorrectEliTokens.Contains(match.Value.TrimEnd('/'))) return null;
        var type = match.Groups["type"].Value.StartsWith("reg", StringComparison.Ordinal) ? 'r'
            : match.Groups["type"].Value.StartsWith("dir", StringComparison.Ordinal) ? 'l' : 'd';
        return $"3{match.Groups["year"].Value}{type}{match.Groups["number"].Value.PadLeft(4, '0')}";
    }

    private static IReadOnlyList<string> CitationHrefFragments(string targetWork)
    {
        var colon = targetWork.IndexOf(':');
        var work = (colon >= 0 ? targetWork[(colon + 1)..] : targetWork).ToLowerInvariant();
        var match = CrossCollectionWorkKey.Match(work);
        if (!match.Success) return [];
        var bare = match.Groups["number"].Value.TrimStart('0');
        if (bare.Length == 0) return [];
        var numbers = bare == match.Groups["number"].Value
            ? [bare] : new[] { bare, match.Groups["number"].Value };
        string[] tokens = match.Groups["type"].Value[0] switch
        {
            'r' => ["reg_ue", "reg", "reg_impl", "reg_del"],
            'l' => ["dir_ue", "dir", "dir_impl", "dir_del"],
            _ => ["dec", "dec_impl", "dec_del", "dec_framw", "dec_entscheid"],
        };
        return tokens.SelectMany(token => numbers.Select(number =>
                $"/eli/{token}/{match.Groups["year"].Value}/{number}"))
            .Where(fragment => !KnownIncorrectEliTokens.Contains(fragment)).ToArray();
    }

    // Base URL for permalinks is deployment config only (never derived from or stored in
    // signed content); the field is omitted entirely when unconfigured so air-gapped
    // deployments do not emit unreachable URLs.
    private JsonObject DocJson(DocRow d, bool withText)
    {
        var o = new JsonObject
        {
            ["lex_id"] = d.Key,
            ["version_key"] = VersionCoordinate(d),
            ["work"] = d.GroupKey,
            ["work_identifier"] = d.GroupIdentifier,
            ["document_type"] = d.Kind,
            // How this version's text was obtained. "akn-lu/1" and "fmx4-eu/1" mean the article
            // boundaries came from the publisher's own structural markup; "pdf-lu/1" means they
            // were inferred from a page-description format, which is a weaker claim and has to
            // travel with the text rather than sit in a file nobody reads.
            ["extraction_profile"] = d.Profile,
            ["language"] = d.Language,
            ["valid_from"] = d.ValidFrom,
            ["valid_to"] = d.ValidTo,
            ["valid_time_source"] = d.ValidTimeSource,
            ["publication_date"] = d.PublicationDate,
            ["title"] = d.TitleShort ?? d.Title,
            ["withdrawn"] = d.Withdrawn,
            ["text_available"] = d.TextAvailable,
            ["record_sha256"] = d.RecordSha,
            ["body_sha256"] = d.BodySha,
            ["source_uri"] = d.SourceUri,
            ["observed_from"] = d.ObservedFrom,
            ["text"] = withText && d.TextPublic && d.Body?.Length <= MaximumLegalTextChars
                ? d.Body : null,
        };
        if (withText && d.TextPublic && d.Body?.Length > MaximumLegalTextChars)
        {
            o["text_omitted"] = true;
            o["text_characters"] = d.Body.Length;
            o["text_omitted_reason"] = "legal text exceeds the bounded MCP item size; use source_uri";
        }
        if (d.Hierarchy is not null) o["hierarchy"] = d.Hierarchy;
        if (d.Domains is not null) o["domains"] = new JsonArray(d.Domains.Trim('|').Split('|',
            StringSplitOptions.RemoveEmptyEntries).Select(x => (JsonNode)x).ToArray());
        if (d.ActForm is not null) o["act_form"] = d.ActForm;
        if (d.BindingStatus is not null) o["binding_status"] = d.BindingStatus;
        if (d.ConsolidationStatus is not null) o["consolidation_status"] = d.ConsolidationStatus;
        if (_publicBase is not null)
            o["permalink"] = $"{_publicBase}/{d.Collection}/{d.GroupKey}/{VersionCoordinate(d)}";
        return o;
    }

    private JsonObject ProvisionJson(DocRow d, ProvisionRow p, bool withText)
    {
        var remainingTextCharacters = MaximumLegalTextChars;
        var remainingCitationRows = MaximumCitationRows;
        var citationsTruncated = false;
        return ProvisionJson(d, p, withText, ref remainingTextCharacters,
            ref remainingCitationRows, ref citationsTruncated);
    }

    private JsonObject ProvisionJson(
        DocRow d,
        ProvisionRow p,
        bool withText,
        ref int remainingTextCharacters,
        ref int remainingCitationRows,
        ref bool citationsTruncated)
    {
        var textLength = p.TextLoaded ? p.TextMd.Length : p.StoredTextCharacters;
        var includeText = withText && p.TextLoaded
            && p.TextMd.Length <= remainingTextCharacters;
        var o = new JsonObject
        {
            ["anchor"] = p.Anchor,
            ["provision_id"] = p.ProvisionId,
            ["type"] = p.PType,
            ["num"] = p.Num,
            ["heading"] = p.Heading,
            ["path"] = p.Path,
            ["article_valid_from"] = p.ArticleValidFrom,
            ["text_sha256"] = p.TextSha,
            ["text"] = includeText ? p.TextMd : null,
        };
        if (includeText)
            remainingTextCharacters -= p.TextMd.Length;
        else if (withText)
        {
            o["text_omitted"] = true;
            if (textLength is not null) o["text_characters"] = textLength.Value;
            if (p.StoredTextBytes is not null) o["text_bytes"] = p.StoredTextBytes.Value;
            o["text_omitted_reason"] = "legal text exceeds the remaining bounded MCP response text budget; use permalink or source_uri";
        }
        // The publisher's own cross-references, captured at derive time. Only with the text, since
        // an outline is a table of contents and these belong to the words.
        if (withText)
        {
            var cits = readers.GetValueOrDefault(d.Collection)?.CitationsOf(
                $"{d.Key}|{d.Language}|{d.ValidFrom}", p.Anchor,
                Math.Max(1, remainingCitationRows + 1));
            if (cits is { Count: > 0 })
            {
                var returned = Math.Min(remainingCitationRows, cits.Count);
                if (returned > 0)
                    o["citations"] = new JsonArray(cits.Take(returned).Select(c => (JsonNode)new JsonObject
                { ["work"] = CanonicalCitationWork(d, c.Slug, c.Href),
                    ["href"] = c.Href, ["text"] = c.Label }).ToArray());
                remainingCitationRows -= returned;
                if (cits.Count > returned)
                {
                    o["citations_truncated"] = true;
                    citationsTruncated = true;
                }
            }
        }
        if (_publicBase is not null)
            o["permalink"] = $"{_publicBase}/{d.Collection}/{d.GroupKey}/{VersionCoordinate(d)}#{p.Anchor}";
        return o;
    }

    private static string KnownExclusions(LexIndexReader r) =>
        r.Stamp.GetValueOrDefault("known_exclusions") ?? r.Collection switch
        {
            "lu-legilux" => "never-consolidated LU acts (~24,579 as-published lois/RGD) are not ingested; ingestion scheduled — see coverage",
            // Named, not gestured at. "Flagship acts" tells a reader nothing about whether the act
            // they care about is here, and the front page used to promise "EU law" over the top
            // of it.
            "eu-eurlex" => $"{r.Coverage(1).Groups:n0} EU works from the reviewed scope are currently mounted; the wider acquis is not yet ingested, see coverage",
            _ => "see the coverage tool for this publisher's known gaps",
        };

    private static bool ProvisionalFor(LexIndexReader r, DateOnly d)
    {
        var b = r.Stamp.GetValueOrDefault("built_at", "");
        return b.Length >= 10 && TryIsoDate(b[..10], out var bd) && d > bd;
    }

    private static bool TryIsoDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);

    /// <summary>
    /// Anchors a caller might have meant, for a mode=select miss.
    ///
    /// Two mechanical steps, no fuzzy string distance (which would rank "art_11" above "art_1"
    /// for the query "art_1"). First stay inside the same KIND of anchor: someone asking for an
    /// article wants articles, and an unfiltered digit match answered "art_1er" with
    /// "attachment_1", which is true and useless. Then match on digits rather than on words,
    /// because the mismatch is almost always a numbering convention: "article-5" against "art_5".
    ///
    /// When digits match nothing, return the first few anchors OF THAT KIND, which is the more
    /// valuable answer anyway: it shows the scheme in use. The Code du travail has no Article 1
    /// under any spelling, and seeing "art_l_010-1, art_l_111-1" says why in one line.
    /// </summary>
    private static List<string> NearestAnchors(IEnumerable<string> wanted, IReadOnlyList<ProvisionRow> all)
    {
        static string Digits(string s) => new string(s.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        static string Kind(string s)
        {
            var head = new string(s.TakeWhile(char.IsAsciiLetter).ToArray()).ToLowerInvariant();
            return head.StartsWith("art") ? "art" : head;   // art / article / arts all mean the same
        }

        var want = wanted.ToList();
        var kinds = want.Select(Kind).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        var family = kinds.Count > 0 ? all.Where(p => kinds.Contains(Kind(p.Anchor))).ToList() : [];
        if (family.Count == 0) family = [.. all];

        var keys = want.Select(Digits).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        var hit = keys.Count > 0
            ? family.Where(p => keys.Contains(Digits(p.Anchor))).Select(p => p.Anchor).Take(10).ToList()
            : [];
        return hit.Count > 0 ? hit : family.Take(6).Select(p => p.Anchor).ToList();
    }

    private static IReadOnlyList<ProvisionRow> SelectionPool(
        LexIndexReader reader,
        string rid,
        IReadOnlyList<string> wanted,
        bool withText)
    {
        if (wanted.Count > 50)
            throw new ArgumentException("anchors accepts at most 50 comma-separated values");
        var selected = withText
            ? reader.ProvisionsByAnchorsForMcp(rid, wanted, MaximumLegalTextChars)
            : reader.ProvisionOutlinesByAnchors(rid, wanted);
        return selected
            .Concat(reader.ProvisionOutlines(rid, 100))
            .GroupBy(provision => provision.Anchor, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private void AddSelectedProvisions(JsonObject output, DocRow document,
        IReadOnlyList<ProvisionRow> all, IReadOnlyList<string> wanted, bool withText,
        int? totalProvisions = null)
    {
        var found = new JsonArray();
        var missing = new JsonArray();
        var remainingTextCharacters = MaximumLegalTextChars;
        var remainingCitationRows = MaximumCitationRows;
        var citationsTruncated = false;
        foreach (var anchorName in wanted)
        {
            var provision = all.FirstOrDefault(x => x.Anchor == anchorName);
            if (provision is null) missing.Add((JsonNode)anchorName);
            else found.Add((JsonNode)ProvisionJson(
                document, provision, withText, ref remainingTextCharacters,
                ref remainingCitationRows, ref citationsTruncated));
        }

        output["provisions"] = found;
        MarkBoundedCitations(output, remainingCitationRows, citationsTruncated);
        if (missing.Count == 0) return;

        output["anchors_not_in_version"] = missing;
        if (found.Count == 0) output["envelope"]!["status"] = McpStatus.AnchorNotInVersion;
        var near = NearestAnchors(wanted, all);
        if (near.Count > 0)
            output["nearest_anchors"] = new JsonArray(near.Select(x => (JsonNode)x).ToArray());
        output["anchors_in_version"] = totalProvisions ?? all.Count;
        output["anchor_note"] = near.Count > 0
            ? "This work numbers its provisions in its own scheme. nearest_anchors are anchors it actually has; call as_of mode=select with one of those, or mode=outline without anchors for the full list. Do NOT fall back to full-text search for a provision of a known work."
            : $"This version holds {all.Count} provisions under other anchors. Call as_of mode=outline without anchors to list them, then select from that list. Do NOT fall back to full-text search for a provision of a known work.";
    }

    private static void MarkBoundedText(JsonObject output, bool rowsOmitted = false)
    {
        var documentTextOmitted = output["document"] is JsonObject document
            && document["text_omitted"]?.GetValue<bool>() == true;
        var provisionTextOmitted = output["provisions"] is JsonArray provisions
            && provisions.OfType<JsonObject>()
                .Any(provision => provision["text_omitted"]?.GetValue<bool>() == true);
        if (!rowsOmitted && !documentTextOmitted && !provisionTextOmitted) return;

        // This is a response-size fact, not a statement about corpus coverage. Keep the legal
        // status `ok`: the publisher text is held and the returned hashes/coordinates remain
        // authoritative, but callers must know that this bounded response is not the whole text.
        output["text_truncated"] = true;
        output["truncated"] = true;
    }

    private static void MarkBoundedCitations(
        JsonObject output,
        int remainingCitationRows,
        bool citationsTruncated)
    {
        output["citations_returned"] = MaximumCitationRows - remainingCitationRows;
        output["citations_truncated"] = citationsTruncated;
        if (citationsTruncated) output["truncated"] = true;
    }

    /// <summary>
    /// Restores the mounted spelling of a corpus filter and reports one that names nothing this
    /// server mounts, or null when both filters select something.
    ///
    /// Both reader selectors match an unrecognised value against nothing and hand the caller an
    /// empty set, which every tool then reports as a bare `[]`. That is precisely the
    /// indistinguishable emptiness the no-corpus guard in CallToolCore was written to eliminate:
    /// a model reading `[]` concludes the law does not exist, or that the corpus is empty, and
    /// tells its user so. An unmatched filter is a fact about the request, and must say so.
    ///
    /// Case is not that fact. Publisher selection compares ordinally, so "T-Pub" would select no
    /// reader and be reported unmounted although this server holds it; the mounted spelling is
    /// restored here instead, which is the same canonicalisation CorpusVocabulary applies on the
    /// assistant path. Jurisdiction already matches case-insensitively in SelectReaders.
    /// </summary>
    private (string Name, string Value)? UnmountedFilter(JsonObject arguments)
    {
        // A build with no verified index has no mounted set to check against, and its own guard
        // must keep answering first.
        if (!_corpusMounted) return null;
        static string? Text(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<string>(out var text) ? text : null;
        if (Text(arguments["publisher"]) is { } publisher)
        {
            var mounted = MountedPublishers.FirstOrDefault(item =>
                string.Equals(item, publisher, StringComparison.OrdinalIgnoreCase));
            if (mounted is null) return ("publisher", publisher);
            if (!string.Equals(mounted, publisher, StringComparison.Ordinal))
                arguments["publisher"] = mounted;
        }
        if (Text(arguments["jurisdiction"]) is { } jurisdiction
            && !MountedJurisdictions.Contains(jurisdiction, StringComparer.OrdinalIgnoreCase))
            return ("jurisdiction", jurisdiction);
        return null;
    }

    private JsonObject UnmountedFilterResult(string tool, (string Name, string Value) filter) => new()
    {
        ["status"] = McpStatus.UnknownPublisher,
        ["tool_called"] = tool,
        ["requested_filter"] = filter.Name,
        ["requested_value"] = filter.Value,
        ["mounted_publishers"] = new JsonArray(
            MountedPublishers.Select(item => (JsonNode)item).ToArray()),
        ["mounted_jurisdictions"] = new JsonArray(
            MountedJurisdictions.Select(item => (JsonNode)item).ToArray()),
        ["detail"] = $"No mounted {filter.Name} matches '{filter.Value}', so this call selected "
                   + "nothing to read from and returned no rows about the corpus. This is not a "
                   + "statement that the corpus is empty. Use one of the mounted values, or omit "
                   + "the filter to span everything held. Call coverage with no arguments for "
                   + "live counts.",
    };

    private (IReadOnlyList<LexIndexReader> Readers, int Total) SelectReaders(
        string? publisher,
        string? jurisdiction = null)
    {
        var selected = readers.Values.Where(reader =>
                (publisher is null || reader.Collection == publisher)
                && (jurisdiction is null || string.Equals(
                    reader.Stamp.GetValueOrDefault("jurisdiction"), jurisdiction,
                    StringComparison.OrdinalIgnoreCase)))
            .OrderBy(reader => reader.Collection, StringComparer.Ordinal)
            .ToArray();
        return (selected.Take(MaximumPublisherRows).ToArray(),
            _publisherSelectionTotal ?? selected.Length);
    }

    private static void MarkPublisherSet(JsonArray output, int total)
    {
        foreach (var item in output.OfType<JsonObject>())
            item["publisher_result_set"] = new JsonObject
            {
                ["total"] = total,
                ["returned"] = Math.Min(total, MaximumPublisherRows),
                ["maximum"] = MaximumPublisherRows,
                ["truncated"] = total > MaximumPublisherRows,
            };
    }

    private static void MarkResponseRows(
        JsonArray output,
        int maximum,
        int returned,
        bool truncated)
    {
        foreach (var item in output.OfType<JsonObject>())
            item["response_row_set"] = new JsonObject
            {
                ["maximum"] = maximum,
                ["returned"] = returned,
                ["truncated"] = truncated,
            };
    }

    private (LexIndexReader r, string norm)? Resolve(string work, string? publisher)
    {
        if (publisher is not null && readers.TryGetValue(publisher, out var rp)) return (rp, work);
        if (work.Contains(':') && !work.Contains("://"))
        {
            var pub = work.Split(':')[0];
            if (readers.TryGetValue(pub, out var rr)) return (rr, work);
        }
        foreach (var r in readers.Values)
            if (r.WorkExists(work)) return (r, work);
        return null;
    }

    public JsonNode CallTool(string name, JsonObject a) =>
        CallToolAsync(name, a, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public async ValueTask<JsonNode> CallToolAsync(
        string name,
        JsonObject a,
        CancellationToken cancellationToken)
    {
        using var activity = McpTelemetry.StartTool(name, artifactManifestIdentity);
        try
        {
            // Reject malformed work before waiting on any publisher or allocating an execution
            // session. Invalid public input must not queue behind a valid long-running search.
            McpInputPolicy.Validate(name, a);
            McpTelemetry.SetLanguageTag(activity, a);
            if (UnmountedFilter(a) is { } unmounted)
            {
                var unmountedResult = UnmountedFilterResult(name, unmounted);
                McpTelemetry.SetResultTags(activity, unmountedResult);
                return unmountedResult;
            }
            var selection = ReadersFor(name, a);
            var selected = selection.Readers;
            var held = new List<SemaphoreSlim>(selected.Count);
            try
            {
                foreach (var (_, semaphore, _) in selected)
                {
                    await semaphore.WaitAsync(cancellationToken);
                    held.Add(semaphore);
                }
            }
            catch
            {
                foreach (var semaphore in held) semaphore.Release();
                throw;
            }
            var execution = Task.Run(() =>
            {
                var sessions = new Dictionary<string, LexIndexReader>(StringComparer.Ordinal);
                CancellationTokenRegistration[] registrations = [];
                try
                {
                    // Citation targets can cross publisher boundaries. Give the execution core
                    // isolated, read-only sessions for every mounted publisher so forward citation
                    // canonicalisation can prove that the target is actually held. The callback
                    // still reports only publishers selected for the requested operation.
                    var selectedPublishers = selected
                        .Select(item => item.Publisher)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var item in readers.OrderBy(item => item.Key, StringComparer.Ordinal))
                    {
                        sessions.Add(item.Key, item.Value.CreateIsolatedSession());
                        if (selectedPublishers.Contains(item.Key))
                            _sessionOpened?.Invoke(item.Key);
                    }
                    registrations = sessions.Values
                        .Select(reader => cancellationToken.Register(reader.Interrupt)).ToArray();
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = new McpCore(
                        sessions, artifactManifestIdentity, _corpusMounted, null,
                        selection.Total, _publicBase, _hybridStatuses).CallToolCore(name, a);
                    cancellationToken.ThrowIfCancellationRequested();
                    return result;
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                finally
                {
                    foreach (var registration in registrations) registration.Dispose();
                    foreach (var session in sessions.Values) session.Dispose();
                    foreach (var semaphore in held) semaphore.Release();
                }
            }, CancellationToken.None);

            // SQLite is interrupted cooperatively. An encoder cannot be pre-empted safely, so the
            // timed-out caller leaves that one bounded worker holding its selected-reader semaphores
            // until it exits. No request keeps waiting for it and no second call can touch the same
            // reader concurrently; calls pinned to different publishers can still run in parallel.
            var completed = await execution.WaitAsync(cancellationToken);
            McpTelemetry.SetResultTags(activity, completed);
            return completed;
        }
        catch (OperationCanceledException)
        {
            McpTelemetry.SetFailure(activity, "cancelled");
            throw;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        {
            McpTelemetry.SetFailure(activity, "invalid_request");
            throw;
        }
        catch
        {
            McpTelemetry.SetFailure(activity, "failed");
            throw;
        }
    }

    private (
        IReadOnlyList<(string Publisher, SemaphoreSlim Semaphore, LexIndexReader Reader)> Readers,
        int Total)
    ReadersFor(string tool, JsonObject arguments)
    {
        static string? Text(JsonNode? node) => node is JsonValue value
            && value.TryGetValue<string>(out var text) ? text : null;
        var publisher = PublisherScopeOf(arguments);
        var work = Text(arguments["work"]) ?? Text(arguments["lex_id"]);
        if (publisher is null && tool != "cited_by" && work is not null)
            throw new ArgumentException(
                "publisher is required when work is not a publisher-qualified lex_id.",
                "publisher");
        var jurisdiction = Text(arguments["jurisdiction"]);
        var eligible = readers
            .Where(item => (publisher is null || item.Key == publisher)
                && (jurisdiction is null || string.Equals(
                    item.Value.Stamp.GetValueOrDefault("jurisdiction"), jurisdiction,
                    StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        return (eligible.Take(MaximumPublisherRows)
            .Select(item => (item.Key, _readerExecutions[item.Key], item.Value))
            .ToArray(), eligible.Length);
    }

    private JsonNode CallToolCore(string name, JsonObject a)
    {
        // A build with no corpus must say so, once, in every answer.
        //
        // Source builds and incorrectly assembled releases can start without verified artifacts.
        // Before this guard every tool answered such a caller with `[]`: coverage said [], search
        // said [], and nothing distinguished "this law does not exist" from "no law is mounted".
        // A model reading that concludes the corpus is empty and tells its user so.
        //
        // Emptiness is the one answer this project is not allowed to give silently, and
        // coverage exists specifically "to say what we do NOT have" (F10). Saying it here
        // costs one dictionary lookup per call and turns a broken-looking server into one
        // that explains itself and names where the data actually is.
        if (!_corpusMounted)
            return new JsonObject
            {
                ["status"] = McpStatus.NoCorpusMounted,
                ["detail"] = "This server started with zero verified indexes, so it holds no law "
                           + "and cannot answer legal questions. The engine is available, but its "
                           + "signed index artifacts were not mounted.",
                ["hosted_endpoint"] = "https://law.soufien.lu/mcp",
                ["hosted_endpoint_note"] = "Public, no key, no signup. Call the coverage tool "
                           + "there for live mounted counts and known gaps.",
                ["to_build_a_local_index"] = "Download and verify a signed index release, or run "
                           + "the README pipeline (ingest -> derive -> catalog -> index -> sign), "
                           + "then point LEX_INDEX_DIR at the directory holding index-*.db.",
                ["index_dir_searched"] = Environment.GetEnvironmentVariable("LEX_INDEX_DIR") ?? "indexes",
                ["tool_called"] = name,
            };

        string? Str(string k) => a[k]?.GetValue<string>();
        int Int(string k, int dflt) => a[k] is { } n && int.TryParse(n.ToString(), out var v) ? v : dflt;
        int BoundedInt(string k, int dflt, int minimum, int maximum)
        {
            var value = Int(k, dflt);
            if (value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(k,
                    $"{k} must be between {minimum} and {maximum}");
            return value;
        }

        // Every required date goes through here. `diff` used to parse its two dates with the
        // null-forgiving operator, so a caller that omitted one, or spelled it `from` instead of
        // `from_date`, got "Value cannot be null. (Parameter 's')" back: a .NET internal leaked
        // to an API consumer, naming a parameter that does not exist in the tool schema. The
        // caller here is usually a model, and a model cannot act on that. It can act on being
        // told which argument it got wrong and what shape was expected.
        DateOnly Date(string k)
        {
            var raw = Str(k) ?? throw new ArgumentException($"{k} required (ISO date, YYYY-MM-DD)");
            return DateOnly.TryParseExact(raw, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d)
                ? d : throw new ArgumentException($"{k} must be an ISO date (YYYY-MM-DD), got '{raw}'");
        }

        switch (name)
        {
            case "as_of":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var date = Date("date");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return WithWorkCandidates(new JsonObject { ["status"] = McpStatus.UnknownWork, ["work"] = work }, work, PublisherScopeOf(a));
                var (r, w) = res.Value;
                var language = Str("language");
                var versionKey = Str("version_key");
                var exactDateVersions = versionKey is null
                    ? r.VersionsEffectiveOn(w, date, language)
                    : [];
                if (versionKey is null && exactDateVersions.Count > 1)
                    return new JsonObject
                    {
                        ["envelope"] = Envelope(r, McpStatus.AmbiguousVersion, ProvisionalFor(r, date)),
                        ["work"] = w,
                        ["date"] = date.ToString("yyyy-MM-dd"),
                        ["version_choices"] = new JsonArray(exactDateVersions.Take(20).Select(choice =>
                            (JsonNode)DocJson(choice, false)).ToArray()),
                        ["choices_truncated"] = exactDateVersions.Count > 20,
                    };
                DocRow? doc;
                if (versionKey is not null)
                {
                    doc = r.VersionByCoordinate(w, date, versionKey, language);
                    if (doc is null)
                        throw new ArgumentException(
                            "version_key must identify a held version of the resolved work on date.",
                            "version_key");
                }
                else
                    doc = exactDateVersions.Count == 1
                        ? exactDateVersions[0]
                        : r.AsOf(w, date, new FilterSet(null, null, null, language));
                if (doc is null)
                {
                    // no_version_for_date stays a bare dated refusal; only the unknown-work miss
                    // carries nearest held candidates, scoped to the reader this call selected.
                    var exists = r.WorkExists(w);
                    var miss = new JsonObject
                    {
                        ["envelope"] = Envelope(r, exists ? McpStatus.NoVersionForDate : McpStatus.UnknownWork, ProvisionalFor(r, date)),
                        ["work"] = w,
                        ["date"] = date.ToString("yyyy-MM-dd"),
                    };
                    return exists ? miss : WithWorkCandidates(miss, w, r.Collection);
                }
                var status = TextStatus(r, doc);
                var mode = Str("mode") ?? "full";
                var o = new JsonObject
                {
                    ["envelope"] = Envelope(r, status, ProvisionalFor(r, date)),
                };
                var rid = LexIndexReader.RidOf(doc);
                switch (mode)
                {
                    case "outline":
                    {
                        o["document"] = DocJson(doc, withText: false);
                        var total = r.ProvisionCount(rid);
                        var wanted = (Str("anchors") ?? "").Split(',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (wanted.Length == 0)
                        {
                            var page = r.ProvisionOutlines(rid, MaximumProvisionRows);
                            o["total_provisions"] = total;
                            o["truncated"] = total > MaximumProvisionRows;
                            o["provisions"] = new JsonArray(page
                                .Select(p => (JsonNode)ProvisionJson(doc, p, withText: false)).ToArray());
                        }
                        else
                            AddSelectedProvisions(o, doc, SelectionPool(r, rid, wanted, withText: false), wanted,
                                withText: false, total);
                        break;
                    }
                    case "select":
                    {
                        var wanted = (Str("anchors") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (wanted.Length == 0) throw new ArgumentException("mode=select requires anchors");
                        o["document"] = DocJson(doc, withText: false);
                        var total = r.ProvisionCount(rid);
                        // Keep refusal metadata identical in outline and select so a client can
                        // narrow a comparison without losing the honest next step.
                        AddSelectedProvisions(o, doc, SelectionPool(r, rid, wanted, withText: doc.TextPublic), wanted,
                            withText: doc.TextPublic, total);
                        break;
                    }
                    default:
                    {
                        // full = every provision, with text. It used to return only the
                        // concatenated body, so `provisions` was the one thing full mode did
                        // NOT carry — while outline and select both did. Every client reading
                        // provisions uniformly (the workspace reader, the answer-to-view
                        // mapper) therefore saw an empty result for a document that has text,
                        // and reported "no text is held" for laws Lex holds in full. full is
                        // also as_of's DEFAULT mode, so this was the common path, not a corner.
                        //
                        // The text is returned exactly once, in the most structured form
                        // available: as provisions when the version is split into them, and as
                        // document.text when it is not.
                        var total = r.ProvisionCount(rid);
                        if (total > 0)
                        {
                            var page = r.ProvisionsForMcp(
                                rid, MaximumProvisionRows, MaximumLegalTextChars);
                            o["document"] = DocJson(doc, withText: false);
                            o["total_provisions"] = total;
                            o["truncated"] = total > MaximumProvisionRows;
                            var provisions = new JsonArray();
                            var remainingTextCharacters = MaximumLegalTextChars;
                            var remainingCitationRows = MaximumCitationRows;
                            var citationsTruncated = false;
                            foreach (var provision in page)
                                provisions.Add(ProvisionJson(doc, provision,
                                    withText: doc.TextPublic, ref remainingTextCharacters,
                                    ref remainingCitationRows, ref citationsTruncated));
                            o["provisions"] = provisions;
                            MarkBoundedCitations(o, remainingCitationRows, citationsTruncated);
                        }
                        else
                        {
                            var body = doc.TextPublic ? r.BuildBody(doc) : null;
                            if (doc.TextPublic && string.IsNullOrWhiteSpace(body))
                            {
                                status = McpStatus.TextNotAvailable;
                                o["envelope"] = Envelope(r, status, ProvisionalFor(r, date));
                            }
                            o["document"] = DocJson(doc with { Body = body }, withText: true);
                        }
                        break;
                    }
                }
                if (status == McpStatus.TextWithheld)
                    o["text_withheld_reason"] = "publisher text gate pending; read the official text at source_uri";
                else if (status == McpStatus.TextNotAvailable)
                    o["text_unavailable_reason"] = "publisher record held; no safely derived provision text is available; read source_uri";
                MarkBoundedText(o, mode == "full"
                    && o["total_provisions"]?.GetValue<int>() > MaximumProvisionRows);
                return o;
            }
            case "timeline":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return WithWorkCandidates(new JsonObject { ["status"] = McpStatus.UnknownWork, ["work"] = work }, work, PublisherScopeOf(a));
                var (r, w) = res.Value;
                var limit = Int("limit", 100); var offset = Int("offset", 0);
                var (rows, total) = r.Timeline(w, limit, offset);
                if (total == 0) return WithWorkCandidates(new JsonObject { ["envelope"] = Envelope(r, McpStatus.UnknownWork), ["work"] = w }, w, r.Collection);
                var provisional = rows.Any(row => TryIsoDate(row.Version.ValidFrom, out var validFrom)
                    && ProvisionalFor(r, validFrom));
                return new JsonObject
                {
                    ["envelope"] = Envelope(r, McpStatus.Ok, provisional),
                    ["work"] = w,
                    ["total_count"] = total,
                    ["truncated"] = total > offset + limit,
                    ["versions"] = new JsonArray(rows.Select(version =>
                    {
                        // Compatibility: retain the former flat fields using the first expression
                        // in ordinal language order, and add the complete expression collection.
                        // Pagination and total_count now operate on publisher versions, not rows.
                        var value = DocJson(version.Version, false);
                        value["expressions"] = new JsonArray(version.Expressions.Select(expression =>
                            (JsonNode)new JsonObject
                            {
                                ["language"] = expression.Language,
                                ["title"] = expression.Title,
                                ["title_short"] = expression.TitleShort,
                                ["text_available"] = expression.TextAvailable,
                                ["text_public"] = expression.TextPublic,
                                ["source_uri"] = expression.SourceUri,
                                ["body_sha256"] = expression.BodySha,
                                ["extraction_profile"] = expression.Profile,
                            }).ToArray());
                        return (JsonNode)value;
                    }).ToArray()),
                };
            }
            case "in_force_on":
            {
                var date = Date("date");
                var limit = Int("limit", 50); var offset = Int("offset", 0);
                var pub = Str("publisher");
                var jurisdiction = Str("jurisdiction");
                var outp = new JsonArray();
                var selectedReaders = SelectReaders(pub, jurisdiction);
                var filter = new FilterSet(null, null, Str("source_class") ?? Str("document_type"),
                    Str("language"), null, Str("hierarchy"), Str("act_form"),
                    Str("binding_status"), Str("domain"));
                var remainingOffset = offset;
                var remainingLimit = limit;
                var totalAcrossPublishers = 0;
                foreach (var r in selectedReaders.Readers)
                {
                    var unsupported = r.UnsupportedFilters(
                        filter, CapabilityTimeScope.AsOf, date);
                    if (unsupported.Count > 0)
                    {
                        outp.Add(UnsupportedFilterResult(
                            r, unsupported, filter, CapabilityManifest.AsOf, date, "works"));
                        continue;
                    }
                    var localOffset = remainingOffset;
                    var page = r.InForceOn(
                        date, filter, Math.Max(1, remainingLimit), localOffset);
                    var queriedRows = page.Rows;
                    var total = page.TotalGroups;
                    totalAcrossPublishers += total;
                    var skipped = Math.Min(remainingOffset, total);
                    remainingOffset -= skipped;
                    var rows = remainingLimit == 0 ? [] : queriedRows.Take(remainingLimit).ToList();
                    IReadOnlyList<InForceAmbiguity> ambiguities = remainingLimit == 0
                        ? [] : page.Ambiguities;
                    var pageUnits = rows.Count + ambiguities.Count;
                    remainingLimit -= pageUnits;
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, total == 0 ? McpStatus.NoResult
                            : ambiguities.Count > 0 ? McpStatus.AmbiguousVersion
                            : McpStatus.Ok,
                            ProvisionalFor(r, date)),
                        ["population"] = new JsonObject
                        {
                            ["basis"] = "versioned works only",
                            ["works_covered"] = r.Coverage(1).Groups,
                            ["known_exclusions"] = KnownExclusions(r),
                        },
                        ["total_works_in_force"] = total,
                        ["offset"] = localOffset,
                        ["truncated"] = total > localOffset + pageUnits,
                        ["works"] = new JsonArray(rows.Select(v => (JsonNode)DocJson(v, false)).ToArray()),
                        ["ambiguous_works"] = new JsonArray(ambiguities.Select(ambiguity =>
                            (JsonNode)new JsonObject
                            {
                                ["work"] = $"{r.Collection}:{ambiguity.GroupKey}",
                                ["valid_from"] = ambiguity.ValidFrom,
                                ["choices"] = new JsonArray(ambiguity.Choices.Take(20).Select(choice =>
                                    (JsonNode)DocJson(choice, false)).ToArray()),
                                ["choices_truncated"] = ambiguity.Choices.Count > 20,
                            }).ToArray()),
                    });
                }
                MarkPublisherSet(outp, selectedReaders.Total);
                MarkResponseRows(outp, limit, limit - remainingLimit,
                    totalAcrossPublishers > offset + (limit - remainingLimit));
                return outp;
            }
            case "diff":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var from = Date("from_date");
                var to = Date("to_date");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return WithWorkCandidates(new JsonObject { ["status"] = McpStatus.UnknownWork, ["work"] = work }, work, PublisherScopeOf(a));
                var (r, w) = res.Value;
                var f = new FilterSet(null, null, null, Str("language"));
                var language = Str("language");
                var fromVersionKey = Str("from_version_key");
                var toVersionKey = Str("to_version_key");
                var fromChoices = fromVersionKey is null ? r.VersionsEffectiveOn(w, from, language) : [];
                var toChoices = toVersionKey is null ? r.VersionsEffectiveOn(w, to, language) : [];
                var ambiguousFrom = fromVersionKey is null && fromChoices.Count > 1;
                var ambiguousTo = toVersionKey is null && toChoices.Count > 1;
                if (ambiguousFrom || ambiguousTo)
                {
                    var ambiguity = new JsonObject
                    {
                        ["envelope"] = Envelope(r, McpStatus.AmbiguousVersion,
                            ProvisionalFor(r, from) || ProvisionalFor(r, to)),
                        ["work"] = w,
                        ["from_date"] = from.ToString("yyyy-MM-dd"),
                        ["to_date"] = to.ToString("yyyy-MM-dd"),
                        ["choices_truncated"] = fromChoices.Count > 20 || toChoices.Count > 20,
                    };
                    if (ambiguousFrom)
                        ambiguity["from_version_choices"] = new JsonArray(fromChoices.Take(20)
                            .Select(choice => (JsonNode)DocJson(choice, false)).ToArray());
                    if (ambiguousTo)
                        ambiguity["to_version_choices"] = new JsonArray(toChoices.Take(20)
                            .Select(choice => (JsonNode)DocJson(choice, false)).ToArray());
                    return ambiguity;
                }
                var a1 = fromVersionKey is null
                    ? fromChoices.Count == 1 ? fromChoices[0] : r.AsOf(w, from, f)
                    : r.VersionByCoordinate(w, from, fromVersionKey, language);
                var b1 = toVersionKey is null
                    ? toChoices.Count == 1 ? toChoices[0] : r.AsOf(w, to, f)
                    : r.VersionByCoordinate(w, to, toVersionKey, language);
                if (fromVersionKey is not null && a1 is null)
                    throw new ArgumentException(
                        "from_version_key must identify a held version of the resolved work on from_date.",
                        "from_version_key");
                if (toVersionKey is not null && b1 is null)
                    throw new ArgumentException(
                        "to_version_key must identify a held version of the resolved work on to_date.",
                        "to_version_key");
                if (a1 is null || b1 is null)
                {
                    // An unknown work must never masquerade as a dated version miss: the
                    // no_version_for_date explanation claims Lex holds this law, so it is
                    // reserved for works the reader actually holds.
                    if (!r.WorkExists(w))
                        return WithWorkCandidates(new JsonObject
                        {
                            ["envelope"] = Envelope(r, McpStatus.UnknownWork),
                            ["work"] = w,
                        }, w, r.Collection);
                    return new JsonObject { ["envelope"] = Envelope(r, McpStatus.NoVersionForDate), ["from_resolved"] = a1 is not null, ["to_resolved"] = b1 is not null };
                }
                var anchor = Str("anchor")?.Trim().ToLowerInvariant();
                if (anchor is { Length: > 0 } && (anchor.Length > McpInputPolicy.MaximumAnchorLength
                    || !System.Text.RegularExpressions.Regex.IsMatch(anchor,
                        "^[a-z0-9][a-z0-9_.-]*$",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
                    throw new ArgumentException("anchor must be a provision anchor returned by search, e.g. art_92");
                ProvisionRow? fromProvision = null;
                ProvisionRow? toProvision = null;
                if (anchor is { Length: > 0 })
                {
                    var fromRid = LexIndexReader.RidOf(a1);
                    var toRid = LexIndexReader.RidOf(b1);
                    fromProvision = r.Provision(fromRid, anchor);
                    toProvision = r.Provision(toRid, anchor);
                    var exists = fromProvision is not null || toProvision is not null;
                    var anchors = r.ProvisionAnchors(fromRid, 100)
                        .Concat(r.ProvisionAnchors(toRid, 100))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    if (!exists)
                        return new JsonObject
                        {
                            ["envelope"] = Envelope(r, McpStatus.UnknownAnchor),
                            ["work"] = w,
                            ["anchor"] = anchor,
                            ["anchors_not_in_version"] = new JsonArray(
                                anchors.Take(100).Select(value => (JsonNode)value).ToArray()),
                        };
                }
                var pa = a1.Profile;
                var pb = b1.Profile;
                var profilesDiffer = pa is not null && pb is not null && pa != pb;
                var anchorTextEqual = !profilesDiffer
                    && fromProvision is not null && toProvision is not null
                    ? string.Equals(fromProvision.TextSha, toProvision.TextSha,
                        StringComparison.Ordinal)
                    : (bool?)null;
                var changed = anchor is { Length: > 0 }
                    ? (fromProvision is null) != (toProvision is null)
                      || anchorTextEqual == false
                    : a1.Key != b1.Key;

                // Two versions of the same work are only comparable provision by provision when
                // the same extraction profile produced both. The Code du travail is the proof:
                // its 2020 version came from pdf-lu/1 with 13 provisions anchored art_541-8, its
                // 2026 version from akn-lu/1 with 1,197 anchored art_l_010-1. Pair those by anchor
                // and you get "1,196 articles added", which is not a fact about Luxembourg law,
                // it is a fact about two parsers. Saying so is the whole point of F10: a caller
                // that is told the comparison is unsound can fall back to the source URIs, while
                // a caller handed a confident diff cannot know to.
                //
                // Only when BOTH profiles are known and they disagree. Profile is null when a
                // version carries no text at all, and two unknowns are not evidence of a
                // mismatch: claiming one would be the same overreach in the other direction,
                // and that case is already told the truth by text_not_available/text_withheld.
                var comparable = !profilesDiffer && a1.TextPublic && b1.TextPublic;
                var output = new JsonObject
                {
                    ["envelope"] = Envelope(r, profilesDiffer ? McpStatus.ProfilesDiffer
                                               : ComparisonTextStatus(r, a1, b1),
                        ProvisionalFor(r, from) || ProvisionalFor(r, to)),
                    ["work"] = w,
                    ["changed"] = changed,
                    ["provision_level_comparable"] = comparable,
                    ["from"] = DocJson(a1, false),
                    ["to"] = DocJson(b1, false),
                    ["note"] = profilesDiffer
                        ? $"the two versions were extracted by different profiles ({pa ?? "unknown"} vs "
                          + $"{pb ?? "unknown"}), so their provisions carry different anchor schemes and "
                          + "cannot be paired: any provision-level diff between them would report "
                          + "differences created by the extraction, not by the legislator. Compare the "
                          + "full texts, or the official source URIs on each side, instead."
                        : changed
                            ? (a1.TextPublic && b1.TextPublic
                                ? "different versions applied; retrieve both via as_of (text included) to compare, or use the web diff permalink /{publisher}/{work}/diff/{from}/{to}"
                                : "different versions applied; text diff unavailable here — compare at the official source URIs")
                            : "the same version applied on both dates",
                };
                if (anchor is { Length: > 0 })
                {
                    output["anchor"] = anchor;
                    output["anchor_from_present"] = fromProvision is not null;
                    output["anchor_to_present"] = toProvision is not null;
                    output["anchor_text_equal"] = anchorTextEqual;
                    if (!profilesDiffer)
                        output["note"] = (fromProvision is not null, toProvision is not null,
                            anchorTextEqual) switch
                        {
                            (true, false, _) => "the requested provision is present only on the earlier date",
                            (false, true, _) => "the requested provision is present only on the later date",
                            (true, true, true) => "the requested provision has the same wording on both dates",
                            (true, true, false) => "the requested provision has different wording on the two dates",
                            _ => output["note"]?.GetValue<string>(),
                        };
                }
                return output;
            }
            case "search":
            {
                var q = Str("query") ?? throw new ArgumentException("query required");
                if (q.Length > 1_000)
                    throw new ArgumentException("query must not exceed 1000 characters", "query");
                var timeScope = Str("time_scope") ?? (Str("as_of") is null ? "all_versions" : "as_of");
                if (timeScope is not ("all_versions" or "as_of")) throw new ArgumentException("time_scope must be all_versions or as_of");
                DateOnly? asOf = timeScope == "as_of" ? Date("as_of") : null;
                var pub = Str("publisher");
                var jurisdiction = Str("jurisdiction");
                var limit = BoundedInt("limit", 10, 1, 50);
                var requestedMode = Str("retrieval_mode") ?? "keyword";
                if (requestedMode is not ("keyword" or "hybrid")) throw new ArgumentException("retrieval_mode must be keyword or hybrid");
                var fuzzy = Str("fuzzy") ?? "auto";
                if (fuzzy is not ("auto" or "off")) throw new ArgumentException("fuzzy must be auto or off");
                // Optional subject scope. Ranking a national corpus by relevance alone is precise
                // only when the question uses rare words: search all of Luxembourg law for "prix"
                // and seed certification outranks the electricity act. A caller that knows which
                // works it cares about can name them.
                var works = Str("works")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .Select(w => w.Contains(':') ? w[(w.IndexOf(':') + 1)..] : w).ToArray();
                var publisherMetadataIdentifier = Str("publisher_metadata_identifier");
                var outp = new JsonArray();
                var filter = new FilterSet(asOf, null, Str("source_class") ?? Str("document_type"),
                    Str("language"), works, Str("hierarchy"), Str("act_form"),
                    Str("binding_status"), Str("domain"),
                    PublisherMetadataIdentifier: publisherMetadataIdentifier);
                var selectedReaders = SelectReaders(pub, jurisdiction);
                var capabilityTimeScope = timeScope == CapabilityManifest.AsOf
                    ? CapabilityTimeScope.AsOf : CapabilityTimeScope.AllVersions;
                var supportedReaders = new List<LexIndexReader>();
                foreach (var reader in selectedReaders.Readers)
                {
                    var unsupported = reader.UnsupportedFilters(
                        filter, capabilityTimeScope, asOf);
                    if (unsupported.Count > 0)
                        outp.Add(UnsupportedFilterResult(
                            reader, unsupported, filter, timeScope, asOf, "hits"));
                    else
                        supportedReaders.Add(reader);
                }
                foreach (var reader in supportedReaders.Where(reader =>
                             requestedMode == "hybrid" && !reader.HybridReady))
                {
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(reader, McpStatus.RetrievalModeUnavailable),
                        ["requested_retrieval_mode"] = "hybrid",
                        ["retrieval_unavailable_reason"] =
                            _hybridStatuses.GetValueOrDefault(reader.Collection, "not_activated"),
                        ["time_scope"] = timeScope,
                        ["as_of"] = asOf?.ToString("yyyy-MM-dd"),
                        ["query_expansions"] = new JsonArray(),
                        ["artifact_manifest_id"] =
                            Environment.GetEnvironmentVariable("LEX_ARTIFACT_MANIFEST_ID"),
                        ["hits"] = new JsonArray(),
                    });
                }
                var executions = supportedReaders
                    .Where(reader => requestedMode != "hybrid" || reader.HybridReady)
                    .Select(reader => (Reader: reader, Execution: requestedMode == "hybrid"
                        ? reader.SearchHybrid(q, filter, limit * 6, fuzzy == "auto")
                        : reader.SearchKeyword(q, filter, limit * 6, fuzzy == "auto")))
                    .ToList();
                var hasGlobalStrongWorkMatch = executions.Any(item =>
                    item.Execution.QueryPlan?.HasStrongWorkMatch == true);
                var globalResolutions = executions.SelectMany(item =>
                        (item.Execution.QueryPlan?.WorkResolutions ?? []).Select(resolution => new
                        {
                            resolution.Mention,
                            resolution.Status,
                            resolution.Kind,
                            Candidates = resolution.Candidates
                                .Select(candidate => $"{item.Reader.Collection}:{candidate}"),
                            ShortTitles = (resolution.PublisherShortTitleMatches ?? [])
                                .Select(match => match with
                                {
                                    Work = $"{item.Reader.Collection}:{match.Work}",
                                }),
                        }))
                    .GroupBy(item => item.Mention, StringComparer.Ordinal)
                    .Select(group =>
                    {
                        var allCandidates = group.SelectMany(item => item.Candidates)
                            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                        var status = group.Any(item => item.Status == "ambiguous") || allCandidates.Length > 1
                            ? "ambiguous" : allCandidates.Length == 1 ? "resolved" : "unresolved";
                        // Across publishers the same mention may match a title in one catalogue
                        // and an identifier in another. The stronger claim is the one reported,
                        // in the same order the per-catalogue reducer uses.
                        var kind = group.Select(item => item.Kind).Contains("publisher_short_title")
                            ? "publisher_short_title"
                            : group.Select(item => item.Kind).Contains("title") ? "title"
                            : group.Select(item => item.Kind).Contains("alias") ? "alias"
                            : group.Select(item => item.Kind).Contains("identifier") ? "identifier"
                            : null;
                        return new
                        {
                            Mention = group.Key, Status = status, Kind = kind,
                            Candidates = allCandidates.Take(20).ToArray(),
                            PublisherShortTitleMatches = group.SelectMany(item => item.ShortTitles)
                                .Distinct().OrderBy(match => match.Work, StringComparer.Ordinal)
                                .ThenBy(match => match.Language, StringComparer.Ordinal)
                                .Take(20).ToArray(),
                        };
                    }).OrderBy(item => item.Mention, StringComparer.Ordinal).ToArray();
                var globalResolutionStatus = globalResolutions.Any(item => item.Status == "ambiguous")
                    ? "ambiguous"
                    : globalResolutions.Any(item => item.Status == "unresolved") ? "unresolved"
                    : globalResolutions.Any(item => item.Status == "resolved") ? "resolved"
                    : executions.Any(item => item.Execution.QueryPlan?.WorkCatalogAvailable == false)
                        ? "unavailable" : "not_requested";
                var remainingResults = limit;
                var responseRowsTruncated = false;
                // Readers run in collection order, so a single global budget let the first
                // publisher drain it: on any query with EU matches, lu-legilux was handed a limit
                // of zero and returned an empty hits array. Each publisher that is not suppressed
                // keeps a floor of the budget reserved until it has been visited.
                var suppressed = executions.Select(item => hasGlobalStrongWorkMatch
                    && item.Execution.QueryPlan?.HasStrongWorkMatch != true).ToArray();
                var contributing = suppressed.Count(value => !value);
                var floor = contributing == 0 ? 0 : Math.Max(1, limit / contributing);
                for (var index = 0; index < executions.Count; index++)
                {
                    var (r, execution) = executions[index];
                    var suppressUnresolvedPublisher = suppressed[index];
                    var reserved = floor * suppressed.Skip(index + 1).Count(value => !value);
                    var localLimit = suppressUnresolvedPublisher
                        ? 0
                        : Math.Min(remainingResults, Math.Max(floor, remainingResults - reserved));
                    // provision-level hits: the retrieval unit is the article; at most two
                    // provisions per work so one huge code cannot monopolize the result set.
                    //
                    // Unless the caller named the works. The cap protects a corpus-wide result set
                    // from a single Code carrying thousands of articles. Once the question is
                    // scoped to named works there is nothing left to protect it from, and the cap
                    // was answering "search inside this law" with two articles however many
                    // matched: one work at a limit of 40 returned exactly 2.
                    //
                    // localLimit + 1 rather than no cap: one more row than can be returned is
                    // exactly what responseRowsTruncated needs, and it states the bound here
                    // instead of leaving the reader's oversample as the only thing holding it.
                    var perWorkCap = works is { Length: > 0 } ? localLimit + 1 : 2;
                    var eligibleHits = (suppressUnresolvedPublisher ? [] : execution.Hits)
                        .GroupBy(h => (h.Doc.GroupKey, h.Provision.Anchor)).Select(g => g.First())
                        .GroupBy(h => h.Doc.GroupKey).SelectMany(g => g.Take(perWorkCap))
                        .ToList();
                    responseRowsTruncated |= eligibleHits.Count > localLimit;
                    var hits = eligibleHits.Take(localLimit).ToList();
                    var hitsArr = new JsonArray(hits.Select(h =>
                    {
                        var d = DocJson(h.Doc, false);
                        d["anchor"] = h.Provision.Anchor.Length == 0 ? null : h.Provision.Anchor;
                        d["provision_id"] = h.Provision.ProvisionId;
                        d["provision_num"] = h.Provision.Num;
                        d["provision_heading"] = h.Provision.Heading;
                        // SQLite returns no snippet: the provision index is contentless, so the
                        // window has to be cut from the content-addressed text instead. Matched on
                        // the provision query rather than the raw one, because the work name in a
                        // question is matched against titles, not against the article's wording.
                        d["snippet"] = string.IsNullOrEmpty(h.Snippet)
                            ? r.SnippetFor(h.Provision.TextSha,
                                execution.QueryPlan?.ProvisionQuery is { Length: > 0 } pq ? pq : q)
                            : h.Snippet;
                        d["match_reasons"] = new JsonArray(h.MatchReasons.Select(x => (JsonNode)x).ToArray());
                        if (h.MatchedPublisherMetadata is { } metadata)
                            d["matched_publisher_metadata"] = PublisherMetadataJson(metadata);
                        if (h.Provision.PType == "work" && !h.Doc.TextPublic)
                            d["match_note"] = WorkMatchNote(r, h.Doc);
                        if (_publicBase is not null)
                            d["permalink"] = $"{_publicBase}/{h.Doc.Collection}/{h.Doc.GroupKey}/{VersionCoordinate(h.Doc)}"
                                + (h.Provision.Anchor.Length == 0 ? "" : $"#{h.Provision.Anchor}");
                        return (JsonNode)d;
                    }).ToArray());

                    // Identifier/title fallback: works holding no per-article text are invisible
                    // to the provision FTS, and no indexed text contains a work's own identifier
                    // (a CELEX number lives in the slug) — both must still be findable.
                    if (!suppressUnresolvedPublisher && hits.Count < localLimit)
                    {
                        var seen = hits.Select(h => h.Doc.GroupKey).ToHashSet(StringComparer.Ordinal);
                        var fallbackFilter = execution.QueryPlan?.HasStrongWorkMatch == true
                            ? filter with { Works = execution.QueryPlan.WorkConstraints }
                            : filter;
                        // `works` has to reach this branch too. It did not, so a caller that named
                        // its subject and got few article hits was handed unrelated works to fill
                        // the quota — the documented scope silently ignored on exactly the path a
                        // scoped search is most likely to take.
                        var fallbackCandidates = r.SearchWorksByIdentifierOrTitle(q,
                                     fallbackFilter, limit * 4)
                                 .GroupBy(x => x.GroupKey)
                                 .Select(g => g.OrderByDescending(x => x.ValidFrom, StringComparer.Ordinal).First())
                                 .Where(x => !seen.Contains(x.GroupKey))
                                 .ToList();
                        responseRowsTruncated |= fallbackCandidates.Count > localLimit - hits.Count;
                        foreach (var doc in fallbackCandidates.Take(localLimit - hits.Count))
                        {
                            var d = DocJson(doc, false);
                            d["match"] = "work_identifier_or_title";
                            d["match_note"] = WorkMatchNote(r, doc);
                            if (_publicBase is not null)
                                d["permalink"] = $"{_publicBase}/{doc.Collection}/{doc.GroupKey}/{VersionCoordinate(doc)}";
                            hitsArr.Add(d);
                        }
                    }
                    remainingResults -= hitsArr.Count;

                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, McpStatus.Ok),
                        ["retrieval_mode"] = execution.RetrievalMode,
                        ["time_scope"] = timeScope,
                        ["as_of"] = asOf?.ToString("yyyy-MM-dd"),
                        ["query_plan"] = execution.QueryPlan is not { } plan ? null : new JsonObject
                        {
                            ["provision_query"] = plan.ProvisionQuery,
                            ["work_constraints"] = new JsonArray(
                                plan.WorkConstraints.Select(work => (JsonNode)work).ToArray()),
                            ["article_number"] = plan.ArticleNumber,
                            ["role_intent"] = plan.RoleIntent,
                            ["has_strong_work_match"] = plan.HasStrongWorkMatch,
                            ["work_resolution_status"] = plan.WorkResolutionStatus,
                            ["work_catalog_available"] = plan.WorkCatalogAvailable,
                            ["global_work_resolution_status"] = globalResolutionStatus,
                            ["global_work_resolutions"] = new JsonArray(globalResolutions
                                .Select(resolution => (JsonNode)WorkResolutionJson(
                                    resolution.Mention, resolution.Status, resolution.Kind,
                                    resolution.Candidates,
                                    resolution.PublisherShortTitleMatches)).ToArray()),
                            ["work_resolutions"] = new JsonArray(
                                (plan.WorkResolutions ?? []).Select(resolution =>
                                    (JsonNode)WorkResolutionJson(
                                        resolution.Mention, resolution.Status, resolution.Kind,
                                        resolution.Candidates,
                                        resolution.PublisherShortTitleMatches)).ToArray()),
                        },
                        ["query_expansions"] = new JsonArray(execution.QueryExpansions.Select(x => (JsonNode)x).ToArray()),
                        ["artifact_manifest_id"] = Environment.GetEnvironmentVariable("LEX_ARTIFACT_MANIFEST_ID"),
                        ["hits"] = hitsArr,
                    });
                }
                MarkPublisherSet(outp, selectedReaders.Total);
                MarkResponseRows(outp, limit, limit - remainingResults,
                    responseRowsTruncated);
                return outp;
            }
            case "article_history":
            {
                var work = Str("work") ?? throw new ArgumentException("work required");
                var anchor = Str("anchor") ?? throw new ArgumentException("anchor required");
                var res = Resolve(work, Str("publisher"));
                if (res is null) return WithWorkCandidates(new JsonObject { ["status"] = McpStatus.UnknownWork, ["work"] = work }, work, PublisherScopeOf(a));
                var (r, w) = res.Value;
                if (!r.WorkExists(w)) return WithWorkCandidates(new JsonObject { ["envelope"] = Envelope(r, McpStatus.UnknownWork), ["work"] = w }, w, r.Collection);
                var requestedLanguage = Str("language");
                var windowFrom = Str("from_date");
                var windowTo = Str("to_date");
                var stateCount = r.ProvisionStateCount(
                    w, anchor, requestedLanguage, windowFrom, windowTo);
                var eventCount = r.AnchorEventCount(
                    w, anchor, requestedLanguage, windowFrom, windowTo);
                var states = r.ProvisionStates(
                    w, anchor, requestedLanguage, MaximumHistoryRows, windowFrom, windowTo);
                var evs = r.AnchorEvents(
                    w, anchor, requestedLanguage, MaximumHistoryRows, windowFrom, windowTo);
                // The optional window. A state OVERLAPS the window when it began on or before
                // to_date and had not yet ended at from_date; that is deliberately not "began
                // inside the window", because the state a reader most needs for "in 2024" is
                // usually the one that took effect in 2019 and was still in force throughout.
                if (stateCount == 0 && eventCount == 0)
                    return new JsonObject
                    {
                        ["envelope"] = Envelope(r, r.HasProvisionHistory(w) ? McpStatus.UnknownAnchor : McpStatus.NoProvisionHistory),
                        ["work"] = w,
                        ["anchor"] = anchor,
                        ["hint"] = "list anchors via as_of with mode=outline",
                    };
                var statesArr = new JsonArray(states.Take(MaximumHistoryRows).Select(s =>
                {
                    var document = s.InVersion is null ? null : r.ByKey(s.InVersion);
                    var o = new JsonObject
                    {
                        ["valid_from"] = s.ValidFrom,
                        ["valid_to"] = s.ValidTo,
                        ["language"] = s.Language,
                        ["text_sha256"] = s.TextSha,
                        ["in_version"] = s.InVersion,
                        ["source_uri"] = document?.SourceUri,
                        ["extraction_profile"] = document?.Profile,
                        ["record_sha256"] = document?.RecordSha,
                        ["body_sha256"] = document?.BodySha,
                    };
                    if (s.ArticleValidFrom is not null) o["article_valid_from"] = s.ArticleValidFrom;
                    if (s.ValidityConflict) o["validity_conflict"] = true;
                    if (_publicBase is not null && s.InVersion is not null)
                    {
                        var firstColon = s.InVersion.IndexOf(':');
                        var lastColon = s.InVersion.LastIndexOf(':');
                        if (firstColon > 0 && lastColon > firstColon)
                            o["permalink"] = $"{_publicBase}/{s.InVersion[..firstColon]}/{s.InVersion[(firstColon + 1)..lastColon]}/{VersionCoordinate(s.InVersion)}#{anchor}";
                    }
                    return (JsonNode)o;
                }).ToArray());
                return new JsonObject
                {
                    ["envelope"] = Envelope(r, McpStatus.Ok,
                        states.Any(state => TryIsoDate(state.ValidFrom, out var validFrom)
                            && ProvisionalFor(r, validFrom))),
                    ["work"] = w,
                    ["anchor"] = anchor,
                    ["language"] = states.FirstOrDefault()?.Language ?? evs.FirstOrDefault()?.Language ?? requestedLanguage,
                    ["distinct_texts"] = stateCount,
                    ["truncated"] = stateCount > MaximumHistoryRows || eventCount > MaximumHistoryRows,
                    ["states"] = statesArr,
                        ["anchor_events"] = new JsonArray(evs.Select(e => (JsonNode)new JsonObject
                    {
                        ["type"] = e.EType,
                        ["language"] = e.Language,
                        ["from"] = e.FromAnchor,
                        ["to"] = e.ToAnchor,
                        ["anchor"] = e.Anchor,
                        ["at_version"] = e.AtVersion,
                    }).ToArray()),
                };
            }
            case "provenance":
            {
                var key = Str("lex_id") ?? throw new ArgumentException("lex_id required");
                var publisher = PublisherScopeOf(a);
                foreach (var r in SelectReaders(publisher).Readers)
                {
                    var d = r.ByKey(key);
                    if (d is null) continue;
                    var events = r.Events(key, MaximumProvenanceRows + 1);
                    var observations = r.Observations(
                        key, Str("language"), MaximumProvenanceRows + 1);
                    return new JsonObject
                    {
                        ["envelope"] = Envelope(r, McpStatus.Ok),
                        ["document"] = DocJson(d, false),
                        ["truncated"] = events.Count > MaximumProvenanceRows
                            || observations.Count > MaximumProvenanceRows,
                        ["events"] = new JsonArray(events.Take(MaximumProvenanceRows).Select(e => (JsonNode)new JsonObject
                        {
                            ["event"] = e.Event, ["scope"] = e.Scope,
                            ["observed_from"] = e.ObservedFrom, ["detail"] = e.Detail,
                            ["first_missed_at"] = e.FirstMissedAt,
                            ["runs_missed"] = e.RunsMissed,
                            ["run_identity"] = e.RunIdentity,
                        }).ToArray()),
                        ["observations"] = new JsonArray(observations.Take(MaximumProvenanceRows).Select(o => (JsonNode)new JsonObject
                        {
                            ["language"] = o.Language, ["expr_valid_from"] = o.ExprValidFrom, ["sha256"] = o.Sha256,
                            ["observed_from"] = o.ObservedFrom, ["observed_to"] = o.ObservedTo,
                        }).ToArray()),
                        ["stamp"] = new JsonObject
                        {
                            ["signature_valid"] = r.SignatureValid,
                            ["algorithm"] = r.Stamp.GetValueOrDefault("algorithm"),
                            ["public_key"] = r.Stamp.GetValueOrDefault("public_key"),
                            ["signature"] = r.Stamp.GetValueOrDefault("signature"),
                        },
                    };
                }
                return WithWorkCandidates(new JsonObject { ["status"] = McpStatus.UnknownWork, ["lex_id"] = key }, key, publisher);
            }
            case "cited_by":
            {
                var w = Str("work") ?? throw new ArgumentException("work required");
                var lim = Int("limit", 50);
                var outp = new JsonArray();
                var selectedReaders = SelectReaders(publisher: null);
                var remaining = lim;
                var responseTruncated = false;
                foreach (var r in selectedReaders.Readers)
                {
                    var candidates = r.CitedBy(
                        w, Math.Max(1, remaining + 1), CitationHrefFragments(w));
                    var hits = remaining == 0 ? [] : candidates.Take(remaining).ToList();
                    responseTruncated |= candidates.Count > hits.Count;
                    remaining -= hits.Count;
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, hits.Count == 0 ? McpStatus.NoResult : McpStatus.Ok),
                        ["cited_work"] = w,
                        ["evidence_scope"] = "captured_cross_references_in_held_non_withdrawn_versions",
                        ["current_legal_effect_assessed"] = false,
                        ["relationship_type_assessed"] = false,
                        ["citing_articles"] = hits.Count,
                        ["citations"] = new JsonArray(hits.Select(h => (JsonNode)new JsonObject
                        {
                            ["work"] = $"{r.Collection}:{h.GroupKey}",
                            ["title"] = h.Title,
                            ["valid_from"] = h.ValidFrom,
                            ["anchor"] = h.Anchor,
                            ["num"] = h.Num,
                            ["permalink"] = _publicBase is null ? null
                                : $"{_publicBase}/{r.Collection}/{h.GroupKey}/{VersionCoordinate(h.VersionKey)}#{h.Anchor}",
                        }).ToArray()),
                    });
                }
                MarkPublisherSet(outp, selectedReaders.Total);
                MarkResponseRows(outp, lim, lim - remaining, responseTruncated);
                return outp.Count == 1 ? outp[0]!.DeepClone() : outp;
            }

            case "changes_in_period":
            {
                var fromDate = Date("from_date");
                var toDate = Date("to_date");
                if (fromDate > toDate) (fromDate, toDate) = (toDate, fromDate);
                var from = fromDate.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
                var to = toDate.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
                var pub = Str("publisher");
                var jurisdiction = Str("jurisdiction");
                // Comma-separated, and a leading "!" inverts: "!RECUEIL,!CODE_RECUEIL" asks for
                // everything that is not a thematic collection, which is what a reader means by
                // "laws" and would otherwise require naming every other type.
                var kinds = (Str("source_class") ?? Str("document_type"))
                    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var filter = new FilterSet(null, null, null, Str("language"), null,
                    Str("hierarchy"), Str("act_form"), Str("binding_status"), Str("domain"));
                var byChurn = string.Equals(Str("order"), "by_churn", StringComparison.OrdinalIgnoreCase);
                var limit = Int("limit", 20);
                var offset = Int("offset", 0);
                var outp = new JsonArray();
                var selectedReaders = SelectReaders(pub, jurisdiction);
                var supportedReaders = new List<LexIndexReader>();
                foreach (var reader in selectedReaders.Readers)
                {
                    var unsupported = reader.UnsupportedFiltersInPeriod(
                        filter, fromDate, toDate);
                    if (unsupported.Count == 0)
                    {
                        supportedReaders.Add(reader);
                        continue;
                    }
                    var refusal = UnsupportedFilterResult(
                        reader, unsupported, filter, "period",
                        null, "changes");
                    refusal["window"] = new JsonObject { ["from"] = from, ["to"] = to };
                    refusal["works_changed"] = 0;
                    refusal["new_versions"] = 0;
                    refusal["shown"] = 0;
                    refusal["offset"] = offset;
                    outp.Add(refusal);
                }
                var totalAcrossPublishers = 0;
                var globalPage = MergeGlobalChanges(
                    supportedReaders, from, to, kinds, byChurn, limit, offset, filter);
                var candidates = globalPage.Items;
                var globalRanks = candidates.ToDictionary(
                    item => (item.Reader.Collection, item.Row.GroupKey), item => item.Rank);
                foreach (var r in supportedReaders)
                {
                    var (works, versions) = r.ChangeTotals(from, to, kinds, filter);
                    totalAcrossPublishers += works;
                    var rows = candidates.Where(item => ReferenceEquals(item.Reader, r))
                        .Select(item => item.Row).ToArray();
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r,
                            works == 0 ? McpStatus.NoChangesInPeriod : McpStatus.Ok,
                            TryIsoDate(to, out var requestedTo) && ProvisionalFor(r, requestedTo)),
                        ["window"] = new JsonObject { ["from"] = from, ["to"] = to },
                        ["order"] = byChurn ? "by_churn" : "by_date",
                        ["works_changed"] = works,
                        ["new_versions"] = versions,
                        ["population"] = new JsonObject
                        {
                            ["basis"] = "distinct non-withdrawn works in the selected publisher and legal metadata scope",
                            ["works_in_scope"] = r.PopulationTotal(kinds, filter),
                            ["expected_works"] = r.Coverage(1).ExpectedWorks,
                            ["known_exclusions"] = KnownExclusions(r),
                        },
                        ["shown"] = rows.Length,
                        ["offset"] = offset,
                        ["changes"] = new JsonArray(rows.Select(c =>
                        {
                            var o = new JsonObject
                            {
                                ["work"] = $"{r.Collection}:{c.GroupKey}",
                                ["title"] = c.Title,
                                ["versions_in_period"] = c.VersionsInPeriod,
                                ["versions_total"] = c.VersionsTotal,
                                ["first_change"] = c.FirstChange,
                                ["last_change"] = c.LastChange,
                                // What this law looked like before the window touched it, and so
                                // the correct left-hand side of a diff. Comparing first_change
                                // with last_change is a comparison with itself whenever a work
                                // moved exactly once in the window, which is the usual case.
                                // Null when the window's first change is the work's own first
                                // version: there is no earlier state to show.
                                ["baseline"] = c.Baseline,
                                // 1 means the publisher reissued this act without altering a
                                // word, so "2 new versions" and "nothing changed" are both true.
                                ["distinct_texts"] = c.DistinctTexts,
                                ["wording_changed"] = c.DistinctTexts > 1,
                                ["text_comparable"] = c.TextComparable,
                                ["diff_from"] = c.Baseline ?? c.FirstChange,
                                ["diff_to"] = c.LastChange,
                                ["global_rank"] = globalRanks[(r.Collection, c.GroupKey)],
                            };
                            if (c.SourceClass is not null) o["source_class"] = c.SourceClass;
                            if (c.Hierarchy is not null) o["hierarchy"] = c.Hierarchy;
                            if (c.Domains is not null)
                                o["domains"] = new JsonArray(c.Domains.Trim('|').Split('|',
                                    StringSplitOptions.RemoveEmptyEntries).Select(x => (JsonNode)x).ToArray());
                            if (c.ActForm is not null) o["act_form"] = c.ActForm;
                            if (c.BindingStatus is not null) o["binding_status"] = c.BindingStatus;
                            if (c.Language is not null) o["language"] = c.Language;
                            if (_publicBase is not null)
                            {
                                o["permalink"] = $"{_publicBase}/{r.Collection}/{c.GroupKey}/{c.LastChange}";
                                // Built from the baseline, so a law that moved exactly once still
                                // gets a comparison link. The old condition compared FirstChange
                                // with LastChange and therefore emitted no link at all for the
                                // most common row in the report.
                                var diffFrom = c.Baseline ?? c.FirstChange;
                                if (diffFrom != c.LastChange)
                                    o["diff_permalink"] = $"{_publicBase}/{r.Collection}/{c.GroupKey}/diff/{diffFrom}/{c.LastChange}";
                            }
                            return (JsonNode)o;
                        }).ToArray()),
                        ["note"] = "a row records publisher version dates inside the window; it does not by itself assert a wording change, legal effect, or entry into force; use diff or as_of to inspect text",
                    });
                }
                MarkPublisherSet(outp, selectedReaders.Total);
                foreach (var item in outp.OfType<JsonObject>())
                {
                    var shown = item["shown"]!.GetValue<int>();
                    item["response_row_set"] = new JsonObject
                    {
                        ["maximum"] = limit,
                        ["returned"] = shown,
                        ["truncated"] = item["works_changed"]!.GetValue<int>() > shown,
                    };
                    item["global_response_row_set"] = new JsonObject
                    {
                        ["offset"] = offset,
                        ["maximum"] = limit,
                        ["returned"] = candidates.Count,
                        ["total"] = totalAcrossPublishers,
                        ["truncated"] = totalAcrossPublishers > offset + candidates.Count,
                    };
                }
                return outp;
            }
            case "coverage":
            {
                var pub = Str("publisher");
                var outp = new JsonArray();
                var selectedReaders = SelectReaders(pub);
                var remainingKinds = MaximumCoverageFacetRows;
                var remainingLanguages = MaximumCoverageFacetRows;
                var remainingBuildIssues = MaximumBuildIssueRows;
                foreach (var r in selectedReaders.Readers)
                {
                    var c = r.Coverage(Math.Max(1, Math.Max(remainingKinds, remainingLanguages)));
                    var kinds = c.Kinds.Take(remainingKinds).ToArray();
                    var languages = c.Languages.Take(remainingLanguages).ToArray();
                    var buildIssues = (c.BuildIssues ?? []).Take(remainingBuildIssues).ToArray();
                    remainingKinds -= kinds.Length;
                    remainingLanguages -= languages.Length;
                    remainingBuildIssues -= buildIssues.Length;
                    outp.Add(new JsonObject
                    {
                        ["envelope"] = Envelope(r, McpStatus.Ok),
                        ["publisher_name"] = r.Stamp.GetValueOrDefault("publisher_name"),
                        ["works"] = c.Groups,
                        ["scope_expected_works"] = c.ExpectedWorks,
                        ["build_inventory_status"] = c.ExpectedWorks is null ? "unavailable"
                            : c.Groups > c.ExpectedWorks ? "overfull"
                            : c.Groups == c.ExpectedWorks && (c.BuildIssues?.Count ?? 0) == 0
                                ? "complete" : "incomplete",
                        ["build_complete"] = c.ExpectedWorks is null ? null
                            : c.Groups == c.ExpectedWorks && (c.BuildIssues?.Count ?? 0) == 0,
                        ["build_issues"] = new JsonArray(buildIssues
                            .Select(issue => (JsonNode)new JsonObject
                            {
                                ["code"] = issue.Code,
                                ["work"] = issue.Work,
                                ["detail"] = issue.Detail,
                            }).ToArray()),
                        ["build_issues_total"] = c.BuildIssues?.Count ?? 0,
                        ["build_issues_truncated"] = (c.BuildIssues?.Count ?? 0) > buildIssues.Length,
                        ["versions"] = c.Versions,
                        ["expressions"] = c.Rows,
                        ["valid_from_earliest"] = c.EarliestValidFrom,
                        ["valid_from_latest"] = c.LatestValidFrom,
                        ["document_types"] = new JsonArray(kinds.Select(k => (JsonNode)new JsonObject
                        { ["code"] = k.Kind, ["versions"] = k.Versions, ["versions_with_text"] = k.WithText }).ToArray()),
                        ["document_types_total"] = c.TotalKinds,
                        // Which languages this corpus is in, and how rarely the same law exists
                        // in more than one. A caller planning a language filter needs to know
                        // that picking one here does not narrow a translation, it removes a
                        // publisher: Luxembourg publishes in French, the EU acts held here are
                        // English, and almost no work carries both.
                        ["languages"] = new JsonArray(languages.Select(l => (JsonNode)new JsonObject
                        { ["code"] = l.Language, ["works"] = l.Works, ["versions"] = l.Versions }).ToArray()),
                        ["languages_total"] = c.TotalLanguages,
                        ["facets_truncated"] = c.TotalKinds > kinds.Length
                            || c.TotalLanguages > languages.Length
                            || c.TotalProfiles > c.Profiles.Count,
                        ["multilingual_works"] = c.MultilingualWorks,
                        ["text"] = new JsonObject
                        {
                            ["versions_with_text_served"] = c.VersionsWithText,
                            ["versions_without_text"] = c.Versions - c.VersionsWithText,
                            ["expressions_with_text_served"] = c.TextServed,
                            ["note"] = "versions count dated consolidations and a multilingual version counts once; expressions count its language texts separately. text availability is recorded per language expression (text_available/text_public on each document); versions without text carry the official source link",
                        },
                        ["known_gaps"] = new JsonArray(
                            KnownExclusions(r),
                            r.Collection == "lu-legilux"
                                ? "coverage density follows the publisher's own digitised consolidations: dense from 2017 onward; sparse before; isolated snapshots back to 1849; forward-dated to 2030"
                                : "coverage follows the publisher's consolidation practice; future-dated versions are provisional"),
                    });
                }
                MarkPublisherSet(outp, selectedReaders.Total);
                return outp;
            }
            default:
                throw new ArgumentException($"unknown tool {name}");
        }
    }
}

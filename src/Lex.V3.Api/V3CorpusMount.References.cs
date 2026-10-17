using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// Exact references first (the plan's item 4.2; the specification of record's R59, R90 and S4-A03's resolver-first order): a
/// <c>search</c> that names no work reads its query as an exact reference before any word is searched
/// (<see cref="ExactReferenceReader"/>), and the answer reports the phase as <c>reference</c>. A reference the indexes hold opens
/// its act: Reading at the article and date the reference names (the date defaults to the search's date, else today in
/// Luxembourg, R13), or the dossier when it names neither; the answer is the address to open, and no word is searched. A
/// reference naming an act the indexes do not hold is refused <c>identifier_unknown</c> with no list, never searched as words;
/// several acts or articles are <c>ambiguous_identifier</c> with every candidate, never a guess; a date before the act's held
/// history is <c>no_version_for_date</c>, an article the version does not hold <c>anchor_not_in_version</c>. Every refusal of
/// the phase carries what was recognised (<c>recognised_*</c>), so a page can say what it read. A query that is no reference is
/// searched as before.
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string ExactReferenceRule =
        "before any word is searched, the query is read as an exact reference: an ELI, a CELEX (consolidated forms included), a " +
        "Memorial or Journal officiel A citation, a historical identifier of the publisher's catalogue, an address of this site for " +
        "law, an act by its type and date or an EU act by its conventional name, or a citation of an article of an act and of a " +
        "date; a reference the indexes hold opens its act, at the article and date it names (the date defaults to the search's " +
        "date, else to today in Luxembourg), or its dossier when it names neither, and no word is searched; a reference to an act " +
        "the indexes do not hold is refused identifier_unknown, with no list; several acts or articles are refused " +
        "ambiguous_identifier with every candidate; a date in a date's shape that names no calendar day opens nothing; a query that " +
        "is no reference is searched as words";

    private const string ReferencePhase = "exact_reference";

    /// <summary>What every <c>identifier_unknown</c> of the phase says after what would answer: the reference was read, and no word searched.</summary>
    private const string ReferenceNotHeld = "; the reference was read, and what it names is not held here, so no word was searched";

    private const string LuxembourgEliRoot = "http://data.legilux.public.lu/eli/etat/";

    private readonly object _catalogueGate = new();
    private Dictionary<string, string[]>? _memorialSubjects;
    private Dictionary<string, string[]>? _historicalSubjects;

    /// <summary>What was recognised, as the answer and every refusal of the phase say it.</summary>
    private sealed record Recognition(
        ExactReference Reference,
        string Publisher,
        string? Work = null,
        string? Title = null,
        string? TitleLanguage = null,
        string? ArticleMatchedBy = null,
        string? Date = null,
        string? DateSource = null,
        string? Language = null);

    /// <summary>The phase's report when it did not run or read no reference: the search answer carries it as <c>reference</c>.</summary>
    private static object ReferenceNotRead(string outcome) => new
    {
        phase = ReferencePhase,
        outcome,
        rule = ExactReferenceRule,
        kind = (string?)null,
        recognised = (object?)null,
        opens = (object?)null,
        reason = (string?)null,
    };

    /// <summary>
    /// The phase: null when the query is no reference (the search continues as before), else the outcome that answers the search
    /// (the address to open, or the refusal, with what was recognised).
    /// </summary>
    private V3PlatformOperationOutcome? SearchReference(
        V3PlatformOperationRequest request,
        string query,
        string language,
        string? requestedDate,
        string? mode,
        string? after,
        DateTimeOffset observedAt)
    {
        if (ExactReferenceReader.Read(query) is not { } reference)
        {
            return null;
        }

        if (after is not null)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestSchemaInvalid,
                "The operation request's 'after' names a later page, and an exact reference opens its act: it has no page of hits.");
        }

        if (reference.DateNotRead is { } notRead)
        {
            return ReferenceAnswer(request, query, language, requestedDate, mode, observedAt,
                new Recognition(reference, reference.Act.IsEurope ? "eu-eurlex" : "lu-legilux"), opens: null, reason: notRead);
        }

        return reference.Act.Form switch
        {
            ReferenceActForm.Permalink => SitePathReference(request, query, language, requestedDate, mode, reference, observedAt),
            ReferenceActForm.Celex or ReferenceActForm.EuropeName =>
                EuropeReference(request, query, language, requestedDate, mode, reference, reference.Act.Celex!, observedAt),
            _ => LuxembourgReference(request, query, language, requestedDate, mode, reference, observedAt),
        };
    }

    // ---- Luxembourg -------------------------------------------------------------------------------------------------------

    /// <summary>A reference to a Luxembourg act: located by its form, then opened at the article and date, or its dossier.</summary>
    private V3PlatformOperationOutcome LuxembourgReference(
        V3PlatformOperationRequest request,
        string query,
        string language,
        string? requestedDate,
        string? mode,
        ExactReference reference,
        DateTimeOffset observedAt)
    {
        var recognition = new Recognition(reference, "lu-legilux");
        if (_reader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
            return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement)), recognition);
        }

        string? pinnedResource = null;
        string? pinnedLanguage = null;
        string? pinnedDate = null;
        IReadOnlyList<string> workKeys;
        string? matchedTitle = null;
        string? matchedTitleLanguage = null;
        switch (reference.Act.Form)
        {
            case ReferenceActForm.LuxembourgEli:
            {
                var eli = ReadEli(reference.Act.Eli!);
                pinnedResource = eli.LegalResource;
                pinnedLanguage = eli.Language;
                pinnedDate = eli.Date;
                workKeys = _reader.ResolveWorkStates(eli.Work).Select(static state => state.WorkKey)
                    .Distinct(StringComparer.Ordinal).ToArray();
                break;
            }

            case ReferenceActForm.Memorial:
            {
                var issue = $"{LuxembourgEliRoot}leg/memorial/{reference.Act.MemorialYear}/a{reference.Act.MemorialNumber}";
                var subjects = CatalogueSubjects(memorial: true, issue);
                if (reference.Act.MemorialDate is { } issued)
                {
                    // The issue's date, when written, is the date its acts were published: an act published on another day is not
                    // in the issue cited (the date is never dropped).
                    var published = _reader.ResolveFacts(subjects)
                        .Where(static fact => string.Equals(fact.Predicate, "publicationDate", StringComparison.Ordinal))
                        .ToArray();
                    subjects = subjects.Where(subject => published.Any(fact =>
                        string.Equals(fact.SubjectIri, subject, StringComparison.Ordinal) &&
                        string.Equals(fact.ObjectValue, issued.Date, StringComparison.Ordinal))).ToArray();
                }

                workKeys = WorksOfSubjects(subjects);
                break;
            }

            case ReferenceActForm.HistoricalLegalId:
                workKeys = WorksOfSubjects(CatalogueSubjects(memorial: false, reference.Act.HistoricalLegalId!));
                break;

            case ReferenceActForm.TypeAndDate:
            {
                var date = reference.Act.ActDate!.Date;
                var prefix = $"{LuxembourgEliRoot}leg/{reference.Act.LuxembourgType}/{date[..4]}/{date[5..7]}/{date[8..10]}/";
                workKeys = _reader.ResolveWorksUnder(prefix).Select(static work => work.WorkKey)
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (reference.Act.TitleWords is { } words)
                {
                    // Title words after the date name the act among those of that day: every one of them, in order, in its title.
                    var folded = ArticleNumbers.Fold(words);
                    workKeys = workKeys.Where(key => TitlesOfWork(key).Any(title =>
                        (" " + Lex.V3.Contracts.Index.TitleFolding.NormalizeTitle(title.Title) + " ")
                            .Contains(" " + folded + " ", StringComparison.Ordinal))).ToArray();
                }

                break;
            }

            default:
            {
                // A title: the title ladder's exact and prefix tiers only; a title that only shares words with the act's is not it.
                var resolution = _reader.ResolveWorkTitle(reference.Act.Title!);
                var matched = resolution.Candidates
                    .Where(static candidate => candidate.MatchReason is "exact_normalized_title" or "prefix")
                    .ToArray();
                var states = _reader.ResolveStatesOfExpressions(matched.SelectMany(static candidate => candidate.ExpressionIris).ToArray());
                workKeys = states.Select(static state => state.WorkKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                if (workKeys.Count == 1 && matched.Length > 0)
                {
                    matchedTitle = matched[0].MatchedTitle;
                    matchedTitleLanguage = matched[0].MatchedTitleLanguage;
                }

                break;
            }
        }

        if (workKeys.Count == 0)
        {
            return WithRecognition(request, Unknown(request, query, observedAt, PublisherId.LuLegilux,
                "an act this index holds" + ReferenceNotHeld), recognition);
        }

        if (workKeys.Count > 1)
        {
            var ordered = workKeys.Order(StringComparer.Ordinal).ToArray();
            using var ambiguous = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = query,
                candidates = ordered.Select(static key => "/lu-legilux/" + key).ToArray(),
                candidate_titles = ordered.Select(key => TitlesOfWork(key).FirstOrDefault()?.Title ?? key).ToArray(),
            });
            return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement)), recognition);
        }

        var workKey = workKeys[0];
        if (matchedTitle is null && TitlesOfWork(workKey).FirstOrDefault() is { } held)
        {
            (matchedTitle, matchedTitleLanguage) = (held.Title, held.Language);
        }

        recognition = recognition with { Work = workKey, Title = matchedTitle, TitleLanguage = matchedTitleLanguage };
        return OpenLuxembourgWork(request, query, language, requestedDate, mode, recognition, workKey,
            pinnedResource, pinnedLanguage, pinnedDate, observedAt);
    }

    /// <summary>
    /// Opens one held Luxembourg work: its dossier when the reference names no article and no date; else Reading at the state
    /// the date selects among the work's states (in the version an ELI names, and the language it names, when it does) and at
    /// the article the fixed table opens.
    /// </summary>
    private V3PlatformOperationOutcome OpenLuxembourgWork(
        V3PlatformOperationRequest request,
        string query,
        string language,
        string? requestedDate,
        string? mode,
        Recognition recognition,
        string workKey,
        string? pinnedResource,
        string? pinnedLanguage,
        string? pinnedDate,
        DateTimeOffset observedAt,
        string? heldArticleId = null,
        bool reading = false)
    {
        var reference = recognition.Reference;
        if (!reading && reference.Article is null && reference.Date is null && pinnedDate is null && heldArticleId is null && pinnedResource is null)
        {
            return ReferenceAnswer(request, query, language, requestedDate, mode, observedAt, recognition, new
            {
                page = "dossier",
                address_kind = "dossier",
                publisher = "lu-legilux",
                address = "/lu-legilux/" + workKey,
                work = workKey,
                language = (string?)null,
                state_date = (string?)null,
                article_id = (string?)null,
            });
        }

        // Two dates that disagree (an ELI's consolidation date and the citation's) open nothing: neither is dropped.
        if (reference.Date is { } cited && pinnedDate is not null && !string.Equals(cited.Date, pinnedDate, StringComparison.Ordinal))
        {
            return ReferenceAnswer(request, query, language, requestedDate, mode, observedAt, recognition, opens: null, reason: "two_dates_disagree");
        }

        var states = _reader!.ResolveWorkStates(workKey);
        var scope = pinnedResource is null
            ? states
            : states.Where(state => string.Equals(state.PublisherLegalResourceIri, pinnedResource, StringComparison.Ordinal)).ToArray();
        if (scope.Count == 0)
        {
            return WithRecognition(request, Unknown(request, query, observedAt, PublisherId.LuLegilux,
                "a version of this act this index holds: the ELI names a version the index does not hold" + ReferenceNotHeld), recognition);
        }

        var textLanguage = pinnedLanguage ?? language;
        var scoped = scope.Where(state => string.Equals(state.Language, textLanguage, StringComparison.Ordinal)).ToArray();
        if (scoped.Length == 0)
        {
            using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
            {
                requested_language = textLanguage,
                available_languages = scope.Select(static state => state.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            });
            return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement)), recognition);
        }

        // The date: the reference's own; else the version's an ELI names; else the search's; else today in Luxembourg (R13). A
        // version an ELI pins opens at its own date.
        var (date, source) = reference.Date is { } own ? (own.Date, "citation")
            : pinnedDate is not null ? (pinnedDate, "identifier")
            : pinnedResource is not null && scoped.Length == 1 ? (scoped[0].ApplicabilityDate, "identifier")
            : requestedDate is not null ? (requestedDate, "search_date")
            : (TodayInLuxembourg(observedAt), "today_in_luxembourg");
        recognition = recognition with { Date = date, DateSource = source, Language = textLanguage };
        var selected = SelectAtDate(scoped, date).Selected;
        if (selected.Length == 0)
        {
            return WithRecognition(request, RefuseNoVersionForDate(request, observedAt, scoped, date, bound: null), recognition);
        }

        if (selected.Length > 1)
        {
            return WithRecognition(request, RefuseAmbiguousVersion(request, observedAt, date,
                selected.Select(StateUrl).Order(StringComparer.Ordinal).ToArray(), bound: null), recognition);
        }

        var state = selected[0];
        var permalink = StateUrl(state);
        string? articleId = null;
        if (reference.Article is { } article || heldArticleId is not null)
        {
            var ids = _reader.ResolveArticleIds(state.StateSha256);
            IReadOnlyList<string> opened;
            var withinGroup = false;
            string requested;
            if (heldArticleId is not null)
            {
                opened = ids.Contains(heldArticleId, StringComparer.Ordinal) ? [heldArticleId] : [];
                requested = heldArticleId;
            }
            else
            {
                (opened, withinGroup) = ArticleNumbers.Opened(reference.Article!, ids, ArticleNumbers.FromLuxembourgId);
                requested = reference.Article!.Printed;
            }

            recognition = recognition with { ArticleMatchedBy = withinGroup ? "within_grouped_article" : "number" };
            if (opened.Count == 0)
            {
                return WithRecognition(request, AnchorNotInVersion(request, observedAt, PublisherId.LuLegilux, requested,
                    NearestArticles(ids, reference.Article, ArticleNumbers.FromLuxembourgId, requested)), recognition);
            }

            if (opened.Count > 1)
            {
                using var ambiguous = JsonSerializer.SerializeToDocument(new
                {
                    requested_identifier = query,
                    candidates = opened.Order(StringComparer.Ordinal).Select(id => ArticlePermalink(permalink, id)).ToArray(),
                });
                return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement)), recognition);
            }

            articleId = opened[0];
        }

        // No date given (the reference names none, and the search's is today's or none): the act's readable address, which
        // follows the act and opens today's version (R67); a date given: the permalink of the version the date selects (R64).
        var readable = IsToday(source, date, observedAt);
        var target = readable ? $"/lu-legilux/{workKey}/{state.Language}" : permalink;
        return ReferenceAnswer(request, query, language, requestedDate, mode, observedAt, recognition, new
        {
            page = "reading",
            address_kind = readable ? "readable" : "permalink",
            publisher = "lu-legilux",
            address = articleId is null ? target : ArticlePermalink(target, articleId),
            work = workKey,
            language = state.Language,
            state_date = state.ApplicabilityDate,
            article_id = articleId,
        });
    }

    /// <summary>
    /// Whether a reference's date is no date the reader gave: today in Luxembourg, or the search's own date when it is today's (the
    /// home page's box starts at today). A date the reference names, or a search's other date, is a date given.
    /// </summary>
    private static bool IsToday(string source, string date, DateTimeOffset observedAt) =>
        source == "today_in_luxembourg" ||
        (source == "search_date" && string.Equals(date, TodayInLuxembourg(observedAt), StringComparison.Ordinal));

    /// <summary>
    /// A Luxembourg ELI's parts: the work (the path up to its version, language or format segment), the legal resource it names
    /// (<c>{work}/jo</c>, <c>{work}/consolide/{yyyymmdd}</c>) or null, the language its expression names (<c>fr</c> is
    /// <c>fra</c>) or null, and the date a version segment names (a consolidation's, or a code version's) or null.
    /// </summary>
    private static (string Work, string? LegalResource, string? Language, string? Date) ReadEli(string eli)
    {
        var segments = eli[LuxembourgEliRoot.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        var work = new List<string>();
        var index = 0;
        for (; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment is "jo" or "consolide" || EliLanguages.ContainsKey(segment) || segment is "xml" or "html" or "pdf" ||
                (segment.Length == 8 && segment.All(char.IsAsciiDigit)))
            {
                break;
            }

            work.Add(segment);
        }

        var workIri = LuxembourgEliRoot + string.Join('/', work);
        string? resource = null, language = null, date = null;
        if (index < segments.Length && segments[index] == "jo")
        {
            resource = workIri + "/jo";
            index++;
        }
        else if (index + 1 < segments.Length && segments[index] == "consolide" && IsEightDigitDay(segments[index + 1]))
        {
            resource = workIri + "/consolide/" + segments[index + 1];
            date = DayOf(segments[index + 1]);
            index += 2;
        }
        else if (index < segments.Length && IsEightDigitDay(segments[index]))
        {
            // A code's version: the work's date-named version (code/travail/20210315).
            date = DayOf(segments[index]);
            index++;
        }

        if (index < segments.Length && EliLanguages.TryGetValue(segments[index], out var named))
        {
            language = named;
        }

        return (workIri, resource, language, date);
    }

    private static readonly IReadOnlyDictionary<string, string> EliLanguages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fr"] = "fra",
        ["de"] = "deu",
        ["en"] = "eng",
        ["lb"] = "ltz",
    };

    private static bool IsEightDigitDay(string segment) =>
        segment.Length == 8 && DateOnly.TryParseExact(segment, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    private static string DayOf(string yyyymmdd) => $"{yyyymmdd[..4]}-{yyyymmdd[4..6]}-{yyyymmdd[6..8]}";

    /// <summary>The held works of catalogue subjects (legal resources or works): each subject's work by its ELI, held or not.</summary>
    private string[] WorksOfSubjects(IReadOnlyList<string> subjects) =>
        subjects
            .Where(static subject => subject.StartsWith(LuxembourgEliRoot, StringComparison.Ordinal))
            .Select(subject => ReadEli(subject).Work)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(work => _reader!.ResolveWorkStates(work).Select(static state => state.WorkKey))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The catalogue's subjects of one Mémorial issue (by its IRI) or of one historical legal identifier, from the fact table
    /// read once per mount (the index does not change while it is mounted).
    /// </summary>
    private string[] CatalogueSubjects(bool memorial, string key)
    {
        lock (_catalogueGate)
        {
            if (_memorialSubjects is null || _historicalSubjects is null)
            {
                var memorials = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                var historical = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                foreach (var (subject, predicate, value) in _reader!.ResolveCatalogueReferences())
                {
                    var map = string.Equals(predicate, "isPartOf", StringComparison.Ordinal) ? memorials : historical;
                    if (!map.TryGetValue(value, out var subjects))
                    {
                        map[value] = subjects = new SortedSet<string>(StringComparer.Ordinal);
                    }

                    subjects.Add(subject);
                }

                _memorialSubjects = memorials.ToDictionary(static entry => entry.Key, static entry => entry.Value.ToArray(), StringComparer.Ordinal);
                _historicalSubjects = historical.ToDictionary(static entry => entry.Key, static entry => entry.Value.ToArray(), StringComparer.Ordinal);
            }

            return (memorial ? _memorialSubjects : _historicalSubjects).TryGetValue(key, out var found) ? found : [];
        }
    }

    /// <summary>A held work's titles, French first, then the rest in language order: what a recognition and a candidate name it by.</summary>
    private IReadOnlyList<LuxembourgIndexWorkTitle> TitlesOfWork(string workKey)
    {
        var expressions = _reader!.ResolveWorkStates(workKey).Select(static state => state.ExpressionIri)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return _reader.ResolveWorkTitles(expressions)
            .OrderBy(static title => title.Language == "fra" ? 0 : 1)
            .ThenBy(static title => title.Language, StringComparer.Ordinal)
            .ThenBy(static title => title.TitleKind == "title" ? 0 : 1)
            .ThenBy(static title => title.Title, StringComparer.Ordinal)
            .ToArray();
    }

    // ---- EU ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A reference to an EU act by its CELEX: its dossier when it names no article and no date (a consolidated CELEX names its
    /// version's date); else Reading at the wording the date selects (the EU time view, where the mount holds its census) and
    /// at the provision the fixed table opens.
    /// </summary>
    private V3PlatformOperationOutcome EuropeReference(
        V3PlatformOperationRequest request,
        string query,
        string language,
        string? requestedDate,
        string? mode,
        ExactReference reference,
        string celex,
        DateTimeOffset observedAt,
        string? pinnedLanguage = null,
        string? pinnedDate = null,
        string? heldProvision = null,
        bool reading = false)
    {
        var recognition = new Recognition(reference, "eu-eurlex");
        if (_europeReader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "eu" });
            return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement)), recognition);
        }

        var seeds = EuropeSeedsOf(celex);
        if (seeds.Count > 1)
        {
            using var ambiguous = JsonSerializer.SerializeToDocument(new { requested_identifier = query, candidates = seeds });
            return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.EuEurLex),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement)), recognition);
        }

        var seed = seeds.Count == 1 ? seeds[0] : null;
        var expressions = _europeReader.ResolveExact(celex);
        if (seed is null && expressions.Count == 0)
        {
            return WithRecognition(request, Unknown(request, query, observedAt, PublisherId.EuEurLex,
                "an EU act this EU index holds" + ReferenceNotHeld), recognition);
        }

        var act = seed ?? celex;
        recognition = recognition with { Work = act };

        // A consolidated CELEX names its version's date (02016R0679-20160504); a second date that disagrees opens nothing.
        var versionDate = pinnedDate ?? (celex.Length > 11 && celex[0] == '0' && celex[^9] == '-' && IsEightDigitDay(celex[^8..]) ? DayOf(celex[^8..]) : null);
        if (reference.Date is { } cited && versionDate is not null && !string.Equals(cited.Date, versionDate, StringComparison.Ordinal))
        {
            return ReferenceAnswer(request, query, language, requestedDate, mode, observedAt, recognition, opens: null, reason: "two_dates_disagree");
        }

        if (!reading && reference.Article is null && reference.Date is null && versionDate is null && heldProvision is null)
        {
            return ReferenceAnswer(request, query, language, requestedDate, mode, observedAt, recognition, new
            {
                page = "dossier",
                address_kind = "dossier",
                publisher = "eu-eurlex",
                address = "/eu-eurlex/" + act,
                work = act,
                language = (string?)null,
                state_date = (string?)null,
                article_id = (string?)null,
            });
        }

        var textLanguage = pinnedLanguage ?? language;
        string permalink;
        string wordingDate;
        IReadOnlyList<string> provisions;
        var readable = false;
        if (seed is not null && HasEuropeCensus())
        {
            var (date, source) = reference.Date is { } own ? (own.Date, "citation")
                : versionDate is not null ? (versionDate, "identifier")
                : requestedDate is not null ? (requestedDate, "search_date")
                : (TodayInLuxembourg(observedAt), "today_in_luxembourg");
            recognition = recognition with { Date = date, DateSource = source, Language = textLanguage };
            var timeline = EuropeTimelineOf(seed);
            if (SelectEuropeAt(request, timeline, date, textLanguage, observedAt, out var answered) is { } refused)
            {
                return WithRecognition(request, refused, recognition);
            }

            var entry = answered[0].Entry!;
            var pin = EuropeStatePin(seed, entry.Answer!, entry.Date);
            (permalink, wordingDate, provisions) = (pin.Permalink, entry.Date, pin.Provisions);

            // No date given: the act's readable address, which opens the wording that answers today (R67).
            readable = IsToday(source, date, observedAt);
        }
        else
        {
            // No census: the one wording this EU index holds of the act's expression, which answers no date.
            recognition = recognition with { Language = textLanguage };
            if (reference.Date is not null || versionDate is not null)
            {
                using var unavailableMode = JsonSerializer.SerializeToDocument(new
                {
                    requested_mode = "r6_as_of",
                    available_modes = new[] { "r0_exact_coordinate" },
                });
                return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.EuEurLex),
                    new V3PlatformOperationRefusal(request, "retrieval_mode_unavailable", unavailableMode.RootElement)), recognition);
            }

            var inLanguage = expressions.Where(expression => string.Equals(expression.Language, textLanguage, StringComparison.Ordinal))
                .Select(static expression => expression.PublisherExpressionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (inLanguage.Length == 0)
            {
                using var unavailableLanguage = JsonSerializer.SerializeToDocument(new
                {
                    requested_language = textLanguage,
                    available_languages = expressions.Select(static expression => expression.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                });
                return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.EuEurLex),
                    new V3PlatformOperationRefusal(request, "language_not_available", unavailableLanguage.RootElement)), recognition);
            }

            if (inLanguage.Length > 1 || EuropeWordingOf(inLanguage[0]) is not { } wording)
            {
                using var ambiguous = JsonSerializer.SerializeToDocument(new { requested_identifier = query, candidates = inLanguage });
                return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.EuEurLex),
                    new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement)), recognition);
            }

            (permalink, wordingDate, provisions) = (wording.Permalink, wording.WordingDate, wording.Provisions);
        }

        string? provision = null;
        if (heldProvision is not null)
        {
            if (!provisions.Contains(heldProvision, StringComparer.Ordinal))
            {
                return WithRecognition(request, AnchorNotInVersion(request, observedAt, PublisherId.EuEurLex, heldProvision,
                    NearestAnchors(provisions, heldProvision)), recognition);
            }

            provision = heldProvision;
        }
        else if (reference.Article is { } article)
        {
            var (opened, withinGroup) = ArticleNumbers.Opened(article, provisions, ArticleNumbers.FromEuropeId);
            recognition = recognition with { ArticleMatchedBy = withinGroup ? "within_grouped_article" : "number" };
            if (opened.Count == 0)
            {
                return WithRecognition(request, AnchorNotInVersion(request, observedAt, PublisherId.EuEurLex, article.Key,
                    NearestArticles(provisions, article, ArticleNumbers.FromEuropeId, article.Key)), recognition);
            }

            if (opened.Count > 1)
            {
                using var ambiguous = JsonSerializer.SerializeToDocument(new
                {
                    requested_identifier = query,
                    candidates = opened.Order(StringComparer.Ordinal).Select(id => EuropeProvisionPermalink(permalink, id)).ToArray(),
                });
                return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt, PublisherId.EuEurLex),
                    new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement)), recognition);
            }

            provision = opened[0];
        }

        var target = readable ? $"/eu-eurlex/{act}/{textLanguage}" : permalink;
        return ReferenceAnswer(request, query, language, requestedDate, mode, observedAt, recognition, new
        {
            page = "reading",
            address_kind = readable ? "readable" : "permalink",
            publisher = "eu-eurlex",
            address = provision is null ? target : EuropeProvisionPermalink(target, provision),
            work = act,
            language = textLanguage,
            state_date = wordingDate,
            article_id = provision,
        });
    }

    // ---- This site's addresses --------------------------------------------------------------------------------------------

    /// <summary>
    /// A reference that is an address of this site for law, in any form R64's reader accepts (a permalink, at an article or not;
    /// a work's coordinate; a dated coordinate; an EU permalink of the shape before R64), as a path, under an interface
    /// language or under this origin: opened when the indexes hold what it names.
    /// </summary>
    private V3PlatformOperationOutcome SitePathReference(
        V3PlatformOperationRequest request,
        string query,
        string language,
        string? requestedDate,
        string? mode,
        ExactReference reference,
        DateTimeOffset observedAt)
    {
        var address = reference.Act.Address!;
        var segments = address.Split('/');
        if (address.StartsWith("/eu-eurlex/", StringComparison.Ordinal))
        {
            var recognised = new Recognition(reference, "eu-eurlex");
            if (TryParseEuropePermalink(address, out var celex, out var wordingLanguage, out var wordingDate, out _, out _))
            {
                return OpenAddressAsTyped(request, query, language, requestedDate, mode, recognised with { Work = celex }, address, "eu-eurlex",
                    celex, wordingLanguage, wordingDate, observedAt, held: EuropeSeedsOf(celex).Count > 0 || (_europeReader?.ResolveExact(celex).Count ?? 0) > 0);
            }

            // The shape before R64 (/eu-eurlex/{celex}/{lng}/{date}--{sha256}), moved for good to R64's.
            if (segments.Length == 5 && segments[3].Length == 3 && segments[4].Length == 76 &&
                TryParseEuropePermalink($"/eu-eurlex/{segments[2]}/{segments[4]}/{segments[3]}", out var movedCelex, out var movedLanguage, out var movedDate, out _, out _))
            {
                var moved = $"/eu-eurlex/{segments[2]}/{segments[4]}/{segments[3]}";
                return OpenAddressAsTyped(request, query, language, requestedDate, mode, recognised with { Work = movedCelex }, moved, "eu-eurlex",
                    movedCelex, movedLanguage, movedDate, observedAt,
                    held: EuropeSeedsOf(movedCelex).Count > 0 || (_europeReader?.ResolveExact(movedCelex).Count ?? 0) > 0);
            }

            // An act's address (/eu-eurlex/{celex}), and the EU stable coordinate (/eu-eurlex/{celex}/{lng}/{date}).
            if (segments.Length == 3 && segments[2].Length > 0)
            {
                return EuropeReference(request, query, language, requestedDate, mode, reference, segments[2], observedAt);
            }

            if (segments.Length == 5 && segments[3].Length == 3 && segments[3].All(char.IsAsciiLetterLower) &&
                DateOnly.TryParseExact(segments[4], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
            {
                return EuropeReference(request, query, language, requestedDate, mode, reference, segments[2], observedAt,
                    pinnedLanguage: segments[3], pinnedDate: segments[4]);
            }

            // An act's readable address (/eu-eurlex/{celex}/{language}[/{provision}]): the wording that answers today, at that provision.
            if (segments.Length is 4 or 5 && segments[2].Length > 0 && segments[3].Length == 3 && segments[3].All(char.IsAsciiLetterLower) &&
                (segments.Length == 4 || IsAnchorSegment(segments[4])))
            {
                return EuropeReference(request, query, language, requestedDate, mode, reference, segments[2], observedAt,
                    pinnedLanguage: segments[3], heldProvision: segments.Length == 5 ? Uri.UnescapeDataString(segments[4]) : null, reading: true);
            }

            return WithRecognition(request, Unknown(request, query, observedAt, PublisherId.EuEurLex,
                "an address of this site for EU law: /eu-eurlex/{celex}, or a permalink /eu-eurlex/{celex}/{date}--{sha256}/{language}" + ReferenceNotHeld), recognised);
        }

        var recognition = new Recognition(reference, "lu-legilux");
        if (_reader is null)
        {
            using var unmounted = JsonSerializer.SerializeToDocument(new { required_corpus = "lu" });
            return WithRecognition(request, V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt, PublisherId.LuLegilux),
                new V3PlatformOperationRefusal(request, "no_corpus_mounted", unmounted.RootElement)), recognition);
        }

        if (TryParsePinnedPermalink(address, out var workKey, out var applicabilityDate, out _, out var stateLanguage, out _))
        {
            var held = _reader.ResolveState(workKey, applicabilityDate)
                .Any(state => string.Equals(state.Language, stateLanguage, StringComparison.Ordinal));
            return OpenAddressAsTyped(request, query, language, requestedDate, mode, recognition with { Work = workKey }, address, "lu-legilux",
                workKey, stateLanguage, applicabilityDate, observedAt, held);
        }

        if (TryParseStableWorkCoordinate(address, out var work) && _reader.ResolveWorkStates(work).Count > 0)
        {
            return OpenLuxembourgWork(request, query, language, requestedDate, mode, recognition with { Work = work }, work, null, null, null, observedAt);
        }

        if (TryParseStableStateCoordinate(address, out var datedWork, out var stateDate) && _reader.ResolveWorkStates(datedWork).Count > 0)
        {
            return OpenLuxembourgWork(request, query, language, requestedDate, mode, recognition with { Work = datedWork }, datedWork, null, null, stateDate, observedAt);
        }

        // An act's readable address (/lu-legilux/{work}/{language}[/{article id}]): its version today, at that article.
        if (segments.Length is 4 or 5 && segments[3].Length == 3 && segments[3].All(char.IsAsciiLetterLower) &&
            IsWorkKey(segments[2]) && (segments.Length == 4 || IsAnchorSegment(segments[4])) &&
            _reader.ResolveWorkStates(segments[2]).Count > 0)
        {
            var articleId = segments.Length == 5 ? Uri.UnescapeDataString(segments[4]) : null;
            return OpenLuxembourgWork(request, query, language, requestedDate, mode, recognition with { Work = segments[2] }, segments[2],
                null, segments[3], null, observedAt, heldArticleId: articleId, reading: true);
        }

        return WithRecognition(request, Unknown(request, query, observedAt, PublisherId.LuLegilux,
            "an address of this site for a Luxembourg act this index holds: /lu-legilux/{work}, or a permalink /lu-legilux/{work}/{date}--{sha256}/{language}" + ReferenceNotHeld), recognition);
    }

    /// <summary>A permalink opens as written (its own page says when its pin is not the version served) when its act is held.</summary>
    private V3PlatformOperationOutcome OpenAddressAsTyped(
        V3PlatformOperationRequest request,
        string query,
        string language,
        string? requestedDate,
        string? mode,
        Recognition recognition,
        string address,
        string publisher,
        string work,
        string textLanguage,
        string stateDate,
        DateTimeOffset observedAt,
        bool held)
    {
        var context = publisher == "eu-eurlex" ? PublisherId.EuEurLex : PublisherId.LuLegilux;
        if (!held)
        {
            return WithRecognition(request, Unknown(request, query, observedAt, context,
                "a permalink of a version this site holds" + ReferenceNotHeld), recognition);
        }

        var segments = address.Split('/');
        return ReferenceAnswer(request, query, language, requestedDate, mode, observedAt,
            recognition with { Date = stateDate, DateSource = "identifier", Language = textLanguage }, new
            {
                page = "reading",
                address_kind = "permalink",
                publisher,
                address,
                work,
                language = textLanguage,
                state_date = stateDate,
                article_id = segments.Length == 6 ? Uri.UnescapeDataString(segments[5]) : null,
            });
    }

    // ---- The answer and its refusals --------------------------------------------------------------------------------------

    /// <summary>
    /// The search's answer when the phase recognised a reference that answers it: the address to open (<paramref name="opens"/>),
    /// or why nothing is opened (<paramref name="reason"/>); no word was searched, and the answer says so.
    /// </summary>
    private V3PlatformOperationOutcome ReferenceAnswer(
        V3PlatformOperationRequest request,
        string query,
        string language,
        string? requestedDate,
        string? mode,
        DateTimeOffset observedAt,
        Recognition recognition,
        object? opens,
        string? reason = null)
    {
        var europe = recognition.Publisher == "eu-eurlex";
        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_query = query,
            requested_language = language,
            requested_date = requestedDate,
            requested_identifier = (string?)null,
            requested_mode = mode,
            requested_after = (string?)null,
            publisher = recognition.Publisher,
            reference = new
            {
                phase = ReferencePhase,
                outcome = opens is null ? "not_opened" : "opens",
                rule = ExactReferenceRule,
                kind = recognition.Reference.Kind,
                recognised = RecognisedMember(recognition),
                opens,
                reason,
            },
            lanes_run = false,
            hits = Array.Empty<object>(),
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = europe ? _europeReader?.IndexRef.Sha256 : _reader?.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt, europe ? PublisherId.EuEurLex : PublisherId.LuLegilux),
            new V3PlatformOperationResult(request, "quote", result.RootElement));
    }

    private static object RecognisedMember(Recognition recognition)
    {
        var reference = recognition.Reference;
        return new
        {
            text = reference.Text,
            act = new
            {
                form = reference.ActForm,
                as_typed = reference.Act.AsTyped,
                identifier = reference.Act.Label,
                work = recognition.Work,
                title = recognition.Title,
                title_language = recognition.TitleLanguage,
            },
            article = reference.Article is not { } article ? null : new
            {
                as_typed = reference.ArticleAsTyped,
                number = recognition.Publisher == "eu-eurlex" ? article.Key : article.Printed,
                key = article.Key,
                matched_by = recognition.ArticleMatchedBy,
            },
            date = reference.DateAsTyped is null && recognition.Date is null ? null : new
            {
                as_typed = reference.DateAsTyped,
                date = recognition.Date ?? reference.Date?.Date,
                source = recognition.DateSource ?? (reference.DateAsTyped is null ? null : "citation"),
            },
            language = recognition.Language,
            article_rule = reference.Article is null ? null : ArticleNumbers.Rule,
        };
    }

    /// <summary>
    /// A refusal of the phase with what was recognised beside its payload, as scalars (a refusal payload carries scalars and
    /// lists of scalars): the kind, the act, the act's title when held, the article and the date the reference names, and where
    /// the date came from. A success is returned unchanged.
    /// </summary>
    private static V3PlatformOperationOutcome WithRecognition(
        V3PlatformOperationRequest request, V3PlatformOperationOutcome outcome, Recognition recognition)
    {
        if (outcome.Refusal is not { } refusal)
        {
            return outcome;
        }

        var reference = recognition.Reference;
        var payload = JsonNode.Parse(refusal.HelpfulPayload.GetRawText())!.AsObject();
        payload["recognised_kind"] = reference.Kind;
        payload["recognised_act"] = reference.Act.Label;
        payload["recognised_title"] = recognition.Title;
        payload["recognised_article"] = reference.Article is null
            ? null
            : recognition.Publisher == "eu-eurlex" ? reference.Article.Key : reference.Article.Printed;
        payload["recognised_date"] = recognition.Date ?? reference.Date?.Date;
        payload["recognised_date_source"] = recognition.DateSource ?? (reference.Date is null ? null : "citation");
        using var document = JsonSerializer.SerializeToDocument(payload);
        return V3PlatformOperationOutcome.Refused(
            outcome.Context,
            new V3PlatformOperationRefusal(request, refusal.Code, document.RootElement));
    }

    /// <summary>
    /// The held article ids nearest a cited number that no article holds: those whose key shares the longest beginning with the
    /// cited key (42bis and 42-1 beside 42), at most ten in ordinal order; when none shares one, the rule every refusal of this
    /// code uses (<see cref="NearestAnchors"/>). A stated rule, not a search: nothing is matched by content, and none is opened.
    /// </summary>
    private static string[] NearestArticles(
        IReadOnlyList<string> held, ArticleNumber? cited, Func<string, ArticleNumber?> read, string requested)
    {
        if (cited is not null)
        {
            var scored = held
                .Select(id => (Id: id, Prefix: read(id) is { Reading: not ArticleNumberReading.Quoted } number
                    ? CommonPrefixLength(number.Key, cited.Key)
                    : 0))
                .Where(static pair => pair.Prefix > 0)
                .ToArray();
            if (scored.Length > 0)
            {
                var longest = scored.Max(static pair => pair.Prefix);
                return scored.Where(pair => pair.Prefix == longest).Select(static pair => pair.Id)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(10).ToArray();
            }
        }

        return NearestAnchors(held, requested);
    }

    /// <summary>
    /// The phase's <c>anchor_not_in_version</c>: the article the reference names is not one the version holds; its nearest held
    /// ids (<see cref="NearestArticles"/>), and the rule that no word is searched instead.
    /// </summary>
    private V3PlatformOperationOutcome AnchorNotInVersion(
        V3PlatformOperationRequest request, DateTimeOffset observedAt, PublisherId publisher, string requested, IReadOnlyList<string> nearest)
    {
        if (nearest.Count == 0)
        {
            // A version that holds no article names no neighbourhood, which the refusal contract requires: the article is unknown.
            return Unknown(request, requested, observedAt, publisher, "an article of a version this index holds with its articles");
        }

        using var notInVersion = JsonSerializer.SerializeToDocument(new
        {
            requested_anchor = requested,
            nearest_anchors = nearest,
            do_not_fall_back_to_full_text_search = true,
            what_would_answer = AnchorNotInVersionRoutes,
            asserts_absence_of_law = false,
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt, publisher),
            new V3PlatformOperationRefusal(request, "anchor_not_in_version", notInVersion.RootElement));
    }

    /// <summary>Today, the calendar date in Luxembourg at the answer's instant (R13), by the coded rule.</summary>
    private static string TodayInLuxembourg(DateTimeOffset observedAt) =>
        LuxembourgCalendar.DateAt(observedAt).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

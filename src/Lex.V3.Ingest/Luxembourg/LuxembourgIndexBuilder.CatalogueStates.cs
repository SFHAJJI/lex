using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// Index 9's states, from the run's catalogue (design §3.1): every catalogue state of a served work gets a row, held or
/// textless, dated by its role, with the publisher's end and the boundary kind of its transition to the next state, its
/// publication date, why its text is held or not, what its own file says, and, for a translation, the French state whose
/// interval it takes. Pure functions of the catalogue's facts (the <c>work_facts</c> rows), the held bodies and what the
/// derivations made of them, and the corpus records, so a test or a read-only probe can run them without an envelope.
/// </summary>
public static partial class LuxembourgIndexBuilder
{
    // ---- Tokens of the states table (index 9). ----
    internal const string KindAsPublished = "as_published";
    internal const string KindConsolidation = "consolidation";
    internal const string KindCode = "code";

    internal const string RoleApplicability = "applicability";
    internal const string RoleApplicabilityInFile = "applicability_in_file";
    internal const string RoleEntryIntoForce = "entry_into_force";

    internal const string SourcePublisher = "publisher";
    internal const string SourceDerived = "derived";

    internal const string BoundaryHalfOpen = "half_open";
    internal const string BoundaryInclusiveSuspected = "inclusive_suspected";
    internal const string BoundaryInvalid = "invalid";
    internal const string BoundaryDerived = "derived";

    internal const string TextHeld = "held";
    internal const string TextWithheldRights = "withheld_rights";
    internal const string TextNotAvailablePdf = "not_available_pdf";
    internal const string TextNotAvailableDerivation = "not_available_derivation";
    internal const string TextNotAvailableMisfiled = "not_available_misfiled";
    internal const string TextNotAcquired = "not_acquired";

    internal const string IntervalSameDate = "same_date";
    internal const string IntervalContiguous = "contiguous";
    internal const string IntervalDerived = "derived";
    internal const string IntervalInclusiveSuspected = "inclusive_suspected";
    internal const string IntervalHole = "hole";
    internal const string IntervalOverlap = "overlap";
    internal const string IntervalInvalid = "invalid";

    private const string FrenchLanguageIri = "http://publications.europa.eu/resource/authority/language/FRA";
    private const string FrenchLanguage = "fra";
    private const string ComplexWorkIri = "http://data.legilux.public.lu/resource/ontology/jolux#ComplexWork";

    /// <summary>
    /// The catalogue predicates a state row reads, by their wire names in <c>work_facts</c>. Every other fact is kept in
    /// <c>work_facts</c> and read by no state.
    /// </summary>
    private static readonly HashSet<string> StatePredicates = new(StringComparer.Ordinal)
    {
        "dateApplicability", "dateEndApplicability", "dateEntryInForce", "publicationDate", "isMemberOf", "isRealizedBy",
        "language", "rdf:type",
    };

    /// <summary>
    /// State digest /2's rule profile: what index 9 does to make a state row and what its digest covers. Named by rule set
    /// <c>lu-release-1</c>, so inputs written before index 9 existed refuse rather than run under another state rule.
    /// </summary>
    private const string StateRuleProfile =
        "lex-v3-luxembourg-expression-state/2\n" +
        "rows=every-catalogue-state-of-a-served-work;served=at-least-one-held-row;held-or-textless\n" +
        "catalogue-state=subject-spelling-act-jo,consolidation-consolide-yyyymmdd,code-name-yyyymmdd\n" +
        "work=state-iri-less-jo,consolide-yyyymmdd,code-yyyymmdd;refused-when-isMemberOf-names-another-work-or-a-typed-code-work-is-not-ComplexWork\n" +
        "key=work-key-of-the-work-iri;codes-keyed-by-the-code\n" +
        "expression=the-selected-expression-when-held;else-each-french-catalogue-expression;else-empty-with-empty-language\n" +
        "language=the-selected-wemi-language;held-language-must-equal-the-catalogue-expression-language-when-stated\n" +
        "date=consolidation-and-code:catalogue-dateApplicability-one-exact;act:file-dateApplicability-else-catalogue-dateEntryInForce-one-exact;undated-states-get-no-row\n" +
        "date-role=applicability,applicability_in_file,entry_into_force;the-role-names-the-source\n" +
        "entry-into-force=catalogue-dateEntryInForce-one-exact;acts-only;kept-beside-the-date-whatever-its-role\n" +
        "end=catalogue-dateEndApplicability-one-exact;consolidation-and-code-only;half-open\n" +
        "publication-date=catalogue-publicationDate-one-exact\n" +
        "text-status=held,withheld_rights,not_available_pdf,not_available_derivation,not_available_misfiled,not_acquired\n" +
        "translation=language-not-french;takes-the-french-state-of-its-legal-resource\n" +
        "digest=prefix,publisher,work-key,date,role,kind,expression,work,legal-resource,language,publisher-end,publication-date,text-status,reason,in-file-identity,in-file-date,entry-into-force,translation-of,profiles,articles;length-prefixed-utf8\n" +
        "not-in-digest=derived-end,boundary,provisional\n";

    private static readonly string StateRuleDigest = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(StateRuleProfile)));

    /// <summary>State digest /2's rule digest, which rule set <c>lu-release-1</c> names.</summary>
    public static string StateRuleProfileSha256 => StateRuleDigest;

    /// <summary>State digest /2's rule profile, as bytes.</summary>
    public static byte[] CopyStateRuleProfileBytes() => Encoding.UTF8.GetBytes(StateRuleProfile);

    /// <summary>
    /// One held body as the catalogue projection reads it: its member and selected WEMI, what the inventory made of it and
    /// what its own file says (the /4 evidence, or the /3 coordinate's date), and the legal-content outcomes recorded for it.
    /// </summary>
    internal sealed record CatalogueBody(
        string ObjectRefSha256,
        string RootIri,
        string ExpressionIri,
        string Language,
        string MemberOutcome,
        string? RightsDisposition,
        string InventoryDisposition,
        string? InventoryDetail,
        string? InventoryRuleProfileSha256,
        string? InFileIdentity,
        string? InFileDate,
        IReadOnlyList<string> ConsistencyFindings,
        IReadOnlyList<KeyValuePair<string, long>> ContentDispositions);

    /// <summary>One corpus record of the Luxembourg population: its publisher URI (a state's legal resource), member and body axis.</summary>
    internal sealed record CatalogueRecord(string PublisherUri, string ObjectRefSha256, string MemberOutcome, string BodyDisposition);

    /// <summary>
    /// One interval transition of a work's French chain (<c>intervals</c>): from a state to the next state of a later
    /// date (its kind the publisher's end against that start), or to the next state of the same date (<c>same_date</c>).
    /// A hole or an overlap names its span: a hole from the end to the next start, an overlap from the next start to the end.
    /// </summary>
    internal sealed record IntervalRow(
        string WorkKey,
        int Ordinal,
        string Kind,
        string FromStateSha256,
        string ToStateSha256,
        string? SpanFrom,
        string? SpanTo);

    /// <summary>The catalogue facts a state row reads, by subject and predicate, distinct values in ordinal order.</summary>
    internal sealed class CatalogueFacts
    {
        private readonly Dictionary<string, Dictionary<string, SortedSet<string>>> _facts;

        private CatalogueFacts(Dictionary<string, Dictionary<string, SortedSet<string>>> facts) => _facts = facts;

        internal static CatalogueFacts From(IEnumerable<WorkFactRow> rows)
        {
            var facts = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!StatePredicates.Contains(row.Predicate))
                {
                    continue;
                }

                if (!facts.TryGetValue(row.SubjectIri, out var byPredicate))
                {
                    byPredicate = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                    facts.Add(row.SubjectIri, byPredicate);
                }

                if (!byPredicate.TryGetValue(row.Predicate, out var values))
                {
                    values = new SortedSet<string>(StringComparer.Ordinal);
                    byPredicate.Add(row.Predicate, values);
                }

                values.Add(row.ObjectValue);
            }

            return new CatalogueFacts(facts);
        }

        internal IEnumerable<string> Subjects => _facts.Keys;

        internal IReadOnlyCollection<string> Values(string subject, string predicate) =>
            _facts.TryGetValue(subject, out var byPredicate) && byPredicate.TryGetValue(predicate, out var values)
                ? values
                : Array.Empty<string>();

        /// <summary>The one exact civil date the subject states for the predicate, or null (none, several, or not a date).</summary>
        internal string? OneDate(string subject, string predicate) =>
            Values(subject, predicate) is { Count: 1 } values && IsExactDate(values.First()) ? values.First() : null;
    }

    /// <summary>
    /// The states of index 9 and their interval transitions. Held rows come from the admitted articles, one per held body
    /// (its selected expression); then every catalogue state of each work that has a held row gets a row of its own if it
    /// has none, one per French catalogue expression (or one with an empty expression and language when the catalogue
    /// names none), its text status saying why no text is held. Rows are dated by role and undated states get none; the
    /// publisher's ends, boundary kinds, derived ends and translations are completed over each work's French chain.
    /// </summary>
    internal static (StateRow[] States, IntervalRow[] Intervals) ProjectCatalogueStates(
        IReadOnlyList<ArticleRow> articles,
        CatalogueFacts facts,
        IReadOnlyList<CatalogueBody> bodies,
        IReadOnlyList<CatalogueRecord> records,
        string builtAtDate)
    {
        var bodyByObject = bodies.ToDictionary(static body => body.ObjectRefSha256, StringComparer.Ordinal);
        var bodiesByRoot = bodies
            .GroupBy(static body => body.RootIri, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(static body => body.ObjectRefSha256, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var recordByUri = new Dictionary<string, CatalogueRecord>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            // A legal resource is one record; a second would leave its text status to chance.
            if (!recordByUri.TryAdd(record.PublisherUri, record))
            {
                throw new InvalidDataException($"The Luxembourg corpus holds two records of {record.PublisherUri}.");
            }
        }

        // ---- Held rows: one per held body, its selected expression's admitted articles. ----
        var held = new List<PendingState>();
        foreach (var group in articles.GroupBy(static article => article.ObjectRefSha256, StringComparer.Ordinal))
        {
            if (!bodyByObject.TryGetValue(group.Key, out var body))
            {
                throw new InvalidDataException($"The admitted articles of {group.Key} come from no held body.");
            }

            var expressions = group.Select(static article => article.ExpressionIri).Distinct(StringComparer.Ordinal).ToArray();
            var languages = group.Select(static article => article.Language).Distinct(StringComparer.Ordinal).ToArray();
            if (expressions.Length != 1 || languages.Length != 1 ||
                !string.Equals(expressions[0], body.ExpressionIri, StringComparison.Ordinal) ||
                !string.Equals(languages[0], body.Language, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A Luxembourg expression-state source does not match its admitted articles.");
            }

            var kind = StateKindOf(body.RootIri);
            if (kind is null)
            {
                continue;
            }

            RequireCatalogueLanguage(facts, body.ExpressionIri, body.Language);
            var dated = DateOf(facts, body.RootIri, kind, body.InFileDate);
            if (dated is null)
            {
                continue;
            }

            var profiles = group.Select(static article => article.RuleProfileSha256)
                .Concat(body.InventoryRuleProfileSha256 is { } inventoryProfile ? [inventoryProfile] : [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var identities = group.Select(static article => article.ArticleIdentitySha256).Order(StringComparer.Ordinal).ToArray();
            held.Add(new PendingState(
                body.RootIri, kind, dated.Value.Date, dated.Value.Role, body.ExpressionIri, body.Language,
                TextHeld, null, body.InFileIdentity, body.InFileDate, profiles, identities));
        }

        // ---- Textless rows: the other catalogue states of every served work. ----
        var heldWorks = held.Select(static state => WorkOfState(state.LegalResourceIri)).ToHashSet(StringComparer.Ordinal);
        var heldKeys = held.Select(static state => (state.LegalResourceIri, state.ExpressionIri)).ToHashSet();
        var heldRoots = held.Select(static state => state.LegalResourceIri).ToHashSet(StringComparer.Ordinal);
        var catalogueStatesByWork = facts.Subjects
            .Where(static subject => StateKindOf(subject) is not null)
            .GroupBy(WorkOfState, StringComparer.Ordinal)
            .Where(group => heldWorks.Contains(group.Key))
            .ToDictionary(static group => group.Key, static group => group.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var textless = new List<PendingState>();
        foreach (var work in heldWorks.Order(StringComparer.Ordinal))
        {
            foreach (var root in catalogueStatesByWork.GetValueOrDefault(work) ?? [])
            {
                var kind = StateKindOf(root)!;
                var rootBodies = bodiesByRoot.GetValueOrDefault(root) ?? [];
                var frenchExpressions = facts.Values(root, "isRealizedBy")
                    .Where(expression => facts.Values(expression, "language").Contains(FrenchLanguageIri, StringComparer.Ordinal))
                    .ToArray();
                var anyExpression = facts.Values(root, "isRealizedBy").Count != 0;
                if (frenchExpressions.Length == 0 && anyExpression)
                {
                    // Only translations are catalogued: nothing of the French chain to stand for; a held translation has its own row.
                    continue;
                }

                foreach (var expression in frenchExpressions.Length == 0 ? [""] : frenchExpressions)
                {
                    // A held state is that state's row: a catalogue that names no expression of it adds no textless row beside it.
                    if (heldKeys.Contains((root, expression)) || (expression.Length == 0 && heldRoots.Contains(root)))
                    {
                        continue;
                    }

                    // What a file says of itself is that file's: a textless row takes the in-file facts of a body of its own
                    // expression only, never another expression's file (review of #980), and is dated with them.
                    var evidence = expression.Length == 0
                        ? null
                        : rootBodies.FirstOrDefault(body =>
                            string.Equals(body.ExpressionIri, expression, StringComparison.Ordinal) &&
                            (body.InFileIdentity is not null || body.InFileDate is not null));
                    var dated = DateOf(facts, root, kind, evidence?.InFileDate);
                    if (dated is null)
                    {
                        continue;
                    }

                    var (status, reason) = expression.Length == 0
                        ? (TextNotAcquired, "the catalogue names no expression of this state" + RecordReason(recordByUri.GetValueOrDefault(root)))
                        : TextStatusOf(expression, rootBodies, recordByUri.GetValueOrDefault(root));
                    textless.Add(new PendingState(
                        root, kind, dated.Value.Date, dated.Value.Role, expression, expression.Length == 0 ? "" : FrenchLanguage,
                        status, reason, evidence?.InFileIdentity, evidence?.InFileDate, [], []));
                }
            }
        }

        // ---- The publisher's facts each row carries, then its digest; then the chain's derived fields. ----
        var pending = held.Concat(textless).ToArray();
        var frenchByRoot = pending
            .Where(static state => string.Equals(state.Language, FrenchLanguage, StringComparison.Ordinal))
            .GroupBy(static state => state.LegalResourceIri, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var rows = new List<StateRow>(pending.Length);
        foreach (var state in pending)
        {
            var (date, role) = (state.Date, state.Role);
            string? translationOf = null;
            if (IsTranslationLanguage(state.Language) &&
                frenchByRoot.GetValueOrDefault(state.LegalResourceIri) is [var french])
            {
                // A translation takes the interval of the French state it translates: its date and role are that state's.
                translationOf = french.ExpressionIri;
                (date, role) = (french.Date, french.Role);
            }

            var work = WorkOfState(state.LegalResourceIri);
            var workKey = WorkKeyOf(work);
            var publisherEnd = state.Kind == KindAsPublished ? null : facts.OneDate(state.LegalResourceIri, "dateEndApplicability");
            // An act's catalogue entry into force, beside its date whatever that date's role: a file's applicability is not
            // its entry into force (five of derive-c's acts differ), and the two are never spoken as one.
            var entryIntoForce = state.Kind == KindAsPublished ? facts.OneDate(state.LegalResourceIri, "dateEntryInForce") : null;
            var publicationDate = facts.OneDate(state.LegalResourceIri, "publicationDate");
            var row = new StateRow(
                workKey, date, "", state.ExpressionIri, work, state.LegalResourceIri, state.Language,
                JsonSerializer.Serialize(state.Profiles), JsonSerializer.Serialize(state.Identities),
                state.Kind, role, publisherEnd, publisherEnd is null ? null : SourcePublisher, BoundaryDerived, publicationDate,
                state.TextStatus, state.TextStatusReason, state.InFileIdentity, state.InFileDate, translationOf,
                string.CompareOrdinal(date, builtAtDate) > 0, entryIntoForce);
            rows.Add(row with { StateSha256 = StateSha256Two(row) });
        }

        RequireOneRowPerKey(rows);
        RequireWorks(facts, rows);
        return CompleteIntervals(rows);
    }

    /// <summary>
    /// The chain fields of index 9's rows and the interval transitions between them, from the rows' own facts: over each
    /// work's French chain (French rows and rows naming no expression) in date order, a row's end is the publisher's when
    /// stated, else the next later date's start (derived), and its boundary says how its end meets the next start; a
    /// translation takes its French state's end and boundary. The reader recomputes exactly this from the rows it holds.
    /// </summary>
    internal static (StateRow[] States, IntervalRow[] Intervals) CompleteIntervals(IEnumerable<StateRow> source)
    {
        var rows = source.Select(static row => row with
        {
            ApplicableTo = row.PublisherEnd,
            ApplicableToSource = row.PublisherEnd is null ? null : SourcePublisher,
            Boundary = BoundaryDerived,
        }).ToList();
        var completed = new Dictionary<string, StateRow>(StringComparer.Ordinal);
        var intervals = new List<IntervalRow>();
        foreach (var work in rows.GroupBy(static row => row.WorkKey, StringComparer.Ordinal).OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var chain = work.Where(static row => !IsTranslationLanguage(row.Language)).OrderBy(static row => row, ChainOrder).ToArray();
            var dates = chain.Select(static row => row.ApplicabilityDate).Distinct(StringComparer.Ordinal).ToArray();
            var ordinal = 0;
            for (var index = 0; index < chain.Length; index++)
            {
                var row = chain[index];
                var dateIndex = Array.IndexOf(dates, row.ApplicabilityDate);
                var next = dateIndex + 1 < dates.Length ? dates[dateIndex + 1] : null;
                var end = row.PublisherEnd;
                var boundary = end is null ? BoundaryDerived
                    : string.CompareOrdinal(end, row.ApplicabilityDate) <= 0 ? BoundaryInvalid
                    : next is not null && string.Equals(end, DayBefore(next), StringComparison.Ordinal) ? BoundaryInclusiveSuspected
                    : BoundaryHalfOpen;
                completed[row.StateSha256] = row with
                {
                    ApplicableTo = end ?? next,
                    ApplicableToSource = end is not null ? SourcePublisher : next is not null ? SourceDerived : null,
                    Boundary = boundary,
                };

                if (index + 1 < chain.Length && string.Equals(chain[index + 1].ApplicabilityDate, row.ApplicabilityDate, StringComparison.Ordinal))
                {
                    intervals.Add(new IntervalRow(work.Key, ordinal++, IntervalSameDate, row.StateSha256, chain[index + 1].StateSha256, null, null));
                }

                if (next is not null)
                {
                    var to = chain.First(candidate => string.Equals(candidate.ApplicabilityDate, next, StringComparison.Ordinal));
                    var (kind, spanFrom, spanTo) = end is null ? (IntervalDerived, null, null)
                        : string.CompareOrdinal(end, row.ApplicabilityDate) <= 0 ? (IntervalInvalid, (string?)null, (string?)null)
                        : string.Equals(end, next, StringComparison.Ordinal) ? (IntervalContiguous, null, null)
                        : string.Equals(end, DayBefore(next), StringComparison.Ordinal) ? (IntervalInclusiveSuspected, null, null)
                        : string.CompareOrdinal(end, next) < 0 ? (IntervalHole, end, next)
                        : (IntervalOverlap, next, end);
                    intervals.Add(new IntervalRow(work.Key, ordinal++, kind, row.StateSha256, to.StateSha256, spanFrom, spanTo));
                }
            }
        }

        var result = new List<StateRow>(rows.Count);
        foreach (var row in rows)
        {
            if (completed.TryGetValue(row.StateSha256, out var chained))
            {
                result.Add(chained);
                continue;
            }

            // A translation: its French state's end and boundary, when that state is held as a row; else its own.
            var french = row.TranslationOf is null
                ? null
                : rows.FirstOrDefault(candidate =>
                    string.Equals(candidate.PublisherLegalResourceIri, row.PublisherLegalResourceIri, StringComparison.Ordinal) &&
                    string.Equals(candidate.ExpressionIri, row.TranslationOf, StringComparison.Ordinal) &&
                    string.Equals(candidate.Language, FrenchLanguage, StringComparison.Ordinal));
            result.Add(french is not null && completed.TryGetValue(french.StateSha256, out var frenchRow)
                ? row with { ApplicableTo = frenchRow.ApplicableTo, ApplicableToSource = frenchRow.ApplicableToSource, Boundary = frenchRow.Boundary }
                : row with { Boundary = row.PublisherEnd is { } end && string.CompareOrdinal(end, row.ApplicabilityDate) <= 0 ? BoundaryInvalid : row.PublisherEnd is null ? BoundaryDerived : BoundaryHalfOpen });
        }

        return (
            result.OrderBy(static row => row.WorkKey, StringComparer.Ordinal)
                .ThenBy(static row => row.ApplicabilityDate, StringComparer.Ordinal)
                .ThenBy(static row => row.ExpressionIri, StringComparer.Ordinal)
                .ThenBy(static row => row.Language, StringComparer.Ordinal)
                .ToArray(),
            intervals.ToArray());
    }

    /// <summary>The chain's order: date, then kind (the act before its consolidation, then a code state), legal resource, expression.</summary>
    private static readonly Comparer<StateRow> ChainOrder = Comparer<StateRow>.Create(static (left, right) =>
    {
        var compared = string.CompareOrdinal(left.ApplicabilityDate, right.ApplicabilityDate);
        if (compared != 0) return compared;
        compared = KindRank(left.StateKind).CompareTo(KindRank(right.StateKind));
        if (compared != 0) return compared;
        compared = string.CompareOrdinal(left.PublisherLegalResourceIri, right.PublisherLegalResourceIri);
        return compared != 0 ? compared : string.CompareOrdinal(left.ExpressionIri, right.ExpressionIri);
    });

    private static int KindRank(string? kind) => kind switch
    {
        KindAsPublished => 0,
        KindConsolidation => 1,
        _ => 2,
    };

    /// <summary>A language whose state is a translation: any but French, and not the empty language of a state naming no expression.</summary>
    internal static bool IsTranslationLanguage(string language) =>
        language.Length != 0 && !string.Equals(language, FrenchLanguage, StringComparison.Ordinal);

    /// <summary>The state kind an ELI state IRI spells (<see cref="LuxembourgEliState.KindOf"/>), or null when it spells none.</summary>
    internal static string? StateKindOf(string iri) => LuxembourgEliState.KindOf(iri) switch
    {
        "act" => KindAsPublished,
        "consolidation" => KindConsolidation,
        "code" => KindCode,
        _ => null,
    };

    /// <summary>A state's work: its IRI less <c>/jo</c>, <c>/consolide/yyyymmdd</c> or a code's <c>/yyyymmdd</c>.</summary>
    internal static string WorkOfState(string stateIri) =>
        LuxembourgEliState.WorkOf(stateIri) ?? throw new InvalidDataException($"{stateIri} spells no Luxembourg state.");

    /// <summary>
    /// A state's date and its role: a consolidation's or code state's catalogue <c>dateApplicability</c>; an act's own file
    /// date where its file states one, else its catalogue <c>dateEntryInForce</c>. Null when none is one exact date.
    /// </summary>
    private static (string Date, string Role)? DateOf(CatalogueFacts facts, string root, string kind, string? inFileDate)
    {
        if (kind != KindAsPublished)
        {
            return facts.OneDate(root, "dateApplicability") is { } applicability ? (applicability, RoleApplicability) : null;
        }

        if (inFileDate is not null && IsExactDate(inFileDate))
        {
            return (inFileDate, RoleApplicabilityInFile);
        }

        return facts.OneDate(root, "dateEntryInForce") is { } entry ? (entry, RoleEntryIntoForce) : null;
    }

    /// <summary>
    /// Why a catalogue expression has no text: what became of the state's held body (its rights first, then its file and its
    /// derivation), or its record, or that the corpus holds none.
    /// </summary>
    private static (string Status, string Reason) TextStatusOf(string expression, IReadOnlyList<CatalogueBody> rootBodies, CatalogueRecord? record)
    {
        var body = rootBodies.FirstOrDefault(candidate => string.Equals(candidate.ExpressionIri, expression, StringComparison.Ordinal));
        if (body is null)
        {
            return rootBodies.Count != 0
                ? (TextNotAcquired, "another expression of this state was acquired: " + string.Join(",", rootBodies.Select(static other => other.ExpressionIri)))
                : (TextNotAcquired, record is null ? "not_a_corpus_member" : "not acquired" + RecordReason(record));
        }

        var misfiled = body.ConsistencyFindings.Count == 0 ? null : "content consistency: " + string.Join(",", body.ConsistencyFindings);
        if (!string.Equals(body.MemberOutcome, "acquired", StringComparison.Ordinal))
        {
            return (TextWithheldRights, (body.RightsDisposition ?? body.MemberOutcome) + (misfiled is null ? "" : "; " + misfiled));
        }

        return body.InventoryDisposition switch
        {
            "not_akn" => (TextNotAvailablePdf, "not_akn: " + body.InventoryDetail),
            "publisher_file_names_other_state" => (TextNotAvailableMisfiled, misfiled!),
            "inventoried" => (TextNotAvailableDerivation, "no admitted article: " + string.Join(",",
                body.ContentDispositions.Select(static pair => $"{pair.Key}={pair.Value.ToString(CultureInfo.InvariantCulture)}"))),
            _ => (TextNotAvailableDerivation, body.InventoryDisposition + (body.InventoryDetail is null ? "" : ": " + body.InventoryDetail)),
        };
    }

    private static string RecordReason(CatalogueRecord? record) =>
        record is null ? "; not a corpus member" : $"; member {record.MemberOutcome}, body {record.BodyDisposition}";

    /// <summary>A held expression's language is the selected WEMI's, and the catalogue, where it states one, says the same.</summary>
    private static void RequireCatalogueLanguage(CatalogueFacts facts, string expression, string language)
    {
        var stated = facts.Values(expression, "language");
        if (stated.Count != 0 && !stated.Any(iri => string.Equals(LanguageTokenOf(iri), language, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"The held expression {expression} is {language}; the catalogue says {string.Join(",", stated)}.");
        }
    }

    private static string? LanguageTokenOf(string iri)
    {
        var token = iri[(iri.LastIndexOf('/') + 1)..].ToLowerInvariant();
        return token.Length == 3 && token.All(static value => value is >= 'a' and <= 'z') ? token : null;
    }

    /// <summary>
    /// The work check (design §3.1): an act or consolidation the catalogue names a membership of is a member of exactly its
    /// IRI's work, and a code state's work the catalogue types is a <c>jolux:ComplexWork</c>; a contradiction refuses.
    /// </summary>
    private static void RequireWorks(CatalogueFacts facts, IEnumerable<StateRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.StateKind == KindCode)
            {
                var types = facts.Values(row.PublisherWorkIri, "rdf:type");
                if (types.Count != 0 && !types.Contains(ComplexWorkIri, StringComparer.Ordinal))
                {
                    throw new InvalidDataException($"The code state {row.PublisherLegalResourceIri}'s work {row.PublisherWorkIri} is not a catalogue ComplexWork.");
                }

                continue;
            }

            var members = facts.Values(row.PublisherLegalResourceIri, "isMemberOf");
            if (members.Count != 0 && (members.Count != 1 || !members.Contains(row.PublisherWorkIri, StringComparer.Ordinal)))
            {
                throw new InvalidDataException(
                    $"The catalogue names {row.PublisherLegalResourceIri} a member of {string.Join(",", members)}, not of its work {row.PublisherWorkIri}.");
            }
        }
    }

    private static void RequireOneRowPerKey(IEnumerable<StateRow> rows)
    {
        var seen = new HashSet<(string, string, string, string)>();
        foreach (var row in rows)
        {
            if (!seen.Add((row.WorkKey, row.ApplicabilityDate, row.ExpressionIri, row.Language)))
            {
                throw new InvalidDataException(
                    $"Two Luxembourg states of {row.WorkKey} on {row.ApplicabilityDate} share the expression '{row.ExpressionIri}' ({row.PublisherLegalResourceIri}).");
            }
        }
    }

    internal static bool IsExactDate(string? value) =>
        value is not null &&
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static string DayBefore(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// State digest /2: the prefix <c>lex-v3-luxembourg-expression-state/2</c> and the row's own facts, each length-prefixed
    /// UTF-8, the lists counted first. The derived end, the boundary and <c>provisional</c> are not in it: they move with the
    /// other states of the work and the build's time, never with the state, so a chained build that adds a state leaves
    /// the digest of an unchanged one as it was.
    /// </summary>
    internal static string StateSha256Two(StateRow row)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        static void Append(IncrementalHash target, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            target.AppendData(length);
            target.AppendData(bytes);
        }

        var profiles = JsonSerializer.Deserialize<string[]>(row.RuleProfilesJson) ?? throw new InvalidDataException("A state has no rule-profile population.");
        var identities = JsonSerializer.Deserialize<string[]>(row.ArticleIdentitiesJson) ?? throw new InvalidDataException("A state has no article-identity population.");
        Append(hash, "lex-v3-luxembourg-expression-state/2");
        Append(hash, "lu-legilux");
        Append(hash, row.WorkKey);
        Append(hash, row.ApplicabilityDate);
        Append(hash, row.DateRole ?? "");
        Append(hash, row.StateKind ?? "");
        Append(hash, row.ExpressionIri);
        Append(hash, row.PublisherWorkIri);
        Append(hash, row.PublisherLegalResourceIri);
        Append(hash, row.Language);
        Append(hash, row.PublisherEnd ?? "");
        Append(hash, row.PublicationDate ?? "");
        Append(hash, row.TextStatus ?? "");
        Append(hash, row.TextStatusReason ?? "");
        Append(hash, row.InFileIdentity ?? "");
        Append(hash, row.InFileDate ?? "");
        Append(hash, row.EntryIntoForce ?? "");
        Append(hash, row.TranslationOf ?? "");
        Append(hash, profiles.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var profile in profiles) Append(hash, profile);
        Append(hash, identities.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var identity in identities) Append(hash, identity);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>A state row before its digest and chain fields: its legal resource, kind, date and role, expression and text.</summary>
    private sealed record PendingState(
        string LegalResourceIri,
        string Kind,
        string Date,
        string Role,
        string ExpressionIri,
        string Language,
        string TextStatus,
        string? TextStatusReason,
        string? InFileIdentity,
        string? InFileDate,
        string[] Profiles,
        string[] Identities);
}

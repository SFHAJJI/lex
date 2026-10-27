using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest;

/// <summary>
/// The text yield per class (the specification of record's R86(4)): per publisher and per class of act, how many versions
/// the catalogue lists and how many have text served, the acts and articles with served text, and what became of every
/// publisher file the mount holds: text served, text withheld (by the rights disposition, verbatim), a PDF with no text
/// (where the corpus records the file's format: Luxembourg), or an admitted file from which no article is served (R86(2)'s
/// failure, by the derivation's own dispositions, verbatim; for the EU, whose corpus records no file format, a PDF or HTML
/// file with no Formex main body among them, by its own disposition, such as <c>formex_main_body_not_eligible</c>).
/// Written into the build report by <see cref="V3CorpusMountWriter"/> and served by the coverage answer, so Coverage and
/// R26's sentence say the same figures.
/// </summary>
/// <remarks>
/// <para>
/// Computed from what the derive produces and nothing else (<see cref="FromMount"/>): the corpus's members, the
/// Luxembourg index's articles and catalogue facts (<c>work_facts</c>), and the EU index's articles and states. Every count
/// is a pure function of those rows, ordered by ordinal comparison, so a second derive writes the same bytes.
/// </para>
/// <para>
/// A reason is the token the data carries, never one mapped onto a closed list here: a withheld file is counted under its
/// rights disposition's wire name and a failed one under its legal-content dispositions' wire names, so a disposition a
/// later rule set adds is counted under its own name with no change here. The only tokens this class mints are the
/// catalogue's reasons for a version of which no file is held (<see cref="NotHeldNoExpression"/> and its siblings), which
/// say what the catalogue states of it, not why the acquisition did not fetch it.
/// </para>
/// </remarks>
public sealed record V3TextYield(string Schema, IReadOnlyList<V3TextYieldPublisher> Publishers)
{
    /// <summary>The block's schema; a change to its shape bumps it.</summary>
    public const string SchemaId = "lex-v3-text-yield/1";

    /// <summary>The Luxembourg classes' family for a state IRI that spells no family the publisher's ELI names.</summary>
    public const string UnrecognisedFamily = "unrecognised";

    /// <summary>The family of every EU class: the EU's classes are its kinds (original or consolidated wording).</summary>
    public const string EuropeFamily = "eu";

    /// <summary>A catalogue state whose catalogue names no expression of it.</summary>
    public const string NotHeldNoExpression = "catalogue_no_expression";

    /// <summary>A catalogue state whose catalogue names expressions in other languages than French only.</summary>
    public const string NotHeldTranslationsOnly = "catalogue_translations_only";

    /// <summary>A catalogue state whose French expression has no XML or PDF manifestation in the catalogue.</summary>
    public const string NotHeldNoReadableManifestation = "catalogue_no_xml_or_pdf_manifestation";

    /// <summary>A catalogue state whose every XML and PDF manifestation the catalogue licenses licenceSCL.</summary>
    public const string NotHeldLicenceScl = "catalogue_licence_scl";

    /// <summary>A catalogue state whose XML and PDF manifestations the catalogue licenses licenceSCL in part, and otherwise in part.</summary>
    public const string NotHeldLicenceSclInPart = "catalogue_licence_scl_in_part";

    /// <summary>A catalogue state whose every XML and PDF manifestation the catalogue licenses CC BY, of which no file is held.</summary>
    public const string NotHeldCcByNotAcquired = "catalogue_cc_by_not_acquired";

    /// <summary>A catalogue state whose XML and PDF manifestations carry no licence in the catalogue, or another licence.</summary>
    public const string NotHeldOtherLicence = "catalogue_no_or_other_licence";

    /// <summary>The reason of a failed file whose corpus member records no legal-content outcome at all.</summary>
    public const string NoLegalContentOutcome = "no_legal_content_outcome";

    internal const string LicenceScl = "http://data.legilux.public.lu/resource/authority/license/licenceSCL";
    internal const string LicenceCcBy = "http://creativecommons.org/licenses/by/4.0/";
    private const string FrenchLanguageIri = "http://publications.europa.eu/resource/authority/language/FRA";
    private const string EliPrefix = "http://data.legilux.public.lu/eli/etat/leg/";
    private const string UserFormatPrefix = "http://data.legilux.public.lu/resource/authority/user-format/";

    /// <summary>The catalogue predicates the not-held reasons read, by their wire names in <c>work_facts</c>.</summary>
    private static readonly string[] CataloguePredicates = ["isRealizedBy", "isEmbodiedBy", "language", "license", "userFormat"];

    /// <summary>How the block is written in the build report and in the coverage answer: snake_case, nulls kept.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
    };

    /// <summary>
    /// Reads a block as the build report holds it (<see cref="JsonOptions"/>) and holds it to its own arithmetic
    /// (<see cref="Validate"/>); anything else is <see cref="InvalidDataException"/>.
    /// </summary>
    public static V3TextYield Read(JsonElement block)
    {
        V3TextYield? read;
        try
        {
            read = block.Deserialize<V3TextYield>(JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("The text yield is not one lex-v3-text-yield/1 block.", exception);
        }

        return (read ?? throw new InvalidDataException("The text yield is null.")).Validate();
    }

    /// <summary>
    /// The block held to its arithmetic, so a reader can trust each relation it shows: within a class, each held file is in
    /// exactly one bucket, a part is never more than its whole, and the versions not held are among those without text; a
    /// publisher's versions, files and articles are its classes' sums, and its acts, which are distinct, are no more than the
    /// sum of its classes' and no fewer than the largest; every list is keyed once, by a reason the data carries.
    /// </summary>
    public V3TextYield Validate()
    {
        if (!string.Equals(Schema, SchemaId, StringComparison.Ordinal) || Publishers is null)
        {
            throw new InvalidDataException($"The text yield's schema is {Schema}, not {SchemaId}.");
        }

        RequireDistinct(Publishers.Select(static publisher => publisher.Publisher), "publishers");
        foreach (var publisher in Publishers)
        {
            if (publisher.Publisher is not ("lu-legilux" or "eu-eurlex") || publisher.Classes is null)
            {
                throw new InvalidDataException($"The text yield names a publisher it does not know: {publisher.Publisher}.");
            }

            RequireDistinct(publisher.Classes.Select(static row => row.Family + "/" + row.Kind), publisher.Publisher + " classes");
            foreach (var row in publisher.Classes)
            {
                ValidateClass(publisher.Publisher, row);
            }

            var classes = publisher.Classes;
            Require(publisher.Versions == classes.Sum(static row => row.Versions), publisher.Publisher, "versions are its classes' sum");
            Require(publisher.VersionsWithText == classes.Sum(static row => row.VersionsWithText), publisher.Publisher, "versions with text are its classes' sum");
            Require(publisher.Articles == classes.Sum(static row => row.Articles), publisher.Publisher, "articles are its classes' sum");
            Require(publisher.Files == classes.Sum(static row => row.Files), publisher.Publisher, "files are its classes' sum");
            Require(publisher.FilesWithText == classes.Sum(static row => row.FilesWithText), publisher.Publisher, "files with text are its classes' sum");
            Require(publisher.Works <= classes.Sum(static row => row.Works) && publisher.Works >= classes.Select(static row => row.Works).DefaultIfEmpty(0).Max(),
                publisher.Publisher, "acts are distinct: no more than its classes' sum and no fewer than the largest class's");
            Require(publisher.WorksWithText <= classes.Sum(static row => row.WorksWithText) &&
                    publisher.WorksWithText >= classes.Select(static row => row.WorksWithText).DefaultIfEmpty(0).Max() &&
                    publisher.WorksWithText <= publisher.Works,
                publisher.Publisher, "acts with text are distinct, and among its acts");
        }

        return this;
    }

    private static void ValidateClass(string publisher, V3TextYieldClass row)
    {
        var where = $"{publisher} {row.Family}/{row.Kind}";
        if (string.IsNullOrWhiteSpace(row.Family) || string.IsNullOrWhiteSpace(row.Kind) ||
            row.FilesWithheld is null || row.FilesParseFailed is null || row.Annexes is { ByDisposition: null })
        {
            throw new InvalidDataException($"The text yield's {where} does not name its family and kind, or does not list its files.");
        }

        Require(new[] { row.Works, row.WorksWithText, row.Versions, row.VersionsWithText, row.Articles, row.Files, row.FilesWithText, row.FilesPdfOnly ?? 0 }
            .All(static count => count >= 0), where, "every count is a count");
        Require(row.WorksWithText <= row.Works, where, "acts with text are among its acts");
        Require(row.VersionsWithText <= row.Versions, where, "versions with text are among its versions");
        Require((row.VersionsWithText == 0) == (row.Articles == 0) && (row.FilesWithText == 0) == (row.Articles == 0),
            where, "a version or file has text exactly when an article is served");
        // An act with text has a file with text of its own; a version need not (an EU work the census lists under two seeds
        // gives its one file's text to both seeds' versions).
        Require(row.WorksWithText <= row.FilesWithText, where, "an act with text has a file with text");
        RequireCounts(row.FilesWithheld.Select(static withheld => (withheld.Reason, withheld.Files)), where + " withheld files");
        Require(row.FilesWithheld.All(static withheld => withheld.Pdf >= 0 && withheld.Pdf <= withheld.Files), where, "withheld PDFs are among the withheld files");
        RequireCounts(row.FilesParseFailed.Select(static failed => (failed.Reason, failed.Count)), where + " failed files");
        Require(row.Files == row.FilesWithText + row.FilesWithheld.Sum(static withheld => withheld.Files) + (row.FilesPdfOnly ?? 0) +
                row.FilesParseFailed.Sum(static failed => failed.Count),
            where, "each held file is in exactly one of with text, withheld, PDF only and failed");
        if (row.VersionsNotHeld is { } notHeld)
        {
            RequireCounts(notHeld.Select(static count => (count.Reason, count.Count)), where + " versions not held");
            Require(notHeld.Sum(static count => count.Count) <= row.Versions - row.VersionsWithText, where, "the versions not held are among those without text");
        }

        if (row.Annexes is { } annexes)
        {
            RequireCounts(annexes.ByDisposition.Select(static count => (count.Reason, count.Count)), where + " annexes");
            Require(annexes.WithText >= 0 && annexes.ImageOnly >= 0 && annexes.Failed >= 0 &&
                    annexes.Annexes == annexes.WithText + annexes.ImageOnly + annexes.Failed &&
                    annexes.Annexes == annexes.ByDisposition.Sum(static count => count.Count),
                where, "annexes are with text, image-only or failed, and their dispositions' sum");
        }
    }

    private static void RequireCounts(IEnumerable<(string Reason, long Count)> rows, string where)
    {
        var listed = rows.ToArray();
        RequireDistinct(listed.Select(static row => row.Reason), where);
        Require(listed.All(static row => !string.IsNullOrWhiteSpace(row.Reason) && row.Count > 0), where, "each row is a reason the data carries with a count of at least one");
    }

    private static void RequireDistinct(IEnumerable<string> keys, string where)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (key is null || !seen.Add(key))
            {
                throw new InvalidDataException($"The text yield lists {key ?? "null"} twice, or null, in its {where}.");
            }
        }
    }

    private static void Require(bool holds, string where, string rule)
    {
        if (!holds)
        {
            throw new InvalidDataException($"The text yield's {where} does not hold: {rule}.");
        }
    }

    /// <summary>
    /// The yield of a mount from its corpus members and its two index files, opened read-only: the members a mount keeps
    /// (<see cref="LexCorpus6MountView.Holds"/>: every EU member and every Luxembourg member with a body or rights evidence;
    /// any others given are ignored), the Luxembourg index's articles and catalogue facts, and the EU index's articles and
    /// states. Either index may be absent (null), and its publisher is then not reported.
    /// </summary>
    public static V3TextYield FromMount(IEnumerable<LexCorpus6Member> members, string? luxembourgIndexPath, string? europeIndexPath)
    {
        ArgumentNullException.ThrowIfNull(members);
        var kept = members.Where(LexCorpus6MountView.Holds).ToArray();
        return Count(
            kept,
            luxembourgIndexPath is null ? null : ReadLuxembourg(luxembourgIndexPath),
            europeIndexPath is null ? null : ReadEurope(europeIndexPath));
    }

    /// <summary>What the Luxembourg index gives the yield: the articles served per member, every catalogue subject and the facts the not-held reasons read.</summary>
    internal sealed record LuxembourgInputs(
        IReadOnlyDictionary<string, long> ArticlesByObject,
        IReadOnlyList<string> CatalogueSubjects,
        IReadOnlyList<(string Subject, string Predicate, string Value)> CatalogueFacts);

    /// <summary>One EU state as the yield reads it: its seed, its Cellar work, whether it is the original wording, and its consolidation date.</summary>
    internal sealed record EuropeState(string SeedCelex, string PublisherWorkIri, bool Original, string? ConsolidationDate);

    /// <summary>Two Luxembourg or EU classes in ordinal order: by family, then by kind.</summary>
    private static readonly Comparer<(string Family, string Kind)> ClassOrder = Comparer<(string Family, string Kind)>.Create(
        static (left, right) => string.CompareOrdinal(left.Family, right.Family) is var family and not 0
            ? family
            : string.CompareOrdinal(left.Kind, right.Kind));

    /// <summary>What the EU index gives the yield: the articles served per member and per Cellar work, and its states.</summary>
    internal sealed record EuropeInputs(
        IReadOnlyDictionary<string, long> ArticlesByObject,
        IReadOnlyDictionary<string, long> ArticlesByWork,
        IReadOnlyList<EuropeState> States);

    internal static LuxembourgInputs ReadLuxembourg(string indexPath)
    {
        using var connection = LuxembourgIndexBuilder.OpenForServing(indexPath);
        var articles = CountsBy(connection, "SELECT object_ref_sha256, COUNT(*) FROM articles GROUP BY object_ref_sha256");
        var subjects = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT DISTINCT subject_iri FROM work_facts";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var subject = reader.GetString(0);
                if (LuxembourgIndexBuilder.StateKindOf(subject) is not null)
                {
                    subjects.Add(subject);
                }
            }
        }

        var facts = new List<(string, string, string)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT subject_iri, predicate, object_value FROM work_facts WHERE predicate IN ("
                + string.Join(",", CataloguePredicates.Select(static (_, index) => "$p" + index)) + ")";
            for (var index = 0; index < CataloguePredicates.Length; index++)
            {
                command.Parameters.AddWithValue("$p" + index, CataloguePredicates[index]);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                facts.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        return new LuxembourgInputs(articles, subjects, facts);
    }

    internal static EuropeInputs ReadEurope(string indexPath)
    {
        using var connection = EuropeIndexBuilder.OpenForServing(indexPath);
        var byObject = CountsBy(connection, "SELECT object_ref_sha256, COUNT(*) FROM articles GROUP BY object_ref_sha256");
        var byWork = CountsBy(connection, "SELECT publisher_work_id, COUNT(*) FROM articles GROUP BY publisher_work_id");
        var states = new List<EuropeState>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT seed_celex, publisher_work_iri, date_status, publisher_consolidation_date FROM states ORDER BY seed_celex, publisher_work_iri";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                states.Add(new EuropeState(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2) == (long)EuropeIndexStateDateStatus.OriginalWording,
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        return new EuropeInputs(byObject, byWork, states);
    }

    private static Dictionary<string, long> CountsBy(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        while (reader.Read())
        {
            counts.Add(reader.GetString(0), reader.GetInt64(1));
        }

        return counts;
    }

    /// <summary>
    /// The yield from the members a mount keeps and what its indexes give; a pure function, so a test can run it over
    /// synthetic rows. Luxembourg first, then the EU; within a publisher, classes by family and kind, and reasons by their
    /// token, each in ordinal order.
    /// </summary>
    internal static V3TextYield Count(IReadOnlyList<LexCorpus6Member> members, LuxembourgInputs? luxembourg, EuropeInputs? europe)
    {
        ArgumentNullException.ThrowIfNull(members);
        var publishers = new List<V3TextYieldPublisher>(2);
        if (luxembourg is not null)
        {
            publishers.Add(CountLuxembourg(members.Where(static member => member.Publisher == PublisherId.LuLegilux).ToArray(), luxembourg));
        }

        if (europe is not null)
        {
            publishers.Add(CountEurope(members.Where(static member => member.Publisher == PublisherId.EuEurLex).ToArray(), europe));
        }

        return new V3TextYield(SchemaId, publishers).Validate();
    }

    /// <summary>The counting of one class, filled as rows are read and frozen into a <see cref="V3TextYieldClass"/>.</summary>
    private sealed class Tally(string family, string kind)
    {
        public string Family { get; } = family;
        public string Kind { get; } = kind;
        public HashSet<string> Works { get; } = new(StringComparer.Ordinal);
        public HashSet<string> WorksWithText { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Versions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> VersionsWithText { get; } = new(StringComparer.Ordinal);
        public long Articles { get; set; }
        public SortedDictionary<string, long>? NotHeld { get; init; }
        public long Files { get; set; }
        public long FilesWithText { get; set; }
        public SortedDictionary<string, (long Files, long Pdf)> Withheld { get; } = new(StringComparer.Ordinal);
        public long? PdfOnly { get; set; }
        public SortedDictionary<string, long> ParseFailed { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, long>? Annexes { get; init; }

        public V3TextYieldClass Freeze() => new(
            Family,
            Kind,
            Works.Count,
            WorksWithText.Count,
            Versions.Count,
            VersionsWithText.Count,
            Articles,
            NotHeld?.Select(static pair => new V3TextYieldCount(pair.Key, pair.Value)).ToArray(),
            Files,
            FilesWithText,
            Withheld.Select(static pair => new V3TextYieldWithheld(pair.Key, pair.Value.Files, pair.Value.Pdf)).ToArray(),
            PdfOnly,
            ParseFailed.Select(static pair => new V3TextYieldCount(pair.Key, pair.Value)).ToArray(),
            Annexes is null ? null : V3TextYieldAnnexes.Of(Annexes));
    }

    private static V3TextYieldPublisher CountLuxembourg(IReadOnlyList<LexCorpus6Member> members, LuxembourgInputs inputs)
    {
        var classes = new SortedDictionary<(string Family, string Kind), Tally>(ClassOrder);
        Tally ClassOf(string stateIri)
        {
            var key = (FamilyOf(stateIri), LuxembourgIndexBuilder.StateKindOf(stateIri) ?? UnrecognisedFamily);
            if (!classes.TryGetValue(key, out var tally))
            {
                tally = new Tally(key.Item1, key.Item2) { NotHeld = new SortedDictionary<string, long>(StringComparer.Ordinal), PdfOnly = 0 };
                classes.Add(key, tally);
            }

            return tally;
        }

        var heldRoots = new HashSet<string>(StringComparer.Ordinal);
        var counted = 0L;
        foreach (var member in members.Where(static member => member.LuxembourgRights is not null))
        {
            var wemi = member.LuxembourgRights!.SelectedWemi;
            var root = wemi.RootIri;
            var tally = ClassOf(root);
            var work = LuxembourgEliState.WorkOf(root) ?? root;
            heldRoots.Add(root);
            tally.Versions.Add(root);
            tally.Works.Add(work);
            tally.Files++;
            var served = inputs.ArticlesByObject.GetValueOrDefault(member.ObjectRefSha256);
            var pdf = IsPdfFormat(wemi.FormatIri);
            // One rule for a held file's bucket, which the API's record of an act the index holds no version of says too (R77).
            var held = HeldFile(member, served);
            if (held.Bucket == HeldFileWithText)
            {
                counted++;
                tally.FilesWithText++;
                tally.Articles += served;
                tally.VersionsWithText.Add(root);
                tally.WorksWithText.Add(work);
            }
            else
            {
                switch (held.Bucket)
                {
                    case HeldFileWithheld:
                        var (files, pdfFiles) = tally.Withheld.GetValueOrDefault(held.Reason!);
                        tally.Withheld[held.Reason!] = (files + 1, pdfFiles + (pdf ? 1 : 0));
                        break;
                    case HeldFilePdfOnly:
                        tally.PdfOnly++;
                        break;
                    default:
                        tally.ParseFailed[held.Reason!] = tally.ParseFailed.GetValueOrDefault(held.Reason!) + 1;
                        break;
                }
            }
        }

        if (counted != inputs.ArticlesByObject.Count(static pair => pair.Value > 0))
        {
            throw new InvalidDataException("The Luxembourg index serves articles of a member the corpus does not hold as a Luxembourg file.");
        }

        // Every catalogue state of a class is one of its versions; one of which no file is held is counted by what the
        // catalogue says of it.
        var facts = FactsBySubject(inputs.CatalogueFacts);
        foreach (var subject in inputs.CatalogueSubjects.Distinct(StringComparer.Ordinal)
                     .Where(static subject => LuxembourgIndexBuilder.StateKindOf(subject) is not null))
        {
            var tally = ClassOf(subject);
            tally.Versions.Add(subject);
            tally.Works.Add(LuxembourgEliState.WorkOf(subject) ?? subject);
            if (!heldRoots.Contains(subject))
            {
                var reason = NotHeldReason(facts, subject);
                tally.NotHeld![reason] = tally.NotHeld.GetValueOrDefault(reason) + 1;
            }
        }

        return Publisher(PublisherId.LuLegilux, classes.Values);
    }

    private static V3TextYieldPublisher CountEurope(IReadOnlyList<LexCorpus6Member> members, EuropeInputs inputs)
    {
        var classes = new SortedDictionary<(string Family, string Kind), Tally>(ClassOrder);
        Tally ClassOf(string kind)
        {
            if (!classes.TryGetValue((EuropeFamily, kind), out var tally))
            {
                tally = new Tally(EuropeFamily, kind) { Annexes = new SortedDictionary<string, long>(StringComparer.Ordinal) };
                classes.Add((EuropeFamily, kind), tally);
            }

            return tally;
        }

        // A Cellar work is one class's; one the census lists under several seeds is a work of each of their versions, so a
        // work with text gives text to every version it belongs to, whatever order the rows come in.
        var stateOfWork = new Dictionary<string, (Tally Tally, SortedSet<string> Versions)>(StringComparer.Ordinal);
        foreach (var state in inputs.States
                     .OrderBy(static state => state.SeedCelex, StringComparer.Ordinal)
                     .ThenBy(static state => state.PublisherWorkIri, StringComparer.Ordinal))
        {
            var kind = state.Original
                ? ContractWire.NameOf(Lex.V3.Contracts.Source.Europe.EuContentClass.OriginalLegalText)
                : ContractWire.NameOf(Lex.V3.Contracts.Source.Europe.EuContentClass.Consolidation);
            // A version is the publisher's: the original wording of a seed, or one consolidation date of it; a consolidated
            // work with no usable date is a version of its own.
            var version = state.Original ? state.SeedCelex
                : state.ConsolidationDate is { } date ? state.SeedCelex + "@" + date
                : state.SeedCelex + "#" + state.PublisherWorkIri;
            var tally = ClassOf(kind);
            tally.Works.Add(state.PublisherWorkIri);
            tally.Versions.Add(version);
            if (!stateOfWork.TryGetValue(state.PublisherWorkIri, out var listed))
            {
                listed = (tally, new SortedSet<string>(StringComparer.Ordinal));
                stateOfWork.Add(state.PublisherWorkIri, listed);
            }
            else if (listed.Tally != tally)
            {
                throw new InvalidDataException($"The EU index holds {state.PublisherWorkIri} as an original and as a consolidation.");
            }

            listed.Versions.Add(version);
        }

        foreach (var (work, articles) in inputs.ArticlesByWork.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (articles <= 0)
            {
                continue;
            }

            if (!stateOfWork.TryGetValue(work, out var held))
            {
                throw new InvalidDataException($"The EU index serves articles of {work}, which none of its states names.");
            }

            held.Tally.Articles += articles;
            held.Tally.WorksWithText.Add(work);
            held.Tally.VersionsWithText.UnionWith(held.Versions);
        }

        var counted = 0L;
        foreach (var member in members.Where(static member => member.Outcome == LexCorpus6OutcomeKind.Acquired && member.EuropeContentClass is not null))
        {
            var tally = ClassOf(ContractWire.NameOf(member.EuropeContentClass!.ContentClass));
            tally.Files++;
            var served = inputs.ArticlesByObject.GetValueOrDefault(member.ObjectRefSha256);
            if (served > 0)
            {
                counted += served;
                tally.FilesWithText++;
            }
            else
            {
                // The corpus records no format for an EU file: one with no Formex main body, a PDF or an HTML body among them,
                // is counted here by its own disposition (formex_main_body_not_eligible, for one), never as PDF only.
                var reason = DispositionsOf(member, LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody);
                tally.ParseFailed[reason] = tally.ParseFailed.GetValueOrDefault(reason) + 1;
            }

            foreach (var annex in member.Stage3Outcomes.Where(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody))
            {
                var disposition = ContractWire.NameOf(annex.Disposition);
                tally.Annexes![disposition] = tally.Annexes.GetValueOrDefault(disposition) + 1;
            }
        }

        if (counted != inputs.ArticlesByObject.Values.Sum() ||
            classes.Values.Sum(static tally => tally.Articles) != inputs.ArticlesByObject.Values.Sum())
        {
            throw new InvalidDataException("The EU index serves articles of a member the corpus does not hold as an acquired EU file, or of a work no state names.");
        }

        return Publisher(PublisherId.EuEurLex, classes.Values);
    }

    private static V3TextYieldPublisher Publisher(PublisherId publisher, IEnumerable<Tally> tallies)
    {
        var all = tallies.ToArray();
        var classes = all.Select(static tally => tally.Freeze()).ToArray();
        return new V3TextYieldPublisher(
            ContractWire.NameOf(publisher),
            all.SelectMany(static tally => tally.Works).Distinct(StringComparer.Ordinal).LongCount(),
            all.SelectMany(static tally => tally.WorksWithText).Distinct(StringComparer.Ordinal).LongCount(),
            classes.Sum(static row => row.Versions),
            classes.Sum(static row => row.VersionsWithText),
            classes.Sum(static row => row.Articles),
            classes.Sum(static row => row.Files),
            classes.Sum(static row => row.FilesWithText),
            classes);
    }

    /// <summary>The ELI type segment of a Luxembourg state IRI (<c>loi</c>, <c>rgd</c>, <c>code</c>), or <see cref="UnrecognisedFamily"/>.</summary>
    internal static string FamilyOf(string stateIri)
    {
        if (!stateIri.StartsWith(EliPrefix, StringComparison.Ordinal))
        {
            return UnrecognisedFamily;
        }

        var end = stateIri.IndexOf('/', EliPrefix.Length);
        return end <= EliPrefix.Length ? UnrecognisedFamily : stateIri[EliPrefix.Length..end];
    }

    /// <summary>Whether a selected manifestation's format is a PDF (the publisher's <c>pdf</c> or <c>pdfa</c>).</summary>
    internal static bool IsPdfFormat(string formatIri)
    {
        var token = formatIri[(formatIri.LastIndexOf('/') + 1)..];
        return token.StartsWith("pdf", StringComparison.Ordinal);
    }

    /// <summary>A member's dispositions in one domain, by wire name in the corpus's ordinal order, joined with <c>+</c>.</summary>
    private static string DispositionsOf(LexCorpus6Member member, LexCorpus6Stage3OutcomeDomain domain)
    {
        var names = member.Stage3Outcomes
            .Where(outcome => outcome.Domain == domain)
            .Select(static outcome => outcome.Disposition)
            .Distinct()
            .Order()
            .Select(static disposition => ContractWire.NameOf(disposition))
            .ToArray();
        return names.Length == 0 ? NoLegalContentOutcome : string.Join("+", names);
    }

    private static Dictionary<string, Dictionary<string, List<string>>> FactsBySubject(
        IEnumerable<(string Subject, string Predicate, string Value)> facts)
    {
        var bySubject = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        foreach (var (subject, predicate, value) in facts)
        {
            if (!bySubject.TryGetValue(subject, out var predicates))
            {
                predicates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                bySubject.Add(subject, predicates);
            }

            if (!predicates.TryGetValue(predicate, out var values))
            {
                values = [];
                predicates.Add(predicate, values);
            }

            values.Add(value);
        }

        return bySubject;
    }

    /// <summary>
    /// The reason this block counts a catalogue version of which no file is held under (<see cref="NotHeldNoExpression"/> and its
    /// siblings), read from the catalogue's facts as given (each a subject, a predicate and its value, as <c>work_facts</c> holds
    /// them): what the catalogue states of it, never why the acquisition did not fetch it. The API's record of an act the index
    /// holds no version of says each of its versions' reasons with this one rule (R77), so Coverage and the record agree.
    /// </summary>
    public static string CatalogueNotHeldReason(IEnumerable<(string Subject, string Predicate, string Value)> facts, string state)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrEmpty(state);
        return NotHeldReason(FactsBySubject(facts.Where(static fact => CataloguePredicates.Contains(fact.Predicate, StringComparer.Ordinal))), state);
    }

    /// <summary>The bucket of a held Luxembourg file of which the index holds articles (<c>files_with_text</c>).</summary>
    public const string HeldFileWithText = "files_with_text";

    /// <summary>The bucket of a held Luxembourg file served without text whose rights withhold its text (<c>files_withheld</c>).</summary>
    public const string HeldFileWithheld = "files_withheld";

    /// <summary>The bucket of a held, admitted Luxembourg PDF file served without text (<c>files_pdf_only</c>).</summary>
    public const string HeldFilePdfOnly = "files_pdf_only";

    /// <summary>The bucket of a held, admitted Luxembourg file of which no article is served (<c>files_parse_failed</c>).</summary>
    public const string HeldFileParseFailed = "files_parse_failed";

    /// <summary>
    /// Where this block counts a held Luxembourg file of which no article is served: its bucket (<see cref="HeldFileWithheld"/>,
    /// <see cref="HeldFilePdfOnly"/>, <see cref="HeldFileParseFailed"/>) and the reason it is counted under there (the rights
    /// disposition's wire name, none for a PDF, the legal-content dispositions joined with <c>+</c>). One rule, which the count
    /// applies and the API's record of an act the index holds no version of says (R77), so the two never disagree.
    /// </summary>
    public static (string Bucket, string? Reason) HeldFileWithoutText(LexCorpus6Member member)
    {
        return HeldFile(member, 0);
    }

    /// <summary>
    /// Where this block counts a held Luxembourg file the index holds <paramref name="articles"/> articles of: with text
    /// (<see cref="HeldFileWithText"/>, no reason) when it holds any, else as <see cref="HeldFileWithoutText"/> says. One rule,
    /// which the count applies and the API's record of an act the index holds no version of says (R77): a file whose articles
    /// the index holds while it holds no state of its version is counted with text here, and the record says so.
    /// </summary>
    public static (string Bucket, string? Reason) HeldFile(LexCorpus6Member member, long articles)
    {
        ArgumentNullException.ThrowIfNull(member);
        var rights = member.LuxembourgRights ?? throw new ArgumentException("A held Luxembourg file carries its rights evidence.", nameof(member));
        if (articles > 0)
        {
            return (HeldFileWithText, null);
        }

        if (member.Outcome == LexCorpus6OutcomeKind.RightsWithheld)
        {
            return (HeldFileWithheld, ContractWire.NameOf(rights.Disposition));
        }

        return IsPdfFormat(rights.SelectedWemi.FormatIri)
            ? (HeldFilePdfOnly, null)
            : (HeldFileParseFailed, DispositionsOf(member, LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent));
    }

    /// <summary>
    /// The versions of one work among catalogue subjects, as this block counts them (each subject an ELI state of the work: its act
    /// as published <c>{work}/jo</c>, a consolidation <c>{work}/consolide/{yyyymmdd}</c> or a code's <c>{work}/{yyyymmdd}</c>), each
    /// once, in ordinal order.
    /// </summary>
    public static IReadOnlyList<string> CatalogueVersionsOf(IEnumerable<string> subjects, string work)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentException.ThrowIfNullOrEmpty(work);
        return subjects
            .Where(subject => LuxembourgIndexBuilder.StateKindOf(subject) is not null &&
                              string.Equals(LuxembourgEliState.WorkOf(subject), work, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>A catalogue version's kind as the index names a state's (<c>as_published</c>, <c>consolidation</c>, <c>code</c>), or null.</summary>
    public static string? CatalogueVersionKindOf(string subject) => LuxembourgIndexBuilder.StateKindOf(subject);

    /// <summary>The work a catalogue version or a held file's root belongs to (<c>{work}/jo</c>, a consolidation, a code's version), or null.</summary>
    public static string? CatalogueVersionWorkOf(string subject) => LuxembourgEliState.WorkOf(subject);

    /// <summary>
    /// What the catalogue says of a state of which no file is held: that it names no expression, or translations only; that
    /// its French expressions have no XML or PDF manifestation (the formats this site reads); or how the catalogue licenses
    /// their XML and PDF manifestations.
    /// </summary>
    private static string NotHeldReason(Dictionary<string, Dictionary<string, List<string>>> facts, string state)
    {
        IReadOnlyList<string> Values(string subject, string predicate) =>
            facts.TryGetValue(subject, out var predicates) && predicates.TryGetValue(predicate, out var values) ? values : [];

        var expressions = Values(state, "isRealizedBy");
        if (expressions.Count == 0)
        {
            return NotHeldNoExpression;
        }

        var french = expressions.Where(expression => Values(expression, "language").Contains(FrenchLanguageIri, StringComparer.Ordinal)).ToArray();
        if (french.Length == 0)
        {
            return NotHeldTranslationsOnly;
        }

        var readable = french
            .SelectMany(expression => Values(expression, "isEmbodiedBy"))
            .Distinct(StringComparer.Ordinal)
            .Where(manifestation => Values(manifestation, "userFormat").Any(static format =>
                format.StartsWith(UserFormatPrefix, StringComparison.Ordinal) &&
                (format[UserFormatPrefix.Length..].StartsWith("xml", StringComparison.Ordinal) ||
                 format[UserFormatPrefix.Length..].StartsWith("pdf", StringComparison.Ordinal))))
            .ToArray();
        if (readable.Length == 0)
        {
            return NotHeldNoReadableManifestation;
        }

        var licences = readable.Select(manifestation => Values(manifestation, "license").Distinct(StringComparer.Ordinal).ToArray()).ToArray();
        var scl = licences.Count(static values => values is [LicenceScl]);
        var ccBy = licences.Count(static values => values is [LicenceCcBy]);
        return scl == licences.Length ? NotHeldLicenceScl
            : scl > 0 || licences.Any(static values => values.Contains(LicenceScl, StringComparer.Ordinal)) ? NotHeldLicenceSclInPart
            : ccBy == licences.Length ? NotHeldCcByNotAcquired
            : NotHeldOtherLicence;
    }
}

/// <summary>
/// One publisher's yield: its acts (distinct, so not the sum of its classes'), its versions and the files held (each the
/// sum of its classes'), the articles served, and its classes.
/// </summary>
public sealed record V3TextYieldPublisher(
    string Publisher,
    long Works,
    long WorksWithText,
    long Versions,
    long VersionsWithText,
    long Articles,
    long Files,
    long FilesWithText,
    IReadOnlyList<V3TextYieldClass> Classes);

/// <summary>
/// One class of act: a Luxembourg family (the ELI type, <c>code</c>, <c>loi</c>, <c>rgd</c>) and state kind
/// (<c>code</c>, <c>as_published</c>, <c>consolidation</c>), or the EU's content class (<c>original_legal_text</c>,
/// <c>consolidation</c>). Each held file is in exactly one of <see cref="FilesWithText"/>, <see cref="FilesWithheld"/>,
/// <see cref="FilesPdfOnly"/> and <see cref="FilesParseFailed"/>. <see cref="VersionsNotHeld"/> is null where the index cannot
/// tell a version with no file held (the EU), and <see cref="FilesPdfOnly"/> where the corpus records no file format (the EU:
/// a file with no Formex main body, a PDF or HTML one among them, is counted under <see cref="FilesParseFailed"/> by its own
/// disposition, such as <c>formex_main_body_not_eligible</c>); <see cref="Annexes"/> is null where the corpus records no
/// annex outcome (Luxembourg).
/// </summary>
public sealed record V3TextYieldClass(
    string Family,
    string Kind,
    long Works,
    long WorksWithText,
    long Versions,
    long VersionsWithText,
    long Articles,
    IReadOnlyList<V3TextYieldCount>? VersionsNotHeld,
    long Files,
    long FilesWithText,
    IReadOnlyList<V3TextYieldWithheld> FilesWithheld,
    long? FilesPdfOnly,
    IReadOnlyList<V3TextYieldCount> FilesParseFailed,
    V3TextYieldAnnexes? Annexes);

/// <summary>A count under a reason the data carries.</summary>
public sealed record V3TextYieldCount(string Reason, long Count);

/// <summary>The held files whose text is withheld under one rights disposition, and how many of them are PDFs.</summary>
public sealed record V3TextYieldWithheld(string Reason, long Files, long Pdf);

/// <summary>
/// The annex outcomes the corpus recorded on a class's files (R84's counts): with text (served as text: none, since no
/// annex is served as an article), image-only (<c>annex_text_not_available</c>, recorded on positive evidence) and failed
/// (every other), and each disposition's count.
/// </summary>
public sealed record V3TextYieldAnnexes(long Annexes, long WithText, long ImageOnly, long Failed, IReadOnlyList<V3TextYieldCount> ByDisposition)
{
    internal static V3TextYieldAnnexes Of(IReadOnlyDictionary<string, long> byDisposition)
    {
        var total = byDisposition.Values.Sum();
        var imageOnly = byDisposition.GetValueOrDefault(ContractWire.NameOf(LexCorpus6Stage3Disposition.AnnexTextNotAvailable));
        return new V3TextYieldAnnexes(
            total,
            0,
            imageOnly,
            total - imageOnly,
            byDisposition.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new V3TextYieldCount(pair.Key, pair.Value)).ToArray());
    }
}

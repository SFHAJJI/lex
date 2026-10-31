using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Names;

/// <summary>
/// R92(1)'s measurement over one mount, read in place and read-only (the plan's item 1.11): every work the mount serves is
/// given a class (<see cref="PrivateIndividuals.LuxembourgClass"/>, <see cref="PrivateIndividuals.FreezesListedPersons"/>) and
/// its served text read by the detector (<see cref="PrivateIndividuals.DetectText"/>); so is every act the Luxembourg
/// catalogue names by a title the API can serve without holding its text. Its outcome is three files, each one form for one
/// mount: the indexing record (each work measured to name no private individual whose dossier the site serves), the names
/// record (each work withheld under R92, bound to the mount's corpus and index builds) and the measurement's counts. None
/// holds a name or any other personal data: identifiers, classes, reason codes, counts and hashes only.
/// </summary>
public static class NamesMeasurement
{
    public const string MeasurementFileName = "names-measurement.json";

    public const string MeasurementSchema = "lex-v3-names-measurement/2";

    private const string LuxembourgIndexFile = "luxembourg-index.sqlite3";

    private const string EuropeIndexFile = "europe-index.sqlite3";

    private const string LuxembourgSchema = "lex-v3-luxembourg-index/9";

    /// <summary>Index 10: index 9's tables, which the measurement reads, and the relation records, which it does not.</summary>
    private const string LuxembourgSchemaTen = "lex-v3-luxembourg-index/10";

    private const string EuropeSchema = "lex-v3-europe-index/6";

    /// <summary>The EU index /7: schema 6 and the expressions' titles, which the measurement reads as it reads a Luxembourg title.</summary>
    private const string EuropeTitlesSchema = "lex-v3-europe-index/7";

    /// <summary>The three files of one measurement, as bytes, and the works it withholds and lists.</summary>
    public sealed record Outcome(
        NamesMeasuredMount Measured,
        IReadOnlyList<NamesWithheldWork> Withheld,
        IReadOnlyList<(string Address, IReadOnlyList<string> Languages)> Indexed,
        byte[] IndexingRecordBytes,
        byte[] NamesRecordBytes,
        byte[] MeasurementBytes);

    // What one scope (the Luxembourg works the index holds, the catalogue's other acts, the EU acts) counted.
    private sealed class Tally
    {
        public long Works;
        public long Articles;
        public SortedDictionary<string, long> Classes { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, (long Works, long Articles)> Findings { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, (long Works, long Articles)> Signals { get; } = new(StringComparer.Ordinal);
        public long Withheld;
        public long WithheldByClass;
        public long WithheldByText;
        public long Indexed;
        public SortedDictionary<string, long> NotListed { get; } = new(StringComparer.Ordinal);

        // Articles the index holds that no state references: every reader query reaches an article through a state, so
        // they are served nowhere and the detector does not read them; the measurement records them, per expression, so
        // the index's own gap is on record (derive-r3, 2026-10-31: two 2026 acts admitted by the rulings whose states
        // the derive then dropped kept their 52 articles).
        public long UnservedArticles;
        public SortedDictionary<string, long> UnservedExpressions { get; } = new(StringComparer.Ordinal);
    }

    // One work as the measurement reads it: its identity, titles, text languages, state digests and what its text showed.
    private sealed class WorkReading
    {
        public required string WorkIri { get; init; }
        public string? WorkKey { get; init; }
        public string? Celex { get; init; }
        public List<string> Titles { get; } = [];
        public SortedSet<string> TextLanguages { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> StateSha256s { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Findings { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> FindingArticles { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> SignalArticles { get; } = new(StringComparer.Ordinal);
        public bool FreezesListedPersons;
        public long Articles;

        /// <summary>
        /// One served article: its text, and every other text of it the API serves (its notes' bodies, an EU article's heading),
        /// each read by the detector on its own; the article counts once, under each code any of them gave.
        /// </summary>
        public void Read(string text, IEnumerable<string> served)
        {
            Articles++;
            var texts = served.Prepend(text).Where(static part => part.Length > 0).ToArray();
            var findings = texts.SelectMany(PrivateIndividuals.DetectText).ToHashSet(StringComparer.Ordinal);
            foreach (var code in findings)
            {
                Findings.Add(code);
                FindingArticles[code] = FindingArticles.GetValueOrDefault(code) + 1;
            }

            foreach (var code in texts.SelectMany(PrivateIndividuals.SignalsIn).ToHashSet(StringComparer.Ordinal))
            {
                SignalArticles[code] = SignalArticles.GetValueOrDefault(code) + 1;
            }

            FreezesListedPersons |= texts.Any(PrivateIndividuals.FreezesListedPersons);
        }

        /// <summary>One served title: read by the detector as an article's text is, its findings the work's; it counts as no article.</summary>
        public void ReadTitle(string title)
        {
            if (title.Length == 0) return;
            foreach (var code in PrivateIndividuals.DetectText(title).ToHashSet(StringComparer.Ordinal))
            {
                Findings.Add(code);
            }

            Titles.Add(title);
        }
    }

    /// <summary>
    /// Measures the mount in <paramref name="mountDirectory"/> (its Luxembourg index of schema 9, its EU index of schema 6 or 7,
    /// or both), read-only and in place, or throws <see cref="InvalidDataException"/> when it holds neither or they bind different
    /// corpora. <paramref name="log"/> hears counts only.
    /// </summary>
    public static Outcome Measure(string mountDirectory, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountDirectory);
        log ??= static _ => { };
        var luxembourgPath = Path.Combine(mountDirectory, LuxembourgIndexFile);
        var europePath = Path.Combine(mountDirectory, EuropeIndexFile);
        var hasLuxembourg = File.Exists(luxembourgPath);
        var hasEurope = File.Exists(europePath);
        if (!hasLuxembourg && !hasEurope)
        {
            throw new InvalidDataException("The mount holds no Luxembourg and no EU index to measure.");
        }

        string? corpus = null;
        string? builtAt = null;
        string? luxembourgDigest = null;
        string? europeDigest = null;
        var withheld = new List<NamesWithheldWork>();
        var indexed = new List<(string Address, IReadOnlyList<string> Languages)>();
        Tally? held = null, catalogue = null, europe = null;
        var europeHoldsTitles = false;
        if (hasLuxembourg)
        {
            log("hashing the Luxembourg index");
            luxembourgDigest = FileSha256(luxembourgPath, cancellationToken);
            using var connection = OpenReadOnly(luxembourgPath);
            var (schema, stampedCorpus) = Stamp(connection);
            if (!string.Equals(schema, LuxembourgSchema, StringComparison.Ordinal) &&
                !string.Equals(schema, LuxembourgSchemaTen, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The Luxembourg index is {schema}; the measurement reads {LuxembourgSchema} and {LuxembourgSchemaTen}.");
            }

            corpus = stampedCorpus;
            builtAt = Scalar(connection, "SELECT built_at FROM observations ORDER BY observation DESC LIMIT 1");
            (held, catalogue) = MeasureLuxembourg(connection, withheld, indexed, log, cancellationToken);
        }

        if (hasEurope)
        {
            log("hashing the EU index");
            europeDigest = FileSha256(europePath, cancellationToken);
            using var connection = OpenReadOnly(europePath);
            var (schema, stampedCorpus) = Stamp(connection);
            if (!string.Equals(schema, EuropeSchema, StringComparison.Ordinal) && !string.Equals(schema, EuropeTitlesSchema, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The EU index is {schema}; the measurement reads {EuropeSchema} or {EuropeTitlesSchema}.");
            }

            europeHoldsTitles = string.Equals(schema, EuropeTitlesSchema, StringComparison.Ordinal);

            if (corpus is not null && !string.Equals(corpus, stampedCorpus, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The mount's two indexes bind different corpora.");
            }

            corpus = stampedCorpus;
            europe = MeasureEurope(connection, europeHoldsTitles, withheld, indexed, log, cancellationToken);
        }

        var measured = new NamesMeasuredMount(corpus!, luxembourgDigest, europeDigest, builtAt);
        var indexingBytes = IndexingRecord.Write(builtAt, indexed);
        var namesBytes = NamesRecord.Write(measured, withheld);
        var measurementBytes = WriteMeasurement(measured, held, catalogue, europe, europeHoldsTitles, indexingBytes, namesBytes);
        return new Outcome(measured, withheld.AsReadOnly(), indexed.AsReadOnly(), indexingBytes, namesBytes, measurementBytes);
    }

    /// <summary>Writes an outcome's three files into <paramref name="directory"/> (created if absent), each whole.</summary>
    public static void WriteFiles(Outcome outcome, string directory)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, IndexingRecord.FileName), outcome.IndexingRecordBytes);
        File.WriteAllBytes(Path.Combine(directory, NamesRecord.FileName), outcome.NamesRecordBytes);
        File.WriteAllBytes(Path.Combine(directory, MeasurementFileName), outcome.MeasurementBytes);
    }

    private static (Tally Held, Tally Catalogue) MeasureLuxembourg(
        SqliteConnection connection,
        List<NamesWithheldWork> withheld,
        List<(string Address, IReadOnlyList<string> Languages)> indexed,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        // The works the index holds a version of, by key; each expression's work; and each work IRI's key.
        var works = new SortedDictionary<string, WorkReading>(StringComparer.Ordinal);
        var workOfExpression = new Dictionary<string, WorkReading>(StringComparer.Ordinal);
        var keyOfIri = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT work_key, publisher_work_iri, expression_iri, language, state_sha256, text_status FROM states ORDER BY work_key, state_sha256";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var workKey = reader.GetString(0);
                if (!works.TryGetValue(workKey, out var work))
                {
                    work = new WorkReading { WorkIri = reader.GetString(1), WorkKey = workKey };
                    works[workKey] = work;
                    keyOfIri[work.WorkIri] = workKey;
                }

                var expression = reader.GetString(2);
                if (expression.Length > 0)
                {
                    workOfExpression[expression] = work;
                }

                work.StateSha256s.Add(reader.GetString(4));
                if (string.Equals(reader.GetString(5), "held", StringComparison.Ordinal) && reader.GetString(3).Length > 0)
                {
                    work.TextLanguages.Add(reader.GetString(3));
                }
            }
        }

        // The titles the index holds of its works' expressions, and every title the catalogue states of any act.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT DISTINCT expression_iri, title FROM work_titles ORDER BY expression_iri, title";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (workOfExpression.TryGetValue(reader.GetString(0), out var work))
                {
                    work.Titles.Add(reader.GetString(1));
                }
            }
        }

        var catalogued = new SortedDictionary<string, WorkReading>(StringComparer.Ordinal);
        long titlesOfNoWork = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT subject_iri, object_value FROM work_facts WHERE predicate IN ('title','titleShort') AND object_kind = 'literal' " +
                "ORDER BY subject_iri, object_value";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var iri = PrivateIndividuals.LuxembourgWorkIriOf(reader.GetString(0));
                if (iri is null)
                {
                    titlesOfNoWork++;
                    continue;
                }

                if (keyOfIri.TryGetValue(iri, out var key))
                {
                    works[key].Titles.Add(reader.GetString(1));
                }
                else
                {
                    if (!catalogued.TryGetValue(iri, out var act))
                    {
                        act = new WorkReading { WorkIri = iri };
                        catalogued[iri] = act;
                    }

                    act.Titles.Add(reader.GetString(1));
                }
            }
        }

        // Every served article, read once: the detector's findings and signals per work. An article of an expression no
        // state references is served nowhere (every reader query reaches an article through a state), so it is not read:
        // it is counted, per expression, and the tally records it (unserved_articles).
        log($"reading the Luxembourg articles of {works.Count} works");
        var unserved = new SortedDictionary<string, long>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT expression_iri, searchable_text, tokens_json FROM articles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workOfExpression.TryGetValue(reader.GetString(0), out var work))
                {
                    work.Read(reader.GetString(1), NoteBodies(reader.GetString(2)));
                }
                else
                {
                    Increment(unserved, reader.GetString(0));
                }
            }
        }

        var heldTally = new Tally();
        foreach (var (expression, count) in unserved)
        {
            heldTally.UnservedArticles += count;
            heldTally.UnservedExpressions[expression] = count;
        }

        if (heldTally.UnservedArticles > 0)
        {
            log($"{heldTally.UnservedArticles} Luxembourg articles of {unserved.Count} expressions belong to no state: served nowhere, not read, recorded");
        }

        foreach (var work in works.Values)
        {
            heldTally.Works++;
            heldTally.Articles += work.Articles;
            var workClass = PrivateIndividuals.LuxembourgClass(work.Titles);
            Decide(work, workClass, NamesRecord.LuxembourgPublisher, heldTally, withheld, out var isWithheld);
            if (isWithheld)
            {
                continue;
            }

            var address = "/lu-legilux/" + work.WorkKey;
            if (work.TextLanguages.Count == 0)
            {
                Increment(heldTally.NotListed, "no_text_held");
            }
            else if (!IndexingRecord.Listable(address))
            {
                Increment(heldTally.NotListed, "no_site_address");
            }
            else
            {
                indexed.Add((address, work.TextLanguages.ToArray()));
                heldTally.Indexed++;
            }
        }

        var catalogueTally = new Tally();
        foreach (var act in catalogued.Values)
        {
            catalogueTally.Works++;
            Decide(act, PrivateIndividuals.LuxembourgClass(act.Titles), NamesRecord.LuxembourgPublisher, catalogueTally, withheld, out var isWithheld);
            if (!isWithheld)
            {
                Increment(catalogueTally.NotListed, "no_version_held");
            }
        }

        if (titlesOfNoWork > 0)
        {
            catalogueTally.NotListed["title_of_no_work_iri"] = titlesOfNoWork;
        }

        log($"Luxembourg: {heldTally.Works} works held, {heldTally.Withheld} withheld, {heldTally.Indexed} listed; " +
            $"{catalogueTally.Works} catalogue acts, {catalogueTally.Withheld} withheld");
        return (heldTally, catalogueTally);
    }

    private static Tally MeasureEurope(
        SqliteConnection connection,
        bool holdsTitles,
        List<NamesWithheldWork> withheld,
        List<(string Address, IReadOnlyList<string> Languages)> indexed,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        // The EU acts by their own CELEX (the seed), each with its Cellar work and the version works its articles are read from.
        var acts = new SortedDictionary<string, WorkReading>(StringComparer.Ordinal);
        var actOfVersion = new Dictionary<string, WorkReading>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT seed_celex, root_work_iri, publisher_work_iri, state_identity_sha256 FROM states ORDER BY seed_celex, state_identity_sha256";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var celex = reader.GetString(0);
                if (!acts.TryGetValue(celex, out var act))
                {
                    act = new WorkReading { WorkIri = reader.GetString(1), Celex = celex };
                    acts[celex] = act;
                }
                else if (!string.Equals(act.WorkIri, reader.GetString(1), StringComparison.Ordinal))
                {
                    throw new InvalidDataException("An EU act's states name two root works.");
                }

                actOfVersion[reader.GetString(2)] = act;
                act.StateSha256s.Add(reader.GetString(3));
            }
        }

        log($"reading the EU articles of {acts.Count} acts");
        var unservedEu = new SortedDictionary<string, long>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT publisher_work_id, language, searchable_text, heading, tokens_json FROM articles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (actOfVersion.TryGetValue(reader.GetString(0), out var act))
                {
                    act.TextLanguages.Add(reader.GetString(1));
                    act.Read(reader.GetString(2), NoteBodies(reader.GetString(4)).Prepend(reader.GetString(3)));
                }
                else
                {
                    // An article of a version no state names: served nowhere, not read, recorded (as the Luxembourg side does).
                    Increment(unservedEu, reader.GetString(0));
                }
            }
        }

        // An EU index /7 serves its expressions' titles: each is read by the detector as served text is, so a title that names
        // someone withholds its act; a title of a work no state names is the index's own fault.
        if (holdsTitles)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT publisher_work_iri, title_text FROM expression_titles ORDER BY title_identity_sha256";
            using var reader = command.ExecuteReader();
            long titleOrphans = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (actOfVersion.TryGetValue(reader.GetString(0), out var act))
                {
                    act.ReadTitle(reader.GetString(1));
                }
                else
                {
                    titleOrphans++;
                }
            }

            if (titleOrphans > 0)
            {
                throw new InvalidDataException($"{titleOrphans} EU titles belong to no state's work: the index is not one the measurement can read.");
            }
        }

        var tally = new Tally();
        foreach (var (version, count) in unservedEu)
        {
            tally.UnservedArticles += count;
            tally.UnservedExpressions[version] = count;
        }

        if (tally.UnservedArticles > 0)
        {
            log($"{tally.UnservedArticles} EU articles of {unservedEu.Count} versions belong to no state: served nowhere, not read, recorded");
        }

        foreach (var act in acts.Values)
        {
            tally.Works++;
            tally.Articles += act.Articles;
            var actClass = act.FreezesListedPersons ? PrivateIndividuals.EuropeRestrictiveMeasures : PrivateIndividuals.EuropeGeneral;
            Decide(act, actClass, NamesRecord.EuropePublisher, tally, withheld, out var isWithheld);
            if (isWithheld)
            {
                continue;
            }

            var address = IndexingRecord.EuropeAddressOf(act.Celex!);
            if (act.TextLanguages.Count == 0)
            {
                Increment(tally.NotListed, "no_text_held");
            }
            else if (!IndexingRecord.Listable(address))
            {
                Increment(tally.NotListed, "no_site_address");
            }
            else
            {
                indexed.Add((address, act.TextLanguages.ToArray()));
                tally.Indexed++;
            }
        }

        log($"EU: {tally.Works} acts, {tally.Withheld} withheld, {tally.Indexed} listed");
        return tally;
    }

    /// <summary>Counts one work's class, findings and signals, and withholds it when its class or its text names someone.</summary>
    private static void Decide(WorkReading work, string workClass, string publisher, Tally tally, List<NamesWithheldWork> withheld, out bool isWithheld)
    {
        Increment(tally.Classes, workClass);
        foreach (var (code, articles) in work.FindingArticles)
        {
            var (works, counted) = tally.Findings.GetValueOrDefault(code);
            tally.Findings[code] = (works + 1, counted + articles);
        }

        foreach (var (code, articles) in work.SignalArticles)
        {
            var (works, counted) = tally.Signals.GetValueOrDefault(code);
            tally.Signals[code] = (works + 1, counted + articles);
        }

        var byClass = PrivateIndividuals.NamingClasses.Contains(workClass, StringComparer.Ordinal);
        var byText = work.Findings.Count > 0;
        isWithheld = byClass || byText;
        if (!isWithheld)
        {
            return;
        }

        tally.Withheld++;
        if (byClass) tally.WithheldByClass++;
        if (byText) tally.WithheldByText++;
        var evidence = new SortedSet<string>(work.Findings, StringComparer.Ordinal);
        if (byClass)
        {
            evidence.Add(PrivateIndividuals.ClassCode);
        }

        // A work withheld by its text alone is recorded under its publisher's class for an act whose served text names a person:
        // the class measured from its title (general) would say nothing of why it is withheld.
        var recordedClass = byClass
            ? workClass
            : publisher == NamesRecord.EuropePublisher ? PrivateIndividuals.EuropeTextNamesPerson : PrivateIndividuals.LuxembourgTextNamesPerson;
        withheld.Add(new NamesWithheldWork(publisher, work.WorkIri, work.WorkKey, work.Celex, recordedClass, evidence.ToArray(), work.StateSha256s.ToArray()));
    }

    private static void Increment(SortedDictionary<string, long> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;

    /// <summary>
    /// The bodies of an article's notes as the API serves them (<c>V3CorpusMount.ArticleNotes</c>): each note reference's body, its
    /// text and reference tokens joined in order. The searchable text holds no note body, so the detector reads them apart (the
    /// review of #1001). An article with no note reference is not parsed.
    /// </summary>
    internal static IReadOnlyList<string> NoteBodies(string tokensJson)
    {
        if (string.IsNullOrEmpty(tokensJson) || !tokensJson.Contains("note_reference", StringComparison.Ordinal))
        {
            return [];
        }

        using var document = JsonDocument.Parse(tokensJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var bodies = new List<string>();
        foreach (var token in document.RootElement.EnumerateArray())
        {
            if (token.ValueKind != JsonValueKind.Object || !token.TryGetProperty("kind", out var kind) ||
                !string.Equals(kind.GetString(), "note_reference", StringComparison.Ordinal) ||
                !token.TryGetProperty("note_body", out var body) || body.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var text = new System.Text.StringBuilder();
            foreach (var nested in body.EnumerateArray())
            {
                if (nested.ValueKind == JsonValueKind.Object && nested.TryGetProperty("kind", out var nestedKind) &&
                    nestedKind.GetString() is "text" or "reference" &&
                    nested.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    text.Append(value.GetString());
                }
            }

            bodies.Add(text.ToString());
        }

        return bodies;
    }

    private static byte[] WriteMeasurement(NamesMeasuredMount measured, Tally? held, Tally? catalogue, Tally? europe, bool europeHoldsTitles, byte[] indexing, byte[] names)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, NamesRecord.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", MeasurementSchema);
            writer.WriteString("measure", PrivateIndividuals.MeasureVersion);
            writer.WriteStartObject("measured");
            writer.WriteString("corpus_sha256", measured.CorpusSha256);
            NamesRecord.WriteNullable(writer, "luxembourg_index_sha256", measured.LuxembourgIndexSha256);
            NamesRecord.WriteNullable(writer, "europe_index_sha256", measured.EuropeIndexSha256);
            NamesRecord.WriteNullable(writer, "built_at", measured.BuiltAt);
            writer.WriteEndObject();
            WriteTally(writer, "luxembourg_held", held, "the Luxembourg works the index holds a version of: their titles (the index's and the catalogue's) and every served article");
            WriteTally(writer, "luxembourg_catalogue", catalogue, "the other acts the Luxembourg catalogue names by a title the API can serve (a reference names them): their titles only; no text of them is held");
            WriteTally(writer, "europe", europe, europeHoldsTitles
                ? "the EU acts the index holds, by their own CELEX: their titles (the index's) and every served article"
                : "the EU acts the index holds, by their own CELEX: every served article (the EU index holds no title)");
            writer.WriteStartObject("files");
            writer.WriteString(IndexingRecord.FileName, Convert.ToHexStringLower(SHA256.HashData(indexing)));
            writer.WriteString(NamesRecord.FileName, Convert.ToHexStringLower(SHA256.HashData(names)));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteTally(Utf8JsonWriter writer, string name, Tally? tally, string scope)
    {
        if (tally is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("scope", scope);
        writer.WriteNumber("works", tally.Works);
        writer.WriteNumber("articles_read", tally.Articles);
        writer.WriteNumber("unserved_articles", tally.UnservedArticles);
        writer.WriteStartObject("unserved_expressions");
        foreach (var (key, count) in tally.UnservedExpressions)
        {
            writer.WriteNumber(key, count);
        }

        writer.WriteEndObject();
        writer.WriteStartObject("classes");
        foreach (var (key, count) in tally.Classes)
        {
            writer.WriteNumber(key, count);
        }

        writer.WriteEndObject();
        WritePairs(writer, "findings", tally.Findings);
        WritePairs(writer, "signals", tally.Signals);
        writer.WriteNumber("withheld", tally.Withheld);
        writer.WriteNumber("withheld_by_class", tally.WithheldByClass);
        writer.WriteNumber("withheld_by_text", tally.WithheldByText);
        writer.WriteNumber("indexed", tally.Indexed);
        writer.WriteStartObject("not_listed");
        foreach (var (key, count) in tally.NotListed)
        {
            writer.WriteNumber(key, count);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WritePairs(Utf8JsonWriter writer, string name, SortedDictionary<string, (long Works, long Articles)> pairs)
    {
        writer.WriteStartObject(name);
        foreach (var (key, (works, articles)) in pairs)
        {
            writer.WriteStartObject(key);
            writer.WriteNumber("works", works);
            writer.WriteNumber("articles", articles);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var memory = connection.CreateCommand();
        // Sorts stay in memory: the measurement writes nothing beside the mount it reads.
        memory.CommandText = "PRAGMA temp_store = MEMORY";
        memory.ExecuteNonQuery();
        return connection;
    }

    private static (string Schema, string Corpus) Stamp(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_identity, corpus_sha256 FROM stamp WHERE stamp_id = 1";
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : throw new InvalidDataException("An index holds no stamp.");
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static string FileSha256(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

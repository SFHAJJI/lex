using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Releases;

/// <summary>
/// One version a mount serves, by its served identity: a Luxembourg state (its work, date, expression and language, the
/// coordinate R64's permalink names) or an EU wording (its Cellar work, language and wording date), or a catalogue version
/// the index lists without text. <see cref="IdentitySha256"/> is what a permalink pins: the Luxembourg state digest, or for
/// the EU a digest over the wording's article identities (the API's wording digest is a function of the same coordinate and
/// those identities, so the two change together). <see cref="Identity"/> is the publisher's identity of the version (the
/// Luxembourg state IRI, the EU Cellar work IRI).
/// </summary>
public sealed record V3ServedVersion(
    string Publisher,
    string Key,
    string Work,
    string Identity,
    string Family,
    string Kind,
    string Address,
    string IdentitySha256,
    string TextStatus,
    string? TextStatusReason,
    IReadOnlyList<string> RuleProfiles,
    IReadOnlyList<string> BodySha256s,
    long Articles)
{
    /// <summary>Whether the version is served with text.</summary>
    public bool HasText => string.Equals(TextStatus, V3MountInventory.Held, StringComparison.Ordinal);
}

/// <summary>One served article at a coordinate: the digest of its served text, the publisher body it was read from, and its rule profile (Luxembourg only).</summary>
public sealed record V3ServedArticle(string TextSha256, string BodySha256, string? RuleProfileSha256);

/// <summary>The articles a mount serves at one coordinate (a version and the publisher's article id), usually one.</summary>
public sealed record V3ServedArticles(string VersionKey, string Coordinate, IReadOnlyList<V3ServedArticle> Articles);

/// <summary>
/// One publisher file the corpus holds (a member with a retained body): its outcome, its class, the publisher's identity of
/// what it embodies (the Luxembourg state IRI; null for an EU file, whose member names no work), its format (Luxembourg), its
/// derivation dispositions by wire name, its annex outcomes (EU, one per annex), and how many articles the mount serves from it.
/// </summary>
public sealed record V3HeldFile(
    string Publisher,
    string ObjectRefSha256,
    string BodySha256,
    string Outcome,
    string Family,
    string Kind,
    string? Identity,
    string? Format,
    IReadOnlyList<string> Dispositions,
    IReadOnlyList<string> AnnexDispositions,
    long ArticlesServed);

/// <summary>
/// What one mount serves, read in place and read-only from its corpus and its indexes, as the release-over-release check
/// (R106) and the consolidation text check (R86(2)) compare and list it: every version by its served identity, every article
/// by its coordinate with the digest of its served text, and every held publisher file with its typed outcome. Reads a
/// Luxembourg index of schema 6 to 9 and an EU index of schema 5 or 6, so a candidate can be held to the release serving
/// now, whose mount may be older. Writes nothing.
/// </summary>
public sealed record V3MountInventory(
    string CorpusSha256,
    string? LuxembourgIndexSchema,
    string? EuropeIndexSchema,
    IReadOnlyDictionary<string, V3ServedVersion> Versions,
    IReadOnlyDictionary<string, V3ServedArticles> Articles,
    IReadOnlyList<V3HeldFile> Files)
{
    /// <summary>The text status of a version served with text.</summary>
    public const string Held = "held";

    /// <summary>The text status of a version an index without text statuses (Luxembourg before schema 9, the EU) lists without text.</summary>
    public const string NoText = "no_text";

    /// <summary>The Luxembourg publisher's wire name.</summary>
    public const string Luxembourg = "lu-legilux";

    /// <summary>The EU publisher's wire name.</summary>
    public const string Europe = "eu-eurlex";

    /// <summary>
    /// Reads the mount in <paramref name="mountDirectory"/>: its corpus (verified as the API verifies it) and whichever of its
    /// two indexes it holds, each of which must bind that corpus. Throws <see cref="InvalidDataException"/> on a mount that is
    /// not one (no corpus, no index, an index of another corpus, an article of no member).
    /// </summary>
    public static V3MountInventory Read(string mountDirectory, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountDirectory);
        log ??= static _ => { };
        var corpusPath = Path.Combine(mountDirectory, V3FirstMountBuildResult.CorpusFileName);
        if (!File.Exists(corpusPath))
        {
            throw new InvalidDataException($"The mount holds no {V3FirstMountBuildResult.CorpusFileName}.");
        }

        log("reading the corpus");
        LexCorpus6MountView view;
        try
        {
            view = LexCorpus6MountView.ParseCanonicalAndVerify(File.ReadAllBytes(corpusPath));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The mount's corpus does not verify.", exception);
        }

        var corpusSha256 = view.ArtifactRef.Sha256;
        var bodies = new Dictionary<(string, string), string>();
        var held = new List<(LexCorpus6Member Member, string Publisher)>();
        foreach (var member in view.HeldMembers)
        {
            if (member.BodySha256 is not { } body)
            {
                continue;
            }

            var publisher = ContractWire.NameOf(member.Publisher);
            bodies[(publisher, member.ObjectRefSha256)] = body;
            held.Add((member, publisher));
        }

        var versions = new Dictionary<string, V3ServedVersion>(StringComparer.Ordinal);
        var articles = new Dictionary<string, (string VersionKey, string Coordinate, List<V3ServedArticle> Articles)>(StringComparer.Ordinal);
        var servedByObject = new Dictionary<(string, string), long>();
        string? luxembourgSchema = null, europeSchema = null;
        var luxembourgPath = Path.Combine(mountDirectory, V3FirstMountBuildResult.LuxembourgIndexFileName);
        var europePath = Path.Combine(mountDirectory, V3FirstMountBuildResult.EuropeIndexFileName);
        if (!File.Exists(luxembourgPath) && !File.Exists(europePath))
        {
            throw new InvalidDataException("The mount holds no Luxembourg and no EU index.");
        }

        if (File.Exists(luxembourgPath))
        {
            log("reading the Luxembourg index");
            luxembourgSchema = ReadLuxembourg(luxembourgPath, corpusSha256, bodies, versions, articles, servedByObject, cancellationToken);
        }

        if (File.Exists(europePath))
        {
            log("reading the EU index");
            europeSchema = ReadEurope(europePath, corpusSha256, bodies, versions, articles, servedByObject, cancellationToken);
        }

        var files = new List<V3HeldFile>(held.Count);
        foreach (var (member, publisher) in held)
        {
            var served = servedByObject.GetValueOrDefault((publisher, member.ObjectRefSha256));
            if (member.LuxembourgRights is { } rights)
            {
                var root = rights.SelectedWemi.RootIri;
                var format = rights.SelectedWemi.FormatIri;
                files.Add(new V3HeldFile(
                    publisher, member.ObjectRefSha256, member.BodySha256!, ContractWire.NameOf(member.Outcome),
                    V3TextYield.FamilyOf(root), LuxembourgIndexBuilder.StateKindOf(root) ?? V3TextYield.UnrecognisedFamily,
                    root, format[(format.LastIndexOf('/') + 1)..],
                    Dispositions(member, LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent, LexCorpus6Stage3OutcomeDomain.LuxembourgPublisherPdfActScope),
                    [], served));
            }
            else if (member.EuropeContentClass is { } contentClass)
            {
                files.Add(new V3HeldFile(
                    publisher, member.ObjectRefSha256, member.BodySha256!, ContractWire.NameOf(member.Outcome),
                    V3TextYield.EuropeFamily, ContractWire.NameOf(contentClass.ContentClass), null, null,
                    Dispositions(member, LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody),
                    member.Stage3Outcomes
                        .Where(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody)
                        .Select(static outcome => ContractWire.NameOf(outcome.Disposition))
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    served));
            }
        }

        files.Sort(static (left, right) => string.CompareOrdinal(left.Publisher + "|" + left.ObjectRefSha256, right.Publisher + "|" + right.ObjectRefSha256));
        return new V3MountInventory(
            corpusSha256,
            luxembourgSchema,
            europeSchema,
            versions,
            articles.ToDictionary(
                static pair => pair.Key,
                static pair => new V3ServedArticles(pair.Value.VersionKey, pair.Value.Coordinate, pair.Value.Articles
                    .OrderBy(static article => article.TextSha256, StringComparer.Ordinal)
                    .ThenBy(static article => article.BodySha256, StringComparer.Ordinal)
                    .ToArray()),
                StringComparer.Ordinal),
            files);
    }

    /// <summary>
    /// The files a report lists (a build report's or a release's mount report's <c>files</c>: name, byte length, SHA-256) held
    /// to the files in <paramref name="mountDirectory"/>, as failures (empty when each is there, of its length and digest).
    /// </summary>
    public static IReadOnlyList<string> FileFailures(string mountDirectory, byte[] reportBytes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountDirectory);
        ArgumentNullException.ThrowIfNull(reportBytes);
        var failures = new List<string>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(reportBytes);
        }
        catch (JsonException)
        {
            return ["the mount report is not JSON"];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("files", out var listed) || listed.ValueKind != JsonValueKind.Array || listed.GetArrayLength() == 0)
            {
                return ["the mount report lists no file"];
            }

            foreach (var file in listed.EnumerateArray())
            {
                var name = file.TryGetProperty("Name", out var nameValue) ? nameValue.GetString() : null;
                var length = file.TryGetProperty("ByteLength", out var lengthValue) && lengthValue.TryGetInt64(out var parsed) ? parsed : -1;
                var sha256 = file.TryGetProperty("Sha256", out var shaValue) ? shaValue.GetString() : null;
                if (string.IsNullOrEmpty(name) || name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal) || sha256 is null)
                {
                    failures.Add($"the mount report lists a file it does not name with a digest: {file.GetRawText()}");
                    continue;
                }

                var path = Path.Combine(mountDirectory, name);
                if (!File.Exists(path))
                {
                    failures.Add($"the mount holds no {name}, which its report lists");
                    continue;
                }

                var actual = new FileInfo(path).Length;
                if (length >= 0 && actual != length)
                {
                    failures.Add($"{name} is {actual} bytes, not the {length} its report records");
                    continue;
                }

                var digest = FileSha256(path, cancellationToken);
                if (!string.Equals(digest, sha256, StringComparison.Ordinal))
                {
                    failures.Add($"{name} is sha256:{digest}, not the {sha256} its report records");
                }
            }
        }

        return failures;
    }

    internal static string FileSha256(string path, CancellationToken cancellationToken)
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

    private static string ReadLuxembourg(
        string path,
        string corpusSha256,
        IReadOnlyDictionary<(string, string), string> bodies,
        Dictionary<string, V3ServedVersion> versions,
        Dictionary<string, (string VersionKey, string Coordinate, List<V3ServedArticle> Articles)> articles,
        Dictionary<(string, string), long> servedByObject,
        CancellationToken cancellationToken)
    {
        using var connection = OpenReadOnly(path);
        var schema = Stamp(connection, corpusSha256, "Luxembourg");
        var byIdentity = new Dictionary<string, (string PublisherId, string ObjectRef, string Profile, string TextSha256)>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT article_identity_sha256, object_ref_sha256, publisher_id, rule_profile_sha256, searchable_text FROM articles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var objectRef = reader.GetString(1);
                byIdentity.Add(reader.GetString(0), (reader.GetString(2), objectRef, reader.GetString(3), TextSha256(reader.GetString(4))));
                var key = (Luxembourg, objectRef);
                servedByObject[key] = servedByObject.GetValueOrDefault(key) + 1;
            }
        }

        var hasStatus = Columns(connection, "states").Contains("text_status");
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT work_key, applicability_date, expression_iri, language, state_sha256, publisher_legal_resource_iri, rule_profiles_json, article_identities_json" +
                (hasStatus ? ", text_status, text_status_reason" : "") +
                " FROM states ORDER BY work_key, applicability_date, expression_iri, language";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var workKey = reader.GetString(0);
                var date = reader.GetString(1);
                var expression = reader.GetString(2);
                var language = reader.GetString(3);
                var stateSha256 = reader.GetString(4);
                var legalResource = reader.GetString(5);
                var profiles = (JsonSerializer.Deserialize<string[]>(reader.GetString(6)) ?? []).Order(StringComparer.Ordinal).ToArray();
                var identities = JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? [];
                var status = hasStatus ? reader.GetString(8) : identities.Length > 0 ? Held : NoText;
                var reason = hasStatus && !reader.IsDBNull(9) ? reader.GetString(9) : null;
                var key = string.Join('|', Luxembourg, workKey, date, expression, language);
                var stateBodies = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var identity in identities)
                {
                    if (!byIdentity.TryGetValue(identity, out var article))
                    {
                        throw new InvalidDataException($"The Luxembourg state {workKey} {date} names an article the index does not hold.");
                    }

                    if (!bodies.TryGetValue((Luxembourg, article.ObjectRef), out var body))
                    {
                        throw new InvalidDataException($"A Luxembourg article of {workKey} {date} is read from a member the corpus does not hold.");
                    }

                    stateBodies.Add(body);
                    var articleKey = key + "|" + article.PublisherId;
                    if (!articles.TryGetValue(articleKey, out var group))
                    {
                        group = (key, article.PublisherId, []);
                        articles.Add(articleKey, group);
                    }

                    group.Articles.Add(new V3ServedArticle(article.TextSha256, body, article.Profile));
                }

                var address = language.Length == 0
                    ? $"/lu-legilux/{workKey}/{date}"
                    : $"/lu-legilux/{workKey}/{date}--{stateSha256}/{language}";
                versions.Add(key, new V3ServedVersion(
                    Luxembourg, key, workKey, legalResource, V3TextYield.FamilyOf(legalResource),
                    LuxembourgIndexBuilder.StateKindOf(legalResource) ?? V3TextYield.UnrecognisedFamily, address, stateSha256,
                    status, reason, profiles, stateBodies.ToArray(), identities.Length));
            }
        }

        return schema;
    }

    private static string ReadEurope(
        string path,
        string corpusSha256,
        IReadOnlyDictionary<(string, string), string> bodies,
        Dictionary<string, V3ServedVersion> versions,
        Dictionary<string, (string VersionKey, string Coordinate, List<V3ServedArticle> Articles)> articles,
        Dictionary<(string, string), long> servedByObject,
        CancellationToken cancellationToken)
    {
        using var connection = OpenReadOnly(path);
        var schema = Stamp(connection, corpusSha256, "EU");
        var states = new SortedDictionary<string, (SortedSet<string> Seeds, bool Original, string? Date)>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT seed_celex, publisher_work_iri, date_status, publisher_consolidation_date FROM states";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var work = reader.GetString(1);
                if (!states.TryGetValue(work, out var state))
                {
                    state = (new SortedSet<string>(StringComparer.Ordinal), reader.GetInt64(2) == (long)EuropeIndexStateDateStatus.OriginalWording,
                        reader.IsDBNull(3) ? null : reader.GetString(3));
                    states.Add(work, state);
                }

                state.Seeds.Add(reader.GetString(0));
            }
        }

        var wordings = new SortedDictionary<string, (string Work, string Celex, string Language, string Date, List<string> Identities, SortedSet<string> Bodies)>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT article_identity_sha256, object_ref_sha256, publisher_work_id, publisher_work_celex, language, wording_date, package_entry, publisher_identifier, searchable_text FROM articles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var objectRef = reader.GetString(1);
                var work = reader.GetString(2);
                var language = reader.GetString(4);
                var date = reader.GetString(5);
                if (!bodies.TryGetValue((Europe, objectRef), out var body))
                {
                    throw new InvalidDataException($"An EU article of {work} is read from a member the corpus does not hold.");
                }

                var celex = reader.IsDBNull(3) ? null : reader.GetString(3);
                var key = string.Join('|', Europe, work, language, date);
                if (!wordings.TryGetValue(key, out var wording))
                {
                    var seed = states.TryGetValue(work, out var state) ? state.Seeds.Min : null;
                    wording = (work, celex ?? seed ?? work, language, date, [], new SortedSet<string>(StringComparer.Ordinal));
                    wordings.Add(key, wording);
                }

                wording.Identities.Add(reader.GetString(0));
                wording.Bodies.Add(body);
                var coordinate = reader.GetString(6) + "|" + reader.GetString(7);
                var articleKey = key + "|" + coordinate;
                if (!articles.TryGetValue(articleKey, out var group))
                {
                    group = (key, coordinate, []);
                    articles.Add(articleKey, group);
                }

                group.Articles.Add(new V3ServedArticle(TextSha256(reader.GetString(8)), body, null));
                var served = (Europe, objectRef);
                servedByObject[served] = servedByObject.GetValueOrDefault(served) + 1;
            }
        }

        var withText = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, wording) in wordings)
        {
            withText.Add(wording.Work);
            var known = states.TryGetValue(wording.Work, out var state);
            var kind = !known
                ? V3TextYield.UnrecognisedFamily
                : state.Original
                    ? ContractWire.NameOf(Lex.V3.Contracts.Source.Europe.EuContentClass.OriginalLegalText)
                    : ContractWire.NameOf(Lex.V3.Contracts.Source.Europe.EuContentClass.Consolidation);
            var work = (known ? state.Seeds.Min : null) ?? wording.Celex;
            versions.Add(key, new V3ServedVersion(
                Europe, key, work, wording.Work, V3TextYield.EuropeFamily, kind,
                $"/eu-eurlex/{wording.Celex.Replace("/", "%2F", StringComparison.Ordinal)}/{wording.Language}/{wording.Date}",
                IdentitiesSha256(wording.Identities), Held, null, [], wording.Bodies.ToArray(), wording.Identities.Count));
        }

        // A census version no article is served from: listed without text, by its Cellar work.
        foreach (var (work, state) in states)
        {
            if (withText.Contains(work))
            {
                continue;
            }

            var key = string.Join('|', Europe, work, "", state.Date ?? "");
            var kind = state.Original
                ? ContractWire.NameOf(Lex.V3.Contracts.Source.Europe.EuContentClass.OriginalLegalText)
                : ContractWire.NameOf(Lex.V3.Contracts.Source.Europe.EuContentClass.Consolidation);
            var seed = state.Seeds.Min!;
            versions.Add(key, new V3ServedVersion(
                Europe, key, seed, work, V3TextYield.EuropeFamily, kind,
                $"/eu-eurlex/{seed.Replace("/", "%2F", StringComparison.Ordinal)}" + (state.Date is null ? "" : "/" + state.Date),
                IdentitiesSha256([]), NoText, null, [], [], 0));
        }

        return schema;
    }

    private static IReadOnlyList<string> Dispositions(LexCorpus6Member member, params LexCorpus6Stage3OutcomeDomain[] domains) =>
        member.Stage3Outcomes
            .Where(outcome => domains.Contains(outcome.Domain))
            .Select(static outcome => ContractWire.NameOf(outcome.Disposition))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string IdentitiesSha256(IEnumerable<string> identities)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("lex-v3-release-comparison-wording/1\n"u8);
        foreach (var identity in identities.Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(identity + "\n"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string TextSha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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
        // Sorts stay in memory: the inventory writes nothing beside the mount it reads.
        memory.CommandText = "PRAGMA temp_store = MEMORY";
        memory.ExecuteNonQuery();
        return connection;
    }

    private static string Stamp(SqliteConnection connection, string corpusSha256, string which)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_identity, corpus_sha256 FROM stamp WHERE stamp_id = 1";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidDataException($"The {which} index holds no stamp.");
        }

        if (!string.Equals(reader.GetString(1), corpusSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The {which} index binds another corpus than the mount's.");
        }

        return reader.GetString(0);
    }

    private static HashSet<string> Columns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}

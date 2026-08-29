using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Temporal;

namespace Lex.Derive;

/// <summary>
/// One non-self-referential provenance manifest for the complete articles repository. The
/// articles Git commit binds this file; this file therefore never attempts to contain that same
/// commit. Each publisher entry instead binds the evidence commit and the exact derivation inputs.
/// </summary>
public static class DerivationGeneration
{
    public const string SchemaId = "lex-articles-generation/4";
    public const string PreviousSchemaId = "lex-articles-generation/3";
    public const string Canon1 = "canon/1";
    public const string Canon2 = "canon/2";
    public const string FileName = "generation.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed record Entry(
        string Collection,
        string CorpusRepository,
        string CorpusCommit,
        string CorpusManifestSha256,
        string IngesterCodeCommit,
        string DeriverCodeCommit,
        string DeriverTreeId,
        IReadOnlyList<string> Profiles,
        string ProfilesSha256,
        string ArticlesCanon);

    private sealed record Document(
        string Schema,
        Dictionary<string, Entry> Entries);

    internal static void UpdatePublisherWithLocksHeld(
        string articlesRoot,
        string publisher,
        string corpusCommit,
        string corpusManifestSha256,
        string ingesterCodeCommit,
        string deriverCodeCommit,
        string deriverTreeId,
        IEnumerable<string> profiles,
        string articlesCanon = Canon1)
    {
        var bytes = RenderPublisherUpdate(
            articlesRoot, publisher, corpusCommit, corpusManifestSha256,
            ingesterCodeCommit, deriverCodeCommit, deriverTreeId, profiles,
            articlesCanon);
        Directory.CreateDirectory(articlesRoot);
        WriteAtomically(Path.Combine(articlesRoot, FileName), bytes);
    }

    internal static byte[] RenderPublisherUpdate(
        string articlesRoot,
        string publisher,
        string corpusCommit,
        string corpusManifestSha256,
        string ingesterCodeCommit,
        string deriverCodeCommit,
        string deriverTreeId,
        IEnumerable<string> profiles,
        string articlesCanon = Canon1)
    {
        publisher = Required(publisher, "publisher");
        articlesCanon = RequireArticlesCanon(articlesCanon);
        var sortedProfiles = profiles.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (sortedProfiles.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Derivation profile identities cannot be empty.");
        var entry = Validate(new Entry(
            publisher,
            $"lex-corpus-{publisher}",
            CodeIdentity.RequireFullCommit(corpusCommit, "generation corpus_commit"),
            CodeIdentity.RequireSha256(
                corpusManifestSha256, "generation corpus_manifest_sha256"),
            CodeIdentity.RequireFullCommit(
                ingesterCodeCommit, "generation ingester_code_commit"),
            CodeIdentity.RequireFullCommit(
                deriverCodeCommit, "generation deriver_code_commit"),
            CodeIdentity.RequireFullGitObjectId(
                deriverTreeId, "generation deriver_tree_id"),
            sortedProfiles,
            ProfileDigest(sortedProfiles),
            articlesCanon), publisher);

        var path = Path.Combine(articlesRoot, FileName);
        var existing = File.Exists(path)
            ? ReadAll(path)
            : new Document(PreviousSchemaId,
                new Dictionary<string, Entry>(StringComparer.Ordinal));
        var entries = existing.Entries;
        RequireAllowedTransition(entries, publisher, articlesCanon);
        entries[publisher] = entry;
        var schema = existing.Schema == SchemaId || articlesCanon == Canon2
            ? SchemaId
            : PreviousSchemaId;

        var publishers = new JsonObject();
        foreach (var (key, value) in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
            publishers[key] = ToJson(value, includeArticlesCanon: schema == SchemaId);
        var document = new JsonObject
        {
            ["schema"] = schema,
            ["publishers"] = publishers,
        };
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(document.ToJsonString(JsonOptions) + "\n");
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 4 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    internal static FileStream AcquirePublisherLock(
        string articlesRoot,
        string publisher)
    {
        publisher = Required(publisher, "publisher");
        if (!IsPortablePublisherSegment(publisher))
            throw new InvalidDataException(
                "Publisher identity must be one path segment.");

        var root = ResolvedRoot(articlesRoot);
        var publisherDirectory = Path.GetFullPath(Path.Combine(root, publisher));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(publisherDirectory), root,
                pathComparison))
            throw new InvalidDataException(
                "Publisher identity must be one path segment below the articles root.");
        return AcquireLock(root, publisherDirectory, "publisher",
            $"Publisher derivation already active for '{publisher}'.");
    }

    internal static string RequirePublisherSegment(string value)
    {
        value = Required(value, "publisher");
        if (!IsPortablePublisherSegment(value))
            throw new InvalidDataException(
                "Publisher identity must be one path segment.");
        return value;
    }

    private static bool IsPortablePublisherSegment(string value)
    {
        if (value.Length > 128
            || value[0] == '-'
            || value[^1] == '-'
            || value.Any(character => character is not (>= 'a' and <= 'z')
                && character is not (>= '0' and <= '9')
                && character != '-'))
            return false;

        return value.ToUpperInvariant() is not (
            "CON" or "PRN" or "AUX" or "NUL"
            or "COM1" or "COM2" or "COM3" or "COM4" or "COM5"
            or "COM6" or "COM7" or "COM8" or "COM9"
            or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5"
            or "LPT6" or "LPT7" or "LPT8" or "LPT9");
    }

    internal static FileStream AcquireGenerationLock(string articlesRoot)
    {
        var root = ResolvedRoot(articlesRoot);
        return AcquireLock(root, root, "generation",
            "Derivation generation update already active.");
    }

    private static FileStream AcquireLock(
        string articlesRoot,
        string identity,
        string kind,
        string busyMessage)
    {
        var parent = Directory.GetParent(articlesRoot)?.FullName
            ?? throw new InvalidDataException(
                "Articles root must have a parent directory for derivation locks.");
        Directory.CreateDirectory(parent);
        var normalizedIdentity = OperatingSystem.IsWindows()
            ? identity.ToUpperInvariant()
            : identity;
        var digest = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(normalizedIdentity)));
        var path = Path.Combine(parent, $".lex-derive-{kind}-{digest}.lock");
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException error)
        {
            throw new InvalidOperationException(busyMessage, error);
        }
    }

    internal static string ResolvedRoot(string articlesRoot)
    {
        if (string.IsNullOrWhiteSpace(articlesRoot))
            throw new ArgumentException("Articles root is required.", nameof(articlesRoot));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(articlesRoot));
    }

    internal static void EnsurePublisherTransition(
        string articlesRoot, string publisher, string articlesCanon)
    {
        publisher = Required(publisher, "publisher");
        articlesCanon = RequireArticlesCanon(articlesCanon);
        var publisherDirectory = Path.Combine(articlesRoot, publisher);
        var path = Path.Combine(articlesRoot, FileName);
        if (!File.Exists(path))
        {
            if (Directory.Exists(publisherDirectory))
                throw new InvalidDataException(
                    $"Publisher '{publisher}' output exists without derivation generation evidence.");
            return;
        }

        var entries = ReadAll(path).Entries;
        if (!entries.ContainsKey(publisher))
        {
            if (Directory.Exists(publisherDirectory))
                throw new InvalidDataException(
                    $"Publisher '{publisher}' output exists without a matching derivation generation entry.");
            return;
        }
        if (!Directory.Exists(publisherDirectory))
            throw new InvalidDataException(
                $"Publisher '{publisher}' generation evidence exists without accepted output.");
        RequireAllowedTransition(entries, publisher, articlesCanon);
    }

    private static void RequireAllowedTransition(
        IReadOnlyDictionary<string, Entry> entries,
        string publisher,
        string articlesCanon)
    {
        if (entries.TryGetValue(publisher, out var previous)
            && previous.ArticlesCanon == Canon2
            && articlesCanon == Canon1)
            throw new InvalidDataException(
                $"Publisher '{publisher}' cannot be downgraded from '{Canon2}' to '{Canon1}' without a separately reviewed rollback mechanism.");
    }

    public static Entry ReadPublisher(string articlesRoot, string publisher)
    {
        var path = Path.Combine(articlesRoot, FileName);
        var document = ReadAll(path);
        if (!document.Entries.TryGetValue(publisher, out var entry))
            throw new InvalidDataException(
                $"Derivation generation has no publisher entry for '{publisher}'.");
        return entry;
    }

    public static string Sha256File(string path) => Convert.ToHexStringLower(
        SHA256.HashData(File.ReadAllBytes(path)));

    public static string ProfileDigest(IEnumerable<string> profiles)
    {
        var canonical = string.Join('\n', profiles.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)) + "\n";
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static Document ReadAll(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"Derivation generation manifest is missing: {path}");
        var text = File.ReadAllText(path);
        JsonObject root;
        try
        {
            using var parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("The document is not an object.");
            RequireNoDuplicateProperties(parsed.RootElement,
                "derivation generation");
            root = JsonNode.Parse(text) as JsonObject
                ?? throw new JsonException("The document is not an object.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Derivation generation manifest cannot be parsed: {path}", error);
        }
        var schema = root["schema"]?.GetValue<string>();
        if (schema is not (SchemaId or PreviousSchemaId))
            throw new InvalidDataException(
                $"Derivation generation schema must be '{PreviousSchemaId}' or '{SchemaId}'.");
        if (schema == SchemaId)
            RequireExactProperties(root, "derivation generation",
                "schema", "publishers");
        if (root.ContainsKey("articles_commit"))
            throw new InvalidDataException(
                "Derivation generation must not contain its own articles commit.");
        var publishers = root["publishers"] as JsonObject
            ?? throw new InvalidDataException(
                "Derivation generation publishers must be an object.");
        var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var item in publishers)
        {
            if (item.Value is not JsonObject value)
                throw new InvalidDataException(
                    $"Derivation generation publisher '{item.Key}' is not an object.");
            result.Add(item.Key, Parse(value, item.Key, schema));
        }
        return new Document(schema, result);
    }

    private static Entry Parse(JsonObject value, string key, string schema)
    {
        if (schema == SchemaId)
            RequireExactProperties(value,
                $"derivation generation publisher '{key}'",
                "collection", "corpus_repository", "corpus_commit",
                "corpus_manifest_sha256", "ingester_code_commit",
                "deriver_code_commit", "deriver_tree_id", "profiles",
                "profiles_sha256", "articles_canon");
        else if (value.ContainsKey("articles_canon"))
            throw new InvalidDataException(
                $"Derivation generation schema '{PreviousSchemaId}' cannot contain articles_canon.");
        if (value.ContainsKey("articles_commit"))
            throw new InvalidDataException(
                "Derivation generation publisher entries must not contain the articles commit.");
        var profiles = value["profiles"] as JsonArray
            ?? throw new InvalidDataException(
                $"Derivation generation profiles are missing for '{key}'.");
        var parsedProfiles = profiles.Select(item =>
                item?.GetValue<string>()
                ?? throw new InvalidDataException(
                    $"Derivation generation contains a non-string profile for '{key}'."))
            .ToArray();
        return Validate(new Entry(
            Required(value["collection"]?.GetValue<string>(), "generation collection"),
            Required(value["corpus_repository"]?.GetValue<string>(),
                "generation corpus_repository"),
            value["corpus_commit"]?.GetValue<string>() ?? "",
            value["corpus_manifest_sha256"]?.GetValue<string>() ?? "",
            value["ingester_code_commit"]?.GetValue<string>() ?? "",
            value["deriver_code_commit"]?.GetValue<string>() ?? "",
            value["deriver_tree_id"]?.GetValue<string>() ?? "",
            parsedProfiles,
            value["profiles_sha256"]?.GetValue<string>() ?? "",
            schema == SchemaId
                ? value["articles_canon"]?.GetValue<string>() ?? ""
                : Canon1), key);
    }

    private static Entry Validate(Entry entry, string key)
    {
        if (!string.Equals(entry.Collection, key, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Derivation generation collection '{entry.Collection}' does not match map key '{key}'.");
        if (!string.Equals(entry.CorpusRepository, $"lex-corpus-{key}",
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Derivation generation corpus repository does not match publisher '{key}'.");
        CodeIdentity.RequireFullCommit(entry.CorpusCommit, "generation corpus_commit");
        CodeIdentity.RequireSha256(
            entry.CorpusManifestSha256, "generation corpus_manifest_sha256");
        CodeIdentity.RequireFullCommit(
            entry.IngesterCodeCommit, "generation ingester_code_commit");
        CodeIdentity.RequireFullCommit(
            entry.DeriverCodeCommit, "generation deriver_code_commit");
        CodeIdentity.RequireFullGitObjectId(
            entry.DeriverTreeId, "generation deriver_tree_id");
        var canonicalProfiles = entry.Profiles.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (!entry.Profiles.SequenceEqual(canonicalProfiles, StringComparer.Ordinal)
            || canonicalProfiles.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException(
                $"Derivation generation profiles for '{key}' must be sorted, unique, and non-empty.");
        var expectedProfileDigest = ProfileDigest(canonicalProfiles);
        if (!string.Equals(entry.ProfilesSha256, expectedProfileDigest,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Derivation generation profile digest mismatch for '{key}'.");
        RequireArticlesCanon(entry.ArticlesCanon);
        return entry;
    }

    private static JsonObject ToJson(Entry entry, bool includeArticlesCanon)
    {
        var value = new JsonObject
        {
            ["collection"] = entry.Collection,
            ["corpus_repository"] = entry.CorpusRepository,
            ["corpus_commit"] = entry.CorpusCommit,
            ["corpus_manifest_sha256"] = entry.CorpusManifestSha256,
            ["ingester_code_commit"] = entry.IngesterCodeCommit,
            ["deriver_code_commit"] = entry.DeriverCodeCommit,
            ["deriver_tree_id"] = entry.DeriverTreeId,
            ["profiles"] = new JsonArray(entry.Profiles
                .Select(profile => (JsonNode)profile).ToArray()),
            ["profiles_sha256"] = entry.ProfilesSha256,
        };
        if (includeArticlesCanon)
            value["articles_canon"] = entry.ArticlesCanon;
        return value;
    }

    public static string RequireArticlesCanon(string? value) =>
        RequireArticlesCanon(value, "articles_canon");

    public static string RequireArticlesCanon(string? value, string label) => value switch
    {
        Canon1 => Canon1,
        Canon2 => Canon2,
        _ => throw new InvalidDataException(
            $"{label} must be '{Canon1}' or '{Canon2}'."),
    };

    private static void RequireExactProperties(
        JsonObject value,
        string label,
        params string[] expected)
    {
        var actual = value.Select(property => property.Key)
            .Order(StringComparer.Ordinal).ToArray();
        var canonical = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(canonical, StringComparer.Ordinal))
        {
            var missing = canonical.Except(actual, StringComparer.Ordinal).ToArray();
            var unexpected = actual.Except(canonical, StringComparer.Ordinal).ToArray();
            throw new InvalidDataException(
                $"{label} has an invalid property set; missing={string.Join(',', missing)}; unexpected={string.Join(',', unexpected)}.");
        }
    }

    private static void RequireNoDuplicateProperties(JsonElement value, string label)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException(
                        $"{label} contains duplicate property '{property.Name}'.");
                RequireNoDuplicateProperties(property.Value, label);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                RequireNoDuplicateProperties(item, label);
        }
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{field} is required.")
            : value;
}

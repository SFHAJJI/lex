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
    public const string SchemaId = "lex-articles-generation/2";
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
        string ReviewedConfigurationSha256,
        IReadOnlyList<string> Profiles,
        string ProfilesSha256);

    public static void UpdatePublisher(
        string articlesRoot,
        string publisher,
        string corpusCommit,
        string corpusManifestSha256,
        string ingesterCodeCommit,
        string deriverCodeCommit,
        string deriverTreeId,
        string reviewedConfigurationSha256,
        IEnumerable<string> profiles)
    {
        publisher = Required(publisher, "publisher");
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
            CodeIdentity.RequireSha256(
                reviewedConfigurationSha256,
                "generation reviewed_configuration_sha256"),
            sortedProfiles,
            ProfileDigest(sortedProfiles)), publisher);

        Directory.CreateDirectory(articlesRoot);
        var path = Path.Combine(articlesRoot, FileName);
        var entries = File.Exists(path)
            ? ReadAll(path)
            : new Dictionary<string, Entry>(StringComparer.Ordinal);
        entries[publisher] = entry;

        var publishers = new JsonObject();
        foreach (var (key, value) in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
            publishers[key] = ToJson(value);
        var document = new JsonObject
        {
            ["schema"] = SchemaId,
            ["publishers"] = publishers,
        };
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, document.ToJsonString(JsonOptions) + "\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public static Entry ReadPublisher(string articlesRoot, string publisher)
    {
        var path = Path.Combine(articlesRoot, FileName);
        var entries = ReadAll(path);
        if (!entries.TryGetValue(publisher, out var entry))
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

    private static Dictionary<string, Entry> ReadAll(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"Derivation generation manifest is missing: {path}");
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new JsonException("The document is not an object.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Derivation generation manifest cannot be parsed: {path}", error);
        }
        if (root["schema"]?.GetValue<string>() != SchemaId)
            throw new InvalidDataException(
                $"Derivation generation schema must be '{SchemaId}'.");
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
            result.Add(item.Key, Parse(value, item.Key));
        }
        return result;
    }

    private static Entry Parse(JsonObject value, string key)
    {
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
            value["reviewed_configuration_sha256"]?.GetValue<string>() ?? "",
            parsedProfiles,
            value["profiles_sha256"]?.GetValue<string>() ?? ""), key);
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
        CodeIdentity.RequireSha256(
            entry.ReviewedConfigurationSha256,
            "generation reviewed_configuration_sha256");
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
        return entry;
    }

    private static JsonObject ToJson(Entry entry) => new()
    {
        ["collection"] = entry.Collection,
        ["corpus_repository"] = entry.CorpusRepository,
        ["corpus_commit"] = entry.CorpusCommit,
        ["corpus_manifest_sha256"] = entry.CorpusManifestSha256,
        ["ingester_code_commit"] = entry.IngesterCodeCommit,
        ["deriver_code_commit"] = entry.DeriverCodeCommit,
        ["deriver_tree_id"] = entry.DeriverTreeId,
        ["reviewed_configuration_sha256"] = entry.ReviewedConfigurationSha256,
        ["profiles"] = new JsonArray(entry.Profiles.Select(profile => (JsonNode)profile).ToArray()),
        ["profiles_sha256"] = entry.ProfilesSha256,
    };

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{field} is required.")
            : value;
}

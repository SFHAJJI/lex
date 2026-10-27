using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Releases;

/// <summary>
/// One typed reason a release gives for changes the release-over-release check finds (R106: a change "listed in the pull
/// request"), as the release's explanations file states it. It covers either the items it names, by the exact keys the
/// comparison lists, or, for <c>derivation_rule_changed</c> on Luxembourg, every version and article whose rule profile moved
/// from <see cref="FromRuleProfileSha256"/> to <see cref="ToRuleProfileSha256"/>, or, for <c>rights_withdrawn</c>, every
/// version the candidate withholds under one of <see cref="Dispositions"/>. Each scope is checked against the data, never
/// taken on trust: an entry covers only what the two mounts show it covers.
/// </summary>
public sealed record V3ReleaseChangeExplanation(
    int Index,
    string Reason,
    string Publisher,
    string Explanation,
    string Reference,
    IReadOnlyList<string> Items,
    string? FromRuleProfileSha256,
    string? ToRuleProfileSha256,
    IReadOnlyList<string> Dispositions);

/// <summary>
/// One typed, explained textless outcome a release gives for a consolidation file that R86(2) lists as a failure: the file
/// by its publisher and object reference, the outcome the data carries (which must be exactly the data's), why, and where it
/// is recorded.
/// </summary>
public sealed record V3TextlessExplanation(
    int Index,
    string Publisher,
    string ObjectRefSha256,
    string Outcome,
    string Explanation,
    string Reference);

/// <summary>
/// A release's explanations file (<c>lex-v3-release-explanations/1</c>): the release serving now that it is compared with,
/// the typed reasons for the changes it makes (R106), and the typed textless outcomes it explains (R86(2)). Published as a
/// release asset, so the reasons travel with the release they explain, under its signature. Read strictly: an unknown member,
/// reason or publisher, an entry with no explanation or reference, or an entry whose scope is not one the check can verify, is
/// <see cref="InvalidDataException"/>.
/// </summary>
public sealed record V3ReleaseExplanations(
    string ServingRelease,
    IReadOnlyList<V3ReleaseChangeExplanation> Changes,
    IReadOnlyList<V3TextlessExplanation> Textless,
    string Sha256)
{
    /// <summary>The file's schema.</summary>
    public const string SchemaId = "lex-v3-release-explanations/1";

    /// <summary>A derivation rule changed: the text or identity follows the new rule.</summary>
    public const string DerivationRuleChanged = "derivation_rule_changed";

    /// <summary>A rights rule or policy now withholds text the serving release served.</summary>
    public const string RightsWithdrawn = "rights_withdrawn";

    /// <summary>The publisher no longer lists or serves what the serving release held.</summary>
    public const string WithdrawnByPublisher = "withdrawn_by_publisher";

    /// <summary>The serving release's text was wrong, and the candidate corrects it.</summary>
    public const string DefectCorrected = "defect_corrected";

    /// <summary>The reasons an entry may give, in a closed list.</summary>
    public static IReadOnlyList<string> Reasons { get; } = [DerivationRuleChanged, RightsWithdrawn, WithdrawnByPublisher, DefectCorrected];

    /// <summary>The Luxembourg rights dispositions by their wire names, the tokens a withheld version's reason starts with.</summary>
    private static readonly HashSet<string> LuxembourgRightsDispositions = typeof(LuxembourgRightsChannelDisposition)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(static field => field.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute), false)
            .Cast<System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute>().SingleOrDefault()?.Name)
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    private static bool IsToken(string value) =>
        value.Length > 0 && value[0] is >= 'a' and <= 'z' && value.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    /// <summary>Reads and checks an explanations file.</summary>
    public static V3ReleaseExplanations Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The release's explanations are not JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            Only(root, "the explanations", "schema", "serving_release", "changes", "textless");
            if (Text(root, "schema", "the explanations") != SchemaId)
            {
                throw new InvalidDataException($"The release's explanations are not {SchemaId}.");
            }

            var serving = Text(root, "serving_release", "the explanations");
            var changes = new List<V3ReleaseChangeExplanation>();
            var index = 0;
            foreach (var entry in Entries(root, "changes"))
            {
                var where = $"changes[{index}]";
                Only(entry, where, "reason", "publisher", "explanation", "reference", "items", "from_rule_profile_sha256", "to_rule_profile_sha256", "dispositions");
                var reason = Text(entry, "reason", where);
                if (!Reasons.Contains(reason, StringComparer.Ordinal))
                {
                    throw new InvalidDataException($"{where} gives the reason {reason}, which is not one of {string.Join(", ", Reasons)}.");
                }

                var publisher = Publisher(entry, where);
                var items = Strings(entry, "items", where);
                var from = OptionalDigest(entry, "from_rule_profile_sha256", where);
                var to = OptionalDigest(entry, "to_rule_profile_sha256", where);
                var dispositions = Strings(entry, "dispositions", where);
                var byProfiles = from is not null || to is not null;
                if (byProfiles && (reason != DerivationRuleChanged || publisher != V3MountInventory.Luxembourg || from is null || to is null || from == to))
                {
                    throw new InvalidDataException($"{where}: a scope by rule profiles is a Luxembourg derivation_rule_changed naming two different profiles.");
                }

                // A disposition is a whole rights token the data can carry, never a fragment (the review of #1002, F2).
                foreach (var disposition in dispositions)
                {
                    if (publisher == V3MountInventory.Luxembourg ? !LuxembourgRightsDispositions.Contains(disposition) : !IsToken(disposition))
                    {
                        throw new InvalidDataException($"{where} names the disposition {disposition}, which is not a rights disposition of {publisher}.");
                    }
                }

                if (dispositions.Count > 0 && reason != RightsWithdrawn)
                {
                    throw new InvalidDataException($"{where}: a scope by rights dispositions is a rights_withdrawn entry's.");
                }

                var scopes = (items.Count > 0 ? 1 : 0) + (byProfiles ? 1 : 0) + (dispositions.Count > 0 ? 1 : 0);
                if (scopes != 1)
                {
                    throw new InvalidDataException($"{where} names exactly one scope: items, two rule profiles, or rights dispositions.");
                }

                changes.Add(new V3ReleaseChangeExplanation(
                    index, reason, publisher, Text(entry, "explanation", where), Text(entry, "reference", where), items, from, to, dispositions));
                index++;
            }

            var textless = new List<V3TextlessExplanation>();
            index = 0;
            foreach (var entry in Entries(root, "textless"))
            {
                var where = $"textless[{index}]";
                Only(entry, where, "publisher", "object_ref_sha256", "outcome", "explanation", "reference");
                var objectRef = OptionalDigest(entry, "object_ref_sha256", where)
                    ?? throw new InvalidDataException($"{where} names no object_ref_sha256.");
                textless.Add(new V3TextlessExplanation(
                    index, Publisher(entry, where), objectRef, Text(entry, "outcome", where), Text(entry, "explanation", where), Text(entry, "reference", where)));
                index++;
            }

            return new V3ReleaseExplanations(serving, changes, textless, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }

    private static void Only(JsonElement element, string where, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{where} is not an object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"{where} carries {property.Name}, which the explanations do not have.");
            }
        }
    }

    private static string Text(JsonElement element, string name, string where) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()!.Trim().Length > 0
            ? value.GetString()!
            : throw new InvalidDataException($"{where} carries no {name}.");

    private static string Publisher(JsonElement element, string where)
    {
        var publisher = Text(element, "publisher", where);
        return publisher is V3MountInventory.Luxembourg or V3MountInventory.Europe
            ? publisher
            : throw new InvalidDataException($"{where} names the publisher {publisher}, which is neither {V3MountInventory.Luxembourg} nor {V3MountInventory.Europe}.");
    }

    private static IEnumerable<JsonElement> Entries(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return [];
        }

        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : throw new InvalidDataException($"The explanations' {name} is not a list.");
    }

    private static IReadOnlyList<string> Strings(JsonElement element, string name, string where)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(static item => item.ValueKind != JsonValueKind.String || item.GetString()!.Length == 0))
        {
            throw new InvalidDataException($"{where}'s {name} is not a list of names.");
        }

        var listed = value.EnumerateArray().Select(static item => item.GetString()!).ToArray();
        return listed.Distinct(StringComparer.Ordinal).Count() == listed.Length
            ? listed
            : throw new InvalidDataException($"{where}'s {name} names one twice.");
    }

    private static string? OptionalDigest(JsonElement element, string name, string where)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        return text.Length == 64 && text.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'))
            ? text
            : throw new InvalidDataException($"{where}'s {name} is not a SHA-256 digest.");
    }
}

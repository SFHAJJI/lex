using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>One entry of the handoff registry (R42), as the committed file holds it.</summary>
public sealed record V3HandoffEntry(
    string Id,
    string Theme,
    string Name,
    string Link,
    V3HandoffRole Role,
    string IntroducedAs,
    string? Cost,
    string? Limits,
    IReadOnlyList<string>? Languages,
    string? LinkCheckedOn,
    string Review)
{
    /// <summary>The words a person writes in the review field once they have reviewed the entry; no entry carries them today.</summary>
    public const string NotYetReviewed = "not yet reviewed by a person";

    /// <summary>Whether a person reviewed this entry. False for every entry today.</summary>
    public bool ReviewedByPerson => !string.Equals(Review, NotYetReviewed, StringComparison.Ordinal);
}

/// <summary>
/// The one versioned handoff registry by theme (R42), <c>lex-v3-handoff-registry/1</c>, read from the bytes committed beside
/// this file (<c>handoff-registry.json</c>, with its schema <c>handoff-registry.schema.json</c>). It holds only the entries
/// the specification or the repository already names, every one marked "not yet reviewed by a person", and no adviser.
/// </summary>
/// <remarks>
/// The assistant uses it, and the assistant is switched off; Get help does not use it, and says that Lex names no adviser
/// yet. Nothing about a user is ever sent to an entry's link. The weekly link check R42 asks for is a request to a third
/// party and waits for the run that is authorised to make it; <c>link_checked_on</c> is null until then.
/// </remarks>
public static class V3HandoffRegistry
{
    public const string Schema = "lex-v3-handoff-registry/1";
    internal const string Resource = "Lex.V3.Contracts.Assistant.HandoffRegistry.json";
    internal const string SchemaResource = "Lex.V3.Contracts.Assistant.HandoffRegistry.schema.json";

    private static readonly byte[] Bytes = ReadResource(Resource);

    /// <summary>The committed file's bytes, as the trace's digest covers them.</summary>
    public static ReadOnlyMemory<byte> Utf8 => Bytes;

    /// <summary>The committed schema's bytes.</summary>
    public static ReadOnlyMemory<byte> SchemaUtf8 { get; } = ReadResource(SchemaResource);

    /// <summary>The SHA-256 of the committed file, which every trace records.</summary>
    public static string Sha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(Bytes));

    private static readonly (int Version, ReadOnlyCollection<V3HandoffEntry> Entries) Loaded = Load(Bytes);

    public static int Version => Loaded.Version;

    public static ReadOnlyCollection<V3HandoffEntry> Entries => Loaded.Entries;

    /// <summary>Whether a person reviewed every entry. False today; the assistant's switch cannot take effect while it is false.</summary>
    public static bool ReviewedByPerson => Entries.All(static entry => entry.ReviewedByPerson);

    /// <summary>The registry's entries of a role, in file order.</summary>
    public static IReadOnlyList<V3HandoffEntry> OfRole(V3HandoffRole role) =>
        Entries.Where(entry => entry.Role == role).ToArray();

    public static V3HandoffEntry Get(string id) =>
        Entries.SingleOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal))
        ?? throw new ArgumentException($"The handoff registry has no entry '{id}'.", nameof(id));

    private static (int Version, ReadOnlyCollection<V3HandoffEntry> Entries) Load(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false, AllowDuplicateProperties = false });
        var root = document.RootElement;
        if (!string.Equals(root.GetProperty("schema").GetString(), Schema, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The handoff registry is not lex-v3-handoff-registry/1.");
        }

        var version = root.GetProperty("version").GetInt32();
        var entries = new List<V3HandoffEntry>();
        foreach (var entry in root.GetProperty("entries").EnumerateArray())
        {
            var role = entry.GetProperty("role").GetString() switch
            {
                "official_source" => V3HandoffRole.OfficialSource,
                "information" => V3HandoffRole.Information,
                "advice" => V3HandoffRole.Advice,
                "complaint" => V3HandoffRole.Complaint,
                "mediation" => V3HandoffRole.Mediation,
                var other => throw new InvalidDataException($"The handoff registry names the role '{other}', outside the closed set."),
            };
            var link = entry.GetProperty("link").GetString()!;
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException($"The handoff registry's link '{link}' is not an https address.");
            }

            entries.Add(new V3HandoffEntry(
                entry.GetProperty("id").GetString()!,
                entry.GetProperty("theme").GetString()!,
                entry.GetProperty("name").GetString()!,
                link,
                role,
                entry.GetProperty("introduced_as").GetString()!,
                NullableString(entry, "cost"),
                NullableString(entry, "limits"),
                entry.GetProperty("languages").ValueKind == JsonValueKind.Null
                    ? null
                    : entry.GetProperty("languages").EnumerateArray().Select(static language => language.GetString()!).ToArray(),
                NullableString(entry, "link_checked_on"),
                entry.GetProperty("review").GetString()!));
        }

        if (entries.Select(static entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            throw new InvalidDataException("The handoff registry names an entry twice.");
        }

        return (version, entries.AsReadOnly());
    }

    private static string? NullableString(JsonElement entry, string name) =>
        entry.GetProperty(name).ValueKind == JsonValueKind.Null ? null : entry.GetProperty(name).GetString();

    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(V3HandoffRegistry).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource {name} is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

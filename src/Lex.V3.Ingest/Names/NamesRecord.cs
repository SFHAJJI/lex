using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lex.V3.Ingest.Names;

/// <summary>One work R92's measurement withholds: who it is, why, and the hashes that stay checkable.</summary>
/// <param name="Publisher"><c>lu-legilux</c> or <c>eu-eurlex</c>.</param>
/// <param name="WorkIri">The publisher's work IRI: a Luxembourg act's ELI work, or an EU act's Cellar work.</param>
/// <param name="WorkKey">A Luxembourg work the index holds a version of: its work key; else null.</param>
/// <param name="Celex">An EU act: its CELEX (the act's own, sector 1 or 3); else null.</param>
/// <param name="Class">Its class (<see cref="PrivateIndividuals.Classes"/>).</param>
/// <param name="Evidence">The reason codes that withheld it, in ordinal order (<see cref="PrivateIndividuals.ReasonCodes"/>).</param>
/// <param name="StateSha256s">The digests of the states the index holds of it, in ordinal order (none for an act it holds no version of).</param>
public sealed record NamesWithheldWork(
    string Publisher,
    string WorkIri,
    string? WorkKey,
    string? Celex,
    string Class,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> StateSha256s);

/// <summary>
/// What a names record was measured over: the mount's corpus and index builds, which it is bound to by their digests, and the
/// build time of the Luxembourg index it read (the index's own, so no binding of its own; null for a mount with no Luxembourg
/// index).
/// </summary>
public sealed record NamesMeasuredMount(
    string CorpusSha256,
    string? LuxembourgIndexSha256,
    string? EuropeIndexSha256,
    string? BuiltAt);

/// <summary>
/// The names record beside a mount (<c>names-record.json</c>, schema <c>lex-v3-names-record/1</c>): R92's measurement's
/// withheld works, bound to the mount's corpus and index builds. The API reads it when the mount opens and withholds each
/// work it lists on every operation that would show its text or titles; a record that breaks any rule, or that names another
/// build than the mount's, refuses the mount whole (<see cref="Read"/>). It holds identifiers, classes, reason codes and
/// hashes only: no name and no other personal data.
/// </summary>
public static class NamesRecord
{
    public const string FileName = "names-record.json";

    public const string Schema = "lex-v3-names-record/1";

    public const string LuxembourgPublisher = "lu-legilux";

    public const string EuropePublisher = "eu-eurlex";

    /// <summary>What the record says it does, in its own words (a fixed sentence, so the bytes are the measurement's alone).</summary>
    public const string Rule =
        "R92(1): every work listed names private individuals, by its class or by what the detector found in its served text or " +
        "titles; Lex points to the publisher: its text and titles are not served, searched, quoted or exported, it is kept out of " +
        "search engines, and its identity, hashes and official link stay (R92(2))";

    private static readonly Regex Sha256 = new("^[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);

    private static readonly Regex WorkKeyShape = new("^[a-z0-9][a-z0-9_-]*\\z", RegexOptions.CultureInvariant);

    private static readonly Regex CelexShape = new("^[0-9A-Za-z][0-9A-Za-z()./_-]*\\z", RegexOptions.CultureInvariant);

    private static readonly Regex LuxembourgWork = new("^http://data\\.legilux\\.public\\.lu/eli/[0-9A-Za-z_./-]+\\z", RegexOptions.CultureInvariant);

    private static readonly Regex EuropeWork = new("^http://publications\\.europa\\.eu/resource/(?:cellar|celex)/[0-9A-Za-z()._-]+\\z", RegexOptions.CultureInvariant);

    private static readonly Regex UtcInstant = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\\z", RegexOptions.CultureInvariant);

    /// <summary>The record as UTF-8 bytes: one form for one measurement (LF line ends, two-space indents, fixed member order).</summary>
    public static byte[] Write(NamesMeasuredMount measured, IReadOnlyList<NamesWithheldWork> works)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(works);
        var ordered = works.OrderBy(static work => work.Publisher, StringComparer.Ordinal)
            .ThenBy(static work => work.WorkIri, StringComparer.Ordinal).ToArray();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("measure", PrivateIndividuals.MeasureVersion);
            writer.WriteString("rule", Rule);
            writer.WriteStartObject("measured");
            writer.WriteString("corpus_sha256", measured.CorpusSha256);
            WriteNullable(writer, "luxembourg_index_sha256", measured.LuxembourgIndexSha256);
            WriteNullable(writer, "europe_index_sha256", measured.EuropeIndexSha256);
            WriteNullable(writer, "built_at", measured.BuiltAt);
            writer.WriteEndObject();
            writer.WriteStartArray("works");
            foreach (var work in ordered)
            {
                writer.WriteStartObject();
                writer.WriteString("publisher", work.Publisher);
                writer.WriteString("work_iri", work.WorkIri);
                WriteNullable(writer, "work_key", work.WorkKey);
                WriteNullable(writer, "celex", work.Celex);
                writer.WriteString("class", work.Class);
                writer.WriteString("reason", PrivateIndividuals.Reason);
                WriteStrings(writer, "evidence", work.Evidence.Order(StringComparer.Ordinal));
                WriteStrings(writer, "state_sha256s", work.StateSha256s.Order(StringComparer.Ordinal));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        var bytes = stream.ToArray();
        // What is written is what is read: a writer that could emit a record its reader refuses would refuse its own mount.
        _ = Read(bytes, measured);
        return bytes;
    }

    /// <summary>
    /// The record in <paramref name="bytes"/>, held to the mount it stands beside (<paramref name="mount"/>: its corpus and
    /// index builds), or throws <see cref="InvalidDataException"/> naming the first rule it breaks: the schema and measure
    /// versions; the measured corpus and index digests (64 hex digits, an index's null exactly when the mount holds none) equal
    /// to the mount's, and the build time an instant in UTC or null; each work's publisher, work IRI (a Luxembourg ELI IRI
    /// or an EU Cellar or CELEX work), work key (Luxembourg only, or null), CELEX (EU only, required), class (one of the
    /// publisher's), the one reason, its reason codes (at least one, in order, each once) and its state digests (in order, each
    /// once); the works in (publisher, work IRI) order, each once, and each work key and CELEX once; no member the schema does not
    /// name. Nothing is read leniently: a record that breaks a rule withholds nothing it could be trusted for.
    /// </summary>
    public static IReadOnlyList<NamesWithheldWork> Read(ReadOnlySpan<byte> bytes, NamesMeasuredMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        if (bytes.StartsWith("﻿"u8))
        {
            bytes = bytes[3..];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The names record is not JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            RequireObject(root, "the record", "schema", "measure", "rule", "measured", "works");
            RequireString(root, "schema", Schema);
            RequireString(root, "measure", PrivateIndividuals.MeasureVersion);
            RequireString(root, "rule", Rule);
            var measured = root.GetProperty("measured");
            RequireObject(measured, "measured", "corpus_sha256", "luxembourg_index_sha256", "europe_index_sha256", "built_at");
            var corpus = Digest(measured, "corpus_sha256", nullable: false);
            var luxembourg = Digest(measured, "luxembourg_index_sha256", nullable: true);
            var europe = Digest(measured, "europe_index_sha256", nullable: true);
            var builtAt = Nullable(measured, "built_at");
            if (builtAt is not null && !IsUtcInstant(builtAt))
            {
                throw new InvalidDataException("The names record's built_at is not an instant in UTC.");
            }

            // The build time is the measured index's own (it is inside the digest), so the digests alone bind the record.
            if (!string.Equals(corpus, mount.CorpusSha256, StringComparison.Ordinal) ||
                !string.Equals(luxembourg, mount.LuxembourgIndexSha256, StringComparison.Ordinal) ||
                !string.Equals(europe, mount.EuropeIndexSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The names record was measured over another build than the mount's: its corpus and index digests must be the mount's.");
            }

            var works = root.GetProperty("works");
            if (works.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The names record's works is not an array.");
            }

            var read = new List<NamesWithheldWork>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var work in works.EnumerateArray())
            {
                RequireObject(work, "a work", "publisher", "work_iri", "work_key", "celex", "class", "reason", "evidence", "state_sha256s");
                var publisher = work.GetProperty("publisher").ValueKind == JsonValueKind.String ? work.GetProperty("publisher").GetString()! : "";
                var europeWork = string.Equals(publisher, EuropePublisher, StringComparison.Ordinal);
                if (!europeWork && !string.Equals(publisher, LuxembourgPublisher, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A work's publisher is neither lu-legilux nor eu-eurlex.");
                }

                var workIri = RequiredString(work, "work_iri");
                if (europeWork ? !EuropeWork.IsMatch(workIri) : !LuxembourgWork.IsMatch(workIri))
                {
                    throw new InvalidDataException("A work's work_iri is not a work IRI of its publisher.");
                }

                var workKey = Nullable(work, "work_key");
                if (workKey is not null && (europeWork || !WorkKeyShape.IsMatch(workKey)))
                {
                    throw new InvalidDataException("A work's work_key is not a Luxembourg work key.");
                }

                var celex = Nullable(work, "celex");
                if (europeWork ? celex is null || !CelexShape.IsMatch(celex) : celex is not null)
                {
                    throw new InvalidDataException("A work's celex is not an EU act's CELEX, or is given for a Luxembourg act.");
                }

                var workClass = RequiredString(work, "class");
                if (!PrivateIndividuals.NamingClasses.Contains(workClass, StringComparer.Ordinal) ||
                    workClass.StartsWith("eu_", StringComparison.Ordinal) != europeWork)
                {
                    throw new InvalidDataException("A work's class is not one of its publisher's classes that name private individuals.");
                }

                RequireString(work, "reason", PrivateIndividuals.Reason);
                var evidence = OrderedStrings(work, "evidence", static code => PrivateIndividuals.ReasonCodes.Contains(code, StringComparer.Ordinal));
                if (evidence.Count == 0)
                {
                    throw new InvalidDataException("A work names no reason code.");
                }

                var states = OrderedStrings(work, "state_sha256s", static digest => Sha256.IsMatch(digest));
                if (read.Count > 0)
                {
                    var previous = read[^1];
                    var order = string.CompareOrdinal(previous.Publisher, publisher);
                    if (order > 0 || (order == 0 && string.CompareOrdinal(previous.WorkIri, workIri) >= 0))
                    {
                        throw new InvalidDataException("The names record's works are not in (publisher, work IRI) order, each once.");
                    }
                }

                foreach (var key in new[] { workKey is null ? null : "key:" + workKey, celex is null ? null : "celex:" + celex })
                {
                    if (key is not null && !keys.Add(key))
                    {
                        throw new InvalidDataException("The names record lists one work key or CELEX twice.");
                    }
                }

                read.Add(new NamesWithheldWork(publisher, workIri, workKey, celex, workClass, evidence, states));
            }

            return read.AsReadOnly();
        }
    }

    /// <summary>Whether a value is a real instant in UTC, written <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    public static bool IsUtcInstant(string value) =>
        UtcInstant.IsMatch(value) &&
        DateTime.TryParseExact(value, "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);

    /// <summary>The writer every record of R92's measurement is written with: indented, LF line ends, nothing escaped that JSON allows as is.</summary>
    internal static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    internal static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void RequireObject(JsonElement element, string what, params string[] members)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"The names record's {what} is not an object.");
        }

        var names = element.EnumerateObject().Select(static property => property.Name).ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count() ||
            !names.Order(StringComparer.Ordinal).SequenceEqual(members.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException($"The names record's {what} must hold exactly {string.Join(", ", members)}.");
        }
    }

    private static void RequireString(JsonElement element, string name, string expected)
    {
        if (!string.Equals(RequiredString(element, name), expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The names record's {name} is not {expected}.");
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidDataException($"The names record's {name} is not a string.");
    }

    private static string? Nullable(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String when value.GetString() is { Length: > 0 } text => text,
            _ => throw new InvalidDataException($"The names record's {name} is neither a string nor null."),
        };
    }

    private static string? Digest(JsonElement element, string name, bool nullable)
    {
        var value = nullable ? Nullable(element, name) : RequiredString(element, name);
        return value is null || Sha256.IsMatch(value)
            ? value
            : throw new InvalidDataException($"The names record's {name} is not a SHA-256 digest.");
    }

    private static IReadOnlyList<string> OrderedStrings(JsonElement element, string name, Func<string, bool> admitted)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"The names record's {name} is not an array.");
        }

        var read = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text || !admitted(text) ||
                (read.Count > 0 && string.CompareOrdinal(read[^1], text) >= 0))
            {
                throw new InvalidDataException($"The names record's {name} must be admitted values in order, each once.");
            }

            read.Add(text);
        }

        return read.AsReadOnly();
    }
}

/// <summary>
/// The indexing record a measurement writes beside the mount (<c>indexing-record.json</c>, schema
/// <c>lex-v3-indexing-record/1</c>), as the web root and the live server read it (<c>V3WebRoot.ReadIndexingRecord</c>,
/// <c>readIndexingRecord</c>): each work measured to name no private individual whose readable address the site serves, with
/// the text languages its index holds text in, and the build of the index the measurement read.
/// </summary>
public static class IndexingRecord
{
    public const string FileName = "indexing-record.json";

    public const string Schema = "lex-v3-indexing-record/1";

    // The site's address grammar for a work's dossier, as V3WebRoot.SitePath and the live server's routes read it: an address
    // outside it (an EU treaty's CELEX with "/") cannot be listed, since the reader refuses the whole record for one.
    private static readonly Regex LuxembourgAddress = new("^/lu-legilux/[a-z0-9][a-z0-9_-]*\\z", RegexOptions.CultureInvariant);

    private static readonly Regex EuropeAddress = new("^/eu-eurlex/[0-9A-Za-z][0-9A-Za-z()._-]*\\z", RegexOptions.CultureInvariant);

    // A consolidated version's CELEX names one wording, never a dossier: never listed.
    private static readonly Regex ConsolidatedCelex = new("^0[0-9]{4}[A-Z]{1,2}[0-9]{4,}(?:\\([0-9]{2}\\))?-[0-9]{8}\\z", RegexOptions.CultureInvariant);

    private static readonly Regex TextLanguage = new("^[a-z]{3}\\z", RegexOptions.CultureInvariant);

    /// <summary>Whether a dossier address is one the record may list (the site's grammar, and never a consolidated CELEX).</summary>
    public static bool Listable(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return LuxembourgAddress.IsMatch(address) ||
            (EuropeAddress.IsMatch(address) && !ConsolidatedCelex.IsMatch(address["/eu-eurlex/".Length..]));
    }

    /// <summary>The record as UTF-8 bytes: the works in their addresses' ordinal order, each with its languages in order.</summary>
    public static byte[] Write(string? builtAt, IReadOnlyList<(string Address, IReadOnlyList<string> Languages)> works)
    {
        ArgumentNullException.ThrowIfNull(works);
        if (builtAt is not null && !NamesRecord.IsUtcInstant(builtAt))
        {
            throw new ArgumentException("built_at must be an instant in UTC.", nameof(builtAt));
        }

        var ordered = works.OrderBy(static work => work.Address, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var (address, languages) = ordered[index];
            if (!Listable(address) || (index > 0 && string.Equals(ordered[index - 1].Address, address, StringComparison.Ordinal)))
            {
                throw new ArgumentException($"An indexing record cannot list {address}: not a dossier address, or listed twice.", nameof(works));
            }

            var sorted = languages.Order(StringComparer.Ordinal).ToArray();
            if (sorted.Length == 0 || sorted.Any(language => !TextLanguage.IsMatch(language)) || sorted.Distinct(StringComparer.Ordinal).Count() != sorted.Length)
            {
                throw new ArgumentException($"An indexing record's work {address} must list at least one text language, each once.", nameof(works));
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, NamesRecord.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            NamesRecord.WriteNullable(writer, "built_at", builtAt);
            writer.WriteStartArray("works");
            foreach (var (address, languages) in ordered)
            {
                writer.WriteStartObject();
                writer.WriteString("address", address);
                NamesRecord.WriteStrings(writer, "languages", languages.Order(StringComparer.Ordinal));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>The text of a record's bytes, for a caller that reads it as the web root does.</summary>
    public static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes ?? throw new ArgumentNullException(nameof(bytes)));
}

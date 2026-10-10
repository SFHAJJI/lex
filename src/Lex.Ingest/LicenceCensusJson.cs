using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Temporal;

namespace Lex.Ingest;

public sealed class LicenceCensusSourceArtifact
{
    public LicenceCensusSourceArtifact(string path, byte[] bytes)
    {
        Path = LicenceCensusValidation.RequireRelativePath(path, nameof(path));
        ArgumentNullException.ThrowIfNull(bytes);
        Length = checked((ulong)bytes.LongLength);
        Sha256 = LicenceCensusValidation.Sha256(bytes);
    }

    public string Path { get; }
    public ulong Length { get; }
    public string Sha256 { get; }
}

public sealed record LicenceCensusCorpus
{
    public LicenceCensusCorpus(
        string repository,
        string commit,
        string tree,
        string worksSubtree,
        DateTimeOffset cutoffUtc)
    {
        Repository = LicenceCensusValidation.RequireText(
            repository, 2_048, nameof(repository));
        Commit = CodeIdentity.RequireFullCommit(commit, nameof(commit));
        Tree = CodeIdentity.RequireFullGitObjectId(tree, nameof(tree));
        WorksSubtree = CodeIdentity.RequireFullGitObjectId(
            worksSubtree, nameof(worksSubtree));
        CutoffUtc = LicenceCensusValidation.RequireUtc(
            cutoffUtc, nameof(cutoffUtc));
    }

    public string Repository { get; }
    public string Commit { get; }
    public string Tree { get; }
    public string WorksSubtree { get; }
    public DateTimeOffset CutoffUtc { get; }
}

public sealed record LicenceCensusBlob
{
    public LicenceCensusBlob(string path, string sha256)
    {
        Path = LicenceCensusValidation.RequireRelativePath(path, nameof(path));
        if (!Path.EndsWith(".xml", StringComparison.Ordinal))
            throw new InvalidDataException(
                "A census inventory path must end in exact lowercase .xml.");
        Sha256 = CodeIdentity.RequireSha256(sha256, nameof(sha256));
    }

    public string Path { get; init; }
    public string Sha256 { get; init; }
}

public sealed record LicenceCensusGitSnapshot(
    LicenceCensusCorpus Corpus,
    IReadOnlyList<LicenceCensusBlob> Inventory);

public sealed record LicenceCensusProbeRun(
    string Name,
    string ScriptSha256,
    string ExecutorToken,
    string ExecutorSha256,
    string ExecutorVersion,
    string RuntimeName,
    string RuntimeVersion,
    string Os,
    string Architecture,
    string LogicalCwd,
    IReadOnlyList<string> Argv,
    byte[] Stdin,
    IReadOnlyDictionary<string, string> Environment,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int ExitCode,
    byte[] Stdout,
    byte[] Stderr);

public sealed class LicenceCensusExecutionArtifacts
{
    internal LicenceCensusExecutionArtifacts(
        byte[] receiptBytes,
        byte[] structuralVocabularyBytes,
        byte[] structuralCountsBytes,
        string censusInputSha256,
        string censusOutputSha256)
    {
        ReceiptBytes = receiptBytes;
        ReceiptSha256 = LicenceCensusValidation.Sha256(receiptBytes);
        StructuralVocabularyBytes = structuralVocabularyBytes;
        StructuralCountsBytes = structuralCountsBytes;
        CensusInputSha256 = censusInputSha256;
        CensusOutputSha256 = censusOutputSha256;
    }

    public string Schema => LicenceCensusReceiptBuilder.Schema;
    public byte[] ReceiptBytes { get; }
    public string ReceiptSha256 { get; }
    public byte[] StructuralVocabularyBytes { get; }
    public byte[] StructuralCountsBytes { get; }
    public string CensusInputSha256 { get; }
    public string CensusOutputSha256 { get; }
}

public static class LicenceCensusReceiptBuilder
{
    public const string Schema = "lex-census-execution-receipt/1";
    public const string Selection =
        "regular_git_blob_suffix:.xml;case_sensitive;recursive;no_checkout_filters";
    private const int MaximumRunOutputBytes = 16_777_216;
    private static readonly string[] RequiredProbeNames =
    [
        "probe_akn_licence.py",
        "probe_archive.py",
        "probe_block_repeats.py",
        "probe_edge_cases.py",
        "probe_manifestation_licence.py",
        "probe_schema_binding.py",
        "probe_scl_names.py",
    ];
    private static readonly HashSet<string> AllowedEnvironmentNames = new(
    [
        "LANG", "LC_ALL", "PYTHONHASHSEED", "PYTHONUTF8", "SYSTEMROOT",
        "TEMP", "TMP", "TZ",
    ], StringComparer.Ordinal);

    public static LicenceCensusExecutionArtifacts Build(
        LuLicenceObservationRunReceipt observationRun,
        IReadOnlyCollection<LicenceCensusSourceArtifact> sourceArtifacts,
        LicenceCensusCorpus corpus,
        IReadOnlyCollection<LicenceCensusBlob> inventory,
        IReadOnlyCollection<LicenceCensusProbeRun> runs,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(observationRun);
        ArgumentNullException.ThrowIfNull(sourceArtifacts);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(runs);
        createdAt = LicenceCensusValidation.RequireUtc(createdAt, nameof(createdAt));
        if (createdAt < observationRun.EndedAt || corpus.CutoffUtc > createdAt)
            throw new InvalidDataException(
                "A census receipt cannot predate its observation run or cutoff.");

        var artifacts = sourceArtifacts
            .OrderBy(artifact => artifact.Path, StringComparer.Ordinal).ToArray();
        if (artifacts.Length == 0
            || artifacts.Select(artifact => artifact.Path)
                .Distinct(StringComparer.Ordinal).Count() != artifacts.Length)
            throw new InvalidDataException(
                "Census source artifacts must be nonempty and path-unique.");
        var artifactsByPath = artifacts.ToDictionary(
            artifact => artifact.Path, StringComparer.Ordinal);

        var orderedRuns = runs.OrderBy(run => run.Name, StringComparer.Ordinal).ToArray();
        if (!orderedRuns.Select(run => run.Name).SequenceEqual(RequiredProbeNames))
            throw new InvalidDataException(
                "A census receipt requires the exact seven OBS-02 probe runs.");

        var structuralCounts = new SortedDictionary<string, ulong>(StringComparer.Ordinal);
        var runBytes = new List<byte[]>(orderedRuns.Length);
        foreach (var run in orderedRuns)
        {
            ValidateRun(
                run, observationRun, createdAt, artifactsByPath, structuralCounts);
            runBytes.Add(LicenceCensusJson.SerializeRun(run));
        }

        var blobs = inventory.OrderBy(blob => blob.Path, StringComparer.Ordinal).ToArray();
        if (blobs.Select(blob => blob.Path).Distinct(StringComparer.Ordinal).Count()
            != blobs.Length)
            throw new InvalidDataException(
                "Census inventory paths must be exact and unique.");
        var censusInputSha256 = ComputeCensusInputSha256(corpus.Commit, blobs);
        var vocabularyBytes = LicenceCensusJson.SerializeStructuralVocabulary(
            structuralCounts.Keys);
        var countsBytes = LicenceCensusJson.SerializeStructuralCounts(structuralCounts);
        var vocabularySha256 = LicenceCensusValidation.Sha256(vocabularyBytes);
        var countsSha256 = LicenceCensusValidation.Sha256(countsBytes);
        var censusOutputSha256 = ComputeCensusOutputSha256(
            censusInputSha256,
            runBytes,
            vocabularySha256,
            countsSha256);
        var receiptBytes = LicenceCensusJson.SerializeReceipt(
            artifacts,
            corpus,
            checked((ulong)blobs.Length),
            censusInputSha256,
            orderedRuns,
            vocabularySha256,
            countsSha256,
            censusOutputSha256,
            createdAt);
        return new LicenceCensusExecutionArtifacts(
            receiptBytes,
            vocabularyBytes,
            countsBytes,
            censusInputSha256,
            censusOutputSha256);
    }

    public static string ComputeCensusInputSha256(
        string commit,
        IReadOnlyCollection<LicenceCensusBlob> inventory)
    {
        commit = CodeIdentity.RequireFullCommit(commit, nameof(commit));
        ArgumentNullException.ThrowIfNull(inventory);
        var records = inventory.OrderBy(blob => blob.Path, StringComparer.Ordinal).ToArray();
        if (records.Select(record => record.Path)
            .Distinct(StringComparer.Ordinal).Count() != records.Length)
            throw new InvalidDataException("Census inventory paths must be unique.");
        using var payload = new MemoryStream();
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, Encoding.ASCII.GetBytes("lex-license-census-input/1"));
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, Encoding.ASCII.GetBytes(commit));
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, LicenceCensusValidation.U64(checked((ulong)records.Length)));
        foreach (var record in records)
        {
            using var inner = new MemoryStream();
            LicenceCensusValidation.WriteLengthPrefixed(
                inner, LicenceCensusValidation.StrictUtf8(record.Path));
            LicenceCensusValidation.WriteLengthPrefixed(
                inner, Convert.FromHexString(record.Sha256));
            LicenceCensusValidation.WriteLengthPrefixed(payload, inner.ToArray());
        }
        return LicenceCensusValidation.Sha256(payload.ToArray());
    }

    private static string ComputeCensusOutputSha256(
        string censusInputSha256,
        IReadOnlyList<byte[]> canonicalRuns,
        string vocabularySha256,
        string countsSha256)
    {
        using var payload = new MemoryStream();
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, Encoding.ASCII.GetBytes("lex-license-census-output/1"));
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, Convert.FromHexString(censusInputSha256));
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, LicenceCensusValidation.U64(7));
        foreach (var run in canonicalRuns)
        {
            using var runPayload = new MemoryStream();
            LicenceCensusValidation.WriteLengthPrefixed(
                runPayload, Encoding.ASCII.GetBytes("lex-license-census-run/1"));
            LicenceCensusValidation.WriteLengthPrefixed(runPayload, run);
            LicenceCensusValidation.WriteLengthPrefixed(
                payload, SHA256.HashData(runPayload.ToArray()));
        }
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, Convert.FromHexString(vocabularySha256));
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, Convert.FromHexString(countsSha256));
        return LicenceCensusValidation.Sha256(payload.ToArray());
    }

    private static void ValidateRun(
        LicenceCensusProbeRun run,
        LuLicenceObservationRunReceipt observationRun,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, LicenceCensusSourceArtifact> artifacts,
        IDictionary<string, ulong> structuralCounts)
    {
        if (!artifacts.TryGetValue(run.Name, out var script)
            || script.Sha256 != CodeIdentity.RequireSha256(
                run.ScriptSha256, nameof(run.ScriptSha256)))
            throw new InvalidDataException(
                $"Census probe {run.Name} is not bound to its exact source artifact.");
        if (!run.Stdin.AsSpan().SequenceEqual(observationRun.ManifestBytes))
            throw new InvalidDataException(
                $"Census probe {run.Name} is bound to a different observation run.");
        if (run.StartedAt.Offset != TimeSpan.Zero
            || run.EndedAt.Offset != TimeSpan.Zero
            || run.StartedAt < observationRun.StartedAt
            || run.EndedAt < run.StartedAt
            || run.EndedAt > createdAt)
            throw new InvalidDataException(
                $"Census probe {run.Name} has stale or invalid run timestamps.");
        if (run.ExitCode != 0)
            throw new InvalidDataException(
                $"Census probe {run.Name} did not complete successfully.");
        if (run.Stdout.LongLength > MaximumRunOutputBytes
            || run.Stderr.LongLength > MaximumRunOutputBytes
            || run.Stderr.Length != 0)
            throw new InvalidDataException(
                $"Census probe {run.Name} emitted unsafe or oversized output.");
        if (run.Stdin.LongLength > MaximumRunOutputBytes)
            throw new InvalidDataException(
                $"Census probe {run.Name} stdin exceeds the OBS-02 bound.");

        _ = CodeIdentity.RequireSha256(run.ExecutorSha256, nameof(run.ExecutorSha256));
        LicenceCensusValidation.RequireToken(run.ExecutorToken, 128,
            nameof(run.ExecutorToken), allowDot: true);
        LicenceCensusValidation.RequireText(run.ExecutorVersion, 256,
            nameof(run.ExecutorVersion));
        LicenceCensusValidation.RequireToken(run.RuntimeName, 128,
            nameof(run.RuntimeName), allowDot: true);
        LicenceCensusValidation.RequireText(run.RuntimeVersion, 256,
            nameof(run.RuntimeVersion));
        LicenceCensusValidation.RequireToken(run.Os, 64, nameof(run.Os));
        LicenceCensusValidation.RequireToken(
            run.Architecture, 64, nameof(run.Architecture));
        if (run.LogicalCwd != "corpus-root")
            throw new InvalidDataException(
                "Census probe logical cwd must be corpus-root.");
        if (run.Argv is null || run.Argv.Count == 0)
            throw new InvalidDataException("Census probe argv must be nonempty.");
        foreach (var argument in run.Argv)
            LicenceCensusValidation.RequireText(argument, 8_192, "argv");
        if (run.Environment is null
            || run.Environment.Keys.Any(name => !AllowedEnvironmentNames.Contains(name)))
            throw new InvalidDataException(
                "Census probe environment contains a non-allowlisted name.");
        foreach (var item in run.Environment)
            LicenceCensusValidation.RequireText(
                item.Value, 8_192, $"environment {item.Key}", allowEmpty: true);

        foreach (var counter in LicenceCensusJson.ParseStructuralCounts(run.Stdout))
            if (!structuralCounts.TryAdd(counter.Key, counter.Value))
                throw new InvalidDataException(
                    $"Census counter {counter.Key} was emitted by more than one probe.");
    }
}

public static class LicenceCensusJson
{
    public static byte[] SerializeStructuralCounts(
        IReadOnlyDictionary<string, ulong> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var writer = new CanonicalJson();
        writer.BeginObject();
        var first = true;
        foreach (var item in counts.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            LicenceCensusValidation.RequireCounter(item.Key);
            writer.Property(item.Key, ref first);
            writer.Number(item.Value);
        }
        writer.EndObject();
        return writer.Finish();
    }

    internal static byte[] SerializeStructuralVocabulary(IEnumerable<string> names)
    {
        var ordered = names.Order(StringComparer.Ordinal).ToArray();
        var writer = new CanonicalJson();
        writer.BeginArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (index > 0) writer.Comma();
            LicenceCensusValidation.RequireCounter(ordered[index]);
            writer.String(ordered[index]);
        }
        writer.EndArray();
        return writer.Finish();
    }

    internal static SortedDictionary<string, ulong> ParseStructuralCounts(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(
                    "Census probe stdout must be a structural-count object.");
            var result = new SortedDictionary<string, ulong>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                LicenceCensusValidation.RequireCounter(property.Name);
                if (!result.TryAdd(property.Name,
                        property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetUInt64(out var value)
                            ? value
                            : throw new InvalidDataException(
                                "Census structural counts must be uint64 values.")))
                    throw new InvalidDataException(
                        "Census structural count keys must be unique.");
            }
            if (!SerializeStructuralCounts(result).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException(
                    "Census probe stdout is not canonical structural-count JSON.");
            return result;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Census probe stdout is not strict JSON.", error);
        }
    }

    internal static byte[] SerializeRun(LicenceCensusProbeRun run)
    {
        var writer = new CanonicalJson();
        WriteRun(writer, run);
        return writer.Finish();
    }

    internal static byte[] SerializeReceipt(
        IReadOnlyList<LicenceCensusSourceArtifact> sourceArtifacts,
        LicenceCensusCorpus corpus,
        ulong recordCount,
        string censusInputSha256,
        IReadOnlyList<LicenceCensusProbeRun> runs,
        string vocabularySha256,
        string countsSha256,
        string censusOutputSha256,
        DateTimeOffset createdAt)
    {
        var writer = new CanonicalJson();
        writer.BeginObject();
        var first = true;
        writer.Property("schema", ref first); writer.String(LicenceCensusReceiptBuilder.Schema);
        writer.Property("source_artifacts", ref first); writer.BeginArray();
        for (var index = 0; index < sourceArtifacts.Count; index++)
        {
            if (index > 0) writer.Comma();
            writer.BeginObject();
            var artifactFirst = true;
            writer.Property("path", ref artifactFirst); writer.String(sourceArtifacts[index].Path);
            writer.Property("length", ref artifactFirst); writer.Number(sourceArtifacts[index].Length);
            writer.Property("sha256", ref artifactFirst); writer.String(sourceArtifacts[index].Sha256);
            writer.EndObject();
        }
        writer.EndArray();
        writer.Property("corpus", ref first); writer.BeginObject();
        var corpusFirst = true;
        writer.Property("repository", ref corpusFirst); writer.String(corpus.Repository);
        writer.Property("commit", ref corpusFirst); writer.String(corpus.Commit);
        writer.Property("tree", ref corpusFirst); writer.String(corpus.Tree);
        writer.Property("works_subtree", ref corpusFirst); writer.String(corpus.WorksSubtree);
        writer.Property("cutoff_utc", ref corpusFirst); writer.Timestamp(corpus.CutoffUtc);
        writer.EndObject();
        writer.Property("inventory", ref first); writer.BeginObject();
        var inventoryFirst = true;
        writer.Property("selection", ref inventoryFirst); writer.String(LicenceCensusReceiptBuilder.Selection);
        writer.Property("record_count", ref inventoryFirst); writer.Number(recordCount);
        writer.Property("census_input_sha256", ref inventoryFirst); writer.String(censusInputSha256);
        writer.EndObject();
        writer.Property("runs", ref first); writer.BeginArray();
        for (var index = 0; index < runs.Count; index++)
        {
            if (index > 0) writer.Comma();
            WriteRun(writer, runs[index]);
        }
        writer.EndArray();
        writer.Property("structural_vocabulary_sha256", ref first); writer.String(vocabularySha256);
        writer.Property("structural_counts_sha256", ref first); writer.String(countsSha256);
        writer.Property("census_output_sha256", ref first); writer.String(censusOutputSha256);
        writer.Property("created_at", ref first); writer.Timestamp(createdAt);
        writer.EndObject();
        return writer.Finish();
    }

    private static void WriteRun(CanonicalJson writer, LicenceCensusProbeRun run)
    {
        writer.BeginObject();
        var first = true;
        writer.Property("name", ref first); writer.String(run.Name);
        writer.Property("script_sha256", ref first); writer.String(run.ScriptSha256);
        writer.Property("executor_token", ref first); writer.String(run.ExecutorToken);
        writer.Property("executor_sha256", ref first); writer.String(run.ExecutorSha256);
        writer.Property("executor_version", ref first); writer.String(run.ExecutorVersion);
        writer.Property("runtime_name", ref first); writer.String(run.RuntimeName);
        writer.Property("runtime_version", ref first); writer.String(run.RuntimeVersion);
        writer.Property("os", ref first); writer.String(run.Os);
        writer.Property("architecture", ref first); writer.String(run.Architecture);
        writer.Property("logical_cwd", ref first); writer.String(run.LogicalCwd);
        writer.Property("argv", ref first); writer.BeginArray();
        for (var index = 0; index < run.Argv.Count; index++)
        {
            if (index > 0) writer.Comma();
            writer.String(run.Argv[index]);
        }
        writer.EndArray();
        writer.Property("stdin_mode", ref first); writer.String(run.Stdin.Length == 0 ? "none" : "bytes");
        writer.Property("stdin_length", ref first); writer.Number(checked((ulong)run.Stdin.LongLength));
        writer.Property("stdin_sha256", ref first); writer.String(LicenceCensusValidation.Sha256(run.Stdin));
        writer.Property("environment", ref first); writer.BeginObject();
        var environmentFirst = true;
        foreach (var item in run.Environment.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            writer.Property(item.Key, ref environmentFirst);
            writer.String(item.Value);
        }
        writer.EndObject();
        writer.Property("culture", ref first); writer.String("Invariant");
        writer.Property("timezone", ref first); writer.String("UTC");
        writer.Property("started_at", ref first); writer.Timestamp(run.StartedAt);
        writer.Property("ended_at", ref first); writer.Timestamp(run.EndedAt);
        writer.Property("exit_code", ref first); writer.SignedNumber(run.ExitCode);
        writer.Property("stdout_length", ref first); writer.Number(checked((ulong)run.Stdout.LongLength));
        writer.Property("stdout_sha256", ref first); writer.String(LicenceCensusValidation.Sha256(run.Stdout));
        writer.Property("stderr_length", ref first); writer.Number(checked((ulong)run.Stderr.LongLength));
        writer.Property("stderr_sha256", ref first); writer.String(LicenceCensusValidation.Sha256(run.Stderr));
        writer.EndObject();
    }
}

internal static class LicenceCensusValidation
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static byte[] StrictUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Utf8.GetBytes(value);
    }

    public static DateTimeOffset RequireUtc(DateTimeOffset value, string field)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new InvalidDataException($"{field} must use UTC.");
        return value;
    }

    public static string RequireText(
        string? value, int maximumBytes, string field, bool allowEmpty = false)
    {
        if (value is null || !allowEmpty && value.Length == 0
            || StrictUtf8(value).Length > maximumBytes
            || value.Any(character => char.IsControl(character)))
            throw new InvalidDataException(
                $"{field} must be bounded strict UTF-8 without controls.");
        return value;
    }

    public static string RequireToken(
        string? value, int maximumLength, string field, bool allowDot = false)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > maximumLength
            || value.Any(character => character is not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'
                && !(allowDot && character == '.')))
            throw new InvalidDataException(
                $"{field} must be a bounded lowercase ASCII token.");
        return value;
    }

    public static string RequireRelativePath(string? value, string field)
    {
        RequireText(value, 8_192, field);
        if (value!.Contains('\\')
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.EndsWith("/", StringComparison.Ordinal)
            || value.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException(
                $"{field} must be a strict relative slash path.");
        return value;
    }

    public static void RequireCounter(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 128
            || value.Any(character => character is not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '_'))
            throw new InvalidDataException(
                "Census counter keys must be bounded lowercase ASCII tokens.");
    }

    public static byte[] U64(ulong value)
    {
        var bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    public static void WriteLengthPrefixed(Stream output, byte[] value)
    {
        output.Write(U64(checked((ulong)value.LongLength)));
        output.Write(value);
    }
}

internal sealed class CanonicalJson
{
    private readonly StringBuilder _builder = new();
    private bool _finished;

    public void BeginObject() => _builder.Append('{');
    public void EndObject() => _builder.Append('}');
    public void BeginArray() => _builder.Append('[');
    public void EndArray() => _builder.Append(']');
    public void Comma() => _builder.Append(',');

    public void Property(string name, ref bool first)
    {
        if (!first) Comma();
        first = false;
        String(name);
        _builder.Append(':');
    }

    public void Number(ulong value) =>
        _builder.Append(value.ToString(CultureInfo.InvariantCulture));

    public void SignedNumber(int value) =>
        _builder.Append(value.ToString(CultureInfo.InvariantCulture));

    public void Boolean(bool value) => _builder.Append(value ? "true" : "false");

    public void Null() => _builder.Append("null");

    public void Timestamp(DateTimeOffset value)
    {
        LicenceCensusValidation.RequireUtc(value, nameof(value));
        String(value.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
    }

    public void String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"': _builder.Append("\\\""); break;
                case '\\': _builder.Append("\\\\"); break;
                case < ' ':
                    _builder.Append("\\u00");
                    _builder.Append(((int)character).ToString("x2", CultureInfo.InvariantCulture));
                    break;
                default:
                    if (char.IsHighSurrogate(character))
                    {
                        if (index + 1 >= value.Length
                            || !char.IsLowSurrogate(value[index + 1]))
                            throw new InvalidDataException(
                                "Canonical JSON rejects lone UTF-16 surrogates.");
                        _builder.Append(character);
                        _builder.Append(value[++index]);
                    }
                    else if (char.IsLowSurrogate(character))
                    {
                        throw new InvalidDataException(
                            "Canonical JSON rejects lone UTF-16 surrogates.");
                    }
                    else
                    {
                        _builder.Append(character);
                    }
                    break;
            }
        }
        _builder.Append('"');
    }

    public byte[] Finish()
    {
        if (_finished)
            throw new InvalidOperationException("Canonical JSON is already complete.");
        _finished = true;
        _builder.Append('\n');
        return LicenceCensusValidation.StrictUtf8(_builder.ToString());
    }
}

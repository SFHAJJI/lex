using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Rights;

public sealed class LicenceCensusSourceArtifact
{
    public LicenceCensusSourceArtifact(string path, ulong length, string sha256)
    {
        Path = LicenceCensusValidation.RequireRelativePath(path, nameof(path));
        Length = length;
        Sha256 = LicenceCensusValidation.RequireContentDigest(length, sha256, nameof(sha256));
    }

    public string Path { get; }

    public ulong Length { get; }

    public string Sha256 { get; }
}

public sealed class LicenceCensusGitBlob
{
    public LicenceCensusGitBlob(string path, string sha256)
    {
        Path = LicenceCensusValidation.RequireXmlGitPath(path, nameof(path));
        Sha256 = RoutedHttpValidation.RequireSha256(sha256, nameof(sha256));
    }

    public string Path { get; }

    public string Sha256 { get; }
}

public sealed class LicenceCensusCorpus
{
    public LicenceCensusCorpus(
        string repository,
        string commit,
        string tree,
        string worksSubtree,
        string cutoffUtc)
    {
        Repository = RoutedHttpValidation.RequireAbsoluteHttpsUri(repository, nameof(repository));
        Commit = LicenceCensusValidation.RequireGitObjectId(commit, nameof(commit));
        Tree = LicenceCensusValidation.RequireGitObjectId(tree, nameof(tree));
        WorksSubtree = LicenceCensusValidation.RequireGitObjectId(worksSubtree, nameof(worksSubtree));
        CutoffUtc = RoutedHttpValidation.RequireTimestamp(cutoffUtc, nameof(cutoffUtc));
    }

    public string Repository { get; }

    public string Commit { get; }

    public string Tree { get; }

    public string WorksSubtree { get; }

    public string CutoffUtc { get; }
}

public sealed class LicenceCensusInventory
{
    internal LicenceCensusInventory(
        IReadOnlyList<LicenceCensusGitBlob> selection,
        string censusInputSha256)
    {
        Selection = selection;
        RecordCount = checked((ulong)selection.Count);
        CensusInputSha256 = censusInputSha256;
    }

    public IReadOnlyList<LicenceCensusGitBlob> Selection { get; }

    public ulong RecordCount { get; }

    public string CensusInputSha256 { get; }
}

public enum LicenceCensusStdinMode
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("bytes")]
    Bytes = 1,
}

public sealed class LicenceCensusEnvironmentVariable
{
    public LicenceCensusEnvironmentVariable(string name, string value)
    {
        Name = LicenceCensusValidation.RequireEnvironmentName(name, nameof(name));
        Value = LicenceCensusValidation.RequireBoundedValue(value, nameof(value), 8192);
    }

    public string Name { get; }

    public string Value { get; }
}

public sealed class LicenceCensusRun
{
    public LicenceCensusRun(
        string name,
        string scriptSha256,
        string executorToken,
        string executorSha256,
        string executorVersion,
        string runtimeName,
        string runtimeVersion,
        string os,
        string architecture,
        string logicalCwd,
        IReadOnlyList<string> argv,
        LicenceCensusStdinMode stdinMode,
        ulong stdinLength,
        string stdinSha256,
        IReadOnlyList<LicenceCensusEnvironmentVariable> environment,
        string culture,
        string timezone,
        string startedAt,
        string endedAt,
        ulong exitCode,
        ulong stdoutLength,
        string stdoutSha256,
        ulong stderrLength,
        string stderrSha256)
    {
        Name = LicenceCensusValidation.RequireRunName(name, nameof(name));
        ScriptSha256 = RoutedHttpValidation.RequireSha256(scriptSha256, nameof(scriptSha256));
        ExecutorToken = LicenceCensusValidation.RequireToken(executorToken, nameof(executorToken));
        ExecutorSha256 = RoutedHttpValidation.RequireSha256(executorSha256, nameof(executorSha256));
        ExecutorVersion = LicenceCensusValidation.RequireBoundedAscii(executorVersion, nameof(executorVersion));
        RuntimeName = LicenceCensusValidation.RequireToken(runtimeName, nameof(runtimeName));
        RuntimeVersion = LicenceCensusValidation.RequireBoundedAscii(runtimeVersion, nameof(runtimeVersion));
        Os = LicenceCensusValidation.RequireToken(os, nameof(os));
        Architecture = LicenceCensusValidation.RequireToken(architecture, nameof(architecture));
        if (!string.Equals(logicalCwd, "corpus-root", StringComparison.Ordinal))
        {
            throw new ArgumentException("The census logical working directory must be corpus-root.", nameof(logicalCwd));
        }

        LogicalCwd = logicalCwd;
        ArgumentNullException.ThrowIfNull(argv);
        if (argv.Count == 0)
        {
            throw new ArgumentException("A census run must retain its nonempty logical argument vector.", nameof(argv));
        }

        Argv = argv.Select((value, index) =>
                LicenceCensusValidation.RequireLogicalArgument(value, $"{nameof(argv)}[{index}]") )
            .ToArray();
        if (!Enum.IsDefined(stdinMode))
        {
            throw new ArgumentOutOfRangeException(nameof(stdinMode));
        }

        StdinMode = stdinMode;
        StdinLength = stdinLength;
        StdinSha256 = RoutedHttpValidation.RequireSha256(stdinSha256, nameof(stdinSha256));
        if (stdinMode == LicenceCensusStdinMode.None &&
            (stdinLength != 0 || !string.Equals(stdinSha256, LicenceCensusValidation.EmptySha256, StringComparison.Ordinal)))
        {
            throw new ArgumentException("A none stdin must bind zero exact bytes.", nameof(stdinSha256));
        }

        ArgumentNullException.ThrowIfNull(environment);
        Environment = LicenceCensusValidation.RequireEnvironment(environment, nameof(environment));
        if (!string.Equals(culture, "Invariant", StringComparison.Ordinal))
        {
            throw new ArgumentException("Census culture must be Invariant.", nameof(culture));
        }

        Culture = culture;
        if (!string.Equals(timezone, "UTC", StringComparison.Ordinal))
        {
            throw new ArgumentException("Census timezone must be UTC.", nameof(timezone));
        }

        Timezone = timezone;
        StartedAt = RoutedHttpValidation.RequireTimestamp(startedAt, nameof(startedAt));
        EndedAt = RoutedHttpValidation.RequireTimestamp(endedAt, nameof(endedAt));
        if (string.CompareOrdinal(StartedAt, EndedAt) > 0)
        {
            throw new ArgumentException("A census run cannot end before it starts.", nameof(endedAt));
        }

        ExitCode = exitCode;
        StdoutLength = LicenceCensusValidation.RequireOutputLength(stdoutLength, nameof(stdoutLength));
        StdoutSha256 = LicenceCensusValidation.RequireContentDigest(stdoutLength, stdoutSha256, nameof(stdoutSha256));
        StderrLength = LicenceCensusValidation.RequireOutputLength(stderrLength, nameof(stderrLength));
        StderrSha256 = LicenceCensusValidation.RequireContentDigest(stderrLength, stderrSha256, nameof(stderrSha256));
    }

    public string Name { get; }
    public string ScriptSha256 { get; }
    public string ExecutorToken { get; }
    public string ExecutorSha256 { get; }
    public string ExecutorVersion { get; }
    public string RuntimeName { get; }
    public string RuntimeVersion { get; }
    public string Os { get; }
    public string Architecture { get; }
    public string LogicalCwd { get; }
    public IReadOnlyList<string> Argv { get; }
    public LicenceCensusStdinMode StdinMode { get; }
    public ulong StdinLength { get; }
    public string StdinSha256 { get; }
    public IReadOnlyList<LicenceCensusEnvironmentVariable> Environment { get; }
    public string Culture { get; }
    public string Timezone { get; }
    public string StartedAt { get; }
    public string EndedAt { get; }
    public ulong ExitCode { get; }
    public ulong StdoutLength { get; }
    public string StdoutSha256 { get; }
    public ulong StderrLength { get; }
    public string StderrSha256 { get; }
}

/// <summary>
/// The closed, byte-reproducible execution receipt required by accepted licence contract OBS-02.
/// This is private prerequisite evidence only; it does not decide a licence or publish corpus data.
/// </summary>
public sealed class LicenceCensusExecutionReceipt
{
    public const string SchemaId = "lex-census-execution-receipt/1";

    private static readonly string[] RequiredRunNames =
    [
        "probe_akn_licence.py",
        "probe_archive.py",
        "probe_block_repeats.py",
        "probe_edge_cases.py",
        "probe_manifestation_licence.py",
        "probe_schema_binding.py",
        "probe_scl_names.py",
    ];

    private readonly byte[] _canonicalBytes;

    private LicenceCensusExecutionReceipt(
        IReadOnlyList<LicenceCensusSourceArtifact> sourceArtifacts,
        LicenceCensusCorpus corpus,
        LicenceCensusInventory inventory,
        IReadOnlyList<LicenceCensusRun> runs,
        string structuralVocabularySha256,
        string structuralCountsSha256,
        string censusOutputSha256,
        string createdAt)
    {
        SourceArtifacts = sourceArtifacts;
        Corpus = corpus;
        Inventory = inventory;
        Runs = runs;
        StructuralVocabularySha256 = structuralVocabularySha256;
        StructuralCountsSha256 = structuralCountsSha256;
        CensusOutputSha256 = censusOutputSha256;
        CreatedAt = createdAt;
        _canonicalBytes = WriteCanonicalBytes(this);
    }

    public IReadOnlyList<LicenceCensusSourceArtifact> SourceArtifacts { get; }
    public LicenceCensusCorpus Corpus { get; }
    public LicenceCensusInventory Inventory { get; }
    public IReadOnlyList<LicenceCensusRun> Runs { get; }
    public string StructuralVocabularySha256 { get; }
    public string StructuralCountsSha256 { get; }
    public string CensusOutputSha256 { get; }
    public string CreatedAt { get; }

    public static LicenceCensusExecutionReceipt Create(
        IReadOnlyList<LicenceCensusSourceArtifact> sourceArtifacts,
        LicenceCensusCorpus corpus,
        IReadOnlyList<LicenceCensusGitBlob> selection,
        string censusInputSha256,
        IReadOnlyList<LicenceCensusRun> runs,
        string structuralVocabularySha256,
        string structuralCountsSha256,
        string censusOutputSha256,
        string createdAt)
    {
        ArgumentNullException.ThrowIfNull(sourceArtifacts);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(runs);

        var artifacts = LicenceCensusValidation.RequireSortedUnique(
            sourceArtifacts, static value => value.Path, nameof(sourceArtifacts));
        if (artifacts.Count == 0)
        {
            throw new ArgumentException("The census receipt must bind its artifacts, probes, and wrapper.", nameof(sourceArtifacts));
        }

        var blobs = LicenceCensusValidation.RequireSortedUnique(
            selection, static value => value.Path, nameof(selection));
        var expectedInput = ComputeCensusInput(corpus.Commit, blobs);
        censusInputSha256 = RoutedHttpValidation.RequireSha256(censusInputSha256, nameof(censusInputSha256));
        if (!string.Equals(censusInputSha256, expectedInput, StringComparison.Ordinal))
        {
            throw new ArgumentException("The census input digest does not bind the exact selected Git blobs.", nameof(censusInputSha256));
        }

        var runArray = runs.ToArray();
        if (runArray.Length != RequiredRunNames.Length ||
            !runArray.Select(static run => run?.Name).SequenceEqual(RequiredRunNames, StringComparer.Ordinal))
        {
            throw new ArgumentException("The census receipt must contain the exact seven runs in ASCII order.", nameof(runs));
        }

        structuralVocabularySha256 = RoutedHttpValidation.RequireSha256(
            structuralVocabularySha256, nameof(structuralVocabularySha256));
        structuralCountsSha256 = RoutedHttpValidation.RequireSha256(
            structuralCountsSha256, nameof(structuralCountsSha256));
        censusOutputSha256 = RoutedHttpValidation.RequireSha256(censusOutputSha256, nameof(censusOutputSha256));
        var expectedOutput = ComputeCensusOutput(
            censusInputSha256, runArray, structuralVocabularySha256, structuralCountsSha256);
        if (!string.Equals(censusOutputSha256, expectedOutput, StringComparison.Ordinal))
        {
            throw new ArgumentException("The census output digest does not bind the exact run receipts and structural outputs.", nameof(censusOutputSha256));
        }

        return new LicenceCensusExecutionReceipt(
            artifacts,
            corpus,
            new LicenceCensusInventory(blobs, censusInputSha256),
            runArray,
            structuralVocabularySha256,
            structuralCountsSha256,
            censusOutputSha256,
            RoutedHttpValidation.RequireTimestamp(createdAt, nameof(createdAt)));
    }

    public byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();

    public static byte[] CopyCanonicalRunBytes(LicenceCensusRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var writer = new RoutedHttpTextWriter();
        WriteRun(writer, run);
        return writer.ToUtf8();
    }

    public static LicenceCensusExecutionReceipt ParseAndVerify(ReadOnlySpan<byte> canonicalBytes)
    {
        try
        {
            var json = RoutedHttpValidation.DecodeStrictUtf8(canonicalBytes, nameof(canonicalBytes));
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            RoutedHttpValidation.RequireExactPropertyNames(root,
                [
                    "schema", "source_artifacts", "corpus", "inventory", "runs",
                    "structural_vocabulary_sha256", "structural_counts_sha256",
                    "census_output_sha256", "created_at",
                ], nameof(canonicalBytes));
            if (!string.Equals(root.GetProperty("schema").GetString(), SchemaId, StringComparison.Ordinal))
            {
                throw new ArgumentException("The census receipt has the wrong schema.", nameof(canonicalBytes));
            }

            var corpusElement = root.GetProperty("corpus");
            RoutedHttpValidation.RequireExactPropertyNames(corpusElement,
                ["repository", "commit", "tree", "works_subtree", "cutoff_utc"], nameof(canonicalBytes));
            var corpus = new LicenceCensusCorpus(
                corpusElement.GetProperty("repository").GetString()!,
                corpusElement.GetProperty("commit").GetString()!,
                corpusElement.GetProperty("tree").GetString()!,
                corpusElement.GetProperty("works_subtree").GetString()!,
                corpusElement.GetProperty("cutoff_utc").GetString()!);

            var inventoryElement = root.GetProperty("inventory");
            RoutedHttpValidation.RequireExactPropertyNames(inventoryElement,
                ["selection", "record_count", "census_input_sha256"], nameof(canonicalBytes));
            var selection = RequireArray(inventoryElement.GetProperty("selection"), nameof(canonicalBytes))
                .Select(element =>
                {
                    RoutedHttpValidation.RequireExactPropertyNames(element, ["path", "sha256"], nameof(canonicalBytes));
                    return new LicenceCensusGitBlob(
                        element.GetProperty("path").GetString()!,
                        element.GetProperty("sha256").GetString()!);
                }).ToArray();
            if (inventoryElement.GetProperty("record_count").GetUInt64() != (ulong)selection.Length)
            {
                throw new ArgumentException("The census record count does not equal the retained selection.", nameof(canonicalBytes));
            }

            var receipt = Create(
                RequireArray(root.GetProperty("source_artifacts"), nameof(canonicalBytes))
                    .Select(element => ParseSourceArtifact(element, nameof(canonicalBytes))).ToArray(),
                corpus,
                selection,
                inventoryElement.GetProperty("census_input_sha256").GetString()!,
                RequireArray(root.GetProperty("runs"), nameof(canonicalBytes))
                    .Select(element => ParseRun(element, nameof(canonicalBytes))).ToArray(),
                root.GetProperty("structural_vocabulary_sha256").GetString()!,
                root.GetProperty("structural_counts_sha256").GetString()!,
                root.GetProperty("census_output_sha256").GetString()!,
                root.GetProperty("created_at").GetString()!);
            if (!canonicalBytes.SequenceEqual(receipt._canonicalBytes))
            {
                throw new ArgumentException("The census receipt is not its exact canonical typed representation.", nameof(canonicalBytes));
            }

            return receipt;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                "The census receipt is not one valid closed canonical object.",
                nameof(canonicalBytes),
                exception);
        }
    }

    private static LicenceCensusSourceArtifact ParseSourceArtifact(JsonElement element, string parameterName)
    {
        RoutedHttpValidation.RequireExactPropertyNames(element, ["path", "length", "sha256"], parameterName);
        return new LicenceCensusSourceArtifact(
            element.GetProperty("path").GetString()!,
            element.GetProperty("length").GetUInt64(),
            element.GetProperty("sha256").GetString()!);
    }

    private static LicenceCensusRun ParseRun(JsonElement element, string parameterName)
    {
        RoutedHttpValidation.RequireExactPropertyNames(element,
            [
                "name", "script_sha256", "executor_token", "executor_sha256", "executor_version",
                "runtime_name", "runtime_version", "os", "architecture", "logical_cwd", "argv",
                "stdin_mode", "stdin_length", "stdin_sha256", "environment", "culture", "timezone",
                "started_at", "ended_at", "exit_code", "stdout_length", "stdout_sha256",
                "stderr_length", "stderr_sha256",
            ], parameterName);
        var stdinMode = element.GetProperty("stdin_mode").GetString() switch
        {
            "none" => LicenceCensusStdinMode.None,
            "bytes" => LicenceCensusStdinMode.Bytes,
            _ => throw new ArgumentException("The census stdin mode is not closed.", parameterName),
        };
        return new LicenceCensusRun(
            element.GetProperty("name").GetString()!,
            element.GetProperty("script_sha256").GetString()!,
            element.GetProperty("executor_token").GetString()!,
            element.GetProperty("executor_sha256").GetString()!,
            element.GetProperty("executor_version").GetString()!,
            element.GetProperty("runtime_name").GetString()!,
            element.GetProperty("runtime_version").GetString()!,
            element.GetProperty("os").GetString()!,
            element.GetProperty("architecture").GetString()!,
            element.GetProperty("logical_cwd").GetString()!,
            RequireArray(element.GetProperty("argv"), parameterName)
                .Select(value => value.GetString()!).ToArray(),
            stdinMode,
            element.GetProperty("stdin_length").GetUInt64(),
            element.GetProperty("stdin_sha256").GetString()!,
            RequireArray(element.GetProperty("environment"), parameterName)
                .Select(value => ParseEnvironment(value, parameterName)).ToArray(),
            element.GetProperty("culture").GetString()!,
            element.GetProperty("timezone").GetString()!,
            element.GetProperty("started_at").GetString()!,
            element.GetProperty("ended_at").GetString()!,
            element.GetProperty("exit_code").GetUInt64(),
            element.GetProperty("stdout_length").GetUInt64(),
            element.GetProperty("stdout_sha256").GetString()!,
            element.GetProperty("stderr_length").GetUInt64(),
            element.GetProperty("stderr_sha256").GetString()!);
    }

    private static LicenceCensusEnvironmentVariable ParseEnvironment(JsonElement element, string parameterName)
    {
        RoutedHttpValidation.RequireExactPropertyNames(element, ["name", "value"], parameterName);
        return new LicenceCensusEnvironmentVariable(
            element.GetProperty("name").GetString()!,
            element.GetProperty("value").GetString()!);
    }

    private static IEnumerable<JsonElement> RequireArray(JsonElement element, string parameterName)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("A census collection must be a JSON array.", parameterName);
        }

        return element.EnumerateArray();
    }

    private static byte[] WriteCanonicalBytes(LicenceCensusExecutionReceipt value)
    {
        var writer = new RoutedHttpTextWriter();
        writer.Raw("{\"schema\":");
        writer.String(SchemaId);
        writer.Raw(",\"source_artifacts\":[");
        for (var index = 0; index < value.SourceArtifacts.Count; index++)
        {
            if (index > 0) writer.Raw(",");
            var artifact = value.SourceArtifacts[index];
            writer.Raw("{\"path\":");
            writer.String(artifact.Path);
            writer.Raw(",\"length\":");
            writer.UInt64(artifact.Length);
            writer.Raw(",\"sha256\":");
            writer.String(artifact.Sha256);
            writer.Raw("}");
        }

        writer.Raw("],\"corpus\":{\"repository\":");
        writer.String(value.Corpus.Repository);
        writer.Raw(",\"commit\":");
        writer.String(value.Corpus.Commit);
        writer.Raw(",\"tree\":");
        writer.String(value.Corpus.Tree);
        writer.Raw(",\"works_subtree\":");
        writer.String(value.Corpus.WorksSubtree);
        writer.Raw(",\"cutoff_utc\":");
        writer.String(value.Corpus.CutoffUtc);
        writer.Raw("},\"inventory\":{\"selection\":[");
        for (var index = 0; index < value.Inventory.Selection.Count; index++)
        {
            if (index > 0) writer.Raw(",");
            var blob = value.Inventory.Selection[index];
            writer.Raw("{\"path\":");
            writer.String(blob.Path);
            writer.Raw(",\"sha256\":");
            writer.String(blob.Sha256);
            writer.Raw("}");
        }

        writer.Raw("],\"record_count\":");
        writer.UInt64(value.Inventory.RecordCount);
        writer.Raw(",\"census_input_sha256\":");
        writer.String(value.Inventory.CensusInputSha256);
        writer.Raw("},\"runs\":[");
        for (var index = 0; index < value.Runs.Count; index++)
        {
            if (index > 0) writer.Raw(",");
            WriteRun(writer, value.Runs[index]);
        }

        writer.Raw("],\"structural_vocabulary_sha256\":");
        writer.String(value.StructuralVocabularySha256);
        writer.Raw(",\"structural_counts_sha256\":");
        writer.String(value.StructuralCountsSha256);
        writer.Raw(",\"census_output_sha256\":");
        writer.String(value.CensusOutputSha256);
        writer.Raw(",\"created_at\":");
        writer.String(value.CreatedAt);
        writer.Raw("}\n");
        return writer.ToUtf8();
    }

    private static void WriteRun(RoutedHttpTextWriter writer, LicenceCensusRun run)
    {
        writer.Raw("{\"name\":");
        writer.String(run.Name);
        writer.Raw(",\"script_sha256\":"); writer.String(run.ScriptSha256);
        writer.Raw(",\"executor_token\":"); writer.String(run.ExecutorToken);
        writer.Raw(",\"executor_sha256\":"); writer.String(run.ExecutorSha256);
        writer.Raw(",\"executor_version\":"); writer.String(run.ExecutorVersion);
        writer.Raw(",\"runtime_name\":"); writer.String(run.RuntimeName);
        writer.Raw(",\"runtime_version\":"); writer.String(run.RuntimeVersion);
        writer.Raw(",\"os\":"); writer.String(run.Os);
        writer.Raw(",\"architecture\":"); writer.String(run.Architecture);
        writer.Raw(",\"logical_cwd\":"); writer.String(run.LogicalCwd);
        writer.Raw(",\"argv\":[");
        for (var index = 0; index < run.Argv.Count; index++)
        {
            if (index > 0) writer.Raw(",");
            writer.String(run.Argv[index]);
        }

        writer.Raw("],\"stdin_mode\":");
        writer.String(run.StdinMode == LicenceCensusStdinMode.None ? "none" : "bytes");
        writer.Raw(",\"stdin_length\":"); writer.UInt64(run.StdinLength);
        writer.Raw(",\"stdin_sha256\":"); writer.String(run.StdinSha256);
        writer.Raw(",\"environment\":[");
        for (var index = 0; index < run.Environment.Count; index++)
        {
            if (index > 0) writer.Raw(",");
            writer.Raw("{\"name\":"); writer.String(run.Environment[index].Name);
            writer.Raw(",\"value\":"); writer.String(run.Environment[index].Value);
            writer.Raw("}");
        }

        writer.Raw("],\"culture\":"); writer.String(run.Culture);
        writer.Raw(",\"timezone\":"); writer.String(run.Timezone);
        writer.Raw(",\"started_at\":"); writer.String(run.StartedAt);
        writer.Raw(",\"ended_at\":"); writer.String(run.EndedAt);
        writer.Raw(",\"exit_code\":"); writer.UInt64(run.ExitCode);
        writer.Raw(",\"stdout_length\":"); writer.UInt64(run.StdoutLength);
        writer.Raw(",\"stdout_sha256\":"); writer.String(run.StdoutSha256);
        writer.Raw(",\"stderr_length\":"); writer.UInt64(run.StderrLength);
        writer.Raw(",\"stderr_sha256\":"); writer.String(run.StderrSha256);
        writer.Raw("}");
    }

    private static string ComputeCensusInput(
        string commit,
        IReadOnlyList<LicenceCensusGitBlob> selection)
    {
        using var stream = new MemoryStream();
        WriteLengthPrefixed(stream, Encoding.ASCII.GetBytes("lex-license-census-input/1"));
        WriteLengthPrefixed(stream, Encoding.ASCII.GetBytes(commit));
        WriteLengthPrefixed(stream, UInt64Bytes((ulong)selection.Count));
        foreach (var blob in selection)
        {
            using var record = new MemoryStream();
            WriteLengthPrefixed(record, RoutedHttpValidation.StrictUtf8.GetBytes(blob.Path));
            WriteLengthPrefixed(record, Convert.FromHexString(blob.Sha256));
            WriteLengthPrefixed(stream, record.ToArray());
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static string ComputeCensusOutput(
        string inputSha256,
        IReadOnlyList<LicenceCensusRun> runs,
        string structuralVocabularySha256,
        string structuralCountsSha256)
    {
        using var stream = new MemoryStream();
        WriteLengthPrefixed(stream, Encoding.ASCII.GetBytes("lex-license-census-output/1"));
        WriteLengthPrefixed(stream, Convert.FromHexString(inputSha256));
        WriteLengthPrefixed(stream, UInt64Bytes((ulong)runs.Count));
        foreach (var run in runs)
        {
            using var runPreimage = new MemoryStream();
            WriteLengthPrefixed(runPreimage, Encoding.ASCII.GetBytes("lex-license-census-run/1"));
            WriteLengthPrefixed(runPreimage, CopyCanonicalRunBytes(run));
            WriteLengthPrefixed(stream, SHA256.HashData(runPreimage.ToArray()));
        }

        WriteLengthPrefixed(stream, Convert.FromHexString(structuralVocabularySha256));
        WriteLengthPrefixed(stream, Convert.FromHexString(structuralCountsSha256));
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static byte[] UInt64Bytes(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static void WriteLengthPrefixed(Stream stream, byte[] value)
    {
        stream.Write(UInt64Bytes((ulong)value.Length));
        stream.Write(value);
    }
}

internal static class LicenceCensusValidation
{
    public const string EmptySha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const ulong MaximumOutputBytes = 16_777_216;
    private static readonly HashSet<string> EnvironmentNames = new(StringComparer.Ordinal)
    {
        "LANG", "LC_ALL", "PYTHONHASHSEED", "PYTHONUTF8", "SYSTEMROOT", "TEMP", "TMP", "TZ",
    };

    public static string RequireRelativePath(string value, string parameterName)
    {
        RequireBoundedValue(value, parameterName, 8192);
        if (value.StartsWith("/", StringComparison.Ordinal) || value.Contains('\\') ||
            value.Contains(':') || value.Split('/').Any(static part => part.Length == 0 || part is "." or ".."))
        {
            throw new ArgumentException("Census paths must be strict relative Git-style paths.", parameterName);
        }

        return value;
    }

    public static string RequireXmlGitPath(string value, string parameterName)
    {
        RequireRelativePath(value, parameterName);
        if (!value.EndsWith(".xml", StringComparison.Ordinal))
        {
            throw new ArgumentException("Census inventory selects only exact lowercase .xml Git blobs.", parameterName);
        }

        return value;
    }

    public static string RequireGitObjectId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 40 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("Git object ids must be exact lowercase 40-hex ASCII.", parameterName);
        }

        return value;
    }

    public static string RequireContentDigest(ulong length, string value, string parameterName)
    {
        RoutedHttpValidation.RequireSha256(value, parameterName);
        if (length == 0 && !string.Equals(value, EmptySha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("A zero-byte stream must carry the SHA-256 of empty bytes.", parameterName);
        }

        return value;
    }

    public static string RequireRunName(string value, string parameterName)
    {
        RequireBoundedAscii(value, parameterName);
        if (!value.EndsWith(".py", StringComparison.Ordinal))
        {
            throw new ArgumentException("A census run name must be an exact Python probe filename.", parameterName);
        }

        return value;
    }

    public static string RequireToken(string value, string parameterName)
    {
        RequireBoundedAscii(value, parameterName);
        if (value.Any(static character => character is '/' or '\\' or ':'))
        {
            throw new ArgumentException("Logical execution tokens cannot contain physical path syntax.", parameterName);
        }

        return value;
    }

    public static string RequireLogicalArgument(string value, string parameterName)
    {
        RequireBoundedValue(value, parameterName, 8192);
        if (value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("\\", StringComparison.Ordinal) ||
            value.Contains('\\') || value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
        {
            throw new ArgumentException("Census arguments cannot retain physical absolute paths.", parameterName);
        }

        return value;
    }

    public static string RequireEnvironmentName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!EnvironmentNames.Contains(value))
        {
            throw new ArgumentException("The census environment name is outside the closed allowlist.", parameterName);
        }

        return value;
    }

    public static IReadOnlyList<LicenceCensusEnvironmentVariable> RequireEnvironment(
        IReadOnlyList<LicenceCensusEnvironmentVariable> values,
        string parameterName) => RequireSortedUnique(values, static value => value.Name, parameterName);

    public static string RequireBoundedAscii(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(static character => character is < '!' or > '~'))
        {
            throw new ArgumentException("A census execution scalar must be bounded printable ASCII.", parameterName);
        }

        return value;
    }

    public static string RequireBoundedValue(string value, string parameterName, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        int length;
        try
        {
            length = RoutedHttpValidation.StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("A census string must be strict UTF-8.", parameterName, exception);
        }

        if (length == 0 || length > maximumBytes || value.Contains('\0') || value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException("A census string must be one bounded nonempty scalar.", parameterName);
        }

        return value;
    }

    public static ulong RequireOutputLength(ulong value, string parameterName)
    {
        if (value > MaximumOutputBytes)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Captured process output exceeds the OBS-02 byte cap.");
        }

        return value;
    }

    public static IReadOnlyList<T> RequireSortedUnique<T>(
        IReadOnlyList<T> values,
        Func<T, string> key,
        string parameterName)
        where T : class
    {
        var frozen = values.ToArray();
        string? previous = null;
        for (var index = 0; index < frozen.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(frozen[index], $"{parameterName}[{index}]");
            var current = key(frozen[index]);
            if (previous is not null && CompareUnsignedUtf8(previous, current) >= 0)
            {
                throw new ArgumentException("A census collection must be strictly ASCII-sorted and duplicate-free.", parameterName);
            }

            previous = current;
        }

        return frozen;
    }

    private static int CompareUnsignedUtf8(string left, string right)
    {
        var leftBytes = RoutedHttpValidation.StrictUtf8.GetBytes(left);
        var rightBytes = RoutedHttpValidation.StrictUtf8.GetBytes(right);
        var sharedLength = Math.Min(leftBytes.Length, rightBytes.Length);
        for (var index = 0; index < sharedLength; index++)
        {
            var comparison = leftBytes[index].CompareTo(rightBytes[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftBytes.Length.CompareTo(rightBytes.Length);
    }
}

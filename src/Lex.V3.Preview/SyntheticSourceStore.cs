using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;

namespace Lex.V3.Preview;

internal enum SyntheticBuildFailpoint
{
    SourcePartialWritten,
    SourceFlushed,
    SourceRenamed,
    BeforeDecode,
    RejectedReceiptFlushed,
    RejectedReceiptRenamed,
    DerivedFlushed,
    DerivedRenamed,
}

internal enum SyntheticRecoveryKind
{
    EmptyOrPartial,
    TransportPersisted,
    DecodeRejected,
    SuccessfulOutputPresent,
}

internal sealed record SyntheticRejectedReceipt(
    string Schema,
    string SourceSha256,
    long SourceBytes,
    string Reason,
    string State);

internal sealed record SyntheticTransportResult(
    string SourcePath,
    string SourceSha256,
    long SourceBytes,
    string DerivedPath,
    string DerivedSha256,
    long DerivedBytes,
    byte[] DerivedUtf8);

internal sealed record SyntheticRecoveryState(
    SyntheticRecoveryKind Kind,
    string? SourcePath,
    string? SourceSha256,
    SyntheticRejectedReceipt? RejectedReceipt,
    bool HasDerivedOrIndexOutput);

internal sealed class RejectedSyntheticBuildException(
    string sourcePath,
    string sourceSha256,
    string receiptPath,
    SyntheticDerivationException innerException)
    : Exception("Synthetic build stopped after a durable decode rejection.", innerException)
{
    internal string SourcePath { get; } = sourcePath;

    internal string SourceSha256 { get; } = sourceSha256;

    internal string ReceiptPath { get; } = receiptPath;
}

internal static class SyntheticSourceStore
{
    private const int SourceLimit = 4 * 1024;
    private const int ReceiptLimit = 4 * 1024;
    private const string ReceiptFileName = "rejected-build.json";

    internal static SyntheticTransportResult PersistAndNormalize(
        string buildRoot,
        ReadOnlySpan<byte> source,
        Action<SyntheticBuildFailpoint>? failpoint = null)
    {
        if (source.Length > SourceLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(source), $"Source exceeds {SourceLimit} bytes.");
        }

        var root = PrepareEmptyRoot(buildRoot);
        var partialPath = Path.Combine(root, "source.partial");
        string sourceSha256;
        using (var stream = OpenNewWriteThrough(partialPath))
        {
            stream.Write(source);
            failpoint?.Invoke(SyntheticBuildFailpoint.SourcePartialWritten);
            stream.Flush(flushToDisk: true);
            failpoint?.Invoke(SyntheticBuildFailpoint.SourceFlushed);
            stream.Position = 0;
            sourceSha256 = DigestFraming.Hash(stream);
        }

        var sourcePath = Path.Combine(root, $"source.{sourceSha256}.bin");
        File.Move(partialPath, sourcePath, overwrite: false);
        failpoint?.Invoke(SyntheticBuildFailpoint.SourceRenamed);
        var persistedSource = ReadBoundedAndVerify(sourcePath, SourceLimit, sourceSha256);

        failpoint?.Invoke(SyntheticBuildFailpoint.BeforeDecode);
        byte[] derived;
        try
        {
            derived = SyntheticTextNormalizer.Normalize(persistedSource);
        }
        catch (SyntheticDerivationException exception)
        {
            var reason = exception.Code switch
            {
                SyntheticDerivationFailureCode.InvalidUtf8 => "invalid_utf8",
                SyntheticDerivationFailureCode.Utf8BomForbidden => "utf8_bom_forbidden",
                SyntheticDerivationFailureCode.NoVisibleContent => "no_visible_content",
                _ => throw new InvalidDataException("Unknown synthetic derivation rejection.", exception),
            };
            var receipt = new SyntheticRejectedReceipt(
                "rejected-build/1",
                sourceSha256,
                persistedSource.LongLength,
                reason,
                "transport_persisted_decode_rejected");
            var receiptPath = PublishRejectedReceipt(root, receipt, failpoint);
            throw new RejectedSyntheticBuildException(sourcePath, sourceSha256, receiptPath, exception);
        }

        var derivedSha256 = DigestFraming.Hash(derived);
        var derivedPath = Path.Combine(root, $"derived.{derivedSha256}.txt");
        PublishAtomic(
            Path.Combine(root, "derived.partial"),
            derivedPath,
            derived,
            () => failpoint?.Invoke(SyntheticBuildFailpoint.DerivedFlushed),
            () => failpoint?.Invoke(SyntheticBuildFailpoint.DerivedRenamed));

        return new SyntheticTransportResult(
            sourcePath,
            sourceSha256,
            persistedSource.LongLength,
            derivedPath,
            derivedSha256,
            derived.LongLength,
            derived);
    }

    internal static SyntheticRecoveryState Recover(string buildRoot)
    {
        var root = Path.GetFullPath(buildRoot);
        if (!Directory.Exists(root))
        {
            return new SyntheticRecoveryState(
                SyntheticRecoveryKind.EmptyOrPartial,
                null,
                null,
                null,
                false);
        }

        var sources = Directory.EnumerateFiles(root, "source.*.bin", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var hasOutput = VerifySuccessfulOutput(root);
        if (sources.Length == 0)
        {
            if (hasOutput)
            {
                throw new InvalidDataException("Successful output cannot exist without its published source.");
            }

            return new SyntheticRecoveryState(
                SyntheticRecoveryKind.EmptyOrPartial,
                null,
                null,
                null,
                false);
        }

        if (sources.Length != 1)
        {
            throw new InvalidDataException("Recovery requires exactly one published source.");
        }

        var sourcePath = sources[0];
        var sourceSha256 = ParseContentAddress(sourcePath, "source.", ".bin");
        var sourceBytes = ReadBoundedAndVerify(sourcePath, SourceLimit, sourceSha256);
        var receiptPath = Path.Combine(root, ReceiptFileName);
        if (!File.Exists(receiptPath))
        {
            return new SyntheticRecoveryState(
                hasOutput ? SyntheticRecoveryKind.SuccessfulOutputPresent : SyntheticRecoveryKind.TransportPersisted,
                sourcePath,
                sourceSha256,
                null,
                hasOutput);
        }

        var receipt = ReadRejectedReceipt(receiptPath);
        if (!string.Equals(receipt.SourceSha256, sourceSha256, StringComparison.Ordinal) ||
            receipt.SourceBytes != sourceBytes.LongLength)
        {
            throw new InvalidDataException("Rejected-build receipt does not bind the published source.");
        }

        if (hasOutput)
        {
            throw new InvalidDataException("A decode-rejected build cannot expose derived or index output.");
        }

        return new SyntheticRecoveryState(
            SyntheticRecoveryKind.DecodeRejected,
            sourcePath,
            sourceSha256,
            receipt,
            false);
    }

    private static string PrepareEmptyRoot(string buildRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildRoot);
        var root = Path.GetFullPath(buildRoot);
        Directory.CreateDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException("Synthetic build root must be empty.");
        }

        return root;
    }

    private static FileStream OpenNewWriteThrough(string path) => new(
        path,
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        bufferSize: 4096,
        FileOptions.WriteThrough);

    private static byte[] ReadBoundedAndVerify(string path, int limit, string expectedSha256)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException("Published members must be real files.");
        }

        var length = new FileInfo(path).Length;
        if (length < 0 || length > limit)
        {
            throw new InvalidDataException($"Published member exceeds {limit} bytes.");
        }

        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(DigestFraming.Hash(bytes), expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Published member digest does not match its content address.");
        }

        return bytes;
    }

    private static string PublishRejectedReceipt(
        string root,
        SyntheticRejectedReceipt receipt,
        Action<SyntheticBuildFailpoint>? failpoint)
    {
        var bytes = SerializeRejectedReceipt(receipt);
        if (bytes.Length > ReceiptLimit)
        {
            throw new InvalidDataException("Rejected-build receipt exceeds its bound.");
        }

        var receiptPath = Path.Combine(root, ReceiptFileName);
        PublishAtomic(
            Path.Combine(root, "rejected-build.partial"),
            receiptPath,
            bytes,
            () => failpoint?.Invoke(SyntheticBuildFailpoint.RejectedReceiptFlushed),
            () => failpoint?.Invoke(SyntheticBuildFailpoint.RejectedReceiptRenamed));
        return receiptPath;
    }

    private static void PublishAtomic(
        string partialPath,
        string finalPath,
        ReadOnlySpan<byte> bytes,
        Action afterFlush,
        Action afterRename)
    {
        using (var stream = OpenNewWriteThrough(partialPath))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            afterFlush();
        }

        File.Move(partialPath, finalPath, overwrite: false);
        afterRename();
    }

    private static byte[] SerializeRejectedReceipt(SyntheticRejectedReceipt receipt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", receipt.Schema);
            writer.WriteString("source_sha256", receipt.SourceSha256);
            writer.WriteNumber("source_bytes", receipt.SourceBytes);
            writer.WriteString("reason", receipt.Reason);
            writer.WriteString("state", receipt.State);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static SyntheticRejectedReceipt ReadRejectedReceipt(string path)
    {
        var length = new FileInfo(path).Length;
        if (length < 0 || length > ReceiptLimit)
        {
            throw new InvalidDataException("Rejected-build receipt exceeds its bound.");
        }

        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Rejected-build receipt must be an object.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value.Clone()))
            {
                throw new InvalidDataException("Rejected-build receipt has a duplicate member.");
            }
        }

        var expected = new[] { "schema", "source_sha256", "source_bytes", "reason", "state" };
        if (properties.Count != expected.Length || expected.Any(name => !properties.ContainsKey(name)))
        {
            throw new InvalidDataException("Rejected-build receipt shape is not exact.");
        }

        var schema = ReadString(properties, "schema");
        var sourceSha256 = ReadString(properties, "source_sha256");
        var sourceBytes = ReadInt64(properties, "source_bytes");
        var reason = ReadString(properties, "reason");
        var state = ReadString(properties, "state");
        if (!string.Equals(schema, "rejected-build/1", StringComparison.Ordinal) ||
            !IsLowerHexSha256(sourceSha256) ||
            sourceBytes < 0 ||
            (reason is not "invalid_utf8" and not "utf8_bom_forbidden" and not "no_visible_content") ||
            !string.Equals(state, "transport_persisted_decode_rejected", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Rejected-build receipt violates a typed invariant.");
        }

        return new SyntheticRejectedReceipt(schema, sourceSha256, sourceBytes, reason, state);
    }

    private static string ReadString(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        if (properties[name].ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Rejected-build receipt member {name} must be a string.");
        }

        return properties[name].GetString()!;
    }

    private static long ReadInt64(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        if (properties[name].ValueKind != JsonValueKind.Number || !properties[name].TryGetInt64(out var value))
        {
            throw new InvalidDataException($"Rejected-build receipt member {name} must be an integer.");
        }

        return value;
    }

    private static string ParseContentAddress(string path, string prefix, string suffix)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
            !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Published source name is malformed.");
        }

        var digest = name[prefix.Length..^suffix.Length];
        if (!IsLowerHexSha256(digest))
        {
            throw new InvalidDataException("Published source content address is malformed.");
        }

        return digest;
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool VerifySuccessfulOutput(string root)
    {
        var derived = Directory.EnumerateFiles(root, "derived.*.txt", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var indexes = Directory.EnumerateFiles(root, "index.*.sqlite", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (derived.Length == 0 && indexes.Length == 0)
        {
            return false;
        }

        if (derived.Length != 1 || indexes.Length != 1)
        {
            throw new InvalidDataException(
                "Successful recovery requires exactly one derived member and one SQLite index.");
        }

        var derivedSha256 = ParseContentAddress(derived[0], "derived.", ".txt");
        _ = ReadBoundedAndVerify(
            derived[0],
            SyntheticSliceContractLimits.MaximumDerivedBytes,
            derivedSha256);
        var indexSha256 = ParseContentAddress(indexes[0], "index.", ".sqlite");
        _ = ReadBoundedAndVerify(
            indexes[0],
            SyntheticSliceContractLimits.MaximumSqliteBytes,
            indexSha256);
        return true;
    }
}

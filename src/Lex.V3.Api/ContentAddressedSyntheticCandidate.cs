using Lex.V3.Artifacts;
using Lex.V3.Contracts;

namespace Lex.V3.Api;

internal sealed class ContentAddressedSyntheticCandidate : ISyntheticSliceCandidate
{
    private const string ManifestFileName = "artifact.json";
    private readonly string root;

    public ContentAddressedSyntheticCandidate(string graphRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphRoot);
        root = Path.GetFullPath(graphRoot);
    }

    public ValueTask<Stream> OpenAdmissionManifestAsync(CancellationToken cancellationToken) =>
        OpenAsync(Path.Combine(root, ManifestFileName), cancellationToken);

    public ValueTask<Stream> OpenControlAsync(string sha256, CancellationToken cancellationToken) =>
        OpenAsync(PathForControl(sha256), cancellationToken);

    public ValueTask<Stream> OpenBlobAsync(
        SyntheticSliceBlobKind kind,
        string sha256,
        CancellationToken cancellationToken) =>
        OpenAsync(PathForBlob(kind, sha256), cancellationToken);

    internal string PathForSqlite(string sha256) =>
        PathForBlob(SyntheticSliceBlobKind.SqliteIndex, sha256);

    private string PathForControl(string sha256) =>
        Path.Combine(root, $"control.{RequireSha256(sha256)}.json");

    private string PathForBlob(SyntheticSliceBlobKind kind, string sha256)
    {
        var (prefix, extension) = kind switch
        {
            SyntheticSliceBlobKind.SourceTransport => ("source_transport", ".bin"),
            SyntheticSliceBlobKind.DerivedText => ("derived_text", ".txt"),
            SyntheticSliceBlobKind.SqliteIndex => ("sqlite_index", ".sqlite3"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return Path.Combine(root, $"{prefix}.{RequireSha256(sha256)}{extension}");
    }

    private static ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Synthetic graph members cannot be reparse points.");
        }

        Stream stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                Share = FileShare.Read,
            });
        return ValueTask.FromResult(stream);
    }

    private static string RequireSha256(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64 || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 digest is required.", nameof(value));
        }

        return value;
    }
}

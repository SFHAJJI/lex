using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.Index;

/// <summary>
/// A signature over the release as a whole. Unlike the stamp inside one database, the trust
/// root is supplied by the application release and the manifest can bind every companion file.
/// </summary>
public static class ArtifactManifests
{
    public const string Schema = "lex-artifacts/1";
    public const string Algorithm = "ECDSA-P256-SHA256";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static ArtifactManifest Create(
        string rootDirectory,
        IEnumerable<string> relativePaths,
        string keyId,
        string createdAt,
        string codeCommit,
        IReadOnlyDictionary<string, string>? sources = null)
    {
        var files = relativePaths.Select(path =>
        {
            var normalized = NormalizeRelativePath(path);
            var full = ResolveInside(rootDirectory, normalized);
            using var stream = File.OpenRead(full);
            return new ArtifactFile(
                normalized,
                KindOf(normalized),
                stream.Length,
                Convert.ToHexStringLower(SHA256.HashData(stream)));
        }).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();

        if (files.Length == 0) throw new InvalidOperationException("An artifact manifest cannot be empty.");
        var sortedSources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in sources ?? new Dictionary<string, string>())
            sortedSources[key] = value;
        return new ArtifactManifest(
            Schema,
            Algorithm,
            keyId,
            createdAt,
            codeCommit,
            sortedSources,
            files);
    }

    public static byte[] Serialize(ArtifactManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, Json);

    public static ArtifactManifest Parse(ReadOnlySpan<byte> bytes)
    {
        var manifest = JsonSerializer.Deserialize<ArtifactManifest>(bytes, Json)
            ?? throw new InvalidDataException("Artifact manifest is empty.");
        if (manifest.Schema != Schema)
            throw new InvalidDataException($"Unsupported artifact manifest schema '{manifest.Schema}'.");
        if (manifest.Algorithm != Algorithm)
            throw new InvalidDataException($"Unsupported artifact signature algorithm '{manifest.Algorithm}'.");
        if (manifest.Files.Count == 0) throw new InvalidDataException("Artifact manifest has no files.");
        return manifest;
    }

    public static string SignBase64(ReadOnlySpan<byte> manifestBytes, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        var signature = key.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(signature);
    }

    public static ArtifactTrustRoot TrustRoot(string keyId, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        var der = key.ExportSubjectPublicKeyInfo();
        return new ArtifactTrustRoot(
            keyId,
            Convert.ToHexStringLower(SHA256.HashData(der)),
            key.ExportSubjectPublicKeyInfoPem());
    }

    public static IReadOnlySet<string> VerifyDirectory(
        string rootDirectory,
        string manifestPath,
        string signaturePath,
        IEnumerable<ArtifactTrustRoot> trustRoots)
    {
        var manifestBytes = File.ReadAllBytes(manifestPath);
        var manifest = Parse(manifestBytes);
        var trust = trustRoots.SingleOrDefault(r => r.KeyId == manifest.KeyId)
            ?? throw new CryptographicException($"Artifact key '{manifest.KeyId}' is not trusted by this release.");

        using var key = ECDsa.Create();
        key.ImportFromPem(trust.PublicKeyPem);
        var actualFingerprint = Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualFingerprint),
                Encoding.ASCII.GetBytes(trust.FingerprintSha256.ToLowerInvariant())))
            throw new CryptographicException($"Pinned fingerprint for artifact key '{manifest.KeyId}' does not match its public key.");

        byte[] signature;
        try { signature = DecodeBase64Signature(File.ReadAllText(signaturePath).Trim()); }
        catch (FormatException ex) { throw new CryptographicException("Artifact signature is not valid base64 or base64url.", ex); }
        if (!key.VerifyData(
                manifestBytes,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new CryptographicException("Artifact manifest signature is invalid.");

        var verified = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            var normalized = NormalizeRelativePath(file.Path);
            if (!verified.Add(normalized)) throw new InvalidDataException($"Artifact '{normalized}' is listed more than once.");
            var full = ResolveInside(rootDirectory, normalized);
            if (!File.Exists(full)) throw new FileNotFoundException($"Artifact '{normalized}' is missing.", full);
            var info = new FileInfo(full);
            if (info.Length != file.Size)
                throw new InvalidDataException($"Artifact '{normalized}' has size {info.Length}, expected {file.Size}.");
            using var stream = File.OpenRead(full);
            var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(digest),
                    Encoding.ASCII.GetBytes(file.Sha256.ToLowerInvariant())))
                throw new CryptographicException($"Artifact '{normalized}' failed its SHA-256 check.");
        }
        return verified;
    }

    public static IReadOnlyList<ArtifactTrustRoot> ParseTrustRoots(ReadOnlySpan<byte> bytes)
    {
        var roots = JsonSerializer.Deserialize<ArtifactTrustRoot[]>(bytes, Json)
            ?? throw new InvalidDataException("Artifact trust-root file is empty.");
        if (roots.Length == 0) throw new InvalidDataException("Artifact trust-root file contains no roots.");
        if (roots.Select(r => r.KeyId).Distinct(StringComparer.Ordinal).Count() != roots.Length)
            throw new InvalidDataException("Artifact trust-root key ids must be unique.");
        return roots;
    }

    public static byte[] SerializeTrustRoots(IEnumerable<ArtifactTrustRoot> roots) =>
        JsonSerializer.SerializeToUtf8Bytes(roots.OrderBy(r => r.KeyId, StringComparer.Ordinal), Json);

    private static byte[] DecodeBase64Signature(string value)
    {
        if (!value.Contains('-') && !value.Contains('_')) return Convert.FromBase64String(value);
        var standard = value.Replace('-', '+').Replace('_', '/');
        standard += (standard.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(standard);
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException($"Artifact path '{path}' is not a safe relative path.");
        return normalized;
    }

    private static string ResolveInside(string rootDirectory, string relativePath)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison))
            throw new InvalidDataException($"Artifact path '{relativePath}' escapes its release directory.");
        return full;
    }

    private static string KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".db" => "sqlite-index",
        ".onnx" => "embedding-model",
        ".json" => "json",
        ".bin" => "vectors",
        _ => "file",
    };
}

public sealed record ArtifactManifest(
    string Schema,
    string Algorithm,
    string KeyId,
    string CreatedAt,
    string CodeCommit,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyList<ArtifactFile> Files);

public sealed record ArtifactFile(string Path, string Kind, long Size, string Sha256);

public sealed record ArtifactTrustRoot(string KeyId, string FingerprintSha256, string PublicKeyPem);

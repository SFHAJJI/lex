using Lex.Index;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Lex.Web;

/// <summary>
/// The mounted indexes, one per publisher (D27), as a service rather than as a local variable
/// captured by twenty-five route lambdas.
///
/// Publishers are independent by design, so one unreadable index must not take the others down
/// with it: a refusal is logged loudly and the rest still mount. <c>/coverage</c> then reports the
/// survivors, which is the honest state of the service rather than a blank page.
/// </summary>
public sealed class IndexRegistry : IDisposable
{
    private readonly Dictionary<string, LexIndexReader> _readers = new(StringComparer.Ordinal);
    private readonly List<VerifiedArtifactManifest> _artifactManifests = [];
    private readonly HashSet<string> _verifiedFiles = new(StringComparer.Ordinal);
    private readonly MultilingualE5Encoder? _encoder;

    public IndexRegistry(IOptions<LexOptions> options, ILogger<IndexRegistry> log)
    {
        var dir = options.Value.IndexDir;
        if (!Directory.Exists(dir))
        {
            log.LogWarning("No index directory at {Dir}; the service will report empty coverage.", dir);
            return;
        }

        var manifests = Directory.EnumerateFiles(dir, "*.manifest.json").Order(StringComparer.Ordinal).ToList();
        if (manifests.Count == 0 && options.Value.RequireArtifactManifest)
        {
            log.LogCritical("Artifact manifests are required but none exist in {Dir}; no index will be mounted.", dir);
            return;
        }
        try
        {
            foreach (var manifest in manifests)
            {
                var signature = manifest[..^".json".Length] + ".sig";
                var manifestBytes = File.ReadAllBytes(manifest);
                foreach (var path in ArtifactManifests.VerifyDirectory(
                             dir, manifest, signature, ArtifactTrustStore.Roots))
                    _verifiedFiles.Add(path);
                var parsed = ArtifactManifests.Parse(manifestBytes);
                _artifactManifests.Add(new VerifiedArtifactManifest(
                    Path.GetFileName(manifest),
                    Convert.ToHexStringLower(SHA256.HashData(manifestBytes)),
                    parsed.KeyId,
                    parsed.CodeCommit,
                    parsed.CreatedAt,
                    parsed.Files.Select(f => f.Path).ToArray()));
                log.LogInformation("Verified artifact manifest {Manifest}", Path.GetFileName(manifest));
            }
        }
        catch (Exception ex)
        {
            log.LogCritical("Artifact verification failed; no index will be mounted: {Reason}", ex.Message);
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.Value.EmbeddingModelDir))
        {
            try
            {
                var modelDir = Path.GetFullPath(options.Value.EmbeddingModelDir);
                if (manifests.Count > 0)
                    foreach (var file in new[] { "model-manifest.json", "model.onnx", "sentencepiece.bpe.model" })
                    {
                        var relative = Path.GetRelativePath(dir, Path.Combine(modelDir, file)).Replace('\\', '/');
                        if (!_verifiedFiles.Contains(relative))
                            throw new InvalidDataException($"embedding artifact '{relative}' is not in a verified manifest");
                    }
                _encoder = MultilingualE5Encoder.Open(modelDir);
                log.LogInformation("Loaded local embedding model {Model} at {Revision}",
                    _encoder.ModelId, _encoder.ModelRevision);
            }
            catch (Exception ex)
            {
                log.LogError("Hybrid retrieval remains disabled: {Reason}", ex.Message);
            }
        }

        foreach (var db in Directory.EnumerateFiles(dir, "index-*.db"))
        {
            try
            {
                if (manifests.Count > 0 && !_verifiedFiles.Contains(Path.GetFileName(db)))
                    throw new InvalidDataException("the database is not listed by a verified artifact manifest");
                var vectorPath = Path.ChangeExtension(db, ".vectors");
                var vectorRelative = Path.GetRelativePath(dir, vectorPath).Replace('\\', '/');
                var hybridReady = _encoder is not null && File.Exists(vectorPath)
                                  && (manifests.Count == 0 || _verifiedFiles.Contains(vectorRelative));
                LexIndexReader reader;
                if (hybridReady)
                {
                    try
                    {
                        reader = LexIndexReader.Open(db, _encoder, vectorPath);
                    }
                    catch (Exception ex)
                    {
                        // Semantic vectors are a derived acceleration artifact. A corrupt or stale
                        // sidecar must disable hybrid retrieval for this publisher, not hide the
                        // independently verified legal index and its deterministic keyword search.
                        log.LogError(
                            "Hybrid retrieval disabled for {Db}; mounting verified lexical index: {Reason}",
                            Path.GetFileName(db), ex.Message);
                        reader = LexIndexReader.Open(db);
                    }
                }
                else
                {
                    reader = LexIndexReader.Open(db);
                }
                _readers[reader.Collection] = reader;
                log.LogInformation("Mounted {Db} ({Collection}, signature_valid={Valid})",
                    db, reader.Collection, reader.SignatureValid);
            }
            catch (Exception ex)
            {
                // Deliberately swallowed: see the class remarks. The message names the file and
                // the reason, which is what a stale schema needs in order to be diagnosed.
                log.LogError("Refused {Db}: {Reason}", db, ex.Message);
            }
        }

        if (_readers.Count == 0)
            log.LogWarning("No indexes mounted from {Dir}.", dir);
    }

    public IReadOnlyDictionary<string, LexIndexReader> All => _readers;

    public IReadOnlyList<VerifiedArtifactManifest> VerifiedArtifactManifests => _artifactManifests;

    public bool IsArtifactVerified(string relativePath) =>
        _verifiedFiles.Contains(relativePath.Replace('\\', '/'));

    public IEnumerable<LexIndexReader> Values => _readers.Values;

    public int Count => _readers.Count;

    public bool TryGet(string collection, out LexIndexReader reader) =>
        _readers.TryGetValue(collection, out reader!);

    /// <summary>The readers a request should ask, narrowed to one publisher when named.</summary>
    public IEnumerable<LexIndexReader> For(string? publisher) =>
        publisher is null ? _readers.Values : _readers.Values.Where(r => r.Collection == publisher);

    public void Dispose()
    {
        foreach (var r in _readers.Values) r.Dispose();
        _readers.Clear();
        _encoder?.Dispose();
    }
}

public sealed record VerifiedArtifactManifest(
    string File,
    string Sha256,
    string KeyId,
    string CodeCommit,
    string CreatedAt,
    IReadOnlyList<string> Artifacts);

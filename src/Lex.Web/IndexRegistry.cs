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

    public IndexRegistry(IOptions<LexOptions> options, ILogger<IndexRegistry> log)
    {
        var dir = options.Value.IndexDir;
        if (!Directory.Exists(dir))
        {
            log.LogWarning("No index directory at {Dir}; the service will report empty coverage.", dir);
            return;
        }

        var manifests = Directory.EnumerateFiles(dir, "*.manifest.json").Order(StringComparer.Ordinal).ToList();
        var verifiedFiles = new HashSet<string>(StringComparer.Ordinal);
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
                    verifiedFiles.Add(path);
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

        foreach (var db in Directory.EnumerateFiles(dir, "index-*.db"))
        {
            try
            {
                if (manifests.Count > 0 && !verifiedFiles.Contains(Path.GetFileName(db)))
                    throw new InvalidDataException("the database is not listed by a verified artifact manifest");
                var reader = LexIndexReader.Open(db);
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
    }
}

public sealed record VerifiedArtifactManifest(
    string File,
    string Sha256,
    string KeyId,
    string CodeCommit,
    string CreatedAt,
    IReadOnlyList<string> Artifacts);

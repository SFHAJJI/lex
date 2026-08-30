using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Preview;

if (args is ["source-digest", var previewProjectRoot])
{
    try
    {
        Console.WriteLine(SyntheticPreviewSourceDigest.Compute(previewProjectRoot));
        return 0;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Synthetic preview source digest failed.");
        return 1;
    }
}

if (args.Length == 1)
{
    try
    {
        WriteBuild(SyntheticPreviewBuilder.BuildCanonical(args[0]));
        return 0;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Synthetic preview build failed.");
        return 1;
    }
}

if (args is ["sign", var graphRoot, var privateKeyPath, var environmentBinding,
    var issuerId, var keyId, var builderSourceSha256])
{
    try
    {
        using var signingKey = LoadPrivateKey(privateKeyPath);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            graphRoot,
            signingKey,
            environmentBinding,
            issuerId,
            keyId,
            builderSourceSha256);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "lex-v3-synthetic-public-graph-result/1",
            manifest_sha256 = graph.ManifestSha256,
            control_sha256 = graph.ControlSha256,
            public_key_sha256 = Convert.ToHexStringLower(SHA256.HashData(
                signingKey.ExportSubjectPublicKeyInfo())),
            build_identity = graph.Build.BuildIdentity,
            sqlite_sha256 = graph.Build.SqliteSha256,
        }));
        return 0;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Synthetic preview signing failed.");
        return 1;
    }
}

if (args is ["verify-unsigned", var candidateRoot, var rebuildRoot, var expectedBuilderSourceSha256])
{
    try
    {
        var verification = SyntheticPublicGraphBuilder.VerifyUnsignedGraph(
            candidateRoot,
            rebuildRoot,
            expectedBuilderSourceSha256);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "lex-v3-synthetic-unsigned-rebuild/1",
            control_sha256 = verification.ControlSha256,
            source_sha256 = verification.Build.SourceSha256,
            derived_sha256 = verification.Build.DerivedSha256,
            sqlite_sha256 = verification.Build.SqliteSha256,
            build_identity = verification.Build.BuildIdentity,
        }));
        return 0;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Synthetic preview unsigned verification failed.");
        return 1;
    }
}

Console.Error.WriteLine(
    "Usage: Lex.V3.Preview source-digest <preview-project-root> | <empty-build-root> | " +
    "sign <empty-graph-root> <private-key-file> " +
    "<environment-binding> <issuer-id> <key-id> <builder-source-sha256> | " +
    "verify-unsigned <graph-root> <empty-rebuild-root> <expected-builder-source-sha256>");
return 64;

static void WriteBuild(SyntheticPreviewBuildResult build)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schema = "lex-v3-synthetic-build-result/1",
        source = new
        {
            file = Path.GetFileName(build.SourcePath),
            sha256 = build.SourceSha256,
            bytes = build.SourceBytes,
        },
        derived = new
        {
            file = Path.GetFileName(build.DerivedPath),
            sha256 = build.DerivedSha256,
            bytes = build.DerivedBytes,
        },
        sqlite = new
        {
            file = Path.GetFileName(build.SqlitePath),
            sha256 = build.SqliteSha256,
            bytes = build.SqliteBytes,
            version = build.SqliteProvenance.Version,
            source_id = build.SqliteProvenance.SourceId,
            compile_options_sha256 = build.SqliteProvenance.CompileOptionsSha256,
        },
        profile_identity = build.NormalizationProfileIdentity,
        profile_sha256 = build.NormalizationProfileSha256,
        ddl_sha256 = build.DdlSha256,
        scope_sha256 = build.ScopeSha256,
        logical_rows_sha256 = build.LogicalRowsSha256,
        build_identity = build.BuildIdentity,
    }));
}

static ECDsa LoadPrivateKey(string path)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
    {
        throw new InvalidDataException("Signing input cannot be a reparse point.");
    }

    var length = new FileInfo(path).Length;
    if (length is <= 0 or > 16_384)
    {
        throw new InvalidDataException("Signing input is outside its bound.");
    }

    var bytes = File.ReadAllBytes(path);
    var characters = Encoding.ASCII.GetChars(bytes);
    var key = ECDsa.Create();
    try
    {
        key.ImportFromPem(characters);
        return key;
    }
    catch
    {
        key.Dispose();
        throw;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(bytes);
        characters.AsSpan().Clear();
    }
}

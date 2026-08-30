using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;

namespace Lex.V3.Preview;

public sealed record SyntheticPublicGraphResult(
    SyntheticPreviewBuildResult Build,
    string ManifestPath,
    string ManifestSha256,
    string ControlPath,
    string ControlSha256,
    IReadOnlyList<string> PublicMemberPaths);

public sealed record SyntheticUnsignedGraphVerification(
    SyntheticPreviewBuildResult Build,
    string ControlSha256);

public static class SyntheticPublicGraphBuilder
{
    private const string ManifestFileName = "artifact.json";
    private const string SnapshotId = "s0-05-snapshot";
    private const string BuilderComponentId = "s0-05-builder";

    public static SyntheticPublicGraphResult BuildAndSign(
        string graphRoot,
        ECDsa signingKey,
        string environmentBinding,
        string issuerId,
        string keyId,
        string builderSourceSha256) =>
        BuildAndSign(
            graphRoot,
            signingKey,
            environmentBinding,
            issuerId,
            keyId,
            builderSourceSha256,
            includeCandidate: true);

    internal static SyntheticPublicGraphResult BuildAndSign(
        string graphRoot,
        ECDsa signingKey,
        string environmentBinding,
        string issuerId,
        string keyId,
        string builderSourceSha256,
        bool includeCandidate)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        ValidateP256(signingKey);
        var environment = new PreviewEnvironment("preview", environmentBinding);
        var issuer = new PreviewIssuer("preview_attestor", issuerId, keyId);
        _ = new ComponentIdentity(BuilderComponentId, builderSourceSha256);
        var schemaTable = SyntheticSliceSchemaExporter.ExportSchemaTable();

        var build = SyntheticPreviewBuilder.Build(
            graphRoot,
            SyntheticPreviewBuildContract.CanonicalSourceUtf8,
            includeCandidate);
        var root = Path.GetFullPath(graphRoot);
        var sourcePath = RenameBuildMember(
            build.SourcePath,
            Path.Combine(root, $"source_transport.{build.SourceSha256}.bin"));
        var derivedPath = RenameBuildMember(
            build.DerivedPath,
            Path.Combine(root, $"derived_text.{build.DerivedSha256}.txt"));
        var sqlitePath = RenameBuildMember(
            build.SqlitePath,
            Path.Combine(root, $"sqlite_index.{build.SqliteSha256}.sqlite3"));
        build = build with
        {
            SourcePath = sourcePath,
            DerivedPath = derivedPath,
            SqlitePath = sqlitePath,
        };

        var controlBuild = CreateControl(build, builderSourceSha256, schemaTable);
        var manifest = CreateManifest(
            signingKey,
            environment,
            issuer,
            schemaTable,
            controlBuild.Descriptor);
        var manifestBytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(manifest));
        if (manifestBytes.Length > SyntheticSliceContractLimits.MaximumManifestBytes)
        {
            throw new InvalidDataException("Synthetic manifest exceeds 64 KiB.");
        }

        var controlPath = Path.Combine(root, $"control.{controlBuild.Sha256}.json");
        WriteNewDurableFile(controlPath, controlBuild.Bytes);
        var manifestPath = Path.Combine(root, ManifestFileName);
        WriteNewDurableFile(manifestPath, manifestBytes);
        var paths = Array.AsReadOnly(new[]
        {
            manifestPath,
            controlPath,
            sourcePath,
            derivedPath,
            sqlitePath,
        });
        return new SyntheticPublicGraphResult(
            build,
            manifestPath,
            DigestFraming.Hash(manifestBytes),
            controlPath,
            controlBuild.Sha256,
            paths);
    }

    public static SyntheticUnsignedGraphVerification VerifyUnsignedGraph(
        string graphRoot,
        string emptyRebuildRoot,
        string expectedBuilderSourceSha256)
    {
        var root = Path.GetFullPath(graphRoot);
        var manifestPath = Path.Combine(root, ManifestFileName);
        var manifestBytes = ReadBoundedFile(
            manifestPath,
            SyntheticSliceContractLimits.MaximumManifestBytes);
        var manifest = DeserializeCanonical<SyntheticSliceArtifactManifest>(manifestBytes, "manifest");
        var expectedSchemaTable = SyntheticSliceSchemaExporter.ExportSchemaTable();
        if (!SameSchemaTable(manifest.SchemaTable, expectedSchemaTable))
        {
            throw new InvalidDataException("Synthetic manifest schema table differs from tracked schemas.");
        }

        var controlPath = Path.Combine(root, $"control.{manifest.Control.Sha256}.json");
        var controlBytes = ReadBoundedFile(
            controlPath,
            SyntheticSliceContractLimits.MaximumControlBytes);
        if (controlBytes.LongLength != manifest.Control.Bytes ||
            !string.Equals(DigestFraming.Hash(controlBytes), manifest.Control.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Synthetic control descriptor does not match its bytes.");
        }

        var control = DeserializeCanonical<SyntheticSliceControl>(controlBytes, "control");
        _ = new ComponentIdentity(BuilderComponentId, expectedBuilderSourceSha256);
        if (!string.Equals(
                control.Builder.SourceSha256,
                expectedBuilderSourceSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Synthetic control has an unexpected builder source identity.");
        }

        var build = SyntheticPreviewBuilder.BuildCanonical(emptyRebuildRoot);
        var rebuiltControl = CreateControl(build, expectedBuilderSourceSha256, expectedSchemaTable);
        if (!controlBytes.AsSpan().SequenceEqual(rebuiltControl.Bytes) ||
            !string.Equals(manifest.Control.Sha256, rebuiltControl.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Synthetic control is not the deterministic rebuild output.");
        }

        CompareGraphMember(
            root,
            $"source_transport.{control.Blobs[0].Sha256}.bin",
            build.SourcePath,
            control.Blobs[0]);
        CompareGraphMember(
            root,
            $"derived_text.{control.Blobs[1].Sha256}.txt",
            build.DerivedPath,
            control.Blobs[1]);
        CompareGraphMember(
            root,
            $"sqlite_index.{control.Blobs[2].Sha256}.sqlite3",
            build.SqlitePath,
            control.Blobs[2]);
        RequireExactGraphFiles(root, manifest, control);
        return new SyntheticUnsignedGraphVerification(build, rebuiltControl.Sha256);
    }

    private static ControlBuild CreateControl(
        SyntheticPreviewBuildResult build,
        string builderSourceSha256,
        SyntheticSliceSchemaTable schemaTable)
    {
        var envelopeSchema = schemaTable.Members.Single(static member =>
            string.Equals(member.Schema, V3SchemaIds.SyntheticResolveEnvelope, StringComparison.Ordinal));
        var objectSetSchema = schemaTable.Members.Single(static member =>
            string.Equals(member.Schema, V3SchemaIds.PreviewObjectSet, StringComparison.Ordinal));
        var controlSchema = schemaTable.Members.Single(static member =>
            string.Equals(member.Schema, V3SchemaIds.SyntheticSliceControl, StringComparison.Ordinal));
        var blobs = new[]
        {
            new SyntheticSliceBlobDescriptor(
                SyntheticSliceBlobKind.SourceTransport,
                build.SourceSha256,
                build.SourceBytes,
                "application/octet-stream"),
            new SyntheticSliceBlobDescriptor(
                SyntheticSliceBlobKind.DerivedText,
                build.DerivedSha256,
                build.DerivedBytes,
                "text/plain;charset=utf-8"),
            new SyntheticSliceBlobDescriptor(
                SyntheticSliceBlobKind.SqliteIndex,
                build.SqliteSha256,
                build.SqliteBytes,
                "application/vnd.sqlite3"),
        };
        var control = new SyntheticSliceControl(
            V3SchemaIds.SyntheticSliceControl,
            V3SchemaResourceIds.SyntheticSliceControl,
            SyntheticResolveRequestContract.V1,
            SyntheticSliceOperationCatalog.Create(envelopeSchema.Sha256),
            PreviewRefusalRegistry.StageZero,
            objectSetSchema,
            SyntheticNormalizationProfile.PlainV1,
            SyntheticSliceScope.CompleteLu,
            new PreviewSnapshotReference(SnapshotId, build.BuildIdentity),
            new ComponentIdentity(BuilderComponentId, builderSourceSha256),
            new SyntheticSliceIndexStamp(
                SyntheticSliceIndexStamp.SchemaIdentity,
                build.DdlSha256,
                build.SqliteProvenance.Version,
                build.SqliteProvenance.SourceId,
                build.SqliteProvenance.CompileOptionsSha256,
                build.LogicalRowsSha256,
                build.ScopeSha256,
                build.BuildIdentity),
            blobs);
        var bytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(control));
        if (bytes.Length > SyntheticSliceContractLimits.MaximumControlBytes)
        {
            throw new InvalidDataException("Synthetic control exceeds 128 KiB.");
        }

        var sha256 = DigestFraming.Hash(bytes);
        return new ControlBuild(
            bytes,
            sha256,
            new SyntheticSliceControlDescriptor(
                V3SchemaIds.SyntheticSliceControl,
                V3SchemaResourceIds.SyntheticSliceControl,
                controlSchema.Sha256,
                sha256,
                bytes.LongLength,
                "application/json"));
    }

    private static SyntheticSliceArtifactManifest CreateManifest(
        ECDsa signingKey,
        PreviewEnvironment environment,
        PreviewIssuer issuer,
        SyntheticSliceSchemaTable schemaTable,
        SyntheticSliceControlDescriptor control)
    {
        var artifactSchema = schemaTable.Members.Single(static member =>
            string.Equals(member.Schema, V3SchemaIds.SyntheticSliceArtifact, StringComparison.Ordinal));
        var placeholder = CreateManifest(
            environment,
            issuer,
            schemaTable,
            artifactSchema.Sha256,
            control,
            new string('A', 86));
        var signature = signingKey.SignData(
            SyntheticSliceArtifactCanonicalizer.GetSigningBytes(placeholder),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (signature.Length != 64)
        {
            throw new CryptographicException("P-256 signing did not return a 64-byte P1363 signature.");
        }

        return CreateManifest(
            environment,
            issuer,
            schemaTable,
            artifactSchema.Sha256,
            control,
            Base64Url.Encode(signature));
    }

    private static SyntheticSliceArtifactManifest CreateManifest(
        PreviewEnvironment environment,
        PreviewIssuer issuer,
        SyntheticSliceSchemaTable schemaTable,
        string artifactSchemaSha256,
        SyntheticSliceControlDescriptor control,
        string signature) => new(
            V3SchemaIds.SyntheticSliceArtifact,
            V3SchemaResourceIds.SyntheticSliceArtifact,
            artifactSchemaSha256,
            "synthetic_preview",
            synthetic: true,
            "synthetic_test",
            environment,
            issuer,
            schemaTable,
            control,
            new PreviewAttestation(
                "preview_mechanics_only",
                "ECDSA-P256-SHA256",
                "ieee-p1363",
                signature));

    private static void ValidateP256(ECDsa signingKey)
    {
        if (signingKey.KeySize != 256 ||
            !string.Equals(
                signingKey.ExportParameters(includePrivateParameters: false).Curve.Oid.Value,
                ECCurve.NamedCurves.nistP256.Oid.Value,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Synthetic graph signing requires ECDSA P-256.", nameof(signingKey));
        }
    }

    private static string RenameBuildMember(string sourcePath, string graphPath)
    {
        File.Move(sourcePath, graphPath, overwrite: false);
        return graphPath;
    }

    private static void WriteNewDurableFile(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static byte[] ReadBoundedFile(string path, int maximumBytes)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Synthetic graph members cannot be reparse points.");
        }

        var length = new FileInfo(path).Length;
        if (length < 0 || length > maximumBytes)
        {
            throw new InvalidDataException("Synthetic graph member exceeds its bound.");
        }

        return File.ReadAllBytes(path);
    }

    private static T DeserializeCanonical<T>(byte[] bytes, string member)
    {
        try
        {
            var value = ContractJson.Deserialize<T>(new UTF8Encoding(false, true).GetString(bytes));
            if (!Encoding.UTF8.GetBytes(ContractJson.Serialize(value)).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException($"Synthetic {member} JSON is not canonical.");
            }

            return value;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new InvalidDataException($"Synthetic {member} JSON is invalid.", exception);
        }
    }

    private static void CompareGraphMember(
        string graphRoot,
        string fileName,
        string rebuiltPath,
        SyntheticSliceBlobDescriptor descriptor)
    {
        var graphBytes = ReadBoundedFile(
            Path.Combine(graphRoot, fileName),
            descriptor.Kind switch
            {
                SyntheticSliceBlobKind.SourceTransport => SyntheticSliceContractLimits.MaximumSourceBytes,
                SyntheticSliceBlobKind.DerivedText => SyntheticSliceContractLimits.MaximumDerivedBytes,
                SyntheticSliceBlobKind.SqliteIndex => SyntheticSliceContractLimits.MaximumSqliteBytes,
                _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
            });
        var rebuiltBytes = File.ReadAllBytes(rebuiltPath);
        if (graphBytes.LongLength != descriptor.Bytes ||
            !string.Equals(DigestFraming.Hash(graphBytes), descriptor.Sha256, StringComparison.Ordinal) ||
            !graphBytes.AsSpan().SequenceEqual(rebuiltBytes))
        {
            throw new InvalidDataException("Synthetic graph member differs from deterministic rebuild output.");
        }
    }

    private static void RequireExactGraphFiles(
        string root,
        SyntheticSliceArtifactManifest manifest,
        SyntheticSliceControl control)
    {
        var expected = new[]
        {
            ManifestFileName,
            $"control.{manifest.Control.Sha256}.json",
            $"source_transport.{control.Blobs[0].Sha256}.bin",
            $"derived_text.{control.Blobs[1].Sha256}.txt",
            $"sqlite_index.{control.Blobs[2].Sha256}.sqlite3",
        }.Order(StringComparer.Ordinal);
        var actual = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Synthetic graph root contains missing or extra files.");
        }
    }

    private static bool SameSchemaTable(
        SyntheticSliceSchemaTable actual,
        SyntheticSliceSchemaTable expected) =>
        actual.Members.Count == expected.Members.Count &&
        actual.Members.Zip(expected.Members).All(static pair =>
            string.Equals(pair.First.Schema, pair.Second.Schema, StringComparison.Ordinal) &&
            string.Equals(pair.First.SchemaResource, pair.Second.SchemaResource, StringComparison.Ordinal) &&
            string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.Ordinal) &&
            pair.First.Bytes == pair.Second.Bytes);

    private sealed record ControlBuild(
        byte[] Bytes,
        string Sha256,
        SyntheticSliceControlDescriptor Descriptor);
}

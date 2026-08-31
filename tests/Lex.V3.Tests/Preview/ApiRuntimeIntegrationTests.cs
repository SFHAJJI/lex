using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Preview;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class ApiRuntimeIntegrationTests
{
    private const string EnvironmentBinding = "s0-05-preview";
    private const string IssuerId = "s0-05-issuer";
    private const string KeyId = "s0-05-key";
    private const string BuilderSourceSha256 =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string RuntimeSourceSha256 =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [TestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    public async Task ApplicationOverlimitProductTargetIsFourHundredBeforeMethodAndState(string method)
    {
        var prefix = SyntheticResolveRequestContract.ProductPath + "?";
        var rawTarget = prefix + new string(
            'a',
            SyntheticResolveRequestContract.MaximumApplicationRawTargetByteCount + 1 - prefix.Length);

        var response = await InvokeAsync(SyntheticApiState.Unavailable, method, rawTarget);

        Assert.AreEqual(2049, rawTarget.Length);
        Assert.AreEqual(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.ContentType);
        Assert.AreEqual("no-store", response.CacheControl);
        Assert.AreEqual("nosniff", response.ContentTypeOptions);
        using var json = JsonDocument.Parse(response.Body);
        Assert.AreEqual(
            "urn:lex:v3:preview:invalid-request",
            json.RootElement.GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task AdmittedSqliteDrivesSuccessAndHelpfulRefusal()
    {
        using var graphRoot = new BuildTestDirectory();
        using var trustRoot = new BuildTestDirectory();
        Directory.CreateDirectory(trustRoot.Path);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            graphRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var publicKeyPath = Path.Combine(trustRoot.Path, "public-key.spki");
        await File.WriteAllBytesAsync(publicKeyPath, publicKey);
        using var state = await SyntheticApiBootstrap.OpenAsync(
            graphRoot.Path,
            publicKeyPath,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            RuntimeSourceSha256,
            immutableCustody: false,
            new FixedEntropySource(),
            CancellationToken.None);

        var ready = await InvokeAsync(state, HttpMethods.Get, "/health/ready");
        Assert.AreEqual(StatusCodes.Status204NoContent, ready.StatusCode);
        Assert.IsNull(ready.ContentType);
        Assert.AreEqual(string.Empty, ready.Body);
        Assert.AreEqual("no-store", ready.CacheControl);
        Assert.AreEqual("nosniff", ready.ContentTypeOptions);

        var success = await InvokeAsync(
            state,
            HttpMethods.Get,
            "/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview");
        Assert.AreEqual(StatusCodes.Status200OK, success.StatusCode);
        Assert.AreEqual("application/json;charset=utf-8", success.ContentType);
        Assert.AreEqual("no-store", success.CacheControl);
        Assert.AreEqual("nosniff", success.ContentTypeOptions);
        using var successJson = JsonDocument.Parse(success.Body);
        var successRoot = successJson.RootElement;
        Assert.AreEqual("success", successRoot.GetProperty("branch").GetString());
        Assert.IsTrue(successRoot.GetProperty("synthetic").GetBoolean());
        Assert.AreEqual(
            graph.ManifestSha256,
            successRoot.GetProperty("context").GetProperty("artifact").GetProperty("sha256").GetString());
        Assert.AreEqual(
            graph.Build.SqliteSha256,
            successRoot.GetProperty("context").GetProperty("index").GetProperty("sha256").GetString());
        Assert.AreEqual(
            "This text is synthetic and has no legal authority.",
            successRoot.GetProperty("result").GetProperty("objects")[0]
                .GetProperty("body").GetString()!.Split('\n')[2]);

        var refusal = await InvokeAsync(
            state,
            HttpMethods.Get,
            "/api/v3-preview/resolve?family=historical_legal_id&coordinate=historical_legal_id%3Asynthetic-preview");
        Assert.AreEqual(StatusCodes.Status200OK, refusal.StatusCode);
        using var refusalJson = JsonDocument.Parse(refusal.Body);
        var refusalRoot = refusalJson.RootElement;
        Assert.AreEqual("refusal", refusalRoot.GetProperty("branch").GetString());
        Assert.AreEqual("identifier_unknown", refusalRoot.GetProperty("status").GetString());
        Assert.IsFalse(
            refusalRoot.GetProperty("refusal").GetProperty("asserts_absence_of_law").GetBoolean());
        Assert.AreEqual(
            "eli/synthetic-preview",
            refusalRoot.GetProperty("refusal").GetProperty("possible_held_records")[0]
                .GetProperty("coordinate").GetString());
    }

    [TestMethod]
    public async Task HttpBoundaryMatchesTheClosedFailureMatrixWithoutIndexAccess()
    {
        var cases = new[]
        {
            new BoundaryCase(HttpMethods.Get, "/health/ready", 503, "urn:lex:v3:preview:unavailable", null),
            new BoundaryCase(HttpMethods.Post, "/health/ready", 405, "urn:lex:v3:preview:method-not-allowed", "GET"),
            new BoundaryCase("get", "/health/ready", 405, "urn:lex:v3:preview:method-not-allowed", "GET"),
            new BoundaryCase("GeT", "/api/v3-preview/resolve", 405, "urn:lex:v3:preview:method-not-allowed", "GET"),
            new BoundaryCase(HttpMethods.Get, "/health/ready?x=1", 400, "urn:lex:v3:preview:invalid-request", null),
            new BoundaryCase(HttpMethods.Get, "/api/v3-preview/resolve", 400, "urn:lex:v3:preview:invalid-request", null),
            new BoundaryCase(HttpMethods.Get, "/missing", 404, "urn:lex:v3:preview:not-found", null),
            new BoundaryCase(HttpMethods.Post, "/missing", 404, "urn:lex:v3:preview:not-found", null),
        };

        foreach (var item in cases)
        {
            var response = await InvokeAsync(
                SyntheticApiState.Unavailable,
                item.Method,
                item.RawTarget);
            Assert.AreEqual(item.Status, response.StatusCode, item.RawTarget);
            Assert.AreEqual("application/problem+json", response.ContentType, item.RawTarget);
            Assert.AreEqual(item.Allow, response.Allow, item.RawTarget);
            Assert.IsTrue(response.Body.Length <= 4 * 1024, item.RawTarget);
            using var json = JsonDocument.Parse(response.Body);
            Assert.AreEqual(item.Type, json.RootElement.GetProperty("type").GetString(), item.RawTarget);
        }
    }

    [TestMethod]
    public async Task ValidCandidateRowDeletionProducesAnEmptyHelpfulRefusal()
    {
        using var canonicalRoot = new BuildTestDirectory();
        using var candidateFreeRoot = new BuildTestDirectory();
        using var trustRoot = new BuildTestDirectory();
        Directory.CreateDirectory(trustRoot.Path);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var canonical = SyntheticPublicGraphBuilder.BuildAndSign(
            canonicalRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var candidateFree = SyntheticPublicGraphBuilder.BuildAndSign(
            candidateFreeRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256,
            includeCandidate: false);
        Assert.AreNotEqual(canonical.Build.LogicalRowsSha256, candidateFree.Build.LogicalRowsSha256);
        Assert.AreNotEqual(canonical.Build.SqliteSha256, candidateFree.Build.SqliteSha256);
        Assert.AreNotEqual(canonical.ControlSha256, candidateFree.ControlSha256);
        Assert.AreNotEqual(canonical.ManifestSha256, candidateFree.ManifestSha256);

        var publicKey = key.ExportSubjectPublicKeyInfo();
        var publicKeyPath = Path.Combine(trustRoot.Path, "public-key.spki");
        await File.WriteAllBytesAsync(publicKeyPath, publicKey);
        using var state = await SyntheticApiBootstrap.OpenAsync(
            candidateFreeRoot.Path,
            publicKeyPath,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            RuntimeSourceSha256,
            immutableCustody: false,
            new FixedEntropySource(),
            CancellationToken.None);

        var response = await InvokeAsync(
            state,
            HttpMethods.Get,
            "/api/v3-preview/resolve?family=historical_legal_id&coordinate=historical_legal_id%3Asynthetic-preview");

        Assert.AreEqual(StatusCodes.Status200OK, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.AreEqual(
            0,
            json.RootElement.GetProperty("refusal").GetProperty("possible_held_records").GetArrayLength());
        Assert.IsFalse(
            json.RootElement.GetProperty("refusal").GetProperty("asserts_absence_of_law").GetBoolean());
    }

    [TestMethod]
    public void ImmutableCustodyProbeRejectsWritableStorageWithoutChangingTheIndex()
    {
        using var root = new BuildTestDirectory();
        var build = SyntheticPreviewBuilder.BuildCanonical(root.Path);
        var before = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(build.SqlitePath)));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            SyntheticImmutableCustody.AssertReadOnly(root.Path, build.SqlitePath));

        StringAssert.Contains(exception.Message, "writable by the runtime user");
        var after = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(build.SqlitePath)));
        Assert.AreEqual(before, after);
        Assert.IsFalse(
            Directory.EnumerateFiles(root.Path, ".custody-probe-*", SearchOption.TopDirectoryOnly).Any());
    }

    [TestMethod]
    public async Task SqlTransportLineageMismatchFailsAtTheResolverBoundary()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var control = ContractJson.Deserialize<SyntheticSliceControl>(
            await File.ReadAllTextAsync(graph.ControlPath));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = graph.Build.SqlitePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE versions SET source_sha256=$sha WHERE version_id=1";
            command.Parameters.AddWithValue(
                "$sha",
                "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }

        var changedControl = BindChangedIndex(control, graph.Build.SqlitePath);
        using var resolver = SyntheticIndexResolver.Open(
            graph.Build.SqlitePath,
            changedControl,
            immutableCustody: false);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await resolver.ResolveAsync("eli", "eli/synthetic-preview", CancellationToken.None));
    }

    [TestMethod]
    public async Task SqlDerivedLineageMismatchFailsAtTheResolverBoundary()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var control = ContractJson.Deserialize<SyntheticSliceControl>(
            await File.ReadAllTextAsync(graph.ControlPath));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = graph.Build.SqlitePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE versions SET derived_sha256=$sha WHERE version_id=1";
            command.Parameters.AddWithValue(
                "$sha",
                "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }

        var changedControl = BindChangedIndex(control, graph.Build.SqlitePath);
        using var resolver = SyntheticIndexResolver.Open(
            graph.Build.SqlitePath,
            changedControl,
            immutableCustody: false);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await resolver.ResolveAsync("eli", "eli/synthetic-preview", CancellationToken.None));
    }

    [TestMethod]
    public async Task PresentCandidateWithMissingJoinedProvisionIsUnavailableNotUnknown()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var control = ContractJson.Deserialize<SyntheticSliceControl>(
            await File.ReadAllTextAsync(graph.ControlPath));
        using (var connection = OpenWritable(graph.Build.SqlitePath))
        {
            using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_keys=OFF";
                foreignKeys.ExecuteNonQuery();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM provisions WHERE provision_id=1";
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }

        var changedControl = BindChangedIndex(control, graph.Build.SqlitePath);
        using var resolver = SyntheticIndexResolver.Open(
            graph.Build.SqlitePath,
            changedControl,
            immutableCustody: false);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await resolver.ResolveAsync(
                "historical_legal_id",
                "historical_legal_id:synthetic-preview",
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ForeignKeyMismatchRejectsBeforeAnyResolve()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var control = ContractJson.Deserialize<SyntheticSliceControl>(
            await File.ReadAllTextAsync(graph.ControlPath));
        using (var connection = OpenWritable(graph.Build.SqlitePath))
        {
            using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_keys=OFF";
                foreignKeys.ExecuteNonQuery();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE identifiers SET work_id=999 WHERE identifier_id=2";
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }

        var changedControl = BindChangedIndex(control, graph.Build.SqlitePath);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            SyntheticIndexResolver.Open(
                graph.Build.SqlitePath,
                changedControl,
                immutableCustody: false));
        StringAssert.Contains(exception.Message, "foreign_key_check");
    }

    [TestMethod]
    public async Task BundledGraphIsAdmittedOnlyByTheCompiledTrustIdentity()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var graphRoot = Path.Combine(repositoryRoot, "src", "Lex.V3.Api", "preview-graph");
        var publicKeyPath = Path.Combine(
            repositoryRoot,
            "src",
            "Lex.V3.Api",
            "preview-trust",
            "public-key.spki");

        Assert.AreEqual(
            SyntheticPreviewTrustConfiguration.KeyId,
            $"s0-05-key-{SyntheticPreviewTrustConfiguration.PublicKeySha256[..16]}");

        using var state = await SyntheticApiBootstrap.OpenAsync(
            graphRoot,
            publicKeyPath,
            SyntheticPreviewTrustConfiguration.EnvironmentBinding,
            SyntheticPreviewTrustConfiguration.IssuerId,
            SyntheticPreviewTrustConfiguration.KeyId,
            SyntheticPreviewTrustConfiguration.PublicKeySha256,
            RuntimeSourceSha256,
            immutableCustody: false,
            new FixedEntropySource(),
            CancellationToken.None);

        Assert.IsTrue(state.Ready);
    }

    [TestMethod]
    public async Task MissingHeldIdentifierFailsTheReadinessPreflight()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var verification = await VerifyGraphAsync(root.Path, key);
        var control = verification.Control!;
        using (var connection = OpenWritable(graph.Build.SqlitePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM identifiers WHERE identifier_id=1";
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }

        var changedControl = BindChangedIndex(control, graph.Build.SqlitePath);
        using var resolver = SyntheticIndexResolver.Open(
            graph.Build.SqlitePath,
            changedControl,
            immutableCustody: false);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await SyntheticApiBootstrap.PreflightAsync(
                verification,
                resolver,
                new ComponentIdentity("s0-05-runtime", RuntimeSourceSha256),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task MissingTrustNoticeFailsTheReadinessPreflight()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var verification = await VerifyGraphAsync(root.Path, key);
        var changedBody = Encoding.UTF8.GetBytes(
            "LEX V3 SYNTHETIC PREVIEW\nArticle 1\nVisible but untrusted text.\n");
        var changedSha256 = Convert.ToHexStringLower(SHA256.HashData(changedBody));
        using (var connection = OpenWritable(graph.Build.SqlitePath))
        {
            using var transaction = connection.BeginTransaction();
            using (var version = connection.CreateCommand())
            {
                version.Transaction = transaction;
                version.CommandText =
                    "UPDATE versions SET source_sha256=$sha,derived_sha256=$sha WHERE version_id=1";
                version.Parameters.AddWithValue("$sha", changedSha256);
                Assert.AreEqual(1, version.ExecuteNonQuery());
            }

            using (var blob = connection.CreateCommand())
            {
                blob.Transaction = transaction;
                blob.CommandText =
                    "UPDATE blobs SET sha256=$sha,byte_count=$bytes,content=$body WHERE blob_id=1";
                blob.Parameters.AddWithValue("$sha", changedSha256);
                blob.Parameters.AddWithValue("$bytes", changedBody.LongLength);
                blob.Parameters.AddWithValue("$body", changedBody);
                Assert.AreEqual(1, blob.ExecuteNonQuery());
            }

            transaction.Commit();
        }

        var changedControl = BindChangedIndex(
            verification.Control!,
            graph.Build.SqlitePath,
            changedBody,
            changedBody);
        using var resolver = SyntheticIndexResolver.Open(
            graph.Build.SqlitePath,
            changedControl,
            immutableCustody: false);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await SyntheticApiBootstrap.PreflightAsync(
                verification,
                resolver,
                new ComponentIdentity("s0-05-runtime", RuntimeSourceSha256),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task FatalResolveFailurePermanentlyClosesReadiness()
    {
        using var graphRoot = new BuildTestDirectory();
        using var trustRoot = new BuildTestDirectory();
        Directory.CreateDirectory(trustRoot.Path);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            graphRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var publicKeyPath = Path.Combine(trustRoot.Path, "public-key.spki");
        await File.WriteAllBytesAsync(publicKeyPath, publicKey);
        using var state = await SyntheticApiBootstrap.OpenAsync(
            graphRoot.Path,
            publicKeyPath,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            RuntimeSourceSha256,
            immutableCustody: false,
            new FixedEntropySource(),
            CancellationToken.None);
        using (var connection = OpenWritable(graph.Build.SqlitePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE versions SET source_sha256=$sha WHERE version_id=1";
            command.Parameters.AddWithValue(
                "$sha",
                "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }

        var failedResolve = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.HeldRawTarget);
        var readinessAfterFailure = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.ReadyRawTarget);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, failedResolve.StatusCode);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, readinessAfterFailure.StatusCode);
        Assert.IsFalse(state.Ready);
    }

    [TestMethod]
    public async Task RequestCancellationDoesNotCloseReadiness()
    {
        using var graphRoot = new BuildTestDirectory();
        using var trustRoot = new BuildTestDirectory();
        Directory.CreateDirectory(trustRoot.Path);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _ = SyntheticPublicGraphBuilder.BuildAndSign(
            graphRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var publicKeyPath = Path.Combine(trustRoot.Path, "public-key.spki");
        await File.WriteAllBytesAsync(publicKeyPath, publicKey);
        using var state = await SyntheticApiBootstrap.OpenAsync(
            graphRoot.Path,
            publicKeyPath,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            RuntimeSourceSha256,
            immutableCustody: false,
            new FixedEntropySource(),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await state.ResolveAsync(
                new SyntheticResolveRequest(true, "eli", "eli/synthetic-preview"),
                cancellation.Token));
        var readiness = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.ReadyRawTarget);

        Assert.AreEqual(StatusCodes.Status204NoContent, readiness.StatusCode);
        Assert.IsTrue(state.Ready);
    }

    [TestMethod]
    public async Task TransportWriteFailureDoesNotCloseReadiness()
    {
        using var graphRoot = new BuildTestDirectory();
        using var trustRoot = new BuildTestDirectory();
        Directory.CreateDirectory(trustRoot.Path);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _ = SyntheticPublicGraphBuilder.BuildAndSign(
            graphRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var publicKeyPath = Path.Combine(trustRoot.Path, "public-key.spki");
        await File.WriteAllBytesAsync(publicKeyPath, publicKey);
        using var state = await SyntheticApiBootstrap.OpenAsync(
            graphRoot.Path,
            publicKeyPath,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            RuntimeSourceSha256,
            immutableCustody: false,
            new FixedEntropySource(),
            CancellationToken.None);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget =
            SyntheticResolveRequestContract.HeldRawTarget;
        await using var body = new CommitThenThrowStream();
        context.Response.Body = body;

        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await SyntheticApiHandler.HandleAsync(context, state, CancellationToken.None));
        var readiness = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.ReadyRawTarget);

        Assert.IsTrue(body.CommittedBytes > 0);
        Assert.AreEqual(StatusCodes.Status204NoContent, readiness.StatusCode);
        Assert.IsTrue(state.Ready);
    }

    [TestMethod]
    public async Task EntropyFailurePermanentlyClosesReadiness()
    {
        using var graphRoot = new BuildTestDirectory();
        using var trustRoot = new BuildTestDirectory();
        Directory.CreateDirectory(trustRoot.Path);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _ = SyntheticPublicGraphBuilder.BuildAndSign(
            graphRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var publicKeyPath = Path.Combine(trustRoot.Path, "public-key.spki");
        await File.WriteAllBytesAsync(publicKeyPath, publicKey);
        using var state = await SyntheticApiBootstrap.OpenAsync(
            graphRoot.Path,
            publicKeyPath,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            RuntimeSourceSha256,
            immutableCustody: false,
            new ThrowingEntropySource(),
            CancellationToken.None);

        var failedResolve = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.HeldRawTarget);
        var readinessAfterFailure = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.ReadyRawTarget);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, failedResolve.StatusCode);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, readinessAfterFailure.StatusCode);
        Assert.IsFalse(state.Ready);
    }

    [TestMethod]
    public async Task MapperFailurePermanentlyClosesReadiness()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var verification = await VerifyGraphAsync(root.Path, key);
        var resolver = SyntheticIndexResolver.Open(
            graph.Build.SqlitePath,
            verification.Control!,
            immutableCustody: false);
        using var state = SyntheticApiState.Available(
            verification,
            resolver,
            new ComponentIdentity(
                verification.Control!.Builder.ComponentId,
                RuntimeSourceSha256),
            new FixedEntropySource());

        var failedResolve = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.HeldRawTarget);
        var readinessAfterFailure = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.ReadyRawTarget);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, failedResolve.StatusCode);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, readinessAfterFailure.StatusCode);
        Assert.IsFalse(state.Ready);
    }

    [TestMethod]
    public async Task PreCommitResponsePreparationFailurePermanentlyClosesReadiness()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var verification = await VerifyGraphAsync(root.Path, key);
        var resolver = SyntheticIndexResolver.Open(
            graph.Build.SqlitePath,
            verification.Control!,
            immutableCustody: false);
        using var state = SyntheticApiState.Available(
            verification,
            resolver,
            new ComponentIdentity("s0-05-runtime", RuntimeSourceSha256),
            new FixedEntropySource(),
            static _ => throw new InvalidOperationException("Synthetic response preparation failure."));

        var failedResolve = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.HeldRawTarget);
        var readinessAfterFailure = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.ReadyRawTarget);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, failedResolve.StatusCode);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, readinessAfterFailure.StatusCode);
        Assert.IsFalse(state.Ready);
    }

    [TestMethod]
    public async Task AdmittedSqlProjectionReconcilesEveryEnvelopeLineageField()
    {
        using var graphRoot = new BuildTestDirectory();
        using var trustRoot = new BuildTestDirectory();
        Directory.CreateDirectory(trustRoot.Path);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            graphRoot.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var verification = await VerifyGraphAsync(graphRoot.Path, key);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var publicKeyPath = Path.Combine(trustRoot.Path, "public-key.spki");
        await File.WriteAllBytesAsync(publicKeyPath, publicKey);
        using var state = await SyntheticApiBootstrap.OpenAsync(
            graphRoot.Path,
            publicKeyPath,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            RuntimeSourceSha256,
            immutableCustody: false,
            new FixedEntropySource(),
            CancellationToken.None);

        var response = await InvokeAsync(
            state,
            HttpMethods.Get,
            SyntheticResolveRequestContract.HeldRawTarget);
        var projection = ReadIndependentProjection(graph.Build.SqlitePath);
        using var json = JsonDocument.Parse(response.Body);
        var coordinate = json.RootElement.GetProperty("result").GetProperty("objects")[0];
        var sourceDescriptor = verification.Control!.Blobs.Single(
            static blob => blob.Kind == SyntheticSliceBlobKind.SourceTransport);
        var derivedDescriptor = verification.Control.Blobs.Single(
            static blob => blob.Kind == SyntheticSliceBlobKind.DerivedText);

        Assert.AreEqual(projection.IdentifierWorkId, projection.WorkId);
        Assert.AreEqual(projection.WorkId, projection.VersionWorkId);
        Assert.AreEqual(projection.VersionId, projection.ProvisionVersionId);
        Assert.AreEqual(projection.ProvisionBlobId, projection.BlobId);
        Assert.AreEqual(sourceDescriptor.Sha256, projection.SourceSha256);
        Assert.AreEqual(derivedDescriptor.Sha256, projection.DerivedSha256);
        Assert.AreEqual(projection.DerivedSha256, projection.BodySha256);
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(projection.Body)),
            projection.BodySha256);
        Assert.AreEqual(
            $"preview:{projection.CanonicalIdentifier}#{projection.Anchor}",
            coordinate.GetProperty("object_id").GetString());
        Assert.AreEqual(
            $"preview:{projection.CanonicalIdentifier}",
            coordinate.GetProperty("work_id").GetString());
        Assert.AreEqual(
            $"preview:{projection.VersionKey}",
            coordinate.GetProperty("version_key").GetString());
        Assert.AreEqual(
            $"preview:{projection.Anchor}",
            coordinate.GetProperty("anchor").GetString());
        Assert.AreEqual(projection.BodySha256, coordinate.GetProperty("body_sha256").GetString());
        Assert.AreEqual(Encoding.UTF8.GetString(projection.Body), coordinate.GetProperty("body").GetString());
    }

    [TestMethod]
    public async Task VariedProjectedRowDrivesEveryEnvelopeCoordinateAndDigest()
    {
        using var root = new BuildTestDirectory();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var graph = SyntheticPublicGraphBuilder.BuildAndSign(
            root.Path,
            key,
            EnvironmentBinding,
            IssuerId,
            KeyId,
            BuilderSourceSha256);
        var admitted = await VerifyGraphAsync(root.Path, key);
        var sourceBytes = Encoding.UTF8.GetBytes("Synthetic transport variation.\r\n");
        var body = Encoding.UTF8.GetBytes(
            "LEX V3 SYNTHETIC PREVIEW\nArticle 9\n" +
            SyntheticResolveSuccessEnvelope.TrustNotice + "\nVaried SQL row.\n");
        var variedControl = BindChangedIndex(
            admitted.Control!,
            graph.Build.SqlitePath,
            sourceBytes,
            body);
        var sourceDescriptor = variedControl.Blobs.Single(
            static blob => blob.Kind == SyntheticSliceBlobKind.SourceTransport);
        var derivedDescriptor = variedControl.Blobs.Single(
            static blob => blob.Kind == SyntheticSliceBlobKind.DerivedText);
        var variedVerification = SyntheticSliceVerification.Accepted(
            admitted.Manifest!,
            variedControl,
            admitted.ManifestSha256!,
            admitted.ControlSha256!,
            sourceBytes,
            body,
            admitted.SqliteBytes.ToArray());
        var row = new SyntheticResolvedRow(
            SyntheticResolutionDisposition.Held,
            EvidenceBasis: null,
            "lu-legilux",
            "eli/varied-row-9",
            "Varied projected row",
            "varied-version-9",
            sourceDescriptor.Sha256,
            derivedDescriptor.Sha256,
            "article-9",
            Ordinal: 9,
            derivedDescriptor.Sha256,
            "text/plain;charset=utf-8",
            body);

        var envelope = SyntheticResponseMapper.Map(
            variedVerification,
            row,
            "eli",
            "eli/synthetic-preview",
            "req_99999999999999999999999999999999",
            new ComponentIdentity("s0-05-runtime", RuntimeSourceSha256));
        var success = envelope as SyntheticResolveSuccessEnvelope;
        Assert.IsNotNull(success);
        var coordinate = success.Result.Objects.Single() as PreviewSyntheticCoordinate;
        Assert.IsNotNull(coordinate);

        Assert.AreEqual(sourceDescriptor.Sha256, row.SourceSha256);
        Assert.AreEqual(sourceBytes.LongLength, sourceDescriptor.Bytes);
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(sourceBytes)),
            sourceDescriptor.Sha256);
        Assert.AreEqual(derivedDescriptor.Sha256, row.DerivedSha256);
        Assert.AreEqual(body.LongLength, derivedDescriptor.Bytes);
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(body)),
            derivedDescriptor.Sha256);
        Assert.AreEqual(row.DerivedSha256, row.BlobSha256);
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(row.Body)),
            row.BlobSha256);
        Assert.AreEqual($"preview:{row.CanonicalIdentifier}#{row.Anchor}", coordinate.ObjectId);
        Assert.AreEqual($"preview:{row.CanonicalIdentifier}", coordinate.WorkId);
        Assert.AreEqual($"preview:{row.VersionKey}", coordinate.VersionKey);
        Assert.AreEqual($"preview:{row.Anchor}", coordinate.Anchor);
        Assert.AreEqual(Encoding.UTF8.GetString(row.Body), coordinate.Body);
        Assert.AreEqual(row.BlobSha256, coordinate.BodySha256);
    }

    private static async Task<CapturedResponse> InvokeAsync(
        SyntheticApiState state,
        string method,
        string rawTarget)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget = rawTarget;
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await SyntheticApiHandler.HandleAsync(context, state, CancellationToken.None);

        return new CapturedResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            context.Response.Headers.CacheControl.ToString(),
            context.Response.Headers.XContentTypeOptions.ToString(),
            context.Response.Headers.Allow.ToString() is { Length: > 0 } allow ? allow : null,
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static SqliteConnection OpenWritable(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static IndependentProjection ReadIndependentProjection(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT i.work_id,w.work_id,v.work_id,v.version_id,p.version_id,p.blob_id,b.blob_id,
                   w.canonical_identifier,v.version_key,p.anchor,v.source_sha256,v.derived_sha256,
                   b.sha256,b.content
            FROM identifiers AS i
            JOIN works AS w ON w.work_id=i.work_id
            JOIN versions AS v ON v.work_id=w.work_id
            JOIN provisions AS p ON p.version_id=v.version_id
            JOIN blobs AS b ON b.blob_id=p.blob_id
            WHERE i.family='eli' AND i.coordinate='eli/synthetic-preview'
            """;
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read());
        var projection = new IndependentProjection(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetFieldValue<byte[]>(13));
        Assert.IsFalse(reader.Read());
        return projection;
    }

    private static SyntheticSliceControl BindChangedIndex(
        SyntheticSliceControl control,
        string sqlitePath,
        byte[]? sourceBytes = null,
        byte[]? derivedBytes = null)
    {
        var changedIndex = File.ReadAllBytes(sqlitePath);
        var blobs = control.Blobs
            .Select(blob => blob.Kind switch
            {
                SyntheticSliceBlobKind.SourceTransport when sourceBytes is not null =>
                    NewDescriptor(blob, sourceBytes),
                SyntheticSliceBlobKind.DerivedText when derivedBytes is not null =>
                    NewDescriptor(blob, derivedBytes),
                SyntheticSliceBlobKind.SqliteIndex => NewDescriptor(blob, changedIndex),
                _ => blob,
            })
            .ToArray();
        return new SyntheticSliceControl(
            control.Schema,
            control.SchemaResource,
            control.ResolveRequestContract,
            control.OperationCatalog,
            control.RefusalRegistry,
            control.ObjectSetSchema,
            control.NormalizationProfile,
            control.Scope,
            control.Snapshot,
            control.Builder,
            control.IndexStamp,
            blobs);
    }

    private static SyntheticSliceBlobDescriptor NewDescriptor(
        SyntheticSliceBlobDescriptor descriptor,
        byte[] bytes) => new(
        descriptor.Kind,
        Convert.ToHexStringLower(SHA256.HashData(bytes)),
        bytes.LongLength,
        descriptor.MediaType);

    private static async Task<SyntheticSliceVerification> VerifyGraphAsync(
        string graphRoot,
        ECDsa key)
    {
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var verifier = new SyntheticSliceArtifactVerifier(
            EnvironmentBinding,
            IssuerId,
            KeyId,
            Convert.ToHexStringLower(SHA256.HashData(publicKey)),
            SyntheticSliceSchemaExporter.ExportSchemaTable(),
            new TestTrustStore(publicKey));
        var verification = await verifier.VerifyAsync(
            new ContentAddressedSyntheticCandidate(graphRoot),
            CancellationToken.None);
        Assert.IsTrue(verification.Verified, verification.Failure?.ToString());
        return verification;
    }

    private sealed class FixedEntropySource : IRequestEntropySource
    {
        public void Fill(Span<byte> destination) => destination.Fill(0x5a);
    }

    private sealed class ThrowingEntropySource : IRequestEntropySource
    {
        public void Fill(Span<byte> destination) =>
            throw new CryptographicException("Synthetic entropy failure.");
    }

    private sealed class CommitThenThrowStream : MemoryStream
    {
        public int CommittedBytes { get; private set; }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length > 0)
            {
                base.WriteByte(buffer.Span[0]);
                CommittedBytes++;
            }

            throw new IOException("Synthetic post-commit transport interruption.");
        }
    }

    private sealed class TestTrustStore(byte[] publicKey) : IPreviewTrustStore
    {
        public bool ContainsIssuer(string issuerId) =>
            string.Equals(issuerId, IssuerId, StringComparison.Ordinal);

        public bool TryGetSubjectPublicKeyInfo(
            string issuerId,
            string keyId,
            out ReadOnlyMemory<byte> subjectPublicKeyInfo)
        {
            if (ContainsIssuer(issuerId) && string.Equals(keyId, KeyId, StringComparison.Ordinal))
            {
                subjectPublicKeyInfo = publicKey;
                return true;
            }

            subjectPublicKeyInfo = default;
            return false;
        }
    }

    private sealed record BoundaryCase(
        string Method,
        string RawTarget,
        int Status,
        string Type,
        string? Allow);

    private sealed record CapturedResponse(
        int StatusCode,
        string? ContentType,
        string CacheControl,
        string ContentTypeOptions,
        string? Allow,
        string Body);

    private sealed record IndependentProjection(
        long IdentifierWorkId,
        long WorkId,
        long VersionWorkId,
        long VersionId,
        long ProvisionVersionId,
        long ProvisionBlobId,
        long BlobId,
        string CanonicalIdentifier,
        string VersionKey,
        string Anchor,
        string SourceSha256,
        string DerivedSha256,
        string BodySha256,
        byte[] Body);
}

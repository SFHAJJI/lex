using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Lex.V3.Contracts.Custody;
using Lex.V3.Custody.Probe;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class ProbeFailureDiagnosticTests
{
    private const string Secret = "sentinel-token-host-body-and-private-message";

    [TestMethod]
    public void KnownFailuresRemainDistinctWithoutPublishingTheirMessages()
    {
        var cases = new (Exception Error, string Kind)[]
        {
            (new CustodyPolicyException(Secret), "custody_policy"),
            (new CustodyIntegrityException(Secret), "custody_integrity"),
            (new CustodyRequiredException(Secret), "custody_required"),
            (new CredentialUnavailableException(Secret), "identity_unavailable"),
            (new AuthenticationFailedException(Secret), "authentication_failed"),
            (new RequestFailedException(Secret), "azure_request"),
            (new HttpRequestException(Secret), "http_request"),
            (new OperationCanceledException(Secret), "operation_canceled"),
            (new TimeoutException(Secret), "timeout"),
            (new JsonException(Secret), "invalid_json"),
            (new ArgumentException(Secret), "invalid_argument"),
            (new InvalidOperationException(Secret), "invalid_operation"),
            (new Exception(Secret), "unknown"),
        };

        foreach (var (error, expectedKind) in cases)
        {
            error.Data[Secret] = Secret;
            using var diagnostic = ParseWithoutSecrets(error);
            var causes = diagnostic.RootElement.GetProperty("causes");
            Assert.AreEqual(1, causes.GetArrayLength());
            Assert.AreEqual(expectedKind, causes[0].GetProperty("kind").GetString());
            Assert.AreEqual(JsonValueKind.Null, causes[0].GetProperty("http_status").ValueKind);
            Assert.IsFalse(diagnostic.RootElement.GetProperty("truncated").GetBoolean());
        }
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    [DataRow(412)]
    [DataRow(429)]
    [DataRow(500)]
    [DataRow(503)]
    public void WrappedArmAndBlobStatusesRemainAttributable(int status)
    {
        foreach (var cause in new Exception[]
                 {
                     new HttpRequestException(Secret, null, (HttpStatusCode)status),
                     new RequestFailedException(status, Secret, Secret, null),
                 })
        {
            using var diagnostic = ParseWithoutSecrets(new CustodyRequiredException(Secret, cause));
            var causes = diagnostic.RootElement.GetProperty("causes");
            Assert.AreEqual(2, causes.GetArrayLength());
            Assert.AreEqual("custody_required", causes[0].GetProperty("kind").GetString());
            Assert.AreEqual(cause is HttpRequestException ? "http_request" : "azure_request",
                causes[1].GetProperty("kind").GetString());
            Assert.AreEqual(status, causes[1].GetProperty("http_status").GetInt32());
        }
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(99)]
    [DataRow(600)]
    [DataRow(int.MaxValue)]
    public void NonHttpStatusValuesAreNotPublished(int status)
    {
        foreach (var error in new Exception[]
                 {
                     new RequestFailedException(status, Secret),
                     new HttpRequestException(Secret, null, (HttpStatusCode)status),
                 })
        {
            using var diagnostic = ParseWithoutSecrets(error);
            Assert.AreEqual(JsonValueKind.Null,
                diagnostic.RootElement.GetProperty("causes")[0].GetProperty("http_status").ValueKind);
        }
    }

    [TestMethod]
    [DataRow(8, false)]
    [DataRow(9, true)]
    public void LongCauseChainsAreExplicitlyTruncated(int depth, bool truncated)
    {
        Exception error = new HttpRequestException(Secret, null, HttpStatusCode.Forbidden);
        for (var i = 1; i < depth; i++)
        {
            error = new Exception(Secret, error);
        }

        using var diagnostic = ParseWithoutSecrets(error);
        var causes = diagnostic.RootElement.GetProperty("causes");
        Assert.AreEqual(8, causes.GetArrayLength());
        Assert.AreEqual(truncated, diagnostic.RootElement.GetProperty("truncated").GetBoolean());
        Assert.AreEqual(truncated ? "unknown" : "http_request",
            causes[7].GetProperty("kind").GetString());
    }

    [TestMethod]
    public void UnknownExceptionMembersAreNotReadOrSerialized()
    {
        using var diagnostic = ParseWithoutSecrets(new SecretNamedException());
        Assert.AreEqual("unknown",
            diagnostic.RootElement.GetProperty("causes")[0].GetProperty("kind").GetString());
    }

    private static JsonDocument ParseWithoutSecrets(Exception error)
    {
        var json = ProbeFailureDiagnostic.Serialize(error);
        Assert.IsFalse(json.Contains(Secret, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(nameof(SecretNamedException), StringComparison.Ordinal));
        Assert.IsTrue(Encoding.UTF8.GetByteCount(json) <= 1024);
        var document = JsonDocument.Parse(json);
        CollectionAssert.AreEquivalent(new[] { "schema", "event", "causes", "truncated" },
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("lex-v3-custody-probe-diagnostic/1",
            document.RootElement.GetProperty("schema").GetString());
        Assert.AreEqual("custody_probe_failed", document.RootElement.GetProperty("event").GetString());
        foreach (var cause in document.RootElement.GetProperty("causes").EnumerateArray())
        {
            CollectionAssert.AreEquivalent(new[] { "kind", "http_status" },
                cause.EnumerateObject().Select(property => property.Name).ToArray());
        }

        return document;
    }

    [TestMethod]
    public void VersionTwoDoesNotAttributeExternalOrLookalikeExceptions()
    {
        var own = ProbeFailureDiagnostic.ConfigurationFailure(
            ProbeConfigurationGuard.MissingSetting, Secret, "IDENTITY_HEADER");
        foreach (var error in new Exception[]
                 { new InvalidOperationException(Secret), new SecretNamedException(), new SecretInvalidOperationException(),
                     new RequestFailedException(403, Secret) })
        {
            if (error is not SecretNamedException and not SecretInvalidOperationException)
            {
                error.Data["configuration_guard"] = own;
            }
            var json = ProbeFailureDiagnostic.Serialize(error, includeConfiguration: true);
            Assert.IsFalse(json.Contains(Secret, StringComparison.Ordinal));
            using var document = JsonDocument.Parse(json);
            Assert.AreEqual(JsonValueKind.Null,
                document.RootElement.GetProperty("causes")[0].GetProperty("configuration_guard").ValueKind);
        }
    }

    [TestMethod]
    public void VersionTwoAttributionIsAllowlistedBoundedAndPreservesCausePosition()
    {
        var unknown = ProbeFailureDiagnostic.ConfigurationFailure(
            (ProbeConfigurationGuard)int.MaxValue, Secret, Secret);
        using var unknownDocument = JsonDocument.Parse(
            ProbeFailureDiagnostic.Serialize(unknown, includeConfiguration: true));
        var unknownGuard = unknownDocument.RootElement.GetProperty("causes")[0].GetProperty("configuration_guard");
        Assert.AreEqual("unknown", unknownGuard.GetProperty("kind").GetString());
        Assert.AreEqual(JsonValueKind.Null, unknownGuard.GetProperty("setting").ValueKind);

        Exception error = ProbeFailureDiagnostic.ConfigurationFailure(
            ProbeConfigurationGuard.MissingSetting, Secret, "IDENTITY_HEADER");
        for (var depth = 1; depth <= 9; depth++)
        {
            var json = ProbeFailureDiagnostic.Serialize(error, includeConfiguration: true);
            Assert.IsFalse(json.Contains(Secret, StringComparison.Ordinal));
            Assert.IsTrue(Encoding.UTF8.GetByteCount(json) <= 4096);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            CollectionAssert.AreEquivalent(new[] { "schema", "event", "causes", "truncated" },
                root.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.AreEqual("lex-v3-custody-probe-diagnostic/2", root.GetProperty("schema").GetString());
            Assert.AreEqual("custody_probe_failed", root.GetProperty("event").GetString());
            Assert.AreEqual(depth > 8, root.GetProperty("truncated").GetBoolean());
            var causes = root.GetProperty("causes");
            Assert.AreEqual(Math.Min(depth, 8), causes.GetArrayLength());
            for (var index = 0; index < causes.GetArrayLength(); index++)
            {
                var cause = causes[index];
                CollectionAssert.AreEquivalent(new[] { "kind", "http_status", "configuration_guard" },
                    cause.EnumerateObject().Select(property => property.Name).ToArray());
                if (index == depth - 1)
                {
                    Assert.AreEqual("IDENTITY_HEADER",
                        cause.GetProperty("configuration_guard").GetProperty("setting").GetString());
                }
                else
                {
                    Assert.AreEqual(JsonValueKind.Null, cause.GetProperty("configuration_guard").ValueKind);
                    Assert.AreEqual(403, cause.GetProperty("http_status").GetInt32());
                }
            }
            error = new HttpRequestException(Secret, error, HttpStatusCode.Forbidden);
        }
    }

    private sealed class SecretNamedException : Exception
    {
        public override System.Collections.IDictionary Data =>
            throw new InvalidOperationException("Do not read arbitrary Data.");
        public override string Message => throw new InvalidOperationException("Do not read Message.");
        public override string ToString() => throw new InvalidOperationException("Do not read ToString.");
    }

    private sealed class SecretInvalidOperationException : InvalidOperationException
    {
        public override System.Collections.IDictionary Data =>
            throw new InvalidOperationException("Do not read subtype Data.");
    }
}

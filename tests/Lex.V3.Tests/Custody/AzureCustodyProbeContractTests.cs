using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Custody.Azure;
using Lex.V3.Custody.Probe;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class AzureCustodyProbeContractTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 9, 1, 7, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task WriteEmitsOnlyAProviderNeutralReceiptForSyntheticBytes()
    {
        var store = new ProbeStore();
        var output = new StringWriter();

        await CustodyProbeApplication.RunAsync(
            ["write", "nightly_floor_90d"],
            TextReader.Null,
            output,
            ValidEnvironment(),
            _ => store,
            CancellationToken.None);

        var receipt = ContractJson.Deserialize<DurableBlobWriteReceipt>(output.ToString());
        Assert.AreEqual(CustodyClass.NightlyFloor90d, receipt.Reference.CustodyClass);
        Assert.IsTrue(receipt.Reference.ByteLength > 0);
        Assert.AreEqual(1, store.CreateCalls);
        Assert.IsFalse(output.ToString().Contains("blob.core.windows.net", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains("staging", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains("resource-group", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains(TestClientId.ToString("D"), StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadAcceptsOnlyAReceiptRestoresCheckedBytesAndWritesNothing()
    {
        var body = "retained synthetic probe"u8.ToArray();
        var receipt = ReceiptFor(body, CustodyClass.LegalHoldEvidence);
        var store = new ProbeStore(body);
        var output = new StringWriter();

        await CustodyProbeApplication.RunAsync(
            ["read"],
            new StringReader(ContractJson.Serialize(receipt)),
            output,
            ValidEnvironment(),
            _ => store,
            CancellationToken.None);

        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(1, store.ReadCalls);
        Assert.AreEqual(0, store.CreateCalls);
    }

    [TestMethod]
    public async Task SecretCredentialEnvironmentIsRefusedBeforeAStoreExists()
    {
        foreach (var name in new[] { "AZURE_CLIENT_SECRET", "azure_storage_connection_string" })
        {
            var environment = ValidEnvironment();
            environment[name] = "must-not-be-used";
            var storeCreated = false;

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                CustodyProbeApplication.RunAsync(
                    ["write", "legal_hold_evidence"],
                    TextReader.Null,
                    TextWriter.Null,
                    environment,
                    _ =>
                    {
                        storeCreated = true;
                        return new ProbeStore();
                    },
                    CancellationToken.None));

            Assert.IsFalse(storeCreated, name);
        }
    }

    [TestMethod]
    public async Task ReadRefusesWrongOrUnenforcedPolicyBeforeAStoreExists()
    {
        var body = "retained synthetic probe"u8.ToArray();
        var reference = ReferenceFor(body, CustodyClass.LegalHoldEvidence);
        var wrongPolicy = ReceiptFor(
            body,
            CustodyClass.LegalHoldEvidence,
            Guid.Parse("fc5fe10c-9255-49af-a996-d04b080b560c"));
        var unenforced = new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                policyKey: null,
                CustodyProtection.NotEnforced,
                ObservedAt,
                protectedUntil: null));

        foreach (var receipt in new[] { wrongPolicy, unenforced })
        {
            var storeCreated = false;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                CustodyProbeApplication.RunAsync(
                    ["read"],
                    new StringReader(ContractJson.Serialize(receipt)),
                    TextWriter.Null,
                    ValidEnvironment(),
                    _ =>
                    {
                        storeCreated = true;
                        return new ProbeStore(body);
                    },
                    CancellationToken.None));
            Assert.IsFalse(storeCreated);
        }
    }

    [TestMethod]
    public async Task WriteRefusesAReceiptForDifferentBytes()
    {
        var output = new StringWriter();

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            CustodyProbeApplication.RunAsync(
                ["write", "nightly_floor_90d"],
                TextReader.Null,
                output,
                ValidEnvironment(),
                _ => new MismatchingWriteStore(),
                CancellationToken.None));

        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public async Task ModeAndLaneVocabulariesAreClosed()
    {
        foreach (var arguments in new[]
                 {
                     Array.Empty<string>(),
                     new[] { "WRITE", "nightly_floor_90d" },
                     new[] { "write" },
                     new[] { "write", "NightlyFloor90d" },
                     new[] { "read", "extra" },
                     new[] { "verify" },
                 })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                CustodyProbeApplication.RunAsync(
                    arguments,
                    TextReader.Null,
                    TextWriter.Null,
                    ValidEnvironment(),
                    _ => new ProbeStore(),
                    CancellationToken.None));
        }
    }

    [TestMethod]
    public async Task ConsoleBoundaryReturnsOnlyTheFixedFailureSignal()
    {
        var assembly = Path.Combine(AppContext.BaseDirectory, "Lex.V3.Custody.Probe.dll");
        Assert.IsTrue(File.Exists(assembly), assembly);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet))
        {
            dotnet = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        }

        var startInfo = new ProcessStartInfo(dotnet)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(assembly);
        startInfo.ArgumentList.Add("invalid-mode");

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        Assert.AreEqual(string.Empty, await stdoutTask);
        Assert.AreEqual($"custody_probe_failed{Environment.NewLine}", await stderrTask);
        Assert.AreEqual(1, process.ExitCode);
    }

    [TestMethod]
    public async Task MalformedOrOversizedReceiptFailsBeforeAzureIsReached()
    {
        var malformedStoreCreated = false;
        await Assert.ThrowsExactlyAsync<JsonException>(() => CustodyProbeApplication.RunAsync(
            ["read"],
            new StringReader("{}"),
            TextWriter.Null,
            ValidEnvironment(),
            _ =>
            {
                malformedStoreCreated = true;
                return new ProbeStore();
            },
            CancellationToken.None));
        Assert.IsFalse(malformedStoreCreated);

        var oversizedStoreCreated = false;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => CustodyProbeApplication.RunAsync(
            ["read"],
            new StringReader(new string('x', 32_769)),
            TextWriter.Null,
            ValidEnvironment(),
            _ =>
            {
                oversizedStoreCreated = true;
                return new ProbeStore();
            },
            CancellationToken.None));
        Assert.IsFalse(oversizedStoreCreated);
    }

    private static readonly Guid TestClientId =
        Guid.Parse("66ba38aa-74fa-42c4-ab19-e4e41b9ae01b");

    private static readonly Guid NightlyPolicyKey =
        Guid.Parse("ff52fe20-4b11-4ca2-9542-22249d5c4c06");

    private static readonly Guid LegalHoldPolicyKey =
        Guid.Parse("b3eb07d3-9159-4673-a4b2-0f4b3ca86293");

    private static Dictionary<string, string?> ValidEnvironment() => new(StringComparer.Ordinal)
    {
        ["LEX_V3_CUSTODY_SERVICE_URI"] = "https://stlexv3custody.blob.core.windows.net/",
        ["LEX_V3_CUSTODY_STAGING_CONTAINER"] = "staging",
        ["LEX_V3_CUSTODY_NIGHTLY_CONTAINER"] = "nightly",
        ["LEX_V3_CUSTODY_LEGAL_HOLD_CONTAINER"] = "legal-hold",
        ["LEX_V3_CUSTODY_MANAGED_IDENTITY_CLIENT_ID"] = TestClientId.ToString("D"),
        ["LEX_V3_CUSTODY_NIGHTLY_POLICY_KEY"] = NightlyPolicyKey.ToString("D"),
        ["LEX_V3_CUSTODY_LEGAL_HOLD_POLICY_KEY"] = LegalHoldPolicyKey.ToString("D"),
        ["LEX_V3_CUSTODY_SUBSCRIPTION_ID"] = "37000e0e-4444-4f9a-95f9-3a786b4ddd30",
        ["LEX_V3_CUSTODY_RESOURCE_GROUP"] = "resource-group",
    };

    private static DurableBlobWriteReceipt ReceiptFor(
        byte[] body,
        CustodyClass custodyClass,
        Guid? policyKey = null)
    {
        var reference = ReferenceFor(body, custodyClass);
        var evidence = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.ImmutableObject1,
            policyKey ?? (custodyClass == CustodyClass.NightlyFloor90d
                ? NightlyPolicyKey
                : LegalHoldPolicyKey),
            custodyClass == CustodyClass.NightlyFloor90d
                ? CustodyProtection.LockedTime
                : CustodyProtection.ActiveLegalHold,
            ObservedAt,
            custodyClass == CustodyClass.NightlyFloor90d ? ObservedAt.AddDays(90) : null);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            evidence);
    }

    private static DurableBlobRef ReferenceFor(byte[] body, CustodyClass custodyClass) =>
        new(
            CustodySchemaIds.DurableBlobRef,
            CustodyDigest.Of(body),
            body.LongLength,
            custodyClass);

    private sealed class ProbeStore(ReadOnlyMemory<byte>? restored = null) : ICustodyStore
    {
        public int CreateCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(ReceiptFor(bytes.ToArray(), custodyClass));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            return Task.FromResult(restored ?? ReadOnlyMemory<byte>.Empty);
        }
    }

    private sealed class MismatchingWriteStore : ICustodyStore
    {
        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReceiptFor([0], custodyClass));

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

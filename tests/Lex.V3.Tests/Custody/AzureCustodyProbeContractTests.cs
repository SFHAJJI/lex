using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        var body = new byte[32];
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
    public async Task WriteUsesFreshSyntheticBytesForEveryProbe()
    {
        var firstStore = new ProbeStore();
        var secondStore = new ProbeStore();

        await CustodyProbeApplication.RunAsync(
            ["write", "nightly_floor_90d"], TextReader.Null, TextWriter.Null,
            ValidEnvironment(), _ => firstStore, CancellationToken.None);
        await CustodyProbeApplication.RunAsync(
            ["write", "nightly_floor_90d"], TextReader.Null, TextWriter.Null,
            ValidEnvironment(), _ => secondStore, CancellationToken.None);

        Assert.HasCount(32, firstStore.CreatedBytes);
        Assert.HasCount(32, secondStore.CreatedBytes);
        CollectionAssert.AreNotEqual(firstStore.CreatedBytes, secondStore.CreatedBytes);
    }

    [TestMethod]
    public async Task ReceiptArgumentRestoresCheckedBytesThroughASeparateInvocation()
    {
        foreach (var lane in Enum.GetValues<CustodyClass>())
        {
            var body = RandomNumberGenerator.GetBytes(32);
            var receipt = ReceiptFor(body, lane);
            var store = new ProbeStore(body);
            var output = new StringWriter();

            await CustodyProbeApplication.RunAsync(
                ["read-receipt", EncodeReceipt(receipt)],
                TextReader.Null,
                output,
                ValidEnvironment(),
                _ => store,
                CancellationToken.None);

            Assert.AreEqual(string.Empty, output.ToString());
            Assert.AreEqual(1, store.ReadCalls);
            Assert.AreEqual(0, store.CreateCalls);
            Assert.AreEqual(lane, store.LastReadReference!.CustodyClass);
        }
    }

    [TestMethod]
    public async Task ReceiptArgumentRefusesNoncanonicalOrOversizedEncodingBeforeStoreCreation()
    {
        var receipt = ReceiptFor(new byte[32], CustodyClass.NightlyFloor90d);
        var canonical = EncodeReceipt(receipt);
        var noncanonical = EncodeReceiptWithNoncanonicalBase64Url(receipt);
        var invalidUtf8 = EncodeBytes([byte.MaxValue]);
        foreach (var value in new[]
                 {
                     canonical + "=",
                     " " + canonical,
                     "a",
                     noncanonical,
                     invalidUtf8,
                     new string('a', 16_385),
                 })
        {
            var storeCreated = false;
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => CustodyProbeApplication.RunAsync(
                ["read-receipt", value],
                TextReader.Null,
                TextWriter.Null,
                ValidEnvironment(),
                _ =>
                {
                    storeCreated = true;
                    return new ProbeStore();
                },
                CancellationToken.None));
            Assert.IsFalse(storeCreated);
        }

        Assert.IsFalse(canonical.Contains('='));
        Assert.AreNotEqual(canonical, noncanonical);
    }

    [TestMethod]
    public async Task ReceiptArgumentRefusesCorruptedRestoredBytes()
    {
        var expected = RandomNumberGenerator.GetBytes(32);
        var corrupted = expected.ToArray();
        corrupted[0] ^= byte.MaxValue;
        var receipt = ReceiptFor(expected, CustodyClass.NightlyFloor90d);

        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            CustodyProbeApplication.RunAsync(
                ["read-receipt", EncodeReceipt(receipt)],
                TextReader.Null,
                TextWriter.Null,
                ValidEnvironment(),
                _ => new ProbeStore(corrupted),
                CancellationToken.None));
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
    public async Task OnlyTheModernAzureHostManagedIdentityEndpointIsAdmitted()
    {
        var invalidEnvironments = new (string Name, Action<Dictionary<string, string?>> Mutate)[]
        {
            ("missing endpoint", environment => environment.Remove("IDENTITY_ENDPOINT")),
            ("missing header", environment => environment.Remove("IDENTITY_HEADER")),
            ("blank header", environment => environment["IDENTITY_HEADER"] = "  "),
            ("external endpoint", environment =>
                environment["IDENTITY_ENDPOINT"] = "http://identity.example.invalid/token"),
            ("legacy endpoint", environment => environment["MSI_ENDPOINT"] = "http://127.0.0.1/token"),
            ("legacy secret", environment => environment["MSI_SECRET"] = "platform-secret"),
            ("arc selector", environment => environment["IMDS_ENDPOINT"] = "http://127.0.0.1/token"),
            ("service fabric selector", environment =>
                environment["IDENTITY_SERVER_THUMBPRINT"] = "00"),
            ("federated token selector", environment =>
                environment["AZURE_FEDERATED_TOKEN_FILE"] = "/token/exchange"),
        };

        foreach (var (name, mutate) in invalidEnvironments)
        {
            var environment = ValidEnvironment();
            mutate(environment);
            var storeCreated = false;

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                CustodyProbeApplication.RunAsync(
                    ["write", "nightly_floor_90d"],
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
        var body = new byte[32];
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
            foreach (var command in new[] { "read", "read-receipt" })
            {
                var storeCreated = false;
                var arguments = command == "read"
                    ? new[] { "read" }
                    : new[] { "read-receipt", EncodeReceipt(receipt) };
                var input = command == "read"
                    ? new StringReader(ContractJson.Serialize(receipt))
                    : TextReader.Null;
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                    CustodyProbeApplication.RunAsync(
                        arguments,
                        input,
                        TextWriter.Null,
                        ValidEnvironment(),
                        _ =>
                        {
                            storeCreated = true;
                            return new ProbeStore(body);
                        },
                        CancellationToken.None));
                Assert.IsFalse(storeCreated, command);
            }
        }
    }

    [TestMethod]
    public async Task ReadAcceptsOnlyTheExactSyntheticProbeByteCount()
    {
        foreach (var byteCount in new[] { 31, 33 })
        {
            var body = new byte[byteCount];
            var receipt = ReceiptFor(body, CustodyClass.NightlyFloor90d);
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

            Assert.IsFalse(storeCreated, byteCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [TestMethod]
    public async Task WriteRefusesAReceiptForDifferentBytesOrLane()
    {
        foreach (var mismatch in Enum.GetValues<WriteReceiptMismatch>())
        {
            var output = new StringWriter();

            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
                CustodyProbeApplication.RunAsync(
                    ["write", "nightly_floor_90d"],
                    TextReader.Null,
                    output,
                    ValidEnvironment(),
                    _ => new MismatchingWriteStore(mismatch),
                    CancellationToken.None));

            Assert.AreEqual(string.Empty, output.ToString(), mismatch.ToString());
        }
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
                     new[] { "roundtrip", "nightly_floor_90d" },
                     new[] { "read-receipt" },
                     new[] { "roundtrip" },
                     new[] { "roundtrip", "NightlyFloor90d" },
                     new[] { "roundtrip", "nightly_floor_90d", "extra" },
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
    public async Task PreCancelledProcessReturnsTheFixedCancellationExitCode()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = await Lex.V3.Custody.Probe.Program.RunProcessAsync(
            ["read"],
            cancellation.Token);

        Assert.AreEqual(130, exitCode);
    }

    [TestMethod]
    public async Task ReadCancellationDoesNotDependOnTheReaderCooperating()
    {
        using var cancellation = new CancellationTokenSource();
        var input = new CancellationIgnoringReader();
        var storeCreated = false;
        var run = CustodyProbeApplication.RunAsync(
            ["read"],
            input,
            TextWriter.Null,
            ValidEnvironment(),
            _ =>
            {
                storeCreated = true;
                return new ProbeStore();
            },
            cancellation.Token);

        await input.ReadStarted.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.IsFalse(storeCreated);
    }

    [TestMethod]
    public async Task LinuxSigtermCancelsWithoutReturningOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var assembly = Path.Combine(AppContext.BaseDirectory, "Lex.V3.Custody.Probe.dll");
        Assert.IsTrue(File.Exists(assembly), assembly);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet))
        {
            dotnet = "dotnet";
        }

        var startInfo = new ProcessStartInfo(dotnet)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(assembly);
        startInfo.ArgumentList.Add("read");
        foreach (var name in new[]
                 {
                     "AZURE_CLIENT_SECRET",
                     "AZURE_STORAGE_ACCOUNT_KEY",
                     "AZURE_STORAGE_CONNECTION_STRING",
                     "AZURE_STORAGE_KEY",
                     "LEX_V3_CUSTODY_ACCOUNT_KEY",
                     "LEX_V3_CUSTODY_CLIENT_SECRET",
                     "LEX_V3_CUSTODY_CONNECTION_STRING",
                     "MSI_ENDPOINT",
                     "MSI_SECRET",
                     "IMDS_ENDPOINT",
                     "IDENTITY_SERVER_THUMBPRINT",
                     "AZURE_FEDERATED_TOKEN_FILE",
                 })
        {
            startInfo.Environment.Remove(name);
        }

        foreach (var entry in ValidEnvironment())
        {
            startInfo.Environment[entry.Key] = entry.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.IsFalse(process.HasExited);
        Assert.AreEqual(0, Kill(process.Id, 15));

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
        Assert.AreEqual(string.Empty, await stderrTask);
        Assert.AreEqual(130, process.ExitCode);
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

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);

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
        ["IDENTITY_ENDPOINT"] = "http://127.0.0.1:42356/msi/token",
        ["IDENTITY_HEADER"] = "platform-rotated-header",
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

    private static string EncodeReceipt(DurableBlobWriteReceipt receipt) =>
        EncodeBytes(Encoding.UTF8.GetBytes(ContractJson.Serialize(receipt)));

    private static string EncodeReceiptWithNoncanonicalBase64Url(
        DurableBlobWriteReceipt receipt)
    {
        var json = ContractJson.Serialize(receipt);
        while (Encoding.UTF8.GetByteCount(json) % 3 == 0)
        {
            json += " ";
        }

        var canonical = EncodeBytes(Encoding.UTF8.GetBytes(json));
        const string alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var last = alphabet.IndexOf(canonical[^1], StringComparison.Ordinal);
        var unusedBits = canonical.Length % 4 == 2 ? 4 : 2;
        var groupSize = 1 << unusedBits;
        var replacement = last / groupSize * groupSize + (last + 1) % groupSize;
        return canonical[..^1] + alphabet[replacement];
    }

    private static string EncodeBytes(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static DurableBlobRef ReferenceFor(byte[] body, CustodyClass custodyClass) =>
        new(
            CustodySchemaIds.DurableBlobRef,
            CustodyDigest.Of(body),
            body.LongLength,
            custodyClass);

    private sealed class ProbeStore(ReadOnlyMemory<byte>? restored = null) : ICustodyStore
    {
        public byte[] CreatedBytes { get; private set; } = [];

        public int CreateCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public DurableBlobRef? LastReadReference { get; private set; }

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            CreatedBytes = bytes.ToArray();
            return Task.FromResult(ReceiptFor(bytes.ToArray(), custodyClass));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            LastReadReference = reference;
            return Task.FromResult(restored ?? ReadOnlyMemory<byte>.Empty);
        }
    }

    private enum WriteReceiptMismatch
    {
        ContentDigest,
        CustodyLane,
    }

    private sealed class MismatchingWriteStore(WriteReceiptMismatch mismatch) : ICustodyStore
    {
        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes,
            CustodyClass custodyClass,
            CancellationToken cancellationToken)
        {
            var receiptBytes = bytes.ToArray();
            if (mismatch == WriteReceiptMismatch.ContentDigest)
            {
                receiptBytes[0] ^= byte.MaxValue;
            }

            var receiptLane = mismatch == WriteReceiptMismatch.CustodyLane
                ? CustodyClass.LegalHoldEvidence
                : custodyClass;
            return Task.FromResult(ReceiptFor(receiptBytes, receiptLane));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CancellationIgnoringReader : TextReader
    {
        private readonly TaskCompletionSource readStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> neverCompletes = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => readStarted.Task;

        public override ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            readStarted.TrySetResult();
            return new ValueTask<int>(neverCompletes.Task);
        }
    }
}

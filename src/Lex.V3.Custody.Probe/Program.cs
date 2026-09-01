using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Custody.Azure;

[assembly: InternalsVisibleTo("Lex.V3.Tests")]

namespace Lex.V3.Custody.Probe;

internal static class Program
{
    public static async Task<int> Main(string[] arguments)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArguments) =>
        {
            eventArguments.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        PosixSignalRegistration? termination = null;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                termination = PosixSignalRegistration.Create(
                    PosixSignal.SIGTERM,
                    context =>
                    {
                        context.Cancel = true;
                        cancellation.Cancel();
                    });
            }

            return await RunProcessAsync(arguments, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            termination?.Dispose();
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    internal static async Task<int> RunProcessAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CustodyProbeApplication.RunConsoleAsync(arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
    }
}

internal static class CustodyProbeApplication
{
    private const int MaximumReceiptCharacters = 32 * 1024;
    private const int SyntheticByteCount = 32;

    private static readonly string[] ForbiddenCredentialVariables =
    [
        "AZURE_CLIENT_SECRET",
        "AZURE_STORAGE_ACCOUNT_KEY",
        "AZURE_STORAGE_CONNECTION_STRING",
        "AZURE_STORAGE_KEY",
        "LEX_V3_CUSTODY_ACCOUNT_KEY",
        "LEX_V3_CUSTODY_CLIENT_SECRET",
        "LEX_V3_CUSTODY_CONNECTION_STRING",
    ];

    private static readonly string[] AlternateManagedIdentitySourceVariables =
    [
        "MSI_ENDPOINT",
        "MSI_SECRET",
        "IMDS_ENDPOINT",
        "IDENTITY_SERVER_THUMBPRINT",
        "AZURE_FEDERATED_TOKEN_FILE",
    ];

    internal static async Task<int> RunConsoleAsync(
        string[] arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var environment = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(
                    static entry => (string)entry.Key,
                    static entry => entry.Value?.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            await RunAsync(
                    arguments,
                    Console.In,
                    Console.Out,
                    environment,
                    static options => new AzureBlobCustodyStore(
                        options,
                        new AzureBlobCustodyConfigurationReceiptJournal(options)),
                    cancellationToken)
                .ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await Console.Error.WriteLineAsync("custody_probe_failed").ConfigureAwait(false);
            return 1;
        }
    }

    internal static async Task RunAsync(
        string[] arguments,
        TextReader input,
        TextWriter output,
        IReadOnlyDictionary<string, string?> environment,
        Func<AzureBlobCustodyOptions, ICustodyStore> storeFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(storeFactory);

        var command = ParseCommand(arguments);
        var options = ReadOptions(environment);

        DurableBlobWriteReceipt? inputReceipt = null;
        if (command.Mode == ProbeMode.Read)
        {
            var json = await ReadBoundedAsync(input, cancellationToken).ConfigureAwait(false);
            inputReceipt = ContractJson.Deserialize<DurableBlobWriteReceipt>(json);
            ValidateConfiguredReceipt(inputReceipt, options);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var store = storeFactory(options)
            ?? throw new InvalidOperationException("The custody store factory returned no store.");

        if (command.Mode == ProbeMode.Write)
        {
            var synthetic = RandomNumberGenerator.GetBytes(SyntheticByteCount);
            var receipt = await store.CreateAsync(
                    synthetic,
                    command.CustodyClass!.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            if (receipt is null
                || receipt.Reference.CustodyClass != command.CustodyClass.Value
                || receipt.Reference.ByteLength != synthetic.LongLength
                || !string.Equals(
                    receipt.Reference.ContentSha256,
                    CustodyDigest.Of(synthetic),
                    StringComparison.Ordinal))
            {
                throw new CustodyIntegrityException(
                    "The custody store returned a receipt for different synthetic bytes.");
            }

            ValidateConfiguredReceipt(receipt, options);
            await output.WriteAsync(ContractJson.Serialize(receipt)).ConfigureAwait(false);
            return;
        }

        _ = await CustodyRestore.ReadCheckedAsync(
                store,
                inputReceipt!.Reference,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ProbeCommand ParseCommand(string[] arguments)
    {
        if (arguments.Length == 1 && string.Equals(arguments[0], "read", StringComparison.Ordinal))
        {
            return new ProbeCommand(ProbeMode.Read, null);
        }

        if (arguments.Length == 2 && string.Equals(arguments[0], "write", StringComparison.Ordinal))
        {
            var custodyClass = arguments[1] switch
            {
                "nightly_floor_90d" => CustodyClass.NightlyFloor90d,
                "legal_hold_evidence" => CustodyClass.LegalHoldEvidence,
                _ => throw new ArgumentException("Unknown custody probe lane.", nameof(arguments)),
            };
            return new ProbeCommand(ProbeMode.Write, custodyClass);
        }

        throw new ArgumentException("Expected read or write with one exact custody lane.", nameof(arguments));
    }

    private static AzureBlobCustodyOptions ReadOptions(
        IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var entry in environment)
        {
            if (entry.Value is not null
                && ForbiddenCredentialVariables.Contains(
                    entry.Key,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Secret-bearing credential variable {entry.Key} is forbidden.");
            }
        }

        ValidateManagedIdentitySource(environment);

        return new AzureBlobCustodyOptions(
            new Uri(Required(environment, "LEX_V3_CUSTODY_SERVICE_URI"), UriKind.Absolute),
            Required(environment, "LEX_V3_CUSTODY_STAGING_CONTAINER"),
            Required(environment, "LEX_V3_CUSTODY_NIGHTLY_CONTAINER"),
            Required(environment, "LEX_V3_CUSTODY_LEGAL_HOLD_CONTAINER"),
            RequiredGuid(environment, "LEX_V3_CUSTODY_MANAGED_IDENTITY_CLIENT_ID"),
            RequiredGuid(environment, "LEX_V3_CUSTODY_NIGHTLY_POLICY_KEY"),
            RequiredGuid(environment, "LEX_V3_CUSTODY_LEGAL_HOLD_POLICY_KEY"),
            RequiredGuid(environment, "LEX_V3_CUSTODY_SUBSCRIPTION_ID"),
            Required(environment, "LEX_V3_CUSTODY_RESOURCE_GROUP"));
    }

    private static void ValidateConfiguredReceipt(
        DurableBlobWriteReceipt receipt,
        AzureBlobCustodyOptions options)
    {
        if (receipt.Reference.ByteLength != SyntheticByteCount)
        {
            throw new InvalidOperationException(
                "The custody receipt is not for the exact synthetic probe size.");
        }

        var expectedPolicyKey = receipt.Reference.CustodyClass switch
        {
            CustodyClass.NightlyFloor90d => options.NightlyPolicyKey,
            CustodyClass.LegalHoldEvidence => options.LegalHoldPolicyKey,
            _ => throw new InvalidOperationException(
                "The custody receipt names an unsupported lane."),
        };
        if (receipt.PolicyEvidence.VerificationProfile
                != CustodyVerificationProfile.ImmutableObject1
            || receipt.PolicyEvidence.PolicyKey != expectedPolicyKey)
        {
            throw new InvalidOperationException(
                "The custody receipt does not bind the configured immutable policy lane.");
        }
    }

    private static void ValidateManagedIdentitySource(
        IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var name in AlternateManagedIdentitySourceVariables)
        {
            if (environment.TryGetValue(name, out var value) && value is not null)
            {
                throw new InvalidOperationException(
                    $"Managed identity source selector {name} is forbidden.");
            }
        }

        var endpointText = Required(environment, "IDENTITY_ENDPOINT");
        var identityHeader = Required(environment, "IDENTITY_HEADER");
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !IsLocalIdentityHost(endpoint)
            || string.IsNullOrWhiteSpace(identityHeader))
        {
            throw new InvalidOperationException(
                "IDENTITY_ENDPOINT is not an admitted Azure-host local endpoint.");
        }
    }

    private static bool IsLocalIdentityHost(Uri endpoint)
    {
        if (endpoint.IsLoopback)
        {
            return true;
        }

        if (!IPAddress.TryParse(endpoint.Host, out var address))
        {
            return false;
        }

        if (address.IsIPv6LinkLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static string Required(
        IReadOnlyDictionary<string, string?> environment,
        string name)
    {
        if (!environment.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Required environment variable {name} is missing.");
        }

        return value;
    }

    private static Guid RequiredGuid(
        IReadOnlyDictionary<string, string?> environment,
        string name)
    {
        var value = Required(environment, name);
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
        {
            throw new InvalidOperationException($"Environment variable {name} is not a nonempty D-format GUID.");
        }

        return parsed;
    }

    private static async Task<string> ReadBoundedAsync(
        TextReader input,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var receipt = new System.Text.StringBuilder();
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken)
                .AsTask()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return receipt.ToString();
            }

            if (receipt.Length + read > MaximumReceiptCharacters)
            {
                throw new ArgumentException("The custody receipt exceeds its input bound.", nameof(input));
            }

            receipt.Append(buffer, 0, read);
        }
    }

    private enum ProbeMode
    {
        Write,
        Read,
    }

    private sealed record ProbeCommand(ProbeMode Mode, CustodyClass? CustodyClass);
}

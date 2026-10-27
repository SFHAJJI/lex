using System.Globalization;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Luxembourg;

/// <summary>
/// The two re-derivation steps around an offline <c>derive</c>. Neither reopens an acquisition or reads a body: each reads
/// retained inputs from custody, writes new inputs into it, and writes their pointer beside it. Like <c>derive</c>, each
/// enters the offline guard and measures the process's network activity (<see cref="OfflineNetwork"/>).
/// </summary>
/// <remarks>
/// <c>rederive-inputs</c> writes unsealed inputs <c>lex-v3-offline-mount-inputs/3</c> over an acquisition's retained inputs
/// /1 or /2 (its <c>mount-inputs-*.json</c> pointer). <c>derive</c> over them prints the record digest
/// (<c>luxembourg_rederivation=</c>), and <c>rederive-seal</c> then writes the same inputs expecting exactly that record, so
/// a later derive of the sealed inputs refuses any other. Exit codes: 0 written; 2 usage; 3 refused (printed); 5 network
/// activity measured (the pointer is removed; the custody object, append-only, stays).
/// </remarks>
internal static class RederiveCommands
{
    private const string InputsUsage =
        "Usage: Lex.V3.Tool rederive-inputs --custody <directory> --base <mount-inputs.json> --rules <rule-set> "
        + "--policy <sha256> --derived-at <yyyy-MM-ddTHH:mm:ssZ> [--custody-encoding raw|brotli]\n"
        + "  --base        the acquisition's retained inputs /1 or /2 pointer (mount-inputs-*.json)\n"
        + "  --rules       a rule set this build implements: " + RuleSets + "\n"
        + "  --policy      the rights admission policy digest the rule set names (lu-release-1: policy /1; lu-release-2: policy A)\n"
        + "  --derived-at  when the re-derivation is stated to run, a whole UTC second, not before the acquisition's build time";

    private const string SealUsage =
        "Usage: Lex.V3.Tool rederive-seal --custody <directory> --inputs <mount-inputs.json> --record <sha256> "
        + "[--custody-encoding raw|brotli]\n"
        + "  --inputs  the unsealed inputs /3 pointer a derive ran\n"
        + "  --record  the luxembourg_rederivation digest that derive printed";

    private const string RuleSets = LuxembourgRederivation.Release1RuleSet + ", " + LuxembourgRederivation.Release2RuleSet;

    internal static async Task<int> InputsAsync(string[] args)
    {
        using var network = OfflineNetwork.Begin();
        if (!TryOptions(args, ["--custody", "--base", "--rules", "--policy", "--derived-at"], InputsUsage, out var options))
        {
            return 2;
        }

        if (!DateTimeOffset.TryParseExact(options["--derived-at"], "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var derivedAt))
        {
            Console.Error.WriteLine("--derived-at must be a whole UTC second, yyyy-MM-ddTHH:mm:ssZ.");
            return 2;
        }

        try
        {
            var (store, custody) = Open(options);
            var baseInputs = await ReadPointerAsync(options["--base"]);
            var inputs = await V3OfflineMount.CaptureRederivationAsync(store, baseInputs, options["--rules"], options["--policy"],
                derivedAt, CancellationToken.None);
            var pointer = await WritePointerAsync(custody, inputs);
            if (OfflineNetwork.Refusal(network, "rederive-inputs") is { } refusal)
            {
                File.Delete(pointer);
                return refusal;
            }

            Console.WriteLine($"re-derivation inputs (unsealed): {pointer}; rule set {options["--rules"]}; "
                + $"policy {options["--policy"]}; derived_at {options["--derived-at"]}; base {baseInputs.Sha256}");
            Console.WriteLine($"next: derive --custody {custody} --checkpoint {pointer} --out <fresh-directory>, then rederive-seal "
                + "with the luxembourg_rederivation it prints");
            Console.WriteLine(OfflineNetwork.Line(network));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or CustodyRequiredException or CustodyIntegrityException
            or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"rederive-inputs refused: {exception.GetType().Name}: {exception.Message} ({OfflineNetwork.Line(network)})");
            return OfflineNetwork.Refusal(network, "rederive-inputs") ?? 3;
        }
    }

    internal static async Task<int> SealAsync(string[] args)
    {
        using var network = OfflineNetwork.Begin();
        if (!TryOptions(args, ["--custody", "--inputs", "--record"], SealUsage, out var options))
        {
            return 2;
        }

        if (!CustodyDigest.IsLowercaseSha256(options["--record"]))
        {
            Console.Error.WriteLine("--record must be the record's lowercase SHA-256.");
            return 2;
        }

        try
        {
            var (store, custody) = Open(options);
            var unsealed = await ReadPointerAsync(options["--inputs"]);
            var sealedInputs = await V3OfflineMount.SealRederivationAsync(store, unsealed, options["--record"], CancellationToken.None);
            var pointer = await WritePointerAsync(custody, sealedInputs);
            if (OfflineNetwork.Refusal(network, "rederive-seal") is { } refusal)
            {
                File.Delete(pointer);
                return refusal;
            }

            Console.WriteLine($"re-derivation inputs (sealed): {pointer}; expected_rederivation {options["--record"]}; "
                + $"unsealed {unsealed.Sha256}");
            Console.WriteLine(OfflineNetwork.Line(network));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or CustodyRequiredException or CustodyIntegrityException
            or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"rederive-seal refused: {exception.GetType().Name}: {exception.Message} ({OfflineNetwork.Line(network)})");
            return OfflineNetwork.Refusal(network, "rederive-seal") ?? 3;
        }
    }

    private static bool TryOptions(string[] args, string[] required, string usage, out Dictionary<string, string> options)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        options = parsed;
        string[] admitted = [.. required, "--custody-encoding"];
        if (args.Length % 2 != 0)
        {
            Console.Error.WriteLine(usage);
            return false;
        }

        for (var index = 0; index < args.Length; index += 2)
        {
            if (!admitted.Contains(args[index], StringComparer.Ordinal) || !options.TryAdd(args[index], args[index + 1]))
            {
                Console.Error.WriteLine(usage);
                return false;
            }
        }

        if (required.Any(key => !parsed.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) ||
            parsed.GetValueOrDefault("--custody-encoding", "raw") is not ("raw" or "brotli"))
        {
            Console.Error.WriteLine(usage);
            return false;
        }

        return true;
    }

    private static (ICustodyStore Store, string Custody) Open(Dictionary<string, string> options)
    {
        var custody = Path.GetFullPath(options["--custody"]);
        if (!Directory.Exists(custody))
        {
            throw new ArgumentException("The retained custody directory does not exist.");
        }

        ICustodyStore store = options.GetValueOrDefault("--custody-encoding", "raw") == "brotli"
            ? FileSystemCustodyStore.WithBrotliCompression(custody)
            : new FileSystemCustodyStore(custody);
        return (store, custody);
    }

    private static async Task<SourceArtifactRef> ReadPointerAsync(string path) =>
        ContractJson.Deserialize<SourceArtifactRef>(await File.ReadAllTextAsync(Path.GetFullPath(path)));

    private static async Task<string> WritePointerAsync(string custody, SourceArtifactRef inputs)
    {
        var pointer = Path.Combine(custody, "mount-inputs-" + inputs.Sha256 + ".json");
        await File.WriteAllTextAsync(pointer, ContractJson.Serialize(inputs));
        return pointer;
    }
}

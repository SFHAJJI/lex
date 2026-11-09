using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest;

/// <summary>
/// <c>compose-inputs</c>: offline mount inputs that pair one acquisition's EU catalog with another acquisition's Luxembourg
/// half (<see cref="V3OfflineMount.ComposeAsync"/>), so a refreshed EU population is derived with the Luxembourg population
/// served now, or the other way round. Like <c>derive</c> and <c>rederive-inputs</c>, it enters the offline guard and measures
/// the process's network activity (<see cref="OfflineNetwork"/>); it reopens both halves from custody, reads no publisher and
/// writes the inputs into custody and their pointer beside it.
/// </summary>
/// <remarks>
/// Exit codes: 0 written; 2 usage; 3 refused (printed: a catalog of other seeds or scope, an object custody does not hold, an
/// undated EU catalog, a base that is a re-derivation's); 5 network activity measured (the pointer is removed; the custody
/// object, append-only, stays).
/// </remarks>
internal static class ComposeInputsCommand
{
    private const string Usage =
        "Usage: Lex.V3.Tool compose-inputs --custody <directory> --base <mount-inputs.json> --europe <eu-acquisition.json> "
        + "[--custody-encoding raw|brotli]\n"
        + "  --custody  the custody holding both acquisitions' objects (one tree, or one linked into the other)\n"
        + "  --base     the retained inputs /1 or /2 pointer (mount-inputs-*.json) whose Luxembourg half, seeds and scope are kept\n"
        + "  --europe   the EU catalog pointer (eu-acquisition-*.json) of the acquisition whose EU half replaces the base's;\n"
        + "             it is reopened against the base's seeds and must state when its collection completed\n"
        + "Exit codes: 0 written, 2 usage, 3 refused, 5 network activity measured (the pointer is removed)";

    internal static async Task<int> RunAsync(string[] args)
    {
        using var network = OfflineNetwork.Begin();
        string[] required = ["--custody", "--base", "--europe"];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        for (var index = 0; index < args.Length; index += 2)
        {
            if (args[index] is not ("--custody" or "--base" or "--europe" or "--custody-encoding") ||
                !options.TryAdd(args[index], args[index + 1]))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
        }

        if (required.Any(key => !options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) ||
            options.GetValueOrDefault("--custody-encoding", "raw") is not ("raw" or "brotli"))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        try
        {
            var custody = Path.GetFullPath(options["--custody"]);
            if (!Directory.Exists(custody))
            {
                throw new ArgumentException("The retained custody directory does not exist.");
            }

            ICustodyStore store = options.GetValueOrDefault("--custody-encoding", "raw") == "brotli"
                ? FileSystemCustodyStore.WithBrotliCompression(custody)
                : new FileSystemCustodyStore(custody);
            var baseInputs = ContractJson.Deserialize<SourceArtifactRef>(await File.ReadAllTextAsync(Path.GetFullPath(options["--base"])));
            var europe = ContractJson.Deserialize<SourceArtifactRef>(await File.ReadAllTextAsync(Path.GetFullPath(options["--europe"])));
            var inputs = await V3OfflineMount.ComposeAsync(store, baseInputs, europe, CancellationToken.None);
            var pointer = Path.Combine(custody, "mount-inputs-" + inputs.Sha256 + ".json");
            await File.WriteAllTextAsync(pointer, ContractJson.Serialize(inputs));
            if (OfflineNetwork.Refusal(network, "compose-inputs") is { } refusal)
            {
                File.Delete(pointer);
                return refusal;
            }

            Console.WriteLine($"composed inputs: {pointer}; base {baseInputs.Sha256} (its Luxembourg half kept); "
                + $"europe catalog {europe.Sha256}");
            Console.WriteLine($"next: rederive-inputs --custody {custody} --base {pointer} ..., or derive --custody {custody} "
                + $"--checkpoint {pointer} --out <fresh-directory>");
            Console.WriteLine(OfflineNetwork.Line(network));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or CustodyRequiredException or CustodyIntegrityException
            or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"compose-inputs refused: {exception.GetType().Name}: {exception.Message} ({OfflineNetwork.Line(network)})");
            return OfflineNetwork.Refusal(network, "compose-inputs") ?? 3;
        }
    }
}

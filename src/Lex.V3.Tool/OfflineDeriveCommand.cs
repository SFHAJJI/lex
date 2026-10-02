using System.Runtime.InteropServices;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest;

internal static class OfflineDeriveCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        const string usage = "Usage: Lex.V3.Tool derive --custody <directory> --checkpoint <mount-inputs.json> --out <empty-directory> [--custody-encoding raw|brotli]";
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0) { Console.Error.WriteLine(usage); return 2; }
        for (var index = 0; index < args.Length; index += 2)
            if (args[index] is not ("--custody" or "--checkpoint" or "--out" or "--custody-encoding") ||
                !options.TryAdd(args[index], args[index + 1])) { Console.Error.WriteLine(usage); return 2; }
        if (new[] { "--custody", "--checkpoint", "--out" }.Any(key => !options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
        { Console.Error.WriteLine(usage); return 2; }
        var encoding = options.GetValueOrDefault("--custody-encoding", "raw");
        if (encoding is not ("raw" or "brotli")) { Console.Error.WriteLine(usage); return 2; }
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += Cancel;
        using var termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; cancellation.Cancel(); });
        void Cancel(object? sender, ConsoleCancelEventArgs eventArgs) { eventArgs.Cancel = true; cancellation.Cancel(); }
        try
        {
            var custody = Path.GetFullPath(options["--custody"]);
            if (!Directory.Exists(custody)) { Console.Error.WriteLine("The retained custody directory does not exist."); return 2; }
            var checkpoint = ContractJson.Deserialize<SourceArtifactRef>(await File.ReadAllTextAsync(options["--checkpoint"], cancellation.Token));
            var store = encoding == "brotli" ? FileSystemCustodyStore.WithBrotliCompression(custody) : new FileSystemCustodyStore(custody);
            var write = await V3OfflineMount.DeriveAsync(store, checkpoint, options["--out"], cancellation.Token);
            foreach (var file in Directory.EnumerateFiles(write.Directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                await using var stream = File.OpenRead(file);
                var digest = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellation.Token));
                Console.WriteLine($"{Path.GetRelativePath(write.Directory, file)} sha256={digest}");
            }
            Console.WriteLine("verified offline mount; publisher_requests=0");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Console.Error.WriteLine("cancelled"); return 130; }
        catch (Exception exception) { Console.Error.WriteLine($"derive refused: {exception.GetType().Name}: {exception.Message}"); return 3; }
        finally { Console.CancelKeyPress -= Cancel; }
    }
}

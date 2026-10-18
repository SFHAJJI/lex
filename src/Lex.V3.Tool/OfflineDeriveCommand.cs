using System.Runtime.InteropServices;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest;

internal static class OfflineDeriveCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        const string usage = "Usage: Lex.V3.Tool derive --custody <directory> --checkpoint <mount-inputs.json> --out <empty-directory> [--custody-encoding raw|brotli]\n"
            + "Exit codes: 0 derived and verified, 2 usage, 3 refused, 5 network activity measured (the output is removed), 130 cancelled";
        // Before any work: no publisher session can be built in this process, and every network operation it starts is counted.
        using var network = OfflineNetwork.Begin();
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
            // First, the staging a stopped derive left behind, on the disk this one needs (the review of #986).
            StaleStagingSweep.Run();
            var custody = Path.GetFullPath(options["--custody"]);
            if (!Directory.Exists(custody)) { Console.Error.WriteLine("The retained custody directory does not exist."); return 2; }
            var checkpoint = ContractJson.Deserialize<SourceArtifactRef>(await File.ReadAllTextAsync(options["--checkpoint"], cancellation.Token));
            var store = encoding == "brotli" ? FileSystemCustodyStore.WithBrotliCompression(custody) : new FileSystemCustodyStore(custody);
            var write = await V3OfflineMount.DeriveAsync(store, checkpoint, options["--out"], cancellation.Token);
            // A derive that measured any network activity leaves no mount behind: the directory it wrote was empty before it.
            if (OfflineNetwork.Refusal(network, "derive") is { } refusal)
            {
                Directory.Delete(write.Directory, recursive: true);
                return refusal;
            }

            foreach (var file in Directory.EnumerateFiles(write.Directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                await using var stream = File.OpenRead(file);
                var digest = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellation.Token));
                Console.WriteLine($"{Path.GetRelativePath(write.Directory, file)} sha256={digest}");
            }
            // A re-derivation names its record: rederive-seal takes this digest.
            if (write.LuxembourgRederivation is { } record)
                Console.WriteLine($"luxembourg_rederivation={record.Sha256}");
            Console.WriteLine(OfflineNetwork.Line(network));
            // The process's own peaks (on Windows, the paged figure is the private commit), beside the runner's machine-wide
            // resource log: the dry run reads both (S4b).
            using (var self = System.Diagnostics.Process.GetCurrentProcess())
                Console.WriteLine($"process peak_working_set_bytes={self.PeakWorkingSet64} peak_private_bytes={self.PeakPagedMemorySize64}");
            Console.WriteLine($"verified offline mount; publisher_requests={network.Total}");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Console.Error.WriteLine("cancelled"); return 130; }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"derive refused: {exception.GetType().Name}: {exception.Message} ({OfflineNetwork.Line(network)})");
            return OfflineNetwork.Refusal(network, "derive") ?? 3;
        }
        finally { Console.CancelKeyPress -= Cancel; }
    }
}

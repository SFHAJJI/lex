using Lex.V3.Ingest.Names;

/// <summary>
/// <c>Lex.V3.Tool names-records --mount &lt;v3-corpus&gt; --out &lt;directory&gt;</c>: R92(1)'s measurement over a mount, read in
/// place and read-only (<see cref="NamesMeasurement"/>), written as the indexing record, the names record and the
/// measurement's counts into a directory of their own, never into the mount (an installed mount is not written to; the
/// release carries the two records beside the mount in its image: <c>web/scripts/image-rehearsal.mjs</c>). Offline: no
/// request is made. It prints counts only, never a name.
/// </summary>
internal static class NamesRecordsCommand
{
    internal const string Usage =
        "Usage: Lex.V3.Tool names-records --mount <v3-corpus directory> --out <new or empty directory>\n" +
        "Exit codes: 0 measured and written, 2 usage, 3 the mount could not be measured, 130 cancelled";

    internal static int Run(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        for (var index = 0; index < args.Length; index += 2)
        {
            if (args[index] is not ("--mount" or "--out") || !options.TryAdd(args[index], args[index + 1]))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
        }

        if (!options.TryGetValue("--mount", out var mountOption) || !options.TryGetValue("--out", out var outOption) ||
            string.IsNullOrWhiteSpace(mountOption) || string.IsNullOrWhiteSpace(outOption))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var mount = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountOption));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outOption));
        if (!Directory.Exists(mount))
        {
            Console.Error.WriteLine("The mount directory does not exist.");
            return 2;
        }

        // Never beside or inside the mount it reads: an installed mount is read-only.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(output, mount, comparison) || output.StartsWith(mount + Path.DirectorySeparatorChar, comparison))
        {
            Console.Error.WriteLine("--out must not be the mount or inside it: the measurement writes nothing beside the mount it reads.");
            return 2;
        }

        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            Console.Error.WriteLine("--out must be a new or empty directory: a measurement never overwrites another's files.");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += Cancel;
        try
        {
            var outcome = NamesMeasurement.Measure(mount, static line => Console.Error.WriteLine(line), cancellation.Token);
            NamesMeasurement.WriteFiles(outcome, output);
            Console.WriteLine($"withheld={outcome.Withheld.Count} indexed={outcome.Indexed.Count}");
            foreach (var name in new[] { IndexingRecord.FileName, NamesRecord.FileName, NamesMeasurement.MeasurementFileName })
            {
                var bytes = File.ReadAllBytes(Path.Combine(output, name));
                Console.WriteLine($"{name} bytes={bytes.Length} sha256={Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))}");
            }

            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("cancelled");
            return 130;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Console.Error.WriteLine($"names-records refused: {exception.GetType().Name}: {exception.Message}");
            return 3;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }
}

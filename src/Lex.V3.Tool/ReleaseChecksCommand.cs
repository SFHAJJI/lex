using Lex.V3.Ingest.Releases;

/// <summary>
/// <c>Lex.V3.Tool release-checks --mount &lt;candidate v3-corpus&gt; --out &lt;directory&gt; [--serving-mount &lt;v3-corpus&gt;
/// --serving-report &lt;mount-report.json&gt;] [--explanations &lt;file&gt;]</c>: the release path's data checks over the candidate
/// release's mount, read in place and read-only, written into a directory of their own, never beside the mount.
/// <list type="bullet">
/// <item>R86(2), always: <c>consolidation-text.json</c>, every admitted consolidation file that yields no text, by identity
/// and typed outcome (<see cref="V3ConsolidationText"/>).</item>
/// <item>R106, with the release serving now: <c>release-comparison.json</c>, the candidate against the serving release's
/// mount, whose files are first held to that release's signed mount report (<see cref="V3ReleaseComparison"/>).</item>
/// </list>
/// The candidate's mount is held to its own build report when it carries one. Offline: no request is made.
/// </summary>
internal static class ReleaseChecksCommand
{
    internal const string Usage =
        "Usage: Lex.V3.Tool release-checks --mount <candidate v3-corpus> --out <new or empty directory>\n" +
        "         [--serving-mount <the serving release's v3-corpus> --serving-report <the serving release's mount-report.json>]\n" +
        "         [--explanations <the release's lex-v3-release-explanations/1 file>]\n" +
        "Exit codes: 0 measured and every check passes, 4 measured and a check refuses the release (the files list why),\n" +
        "            2 usage, 3 a mount could not be measured or does not match its report, 130 cancelled";

    private static readonly string[] Options = ["--mount", "--out", "--serving-mount", "--serving-report", "--explanations"];

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
            if (!Options.Contains(args[index], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(args[index + 1]) || !options.TryAdd(args[index], args[index + 1]))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
        }

        if (!options.TryGetValue("--mount", out var mountOption) || !options.TryGetValue("--out", out var outOption) ||
            options.ContainsKey("--serving-mount") != options.ContainsKey("--serving-report"))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var mount = Full(mountOption);
        var output = Full(outOption);
        var servingMount = options.TryGetValue("--serving-mount", out var servingOption) ? Full(servingOption) : null;
        foreach (var directory in new[] { mount, servingMount }.OfType<string>())
        {
            if (!Directory.Exists(directory))
            {
                Console.Error.WriteLine($"The mount directory {directory} does not exist.");
                return 2;
            }

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(output, directory, comparison) || output.StartsWith(directory + Path.DirectorySeparatorChar, comparison))
            {
                Console.Error.WriteLine("--out must not be a mount or inside one: the checks write nothing beside a mount they read.");
                return 2;
            }
        }

        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            Console.Error.WriteLine("--out must be a new or empty directory: the checks never overwrite another's files.");
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
            void Log(string line) => Console.Error.WriteLine(line);
            var explanations = options.TryGetValue("--explanations", out var explanationsPath)
                ? V3ReleaseExplanations.Read(File.ReadAllBytes(Full(explanationsPath)))
                : null;

            // The candidate held to its own build report, when it carries one (a fixture mount does not).
            var candidateFiles = new List<KeyValuePair<string, string>>();
            var candidateReport = Path.Combine(mount, "build-report.json");
            if (File.Exists(candidateReport))
            {
                Log("holding the candidate's mount to its build report");
                var bytes = File.ReadAllBytes(candidateReport);
                var failures = V3MountInventory.FileFailures(mount, bytes, cancellation.Token);
                if (failures.Count > 0)
                {
                    Console.Error.WriteLine("release-checks refused: the candidate's mount is not the one its build report records:\n- " + string.Join("\n- ", failures));
                    return 3;
                }

                candidateFiles.AddRange(ReportFiles(bytes));
            }

            var candidate = V3MountInventory.Read(mount, Log, cancellation.Token);
            var candidateFacts = new V3ComparedMount("candidate", candidate.CorpusSha256, candidate.LuxembourgIndexSchema, candidate.EuropeIndexSchema, candidateFiles);
            Directory.CreateDirectory(output);
            var refused = false;

            var text = V3ConsolidationText.Measure(candidate, explanations);
            var textBytes = text.Write(candidateFacts, explanations);
            File.WriteAllBytes(Path.Combine(output, V3ConsolidationText.FileName), textBytes);
            refused |= text.Refuses;
            foreach (var count in text.Counts)
            {
                Console.WriteLine($"consolidation-text {count.Publisher}: admitted={count.Admitted} with_text={count.WithText} listed={count.Listed} unexplained={count.Unexplained}");
            }

            Console.WriteLine($"{V3ConsolidationText.FileName} verdict={(text.Refuses ? "refuse" : "pass")} sha256={Sha256(textBytes)}");

            if (servingMount is not null)
            {
                Log("holding the serving release's mount to its signed mount report");
                var reportBytes = File.ReadAllBytes(Full(options["--serving-report"]));
                var failures = V3MountInventory.FileFailures(servingMount, reportBytes, cancellation.Token);
                if (failures.Count > 0)
                {
                    Console.Error.WriteLine("release-checks refused: the serving mount is not the one the serving release's mount report records:\n- " + string.Join("\n- ", failures));
                    return 3;
                }

                var serving = V3MountInventory.Read(servingMount, Log, cancellation.Token);
                var servingFacts = new V3ComparedMount("serving", serving.CorpusSha256, serving.LuxembourgIndexSchema, serving.EuropeIndexSchema, ReportFiles(reportBytes));
                var comparison = V3ReleaseComparison.Compare(serving, candidate, explanations);
                var comparisonBytes = comparison.Write(servingFacts, candidateFacts, explanations);
                File.WriteAllBytes(Path.Combine(output, V3ReleaseComparison.FileName), comparisonBytes);
                refused |= comparison.Refuses;
                foreach (var row in comparison.Classes)
                {
                    Console.WriteLine(
                        $"release-comparison {row.Publisher} {row.Family}/{row.Kind}: " +
                        $"versions kept={row.Versions.Kept} added={row.Versions.Added} changed={row.Versions.Changed} lost={row.Versions.Lost}; " +
                        $"articles kept={row.Articles.Kept} added={row.Articles.Added} changed={row.Articles.Changed} lost={row.Articles.Lost}");
                }

                Console.WriteLine($"{V3ReleaseComparison.FileName} verdict={(comparison.Refuses ? "refuse" : "pass")} listed={comparison.Items.Count} unexplained={comparison.Unexplained} sha256={Sha256(comparisonBytes)}");
            }

            return refused ? 4 : 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("cancelled");
            return 130;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"release-checks refused: {exception.GetType().Name}: {exception.Message}");
            return 3;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    private static IReadOnlyList<KeyValuePair<string, string>> ReportFiles(byte[] reportBytes)
    {
        using var document = System.Text.Json.JsonDocument.Parse(reportBytes);
        return document.RootElement.GetProperty("files").EnumerateArray()
            .Select(static file => KeyValuePair.Create(file.GetProperty("Name").GetString()!, file.GetProperty("Sha256").GetString()!))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
    }
}

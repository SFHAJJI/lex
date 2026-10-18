using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S4b's measurement: the derive's late phase (the mount build with its double builds, the write and the verification)
/// over a synthetic population, with this process's memory sampled phase by phase. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN. It runs only with <c>LEX_DERIVE_MEMORY_PROBE=1</c>, on Windows. <c>LEX_DERIVE_MEMORY_PROBE_ARTICLES</c> (default
/// 20,000) and <c>LEX_DERIVE_MEMORY_PROBE_TEXT_BYTES</c> (default 1,200) size the synthetic consolidation;
/// <c>LEX_DERIVE_MEMORY_PROBE_OUT</c> names a directory that receives the summary as <c>&lt;label&gt;.json</c>, the label
/// from <c>LEX_DERIVE_MEMORY_PROBE_LABEL</c> (default <c>probe</c>). Nothing goes over the network; the mount it writes is
/// removed.
/// </para>
/// <para>
/// WHAT IT RUNS. The acquisition fixture's two populations (<see cref="V3FirstMountBuildTests.AcquireAsync"/>): the EU one
/// as it is, the Luxembourg consolidation replaced by that many synthetic articles, every byte written here in the
/// publisher's shape (<see cref="LuxembourgRederivationTests.SyntheticConsolidation"/>'s identification, invented wording).
/// R0's own derivations, then R1 under Release 1's rule set, then the derive's late phase exactly as
/// <see cref="V3OfflineMount.DeriveAsync"/> runs it: <see cref="V3FirstMountBuild"/>, <see cref="V3CorpusMountWriter.WriteAsync(V3FirstMountBuildResult, string, V3GenerationSource?, CancellationToken, DateTimeOffset?)"/>
/// and <see cref="V3CorpusMountWriter.VerifyAsync(string, CancellationToken)"/>.
/// </para>
/// <para>
/// WHAT IT MEASURES. Every 10 ms, from <c>GetProcessMemoryInfo</c>: the process's private commit (what the runner's guard
/// counts against the machine's commit limit) and its working set, the highest of each kept per phase. The baseline is the
/// commit after a full compacting collection, once R1 is done and before the build: what each phase adds above it is what the
/// late phase costs. The five mount files' lengths and digests are printed, so a run before a change and a run after it are
/// held to the same bytes.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class V3DeriveMemoryProbe
{
    private const string EnableVariable = "LEX_DERIVE_MEMORY_PROBE";

    private static readonly string[] Words =
    [
        "le", "texte", "synthétique", "de", "cet", "article", "sert", "à", "mesurer", "la", "mémoire", "du", "calcul", "et",
        "ne", "dit", "rien", "du", "droit", "applicable", "chaque", "phrase", "est", "faite", "pour", "la", "mesure", "seulement",
    ];

    [TestMethod]
    public async Task TheDerivesLatePhaseIsMeasuredOverASyntheticPopulation()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1" || !OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Set LEX_DERIVE_MEMORY_PROBE=1 (on Windows) for S4b's synthetic measurement of the derive's late phase.");
        }

        var articles = Number("LEX_DERIVE_MEMORY_PROBE_ARTICLES", 20_000);
        var textBytes = Number("LEX_DERIVE_MEMORY_PROBE_TEXT_BYTES", 1_200);
        var label = Environment.GetEnvironmentVariable("LEX_DERIVE_MEMORY_PROBE_LABEL") is { Length: > 0 } named ? named : "probe";
        var clock = Stopwatch.StartNew();
        using var sampler = new MemorySampler();

        // ---- R0 and R1 over the synthetic population. ----
        sampler.Enter("acquire_r0_r1");
        var body = Consolidation(articles, textBytes);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), body, new ReadingClock());
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(verified.Delivered, verified.Detail);
        var records = verified.Run!.CorpusRecordSet!.Set.Records;
        Assert.IsTrue(records.Any(static record => record.Body.Kind == CorpusBodyRecordKind.Held), string.Join("; ", records.Select(static record =>
            $"{record.ObjectRef.PublisherUri} {record.Body.Kind} {record.Body.NotHeldReason} {record.Body.PendingAcquisitionReason?.Kind}")));
        var rederived = await LuxembourgRederivation.ApplyAsync(store, verified, LuxembourgRederivationTests.Release1(), CancellationToken.None);
        var admitted = rederived.AknLegalContent!.Outcomes.Count(static outcome => outcome.Disposition == LuxembourgAknLegalContentDisposition.Admitted);
        Assert.AreEqual(articles, admitted, "every synthetic article is admitted under content /3");
        verified = null!;
        var acquiredAt = clock.Elapsed;

        // ---- The baseline: what is held once R1 is done (the populations, R1's outcomes, the custody store). ----
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        var baseline = MemorySampler.Read();

        // ---- The late phase, as V3OfflineMount.DeriveAsync runs it. ----
        var buildClock = new V3OfflineMount.BuildClock(new DateTimeOffset(2026, 10, 12, 18, 17, 5, TimeSpan.Zero));
        var directory = Path.Combine(Path.GetTempPath(), "lex-v3-derive-memory-probe-" + Guid.NewGuid().ToString("N"));
        var phases = new List<object>();
        var files = new List<object>();
        try
        {
            sampler.Enter("build");
            var started = clock.Elapsed;
            var build = await new V3FirstMountBuild(store, buildClock).RunAsync(europe, rederived, CancellationToken.None);
            Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
            var built = clock.Elapsed;

            sampler.Enter("write");
            try
            {
                await V3CorpusMountWriter.WriteAsync(build, directory, null, CancellationToken.None, buildClock.GetUtcNow());
            }
            finally
            {
                ((object)build as IDisposable)?.Dispose();
            }

            build = null!;
            var writtenAt = clock.Elapsed;

            sampler.Enter("verify");
            var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
            Assert.IsTrue(verification.Verified, verification.Detail);
            var verifiedAt = clock.Elapsed;
            sampler.Enter("done");

            foreach (var (phase, seconds) in new[]
                     {
                         ("acquire_r0_r1", acquiredAt.TotalSeconds), ("build", (built - started).TotalSeconds),
                         ("write", (writtenAt - built).TotalSeconds), ("verify", (verifiedAt - writtenAt).TotalSeconds),
                     })
            {
                var peak = sampler.PeakOf(phase);
                phases.Add(new
                {
                    phase,
                    seconds = Math.Round(seconds, 1),
                    peak_private_bytes = peak.Private,
                    peak_private_above_baseline_bytes = peak.Private - baseline.Private,
                    peak_working_set_bytes = peak.WorkingSet,
                });
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                await using var stream = File.OpenRead(file);
                files.Add(new
                {
                    name = Path.GetRelativePath(directory, file).Replace('\\', '/'),
                    bytes = stream.Length,
                    sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)),
                });
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        var final = MemorySampler.Read();
        var summary = JsonSerializer.Serialize(new
        {
            label,
            synthetic = new { articles, text_bytes = textBytes, body_bytes = body.Length },
            baseline_private_bytes = baseline.Private,
            baseline_working_set_bytes = baseline.WorkingSet,
            process_peak_working_set_bytes = final.PeakWorkingSet,
            process_peak_private_bytes = final.PeakPrivate,
            phases,
            files,
            gc = new { server = GCSettings.IsServerGC, latency = GCSettings.LatencyMode.ToString() },
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(summary);
        if (Environment.GetEnvironmentVariable("LEX_DERIVE_MEMORY_PROBE_OUT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, label + ".json"), summary + "\n");
        }
    }

    /// <summary>The fixture's synthetic consolidation with <paramref name="count"/> articles of about <paramref name="textBytes"/> bytes of invented French each; every twentieth cites an act.</summary>
    internal static byte[] Consolidation(int count, int textBytes)
    {
        var synthetic = Encoding.UTF8.GetString(LuxembourgRederivationTests.SyntheticConsolidation());
        var body = synthetic.IndexOf("<body>", StringComparison.Ordinal);
        var end = synthetic.IndexOf("</body>", StringComparison.Ordinal);
        Assert.IsTrue(body > 0 && end > body);
        var document = new StringBuilder(synthetic.Length + (count * (textBytes + 120)));
        document.Append(synthetic, 0, body).Append("<body>\n");
        for (var number = 1; number <= count; number++)
        {
            var id = number.ToString(CultureInfo.InvariantCulture);
            document.Append("<article id=\"art_").Append(id).Append("\"><num>Art. ").Append(id).Append(".</num><content><p>");
            document.Append("Article synthétique numéro ").Append(id).Append(" :");
            var written = 0;
            for (var word = 0; written < textBytes; word++)
            {
                var next = Words[(word + number) % Words.Length];
                document.Append(' ').Append(next);
                written += next.Length + 1;
                if (word % 17 == 16) document.Append('.');
            }

            if (number % 20 == 0)
            {
                document.Append(" Selon la <ref href=\"/eli/etat/leg/loi/2002/02/02/n2/jo\">loi synthétique citée</ref>");
            }

            document.Append(".</p></content></article>\n");
        }

        document.Append(synthetic, end, synthetic.Length - end);
        return Encoding.UTF8.GetBytes(document.ToString());
    }

    /// <summary>
    /// The Luxembourg fixture's clock (<see cref="LuxembourgAcquisitionTestFixture.FixedTimeProvider"/>: each reading of the
    /// time two seconds later, from the same epoch), except that the elapsed time a body read measures moves 10 ms a reading
    /// instead of two seconds, so a synthetic body of tens of megabytes is read within the request timeout.
    /// </summary>
    private sealed class ReadingClock : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
        private long _ticks;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => Epoch.AddTicks(Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(2).Ticks));

        public override long GetTimestamp() => Interlocked.Add(ref _timestamp, TimeSpan.FromMilliseconds(10).Ticks);
    }

    private static int Number(string variable, int fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)
            : fallback;

    /// <summary>The process's private commit and working set, read every 10 ms; the highest of each kept per phase.</summary>
    private sealed class MemorySampler : IDisposable
    {
        private readonly Dictionary<string, (long Private, long WorkingSet)> _peaks = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _thread;
        private string _phase = "start";

        public MemorySampler()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "derive-memory-probe" };
            _thread.Start();
        }

        public void Enter(string phase)
        {
            lock (_gate)
            {
                Sample();
                _phase = phase;
                Sample();
            }
        }

        public (long Private, long WorkingSet) PeakOf(string phase)
        {
            lock (_gate)
            {
                return _peaks[phase];
            }
        }

        public static (long Private, long WorkingSet, long PeakPrivate, long PeakWorkingSet) Read()
        {
            var counters = new ProcessMemoryCountersEx { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>() };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size))
            {
                throw new InvalidOperationException($"GetProcessMemoryInfo failed: {Marshal.GetLastPInvokeError()}");
            }

            return ((long)counters.PrivateUsage, (long)counters.WorkingSetSize, (long)counters.PeakPagefileUsage, (long)counters.PeakWorkingSetSize);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _thread.Join();
            _stop.Dispose();
        }

        private void Run()
        {
            while (!_stop.IsCancellationRequested)
            {
                lock (_gate)
                {
                    Sample();
                }

                Thread.Sleep(10);
            }
        }

        private void Sample()
        {
            var (privateBytes, workingSet, _, _) = Read();
            var (peakPrivate, peakWorkingSet) = _peaks.GetValueOrDefault(_phase);
            _peaks[_phase] = (Math.Max(peakPrivate, privateBytes), Math.Max(peakWorkingSet, workingSet));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCountersEx
        {
            public uint Size;
            public uint PageFaultCount;
            public nuint PeakWorkingSetSize;
            public nuint WorkingSetSize;
            public nuint QuotaPeakPagedPoolUsage;
            public nuint QuotaPagedPoolUsage;
            public nuint QuotaPeakNonPagedPoolUsage;
            public nuint QuotaNonPagedPoolUsage;
            public nuint PagefileUsage;
            public nuint PeakPagefileUsage;
            public nuint PrivateUsage;
        }

        [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(nint process, ref ProcessMemoryCountersEx counters, uint size);

        [DllImport("kernel32.dll")]
        private static extern nint GetCurrentProcess();
    }
}

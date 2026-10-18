using System.Diagnostics;
using Lex.V3.Ingest;

/// <summary>
/// The sweep a build command runs before it builds (the review of #986): a derive the runner's guard stops leaves its mount
/// build's staging directory in the temporary folder, up to about 4 GB at the population, on the disk the next derive needs.
/// The stale ones (<see cref="V3FirstMountBuild.SweepStaleStaging"/>: only <c>lex-v3-mount-build-*</c> directories, last
/// written before this process started, that no live build holds) are removed, and the command says so.
/// </summary>
internal static class StaleStagingSweep
{
    internal static void Run()
    {
        DateTimeOffset started;
        using (var self = Process.GetCurrentProcess())
        {
            started = new DateTimeOffset(self.StartTime);
        }

        var root = Path.GetTempPath();
        var swept = V3FirstMountBuild.SweepStaleStaging(root, started);
        if (swept.Count != 0)
        {
            Console.WriteLine(
                $"swept {swept.Count} stale mount-build staging director{(swept.Count == 1 ? "y" : "ies")} under {root} "
                + $"({swept.Sum(static entry => entry.ByteLength)} bytes): {string.Join(", ", swept.Select(static entry => Path.GetFileName(entry.Path)))}");
        }
    }
}

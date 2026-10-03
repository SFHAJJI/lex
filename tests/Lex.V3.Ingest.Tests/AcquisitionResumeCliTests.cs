using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class V3FirstMountBuildTests
{
    /// <summary>
    /// <c>build --resume-from</c> as a process: a journal that was reordered, cut, edited or written for another build or
    /// invocation, or that names an object custody does not hold, exits 2 with no request attempted (every proxy points at
    /// a local trap). The intact journal and one with a torn last line pass verification ("verified through seq") and are
    /// then refused by the EU replay, whose stand-in units are not real checkpoints, still before any request; a journal
    /// naming no unit verifies and the run proceeds to the EU acquisition, which refuses its seed with nothing spent.
    /// </summary>
    [TestMethod]
    [DataRow("intact", 2, true)]
    [DataRow("torn_last_line", 2, true)]
    [DataRow("header_only", 3, true)]
    [DataRow("reordered", 2, false)]
    [DataRow("gap", 2, false)]
    [DataRow("edited_byte", 2, false)]
    [DataRow("torn_middle_line", 2, false)]
    [DataRow("foreign_source", 2, false)]
    [DataRow("foreign_arguments", 2, false)]
    [DataRow("missing_object", 2, false)]
    public async Task AResumeJournalIsVerifiedBeforeAnyRequest(string journalCase, int expectedExit, bool verifies)
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-resume-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            // The renderer sources the interrupted run held, in the custody the resume reads.
            var custody = Path.Combine(root, "custody");
            var store = new FileSystemCustodyStore(custody);
            var europe = await EuRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
            var luxembourg = await LuxembourgRendererSources.FromCheckoutAsync(store, CheckoutRoot(), CancellationToken.None);
            var europeRenderers = new[] { europe.Census, europe.ObjectFacts, europe.Witness, europe.DocumentFetch,
                europe.FormexManifestation, europe.LegalNotice };
            var renderers = europeRenderers.Select((renderer, index) => new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[index], renderer.Reference))
                .Append(new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[0], luxembourg.Query.Reference))
                .Append(new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[1], luxembourg.DocumentFetch.Reference))
                .ToArray();
            var act = LuxembourgFirstMountAcquisitionTests.ActRange;
            var arguments = AcquisitionJournal.DigestArguments(["NOTASEED"], act, null, "raw", null);
            var header = new AcquisitionJournalHeader(
                journalCase == "foreign_source" ? new string('0', 40) : AcquisitionJournal.CurrentSource(),
                journalCase == "foreign_arguments" ? AcquisitionJournal.DigestArguments(["32016R0679"], act, null, "raw", null) : arguments,
                10, renderers, null);

            // Three stand-in units naming held objects (a missing one for missing_object), through the production writer.
            var named = journalCase == "missing_object" ? new string('f', 64) : renderers[0].Reference.Sha256;
            byte[] written;
            using (var stream = new MemoryStream())
            {
                await using (var journal = new AcquisitionJournal(stream, header, WireRequestBudget.OfWireRequests(10), TimeProvider.System))
                {
                    if (journalCase != "header_only")
                    {
                        await journal.AppendAsync(AcquisitionJournal.EuropeAdapterPhase, "query",
                            AcquisitionJournal.Payload(new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", named)), [named]);
                        await journal.AppendAsync(AcquisitionJournal.EuropeFormexEnumerationPhase, renderers[1].Reference.Sha256,
                            AcquisitionJournal.Payload(new { stand_in = 1 }), [renderers[1].Reference.Sha256]);
                        await journal.AppendAsync(AcquisitionJournal.EuropeFormexEnumerationPhase, renderers[2].Reference.Sha256,
                            AcquisitionJournal.Payload(new { stand_in = 2 }), [renderers[2].Reference.Sha256]);
                    }
                }

                written = stream.ToArray();
            }

            var lines = Encoding.UTF8.GetString(written).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var bytes = journalCase switch
            {
                "reordered" => JoinLines(lines[0], lines[2], lines[1], lines[3]),
                "gap" => JoinLines(lines[0], lines[1], lines[3]),
                "edited_byte" => JoinLines(lines[0], lines[1].Replace("\"query\"", "\"querz\"", StringComparison.Ordinal), lines[2], lines[3]),
                "torn_middle_line" => JoinLines(lines[0], lines[1], lines[2][..(lines[2].Length / 2)], lines[3]),
                "torn_last_line" => written.Concat(Encoding.UTF8.GetBytes(lines[3][..(lines[3].Length / 2)])).ToArray(),
                _ => written,
            };
            var journalPath = Path.Combine(root, "acquisition-progress-interrupted.jsonl");
            await File.WriteAllBytesAsync(journalPath, bytes);

            var attempted = listener.AcceptTcpClientAsync();
            var proxy = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var tool = Path.Combine(CheckoutRoot(), "src", "Lex.V3.Tool", "bin", configuration, "net10.0", "Lex.V3.Tool.dll");
            Assert.IsTrue(File.Exists(tool), tool);
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { tool, "build", "--celex", "NOTASEED",
                "--lu-name", act.Name, "--lu-start", act.StartInclusive, "--lu-end", act.EndExclusive,
                "--custody", custody, "--out", Path.Combine(root, "mount"), "--checkout", CheckoutRoot(),
                "--wire-ceiling", "10", "--resume-from", journalPath })
                start.ArgumentList.Add(argument);
            foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
                start.Environment[name] = proxy;
            start.Environment["NO_PROXY"] = "";
            start.Environment["no_proxy"] = "";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            var output = await stdout;
            var error = await stderr;
            var transcript = $"exit {process.ExitCode}\n--- stdout ---\n{output}\n--- stderr ---\n{error}";

            Assert.AreEqual(expectedExit, process.ExitCode, transcript);
            Assert.AreEqual(verifies, output.Contains("verified through seq", StringComparison.Ordinal), transcript);
            if (expectedExit == 2) StringAssert.Contains(error, "--resume-from refused before any request", transcript);
            else StringAssert.Contains(error, "(spent 0 of 10)", transcript);
            Assert.IsFalse(attempted.IsCompleted, "A resume journal is verified before any request: " + journalCase);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "mount")));
            if (journalCase != "missing_object" && verifies)
                StringAssert.Contains(output, "renderer sources reopened from custody", transcript);
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] JoinLines(params string[] lines) => Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
}

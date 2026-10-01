using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Tests;

public sealed partial class V3FirstMountBuildTests
{
    [TestMethod]
    [DataRow("missing_file", 2)]
    [DataRow("null", 2)]
    [DataRow("missing_custody", 3)]
    public async Task InvalidRetainedEuCliInputCannotFallBackToLiveAcquisition(string failure, int expectedExit)
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-eu-reuse-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var pointer = Path.Combine(root, "eu-input.json");
            if (failure != "missing_file")
                await File.WriteAllTextAsync(pointer, failure == "null" ? "null" : ContractJson.Serialize(
                    new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000089", new string('a', 64))));
            var attempted = listener.AcceptTcpClientAsync();
            var proxy = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var tool = Path.Combine(CheckoutRoot(), "src", "Lex.V3.Tool", "bin", configuration, "net10.0", "Lex.V3.Tool.dll");
            Assert.IsTrue(File.Exists(tool), tool);
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true };
            var act = LuxembourgFirstMountAcquisitionTests.ActRange;
            foreach (var argument in new[] { tool, "build", "--celex", EuAxiomWiringHarness.Seed(null).Celex,
                "--lu-name", act.Name, "--lu-start", act.StartInclusive, "--lu-end", act.EndExclusive,
                "--custody", Path.Combine(root, "custody"), "--out", Path.Combine(root, "mount"),
                "--checkout", CheckoutRoot(), "--wire-ceiling", "10", "--eu-checkpoint", pointer })
                start.ArgumentList.Add(argument);
            foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
                start.Environment[name] = proxy;
            start.Environment["NO_PROXY"] = "";
            start.Environment["no_proxy"] = "";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            Assert.AreEqual(expectedExit, process.ExitCode, await stderr + "\n" + await stdout);
            Assert.IsFalse(attempted.IsCompleted, "An invalid checkpoint must not fall back to publisher acquisition.");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "mount")));
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }
}

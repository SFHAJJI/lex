using System.Diagnostics;

namespace Lex.V3.Tests.Tool;

/// <summary>
/// The build tool as a process: its documented exit codes, and that a usage error is answered
/// before any work. No test here reaches a publisher: the refused case names a CELEX that is not
/// an Appendix A seed, which the EU acquisition refuses before its first request, and every other
/// case fails argument checking. The tool is built by the whole-solution build the S1-A07 sweep
/// already relies on.
/// </summary>
[TestClass]
public sealed class LexV3ToolProgramTests
{
    private const string ValidStart = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo";
    private const string ValidEnd = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jp";

    [TestMethod]
    public void ARefusedAcquisitionExitsThreeWithoutACrash()
    {
        var run = Run("build", "--celex", "NOTASEED", "--lu-name", "act", "--lu-start", ValidStart, "--lu-end", ValidEnd,
            "--wire-ceiling", "5");

        Assert.AreEqual(3, run.ExitCode, run.Transcript);
        StringAssert.Contains(run.StandardError, "refused: europe:", run.Transcript);
        StringAssert.Contains(run.StandardError, "(spent 0 of 5)", run.Transcript);
        StringAssert.Contains(run.StandardOutput, "renderer sources held", run.Transcript);
        Assert.DoesNotContain("Unhandled exception", run.StandardError, run.Transcript);
    }

    [TestMethod]
    [DataRow("a name with spaces", "--lu-name", "a b", DisplayName = "an act name outside the key alphabet")]
    [DataRow("a reversed range", "--lu-start", ValidEnd, DisplayName = "a reversed act range")]
    [DataRow("an equal range", "--lu-end", ValidStart, DisplayName = "an empty act range")]
    [DataRow("a ceiling of one", "--wire-ceiling", "1", DisplayName = "a wire ceiling under two")]
    [DataRow("a missing checkout", "--checkout", "C:/lex-v3/no-such-checkout", DisplayName = "a checkout that is not a directory")]
    public void ABadArgumentExitsTwoBeforeAnyWork(string scenario, string option, string value)
    {
        var arguments = new List<string>
        {
            "build", "--celex", "NOTASEED", "--lu-name", "act", "--lu-start", ValidStart, "--lu-end", ValidEnd,
            "--wire-ceiling", "5",
        };
        var index = arguments.IndexOf(option);
        if (index >= 0)
        {
            arguments[index + 1] = value;
        }
        else
        {
            arguments.Add(option);
            arguments.Add(value);
        }

        var run = Run(arguments.ToArray(), option == "--checkout" ? value : null);

        Assert.AreEqual(2, run.ExitCode, scenario + ": " + run.Transcript);
        Assert.DoesNotContain("renderer sources held", run.StandardOutput, run.Transcript);
        Assert.DoesNotContain("Unhandled exception", run.StandardError, run.Transcript);
    }

    [TestMethod]
    public void TheWholeLuxembourgPopulationSelectionReachesAcquisitionWithoutSpendingOnAnInvalidEuSeed()
    {
        var run = Run("build", "--celex", "NOTASEED", "--lu-population", "all", "--wire-ceiling", "5");
        Assert.AreEqual(3, run.ExitCode, run.Transcript);
        StringAssert.Contains(run.StandardError, "(spent 0 of 5)", run.Transcript);
    }

    [TestMethod]
    [DataRow("other", false)]
    [DataRow("all", true)]
    public void AnInvalidOrMixedPopulationSelectionIsRejectedBeforeTraffic(string population, bool mixed)
    {
        var arguments = new List<string> { "build", "--celex", "NOTASEED", "--lu-population", population, "--wire-ceiling", "5" };
        if (mixed) arguments.AddRange(["--lu-name", "act"]);
        var run = Run(arguments.ToArray());
        Assert.AreEqual(2, run.ExitCode, run.Transcript);
        Assert.DoesNotContain("renderer sources held", run.StandardOutput, run.Transcript);
    }

    [TestMethod]
    public void AnUnknownARepeatedOrAMissingOptionExitsTwo()
    {
        var unknown = Run("build", "--celex", "NOTASEED", "--lu-name", "act", "--lu-start", ValidStart, "--lu-end", ValidEnd,
            "--wire-ceiling", "5", "--verbose", "yes");
        Assert.AreEqual(2, unknown.ExitCode, unknown.Transcript);
        StringAssert.Contains(unknown.StandardError, "Unknown option: --verbose", unknown.Transcript);

        var repeated = Run("build", "--celex", "NOTASEED", "--celex", "NOTASEED", "--lu-name", "act", "--lu-start", ValidStart,
            "--lu-end", ValidEnd, "--wire-ceiling", "5");
        Assert.AreEqual(2, repeated.ExitCode, repeated.Transcript);
        StringAssert.Contains(repeated.StandardError, "Repeated option: --celex", repeated.Transcript);

        var missing = Run("build", "--celex", "NOTASEED");
        Assert.AreEqual(2, missing.ExitCode, missing.Transcript);
        StringAssert.Contains(missing.StandardError, "Missing: --lu-name", missing.Transcript);

        var noVerb = Run();
        Assert.AreEqual(2, noVerb.ExitCode, noVerb.Transcript);
    }

    private static ToolRun Run(params string[] arguments) => Run(arguments, null);

    /// <summary>
    /// Runs the built tool with <paramref name="arguments"/>, adding a scratch custody root, a
    /// scratch output directory and this checkout unless the arguments name them already.
    /// </summary>
    private static ToolRun Run(string[] arguments, string? checkoutOverride)
    {
        var root = FindRepositoryRoot();
        var target = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = target.Parent?.Name
            ?? throw new InvalidOperationException("Cannot determine the test build configuration.");
        var tool = Path.Combine(root, "src", "Lex.V3.Tool", "bin", configuration, target.Name, "Lex.V3.Tool.dll");
        Assert.IsTrue(File.Exists(tool), $"the complete solution must build Lex.V3.Tool before this test: {tool}");

        var scratch = Path.Combine(Path.GetTempPath(), "lex-v3-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = scratch,
            };
            start.ArgumentList.Add(tool);
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            if (arguments.Length != 0)
            {
                if (!arguments.Contains("--custody"))
                {
                    start.ArgumentList.Add("--custody");
                    start.ArgumentList.Add(Path.Combine(scratch, "custody"));
                }

                if (!arguments.Contains("--out"))
                {
                    start.ArgumentList.Add("--out");
                    start.ArgumentList.Add(Path.Combine(scratch, "v3-corpus"));
                }

                if (checkoutOverride is null && !arguments.Contains("--checkout"))
                {
                    start.ArgumentList.Add("--checkout");
                    start.ArgumentList.Add(root);
                }
            }

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("dotnet did not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            Assert.IsTrue(process.WaitForExit(TimeSpan.FromMinutes(2)), "the tool did not exit within two minutes.");
            return new ToolRun(process.ExitCode, standardOutput.Result, standardError.Result);
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
                // A scratch directory the process still held is left to the temp cleaner.
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lex.V3.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cannot locate the V3 repository root.");
    }

    private sealed record ToolRun(int ExitCode, string StandardOutput, string StandardError)
    {
        public string Transcript =>
            $"exit {ExitCode}\n--- stdout ---\n{StandardOutput}\n--- stderr ---\n{StandardError}";
    }
}

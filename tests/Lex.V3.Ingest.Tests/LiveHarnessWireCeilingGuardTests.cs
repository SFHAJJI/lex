using System.Text;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The offline helper's ceiling has no place in a live path — enforced over every harness at once,
/// not one file at a time.
/// </summary>
/// <remarks>
/// <para>
/// THE RULE ALREADY EXISTED; WHAT DID NOT EXIST WAS ANYTHING THAT APPLIED IT TO A NEW FILE.
/// <c>EuProcedureEventLiveGuardTests.OneBudgetSpansDiscoveryAndAcceptance</c> and two guards in
/// <c>LuxembourgDraftBudgetEvidenceTests</c> each say, in the same sentence, that
/// <c>TestWireBudget()</c>'s 100,000-request ceiling has no place in a live path. Each of those
/// guards reads one named harness file. A slice that gave a fourth and fifth live harness the
/// offline ceiling therefore broke the rule three guards already stated and failed nothing, because
/// no guard had been written for those two files yet. That is the defect this file exists to close:
/// the property is a property of every live harness, so it is checked over every live harness.
/// </para>
/// <para>
/// GATED IS THE DISCRIMINATOR, AND IT IS THE HONEST ONE. A live harness is exactly a test method
/// that reads an enable variable and declines itself when it is unset — that gate is what separates
/// a method that reaches a publisher from a method that drives a scripted handler and sends nothing.
/// Two files here hold one of each (<c>EuCaseLawLiveAcceptance</c> and
/// <c>LuxembourgDraftGraphLiveAcceptance</c> each pair a gated live method with an ungated scripted
/// one), so a file-level check would either miss the live half or condemn the offline half. The
/// scope is the method.
/// </para>
/// <para>
/// AND THE CENSUS IS FLOORED, because an empty list and a parser that found nothing are the same
/// result. If the extraction below stops recognising method bodies, every assertion over them
/// passes vacuously and this file reports success while checking nothing. The floor makes that
/// failure loud instead.
/// </para>
/// </remarks>
[TestClass]
public sealed class LiveHarnessWireCeilingGuardTests
{
    /// <summary>
    /// The number of gated methods below which this guard is presumed broken rather than satisfied.
    /// </summary>
    /// <remarks>
    /// Deliberately well under the count at the time of writing (20 across 17 files). This is a
    /// floor against a silent parse failure, not a pin on the population: adding or retiring a live
    /// harness is ordinary work and must not fail here.
    /// </remarks>
    private const int GatedMethodFloor = 12;

    private const string OfflineHelper = "TestWireBudget(";

    /// <summary>No environment-gated test method carries the offline helper's ceiling.</summary>
    [TestMethod]
    public void NoLiveHarnessBorrowsTheOfflineCeiling()
    {
        var gated = new List<(string File, string Method)>();
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(HarnessDirectory(), "*.cs"))
        {
            var code = WithoutCommentsOrLiterals(File.ReadAllText(path));
            foreach (var (name, body) in TestMethodBodies(code))
            {
                if (!body.Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal))
                {
                    continue;
                }

                gated.Add((Path.GetFileName(path), name));
                if (body.Contains(OfflineHelper, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(path)}.{name}");
                }
            }
        }

        Assert.IsGreaterThanOrEqualTo(
            GatedMethodFloor,
            gated.Count,
            "the extraction found almost no environment-gated test methods, so every assertion "
            + "below it would pass without checking anything.");

        Assert.AreEqual(
            0,
            offenders.Count,
            "the offline helper's 100,000-request ceiling has no place in a live path: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The two harnesses this guard was written for state their ceiling rather than borrowing one.
    /// </summary>
    /// <remarks>
    /// The census above proves no live method holds the offline ceiling; it does not prove a live
    /// method holds any ceiling at all, and deleting the argument entirely would satisfy it. These
    /// two are named because a run of theirs is what would reach a publisher unbounded.
    /// </remarks>
    [TestMethod]
    public void TheTwoRepairedHarnessesCarryOneNamedUndispositionedCeiling()
    {
        foreach (var file in new[]
                 {
                     "EuCaseLawLiveAcceptance.cs",
                     "EuTranspositionBridgeLivePopulation.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(HarnessDirectory(), file));
            var code = WithoutCommentsOrLiterals(source);

            StringAssert.Contains(
                code,
                "private static readonly int? SharedWireCeiling = null;",
                $"{file} names its ceiling and leaves the number to the owner.");
            StringAssert.Contains(
                code,
                "SharedWireCeiling is not { } ceiling",
                $"{file} refuses to start rather than defaulting to something.");
            Assert.AreEqual(
                1,
                CountOf(code, "WireRequestBudget.OfWireRequests(ceiling)"),
                $"{file} builds one budget for the whole operation; a second instance would bound "
                + "each run and leave the operation unbounded.");
        }
    }

    /// <summary>
    /// Every <c>[TestMethod]</c> in <paramref name="code"/> as (name, body), bodies brace-matched.
    /// </summary>
    private static IEnumerable<(string Name, string Body)> TestMethodBodies(string code)
    {
        const string Marker = "[TestMethod]";
        var cursor = code.IndexOf(Marker, StringComparison.Ordinal);
        while (cursor >= 0)
        {
            var open = code.IndexOf('{', cursor);
            var arrow = code.IndexOf("=>", cursor, StringComparison.Ordinal);
            var name = MethodNameAfter(code, cursor);

            // An expression-bodied test method has no block to match; its body runs to the
            // statement's end. Treating it as a block would swallow every method after it.
            if (arrow >= 0 && (open < 0 || arrow < open))
            {
                var end = code.IndexOf(';', arrow);
                if (end < 0)
                {
                    yield break;
                }

                yield return (name, code[arrow..end]);
                cursor = code.IndexOf(Marker, end, StringComparison.Ordinal);
                continue;
            }

            if (open < 0)
            {
                yield break;
            }

            var depth = 0;
            var index = open;
            for (; index < code.Length; index++)
            {
                if (code[index] == '{')
                {
                    depth++;
                }
                else if (code[index] == '}' && --depth == 0)
                {
                    break;
                }
            }

            if (index >= code.Length)
            {
                yield break;
            }

            yield return (name, code[open..index]);
            cursor = code.IndexOf(Marker, index, StringComparison.Ordinal);
        }
    }

    /// <summary>The identifier immediately before the signature's first parenthesis.</summary>
    private static string MethodNameAfter(string code, int fromIndex)
    {
        var paren = code.IndexOf('(', fromIndex);
        if (paren < 0)
        {
            return "<unnamed>";
        }

        var end = paren;
        var start = paren;
        while (start > fromIndex && (char.IsLetterOrDigit(code[start - 1]) || code[start - 1] == '_'))
        {
            start--;
        }

        return start < end ? code[start..end] : "<unnamed>";
    }

    /// <summary>
    /// Comments and the contents of string and character literals removed.
    /// </summary>
    /// <remarks>
    /// Literals are blanked, not deleted, so a guard file that asserts on the very string this
    /// census forbids — three of them do — is not itself reported as an offender. Deleting them
    /// instead would also work; blanking keeps braces inside literals from ever reaching the
    /// brace matcher above.
    /// </remarks>
    private static string WithoutCommentsOrLiterals(string source)
    {
        var builder = new StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            var c = source[index];

            if (c == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (c == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 ? source.Length : close + 2;
                continue;
            }

            if (c == '"' && index + 2 < source.Length && source[index + 1] == '"' && source[index + 2] == '"')
            {
                var fence = 0;
                while (index + fence < source.Length && source[index + fence] == '"')
                {
                    fence++;
                }

                var terminator = new string('"', fence);
                var close = source.IndexOf(terminator, index + fence, StringComparison.Ordinal);
                index = close < 0 ? source.Length : close + fence;
                continue;
            }

            if (c == '@' && index + 1 < source.Length && source[index + 1] == '"')
            {
                index += 2;
                while (index < source.Length)
                {
                    if (source[index] == '"')
                    {
                        if (index + 1 < source.Length && source[index + 1] == '"')
                        {
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    index++;
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                var quote = c;
                index++;
                while (index < source.Length && source[index] != quote)
                {
                    index += source[index] == '\\' ? 2 : 1;
                }

                index++;
                continue;
            }

            builder.Append(c);
            index++;
        }

        return builder.ToString();
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string HarnessDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Checkout root not found.");
        return Path.Combine(root, "tests", "Lex.V3.Ingest.Tests");
    }
}

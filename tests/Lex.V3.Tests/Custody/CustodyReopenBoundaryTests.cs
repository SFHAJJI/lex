using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Custody;

/// <summary>
/// Every custody reopen in production code goes through the CHECKED read, and this holds the
/// boundary rather than trusting that it stayed true.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE CHECKED READ BUYS, and therefore what bypassing it costs.
/// <c>CustodyRestore.ReadByDigestCheckedAsync</c> asks the store for bytes by digest, then proves
/// the returned bytes carry that digest: it compares the length against the durable reference,
/// FREEZES the bytes into a private copy so a store handing back a mutable provider buffer cannot
/// change them after the check, hashes that exact copy, and returns only it. A direct
/// <c>ICustodyStore.ReadByDigestAsync</c> call skips all of that and trusts the store's word.
/// The whole product claim is that an answer can be CHECKED rather than trusted, so a production
/// path that trusts the store is that claim quietly not holding for those bytes.
/// </para>
/// <para>
/// THE MEASUREMENT THIS MAKES PERMANENT. A sweep of every reopen site found 58 calls to the
/// checked read and ZERO direct <c>ReadByDigestAsync</c> callers anywhere under <c>src</c>; all 28
/// direct callers were in test code, where a double deliberately returns wrong bytes so a guard
/// can be shown to bite. That was a good result, and a good result measured once is a fact about
/// the day it was measured. This test is the difference between having swept and staying swept.
/// </para>
/// <para>
/// WHY A SOURCE SCAN RATHER THAN AN IL SCAN. An IL scan over call sites would be the stronger
/// instrument and this repository has no metadata reader to build it on. Rather than introduce
/// one and risk a guard whose own correctness needs auditing, this reads the tracked C# under
/// <c>src</c>, which is exactly the artifact a reviewer would read. Its weakness is stated rather
/// than hidden: it sees the call spelled <c>.ReadByDigestAsync(</c> and would miss a reopen routed
/// through a delegate, an interface variable named differently, or reflection. That is why the
/// second assertion exists.
/// </para>
/// <para>
/// THE SECOND ASSERTION IS NOT DECORATION. A boundary test whose scan silently matches no files
/// passes forever and reports nothing. This asserts the scan really saw the source tree AND that
/// production code really does reopen by digest, so the boundary being clean means the rule holds
/// rather than that the rule has nothing to hold.
/// </para>
/// </remarks>
[TestClass]
public sealed class CustodyReopenBoundaryTests
{
    /// <summary>
    /// The single file allowed to call the store's unchecked read: the checked read is implemented
    /// in terms of it, which is the whole point of having one place that does the verifying.
    /// </summary>
    private const string TheOnlyPermittedCaller = "CustodyStore.cs";

    [TestMethod]
    public void NoProductionCodeReopensCustodyBytesWithoutCheckingThem()
    {
        var offenders = new List<string>();

        foreach (var (relative, lines) in ProductionSources())
        {
            if (Path.GetFileName(relative) == TheOnlyPermittedCaller)
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (lines[i].Contains(".ReadByDigestAsync(", StringComparison.Ordinal))
                {
                    offenders.Add(relative + ":" + (i + 1) + "  " + code.Trim());
                }
            }
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            "these production sites reopen custody bytes WITHOUT verifying them against their "
                + "durable reference. Route them through CustodyRestore.ReadByDigestCheckedAsync, "
                + "which is the only place allowed to trust the store's word:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void TheScanSeesTheSourceTreeAndProductionCodeReallyDoesReopenByDigest()
    {
        var sources = ProductionSources();

        // If a future move of the source root makes the glob match nothing, the boundary test
        // above would pass on an empty set. This is what stops that being invisible.
        Assert.IsTrue(
            sources.Count >= 100,
            "the scan found " + sources.Count + " production source files, which is too few to be "
                + "this repository's src tree. The boundary assertion is only meaningful over a "
                + "scan that actually reached the code.");

        var checkedCalls = sources.Sum(entry => entry.Lines.Count(
            line => line.Contains("ReadByDigestCheckedAsync(", StringComparison.Ordinal)
                && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.IsTrue(
            checkedCalls >= 20,
            "production code made " + checkedCalls + " checked custody reopens. The boundary above "
                + "reports that nothing bypasses the checked read; that is only worth asserting "
                + "while production code is actually reopening custody bytes at all.");
    }

    private static List<(string Relative, string[] Lines)> ProductionSources()
    {
        var root = RepositoryRoot();
        var src = Path.Combine(root, "src");
        Assert.IsTrue(Directory.Exists(src), "the repository has no src directory at " + src);

        var found = new List<(string, string[])>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            found.Add((relative, File.ReadAllLines(file)));
        }

        return found;
    }

    private static string RepositoryRoot()
    {
        foreach (var startingPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new AssertFailedException("Could not locate the V3 repository root.");
    }
}

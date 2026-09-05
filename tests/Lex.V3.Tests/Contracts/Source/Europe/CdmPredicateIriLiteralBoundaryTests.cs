using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// No production file writes a CDM PREDICATE IRI as a string literal. The typed accessor is the
/// only way to name one.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THIS GENERALISES, and generalising it is the point. D1-05g fixed one hand-copied
/// predicate IRI in the EU adapter: it named <c>resource_legal_type</c> while the switch it fed
/// spoke <c>work_has_resource-type</c>'s vocabulary, so the guard could never match and was dead
/// for as long as it existed. Fixing that one site is worth little. The MECHANISM was that an
/// accessibility boundary pushed one caller off the typed path, and a string literal is checked
/// against nothing, so the two drifted with no compiler and no reader able to see it. A test that
/// catches the NEXT one is worth more than the repair.
/// </para>
/// <para>
/// THE NAMESPACE ITSELF IS PERMITTED AT ITS ONE DEFINITION SITE, and that exemption is a design
/// decision rather than a convenience. The prefix has to be written down somewhere, and a test
/// that fails on the place it is legitimately defined gets its assertion deleted by the next
/// author rather than obeyed. What is forbidden is the namespace WITH A LOCAL NAME after it,
/// which is a predicate rather than a prefix.
/// </para>
/// <para>
/// A SOURCE SCAN, with its weakness stated rather than hidden: it sees the IRI spelled out in one
/// literal and would miss a predicate assembled at runtime from parts. That is the same trade the
/// custody reopen boundary makes, for the same reason, and the vacuity guard below is what stops
/// either of them passing on an empty scan.
/// </para>
/// </remarks>
[TestClass]
public sealed class CdmPredicateIriLiteralBoundaryTests
{
    private const string Namespace = "http://publications.europa.eu/ontology/cdm#";

    /// <summary>
    /// The single file allowed to write the bare namespace: it is where the prefix is defined.
    /// </summary>
    private const string TheOnlyPermittedDefinition = "EuConsolidationDiscovery.cs";

    /// <summary>
    /// The one member allowed to carry a full predicate IRI, because it is EVIDENCE rather than a
    /// reference.
    /// </summary>
    /// <remarks>
    /// <c>PlainLiteralDriftProbeSparql</c> is the VERBATIM TEXT OF A QUERY THAT WAS ACTUALLY SENT,
    /// pinned beside the seed digest of the measurement it belongs to. Composing it through
    /// <c>CdmIri</c> would turn a record of what was sent into text assembled now, which is the
    /// substitution that destroys evidence: the two would agree today and the pin would stop
    /// meaning anything the moment the accessor changed. This scan found it, and finding it is the
    /// test working; the exemption is BY NAME with a reason rather than by a pattern, so the next
    /// literal still has to be argued rather than matched.
    /// </remarks>
    private const string TheOnlyPermittedVerbatimQuery = "PlainLiteralDriftProbeSparql";

    [TestMethod]
    public void NoProductionFileNamesACdmPredicateWithAStringLiteral()
    {
        var offenders = new List<string>();

        foreach (var (relative, lines) in ProductionSources())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var at = line.IndexOf(Namespace, StringComparison.Ordinal);
                if (at < 0)
                {
                    continue;
                }

                // The prefix alone, at its definition site, is what every other file composes on.
                var after = line[(at + Namespace.Length)..];
                var namesAPredicate = after.Length > 0 && after[0] != '"';
                if (!namesAPredicate)
                {
                    if (Path.GetFileName(relative) != TheOnlyPermittedDefinition)
                    {
                        offenders.Add(
                            relative + ":" + (i + 1) + "  the bare CDM namespace belongs only in "
                            + TheOnlyPermittedDefinition);
                    }

                    continue;
                }

                if (line.Contains(TheOnlyPermittedVerbatimQuery, StringComparison.Ordinal)
                    || InsideThePermittedVerbatimQuery(lines, i))
                {
                    continue;
                }

                offenders.Add(relative + ":" + (i + 1) + "  " + line.Trim());
            }
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            "these production sites write a CDM predicate IRI as a string literal, which nothing "
                + "checks against the closed vocabulary. Use "
                + "EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.X):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void TheScanSeesTheSourceTreeAndTheNamespaceIsReallyDefinedInIt()
    {
        var sources = ProductionSources();

        Assert.IsTrue(
            sources.Count >= 100,
            "the scan found " + sources.Count + " production source files, too few to be this "
                + "repository's src tree. The boundary above is only meaningful over a scan that "
                + "reached the code.");

        var definitions = sources.Count(entry =>
            Path.GetFileName(entry.Relative) == TheOnlyPermittedDefinition
            && entry.Lines.Any(line => line.Contains(Namespace, StringComparison.Ordinal)));

        Assert.AreEqual(
            1,
            definitions,
            "the permitted definition site was not found carrying the namespace. Either it moved, "
                + "in which case TheOnlyPermittedDefinition is stale, or the scan is not reading "
                + "what it thinks it is.");

        // An exemption for a member that no longer exists is an exemption quietly widening the
        // rule, so the named member has to still be there.
        Assert.IsTrue(
            sources.Any(entry => entry.Lines.Any(line =>
                line.Contains(TheOnlyPermittedVerbatimQuery, StringComparison.Ordinal))),
            TheOnlyPermittedVerbatimQuery + " is exempted by name but no longer exists in src, so "
                + "the exemption is dead and should be removed rather than left standing.");
    }

    /// <summary>
    /// True when this line is the continuation of the one permitted verbatim query's declaration,
    /// which is written across two lines.
    /// </summary>
    private static bool InsideThePermittedVerbatimQuery(string[] lines, int index) =>
        index > 0
        && lines[index - 1].Contains(TheOnlyPermittedVerbatimQuery, StringComparison.Ordinal);

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

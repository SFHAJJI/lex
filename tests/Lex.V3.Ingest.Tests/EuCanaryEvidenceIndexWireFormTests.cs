using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The enum-valued fields of the canary evidence index carry the WIRE token, never the C# member
/// name.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THIS STANDS AGAINST, which was live rather than hypothetical.
/// <c>wholeRunRefusalCode</c> and <c>completion</c> were both written with <c>ToString()</c>, which
/// returns the C# member name and bypasses <see cref="ContractJson"/> entirely. The index declares
/// a schema, so it is a MACHINE READ document, and a machine reading it must see what a wire
/// consumer sees. It did not: the acceptance run that found this recorded
/// <c>RecordFormNotResolved</c> where the wire says <c>record_form_not_resolved</c>.
/// </para>
/// <para>
/// WHY THE FIX ALONE WAS NOT ENOUGH. Correcting two call sites leaves nothing between the next
/// writer and the same mistake, and the mistake is easy to make: <c>ToString()</c> on an enum
/// compiles, reads naturally, and produces a plausible looking string. It was invisible because
/// these fields are only produced behind LEX_EU_CANARY=1. Everything here runs unconditionally,
/// with no store, no custody root and no publisher traffic.
/// </para>
/// <para>
/// WHY ONE FIELD IS DRIVEN AND THE OTHER IS HELD STRUCTURALLY, stated because the asymmetry is
/// real and should not be hidden behind two similar looking test names.
/// <c>wholeRunRefusalCode</c> is driven END TO END: a synthetic refused result goes through the
/// real <c>BuildEvidenceIndex</c> and the field is read back. <c>completion</c> cannot be reached
/// the same way, because <see cref="EuQueryExecutionResult"/> DERIVES it and only a Delivered
/// result carries one, which needs a watermark plan, a root binding, a witness reconciliation, a
/// durable receipt and a verified record set. Synthesising those would be building a fake run to
/// test a string conversion, and a fixture that elaborate tends to be believed rather than read.
/// So that field is held by reading the construction itself, which is a weaker instrument aimed at
/// exactly the regression that occurred.
/// </para>
/// <para>
/// A NOTE ON WHAT NONE OF THIS COVERS. Members of these vocabularies with no
/// <c>JsonStringEnumMemberName</c> fall back to the member name, so their wire token IS PascalCase.
/// That fallback is the separate defect queued as R4. The end-to-end test therefore drives a member
/// that carries an attribute, which is exactly the set where the index and the wire disagreed.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuCanaryEvidenceIndexWireFormTests
{
    [TestMethod]
    public void TheWholeRunRefusalCodeIsWrittenAsTheWireTokenAndNotTheMemberName()
    {
        var index = EuStageOneAcquisitionCanary.BuildEvidenceIndex(RefusedWith(
            EuQueryExecutionRefusal.ScopeManifestNotRetained));

        var written = index["wholeRunRefusalCode"]!.GetValue<string>();

        Assert.AreEqual(
            WireTokenOf(EuQueryExecutionRefusal.ScopeManifestNotRetained),
            written,
            "the index must carry the same token ContractJson would put on the wire.");
        Assert.IsFalse(
            written.Any(char.IsUpper),
            "wholeRunRefusalCode carries '" + written + "', which contains an uppercase letter. "
                + "That is the C# member name, so the value went through ToString() rather than "
                + "ContractJson and the index disagrees with the wire.");
        Assert.IsTrue(
            written.Contains('_', StringComparison.Ordinal),
            "wholeRunRefusalCode carries '" + written + "', which has no underscore.");
    }

    [TestMethod]
    public void AnAbsentCompletionStaysAbsentRatherThanBecomingAString()
    {
        // A machine reading this document distinguishes a run that had no completion from one that
        // completed under an empty token. A converter that returned "" for null would erase that,
        // and it is the kind of thing a helper acquires later when someone wants a tidier document.
        var index = EuStageOneAcquisitionCanary.BuildEvidenceIndex(RefusedWith(
            EuQueryExecutionRefusal.ScopeManifestNotRetained));

        Assert.IsNull(index["completion"], "a run with no completion must not invent one.");
    }

    [TestMethod]
    public void NeitherEnumValuedIndexFieldIsBuiltWithToString()
    {
        var source = File.ReadAllLines(CanarySourcePath());

        var offenders = source
            .Select(static (line, number) => (Line: line.Trim(), Number: number + 1))
            .Where(static entry =>
                (entry.Line.Contains("[\"wholeRunRefusalCode\"]", StringComparison.Ordinal)
                    || entry.Line.Contains("[\"completion\"]", StringComparison.Ordinal))
                && entry.Line.Contains("ToString()", StringComparison.Ordinal))
            .Select(static entry => entry.Number + ": " + entry.Line)
            .ToArray();

        Assert.AreEqual(
            0,
            offenders.Length,
            "these index fields are built with ToString(), which returns the C# member name and "
                + "bypasses ContractJson. Use the WireToken helper beside them:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));

        // The scan must actually find the two fields, or it would pass on a renamed field or a
        // moved file while reporting nothing at all.
        var built = source.Count(static line =>
            line.Contains("[\"wholeRunRefusalCode\"]", StringComparison.Ordinal)
            || line.Contains("[\"completion\"]", StringComparison.Ordinal));
        Assert.AreEqual(
            2,
            built,
            "the scan found " + built + " of the two enum-valued index fields. It is only "
                + "meaningful while it can see them both.");
    }

    /// <summary>
    /// The index's TOP LEVEL FIELD SET, pinned as a literal list so deleting one goes red.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE HOLE THIS FILLS, found by a probe rather than by reading. A probe deleted
    /// <c>observedObjectCount</c> from the document and BOTH SUITES STAYED GREEN. Every other test
    /// over this index reads fields BY NAME, so each one only holds the field it happens to ask
    /// for, and nothing at all held the SHAPE. A consumer diffing two runs, or a later slice
    /// reading the index as the run's record, would have found a field simply gone with no test
    /// having an opinion about it.
    /// </para>
    /// <para>
    /// WHY THE WHOLE SET AND NOT THE FIELDS SOMEBODY REMEMBERED. Asserting presence field by field
    /// is the same shape as the census this repository already learned from: it can only ever
    /// check the names in the list, so a field REMOVED from the list disappears from the document
    /// and from the assertion together. Comparing the document's own key set against a literal
    /// catches a deletion, an addition and a rename, and an addition failing is correct: a new
    /// field in a schema-declaring document is a change a reader should have to declare.
    /// </para>
    /// <para>
    /// The nested sections are pinned as names only. What is inside <c>families</c> is held by the
    /// wire-form and gap tests beside this one, and duplicating that here would make one change
    /// redden three tests for one reason.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheIndexCarriesExactlyItsDeclaredFieldSet()
    {
        var index = EuStageOneAcquisitionCanary.BuildEvidenceIndex(RefusedWith(
            EuQueryExecutionRefusal.ScopeManifestNotRetained));

        // Joined rather than compared element-wise so a diff names the field that moved.
        Assert.AreEqual(
            string.Join("\n", new[]
            {
                "completion",
                "corpusRecordSet",
                "custodyClassSegment",
                "documentBodies",
                "families",
                "observedExpressionCount",
                "observedObjectCount",
                "rolesThisIndexCannotYetCarry",
                "runGitSha",
                "runTreeClean",
                "runTreeDirtyPaths",
                "schema",
                "scopeManifest",
                "wholeRunRefusalCode",
                "wholeRunRefusalDetail",
            }),
            string.Join("\n", index.Select(static pair => pair.Key).OrderBy(
                static key => key, StringComparer.Ordinal)),
            "this index declares a schema, so its field set is a contract with whoever reads it. "
                + "A field deleted here is a reader silently no longer told something; a field "
                + "added is a change to declare, not to discover.");
    }

    private static string WireTokenOf<T>(T value) where T : struct, Enum =>
        JsonNode.Parse(ContractJson.Serialize(value))!.GetValue<string>();

    private static EuQueryExecutionResult RefusedWith(EuQueryExecutionRefusal code) =>
        EuQueryExecutionResult.Refused(
            Topology(),
            [],
            new EuQueryExecutionRefusalDetail(code, "a synthetic refusal for the wire form test."));

    private static SourceProfileTopology Topology() =>
        new(SourceCoreSchemaIds.SourceProfileTopology,
            new SourceArtifactRef(
                "urn:uuid:1c9f6f4a-2f4e-4a4c-9f2e-6b1d8a7c3e50",
                "0000000000000000000000000000000000000000000000000000000000000000"),
            new SourceRegistryMemberRef(
                new SourceArtifactRef(
                    "urn:uuid:2d8e5e3b-3a5f-4b5d-8e3f-7c2e9b8d4f61",
                    "1111111111111111111111111111111111111111111111111111111111111111"),
                "single_publisher_store"));

    private static string CanarySourcePath()
    {
        foreach (var startingPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
                {
                    var path = Path.Combine(
                        directory.FullName,
                        "tests",
                        "Lex.V3.Ingest.Tests",
                        "EuStageOneAcquisitionCanary.cs");
                    Assert.IsTrue(File.Exists(path), "the canary source is not at " + path);
                    return path;
                }
            }
        }

        throw new AssertFailedException("Could not locate the V3 repository root.");
    }
}

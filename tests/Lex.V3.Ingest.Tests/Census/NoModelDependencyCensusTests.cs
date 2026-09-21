using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// Every assembly the deployed Lex assemblies reference, checked against the inference and
/// embedding libraries a model would arrive as. 0 of them when this was written.
/// </summary>
/// <remarks>
/// <para>
/// S4-A12 says Lex does not manufacture consolidation, model-derived legal identity or
/// model-authored authoritative entity extraction. The Stage 4 reconciliation classified it
/// <c>implemented and accepted</c> (issue #348, comment 5756024733), and the evidence was that
/// <b>no model of any kind exists in the product</b>: a clause about what a model must not do is met,
/// at that head, by there being no model. That is the strongest possible evidence and the most
/// fragile: <b>one package reference breaks it, and nothing would have said so.</b>
/// </para>
/// <para>
/// So this is a proxy and it is worth being exact about what kind. It does not test the three
/// prohibitions; those are about behaviour and each would need its own control. It tests the
/// precondition all three share, which is that a model is reachable at all. A day when this fails is
/// not a day the clause is broken — it is a day the clause stops being free and somebody has to
/// argue it on behaviour instead.
/// </para>
/// <para>
/// Why library names rather than words. An earlier reading of this evidence scanned the source for
/// "embedding" and matched a comment about hashing, and for "llm" and matched nothing at all: concept
/// words are in prose, and prose is not a dependency. The names below are the packages an inference
/// or embedding capability actually arrives as, matched case-insensitively on the referenced
/// assembly's simple name, so a reference is caught whether it was added deliberately or pulled in
/// beside something else.
/// </para>
/// <para>
/// Why it can fail. A pin whose green result nobody has seen turn red is a pin nobody should trust,
/// and this one guards a clause by asserting an emptiness, which is the easiest kind of test to
/// write inertly. <see cref="TheRuleCatchesAModelDependencyWhenThereIsOneToCatch"/> puts the
/// forbidden names through the same predicate the sweep uses and requires each to be caught, so the
/// rule is exercised on this run and not only on the run where something breaks.
/// </para>
/// </remarks>
[TestClass]
public sealed class NoModelDependencyCensusTests
{
    /// <summary>
    /// The simple names an inference or embedding capability arrives under. A match is a substring,
    /// case-insensitive, because these ship as families (<c>Microsoft.ML.OnnxRuntime</c>,
    /// <c>Azure.AI.OpenAI</c>) and the family is what matters rather than the leaf.
    /// </summary>
    private static readonly string[] ModelLibraries =
    [
        "OpenAI",
        "Anthropic",
        "Azure.AI",
        "Microsoft.ML",
        "SemanticKernel",
        "OnnxRuntime",
        "TensorFlow",
        "TorchSharp",
        "HuggingFace",
        "LLamaSharp",
        "Ollama",
        "Mistral",
        "Cohere",
    ];

    private static bool IsModelDependency(string assemblyName) =>
        ModelLibraries.Any(library => assemblyName.Contains(library, StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public void NoDeployedLexAssemblyReferencesAModelLibrary()
    {
        // `LexAssembliesBeside` returns the simple names of the Lex assemblies deployed beside these
        // tests, read off the deployment directory rather than off a list, so an assembly added or
        // dropped changes what this sweeps without anyone maintaining it. Each is loaded by name to
        // read its own references, which is where a model would appear whether it was added to a
        // project file or pulled in beside something else.
        var swept = ClosedSurfaceCensus.LexAssembliesBeside(typeof(NoModelDependencyCensusTests).Assembly);
        Assert.IsGreaterThan(0, swept.Count, "No Lex assembly was found beside these tests, so this swept nothing.");

        var offending = new List<string>();
        foreach (var name in swept)
        {
            var assembly = Assembly.Load(name);
            foreach (var referenced in assembly.GetReferencedAssemblies())
            {
                if (IsModelDependency(referenced.Name ?? string.Empty))
                {
                    offending.Add($"{name} -> {referenced.Name}");
                }
            }
        }

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            offending.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            "A Lex assembly references a model library. S4-A12 (no manufactured law) was classified "
                + "accepted on the evidence that no model exists in the product, which is why the clause "
                + "cost nothing to hold. It now costs something: decide what the model is for, and if it "
                + "is anywhere near consolidation, legal identity or authoritative entity extraction, "
                + "S4-A12 has to be argued on behaviour rather than on absence. Then update this pin and "
                + "say on issue #348 which of the three prohibitions is now defended by what.");
    }

    [TestMethod]
    public void TheRuleCatchesAModelDependencyWhenThereIsOneToCatch()
    {
        // The sweep above passes by finding nothing, so its green result says as much about the rule
        // as about the product. Each forbidden family is put through the same predicate here, in the
        // shape it would really arrive in, so the emptiness above is an emptiness that was looked for.
        foreach (var library in ModelLibraries)
        {
            Assert.IsTrue(
                IsModelDependency($"{library}.Core"),
                $"The rule does not catch {library}, so the sweep would not have caught it either.");
        }

        Assert.IsTrue(IsModelDependency("Azure.AI.OpenAI"), "A real package name is not caught.");
        Assert.IsTrue(IsModelDependency("microsoft.ml.onnxruntime"), "The match is not case-insensitive.");

        // And it does not catch what the product legitimately holds, so a green sweep is not a rule
        // that says yes to nothing.
        foreach (var kept in new[] { "Azure.Identity", "Azure.Storage.Blobs", "JsonSchema.Net", "Microsoft.Data.Sqlite", "PdfPig", "System.Text.Json" })
        {
            Assert.IsFalse(
                IsModelDependency(kept),
                $"{kept} is a dependency this product holds and the rule rejects it.");
        }
    }
}

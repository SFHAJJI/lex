using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// Every reference of every production assembly, and every package manifest, checked against the
/// families an inference or embedding capability arrives as. 0 of them when this was written.
/// </summary>
/// <remarks>
/// <para>
/// S4-A12 says Lex does not manufacture consolidation, model-derived legal identity or
/// model-authored authoritative entity extraction. The Stage 4 reconciliation classified it
/// <c>implemented and accepted</c> (issue #348, comment 5756024733) on the evidence that <b>no model
/// of any kind exists in the product</b>: a clause about what a model must not do is met, at that
/// head, by there being no model. That is the strongest available evidence and the most fragile.
/// </para>
/// <para>
/// This is a proxy and it is worth being exact about which. It does not test the three prohibitions;
/// those are about behaviour and each needs its own control. It tests the precondition all three
/// share, which is that a model is reachable at all. A day when this fails is not a day the clause
/// is broken — it is the day the clause stops being free and has to be argued on behaviour instead.
/// </para>
/// <para>
/// <b>Why it looks at two things.</b> A compiled assembly lists the references its code actually
/// binds, so it catches a model that is used and misses a package that is referenced and not yet
/// called. The manifests catch it the moment it is added, and they are the only place the web
/// surface appears at all. The first version of this file swept compiled references alone, and its
/// own remarks claimed "one package reference breaks it", which was not true until something called
/// it.
/// </para>
/// <para>
/// <b>Why library names rather than words.</b> An earlier reading of this evidence scanned source
/// for "embedding" and matched a comment about hashing, and for "llm" and matched nothing: concept
/// words live in prose, and prose is not a dependency. The families below are what a capability
/// actually ships as, matched case-insensitively as substrings because they arrive as families
/// (<c>Microsoft.Extensions.AI.Abstractions</c>, <c>Azure.AI.OpenAI</c>).
/// </para>
/// <para>
/// <b>The over-catching is deliberate and has a cost.</b> <c>Cohere</c> matches an assembly
/// containing "Coherence" and <c>Azure.AI</c> matches <c>Azure.AI.FormRecognizer</c>, which is
/// arguably a model and arguably not. On a clause about legal text this errs toward catching, and
/// the failure message tells the reader what to decide rather than pretending the answer is obvious.
/// </para>
/// <para>
/// <b>Why four tests.</b> A pin that passes by finding nothing says as much about the rule as about
/// the product, and the first version of this file proved that the hard way: four of seven mutants
/// survived review. Dropping a family from the list passed, because the rule test asked each family
/// about itself. Sweeping no assembly passed, and reading no reference passed, because the only
/// guard was that the directory listing was non-empty — the "a loop that would pass having checked
/// none" failure, on a file written by the seat that had found that failure in someone else's work
/// the same night. Each of those is now a named assertion.
/// </para>
/// </remarks>
[TestClass]
public sealed class NoModelDependencyCensusTests
{
    /// <summary>
    /// Every production assembly, pinned literally rather than read from the test's own output
    /// directory. <c>ClosedSurfaceCensus.LexAssembliesBeside</c> returns what is deployed beside
    /// these tests, which is four of the eight; <c>Lex.V3.Custody.Azure</c>,
    /// <c>Lex.V3.Custody.Probe</c>, <c>Lex.V3.Preview</c> and <c>Lex.V3.ContractTool</c> are not,
    /// and the first two are the projects that already carry the Azure SDKs — which is exactly where
    /// an <c>Azure.AI.*</c> reference would most naturally arrive. The shape is the one
    /// <c>RetiredGenerationBoundaryTests</c> uses for the same reason.
    /// </summary>
    private static readonly (string ProjectDirectory, string AssemblyName)[] SweptAssemblies =
    [
        ("src/Lex.V3.Api", "Lex.V3.Api"),
        ("src/Lex.V3.Artifacts", "Lex.V3.Artifacts"),
        ("src/Lex.V3.ContractTool", "Lex.V3.ContractTool"),
        ("src/Lex.V3.Contracts", "Lex.V3.Contracts"),
        ("src/Lex.V3.Custody.Azure", "Lex.V3.Custody.Azure"),
        ("src/Lex.V3.Custody.Probe", "Lex.V3.Custody.Probe"),
        ("src/Lex.V3.Ingest", "Lex.V3.Ingest"),
        ("src/Lex.V3.Preview", "Lex.V3.Preview"),
    ];

    /// <summary>The families an inference or embedding capability arrives under.</summary>
    private static readonly string[] ModelLibraries =
    [
        "Anthropic",
        "Azure.AI",
        "Bedrock",
        "Cohere",
        "Extensions.AI",
        "GenerativeAI",
        "HuggingFace",
        "LangChain",
        "LLamaSharp",
        "Microsoft.ML",
        "Mistral",
        "Ollama",
        "OnnxRuntime",
        "OpenAI",
        "SemanticKernel",
        "TensorFlow",
        "TorchSharp",
        "VectorData",
    ];

    /// <summary>
    /// Package ids that must be caught, written out rather than derived from
    /// <see cref="ModelLibraries"/>. The first version generated these from the families themselves,
    /// so removing a family removed its own assertion and the rule test still passed.
    /// </summary>
    private static readonly string[] MustBeCaught =
    [
        "Anthropic.SDK",
        "AWSSDK.BedrockRuntime",
        "Azure.AI.OpenAI",
        "Betalgo.Ranul.OpenAI",
        "Google.GenerativeAI",
        "LangChain.Core",
        "LLamaSharp",
        "Microsoft.Extensions.AI",
        "Microsoft.Extensions.AI.Abstractions",
        "Microsoft.Extensions.VectorData.Abstractions",
        "Microsoft.ML.OnnxRuntime",
        "Microsoft.SemanticKernel",
        "Mscc.GenerativeAI",
        "OllamaSharp",
        "OpenAI",
        "TorchSharp",
        "microsoft.ml.onnxruntime",
    ];

    /// <summary>.NET dependencies this product really holds, which the assembly rule must not reject.</summary>
    private static readonly string[] MustNotBeCaught =
    [
        "Azure.Core",
        "Azure.Identity",
        "Azure.Storage.Blobs",
        "Azure.Storage.Common",
        "JsonSchema.Net",
        "Microsoft.Data.Sqlite",
        "PdfPig",
        "System.Linq",
        "System.Text.Json",
        "System.Collections.Immutable",
        "Microsoft.Extensions.Logging",
        "netstandard",
    ];

    /// <summary>JavaScript package ids that must be caught, and the three the web surface holds.</summary>
    private static readonly string[] MustBeCaughtJs =
    [
        "openai",
        "@anthropic-ai/sdk",
        "onnxruntime-web",
        "@xenova/transformers",
        "langchain",
        "@huggingface/inference",
    ];

    private static readonly string[] MustNotBeCaughtJs = ["esbuild", "react", "react-dom"];

    /// <summary>JavaScript package ids, which arrive under different names than the .NET families.</summary>
    private static readonly string[] ModelPackagesJs =
    [
        "openai",
        "@anthropic-ai/",
        "onnxruntime",
        "transformers",
        "langchain",
        "@huggingface/",
    ];

    // TWO RULES, because the two namespaces are different and sharing one made a family untestable.
    // With a single rule the JS id `openai` caught the .NET family `OpenAI` case-insensitively, so
    // dropping `OpenAI` from ModelLibraries changed nothing and the mutant that removed it survived.
    // A .NET assembly reference is not a JavaScript package id and should not be matched against one.
    private static bool IsModelAssembly(string name) =>
        ModelLibraries.Any(library => name.Contains(library, StringComparison.OrdinalIgnoreCase));

    private static bool IsModelJsPackage(string name) =>
        ModelPackagesJs.Any(package => name.Contains(package, StringComparison.OrdinalIgnoreCase));

    private const string WhatToDo =
        "S4-A12 (no manufactured law) was classified accepted on the evidence that no model exists in "
        + "the product, which is why the clause cost nothing to hold. It now costs something: decide "
        + "what the model is for, and if it is anywhere near consolidation, legal identity or "
        + "authoritative entity extraction, S4-A12 has to be argued on behaviour rather than on "
        + "absence. Then update this pin and say on issue #348 which of the three prohibitions is "
        + "defended by what. This walk covers the eight production assemblies' compiled references "
        + "and the .NET and web package manifests.";

    [TestMethod]
    public void NoProductionAssemblyReferencesAModelLibrary()
    {
        var offending = new List<string>();
        foreach (var (assembly, name) in LoadSweptAssemblies())
        {
            foreach (var referenced in assembly.GetReferencedAssemblies())
            {
                if (IsModelAssembly(referenced.Name ?? string.Empty))
                {
                    offending.Add($"{name} -> {referenced.Name}");
                }
            }
        }

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            offending.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            "A production assembly references a model library. " + WhatToDo);
    }

    [TestMethod]
    public void TheSweepReadsEveryProductionAssemblyAndItsReferences()
    {
        // The sweep above passes by finding nothing, so on its own it cannot tell an empty walk from
        // a clean one. Two mutants proved that: sweeping no assembly passed, and reading no
        // reference passed. This asserts the walk reached each assembly by name and read references
        // that are really there.
        var seen = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (assembly, name) in LoadSweptAssemblies())
        {
            seen[name] = assembly.GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)
                .ToArray();
        }

        CollectionAssert.AreEqual(
            SweptAssemblies.Select(static item => item.AssemblyName).OrderBy(static n => n, StringComparer.Ordinal).ToArray(),
            seen.Keys.OrderBy(static n => n, StringComparer.Ordinal).ToArray(),
            "The walk did not reach every production assembly.");

        // Known references of two different projects, so a walk that silently skips one fails. The
        // second is in a project the earlier version of this sweep could not see at all.
        CollectionAssert.Contains(seen["Lex.V3.Ingest"], "Microsoft.Data.Sqlite",
            "Lex.V3.Ingest's references were not read.");
        CollectionAssert.Contains(seen["Lex.V3.Custody.Azure"], "Azure.Storage.Blobs",
            "Lex.V3.Custody.Azure's references were not read.");
        Assert.IsGreaterThan(0, seen.Values.Sum(static references => references.Length));
    }

    [TestMethod]
    public void NoPackageManifestDeclaresAModelDependency()
    {
        var offending = new List<string>();
        var root = FindRepositoryRoot();

        foreach (var project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(project))
            {
                var reference = ReadPackageReference(line);
                if (reference is not null && IsModelAssembly(reference))
                {
                    offending.Add($"{Path.GetFileName(project)} -> {reference}");
                }
            }
        }

        var packageJson = Path.Combine(root, "web", "package.json");
        Assert.IsTrue(File.Exists(packageJson), $"the web manifest must exist to be swept: {packageJson}");
        using var manifest = JsonDocument.Parse(File.ReadAllText(packageJson));
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (!manifest.RootElement.TryGetProperty(section, out var map)) continue;
            foreach (var dependency in map.EnumerateObject())
            {
                if (IsModelJsPackage(dependency.Name))
                {
                    offending.Add($"web/package.json {section} -> {dependency.Name}");
                }
            }
        }

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            offending.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            "A package manifest declares a model dependency. " + WhatToDo);
    }

    [TestMethod]
    public void TheRuleCatchesWhatItMustAndKeepsWhatThisProductHolds()
    {
        foreach (var package in MustBeCaught)
        {
            Assert.IsTrue(IsModelAssembly(package),
                $"{package} is a model or embedding client and the assembly rule does not catch it.");
        }

        foreach (var kept in MustNotBeCaught)
        {
            Assert.IsFalse(IsModelAssembly(kept),
                $"{kept} is a dependency this product holds and the assembly rule rejects it.");
        }

        foreach (var package in MustBeCaughtJs)
        {
            Assert.IsTrue(IsModelJsPackage(package),
                $"{package} is a model or embedding client and the web rule does not catch it.");
        }

        foreach (var kept in MustNotBeCaughtJs)
        {
            Assert.IsFalse(IsModelJsPackage(kept),
                $"{kept} is a web dependency this product holds and the web rule rejects it.");
        }

        Assert.IsGreaterThan(10, MustBeCaught.Length);
        Assert.IsGreaterThan(10, MustNotBeCaught.Length);
    }

    private static (Assembly Assembly, string Name)[] LoadSweptAssemblies()
    {
        var root = FindRepositoryRoot();
        var targetDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = targetDirectory.Name;
        var configuration = targetDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Cannot determine the test build configuration.");

        return SweptAssemblies.Select(item =>
        {
            var path = Path.Combine(
                root,
                item.ProjectDirectory.Replace('/', Path.DirectorySeparatorChar),
                "bin",
                configuration,
                targetFramework,
                item.AssemblyName + ".dll");
            Assert.IsTrue(File.Exists(path),
                $"the complete solution must build {item.AssemblyName} before this sweep: {path}");
            return (Assembly.LoadFrom(path), item.AssemblyName);
        }).ToArray();
    }

    private static string? ReadPackageReference(string line)
    {
        const string marker = "PackageReference Include=\"";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        var from = start + marker.Length;
        var end = line.IndexOf('"', from);
        return end < 0 ? null : line[from..end];
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
}

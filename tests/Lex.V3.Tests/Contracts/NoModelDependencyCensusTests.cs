using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
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
/// <b>Why it looks at three things.</b> A compiled assembly lists the references its code actually
/// binds, so it catches a model that is used and misses a package that is referenced and not yet
/// called. The manifests catch it the moment it is declared, and they are the only place the web
/// surface appears at all. The lock files catch what neither sees: a model library that arrives
/// <b>under</b> an allowed package is in no declaration and in no compiled reference of ours, and is
/// in the resolved closure. The first version of this file swept compiled references alone, and its
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
/// <b>Why nine tests, and why no test here walks anything of its own.</b> A pin that passes by
/// finding nothing says as much about the rule as about the product, and this file has proved
/// that twice. First, four of seven mutants survived review: dropping a family from the list
/// passed because the rule test asked each family about itself, and sweeping no assembly passed,
/// and reading no reference passed, because the only guard was that the directory listing was
/// non-empty. The repair added a second test that walked the assemblies again and asserted it
/// had reached all eight, and three mutants survived that too, because <b>a second walk cannot
/// guard the first one</b>: emptying the sweep's own loop left the guard's separate loop intact,
/// so the sweep passed having checked nothing while the guard passed having checked everything.
/// There is now exactly one walk of each kind, in <c>WalkProductionReferences</c>,
/// <c>WalkPackageManifests</c> and <c>WalkResolvedClosures</c>, and every test asserts over what
/// that walk returned. No loop is left that can be emptied without a named assertion failing.
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

    /// <summary>
    /// The spellings a declaration arrives in, and what must be taken from each. These are the
    /// reader's literals, the way <see cref="MustBeCaught"/> is the rule's: a reader that returns
    /// nothing reads as a clean file, so it needs cases that say yes and cases that say no.
    /// </summary>
    private static readonly (string Declaration, string? Package)[] DeclarationSpellings =
    [
        ("<PackageReference Include=\"Azure.AI.OpenAI\" Version=\"2.0.0\" />", "Azure.AI.OpenAI"),
        ("<PackageReference Condition=\"true\" Include=\"Azure.AI.OpenAI\" />", "Azure.AI.OpenAI"),
        ("<PackageReference Include='Azure.AI.OpenAI' />", "Azure.AI.OpenAI"),
        ("<PackageReference Update=\"Azure.AI.OpenAI\" Version=\"2.0.0\" />", "Azure.AI.OpenAI"),
        ("<PackageVersion Include=\"Azure.AI.OpenAI\" Version=\"2.0.0\" />", "Azure.AI.OpenAI"),
        ("<GlobalPackageReference Include=\"Azure.AI.OpenAI\" />", "Azure.AI.OpenAI"),
        ("<PackageDownload Include=\"Azure.AI.OpenAI\" />", "Azure.AI.OpenAI"),
        ("<ProjectReference Include=\"../Lex.V3.Contracts/Lex.V3.Contracts.csproj\" />", null),
        ("<None Include=\"Azure.AI.OpenAI.txt\" />", null),
    ];

    /// <summary>
    /// What these manifests really declare at this head. The offender list is empty when nothing was
    /// read, so naming what is there is the only thing that separates a clean sweep from no sweep.
    /// It makes this pin a dependency ledger as well as a prohibition, and that is the intent: on a
    /// clause whose whole evidence is that the dependency set contains no model, a dependency
    /// arriving unread is the failure mode, so an addition should stop here and be looked at.
    /// </summary>
    private static readonly string[] DeclaredProjectPackages =
    [
        "Azure.Identity",
        "Azure.Storage.Blobs",
        "JsonSchema.Net",
        "Microsoft.Data.Sqlite",
        "PdfPig",
    ];

    private static readonly string[] DeclaredWebPackages = ["esbuild", "react", "react-dom"];

    /// <summary>
    /// Every file that can declare a package for this product, named. <c>Directory.Build.props</c>
    /// is in this list because it applies to every project including the production ones, so a
    /// <c>PackageReference</c> added there is a production dependency that no <c>.csproj</c>
    /// mentions. It declares none today, which is why naming the file rather than counting the
    /// packages is what proves it was read at all.
    /// </summary>
    private static readonly string[] ExpectedManifestFiles =
    [
        "Directory.Build.props",
        "src/Lex.V3.Api/Lex.V3.Api.csproj",
        "src/Lex.V3.Artifacts/Lex.V3.Artifacts.csproj",
        "src/Lex.V3.ContractTool/Lex.V3.ContractTool.csproj",
        "src/Lex.V3.Contracts/Lex.V3.Contracts.csproj",
        "src/Lex.V3.Custody.Azure/Lex.V3.Custody.Azure.csproj",
        "src/Lex.V3.Custody.Probe/Lex.V3.Custody.Probe.csproj",
        "src/Lex.V3.Ingest/Lex.V3.Ingest.csproj",
        "src/Lex.V3.Preview/Lex.V3.Preview.csproj",
        "web/package.json",
    ];

    /// <summary>
    /// The resolved closure of each project that has one. <c>Lex.V3.ContractTool</c> declares no
    /// package and has no lock file, which is why this list is seven and the assembly list is eight.
    /// </summary>
    private static readonly string[] ExpectedLockFiles =
    [
        "src/Lex.V3.Api/packages.lock.json",
        "src/Lex.V3.Artifacts/packages.lock.json",
        "src/Lex.V3.Contracts/packages.lock.json",
        "src/Lex.V3.Custody.Azure/packages.lock.json",
        "src/Lex.V3.Custody.Probe/packages.lock.json",
        "src/Lex.V3.Ingest/packages.lock.json",
        "src/Lex.V3.Preview/packages.lock.json",
    ];

    /// <summary>
    /// Ids the closure really resolves, named rather than counted. The closure is not pinned as a
    /// set: it is thirty-five ids today and it changes with every restore, so a ledger of it would
    /// be churn rather than evidence. These five are the reach: two are <c>Transitive</c>, which is
    /// the thing a declaration sweep cannot see, and <c>JsonSchema.Net</c> and <c>Humanizer.Core</c>
    /// are each in one file only, so a walk that stops early loses them.
    /// </summary>
    private static readonly string[] ClosureAnchors =
    [
        "Azure.Core",
        "Humanizer.Core",
        "JsonSchema.Net",
        "Microsoft.Data.Sqlite",
        "SQLitePCLRaw.lib.e_sqlite3",
    ];

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
        + "defended by what. Three walks look: the eight production assemblies' compiled "
        + "references; the project files, .props and .targets under src, the .props and .targets at "
        + "the repository root, and web/package.json; and the seven resolved closures in "
        + "src/*/packages.lock.json. Build output under obj and bin is not read.";

    [TestMethod]
    public void NoProductionAssemblyReferencesAModelLibrary()
    {
        var seen = WalkProductionReferences();
        AssertTheAssemblyWalkWasComplete(seen);

        var offending = seen
            .SelectMany(static entry => entry.Value
                .Where(IsModelAssembly)
                .Select(reference => $"{entry.Key} -> {reference}"))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            offending,
            "A production assembly references a model library. " + WhatToDo);
    }

    [TestMethod]
    public void TheSweepReadsEveryProductionAssemblyAndItsReferences()
    {
        // The sweep passes by finding nothing, so on its own it cannot tell an empty walk from a
        // clean one, and it asserts completeness itself for that reason. This names the same
        // property separately, so removing that assertion from the sweep fails something.
        AssertTheAssemblyWalkWasComplete(WalkProductionReferences());
    }

    [TestMethod]
    public void NoPackageManifestDeclaresAModelDependency()
    {
        var walk = WalkPackageManifests();
        AssertTheManifestWalkWasComplete(walk);

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            walk.Offending,
            "A package manifest declares a model dependency. " + WhatToDo);
    }

    [TestMethod]
    public void TheManifestSweepReadsEveryManifestItClaimsTo()
    {
        AssertTheManifestWalkWasComplete(WalkPackageManifests());
    }

    [TestMethod]
    public void NoResolvedClosureContainsAModelPackage()
    {
        var walk = WalkResolvedClosures();
        AssertTheClosureWalkWasComplete(walk);

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            walk.Offending,
            "A resolved dependency closure contains a model package. " + WhatToDo);
    }

    [TestMethod]
    public void TheClosureSweepReadsEveryLockFile()
    {
        AssertTheClosureWalkWasComplete(WalkResolvedClosures());
    }

    [TestMethod]
    public void TheDeclarationSweepReachesImportsUnderSrcAndSkipsBuildOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-a12-manifests-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var relative in new[]
            {
                "Directory.Build.props",
                "Directory.Build.targets",
                "src/Widget/Widget.csproj",
                "src/Widget/Directory.Build.props",
                "src/Widget/Directory.Packages.props",
                "src/Widget/Deep/Nested.targets",
                "src/Widget/obj/Widget.csproj.nuget.g.props",
                "src/Widget/bin/Release/Copied.targets",
                "tests/Other/Other.csproj",
                "web/package.json",
            })
            {
                WriteFixtureFile(root, relative);
            }

            CollectionAssert.AreEqual(
                Ordered(new[]
                {
                    "Directory.Build.props",
                    "Directory.Build.targets",
                    "src/Widget/Deep/Nested.targets",
                    "src/Widget/Directory.Build.props",
                    "src/Widget/Directory.Packages.props",
                    "src/Widget/Widget.csproj",
                }),
                Ordered(EnumerateDeclaringFiles(root)),
                "The sweep must reach an import under src at any depth, must skip what obj and bin "
                + "hold, and must not wander outside src and the root. web/package.json is swept by "
                + "the manifest walk itself and tests/ is not production.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void TheDeclarationReaderReadsEverySpellingItClaimsTo()
    {
        foreach (var (declaration, package) in DeclarationSpellings)
        {
            var read = ReadDeclaredPackages(XDocument.Parse($"<Project>{declaration}</Project>"));
            CollectionAssert.AreEqual(
                package is null ? Array.Empty<string>() : [package],
                read,
                package is null
                    ? $"{declaration} declares no package and the reader took one from it."
                    : $"{declaration} declares {package} and the reader did not read it.");
        }
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

    /// <summary>
    /// The one walk of compiled references. Every assembly test asserts over what this returns, so
    /// emptying this loop fails the sweep itself rather than being covered by a second walk.
    /// </summary>
    private static Dictionary<string, string[]> WalkProductionReferences()
    {
        var seen = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (assembly, name) in LoadSweptAssemblies())
        {
            seen[name] = assembly.GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)
                .ToArray();
        }

        return seen;
    }

    private static void AssertTheAssemblyWalkWasComplete(Dictionary<string, string[]> seen)
    {
        CollectionAssert.AreEqual(
            SweptAssemblies.Select(static item => item.AssemblyName)
                .OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            seen.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            "The walk did not reach every production assembly.");

        // Known references of two different projects, so a walk that silently skips one fails. The
        // second is in a project the first version of this sweep could not see at all.
        CollectionAssert.Contains(seen["Lex.V3.Ingest"], "Microsoft.Data.Sqlite",
            "Lex.V3.Ingest's references were not read.");
        CollectionAssert.Contains(seen["Lex.V3.Custody.Azure"], "Azure.Storage.Blobs",
            "Lex.V3.Custody.Azure's references were not read.");

        // Per assembly rather than a total, because a total over eight is satisfied by one of them.
        foreach (var (name, references) in seen)
        {
            Assert.IsGreaterThan(0, references.Length, $"{name}'s references were not read.");
        }
    }

    private sealed record ManifestWalk(
        string[] Offending, string[] ManifestFiles, string[] ProjectPackages, string[] WebPackages);

    /// <summary>
    /// The one walk of declared packages, recording what it read as well as what it objects to.
    /// </summary>
    private static ManifestWalk WalkPackageManifests()
    {
        var offending = new List<string>();
        var manifestFiles = new List<string>();
        var projectPackages = new List<string>();
        var webPackages = new List<string>();
        var root = FindRepositoryRoot();

        foreach (var relative in EnumerateDeclaringFiles(root))
        {
            manifestFiles.Add(relative);
            var absolute = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            foreach (var reference in ReadDeclaredPackages(XDocument.Load(absolute)))
            {
                projectPackages.Add(reference);
                if (IsModelAssembly(reference))
                {
                    offending.Add($"{relative} -> {reference}");
                }
            }
        }

        var packageJson = Path.Combine(root, "web", "package.json");
        Assert.IsTrue(File.Exists(packageJson), $"the web manifest must exist to be swept: {packageJson}");
        manifestFiles.Add(RepositoryRelative(root, packageJson));
        using var manifest = JsonDocument.Parse(File.ReadAllText(packageJson));
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (!manifest.RootElement.TryGetProperty(section, out var map)) continue;
            foreach (var dependency in map.EnumerateObject())
            {
                webPackages.Add(dependency.Name);
                if (IsModelJsPackage(dependency.Name))
                {
                    offending.Add($"web/package.json {section} -> {dependency.Name}");
                }
            }
        }

        return new ManifestWalk(
            offending.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            manifestFiles.ToArray(),
            projectPackages.ToArray(),
            webPackages.ToArray());
    }

    private static void AssertTheManifestWalkWasComplete(ManifestWalk walk)
    {
        CollectionAssert.AreEqual(
            Ordered(ExpectedManifestFiles),
            Ordered(walk.ManifestFiles),
            "The manifest sweep did not read exactly the files that can declare a package for this "
            + "product. A project file, a root .props or .targets, or web/package.json was added, "
            + "removed or skipped; name it in ExpectedManifestFiles so the sweep is known to cover "
            + "it, because an offender list is empty whether the file was clean or unread.");
        CollectionAssert.AreEqual(
            Ordered(DeclaredProjectPackages),
            Ordered(walk.ProjectPackages.Distinct(StringComparer.Ordinal)),
            "The .NET sweep did not read exactly the packages src declares. If a dependency was "
            + "added, check it is not a model client and name it in DeclaredProjectPackages: the "
            + "evidence for S4-A12 is that this set is known, so one arriving unread is the failure.");
        CollectionAssert.AreEqual(
            Ordered(DeclaredWebPackages),
            Ordered(walk.WebPackages),
            "The web sweep did not read exactly the packages web/package.json declares. The same "
            + "applies: name it in DeclaredWebPackages after checking what it is.");
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

    /// <summary>
    /// Every file under <paramref name="root"/> that can declare a package, as repository-relative
    /// paths. Imports as well as project files, and under <c>src</c> as well as at the root:
    /// MSBuild imports the nearest <c>Directory.Build.props</c> and does not chain, so one added
    /// inside <c>src</c> <b>replaces</b> the root one for the projects below it rather than
    /// extending it, and a sweep of the root alone would be reading a file that had stopped
    /// applying.
    /// </summary>
    /// <remarks>
    /// It takes the root as a parameter so a fixture can exercise it. No <c>.props</c> or
    /// <c>.targets</c> exists under <c>src</c> in this repository today, so on the real tree a
    /// sweep that skipped them reads identically to one that does not and no mutant could tell the
    /// two apart. The fixture is what makes the claim testable rather than merely true.
    /// </remarks>
    private static string[] EnumerateDeclaringFiles(string root)
    {
        var source = Path.Combine(root, "src");
        var underSource = Directory.Exists(source)
            ? Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(source, "*.props", SearchOption.AllDirectories))
                .Concat(Directory.EnumerateFiles(source, "*.targets", SearchOption.AllDirectories))
            : Enumerable.Empty<string>();

        return underSource
            .Concat(Directory.EnumerateFiles(root, "*.props", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(root, "*.targets", SearchOption.TopDirectoryOnly))
            .Where(static file => !IsBuildOutput(file))
            .Select(file => RepositoryRelative(root, file))
            .ToArray();
    }

    private sealed record ClosureWalk(string[] Offending, string[] Files, string[] Packages);

    /// <summary>
    /// The one walk of resolved closures. A package this product never names can still put a model
    /// library on disk, and the lock file is where the restore records that.
    /// </summary>
    private static ClosureWalk WalkResolvedClosures()
    {
        var root = FindRepositoryRoot();
        var offending = new List<string>();
        var files = new List<string>();
        var packages = new List<string>();

        var locks = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "packages.lock.json", SearchOption.AllDirectories)
            .Where(static file => !IsBuildOutput(file));

        foreach (var file in locks)
        {
            files.Add(RepositoryRelative(root, file));
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("dependencies", out var frameworks)) continue;
            foreach (var framework in frameworks.EnumerateObject())
            {
                foreach (var package in framework.Value.EnumerateObject())
                {
                    packages.Add(package.Name);
                    if (IsModelAssembly(package.Name))
                    {
                        offending.Add(
                            $"{RepositoryRelative(root, file)} {framework.Name} -> {package.Name}");
                    }
                }
            }
        }

        return new ClosureWalk(
            offending.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            files.Distinct(StringComparer.Ordinal).ToArray(),
            packages.ToArray());
    }

    private static void AssertTheClosureWalkWasComplete(ClosureWalk walk)
    {
        CollectionAssert.AreEqual(
            Ordered(ExpectedLockFiles),
            Ordered(walk.Files),
            "The closure sweep did not read exactly the lock files this product has. A project "
            + "gained or lost one, or the walk stopped early; name it in ExpectedLockFiles. A lock "
            + "file is the restore's own record of what is on disk, so one going unread is the "
            + "closure going unchecked.");

        foreach (var anchor in ClosureAnchors)
        {
            CollectionAssert.Contains(walk.Packages, anchor,
                $"the closure sweep did not read {anchor}, which these lock files resolve.");
        }
    }

    /// <summary>A fixture file. Its content is never parsed: this exercises the enumeration.</summary>
    private static void WriteFixtureFile(string root, string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project />");
    }

    private static string RepositoryRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string[] Ordered(IEnumerable<string> values) =>
        values.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The element names that declare a package, and the two attributes that name one. Matched on
    /// <see cref="XName.LocalName"/> so an old-style project file with an MSBuild namespace reads
    /// the same as an SDK-style one.
    /// </summary>
    private static readonly string[] DeclarationElements =
        ["PackageReference", "PackageVersion", "GlobalPackageReference", "PackageDownload"];

    /// <summary>
    /// Every package a project file, import or central-management file declares. This parses the
    /// XML. The first version matched the text <c>PackageReference Include="</c> on one line, which
    /// is the single spelling <c>dotnet add package</c> writes: a <c>Condition</c> attribute before
    /// <c>Include</c>, single quotes, an <c>Update</c>, and the central-package-management elements
    /// all read as nothing. Reading the document removes the class of miss rather than five of its
    /// members.
    /// </summary>
    private static string[] ReadDeclaredPackages(XDocument document) =>
        document.Descendants()
            .Where(static element => DeclarationElements.Contains(element.Name.LocalName, StringComparer.Ordinal))
            .SelectMany(static element => new[]
            {
                element.Attribute("Include")?.Value,
                element.Attribute("Update")?.Value,
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();

    /// <summary>
    /// NuGet writes <c>.nuget.g.props</c> and <c>.nuget.g.targets</c> into <c>obj</c> on restore.
    /// They are generated output rather than anything a person declares, there are sixteen of them
    /// under <c>src</c> after a build, and they are rewritten whenever a restore runs.
    /// </summary>
    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

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

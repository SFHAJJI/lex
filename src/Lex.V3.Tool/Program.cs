// The V3 build tool: one verb, `build`, which acquires an EU work and a selected Luxembourg population live in
// one process under one wire ceiling, builds the corpus, the two indexes and the two capability
// manifests (each twice, compared), writes the five files Lex.V3.Api mounts, and reads them back.
//
// The composition lives in Lex.V3.Ingest (EuFirstMountAcquisition, LuxembourgFirstMountAcquisition,
// V3FirstMountBuild, V3CorpusMountWriter), where its offline tests can drive the acquisition
// sessions on a scripted transport; this program only parses arguments, opens the custody store
// and the system clock, and reports. Every argument is checked before the first request: the
// Luxembourg act range binds its three family ranges at construction, so an act the plan cannot
// name is a usage error, not a refusal after the EU side has spent the budget.
//
// Exit codes: 0 built and verified; 1 an unexpected failure (printed); 2 usage; 3 a typed refusal
// (printed with the wire spend); 4 the written directory did not verify; 130 cancelled.

using System.Runtime.InteropServices;
using Lex.V3.Artifacts;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

const string Usage =
    "Usage: Lex.V3.Tool build --celex <CELEX> --lu-name <key> --lu-start <IRI> --lu-end <IRI>\n"
    + "   or: Lex.V3.Tool build --celex <CELEX> --lu-population all\n"
    + "                         --custody <directory> --out <directory> --checkout <directory> --wire-ceiling <n>\n"
    + "  --celex        an Appendix A seed, the one EU work to acquire\n"
    + "  --lu-population all  all publisher keys through S/A/G, with existing scope and rights rules\n"
    + "  --lu-name      lowercase ASCII key prefixing the act's three family keys\n"
    + "  --lu-start/--lu-end  an ELI key range on the publisher's key order (start inclusive, end exclusive)\n"
    + "  --custody      the run's custody root (FileSystemCustodyStore); everything the run holds goes here\n"
    + "  --out          the v3-corpus directory to write\n"
    + "  --checkout     the repository root holding the renderer source files\n"
    + "  --wire-ceiling the one ceiling on publisher requests for the whole run, robots included\n"
    + "Exit codes: 0 built and verified, 1 unexpected failure, 2 usage, 3 typed refusal, 4 written directory did not verify, 130 cancelled";

string[] required = ["--celex", "--custody", "--out", "--checkout", "--wire-ceiling"];
string[] rangeOptions = ["--lu-name", "--lu-start", "--lu-end"];
string[] admitted = [.. required, .. rangeOptions, "--lu-population"];

if (args.Length == 0 || !string.Equals(args[0], "build", StringComparison.Ordinal) || (args.Length - 1) % 2 != 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 1; index + 1 < args.Length; index += 2)
{
    var name = args[index];
    if (!admitted.Contains(name, StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"Unknown option: {name}");
        Console.Error.WriteLine(Usage);
        return 2;
    }

    if (!options.TryAdd(name, args[index + 1]))
    {
        Console.Error.WriteLine($"Repeated option: {name}");
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

var wholePopulation = options.TryGetValue("--lu-population", out var population);
if (wholePopulation && (population != "all" || rangeOptions.Any(options.ContainsKey)))
{
    Console.Error.WriteLine("--lu-population must be all and cannot be combined with --lu-name, --lu-start or --lu-end.");
    return 2;
}
var expected = wholePopulation ? required : rangeOptions.Concat(required).ToArray();
var missing = expected.Where(name => !options.ContainsKey(name)).ToArray();
if (missing.Length != 0)
{
    Console.Error.WriteLine("Missing: " + string.Join(", ", missing));
    Console.Error.WriteLine(Usage);
    return 2;
}

if (!int.TryParse(options["--wire-ceiling"], out var ceiling) || ceiling < 2)
{
    Console.Error.WriteLine("--wire-ceiling must be an integer of at least 2 (robots plus one product request).");
    return 2;
}

var checkout = Path.GetFullPath(options["--checkout"]);
if (!Directory.Exists(checkout))
{
    Console.Error.WriteLine($"--checkout is not a directory: {checkout}");
    return 2;
}

LuxembourgActRange act;
try
{
    act = wholePopulation
        ? LuxembourgActRange.WholePopulation
        : new LuxembourgActRange(options["--lu-name"], options["--lu-start"], options["--lu-end"]);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"The Luxembourg act range is not one the plan can name: {exception.Message}");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
using var termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    cancellation.Cancel();
});
var token = cancellation.Token;

try
{
    var custodyRoot = Path.GetFullPath(options["--custody"]);
    Directory.CreateDirectory(custodyRoot);
    var store = new FileSystemCustodyStore(custodyRoot);
    var budget = WireRequestBudget.OfWireRequests(ceiling);
    var startedAt = DateTimeOffset.UtcNow;
    Console.WriteLine($"lex-v3 build: custody={custodyRoot} ceiling={ceiling} started={startedAt:O}");

    var europeRenderers = await EuRendererSources.FromCheckoutAsync(store, checkout, token);
    var luxembourgRenderers = await LuxembourgRendererSources.FromCheckoutAsync(store, checkout, token);
    Console.WriteLine("renderer sources held: 6 Europe, 2 Luxembourg");

    var europe = await new EuFirstMountAcquisition(store, TimeProvider.System)
        .RunAsync(options["--celex"], europeRenderers, budget, token);
    if (!europe.Delivered)
    {
        Console.Error.WriteLine($"refused: europe: {europe.Refusal}: {europe.Detail} (spent {budget.Spent} of {budget.Limit})");
        return 3;
    }

    Console.WriteLine(
        $"europe: run complete, {europe.Run!.ObservedExpressionCount} expression(s), "
        + $"formex eligible {europe.Formex!.EligibleExpressionCount}, notice route {europe.LegalNotice!.Route!.Hops.Count} hop(s); "
        + $"spent {budget.Spent} of {budget.Limit}");

    var luxembourg = await new LuxembourgFirstMountAcquisition(store, TimeProvider.System)
        .RunAsync(act, luxembourgRenderers, budget, token);
    if (!luxembourg.Delivered)
    {
        Console.Error.WriteLine($"refused: luxembourg: {luxembourg.Refusal}: {luxembourg.Detail} (spent {budget.Spent} of {budget.Limit})");
        return 3;
    }

    Console.WriteLine(
        $"luxembourg: run complete, {luxembourg.Run!.CorpusRecordSet?.Set.Records.Count ?? 0} corpus record(s), "
        + $"vocabulary evidence {luxembourg.VocabularyEvidenceRef!.Sha256[..12]}; spent {budget.Spent} of {budget.Limit}");

    var build = await new V3FirstMountBuild(store).RunAsync(europe, luxembourg, token);
    if (!build.Delivered)
    {
        Console.Error.WriteLine($"refused: build: {build.Refusal}: {build.Detail} (spent {budget.Spent} of {budget.Limit})");
        return 3;
    }

    Console.WriteLine(
        $"built: corpus {build.Corpus!.ArtifactRef.Sha256[..12]} ({build.Corpus.VerifiedSet.Set.Members.Count} member(s)), "
        + $"luxembourg index {build.LuxembourgIndex!.IndexRef.Sha256[..12]}, europe index {build.EuropeIndex!.IndexRef.Sha256[..12]}; each built twice and equal");

    var write = await V3CorpusMountWriter.WriteAsync(build, options["--out"], token);
    foreach (var file in write.Files)
    {
        Console.WriteLine($"wrote {file.Name} {file.ByteLength} bytes sha256={file.Sha256}");
    }

    Console.WriteLine($"report {write.ReportPath} sha256={write.ReportSha256}");

    var verification = await V3CorpusMountWriter.VerifyAsync(write.Directory, token);
    if (!verification.Verified)
    {
        Console.Error.WriteLine($"the written directory did not verify: {verification.Detail}");
        return 4;
    }

    Console.WriteLine($"verified: {write.Directory} reads back as one mount bound to corpus {verification.CorpusRef!.Sha256}; "
        + $"wire spent {budget.Spent} of {budget.Limit}; elapsed {DateTimeOffset.UtcNow - startedAt:c}");
    return 0;
}
catch (OperationCanceledException) when (token.IsCancellationRequested)
{
    Console.Error.WriteLine("cancelled");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"failed: {exception.GetType().Name}: {exception.Message}");
    return 1;
}

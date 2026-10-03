// The V3 build tool acquires selected EU works and a selected Luxembourg population live in
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
// Every run writes a progress journal beside its custody (AcquisitionJournal): one line per unit
// whose evidence custody holds. --resume-from names an interrupted run's journal. It is verified
// before any request (its hash chain, its first line against this build's source and these
// arguments, every object it names read back from --custody), held in custody with a resume
// record, and the units it names are replayed through their own checked readers while the rest
// is acquired live; the run's catalogs and build report then say it resumed, from what and with
// what spends. A journal that does not verify is a usage error: nothing is spent.
//
// Exit codes: 0 built and verified; 1 an unexpected failure (printed); 2 usage, or a --resume-from
// journal that does not verify; 3 a typed refusal (printed with the wire spend); 4 the written
// directory did not verify; 130 cancelled.

using System.Runtime.InteropServices;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

if (args.Length > 0 && args[0] == "derive") return await OfflineDeriveCommand.RunAsync(args[1..]);

const string Usage =
    "Usage: Lex.V3.Tool build --celex <CELEX[,CELEX...]|all> --lu-name <key> --lu-start <IRI> --lu-end <IRI>\n"
    + "   or: Lex.V3.Tool build --celex <CELEX[,CELEX...]|all> --lu-population <all|legislative>\n"
    + "                         --custody <directory> --out <directory> --checkout <directory> --wire-ceiling <n>\n"
    + "  --celex        an Appendix A seed, comma-separated seeds, or all for the 82-seed population\n"
    + "  --eu-checkpoint  retained EU acquisition reference JSON in this custody; reuse its population and renew only the rights notice\n"
    + "  --lu-population all  all publisher IRI keys through S/A/G, with existing scope and rights rules\n"
    + "  --lu-population legislative  explicit code/loi/rgd ranges, including descendants; other URI families typed unenumerated\n"
    + "  --lu-name      lowercase ASCII key prefixing the act's three family keys\n"
    + "  --lu-start/--lu-end  an ELI key range on the publisher's key order (start inclusive, end exclusive)\n"
    + "  --custody      the run's custody root (FileSystemCustodyStore); everything the run holds goes here\n"
    + "  --custody-encoding raw (default) or brotli; references always identify original bytes\n"
    + "  --out          the v3-corpus directory to write\n"
    + "  --checkout     the repository root holding the renderer source files\n"
    + "  --wire-ceiling the one ceiling on publisher requests for the whole run, robots included\n"
    + "  --predecessor  a previous build's v3-corpus directory: its Luxembourg event log is carried forward and this build appends to it,\n"
    + "                 and the earlier generations the retention line keeps are copied from it into <out>/generations\n"
    + "  --referenced   with --predecessor: a JSON array of Luxembourg index digests a published permalink or evidence bundle references,\n"
    + "                 each kept indefinitely (none is recorded anywhere else)\n"
    + "  --resume-from  an interrupted run's progress journal (acquisition-progress-*.jsonl), verified before any request; the same\n"
    + "                 arguments and source build are required, and --custody holds the interrupted run's objects (never the journal)\n"
    + "Exit codes: 0 built and verified, 1 unexpected failure, 2 usage or a journal that does not verify, 3 typed refusal,\n"
    + "            4 written directory did not verify, 130 cancelled";

string[] required = ["--celex", "--custody", "--out", "--checkout", "--wire-ceiling"];
string[] rangeOptions = ["--lu-name", "--lu-start", "--lu-end"];
string[] admitted = [.. required, .. rangeOptions, "--lu-population", "--custody-encoding", "--predecessor", "--referenced", "--eu-checkpoint",
    "--resume-from"];

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

var custodyEncoding = options.GetValueOrDefault("--custody-encoding", "raw");
if (custodyEncoding is not ("raw" or "brotli"))
{
    Console.Error.WriteLine("--custody-encoding must be raw or brotli.");
    return 2;
}

var populationRequested = options.TryGetValue("--lu-population", out var population);
if (populationRequested && (population is not ("all" or "legislative") || rangeOptions.Any(options.ContainsKey)))
{
    Console.Error.WriteLine("--lu-population must be all or legislative and cannot be combined with --lu-name, --lu-start or --lu-end.");
    return 2;
}
var expected = populationRequested ? required : rangeOptions.Concat(required).ToArray();
var missing = expected.Where(name => !options.ContainsKey(name)).ToArray();
if (missing.Length != 0)
{
    Console.Error.WriteLine("Missing: " + string.Join(", ", missing));
    if (!populationRequested && rangeOptions.All(name => !options.ContainsKey(name)))
    {
        Console.Error.WriteLine("Use --lu-population legislative for the launch URI families, or all for all publisher IRIs.");
    }
    Console.Error.WriteLine(Usage);
    return 2;
}

if (!int.TryParse(options["--wire-ceiling"], out var ceiling) || ceiling < 2)
{
    Console.Error.WriteLine("--wire-ceiling must be an integer of at least 2 (robots plus one product request).");
    return 2;
}

// The predecessor is read and verified before the first request, like every other argument.
LuxembourgIndexPredecessor? predecessor = null;
if (options.TryGetValue("--predecessor", out var predecessorDirectory))
{
    predecessor = V3FirstMountBuild.ReadPredecessor(Path.GetFullPath(predecessorDirectory), out var predecessorRefusal, out var predecessorDetail);
    if (predecessor is null)
    {
        Console.Error.WriteLine($"--predecessor refused: {predecessorRefusal}: {predecessorDetail}");
        return 2;
    }

    try { await V3OfflineMount.ValidatePredecessorAsync(Path.GetFullPath(predecessorDirectory), CancellationToken.None); }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
        or Lex.V3.Contracts.Custody.CustodyRequiredException or Lex.V3.Contracts.Custody.CustodyIntegrityException)
    {
        Console.Error.WriteLine($"--predecessor refused before acquisition: {exception.Message}");
        return 2;
    }

    Console.WriteLine($"predecessor: luxembourg index {predecessor.IndexSha256[..12]}, its event log carried forward");
}

// The generations a published permalink or evidence bundle references: a JSON array of index digests, read before the
// first request too. Only a chained build keeps generations.
IReadOnlySet<string> referenced = new HashSet<string>(StringComparer.Ordinal);
if (options.TryGetValue("--referenced", out var referencedPath))
{
    if (predecessor is null)
    {
        Console.Error.WriteLine("--referenced needs --predecessor: only a chained build keeps generations.");
        return 2;
    }

    try
    {
        var digests = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.GetFullPath(referencedPath))) ?? [];
        if (digests.Any(static digest => digest is null || digest.Length != 64 || !digest.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))))
        {
            Console.Error.WriteLine("--referenced must be a JSON array of 64-character lower-case hex index digests.");
            return 2;
        }

        referenced = digests.ToHashSet(StringComparer.Ordinal);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"--referenced does not read: {exception.Message}");
        return 2;
    }
}

Lex.V3.Contracts.Source.Core.SourceArtifactRef? retainedEuCheckpoint = null;
if (options.TryGetValue("--eu-checkpoint", out var euCheckpointPath))
{
    try
    {
        retainedEuCheckpoint = Lex.V3.Contracts.ContractJson.Deserialize<Lex.V3.Contracts.Source.Core.SourceArtifactRef>(
            File.ReadAllText(Path.GetFullPath(euCheckpointPath)));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"--eu-checkpoint is not a valid retained acquisition reference: {exception.Message}");
        return 2;
    }
}

// The interrupted run's journal is read here, before anything else is opened; its lines and its first line are verified
// once custody is open, against this build and these arguments, and still before any request.
byte[]? interruptedJournal = null;
if (options.TryGetValue("--resume-from", out var resumePath))
{
    try
    {
        interruptedJournal = await File.ReadAllBytesAsync(Path.GetFullPath(resumePath));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine($"--resume-from refused before any request: the journal does not read: {exception.Message}");
        return 2;
    }
}

var checkout = Path.GetFullPath(options["--checkout"]);
if (!Directory.Exists(checkout))
{
    Console.Error.WriteLine($"--checkout is not a directory: {checkout}");
    return 2;
}

LuxembourgActRange? act;
LuxembourgPopulationScope? populationScope = null;
try
{
    if (population == "legislative")
    {
        populationScope = LuxembourgPopulationScope.Legislative;
        act = null;
    }
    else act = populationRequested
        ? LuxembourgActRange.WholePopulation
        : new LuxembourgActRange(options["--lu-name"], options["--lu-start"], options["--lu-end"]);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"The Luxembourg act range is not one the plan can name: {exception.Message}");
    return 2;
}

var celexes = options["--celex"] == "all"
    ? EuAppendixASeedMap.SeedsInCelexOrder.Select(seed => seed.Celex).ToArray()
    : options["--celex"].Split(',', StringSplitOptions.None);

// One renderer source type's files, out of the references a verified journal names by file.
static IReadOnlyDictionary<string, Lex.V3.Contracts.Source.Core.SourceArtifactRef> Roles(
    IReadOnlyDictionary<string, Lex.V3.Contracts.Source.Core.SourceArtifactRef> held, IReadOnlyList<string> files) =>
    files.ToDictionary(file => file, file => held[file], StringComparer.Ordinal);

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
    var store = custodyEncoding == "brotli"
        ? FileSystemCustodyStore.WithBrotliCompression(custodyRoot)
        : new FileSystemCustodyStore(custodyRoot);
    var budget = WireRequestBudget.OfWireRequests(ceiling);
    var startedAt = DateTimeOffset.UtcNow;
    Console.WriteLine($"lex-v3 build: custody={custodyRoot} custody_encoding={custodyEncoding} ceiling={ceiling} started={startedAt:O}");

    // A resume is verified whole before any request: the journal's lines, its first line against this build and these
    // arguments, and every object it names, read back from this custody; then its bytes and a resume record are held.
    var source = AcquisitionJournal.CurrentSource();
    var arguments = AcquisitionJournal.DigestArguments(celexes, act, populationScope, custodyEncoding, retainedEuCheckpoint);
    AcquisitionResume? resume = null;
    if (interruptedJournal is not null)
    {
        try
        {
            resume = await AcquisitionResume.OpenAsync(store, interruptedJournal, source, arguments, TimeProvider.System, token);
        }
        catch (Exception exception) when (exception is Lex.V3.Contracts.Custody.CustodyRequiredException or Lex.V3.Contracts.Custody.CustodyIntegrityException)
        {
            Console.Error.WriteLine($"--resume-from refused before any request: {exception.Message}");
            return 2;
        }

        Console.WriteLine($"resume: journal {resume.JournalSha256} verified through seq {resume.LastSeq}; "
            + $"the interrupted run spent at least {resume.LastWireSpent} of its ceiling {resume.Header.WireCeiling}; "
            + $"resume record {resume.Record!.Sha256}");
    }

    EuRendererSources? europeRenderers;
    LuxembourgRendererSources luxembourgRenderers;
    if (resume is null)
    {
        europeRenderers = retainedEuCheckpoint is null ? await EuRendererSources.FromCheckoutAsync(store, checkout, token) : null;
        luxembourgRenderers = await LuxembourgRendererSources.FromCheckoutAsync(store, checkout, token);
        Console.WriteLine(retainedEuCheckpoint is null ? "renderer sources held: 6 Europe, 2 Luxembourg"
            : "renderer sources held: 2 Luxembourg; Europe sources reopen from retained custody");
    }
    else
    {
        // The interrupted run's renderer sources reopen from custody by the references its journal names, once the
        // checkout is shown to hold the same bytes: opening the checkout afresh would mint new identities, which every
        // replayed unit's renderer binding refuses.
        try
        {
            IReadOnlyList<string> europeFiles = retainedEuCheckpoint is null ? EuRendererSources.RendererFiles : [];
            var held = await resume.VerifyRenderersAsync(checkout, [.. europeFiles, .. LuxembourgRendererSources.RendererFiles], token);
            europeRenderers = retainedEuCheckpoint is null
                ? await EuRendererSources.FromCustodyAsync(store, Roles(held, EuRendererSources.RendererFiles), token)
                : null;
            luxembourgRenderers = await LuxembourgRendererSources.FromCustodyAsync(store,
                Roles(held, LuxembourgRendererSources.RendererFiles), token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or Lex.V3.Contracts.Custody.CustodyRequiredException or Lex.V3.Contracts.Custody.CustodyIntegrityException)
        {
            Console.Error.WriteLine($"--resume-from refused before any request: renderer sources: {exception.Message}");
            return 2;
        }

        Console.WriteLine($"renderer sources reopened from custody: {(europeRenderers is null ? 0 : 6)} Europe, 2 Luxembourg, "
            + "the bytes the interrupted run held");
    }

    // This run's own journal, written beside custody: one line for each unit whose evidence custody holds.
    var journalRenderers = new List<AcquisitionJournalRenderer>();
    if (europeRenderers is not null)
    {
        var europeSources = new[] { europeRenderers.Census, europeRenderers.ObjectFacts, europeRenderers.Witness,
            europeRenderers.DocumentFetch, europeRenderers.FormexManifestation, europeRenderers.LegalNotice };
        for (var index = 0; index < europeSources.Length; index++)
            journalRenderers.Add(new AcquisitionJournalRenderer(EuRendererSources.RendererFiles[index], europeSources[index].Reference));
    }

    journalRenderers.Add(new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[0], luxembourgRenderers.Query.Reference));
    journalRenderers.Add(new AcquisitionJournalRenderer(LuxembourgRendererSources.RendererFiles[1], luxembourgRenderers.DocumentFetch.Reference));
    await using var journal = AcquisitionJournal.CreateInCustody(custodyRoot,
        new AcquisitionJournalHeader(source, arguments, ceiling, journalRenderers.ToArray(), resume?.Continuation),
        budget, TimeProvider.System);
    Console.WriteLine($"progress journal: {journal.Location}");

    Console.WriteLine($"europe selection: {celexes.Length} seed(s)");
    var euAcquisition = new EuFirstMountAcquisition(store, TimeProvider.System, journal, resume);
    Lex.V3.Contracts.Source.Core.SourceArtifactRef? resumedEuCatalog = null;
    if (resume is not null)
    {
        try
        {
            resumedEuCatalog = await euAcquisition.PrepareResumeAsync(celexes, europeRenderers, retainedEuCheckpoint, token);
        }
        catch (Exception exception) when (exception is Lex.V3.Contracts.Custody.CustodyRequiredException or Lex.V3.Contracts.Custody.CustodyIntegrityException)
        {
            Console.Error.WriteLine($"--resume-from refused before any request: europe: {exception.Message}");
            return 2;
        }
    }

    // The EU catalog the interrupted run journaled is reused exactly as a retained one is: its checked reopen admits the
    // whole population before the one new rights request.
    var reusedEuCheckpoint = resumedEuCatalog ?? retainedEuCheckpoint;
    EuFirstMountAcquisitionResult europe;
    if (reusedEuCheckpoint is not null)
    {
        try
        {
            var currentDocumentFetchSource = await File.ReadAllBytesAsync(Path.Combine(checkout,
                "src/Lex.V3.Contracts/Source/Europe/EuDocumentFetchPlan.cs"), token);
            europe = await euAcquisition.ReuseAsync(reusedEuCheckpoint, celexes,
                currentDocumentFetchSource, budget, token);
        }
        catch (Exception exception) when (exception is Lex.V3.Contracts.Custody.CustodyRequiredException or Lex.V3.Contracts.Custody.CustodyIntegrityException)
        {
            if (resumedEuCatalog is not null)
            {
                Console.Error.WriteLine($"--resume-from refused before any request: europe catalog: {exception.Message}");
                return 2;
            }

            Console.Error.WriteLine($"refused: retained europe acquisition: {exception.Message} (spent {budget.Spent} of {budget.Limit})");
            return 3;
        }
        Console.WriteLine(resumedEuCatalog is null
            ? "europe input: retained population; new rights notice required for this build"
            : "europe input: the population the interrupted run retained; new rights notice required for this build");
    }
    else
    {
        europe = await euAcquisition.RunAsync(celexes, europeRenderers!, budget, token);
    }
    if (!europe.Delivered)
    {
        Console.Error.WriteLine($"refused: europe: {europe.Refusal}: {europe.Detail} (spent {budget.Spent} of {budget.Limit})");
        if (europe.Formex is { } refusedFormex)
            Console.WriteLine("europe formex outcomes: " + refusedFormex.CreateOutcomeDiagnosticsJson());
        return 3;
    }

    Console.WriteLine(
        $"europe: run complete, {europe.Run!.ObservedExpressionCount} expression(s), "
        + $"formex enumerated {europe.Formex!.Enumerations.Count}, eligible {europe.Formex.EligibleExpressionCount}, "
        + $"not_enumerated_language_out_of_scope {europe.Formex.NotEnumeratedExpressionCount}; "
        + $"notice route {europe.LegalNotice!.Route!.Hops.Count} hop(s); "
        + $"spent {budget.Spent} of {budget.Limit}");

    Console.WriteLine("europe formex outcomes: " + europe.Formex.CreateOutcomeDiagnosticsJson());
    if (europe.Resumption is { } europeResumption)
        Console.WriteLine($"europe resumed from journal {europeResumption.JournalSha256} through seq {europeResumption.LastSeq}: "
            + Lex.V3.Contracts.ContractJson.Serialize(europeResumption.Phases));
    if (europe.CheckpointRef is { } euCheckpoint)
    {
        var pointer = Path.Combine(custodyRoot, $"eu-acquisition-{euCheckpoint.Sha256}.json");
        await File.WriteAllTextAsync(pointer, Lex.V3.Contracts.ContractJson.Serialize(euCheckpoint), token);
        Console.WriteLine($"europe checkpoint: {pointer}");
    }

    var luAcquisition = new LuxembourgFirstMountAcquisition(store, TimeProvider.System);
    if (populationScope is not null)
        Console.WriteLine("luxembourg declared scope: " + Lex.V3.Contracts.ContractJson.Serialize(new
        {
            populationScope.Policy, populationScope.Ranges,
            unselectedIriFamilies = "not_enumerated_outside_declared_ranges",
        }));
    var luxembourg = populationScope is null
        ? await luAcquisition.RunAsync(act!, luxembourgRenderers, budget, token)
        : await luAcquisition.RunPopulationAsync(populationScope, luxembourgRenderers, budget, token);
    if (luxembourg.Run is { } observedLuxembourg)
        Console.WriteLine("luxembourg family outcomes: " + Lex.V3.Contracts.ContractJson.Serialize(observedLuxembourg.FamilyOutcomes));
    if (!luxembourg.Delivered)
    {
        Console.Error.WriteLine($"refused: luxembourg: {luxembourg.Refusal}: {luxembourg.Detail} (spent {budget.Spent} of {budget.Limit})");
        return 3;
    }

    Console.WriteLine(
        $"luxembourg: run complete, {luxembourg.Run!.CorpusRecordSet?.Set.Records.Count ?? 0} corpus record(s), "
        + $"vocabulary evidence {luxembourg.VocabularyEvidenceRef!.Sha256[..12]}; spent {budget.Spent} of {budget.Limit}");

    var luRecords = luxembourg.Run!.CorpusRecordSet!.Set.Records;
    var luBodies = luRecords.Where(record => record.Body.Receipt is not null)
        .Select(record => record.Body.Receipt!.Reference).DistinctBy(reference => reference.ContentSha256).ToArray();
    Console.WriteLine("luxembourg record measurements: " + Lex.V3.Contracts.ContractJson.Serialize(new
    {
        records = luRecords.Count,
        uniqueHeldBodyCount = luBodies.Length,
        uniqueHeldBodyBytes = luBodies.Sum(reference => reference.ByteLength),
        outcomes = luRecords.GroupBy(record => new { record.Body.Kind, record.Body.NotHeldReason,
            pendingReason = record.Body.PendingAcquisitionReason?.Kind })
            .Select(group => new { outcome = group.Key, records = group.Count() }).ToArray(),
    }));

    var derivationTime = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1);
    var generationSource = predecessor is null ? null : new V3GenerationSource(Path.GetFullPath(predecessorDirectory!), referenced);
    var checkpoint = populationScope is null
        ? await V3OfflineMount.CaptureAsync(store, europe, luxembourg, celexes, act!, derivationTime, generationSource, token)
        : await V3OfflineMount.CapturePopulationAsync(store, europe, luxembourg, celexes, populationScope,
            derivationTime, generationSource, token);
    if (luxembourg.PopulationScopeManifestRef is { } scopeManifest)
    {
        var scopePath = Path.Combine(custodyRoot, "lu-population-scope-" + scopeManifest.Sha256 + ".json");
        await File.WriteAllTextAsync(scopePath, Lex.V3.Contracts.ContractJson.Serialize(scopeManifest), cancellation.Token);
        Console.WriteLine("LU population family disposition pointer: " + scopePath);
    }
    var checkpointPath = Path.Combine(custodyRoot, "mount-inputs-" + checkpoint.Sha256 + ".json");
    await File.WriteAllTextAsync(checkpointPath, Lex.V3.Contracts.ContractJson.Serialize(checkpoint), token);
    Console.WriteLine($"offline inputs: {checkpointPath}; derive --custody {custodyRoot} --checkpoint {checkpointPath} --out <fresh-directory> --custody-encoding {custodyEncoding}");
    var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(derivationTime)).RunAsync(europe, luxembourg, predecessor, token);
    if (!build.Delivered)
    {
        Console.Error.WriteLine($"refused: build: {build.Refusal}: {build.Detail} (spent {budget.Spent} of {budget.Limit})");
        return 3;
    }

    Console.WriteLine(
        $"built: corpus {build.Corpus!.ArtifactRef.Sha256[..12]} ({build.Corpus.VerifiedSet.Set.Members.Count} member(s)), "
        + $"luxembourg index {build.LuxembourgIndex!.IndexRef.Sha256[..12]}, europe index {build.EuropeIndex!.IndexRef.Sha256[..12]}; each built twice and equal");

    var write = await V3CorpusMountWriter.WriteAsync(
        build,
        options["--out"],
        generationSource,
        token, derivationTime);
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

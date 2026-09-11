using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Asks which event resource a draft reaches is the <c>jolux:OpinionRequest</c>, by identity.
/// </summary>
/// <remarks>
/// <para>
/// THE RELATIONSHIP, NOT THE POPULATION. The retained draft-graph delivery is broad-predicate, so
/// it holds the COMPLETE outbound edge set of its 650 drafts: eighteen predicates, none of them
/// naming an opinion request, and no <c>referralDate</c> anywhere. Two edges reach an event
/// resource almost exactly once per draft - <c>hasOpinion</c> to <c>/evenement/sace/</c> and
/// <c>draftHasTask</c> to <c>/evenement/scac/</c>. Retained evidence carries the class of every
/// DRAFT and the IRI of every event, and never the class of an event, which is the one gap a live
/// request has to close.
/// </para>
/// <para>
/// COUNTS CANNOT DECIDE IT, WHICH IS WHY THIS EXISTS. Extrapolated to the measured population the
/// two candidates give 7,741 and 7,753 against 7,751 measured <c>OpinionRequest</c> instances -
/// both within 0.15%, and picking either on that basis would feel confirmed and prove nothing. So
/// this asks a membership question about one exact resource instead: the range
/// <c>[(OpinionRequest, IRI), (OpinionRequest, IRI + "!"))</c> over the keyset
/// <c>(STR(?type), STR(?resource))</c> contains that resource and nothing else, because any IRI
/// continuing past it does so with a character above <c>!</c> (0x21) and IRIs carry none below it.
/// A count of 1 is that resource holding that class; 0 is it not.
/// </para>
/// <para>
/// THE SECOND PAIR IS GATED, STRUCTURALLY. The owner authorized at most five wire requests and
/// required the robots bootstrap and both membership checks first, with the remaining two spent
/// only to cross-check a POSITIVELY IDENTIFIED relationship. So the cross-check is unreachable
/// unless exactly one candidate answered 1 and the other 0. Both positive, both zero, any other
/// value or any failure is ambiguity: the run stops with two requests unspent and reports. That is
/// a branch in this method, not a judgement made after reading the numbers.
/// </para>
/// <para>
/// MEASUREMENT ONLY. A positive result establishes that a specific resource reached by a specific
/// predicate carries that class on this endpoint at this moment. It is not a production
/// relationship and not a closure claim, and it does NOT establish <c>referralDate</c> on those
/// resources: <c>assertion-rows</c> restricts predicates to a closed vocabulary that excludes it,
/// so that needs its own separately reviewed query.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgOpinionRequestRelationshipCanary
{
    private const string EnableVariable = "LEX_E8_OPINION_REQUEST_RELATIONSHIP";

    /// <summary>The plan's typed-resource set: <c>?resource a ?type</c>, keyed by type then resource.</summary>
    private const string TypedResourcesSetId = "R";

    /// <summary>
    /// The exclusive upper sentinel for a single-resource range.
    /// </summary>
    /// <remarks>
    /// <c>!</c> is 0x21, the lowest printable ASCII above space. An IRI that continues past the
    /// target continues with a character above it - <c>/sace/10</c> continues the <c>/sace/1</c>
    /// prefix with <c>0</c> (0x30) - so the range admits the target alone. Chosen over <c>U+0000</c>
    /// because the parameter transport carries printable text.
    /// </remarks>
    private const string NextAfterTarget = "!";

    private const string DraftA = "http://data.legilux.public.lu/eli/dl/pl/2000/119";
    private const string DraftB = "http://data.legilux.public.lu/eli/dl/pc/2002/215";

    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task TheEventADraftReachesIsAskedWhetherItHoldsTheOpinionRequestClass()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 to ask Legilux which event resource holds jolux:OpinionRequest. "
                + "Skipped by default so the suite sends no unasked traffic.");
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "e8-opinion-relationship-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);

        var scopeBytes = Encoding.UTF8.GetBytes(
            "E8 OpinionRequest relationship probe\n"
            + "question=which event resource a draft reaches carries jolux:OpinionRequest\n"
            + "budget=5 wire requests, gated: cross-check only on positive identification\n");
        var scopeReceipt = await store.CreateAsync(scopeBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var scopeRef = new SourceArtifactRef(NewUrn(), scopeReceipt.Reference.ContentSha256);

        var plan = LuxembourgQueryPlan.CreateDefaultGraph(
            OfficialMachineQuerySourceProfiles.Resolve(
                OfficialMachineQuerySourceProfileId.LuxembourgSparql).ArtifactRef,
            scopeRef);

        var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(
            checkout, "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgQueryPlan.cs"));
        var renderer = MachineQueryRendererSource.Open(
            new SourceArtifactRef(NewUrn(), Convert.ToHexStringLower(SHA256.HashData(sourceBytes))),
            sourceBytes);

        // REQUEST 1: the robots bootstrap the session performs before any product request.
        var witness = plan.BindCount(
            NewUrn(), NewUrn(), NewUrn(), TypedResourcesSetId, LuxembourgQueryPass.Pass1,
            RangeFor(SaceOf(DraftA)), renderer);
        var start = await RoutedHttpAcquisitionSession.StartAsync(
            witness.Request, store, CancellationToken.None);
        Assert.IsNotNull(
            start.Session,
            $"the governed session did not start: {start.Kind} safety={start.LocalSafetyReason} "
            + $"operational={start.OperationalReason} deniedPath={start.DeniedRequestPath}");

        using var session = start.Session;
        var measured = new List<LuxembourgMembershipAnswer>();

        // REQUESTS 2 AND 3: the two membership checks, first, as authorized.
        var saceA = await AskAsync(session, store, plan, renderer, measured, "A/hasOpinion->sace", SaceOf(DraftA));
        var scacA = await AskAsync(session, store, plan, renderer, measured, "A/draftHasTask->scac", ScacOf(DraftA));

        // THE GATE, AS DATA. CrossCheckTargets is empty unless exactly one candidate holds the
        // class, so there is no branch here to invert: forcing this loop to run still sends
        // nothing. LuxembourgOpinionRequestRelationshipDecisionTests pins that emptiness for every
        // ambiguous shape without touching a publisher.
        var decision = LuxembourgOpinionRequestRelationship.DecideFirstPair(
            saceA, scacA, SaceOf(DraftB), ScacOf(DraftB));
        TestContext?.WriteLine($"gate: {decision.Verdict}");

        // REQUESTS 4 AND 5, if the first pair authorized them: the same question on an independent
        // draft of a different family, through the identical path.
        var crossCheck = new List<LuxembourgMembershipAnswer>();
        foreach (var target in decision.CrossCheckTargets)
        {
            crossCheck.Add(await AskAsync(
                session, store, plan, renderer, measured,
                target.Contains("/sace/", StringComparison.Ordinal) ? "B/hasOpinion->sace" : "B/draftHasTask->scac",
                target));
        }

        // AND CONSUMED, NOT ASSUMED. A contradicted or ambiguous cross-check retracts the first
        // pair's identification instead of sitting beside it.
        var concluded = LuxembourgOpinionRequestRelationship.Conclude(decision, crossCheck);
        TestContext?.WriteLine($"conclusion: {concluded.Verdict}");

        var index = JsonSerializer.SerializeToUtf8Bytes(new
        {
            purpose = "E8 diagnostic: which event resource a draft reaches carries jolux:OpinionRequest. "
                + "Measurement only. Not a production relationship, not a closure claim, and it does "
                + "not establish referralDate on the identified resources.",
            observedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
            head = Git(checkout, "rev-parse", "HEAD"),
            dirtyPaths = Git(checkout, "status", "--porcelain"),
            setId = TypedResourcesSetId,
            keyRange = "[(OpinionRequest, IRI), (OpinionRequest, IRI + \"!\")) over (STR(?type), STR(?resource))",
            firstPairVerdict = decision.Verdict,
            conclusion = concluded.Verdict,
            identifiedPredicate = concluded.IdentifiedPredicate,
            crossCheckRequested = decision.CrossCheckTargets.Count,
            crossCheckAnswered = crossCheck.Count,
            root,
            measured,
        }, new JsonSerializerOptions { WriteIndented = true });
        await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var indexPath = Path.Combine(root, "evidence-index.json");
        await File.WriteAllBytesAsync(indexPath, index);
        TestContext?.WriteLine($"evidence: {indexPath}");
        Console.WriteLine(Encoding.UTF8.GetString(index));

        var failed = measured.FirstOrDefault(static value => value.Failure is not null);
        Assert.IsNull(failed?.Failure, $"a membership check did not complete: {failed?.Label} " +
            $"{failed?.Failure}. Evidence: {indexPath}");

        // Deliberately NOT asserted: which candidate won, or that either did. "Neither is an
        // OpinionRequest" and "both are" are real answers the owner asked to be reported, not
        // failures. The run reports and stops; it does not decide.
        Assert.IsNotEmpty(measured, $"nothing was measured. Evidence: {indexPath}");
    }

    private static async Task<LuxembourgMembershipAnswer> AskAsync(
        RoutedHttpAcquisitionSession session,
        ICustodyStore store,
        LuxembourgQueryPlan plan,
        MachineQueryRendererSource renderer,
        List<LuxembourgMembershipAnswer> measured,
        string label,
        string resourceIri)
    {
        long? count = null;
        string? failure = null;
        try
        {
            var (_, transport) = await LuxembourgAcquisitionTestFixture.ObserveOneCountAsync(
                session, store, plan, NewUrn(), TypedResourcesSetId, RangeFor(resourceIri), renderer);
            count = ReadCount(transport.RetainedPayloadBytes.Span);
        }
        catch (Exception error)
        {
            failure = error.GetType().Name + ": " + error.Message;
        }

        var row = new LuxembourgMembershipAnswer(label, resourceIri, count, failure);
        measured.Add(row);
        return row;
    }

    /// <summary>The range containing exactly one resource of the OpinionRequest class.</summary>
    private static LuxembourgQueryPartitionRange RangeFor(string resourceIri) => new(
        "opinion-request-membership",
        new LuxembourgQueryCursor(
            LuxembourgDraftGraphDiscoveryPlan.OpinionRequestClassIri, resourceIri, "", "", "", ""),
        new LuxembourgQueryCursor(
            LuxembourgDraftGraphDiscoveryPlan.OpinionRequestClassIri,
            resourceIri + NextAfterTarget, "", "", "", ""));

    private static string SaceOf(string draftIri) => draftIri + "/evenement/sace/1";

    private static string ScacOf(string draftIri) => draftIri + "/evenement/scac/1";

    private static long ReadCount(ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        var bindings = document.RootElement.GetProperty("results").GetProperty("bindings");
        if (bindings.GetArrayLength() != 1)
        {
            throw new InvalidOperationException(
                $"A count answers in exactly one row; this one had {bindings.GetArrayLength()}.");
        }

        var text = bindings[0].GetProperty("count").GetProperty("value").GetString();
        return long.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : throw new InvalidOperationException($"The count was not a non-negative integer: '{text}'.");
    }

    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
    }

    private static string Git(string checkout, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = checkout, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var result = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? result : throw new InvalidOperationException("git failed.");
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}

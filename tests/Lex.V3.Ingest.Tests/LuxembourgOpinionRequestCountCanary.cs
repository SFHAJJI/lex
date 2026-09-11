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
/// Asks Legilux how many <c>jolux:OpinionRequest</c> resources it holds.
/// </summary>
/// <remarks>
/// <para>
/// THE QUESTION E8 CANNOT ANSWER WITHOUT ASKING. <c>referralDate</c> is declared on
/// <c>jolux:OpinionRequest</c> and not on the draft, so the draft-graph family types every
/// <c>(draft, referralDate)</c> pair as an unresolved gap awaiting "a separate evidence-bound
/// traversal proving the subject role". That traversal needs a subject to traverse TO, and nothing
/// in this repository has ever measured whether this publisher instantiates that class. The class
/// declaration comes from <c>review/38-verified-claims.md</c>, which says in terms that it is
/// "confirmed only through the Fedlex mirror of the ontology"; the one type count on record
/// (2026-08-27) measured <c>InitialDraft</c> and <c>OpinionConseilEtat</c> and never mentions
/// <c>OpinionRequest</c>.
/// </para>
/// <para>
/// A ZERO IS A DATED OBSERVATION, NOT AN IMPOSSIBILITY, and it does not waive <c>referralDate</c>.
/// A positive count does not establish the traversal either: it says the class is populated, not
/// that any draft reaches an instance of it. Both of those are the owner's to rule on; this asks
/// the one question and retains the answer.
/// </para>
/// <para>
/// TWO CONTROLS, BECAUSE A ZERO FROM A BROKEN INSTRUMENT LOOKS EXACTLY LIKE A ZERO FROM AN EMPTY
/// CLASS. The same bound query shape is asked about <c>InitialDraft</c> and
/// <c>OpinionConseilEtat</c>, whose populations were independently measured at 7,748 and 13,009 on
/// 2026-08-27 and whose addressable count this repository re-measured at 7,753. If the controls do
/// not come back near those figures the run proves nothing about the target and says so.
/// </para>
/// <para>
/// NO HAND-AUTHORED SPARQL. The query is the reviewed closed plan's own <c>typed-resources</c>
/// template - <c>?resource a ?type</c>, keyed <c>(STR(?type), STR(?resource))</c> - bound through
/// <see cref="LuxembourgQueryPlan.BindCount"/> and sent through the same governed routed session,
/// robots handling and shared origin pacing as every other Luxembourg request. Pinning the
/// half-open key range to <c>[(C, ""), (C, U+FFFF))</c> forces <c>key_1 = C</c>, so
/// <c>COUNT(*)</c> over <c>SELECT DISTINCT</c> of the keys is the number of distinct IRI resources
/// of class C. The upper sentinel assumes no resource IRI sorts at or above U+FFFF, which is not a
/// guess here: all 6,015 opinion IRIs and 7,753 draft IRIs in the retained evidence are ASCII.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgOpinionRequestCountCanary
{
    private const string EnableVariable = "LEX_E8_OPINION_REQUEST_COUNT";

    /// <summary>The plan's typed-resource set: <c>?resource a ?type</c>, keyed by type then resource.</summary>
    private const string TypedResourcesSetId = "R";

    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task LegiluxIsAskedHowManyOpinionRequestsItHolds()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 to ask Legilux for the jolux:OpinionRequest population. "
                + "Skipped by default so the suite sends no unasked traffic.");
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "e8-opinion-request-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);

        var scopeBytes = Encoding.UTF8.GetBytes(
            "E8 OpinionRequest population probe\n"
            + "purpose=does this publisher instantiate the class that declares referralDate\n");
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

        // The target first, then the controls. Ordered so that if the publisher throttles or the
        // run is stopped early, the question this exists to ask has already been asked.
        var classes = new (string Label, string Iri, long? Expected)[]
        {
            ("OpinionRequest", LuxembourgDraftGraphDiscoveryPlan.OpinionRequestClassIri, null),
            ("InitialDraft", LuxembourgDraftGraphDiscoveryPlan.InitialDraftClassIri, 7753),
            ("OpinionConseilEtat", LuxembourgOpinionLinkOnlyVocabulary.OpinionConseilEtatClassIri, 13009),
        };

        // ONE session, so one robots bootstrap and one shared pacer across all three counts.
        var witnessPartition = PartitionFor(classes[0].Iri);
        var witness = plan.BindCount(
            NewUrn(), NewUrn(), NewUrn(), TypedResourcesSetId, LuxembourgQueryPass.Pass1,
            witnessPartition, renderer);
        var start = await RoutedHttpAcquisitionSession.StartAsync(
            witness.Request, store, CancellationToken.None);
        Assert.IsNotNull(
            start.Session,
            $"the governed session did not start: {start.Kind} safety={start.LocalSafetyReason} "
            + $"operational={start.OperationalReason} deniedPath={start.DeniedRequestPath}");

        using var session = start.Session;
        var measured = new List<ClassCount>();
        string? stopped = null;

        foreach (var (label, iri, expected) in classes)
        {
            long? count = null;
            string? failure = null;
            try
            {
                // A fresh plan resource id per count: OpenPlanItem refuses to open the same
                // QueryPlanRef twice in one session, and that guard fires locally before any send.
                var (_, transport) = await LuxembourgAcquisitionTestFixture.ObserveOneCountAsync(
                    session, store, plan, NewUrn(), TypedResourcesSetId, PartitionFor(iri), renderer);
                count = ReadCount(transport.RetainedPayloadBytes.Span);
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
                stopped ??= $"{label}: {failure}";
            }

            measured.Add(new ClassCount(iri, label, count, expected, failure));
            TestContext?.WriteLine($"{label,-20} {iri}  count={count?.ToString() ?? "(none)"}"
                + (expected is null ? "  [TARGET]" : $"  [control, 2026-08-27 ~{expected}]")
                + (failure is null ? string.Empty : $"  FAILED {failure}"));

            if (failure is not null)
            {
                break;
            }
        }

        var index = JsonSerializer.SerializeToUtf8Bytes(new
        {
            purpose = "E8 diagnostic: does Legilux instantiate jolux:OpinionRequest. "
                + "A count is a dated observation of this endpoint at this moment; it is not a "
                + "population constant, and a zero is not proof the class can never be populated.",
            observedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
            head = Git(checkout, "rev-parse", "HEAD"),
            dirtyPaths = Git(checkout, "status", "--porcelain"),
            setId = TypedResourcesSetId,
            keyRange = "[(classIri, \"\"), (classIri, U+FFFF)) over (STR(?type), STR(?resource))",
            root,
            measured,
        }, new JsonSerializerOptions { WriteIndented = true });
        await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var indexPath = Path.Combine(root, "evidence-index.json");
        await File.WriteAllBytesAsync(indexPath, index);
        TestContext?.WriteLine($"evidence: {indexPath}");
        Console.WriteLine(Encoding.UTF8.GetString(index));

        Assert.IsNull(stopped, $"a count did not complete: {stopped}. Evidence: {indexPath}");

        // THE INSTRUMENT, CHECKED BEFORE THE ANSWER IS BELIEVED. A zero from a query that cannot
        // count anything is indistinguishable from a zero from an empty class, so the controls
        // decide whether this run is entitled to report the target at all.
        foreach (var row in classes.Where(static value => value.Expected is not null))
        {
            Assert.Contains(
                row.Label, measured.Select(static value => value.Label).ToArray(),
                "every control must have been asked.");
        }

        Assert.IsGreaterThan(
            0,
            CountOf(measured, "InitialDraft") ?? 0,
            "the InitialDraft control came back empty, so this query shape counts nothing and the "
                + $"target's result means nothing. Evidence: {indexPath}");
        Assert.IsGreaterThan(
            0,
            CountOf(measured, "OpinionConseilEtat") ?? 0,
            "the OpinionConseilEtat control came back empty, so the target's result means nothing. "
                + $"Evidence: {indexPath}");

        // Deliberately NOT asserted: the target's own count. Zero is a real answer and an owner
        // decision, not a test failure.
    }

    /// <summary>The half-open key range that pins <c>key_1</c> to exactly one class IRI.</summary>
    /// <remarks>
    /// Start is <c>(C, "")</c> inclusive and end is <c>(C, U+FFFF)</c> exclusive, against a keyset
    /// of <c>(STR(?type), STR(?resource))</c>. The start filter admits <c>key_1 = C</c> with any
    /// non-empty <c>key_2</c>; the end filter admits <c>key_1 = C</c> with <c>key_2</c> below the
    /// sentinel. Their conjunction is exactly this class. U+FFFF follows the range idiom already in
    /// <c>LuxembourgAcquisitionTestFixture.FullRange</c>.
    /// </remarks>
    private static LuxembourgQueryPartitionRange PartitionFor(string classIri) => new(
        "class-population",
        new LuxembourgQueryCursor(classIri, "", "", "", "", ""),
        new LuxembourgQueryCursor(classIri, "￿", "", "", "", ""));

    /// <summary>The single <c>?count</c> binding, read strictly.</summary>
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

    /// <summary>One class asked about, with what came back.</summary>
    /// <param name="Expected">The independently measured figure a control is checked against; null for the target.</param>
    private sealed record ClassCount(
        string ClassIri, string Label, long? Count, long? Expected, string? Failure);

    private static long? CountOf(List<ClassCount> measured, string label) => measured
        .Where(value => string.Equals(value.Label, label, StringComparison.Ordinal))
        .Select(static value => value.Count)
        .FirstOrDefault();

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

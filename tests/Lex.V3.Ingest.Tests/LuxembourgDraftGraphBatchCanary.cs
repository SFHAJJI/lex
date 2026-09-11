using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Does the five-predicate draft-graph query avoid SR319 when it is bounded to one batch?
/// </summary>
/// <remarks>
/// <para>
/// THIS RUNS BEFORE THE BATCHING MACHINERY, and the order is the point. Legilux refused this
/// family's query twice at class scope with <c>Virtuoso SR319: Max row length is exceeded</c>, at
/// seven grouped columns and at three. The inventory then answered 7,753 subjects of the same class
/// under a one-column grouping, which removed the reason to assume the engine cannot answer this
/// family at all — but it did not establish that the FIVE-PREDICATE query answers over fifty named
/// drafts. Building deterministic batching on that assumption would be building on a guess.
/// </para>
/// <para>
/// THE BATCH IS THE ONE BATCHING WOULD PRODUCE FIRST: the ordinal-first fifty of the proven
/// inventory retained under #417, not a hand-picked sample. Ordinal order is what the batching stage
/// will use, so if this batch answers, batch one answers.
/// </para>
/// <para>
/// It asserts almost nothing about the answer, for the reason the inventory probe does not either:
/// a refusal is a finding rather than a failure of the probe, and it must be retained and diagnosed
/// without narrowing class scope. What it does fail on is a request sent somewhere this family may
/// not send one, which holds whether the publisher answered or refused.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftGraphBatchCanary
{

    /// <summary>
    /// A REAL enumeration proof, because the citation doors now require one.
    /// </summary>
    /// <remarks>
    /// The run reference and the family key used to be handed to the producer as loose values, which
    /// is how a citation could state an identity instead of carrying one. Both now come off the
    /// proof, whose only door refuses anything but two independently agreeing, custody-verified
    /// passes. <c>AbsenceFixtures.Proof</c> is the same builder the contract tests use and is
    /// memoised, so this costs one assembly for the whole run rather than one per test.
    /// </remarks>
    private const string InventoryFamily = "legilux-initial-draft-inventory";
    private const string EnableVariable = "LEX_E8_BATCH_CANARY";
    private const string LegiluxEndpoint = "https://data.legilux.public.lu/sparqlendpoint";
    private const string DraftPrefix = "http://data.legilux.public.lu/eli/dl/";

    /// <summary>
    /// The ordinal-first fifty subjects of the retained inventory run.
    /// </summary>
    /// <remarks>
    /// Real drafts the publisher answered for, not invented IRIs. An invented one would bind and
    /// render exactly as well and would prove nothing about a class Legilux actually holds. The
    /// shared prefix is factored out only to keep them readable; the suffixes are verbatim.
    /// </remarks>
    private static readonly string[] FirstBatch =
    [
        "pc/2002/215", "pc/2002/221", "pc/2002/231", "pc/2003/23", "pc/2003/24",
        "pc/2004/80", "pc/2005/7", "pc/2006/111", "pc/2006/112", "pc/2008/240",
        "pl/1985/268", "pl/1985/313", "pl/1985/329", "pl/1988/103", "pl/1988/8",
        "pl/1988/96", "pl/1989/60", "pl/1992/194", "pl/1993/68", "pl/1994/72",
        "pl/1996/177", "pl/1999/104", "pl/1999/118", "pl/2000/1", "pl/2000/10",
        "pl/2000/102", "pl/2000/11", "pl/2000/110", "pl/2000/114", "pl/2000/118",
        "pl/2000/119", "pl/2000/120", "pl/2000/121", "pl/2000/122", "pl/2000/123",
        "pl/2000/124", "pl/2000/125", "pl/2000/129", "pl/2000/133", "pl/2000/136",
        "pl/2000/138", "pl/2000/140", "pl/2000/146", "pl/2000/147", "pl/2000/149",
        "pl/2000/150", "pl/2000/153", "pl/2000/154", "pl/2000/155", "pl/2000/16",
    ];

    /// <summary>
    /// The batch this canary asks about: the retained first fifty, or a named selection.
    /// </summary>
    /// <remarks>
    /// <c>LEX_E8_BATCH_DRAFTS</c> takes comma-separated suffixes under the shared prefix, so the
    /// canary can be aimed at a specific subject without inventing one. It exists because the owner
    /// ruling on the long-value cursor requires a canary over THE OFFENDING batch - the one holding
    /// <c>pl/2005/64</c>, whose 2,648-byte titleDraft stopped the acceptance run - and that draft is
    /// not among the ordinal-first fifty this list carries. Unset, the batch is exactly what it was.
    /// </remarks>
    private static string[] BatchIris()
    {
        var named = Environment.GetEnvironmentVariable("LEX_E8_BATCH_DRAFTS");
        var suffixes = string.IsNullOrWhiteSpace(named)
            ? FirstBatch
            : named.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return suffixes.Select(static suffix => DraftPrefix + suffix).ToArray();
    }

    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task TheFivePredicateQueryIsAskedAboutOneBatchAndWhateverCameBackIsRecorded()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 to ask Legilux whether the five-predicate query answers "
                + "over one bounded batch. Skipped by default so the suite sends no unasked traffic.");
        }

        // DISTINCT DRAFTS, not parameters. The plan pads every batch to capacity, so a shorter
        // batch binds the same fifty parameters and asks the publisher about fewer subjects - which
        // is what makes this a measurement of query cost rather than of input shape.
        var distinct = int.TryParse(
            Environment.GetEnvironmentVariable("LEX_E8_BATCH_DISTINCT"), out var requested)
            ? requested
            : LuxembourgDraftGraphDiscoveryPlan.BatchCapacity;
        Assert.IsTrue(
            distinct is > 0 && distinct <= LuxembourgDraftGraphDiscoveryPlan.BatchCapacity,
            "a batch names one to capacity drafts.");
        var batch = BatchIris().Take(distinct).ToArray();

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "e8-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var store = new FileSystemCustodyStore(root);
        var producer = new LuxembourgDraftGraphProducer(store, TimeProvider.System);
        var plan = LuxembourgDraftGraphDiscoveryPlan.Create();

        var result = await producer.RunAsync(
            LuxembourgDraftGraphRunRequest.ForBatch(
                plan, InventoryOver(batch), 0, NewUrn(), RendererSource(checkout)),
            LuxembourgSourceWitness(),
            CancellationToken.None);

        // Holds whether the publisher answered or refused: a request sent in error is a finding even
        // when the answer was no.
        var offending = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var text = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            foreach (var target in RequestTargetsIn(text))
            {
                if (!string.Equals(target, LegiluxEndpoint, StringComparison.Ordinal))
                {
                    offending.Add(Path.GetFileName(file)[..12] + " -> " + target);
                }
            }
        }

        Assert.IsEmpty(
            offending,
            "this canary may contact the Legilux SPARQL endpoint and nothing else: "
            + string.Join("; ", offending.Take(10)));

        var summary = new StringBuilder()
            .AppendLine("e8-batch-canary/1")
            .AppendLine("endpoint=" + LegiluxEndpoint)
            .AppendLine("batch_size=" + batch.Length)
            .AppendLine("delivered=" + result.Delivered)
            .AppendLine("refusal=" + result.Refusal)
            .AppendLine("detail=" + (result.Detail ?? string.Empty))
            .AppendLine("product_requests=" + result.ProductRequestCount)
            .AppendLine("records=" + (result.Records?.Count.ToString() ?? "none"))
            .AppendLine("coverage=" + (result.Coverage?.Describe() ?? "none"))
            .ToString();
        await File.WriteAllTextAsync(Path.Combine(root, "canary-summary.txt"), summary);
        TestContext?.WriteLine(summary);

        Assert.IsTrue(
            result.Delivered || result.Refusal != LuxembourgDraftGraphProductionRefusal.None,
            "a run reports a delivery or a typed refusal, never neither.");

        if (!result.Delivered)
        {
            Assert.Inconclusive(
                "Legilux did not answer the five-predicate query over one batch. The refusal is "
                + "retained and diagnosed without narrowing class scope. Retained under "
                + root + ". Refusal: " + result.Refusal + " " + result.Detail);
        }

        // Every delivered record names a draft this run asked about. The executor already verifies
        // batch membership on the cursor; this reads the same claim off the decoded records, because
        // a record naming a draft outside the batch would mean the partition is not what it says.
        foreach (var record in result.Records!)
        {
            CollectionAssert.Contains(batch, record.DraftIri);
        }

        // THE RELATIONSHIP, NEVER THE NUMBERS. The ruling says remeasure rather than pin, so the
        // only constants asserted here are local: how many drafts this run asked about and how many
        // properties this family asks. 103, 95, 155, 250 and 258 are written to the summary as
        // measurements and asserted nowhere.
        var coverage = result.Coverage!;
        Assert.AreEqual(
            batch.Length * LuxembourgDraftGraphDiscoveryPlan.AskedAbout.Count,
            coverage.CoveredPairCount);
        Assert.AreEqual(
            coverage.CoveredPairCount,
            coverage.PresentPairCount + coverage.DerivedAbsences.Count + coverage.UnresolvedGaps.Count
                + (coverage.DraftsOfUnconfirmedClass.Count
                    * LuxembourgDraftGraphDiscoveryPlan.AskedAbout.Count),
            "every asked pair is present, derived-absent, unresolved, or on a draft whose class "
                + "went unconfirmed - exactly one of the four.");

        // The admitted half and the retained half must account for the whole delivery. The coverage
        // enforces this internally; asserting it here too means the canary reports a delivery it
        // has actually reconciled rather than one it merely received.
        Assert.AreEqual(
            result.Records!.Count + result.RetainedNotAdmitted.Count,
            (int)coverage.Batch.DeliveredRowCount,
            "every delivered row is admitted or retained by name.");

        foreach (var absence in coverage.DerivedAbsences)
        {
            CollectionAssert.Contains(batch, absence.DraftIri);
            Assert.IsEmpty(
                coverage.ValuesFor(absence.DraftIri, absence.PredicateIri),
                "a pair cannot carry both a derived absence and delivered values.");
        }
    }

    /// <summary>An inventory over exactly the drafts this canary sweeps.</summary>
    /// <remarks>
    /// <para>
    /// SYNTHETIC, AND LABELLED AS SUCH. This is not the proven 7,753-member class enumeration; it is
    /// an inventory over the drafts this canary names, so that the run has a population its batch is
    /// genuinely a partition of. The canary measures whether the publisher serves a shape, and that
    /// question does not need the whole class.
    /// </para>
    /// <para>
    /// It replaces a citation assembled from three environment variables. A run can no longer be
    /// handed a citation at all - the batch and the citation are both derived from one inventory
    /// result - which is what closes the pairing that let a run mint absences for subjects the cited
    /// population never contained. The environment variables were that hole wearing a seatbelt.
    /// </para>
    /// <para>
    /// A full acceptance run over the real class does not come through here: it comes from the
    /// proven inventory, through the batch factory, and is reconciled by the terminal cover.
    /// </para>
    /// </remarks>
    private static LuxembourgInitialDraftInventoryResult InventoryOver(IReadOnlyList<string> drafts)
    {
        var profile = LuxembourgInitialDraftInventoryDiscoveryPlan.Create().CreateDeliveryProfile();

        var rows = drafts.Select(draft =>
        {
            var terms = new List<RepeatedEnumerationRdfTerm>
            {
                RepeatedEnumerationRdfTerm.Iri(draft),
                RepeatedEnumerationRdfTerm.Literal(
                    LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
                RepeatedEnumerationRdfTerm.Literal(
                    "1", "http://www.w3.org/2001/XMLSchema#integer", null),
                RepeatedEnumerationRdfTerm.Literal(draft, null, null),
                RepeatedEnumerationRdfTerm.Literal(
                    LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
            };
            return new RepeatedEnumerationRow(terms, terms, terms);
        }).ToArray();

        // THE FAMILY'S OWN PROFILE, and keyed on the subjects. The citation door binds a proof to
        // this family's partition AND to its exact interpretation profile, and derives the proven
        // population from the first canonical-key component. A proof built under the generic fixture
        // profile evidences nothing here - which is exactly how this canary broke: it is gated off,
        // so the authority binding that landed in #540 refused it silently until the owner ordered
        // it run. Only the canonical key comes from the delivery; the terms stay this canary's own.
        var ordered = drafts.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(InventoryFamily, ordered);
        var byDraft = rows.ToDictionary(
            static row => row.Terms[0].Value ?? string.Empty, static row => row, StringComparer.Ordinal);
        var bound = ordered
            .Select((draft, index) => new RepeatedEnumerationRow(
                byDraft[draft].Terms, keys[index], byDraft[draft].Cursor))
            .ToArray();

        return LuxembourgInitialDraftInventoryProducer.DecodeRows(
            bound, profile, proof, "2026-09-10T07:29:37.8950843Z");
    }

    private static BoundMachineRequest LuxembourgSourceWitness()
    {
        var (witnessPlan, planResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(9501);
        return witnessPlan.BindCount(
            planResourceId,
            NewUrn(),
            NewUrn(),
            LuxembourgAcquisitionTestFixture.SubjectsSetId,
            LuxembourgQueryPass.Pass1,
            LuxembourgAcquisitionTestFixture.FullRange(),
            LuxembourgAcquisitionTestFixture.BuildRendererSource(9501)).Request;
    }

    private static IEnumerable<string> RequestTargetsIn(string artifact)
    {
        const string Marker = "\"request_uri\"";
        var index = artifact.IndexOf(Marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var open = artifact.IndexOf('"', index + Marker.Length);
            if (open < 0)
            {
                yield break;
            }

            var close = artifact.IndexOf('"', open + 1);
            if (close < 0)
            {
                yield break;
            }

            yield return artifact[(open + 1)..close];
            index = artifact.IndexOf(Marker, close, StringComparison.Ordinal);
        }
    }

    private static MachineQueryRendererSource RendererSource(string checkout)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            checkout, "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgDraftGraphDiscoveryPlan.cs"));
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                NewUrn(),
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))),
            bytes);
    }

    private static string NewUrn() => "urn:uuid:" + Guid.NewGuid().ToString("D");

    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
    }
}

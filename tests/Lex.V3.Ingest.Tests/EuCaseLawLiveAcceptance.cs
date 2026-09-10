using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// E6's live acceptance proof: real publisher predicates driving link-only judgment text with the
/// granularity disclosed, against the official endpoint rather than a fixture.
/// </summary>
/// <remarks>
/// <para>
/// REL-005's "done when" has three parts, and every other E6 test proves them against rows a test
/// wrote. That is worth having and is not this. A hand-built row carries whatever predicate the
/// fixture author typed, so a family that read the wrong predicate, or that quietly widened
/// granularity, would pass the whole suite. This asks the publisher.
/// </para>
/// <para>
/// SKIPPED BY DEFAULT, like every other live gate here, so the suite does not depend on a third
/// party's uptime or send unasked traffic. It runs only under
/// <see cref="EnableVariable"/>, and only inside the owner's standing authorization for E6 and E8
/// live acceptance work: existing governed routed clients, the executor's own robots handling and
/// shared origin pacing, sequential runs, exact evidence retained, official metadata requests only.
/// </para>
/// <para>
/// WHAT IT MUST NOT DO IS AS IMPORTANT AS WHAT IT DOES. S2-A07 permits case-law metadata and
/// official links and forbids ingesting judgment bodies, so this asserts the negative directly
/// against what the run retained rather than trusting the producer to have no such path: after the
/// run, every retained artifact naming an HTTP target must name the SPARQL endpoint. A future slice
/// that added a body fetch would fail here even if its own tests passed.
/// </para>
/// <para>
/// The acts come from <see cref="EuAppendixASeedMap"/>, which is the checked-in proven CELEX-to-work
/// mapping, rather than from IRIs written here. An invented Cellar identity would make a green run
/// meaningless, and inventing identities is the thing this family exists to refuse.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuCaseLawLiveAcceptance
{
    private const string EnableVariable = "LEX_E6_LIVE_ACCEPTANCE";

    /// <summary>
    /// How many seeds one run asks about. Bounded deliberately: the authorization is for bounded
    /// acceptance work, and the point is to prove the family answers from the publisher, not to
    /// populate a corpus.
    /// </summary>
    private const int SeedCount = 3;

    [TestMethod]
    public async Task RealPublisherPredicatesDriveLinkOnlyJudgmentTextWithGranularityDisclosed()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 for E6's live official acceptance proof. It is skipped by "
                + "default so the suite does not depend on a third party's uptime or send unasked "
                + "traffic.");
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "e6-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        // The REAL store, never a synthetic one: an acceptance run whose evidence lives in memory
        // proves nothing about custody, and the evidence is the deliverable.
        var store = new FileSystemCustodyStore(root);
        var producer = new EuCaseLawLinkProducer(store, TimeProvider.System);

        var seeds = EuAppendixASeedMap.SeedsInCelexOrder.Take(SeedCount).ToArray();
        var acts = seeds.Select(static seed => seed.WorkRoot).ToArray();

        // Every act this run asks about must carry a body scope, and these are acts the pack already
        // declares as held. The scope is the caller's to supply and the run refuses without it.
        var scopes = acts.ToDictionary(
            static act => act, static _ => TargetBodyScope.BodyInScopeHeld, StringComparer.Ordinal);

        var result = await producer.RunAsync(
            new EuCaseLawRunRequest(
                EuCaseLawDiscoveryPlan.Create(),
                acts,
                NewUrn(),
                RendererSource(checkout)),
            scopes,
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuCaseLawLinkProductionRefusal.None, result.Refusal,
            $"the live run must complete: {result.Refusal} {result.Detail}");
        Assert.IsGreaterThan(0, result.ProductRequestCount, "a live run sends real requests.");

        // The run cites its OWN acquisition run, read back out of custody so the artifact names its
        // own schema rather than being compared against the producer's report of it.
        var cited = await store.ReadByDigestAsync(
            result.CompletionEvidenceRef!.Sha256, CancellationToken.None);
        StringAssert.StartsWith(
            Encoding.UTF8.GetString(cited.Span), "lex-http-acquisition-run/1",
            "the links must cite the acquisition RUN, not one request's HTTP evidence.");

        // REL-005's three parts, on every relation the PUBLISHER delivered.
        var relations = acts.SelectMany(result.ForEuWork).ToArray();

        AssertRel005HoldsOnDeliveredRelations(relations);

        // S2-A07's negative, checked against what the run actually retained rather than against the
        // producer's intentions. A judgment body fetched from anywhere would leave its own target
        // behind, and this is the check that would see it.
        var offendingTargets = OffEndpointTargetsIn(root);

        Assert.IsEmpty(
            offendingTargets,
            "this run may contact the official SPARQL endpoint and nothing else; these targets were "
            + "retained instead: " + string.Join("; ", offendingTargets.Take(10)));

        // The transcribable summary, next to the evidence it describes, so the acceptance packet is
        // quoting a file the run wrote rather than a number typed from a console.
        var summary = new StringBuilder()
            .AppendLine("e6-live-acceptance/1")
            .AppendLine("endpoint=" + EuCaseLawLiveEndpoint)
            .AppendLine("acts_asked=" + string.Join(",", result.ActsAskedAbout!.Order(StringComparer.Ordinal)))
            .AppendLine("celex_seeds=" + string.Join(",", seeds.Select(static seed => seed.Celex)))
            .AppendLine("product_requests=" + result.ProductRequestCount)
            .AppendLine("relations=" + relations.Length)
            .AppendLine("ecli_present=" + relations.Count(static value =>
                value.Binding.CaseEcliState == EcliState.EcliPresent))
            .AppendLine("ecli_not_in_this_set=" + relations.Count(static value =>
                value.Binding.CaseEcliState == EcliState.EcliNotInThisSet))
            .AppendLine("ecli_not_applicable=" + relations.Count(static value =>
                value.Binding.CaseEcliState == EcliState.EcliNotApplicable))
            .AppendLine("unrepresentable_rows=" + (result.UnrepresentableRows?.Count ?? 0))
            .AppendLine("completion_evidence=" + result.CompletionEvidenceRef!.Sha256)
            .ToString();
        await File.WriteAllTextAsync(Path.Combine(root, "acceptance-summary.txt"), summary);

        TestContext?.WriteLine(summary);
    }

    /// <summary>
    /// The retained-evidence scan finds the targets it is supposed to find, proven offline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WITHOUT THIS, THE LIVE TEST'S CENTRAL NEGATIVE IS VACUOUS. It asserts that no retained
    /// artifact names a target outside the SPARQL endpoint, and a scan that matched nothing at all
    /// would satisfy that perfectly while proving nothing. The key it reads, <c>request_uri</c>, is
    /// <c>RoutedHttpEvidenceDocument.RequestUri</c> under this contract's
    /// <c>JsonNamingPolicy.SnakeCaseLower</c> — but a naming policy is a thing I can read wrongly,
    /// and a renamed property would silently empty the scan rather than fail anything.
    /// </para>
    /// <para>
    /// So this drives the same producer over the scripted transport into a REAL
    /// <see cref="FileSystemCustodyStore"/> and requires the scan to come back non-empty and pointed
    /// at the endpoint. It runs in every ordinary suite run, which is the point: the live gate is
    /// skipped by default, so its machinery has to be kept honest by something that is not.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheRetainedEvidenceScanFindsTheTargetsItIsSupposedToFind()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "lex-e6-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
            {
                ["CaseLaw"] = EuAcquisitionTestFixture.ScriptFor(
                    "CaseLaw", 1,
                    [EuAcquisitionTestFixture.CaseLawRow(
                        "http://publications.europa.eu/resource/cellar/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                        EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
                        ScanAct,
                        "ECLI:EU:C:2020:559")],
                    EuAcquisitionTestFixture.CaseLawProjection),
            };

            var store = new FileSystemCustodyStore(root);
            var producer = new EuCaseLawLinkProducer(
                store,
                new EuAcquisitionTestFixture.FixedTimeProvider(),
                new EuAcquisitionTestFixture.ClassifyingHandler(scripts));

            var result = await producer.RunAsync(
                new EuCaseLawRunRequest(
                    EuCaseLawDiscoveryPlan.Create(),
                    [ScanAct],
                    NewUrn(),
                    RendererSource(CheckoutRoot())),
                new Dictionary<string, TargetBodyScope>(StringComparer.Ordinal)
                {
                    [ScanAct] = TargetBodyScope.BodyInScopeHeld,
                },
                EuAcquisitionTestFixture.SourceWitness(),
                CancellationToken.None);

            Assert.AreEqual(
                EuCaseLawLinkProductionRefusal.None, result.Refusal,
                $"the scripted run must complete: {result.Refusal} {result.Detail}");

            var targets = new List<string>();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                targets.AddRange(HttpTargetsIn(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file))));
            }

            Assert.IsNotEmpty(
                targets,
                "the scan must actually find request targets, or the live test's negative is vacuous.");
            foreach (var target in targets)
            {
                Assert.AreEqual(
                    EuCaseLawLiveEndpoint, target,
                    "this family asks the SPARQL endpoint and nothing else.");
            }

            // A REAL RUN'S EVIDENCE MUST BE ACCEPTED by the same call the live test makes, or the
            // two could diverge and only the skipped one would know.
            Assert.IsEmpty(OffEndpointTargetsIn(root));

            // AND THE GUARD MUST BE ABLE TO FAIL. This is the reviewer's own injection: a path under
            // the endpoint, which the prefix comparison admitted. It is written into the same
            // directory the scan just passed on, so nothing about the fixture explains the
            // difference except the target itself.
            var planted = Path.Combine(root, "planted-off-endpoint-request");
            await File.WriteAllTextAsync(
                planted,
                "{\"schema\":\"lex-http-logical-request/1\",\"request_uri\":\""
                + EuCaseLawLiveEndpoint + "/judgment-body\"}");

            // THE ACCEPTANCE PREDICATE, driven both ways offline. The scripted run delivers one
            // relation, so the three REL-005 parts hold on real decoded output; and an empty list
            // must be REFUSED, which is the defect the reviewer found - it was a foreach over
            // nothing and passed.
            AssertRel005HoldsOnDeliveredRelations(result.ForEuWork(ScanAct));
            Assert.ThrowsExactly<AssertFailedException>(
                static () => AssertRel005HoldsOnDeliveredRelations([]),
                "an empty answer must not satisfy the acceptance proof.");

            var caught = OffEndpointTargetsIn(root);
            Assert.IsNotEmpty(
                caught,
                "a request to a path UNDER the endpoint is off-endpoint and must be caught.");
            StringAssert.Contains(string.Join(";", caught), "/judgment-body");
            File.Delete(planted);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string ScanAct =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";

    public TestContext? TestContext { get; set; }

    private const string EuCaseLawLiveEndpoint = "https://publications.europa.eu/webapi/rdf/sparql";

    /// <summary>
    /// REL-005's three parts, over relations the publisher actually delivered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXTRACTED SO IT CAN BE PROVEN. Its first version was a <c>foreach</c> over a list nothing
    /// required to be non-empty, so a complete publisher answer carrying zero rows reached a green
    /// result after real requests while proving no predicate, no disposition and no granularity.
    /// Requiring a relation fixes that; putting the requirement inside a gate that is skipped by
    /// default would leave the fix in exactly the condition that produced the defect, which is why
    /// this is a method an ungated test drives both ways.
    /// </para>
    /// <para>
    /// An observed empty set stays honest evidence - the live test writes it to the summary either
    /// way - but it cannot be what an acceptance proof rests on.
    /// </para>
    /// </remarks>
    internal static void AssertRel005HoldsOnDeliveredRelations(
        IReadOnlyList<EuCaseLawLinkRelation> relations)
    {
        Assert.IsNotEmpty(
            relations,
            "an acceptance proof of REL-005's three parts requires at least one delivered relation; "
            + "an empty answer is evidence, not acceptance.");

        foreach (var relation in relations)
        {
            // Part one: a real CDM predicate this family asked for, which is the PINNED SET and not
            // one chosen member of it. The first draft of this assertion named
            // case-law_interpretes_resource_legal alone and was wrong: the family asks a union of
            // five, and the plan's own measurement records work_cites_work at 2,257 against
            // interpretes at 74 on a single act. The live answer agrees - every row of the 2,052
            // retained came back under work_cites_work - so an assertion naming one predicate would
            // have failed a correct delivery.
            CollectionAssert.Contains(
                EuCaseLawDiscoveryPlan.PinnedPredicatesInOrder().ToArray(),
                relation.PredicateUri,
                "part one: the edge carries a real CDM predicate this family pinned and asked for.");
            Assert.AreEqual(
                EuJudgmentBodyDisposition.LinkOnlyNeverHeldOrFetched,
                relation.Binding.JudgmentBodyDisposition,
                "part two: judgment text is link only, and the disposition says so on the fact.");
            Assert.AreEqual(
                EuCaseLawGranularity.ActLevel,
                relation.Binding.Granularity,
                "part three: granularity is disclosed as act level, never article level.");

            // S2-A04 and REL-005's identity rule: the three states are exhaustive, and an ECLI
            // exists on the fact only where the publisher actually declared one. This is the
            // assertion that would catch an invented ECLI, which is the failure the ledger names.
            switch (relation.Binding.CaseEcliState)
            {
                case EcliState.EcliPresent:
                    Assert.IsNotNull(
                        relation.Binding.CaseEcli(),
                        "EcliPresent must carry the publisher's own literal.");
                    break;
                case EcliState.EcliNotInThisSet:
                case EcliState.EcliNotApplicable:
                    Assert.IsNull(
                        relation.Binding.CaseEcli(),
                        "an ECLI the publisher did not declare is never minted.");
                    break;
                default:
                    Assert.Fail($"unexpected ECLI state {relation.Binding.CaseEcliState}.");
                    break;
            }
        }
    }

    /// <summary>
    /// Every retained request target that is not EXACTLY the official endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXACT, NOT A PREFIX, and the difference is not pedantry. The first version compared with
    /// <c>StartsWith</c>, and the reviewer injected
    /// <c>https://publications.europa.eu/webapi/rdf/sparql/judgment-body</c> into the retained
    /// evidence: it starts with the endpoint, so the guard admitted the one request shape it exists
    /// to forbid, and the focused class still passed. A prefix comparison over a URI is a guard
    /// against typos, not against a path.
    /// </para>
    /// <para>
    /// Extracted so a regression can drive it over a directory a test wrote. A guard that is only
    /// ever exercised by a gate skipped in CI is a guard nobody has seen fail.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> OffEndpointTargetsIn(string root)
    {
        var offending = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            foreach (var target in HttpTargetsIn(text))
            {
                if (!string.Equals(target, EuCaseLawLiveEndpoint, StringComparison.Ordinal))
                {
                    offending.Add(Path.GetFileName(file) + " -> " + target);
                }
            }
        }

        return offending;
    }

    /// <summary>
    /// Every absolute http(s) target mentioned by one retained artifact.
    /// </summary>
    /// <remarks>
    /// Deliberately over-broad rather than parsing a known artifact shape: the point is to notice a
    /// request this test's author did not anticipate, and a scan that only understood the shapes we
    /// already send could not do that. IRIs the publisher NAMES in its answer are data, not targets,
    /// so only artifacts recording a request are scanned — those are the ones carrying a request
    /// line, and a payload body naming <c>cellar</c> IRIs is not one.
    /// </remarks>
    private static IEnumerable<string> HttpTargetsIn(string artifact)
    {
        const string RequestMarker = "\"request_uri\"";
        var index = artifact.IndexOf(RequestMarker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var open = artifact.IndexOf('"', index + RequestMarker.Length);
            var colon = artifact.IndexOf(':', index + RequestMarker.Length);
            if (open < 0 || colon < 0 || open < colon)
            {
                yield break;
            }

            var close = artifact.IndexOf('"', open + 1);
            if (close < 0)
            {
                yield break;
            }

            yield return artifact[(open + 1)..close];
            index = artifact.IndexOf(RequestMarker, close, StringComparison.Ordinal);
        }
    }

    private static MachineQueryRendererSource RendererSource(string checkout)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            checkout, "src/Lex.V3.Contracts/Source/Europe/EuCaseLawDiscoveryPlan.cs"));
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                NewUrn(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))),
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

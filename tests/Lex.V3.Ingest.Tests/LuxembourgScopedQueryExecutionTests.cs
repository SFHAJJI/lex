using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

public sealed partial class LuxembourgQueryExecutionAdapterTests
{
    [TestMethod]
    public async Task TwoDisjointDeclaredScopesContributeTheirOwnRowsAndAllRelationProofs()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var (families, members, subjects) = DisjointScopeRequests();
        var adapter = new LuxembourgQueryExecutionAdapter(store,
            NewExecutor(store, DisjointScopeHandler(families, subjects)), profile);
        var result = await adapter.RunScopedAsync(families, members,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);
        Assert.IsNull(result.Refusal, result.Refusal?.Detail);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
        CollectionAssert.AreEquivalent(subjects, result.ResourceObservationSubjects.ToArray());
        foreach (var relation in result.RelationFamilyAcquisitions)
        {
            Assert.AreEqual(LuxembourgRelationFamilyAcquisitionState.AcquiredComplete, relation.State);
            Assert.IsNull(relation.CompletionEvidence, "One member cannot stand in for the complete scope.");
            CollectionAssert.AreEquivalent(new[] { "first-g", "second-g" },
                relation.CompletionProofs.Select(proof => proof.FamilyKey).ToArray());
        }
        Assert.IsNotNull(result.ScopeManifestReceipt);
        var assertions = result.FamilyOutcomes.Where(value => value.FamilyKey.EndsWith("-a", StringComparison.Ordinal))
            .Select(value => value.Proof!).ToArray();
        var held = LuxembourgProvenResourceObservations.RequireAllProven(assertions, []);
        Assert.IsNull(held.AssertionFamilyProof, "A single proof must not represent the union.");
        CollectionAssert.AreEqual(new[] { "first-a", "second-a" },
            held.AssertionFamilyProofs.Select(proof => proof.FamilyKey).ToArray());
        var first = assertions[0];
        assertions[0] = assertions[1];
        Assert.AreSame(first, held.AssertionFamilyProofs[0], "The retained member list must be copied.");
        var relationProof = result.FamilyOutcomes.Single(value => value.FamilyKey == "first-g").Proof!;
        foreach (var bad in new IReadOnlyList<AbsenceFamilyEnumerationProof>[]
            { [], [first, first], [first, null!], [first, relationProof] })
        {
            Assert.ThrowsExactly<ArgumentException>(() => LuxembourgProvenResourceObservations.RequireAllProven(bad, []));
            Assert.ThrowsExactly<ArgumentException>(() => LuxembourgRelationFamilyAcquisition.CompleteAll("urn:test:relation", bad));
        }
    }

    [TestMethod]
    [DataRow("second-s")]
    [DataRow("second-a")]
    [DataRow("second-g")]
    public async Task ARefusedDeclaredScopeMemberCannotLeaveACompleteSubset(string refusedId)
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var (families, members, subjects) = DisjointScopeRequests();
        var adapter = new LuxembourgQueryExecutionAdapter(store,
            NewExecutor(store, DisjointScopeHandler(families, subjects, refusedId)), profile);
        var result = await adapter.RunScopedAsync(families, members,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);
        Assert.IsNotNull(result.Refusal);
        Assert.IsNull(result.ScopeManifestReceipt, "Incomplete declared scope must not reach reduction.");
        Assert.IsNull(result.CorpusRecordSet);
        Assert.IsTrue(result.RelationFamilyAcquisitions.All(value =>
            value.State != LuxembourgRelationFamilyAcquisitionState.AcquiredComplete),
            "An early scoped refusal must not publish a relation union that was never reopened.");
    }

    [TestMethod]
    [DataRow("missing-member")]
    [DataRow("empty-scope")]
    [DataRow("unassigned-family")]
    [DataRow("wrong-set")]
    [DataRow("foreign-plan")]
    [DataRow("foreign-plan-content")]
    [DataRow("misaligned-range")]
    [DataRow("overlap")]
    [DataRow("partial-subject")]
    [DataRow("duplicate-designation")]
    [DataRow("foreign-cover-id")]
    [DataRow("foreign-cover-range")]
    [DataRow("colliding-cover-leaf")]
    public async Task ADeclaredScopeMustHaveAlignedDistinctMembersBeforeAnyTraffic(string defect)
    {
        var (profile, _, _) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var (families, members, _) = DisjointScopeRequests();
        var changed = families.ToArray();
        if (defect == "empty-scope") { changed = []; members = []; }
        else if (defect == "unassigned-family") changed = [.. changed,
            (changed[0].Request with { Partition = new("unassigned", changed[0].Request.Partition.StartInclusive,
                changed[0].Request.Partition.EndExclusive) }, changed[0].Witness, null)];
        else if (defect == "missing-member") changed = changed[..^1];
        else if (defect == "wrong-set") changed[1] = (changed[1].Request with { SetId = "G" }, changed[1].Witness, null);
        else if (defect == "foreign-plan") changed[3] = (changed[3].Request with
            { InvariantPlanResourceId = NewUrn() }, changed[3].Witness, null);
        else if (defect == "foreign-plan-content") changed[3] = (changed[3].Request with
            { InvariantPlan = LuxembourgAcquisitionTestFixture.BuildInvariantPlan(2).Plan }, changed[3].Witness, null);
        if (defect == "misaligned-range")
            changed[1] = (changed[1].Request with { Partition = new("first-a",
                changed[3].Request.Partition.StartInclusive, changed[3].Request.Partition.EndExclusive) }, changed[1].Witness, null);
        if (defect == "overlap")
            for (var i = 3; i < 6; i++) changed[i] = (changed[i].Request with
                { Partition = new(changed[i].Request.Partition.PartitionId,
                    changed[0].Request.Partition.StartInclusive, changed[0].Request.Partition.EndExclusive) }, changed[i].Witness, null);
        if (defect == "partial-subject")
            for (var i = 0; i < 3; i++) changed[i] = (changed[i].Request with
                { Partition = new(changed[i].Request.Partition.PartitionId,
                    new(changed[i].Request.Partition.StartInclusive.Key1, "partial", "", "", "", ""),
                    changed[i].Request.Partition.EndExclusive) }, changed[i].Witness, null);
        if (defect == "duplicate-designation") members[1] = members[0];
        if (defect.StartsWith("foreign-cover-", StringComparison.Ordinal))
        {
            var root = changed[0].Request.Partition;
            var foreign = new LuxembourgQueryPartitionRange(defect == "foreign-cover-id" ? "foreign" : root.PartitionId,
                defect == "foreign-cover-range" ? changed[3].Request.Partition.StartInclusive : root.StartInclusive,
                defect == "foreign-cover-range" ? changed[3].Request.Partition.EndExclusive : root.EndExclusive);
            changed[0] = (changed[0].Request, changed[0].Witness, LuxembourgPartitionChain.Root(foreign));
        }
        if (defect == "colliding-cover-leaf")
        {
            var root = changed[0].Request.Partition;
            changed[0] = (changed[0].Request, changed[0].Witness, LuxembourgPartitionChain.Root(root).SplitLeaf(root.PartitionId,
                new(root.StartInclusive.Key1 + "!", "", "", "", "", ""), "second-a", "other-leaf"));
        }
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, NoSendHandler()), profile);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => adapter.RunScopedAsync(
            changed, members, DocumentFetchRendererSource(), CancellationToken.None));
    }

    [TestMethod]
    public async Task ProvenRelationMembersWhoseCustodyFailsReopeningCannotRemainComplete()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new LaterCorruptingCustodyStore(new InMemoryCustodyStore(),
            CustodyDigest.Of(System.Text.Encoding.UTF8.GetBytes(RelationAssertionsRowsJson())));
        var (families, members, subjects) = DisjointScopeRequests();
        families = families.OrderBy(value => value.Request.SetId == "G" ? 0 : 1).ToList();
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store,
            DisjointScopeHandler(families, subjects, onFamilyStarting: key =>
            { if (key == "first-s") store.Corrupt = true; })), profile);
        var result = await adapter.RunScopedAsync(families, members,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);
        Assert.IsTrue(result.FamilyOutcomes.All(value => value.Kind == LuxembourgFamilyEnumerationOutcomeKind.Proven));
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ResourceObservationRowsNotVerified, result.Refusal.Code);
        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNull(result.CorpusRecordSet);
        Assert.IsTrue(result.RelationFamilyAcquisitions.All(value =>
            value.State != LuxembourgRelationFamilyAcquisitionState.AcquiredComplete));
    }

    private sealed class LaterCorruptingCustodyStore(ICustodyStore inner, string targetDigest) : ICustodyStore
    {
        public bool Corrupt { get; set; }
        public Task<DurableBlobWriteReceipt> CreateAsync(ReadOnlyMemory<byte> bytes, CustodyClass custodyClass,
            CancellationToken cancellationToken) => inner.CreateAsync(bytes, custodyClass, cancellationToken);
        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            ReadByDigestAsync(reference.ContentSha256, cancellationToken);
        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            Corrupt && contentSha256 == targetDigest
                ? Task.FromResult<ReadOnlyMemory<byte>>("corrupted"u8.ToArray())
                : inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }

    private static (List<(LuxembourgPartitionRunRequest Request, BoundMachineRequest Witness, LuxembourgPartitionChain? Cover)> Families,
        LuxembourgScopePartitionFamilies[] Members, string[] Subjects) DisjointScopeRequests()
    {
        var (plan, planId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var renderer = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var families = new List<(LuxembourgPartitionRunRequest, BoundMachineRequest, LuxembourgPartitionChain?)>();
        var subjects = new[] { "http://data.legilux.public.lu/eli/etat/leg/loi/2000/01/01/a/jo",
            "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/b/jo" };
        for (var member = 0; member < 2; member++)
        {
            var prefix = member == 0 ? "first" : "second";
            foreach (var set in new[] { "S", "A", "G" })
            {
                var range = new LuxembourgQueryPartitionRange(prefix + "-" + set.ToLowerInvariant(),
                    new(subjects[member], "", "", "", "", ""),
                    new(subjects[member][..^1] + "p", "", "", "", "", ""));
                families.Add((new(plan, planId, set, range, renderer),
                    plan.BindCount(planId, NewUrn(), NewUrn(), set, LuxembourgQueryPass.Pass1, range, renderer).Request, null));
            }
        }
        return (families, [new("first-s", "first-a", "first-g"), new("second-s", "second-a", "second-g")], subjects);
    }

    private static HttpMessageHandler DisjointScopeHandler(
        IReadOnlyList<(LuxembourgPartitionRunRequest Request, BoundMachineRequest Witness, LuxembourgPartitionChain? Cover)> families,
        string[] subjects, string? refusedId = null, Action<string>? onFamilyStarting = null)
    {
        var replies = new List<(bool Robots, string Body)>();
        var starts = new Dictionary<int, string>();
        foreach (var (request, _, _) in families)
        {
            starts.Add(replies.Count, request.Partition.PartitionId);
            var refused = request.Partition.PartitionId == refusedId;
            replies.Add((true, refused ? "User-agent: *\nDisallow: /\n" : "User-agent: *\nAllow: /\n"));
            if (refused) continue;
            var subject = subjects[request.Partition.PartitionId.StartsWith("first", StringComparison.Ordinal) ? 0 : 1];
            var rows = request.SetId switch
            {
                "S" => LuxembourgAcquisitionTestFixture.RowsJson(subject),
                "A" => AssertionRowsJson((subject, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "iri", "", ""),
                    (subject, RdfType, JoluxAct, "iri", "", "")),
                _ => RelationAssertionsRowsJson(),
            };
            var empty = request.SetId switch
            {
                "S" => LuxembourgAcquisitionTestFixture.RowsJson(Array.Empty<string>()),
                "A" => AssertionRowsJson(),
                _ => RelationAssertionsRowsJson(),
            };
            var count = request.SetId == "S" ? 1 : request.SetId == "A" ? 2 : 0;
            for (var pass = 0; pass < 2; pass++)
            {
                replies.Add((false, LuxembourgAcquisitionTestFixture.CountJson(count)));
                replies.Add((false, rows));
                if (count != 0) replies.Add((false, empty));
            }
        }
        return new LuxembourgAcquisitionTestFixture.SequencedHandler((ordinal, request) =>
        {
            if (starts.TryGetValue(ordinal, out var key)) onFamilyStarting?.Invoke(key);
            return replies[ordinal].Robots ? TextResponse(request, replies[ordinal].Body)
                : LuxembourgAcquisitionTestFixture.JsonResponse(request, replies[ordinal].Body);
        });
    }
}

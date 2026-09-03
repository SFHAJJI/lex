using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class MachineQueryPlanContractTests
{
    private const string Target = "https://data.legilux.public.lu/sparqlendpoint";
    private static readonly byte[] PostBody =
        Encoding.UTF8.GetBytes(
            "query=SELECT%20%2A%20WHERE%20%7B%20%3Fs%20%3Fp%20%3Fo%20%7D%20LIMIT%20500");

    [TestMethod]
    public void PlanContainsOnlyDigestClosedMachineArtifactsAndNoInlineQueryValues()
    {
        var plan = PostPlan();
        var expectedBodyLength = (long?)PostBody.LongLength;
        var expectedBodySha256 = Sha256(PostBody);
        var expectedTargetLength = (long)"/sparqlendpoint".Length;
        var expectedTargetSha256 = Sha256(Encoding.ASCII.GetBytes("/sparqlendpoint"));

        Assert.AreEqual(MachineQueryPlan.SchemaId, plan.Schema);
        Assert.AreEqual(HttpRequestMethod.Post, plan.Method);
        Assert.AreEqual(Target, plan.TargetOriginAndPath);
        Assert.AreEqual(expectedTargetLength, plan.ExpectedRequestTargetLength);
        Assert.AreEqual(expectedTargetSha256, plan.ExpectedRequestTargetSha256);
        Assert.AreEqual(ParameterRef(), plan.OrderedParameterSet);
        Assert.AreEqual(ParameterRef(), plan.PartitionBinding.RegistryRef);
        Assert.AreEqual("jolux-resource-keyset", plan.PartitionBinding.MemberKey);
        Assert.AreEqual(MachineQueryInputMode.RendererInputs, plan.InputMode);
        Assert.AreEqual(RendererSource(), plan.RendererSourceRef);
        Assert.AreEqual(MachineResponseCardinalityKind.BoundedRowSetPage, plan.ResponseCardinality.Kind);
        Assert.AreEqual(500, plan.ResponseCardinality.RowLimit);
        Assert.AreEqual<long?>(
            expected: 13_207_454,
            actual: plan.ResponseCardinality.ExpectedPartitionRowCount);
        Assert.IsTrue(
            plan.ResponseCardinality.ExpectedPartitionRowCountEvidenceRef == CountEvidenceRef());
        Assert.AreEqual(expectedBodyLength, plan.ExpectedRequestBodyLength);
        Assert.AreEqual(expectedBodySha256, plan.ExpectedRequestBodySha256);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Schema",
                "QueryFamilyRef",
                "RendererProfileRef",
                "RendererSourceRef",
                "Method",
                "TargetOriginAndPath",
                "ExpectedRequestTargetLength",
                "ExpectedRequestTargetSha256",
                "ResponseCardinality",
                "ContentType",
                "Charset",
                "InputMode",
                "OrderedParameterSet",
                "PartitionBinding",
                "ExpectedRequestBodyLength",
                "ExpectedRequestBodySha256",
            },
            typeof(MachineQueryPlan).GetProperties().Select(static property => property.Name).ToArray());
        Assert.IsFalse(typeof(MachineQueryPlan).GetProperties().Any(static property =>
            property.Name.Contains("QueryText", StringComparison.Ordinal) ||
            property.Name.Contains("User", StringComparison.Ordinal) ||
            property.Name.Contains("ParameterValue", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void BinderValidatesPlanAndInputArtifactsThenRendersExactlyOnce()
    {
        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
        var renderer = Renderer(plan, Target, PostBody);

        var bound = MachineQueryBinder.BindForSend(plan, planRef, Parameters(), renderer);

        Assert.AreEqual(1, renderer.RenderCount);
        Assert.AreEqual(Target, bound.RequestedUri);
        CollectionAssert.AreEqual(PostBody, bound.CopyRequestBody());
        Assert.AreEqual(planRef, bound.RenderReceipt.QueryPlanRef);
        Assert.AreEqual(ParameterRef(), bound.RenderReceipt.OrderedParameterSetRef);
        Assert.AreEqual(MachineQueryInputMode.RendererInputs, bound.RenderReceipt.InputMode);
        Assert.AreEqual(HttpRequestMethod.Post, bound.RenderReceipt.Method);
        Assert.AreEqual("/sparqlendpoint".Length, bound.RenderReceipt.RequestTargetLength);
        Assert.AreEqual(
            Sha256(Encoding.ASCII.GetBytes("/sparqlendpoint")),
            bound.RenderReceipt.RequestTargetSha256);
        Assert.AreEqual(PostBody.Length, bound.RenderReceipt.RequestBodyLength);
        Assert.AreEqual(Sha256(PostBody), bound.RenderReceipt.RequestBodySha256);

        var escaped = bound.CopyRequestBody();
        escaped[0] ^= 0xff;
        CollectionAssert.AreEqual(PostBody, bound.CopyRequestBody());
    }

    [TestMethod]
    public void BinderCapabilityAndOpenedSnapshotHaveOneClosedPublicBoundary()
    {
        Assert.IsTrue(typeof(BoundMachineRequest).IsAbstract);
        var capabilityConstructor = typeof(BoundMachineRequest)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single();
        Assert.IsTrue(capabilityConstructor.IsFamilyAndAssembly);
        Assert.AreEqual(0, capabilityConstructor.GetParameters().Length);

        CollectionAssert.AreEqual(
            new[]
            {
                "method instance CopyRequestBody(): Byte[]",
                "property instance RenderReceipt: MachineQueryRenderReceipt",
                "property instance RequestedUri: String",
            },
            PublicSurface(typeof(BoundMachineRequest)));

        Assert.IsTrue(typeof(OpenedMachineRequest).IsSealed);
        Assert.AreEqual(0, typeof(OpenedMachineRequest).GetConstructors().Length);
        CollectionAssert.AreEqual(
            new[]
            {
                "method instance CopyOrderedParameterSetCanonicalBytes(): Byte[]",
                "method instance CopyQueryPlanCanonicalBytes(): Byte[]",
                "method instance CopyRenderReceiptCanonicalBytes(): Byte[]",
                "method instance CopyRequestBody(): Byte[]",
                "property instance OrderedParameterSetRef: SourceArtifactRef",
                "property instance QueryPlanRef: SourceArtifactRef",
                "property instance RenderReceipt: MachineQueryRenderReceipt",
                "property instance RenderReceiptRef: SourceArtifactRef",
                "property instance RequestedUri: String",
            },
            PublicSurface(typeof(OpenedMachineRequest)));

        CollectionAssert.AreEqual(
            new[]
            {
                "method static OpenForSend(BoundMachineRequest): OpenedMachineRequest",
                "method static OpenForSendAsync(BoundMachineRequest, IMachineQueryArtifactResolver, CancellationToken): Task`1",
                "method static OpenIdentity(BoundMachineRequest): BoundMachineRequestIdentity",
                "method static VerifyOffline(MachineQueryPlan, SourceArtifactRef, MachineQueryInputArtifact, MachineQueryRenderReceipt, IMachineQueryRenderer): Void",
            },
            PublicSurface(typeof(MachineQueryBinder)));
        Assert.IsTrue(typeof(BoundMachineRequestIdentity).IsSealed);
        Assert.AreEqual(0, typeof(BoundMachineRequestIdentity).GetConstructors().Length);
        CollectionAssert.AreEqual(
            new[]
            {
                "property instance QueryPlanRef: SourceArtifactRef",
                "property instance RenderReceipt: MachineQueryRenderReceipt",
                "property instance RequestedUri: String",
            },
            PublicSurface(typeof(BoundMachineRequestIdentity)));
        var identityFixture = StableCapability();
        var renderCountBeforeIdentityOpen = identityFixture.Renderer.RenderCount;
        var identity = MachineQueryBinder.OpenIdentity(identityFixture.Capability);
        Assert.AreEqual(identityFixture.Capability.RequestedUri, identity.RequestedUri);
        Assert.AreEqual(identityFixture.Capability.RenderReceipt, identity.RenderReceipt);
        Assert.AreEqual(identityFixture.PlanRef, identity.QueryPlanRef);
        Assert.AreEqual(renderCountBeforeIdentityOpen, identityFixture.Renderer.RenderCount);
        CollectionAssert.AreEqual(
            new[]
            {
                "method instance ReopenAsync(SourceArtifactRef, CancellationToken): Task`1",
                "method instance RetainAndReopenAsync(SourceArtifactRef, ReadOnlyMemory`1, CancellationToken): Task`1",
            },
            PublicSurface(typeof(IMachineQueryArtifactResolver)));
        Assert.IsFalse(typeof(MachineQueryBinder).GetMethod(
            nameof(MachineQueryBinder.BindForSend),
            BindingFlags.Public | BindingFlags.Static) is not null);

        var concreteCapabilities = typeof(MachineQueryBinder)
            .GetNestedTypes(BindingFlags.NonPublic)
            .Where(type => typeof(BoundMachineRequest).IsAssignableFrom(type))
            .ToArray();
        Assert.AreEqual(1, concreteCapabilities.Length);
        Assert.IsTrue(concreteCapabilities[0].IsNestedPrivate);
        Assert.IsTrue(concreteCapabilities[0].IsSealed);
    }

    [TestMethod]
    public async Task AsyncOpenReopensAllEightTransitiveArtifactsBeforeRendering()
    {
        var fixture = AsyncCapability();
        fixture.Events.Clear();
        var resolver = new RecordingArtifactResolver(fixture.ExternalArtifacts, fixture.Events);

        var opened = await MachineQueryBinder.OpenForSendAsync(
            fixture.Capability,
            resolver,
            CancellationToken.None);

        Assert.AreEqual(2, fixture.Renderer.RenderCount);
        CollectionAssert.AreEqual(
            Enumerable.Repeat("open", 8).Append("render").ToArray(),
            fixture.Events);
        CollectionAssert.AreEqual(
            new[]
            {
                opened.RenderReceiptRef,
                fixture.PlanRef,
                fixture.Input.ArtifactRef,
                fixture.RendererProfileRef,
                fixture.RendererSourceRef,
                fixture.ContentTypeRegistryRef,
                fixture.QueryRegistryRef,
                fixture.ParameterProvenanceRef,
            },
            resolver.OpenedReferences.ToArray());
    }

    [TestMethod]
    public async Task RendererWithoutBytesRefusesRatherThanReopeningByReference()
    {
        // Item 1b, Decision 75's closure. Before this item a renderer producing nothing fell back
        // to reopen by reference, which is exactly the path that let the product route stay green
        // against a recording double while failing against a real, unseeded store. That fallback
        // is gone: absent renderer bytes now refuse the send outright, so a route that depends on
        // the fallback fails loudly at open rather than silently depending on custody it never
        // produced.
        var fixture = AsyncCapability(rendererProducesItsBytes: false);

        // The fixture's own construction already rendered once, synchronously, to compute the
        // receipt BindForSend freezes. That render is not the one this test is about; the count
        // right after construction is the baseline the refusal must not move past.
        var renderCountBeforeOpen = fixture.Renderer.RenderCount;
        var resolver = new RecordingArtifactResolver(fixture.ExternalArtifacts, fixture.Events);

        var thrown = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            MachineQueryBinder.OpenForSendAsync(
                fixture.Capability,
                resolver,
                CancellationToken.None));

        StringAssert.Contains(thrown.Message, "must produce its own bytes");
        CollectionAssert.DoesNotContain(
            resolver.RetainedReferences.ToArray(),
            fixture.RendererProfileRef,
            "a renderer that produces no bytes cannot have any retained on its behalf");
        Assert.AreEqual(
            renderCountBeforeOpen,
            fixture.Renderer.RenderCount,
            "a refused send must never reach OpenForSendAsync's own render pass");
    }

    [TestMethod]
    public async Task ARendererMissingOnlyItsProfileBytesIsRefusedNamingTheProfile()
    {
        // Isolates the profile's own requiresProducerBytes tag from the source's. Both
        // artifacts always being absent or present together, as the two capability-level
        // fixtures do, cannot tell these two tags apart: whichever is checked first masks the
        // other. This fixture's ExternalArtifacts dictionary carries real bytes for both
        // artifacts by reference, so a mutation that stops requiring the profile's own bytes
        // would silently succeed via bare reopen rather than throwing, which is exactly the
        // regression this test exists to catch.
        var fixture = AsyncCapability(rendererProducesItsBytes: true);
        var renderer = new ObservedRenderer(
            fixture.RendererProfileRef,
            fixture.RendererSourceRef,
            Target,
            PostBody,
            fixture.Events,
            profileBytes: null,
            sourceBytes: fixture.ExternalArtifacts[fixture.RendererSourceRef]);
        var capability = MachineQueryBinder.BindForSend(
            fixture.Plan, fixture.PlanRef, fixture.Input, renderer);
        var resolver = new RecordingArtifactResolver(fixture.ExternalArtifacts, fixture.Events);

        var thrown = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            MachineQueryBinder.OpenForSendAsync(capability, resolver, CancellationToken.None));

        StringAssert.Contains(thrown.Message, "renderer profile");
        StringAssert.Contains(thrown.Message, "must produce its own bytes");
    }

    [TestMethod]
    public async Task ARendererMissingOnlyItsSourceBytesIsRefusedNamingTheSource()
    {
        // The paired isolation for the source's own tag.
        var fixture = AsyncCapability(rendererProducesItsBytes: true);
        var renderer = new ObservedRenderer(
            fixture.RendererProfileRef,
            fixture.RendererSourceRef,
            Target,
            PostBody,
            fixture.Events,
            profileBytes: fixture.ExternalArtifacts[fixture.RendererProfileRef],
            sourceBytes: null);
        var capability = MachineQueryBinder.BindForSend(
            fixture.Plan, fixture.PlanRef, fixture.Input, renderer);
        var resolver = new RecordingArtifactResolver(fixture.ExternalArtifacts, fixture.Events);

        var thrown = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            MachineQueryBinder.OpenForSendAsync(capability, resolver, CancellationToken.None));

        StringAssert.Contains(thrown.Message, "renderer source");
        StringAssert.Contains(thrown.Message, "must produce its own bytes");
    }

    [TestMethod]
    public async Task RendererProducingItsBytesSendsAgainstAStoreThatNeverHeldThem()
    {
        var fixture = AsyncCapability(rendererProducesItsBytes: true);
        var resolver = new RecordingArtifactResolver(
            fixture.ExternalArtifacts,
            fixture.Events,
            absentRefs: new HashSet<SourceArtifactRef>
            {
                fixture.RendererProfileRef,
                fixture.RendererSourceRef,
            });

        // Decision 75, stated as the only thing that proves it: a store that would throw for both
        // renderer artifacts if they were merely reopened. Passing means the run put them there
        // itself. This is the shape that a pre-seeding test double cannot distinguish, and the
        // shape whose absence made the product route fail against a real FileSystemCustodyStore
        // while every double-backed test stayed green.
        var opened = await MachineQueryBinder.OpenForSendAsync(
            fixture.Capability,
            resolver,
            CancellationToken.None);

        Assert.AreEqual(Target, opened.RequestedUri);
        CollectionAssert.Contains(
            resolver.RetainedReferences.ToArray(),
            fixture.RendererProfileRef);
        CollectionAssert.Contains(
            resolver.RetainedReferences.ToArray(),
            fixture.RendererSourceRef);
    }

    [TestMethod]
    public void BytesThatDoNotCarryTheirReferencesDigestAreRefusedAtBind()
    {
        var fixture = AsyncCapability(rendererProducesItsBytes: true);
        var wrongBytes = Encoding.UTF8.GetBytes("renderer-profile/2\n");

        // The digest stays the authority. Producing bytes is a witness to the frozen reference,
        // never a way to redefine what it names, so a renderer offering other bytes cannot bind at
        // all rather than being caught later at reopen.
        var renderer = new ObservedRenderer(
            fixture.RendererProfileRef,
            fixture.RendererSourceRef,
            Target,
            PostBody,
            fixture.Events,
            wrongBytes,
            null);

        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.BindForSend(
            fixture.Plan,
            fixture.PlanRef,
            fixture.Input,
            renderer));
    }

    [TestMethod]
    public async Task MissingLastTransitiveArtifactFailsBeforeRendering()
    {
        var fixture = AsyncCapability();
        fixture.Events.Clear();
        var resolver = new RecordingArtifactResolver(
            fixture.ExternalArtifacts,
            fixture.Events,
            missingRef: fixture.ParameterProvenanceRef);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            MachineQueryBinder.OpenForSendAsync(
                fixture.Capability,
                resolver,
                CancellationToken.None));

        Assert.AreEqual(1, fixture.Renderer.RenderCount);
        Assert.IsFalse(fixture.Events.Contains("render", StringComparer.Ordinal));
        Assert.AreEqual(8, resolver.OpenedReferences.Count);
    }

    [TestMethod]
    public async Task CorruptLastTransitiveArtifactFailsBeforeRendering()
    {
        var fixture = AsyncCapability();
        fixture.Events.Clear();
        var resolver = new RecordingArtifactResolver(
            fixture.ExternalArtifacts,
            fixture.Events,
            corruptRef: fixture.ParameterProvenanceRef);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            MachineQueryBinder.OpenForSendAsync(
                fixture.Capability,
                resolver,
                CancellationToken.None));

        Assert.AreEqual(1, fixture.Renderer.RenderCount);
        Assert.IsFalse(fixture.Events.Contains("render", StringComparer.Ordinal));
        Assert.AreEqual(8, resolver.OpenedReferences.Count);
    }

    [TestMethod]
    public void ExactBinderCapabilityOpensAndReturnedArraysCannotChangeFutureOpens()
    {
        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
        var input = Parameters();
        var renderer = Renderer(plan, Target, PostBody);
        var capability = MachineQueryBinder.BindForSend(plan, planRef, input, renderer);

        var escapedCapabilityBody = capability.CopyRequestBody();
        escapedCapabilityBody[0] ^= 0xff;
        var first = MachineQueryBinder.OpenForSend(capability);
        var escapedOpenedBody = first.CopyRequestBody();
        escapedOpenedBody[0] ^= 0xff;
        var second = MachineQueryBinder.OpenForSend(capability);

        Assert.AreEqual(3, renderer.RenderCount);
        Assert.AreEqual(Target, first.RequestedUri);
        Assert.AreEqual(planRef, first.QueryPlanRef);
        Assert.AreEqual(input.ArtifactRef, first.OrderedParameterSetRef);
        Assert.AreEqual(capability.RenderReceipt, first.RenderReceipt);
        CollectionAssert.AreEqual(PostBody, first.CopyRequestBody());
        CollectionAssert.AreEqual(PostBody, second.CopyRequestBody());
    }

    [TestMethod]
    public void FriendConstructedCapabilityWithAnExactPublicTupleCannotOpen()
    {
        var plan = PostPlan();
        var genuine = MachineQueryBinder.BindForSend(
            plan,
            MachineQueryPlanIdentity.Create(PlanResourceId, plan),
            Parameters(),
            Renderer(plan, Target, PostBody));
        var fake = new FakeBoundMachineRequest(
            genuine.RequestedUri,
            genuine.CopyRequestBody(),
            genuine.RenderReceipt);

        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.OpenIdentity(fake));
        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.OpenForSend(fake));
    }

    [TestMethod]
    public void ReopeningRefusesPlanInputRendererAndReceiptSubstitution()
    {
        var planRefSubstitution = StableCapability();
        SetRetainedProperty(
            planRefSubstitution.Capability,
            "QueryPlanRef",
            new SourceArtifactRef(
                planRefSubstitution.PlanRef.ResourceId,
                new string('f', 64)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(planRefSubstitution.Capability));

        var planBytesSubstitution = StableCapability();
        MutateRetainedBytes(planBytesSubstitution.Capability, "_planCanonicalBytes");
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(planBytesSubstitution.Capability));

        var inputRefSubstitution = StableCapability();
        SetRetainedProperty(
            inputRefSubstitution.Capability,
            "OrderedParameterSetRef",
            new SourceArtifactRef(
                inputRefSubstitution.Input.ArtifactRef.ResourceId,
                new string('e', 64)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(inputRefSubstitution.Capability));

        var inputBytesSubstitution = StableCapability();
        MutateRetainedBytes(inputBytesSubstitution.Capability, "_inputCanonicalBytes");
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(inputBytesSubstitution.Capability));

        var frozenTargetSubstitution = StableCapability();
        SetRetainedProperty(
            frozenTargetSubstitution.Capability,
            "RequestedUri",
            "https://evil.example/sparql");
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(frozenTargetSubstitution.Capability));

        var frozenBodySubstitution = StableCapability();
        MutateRetainedBytes(frozenBodySubstitution.Capability, "_requestBody");
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(frozenBodySubstitution.Capability));

        var rendererProfileSubstitution = MutableCapability();
        rendererProfileSubstitution.Renderer.RendererProfileRef = Artifact(
            rendererProfileSubstitution.Renderer.RendererProfileRef.ResourceId,
            new string('d', 64));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(rendererProfileSubstitution.Capability));

        var rendererSourceSubstitution = MutableCapability();
        rendererSourceSubstitution.Renderer.RendererSourceRef = Artifact(
            rendererSourceSubstitution.Renderer.RendererSourceRef.ResourceId,
            new string('c', 64));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(rendererSourceSubstitution.Capability));

        var contentTypeRegistrySubstitution = StableCapability();
        var receipt = contentTypeRegistrySubstitution.Capability.RenderReceipt;
        SetRetainedProperty(
            contentTypeRegistrySubstitution.Capability,
            "RenderReceipt",
            new MachineQueryRenderReceipt(
                receipt.Schema,
                receipt.QueryPlanRef,
                receipt.QueryPlanSchema,
                receipt.RendererProfileRef,
                receipt.RendererSourceRef,
                receipt.OrderedParameterSetRef,
                new SourceRegistryMemberRef(
                    Artifact("99999999-9999-4999-8999-999999999999", '9'),
                    receipt.ContentType!.MemberKey),
                receipt.Charset,
                receipt.InputMode,
                receipt.Method,
                receipt.RequestTargetLength,
                receipt.RequestTargetSha256,
                receipt.RequestBodyLength,
                receipt.RequestBodySha256));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(contentTypeRegistrySubstitution.Capability));
    }

    [TestMethod]
    public void ReopeningRefusesStatefulRendererOutputDrift()
    {
        var bodyDrift = MutableCapability();
        bodyDrift.Renderer.RequestBody = Encoding.UTF8.GetBytes("query=changed");
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(bodyDrift.Capability));

        var targetDrift = MutableCapability();
        targetDrift.Renderer.RequestedUri = "https://evil.example/sparql";
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryBinder.OpenForSend(targetDrift.Capability));
    }

    [TestMethod]
    public void OfflineVerificationReproducesEvidenceButCannotMintSendCapability()
    {
        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
        var input = Parameters();
        var firstRenderer = Renderer(plan, Target, PostBody);
        var receipt = MachineQueryBinder.BindForSend(
            plan,
            planRef,
            input,
            firstRenderer).RenderReceipt;
        var verifier = typeof(MachineQueryBinder).GetMethod(
            nameof(MachineQueryBinder.VerifyOffline),
            BindingFlags.Public | BindingFlags.Static)!;

        Assert.AreEqual(typeof(void), verifier.ReturnType);
        MachineQueryBinder.VerifyOffline(
            plan,
            planRef,
            input,
            receipt,
            Renderer(plan, Target, PostBody));
        Assert.AreEqual(
            1,
            typeof(MachineQueryBinder).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Count(static method => method.ReturnType == typeof(OpenedMachineRequest)));
    }

    [TestMethod]
    public void OfflineRerenderIsCultureEnvironmentAndInvocationHistoryIndependent()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        const string environmentName = "LEX_V3_MACHINE_QUERY_TEST_CANARY";
        var originalEnvironment = Environment.GetEnvironmentVariable(environmentName);
        try
        {
            var plan = PostPlan();
            var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
            var firstRenderer = Renderer(plan, Target, PostBody);
            var receipt = MachineQueryBinder.BindForSend(
                plan,
                planRef,
                Parameters(),
                firstRenderer).RenderReceipt;

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Environment.SetEnvironmentVariable(environmentName, "changed");

            var freshRenderer = Renderer(plan, Target, PostBody);
            MachineQueryBinder.VerifyOffline(plan, planRef, Parameters(), receipt, freshRenderer);
            Assert.AreEqual(1, freshRenderer.RenderCount);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            Environment.SetEnvironmentVariable(environmentName, originalEnvironment);
        }
    }

    [TestMethod]
    public void EveryPlanRendererInputAndOutputMismatchFailsClosed()
    {
        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);

        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.BindForSend(
            plan,
            Artifact("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", 'a'),
            Parameters(),
            Renderer(plan, Target, PostBody)));
        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.BindForSend(
            plan,
            planRef,
            MachineQueryInputArtifact.Create(
                "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
                QueryFamily(),
                "jolux-resource-keyset",
                BoundedRows(),
                QueryParameters()),
            Renderer(plan, Target, PostBody)));
        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.BindForSend(
            plan,
            planRef,
            Parameters(),
            Renderer(plan, "https://evil.example/sparql", PostBody)));
        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.BindForSend(
            plan,
            planRef,
            Parameters(),
            Renderer(plan, Target, Encoding.UTF8.GetBytes("query=changed"))));
        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.BindForSend(
            plan,
            planRef,
            Parameters(),
            Renderer(plan, Target, PostBody) with
            {
                RendererSourceRef = Artifact(
                    "99999999-9999-4999-8999-999999999999",
                    new string('9', 64)),
            }));
    }

    [TestMethod]
    public void GetUsesOneAbsentBodyShapeAndBindsOriginFormPathAndQuery()
    {
        const string requestedUri = "https://publisher.example/feed?cursor=abc%2F123";
        var plan = GetPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);

        var bound = MachineQueryBinder.BindForSend(
            plan,
            planRef,
            GetParameters(),
            Renderer(plan, requestedUri, []));

        Assert.AreEqual(requestedUri, bound.RequestedUri);
        Assert.AreEqual(
            Sha256(Encoding.ASCII.GetBytes("/feed?cursor=abc%2F123")),
            bound.RenderReceipt.RequestTargetSha256);
        Assert.IsNull(bound.RenderReceipt.RequestBodyLength);
        Assert.IsNull(bound.RenderReceipt.RequestBodySha256);
        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.BindForSend(
            plan,
            planRef,
            GetParameters(),
            Renderer(plan, "https://publisher.example/feed?cursor=unplanned", [])));

        Assert.ThrowsExactly<ArgumentException>(() => new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            QueryFamily(),
            RendererProfile(),
            RendererSource(),
            HttpRequestMethod.Get,
            "https://publisher.example/feed",
            "/feed".Length,
            Sha256(Encoding.ASCII.GetBytes("/feed")),
            OpaqueResponse(),
            ContentType(),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            ParameterRef(),
            Partition(),
            1,
            Sha256([1])));
    }

    [TestMethod]
    public void ResponseCardinalityIsDeclaredBeforeSendAndHasNoUntypedAbsenceState()
    {
        var bounded = BoundedRows();
        Assert.AreEqual(MachineResponseCardinalityKind.BoundedRowSetPage, bounded.Kind);
        Assert.AreEqual(500, bounded.RowLimit);
        Assert.AreEqual<long?>(
            expected: 13_207_454,
            actual: bounded.ExpectedPartitionRowCount);
        Assert.IsTrue(bounded.ExpectedPartitionRowCountEvidenceRef == CountEvidenceRef());

        var opaque = OpaqueResponse();
        Assert.AreEqual(MachineResponseCardinalityKind.OpaqueBody, opaque.Kind);
        Assert.IsNull(opaque.RowLimit);
        Assert.IsNull(opaque.ExpectedPartitionRowCount);
        Assert.IsNull(opaque.ExpectedPartitionRowCountEvidenceRef);

        Assert.ThrowsExactly<ArgumentException>(() => new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody,
            rowLimit: 1,
            expectedPartitionRowCount: null,
            expectedPartitionRowCountEvidenceRef: null));
        Assert.ThrowsExactly<ArgumentException>(() => new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            rowLimit: null,
            expectedPartitionRowCount: 1,
            expectedPartitionRowCountEvidenceRef: CountEvidenceRef()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            rowLimit: 0,
            expectedPartitionRowCount: 1,
            expectedPartitionRowCountEvidenceRef: CountEvidenceRef()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            rowLimit: 1_000_000,
            expectedPartitionRowCount: 1,
            expectedPartitionRowCountEvidenceRef: CountEvidenceRef()));
        Assert.ThrowsExactly<ArgumentException>(() => new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            rowLimit: 500,
            expectedPartitionRowCount: null,
            expectedPartitionRowCountEvidenceRef: CountEvidenceRef()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            rowLimit: 500,
            expectedPartitionRowCount: -1,
            expectedPartitionRowCountEvidenceRef: CountEvidenceRef()));
        Assert.ThrowsExactly<ArgumentException>(() => new MachineResponseCardinality(
            MachineResponseCardinalityKind.BoundedRowSetPage,
            rowLimit: 500,
            expectedPartitionRowCount: 1,
            expectedPartitionRowCountEvidenceRef: null));
        Assert.ThrowsExactly<ArgumentException>(() => new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody,
            rowLimit: null,
            expectedPartitionRowCount: 0,
            expectedPartitionRowCountEvidenceRef: CountEvidenceRef()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MachineResponseCardinality(
            (MachineResponseCardinalityKind)0,
            rowLimit: null,
            expectedPartitionRowCount: null,
            expectedPartitionRowCountEvidenceRef: null));
    }

    [TestMethod]
    public void PlanRejectsUnboundPartitionsInvalidMethodsAndQueryBearingBaseTargets()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PostPlan(
            partition: new SourceRegistryMemberRef(
                Artifact("88888888-8888-4888-8888-888888888888", '8'),
                "wrong-artifact")));
        Assert.ThrowsExactly<ArgumentException>(() => PostPlan(
            queryFamily: new SourceRegistryMemberRef(
                Artifact("11111111-1111-4111-8111-111111111111", '1'),
                "SELECT * WHERE { ?s ?p ?o }")));
        Assert.ThrowsExactly<ArgumentException>(() => PostPlan(
            partition: new SourceRegistryMemberRef(ParameterRef(), "cursor=user%20text")));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PostPlan(method: (HttpRequestMethod)0));
        Assert.ThrowsExactly<ArgumentException>(() => PostPlan(
            target: Target + "?query=hidden"));
        Assert.AreEqual(0, typeof(MachineQueryInputArtifact).GetConstructors().Length);
    }

    [TestMethod]
    public void BinderRejectsInputMetadataDriftBeforeRendererInvocation()
    {
        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
        var renderer = Renderer(plan, Target, PostBody);
        var mismatchedInput = MachineQueryInputArtifact.Create(
            "urn:uuid:66666666-6666-4666-8666-666666666666",
            QueryFamily(),
            "jolux-resource-keyset",
            new MachineResponseCardinality(
                MachineResponseCardinalityKind.BoundedRowSetPage,
                rowLimit: 499,
                expectedPartitionRowCount: 13_207_454,
                expectedPartitionRowCountEvidenceRef: CountEvidenceRef()),
            QueryParameters());

        Assert.ThrowsExactly<ArgumentException>(() => MachineQueryBinder.BindForSend(
            plan,
            planRef,
            mismatchedInput,
            renderer));
        Assert.AreEqual(0, renderer.RenderCount);
    }

    [TestMethod]
    public void InputFactoryDerivesCanonicalBytesAndRendererConsumesTheTypedLimit()
    {
        var first = Parameters();
        var second = Parameters();
        CollectionAssert.AreEqual(first.CopyCanonicalBytes(), second.CopyCanonicalBytes());
        Assert.AreEqual(first.ArtifactRef, second.ArtifactRef);
        Assert.AreEqual(Sha256(first.CopyCanonicalBytes()), first.ArtifactRef.Sha256);
        Assert.IsFalse(first.OrderedParameters.Any(
            static parameter => parameter.Name == "page_size"));
        Assert.AreEqual((long?)500, first.ResponseCardinality.RowLimit);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<MachineQueryParameter>)first.OrderedParameters).Clear());

        var plan = PostPlan();
        var renderer = new TypedLimitRenderer(
            plan.RendererProfileRef,
            plan.RendererSourceRef,
            Target);
        var bound = MachineQueryBinder.BindForSend(
            plan,
            MachineQueryPlanIdentity.Create(PlanResourceId, plan),
            first,
            renderer);

        CollectionAssert.AreEqual(PostBody, bound.CopyRequestBody());
        Assert.AreEqual(1, renderer.RenderCount);
    }

    [TestMethod]
    public void InputFactorySnapshotsAChangingCallerCollectionExactlyOnce()
    {
        var changing = new ChangingParameterList(QueryParameters());

        var artifact = MachineQueryInputArtifact.Create(
            "urn:uuid:66666666-6666-4666-8666-666666666666",
            QueryFamily(),
            "jolux-resource-keyset",
            BoundedRows(),
            changing);

        Assert.AreEqual(1, changing.EnumerationCount);
        CollectionAssert.AreEqual(
            QueryParameters().Select(static parameter => parameter.Name).ToArray(),
            artifact.OrderedParameters.Select(static parameter => parameter.Name).ToArray());
    }

    [TestMethod]
    public void SnapshottedInputElementsCannotBeMutatedAfterValidation()
    {
        Assert.IsTrue(typeof(MachineQueryParameter).IsSealed);
        Assert.IsFalse(typeof(MachineQueryParameter).GetProperties().Any(
            static property => property.SetMethod is not null));
    }

    [TestMethod]
    public void PublisherLiteralIsTypedAsLexicalInputRatherThanAsACursor()
    {
        var provenance = Artifact("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", 'b');
        var value = new MachineQueryParameter(
            "requested_identifier",
            MachineQueryParameterKind.PublisherLiteral,
            integerValue: null,
            textValue: "32016R0679",
            provenance);

        Assert.AreEqual(MachineQueryParameterKind.PublisherLiteral, value.Kind);
        Assert.AreEqual("32016R0679", value.TextValue);
        Assert.AreEqual(provenance, value.ProvenanceRef);
        Assert.AreEqual(
            "\"publisher_literal\"",
            ContractJson.Serialize(MachineQueryParameterKind.PublisherLiteral));
        Assert.AreEqual(
            value,
            ContractJson.Deserialize<MachineQueryParameter>(ContractJson.Serialize(value)));

        Assert.ThrowsExactly<ArgumentException>(() => new MachineQueryParameter(
            "requested_identifier", MachineQueryParameterKind.PublisherLiteral,
            integerValue: 1, textValue: "32016R0679", provenance));
        Assert.ThrowsExactly<ArgumentException>(() => new MachineQueryParameter(
            "requested_identifier", MachineQueryParameterKind.PublisherLiteral,
            integerValue: null, textValue: string.Empty, provenance));
        Assert.ThrowsExactly<ArgumentException>(() => new MachineQueryParameter(
            "requested_identifier", MachineQueryParameterKind.PublisherLiteral,
            integerValue: null, textValue: "line\nbreak", provenance));
        Assert.ThrowsExactly<ArgumentException>(() => new MachineQueryParameter(
            "requested_identifier", MachineQueryParameterKind.PublisherLiteral,
            integerValue: null, textValue: "\ud800", provenance));
    }

    [TestMethod]
    public void RetainedInputArtifactCanBeReconstructedAndReplayedOutsideItsProducer()
    {
        var produced = Parameters();
        var retainedBytes = produced.CopyCanonicalBytes();

        var replayed = MachineQueryInputArtifact.ParseAndVerify(
            produced.ArtifactRef,
            retainedBytes);
        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
        var receipt = MachineQueryBinder.BindForSend(
            plan,
            planRef,
            produced,
            Renderer(plan, Target, PostBody)).RenderReceipt;

        MachineQueryBinder.VerifyOffline(
            plan,
            planRef,
            replayed,
            receipt,
            Renderer(plan, Target, PostBody));

        Assert.AreEqual(produced.ArtifactRef, replayed.ArtifactRef);
        CollectionAssert.AreEqual(retainedBytes, replayed.CopyCanonicalBytes());
    }

    [TestMethod]
    public void RetainedInputArtifactRejectsWrongIdentityMutationAndNonCanonicalJson()
    {
        var produced = Parameters();
        var canonicalBytes = produced.CopyCanonicalBytes();
        var wrongRef = new SourceArtifactRef(
            produced.ArtifactRef.ResourceId,
            new string('f', 64));
        var mutatedBytes = canonicalBytes.ToArray();
        mutatedBytes[^2] ^= 1;
        var nonCanonicalBytes = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(canonicalBytes).Replace(
                "\"schema\":",
                "\"schema\": ",
                StringComparison.Ordinal));
        var nonCanonicalRef = new SourceArtifactRef(
            produced.ArtifactRef.ResourceId,
            Sha256(nonCanonicalBytes));

        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryInputArtifact.ParseAndVerify(wrongRef, canonicalBytes));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryInputArtifact.ParseAndVerify(produced.ArtifactRef, mutatedBytes));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryInputArtifact.ParseAndVerify(nonCanonicalRef, nonCanonicalBytes));
    }

    [TestMethod]
    public void RetainedInputArtifactRejectsEscapedSurrogateAndDuplicateMembers()
    {
        var produced = Parameters();
        var canonicalJson = Encoding.UTF8.GetString(produced.CopyCanonicalBytes());
        var escapedSurrogateBytes = Encoding.UTF8.GetBytes(canonicalJson.Replace(
            "jolux-resource-keyset",
            "\\uD800",
            StringComparison.Ordinal));
        var duplicateMemberBytes = Encoding.UTF8.GetBytes(canonicalJson.Insert(
            1,
            "\"schema\":\"machine_query_input/1\","));

        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryInputArtifact.ParseAndVerify(
                new SourceArtifactRef(
                    produced.ArtifactRef.ResourceId,
                    Sha256(escapedSurrogateBytes)),
                escapedSurrogateBytes));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryInputArtifact.ParseAndVerify(
                new SourceArtifactRef(
                    produced.ArtifactRef.ResourceId,
                    Sha256(duplicateMemberBytes)),
                duplicateMemberBytes));
    }

    [TestMethod]
    public void RetainedInputArtifactRejectsUnknownSchemaAndKeepsConstructionControlled()
    {
        var produced = Parameters();
        var unknownSchemaBytes = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(produced.CopyCanonicalBytes()).Replace(
                "machine_query_input/1",
                "machine_query_input/2",
                StringComparison.Ordinal));
        var unknownSchemaRef = new SourceArtifactRef(
            produced.ArtifactRef.ResourceId,
            Sha256(unknownSchemaBytes));

        Assert.ThrowsExactly<ArgumentException>(() =>
            MachineQueryInputArtifact.ParseAndVerify(unknownSchemaRef, unknownSchemaBytes));
        Assert.AreEqual(0, typeof(MachineQueryInputArtifact).GetConstructors().Length);
        Assert.AreEqual(
            1,
            typeof(MachineQueryInputArtifact).GetMethods()
                .Count(static method =>
                    method.IsPublic &&
                    method.IsStatic &&
                    method.Name == nameof(MachineQueryInputArtifact.Create)));
    }

    [TestMethod]
    public void ProductionIngestCannotBypassContractConstructionControls()
    {
        Assert.IsFalse(typeof(MachineQueryPlan).Assembly
            .GetCustomAttributes(typeof(InternalsVisibleToAttribute), inherit: false)
            .Cast<InternalsVisibleToAttribute>()
            .Any(static attribute =>
                attribute.AssemblyName.StartsWith("Lex.V3.Ingest,", StringComparison.Ordinal) ||
                string.Equals(attribute.AssemblyName, "Lex.V3.Ingest", StringComparison.Ordinal)));
        Assert.AreEqual(0, typeof(BoundMachineRequest).GetConstructors().Length);
    }

    [TestMethod]
    public void PlanRejectsNonCanonicalBaseTargetAliasesAndSideChannels()
    {
        foreach (var target in MachineQueryValidationVectors.RuntimeRejectedTargets)
        {
            Assert.ThrowsExactly<ArgumentException>(() => PostPlan(target: target), target);
        }
    }

    [TestMethod]
    public void PlanAndReceiptUseTheSameConservativeMediaTypeVocabulary()
    {
        foreach (var mediaType in MachineQueryValidationVectors.RuntimeRejectedMediaTypes)
        {
            Assert.ThrowsExactly<ArgumentException>(() => PostPlan(
                contentType: ContentType(mediaType)), mediaType);
        }

        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
        var receipt = MachineQueryBinder.BindForSend(
            plan,
            planRef,
            Parameters(),
            Renderer(plan, Target, PostBody)).RenderReceipt;
        Assert.ThrowsExactly<ArgumentException>(() => new MachineQueryRenderReceipt(
            MachineQueryRenderReceipt.SchemaId,
            receipt.QueryPlanRef,
            receipt.QueryPlanSchema,
            receipt.RendererProfileRef,
            receipt.RendererSourceRef,
            receipt.OrderedParameterSetRef,
            ContentType("application/x~foo"),
            receipt.Charset,
            receipt.InputMode,
            receipt.Method,
            receipt.RequestTargetLength,
            receipt.RequestTargetSha256,
            receipt.RequestBodyLength,
            receipt.RequestBodySha256));
    }

    [TestMethod]
    public void PlanAndReceiptIdentitiesHaveFixedGetAndPostVectors()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        string[][] vectors;
        try
        {
            vectors = MachineQueryValidationVectors.CultureNames
                .Select(cultureName =>
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                    return CreateIdentityVector();
                })
                .ToArray();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        CollectionAssert.AreEqual(vectors[0], vectors[1]);
        CollectionAssert.AreEqual(
            new[]
            {
                "94273fde63a68f325551207a1d6a462591b8f2dec6b6baccc55f737e96706149",
                "195cd2bd78901c9389846468b620daccf2acadf04a8f78f83e678e645786754d",
                "8969dd3a446b15561eeb626b273d3a7b6e77e6258bab970676ba8931ae9765b4",
                "9403acf9104d99d175f6366c5f9bfa19b589da6934a6de3c8a951c096fc4d130",
            },
            vectors[0]);
    }

    private static string[] PublicSurface(Type type) => type
        .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
        .Where(static member => member is not MethodInfo { IsSpecialName: true })
        .Select(DescribePublicMember)
        .OrderBy(static description => description, StringComparer.Ordinal)
        .ToArray();

    private static string DescribePublicMember(MemberInfo member) => member switch
    {
        ConstructorInfo constructor =>
            $"constructor .ctor({DescribeParameters(constructor)}): Void",
        MethodInfo method =>
            $"method {(method.IsStatic ? "static" : "instance")} {method.Name}" +
            $"({DescribeParameters(method)}): {method.ReturnType.Name}",
        PropertyInfo property =>
            $"property {(property.GetMethod?.IsStatic == true ? "static" : "instance")} " +
            $"{property.Name}: {property.PropertyType.Name}",
        FieldInfo field =>
            $"field {(field.IsStatic ? "static" : "instance")} {field.Name}: {field.FieldType.Name}",
        _ => $"{member.MemberType} {member.Name}",
    };

    private static string DescribeParameters(MethodBase method) => string.Join(
        ", ",
        method.GetParameters().Select(static parameter => parameter.ParameterType.Name));

    private static (
        BoundMachineRequest Capability,
        MachineQueryPlan Plan,
        SourceArtifactRef PlanRef,
        MachineQueryInputArtifact Input,
        StubRenderer Renderer) StableCapability()
    {
        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
        var input = Parameters();
        var renderer = Renderer(plan, Target, PostBody);
        return (
            MachineQueryBinder.BindForSend(plan, planRef, input, renderer),
            plan,
            planRef,
            input,
            renderer);
    }

    private static (
        BoundMachineRequest Capability,
        MutableRenderer Renderer) MutableCapability()
    {
        var plan = PostPlan();
        var renderer = new MutableRenderer(
            plan.RendererProfileRef,
            plan.RendererSourceRef,
            Target,
            PostBody);
        return (
            MachineQueryBinder.BindForSend(
                plan,
                MachineQueryPlanIdentity.Create(PlanResourceId, plan),
                Parameters(),
                renderer),
            renderer);
    }

    private static void SetRetainedProperty(
        BoundMachineRequest capability,
        string propertyName,
        object value)
    {
        var field = capability.GetType().GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"The binder must retain {propertyName} independently.");
        field.SetValue(capability, value);
    }

    private static void MutateRetainedBytes(BoundMachineRequest capability, string fieldName)
    {
        var field = capability.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"The binder must retain exact bytes in {fieldName}.");
        var bytes = field.GetValue(capability) as byte[];
        Assert.IsNotNull(bytes);
        bytes[0] ^= 0xff;
    }

    private static string[] CreateIdentityVector()
    {
        var postPlan = PostPlan();
        var postPlanRef = MachineQueryPlanIdentity.Create(PlanResourceId, postPlan);
        var postReceiptRef = MachineQueryRenderReceiptIdentity.Create(
            "urn:uuid:77777777-7777-4777-8777-777777777777",
            MachineQueryBinder.BindForSend(
                postPlan,
                postPlanRef,
                Parameters(),
                new TypedLimitRenderer(
                    postPlan.RendererProfileRef,
                    postPlan.RendererSourceRef,
                    Target)).RenderReceipt);
        var getPlan = GetPlan();
        var getPlanRef = MachineQueryPlanIdentity.Create(PlanResourceId, getPlan);
        var getReceiptRef = MachineQueryRenderReceiptIdentity.Create(
            "urn:uuid:77777777-7777-4777-8777-777777777777",
            MachineQueryBinder.BindForSend(
                getPlan,
                getPlanRef,
                GetParameters(),
                Renderer(
                    getPlan,
                    "https://publisher.example/feed?cursor=abc%2F123",
                    [])).RenderReceipt);

        return
        [
            postPlanRef.Sha256,
            postReceiptRef.Sha256,
            getPlanRef.Sha256,
            getReceiptRef.Sha256,
        ];
    }

    // Item 1b, Decision 75's closure, flipped this default from false to true: a renderer that
    // produces nothing now refuses at open rather than falling back to reopen by reference, so
    // every test that is not specifically exercising that refusal needs a renderer that behaves
    // the way both real renderers do.
    private static AsyncBinderFixture AsyncCapability(bool rendererProducesItsBytes = true)
    {
        var events = new List<string>();
        var rendererProfileBytes = Encoding.UTF8.GetBytes("renderer-profile/1\n");
        var rendererSourceBytes = Encoding.UTF8.GetBytes("renderer-source/1\n");
        var contentTypeRegistryBytes = Encoding.UTF8.GetBytes("content-type-registry/1\n");
        var queryRegistryBytes = Encoding.UTF8.GetBytes("query-registry/1\n");
        var parameterProvenanceBytes = Encoding.UTF8.GetBytes("parameter-provenance/1\n");
        var rendererProfileRef = ArtifactBytes(
            "urn:uuid:10000000-0000-4000-8000-000000000001",
            rendererProfileBytes);
        var rendererSourceRef = ArtifactBytes(
            "urn:uuid:10000000-0000-4000-8000-000000000002",
            rendererSourceBytes);
        var contentTypeRegistryRef = ArtifactBytes(
            "urn:uuid:10000000-0000-4000-8000-000000000003",
            contentTypeRegistryBytes);
        var queryRegistryRef = ArtifactBytes(
            "urn:uuid:10000000-0000-4000-8000-000000000004",
            queryRegistryBytes);
        var parameterProvenanceRef = ArtifactBytes(
            "urn:uuid:10000000-0000-4000-8000-000000000005",
            parameterProvenanceBytes);
        var queryFamilyRef = new SourceRegistryMemberRef(queryRegistryRef, "eu-test-query");
        var cardinality = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody,
            rowLimit: null,
            expectedPartitionRowCount: null,
            expectedPartitionRowCountEvidenceRef: null);
        var input = MachineQueryInputArtifact.Create(
            "urn:uuid:10000000-0000-4000-8000-000000000006",
            queryFamilyRef,
            "eu-test-partition",
            cardinality,
            [
                new MachineQueryParameter(
                    "limit",
                    MachineQueryParameterKind.BoundedInteger,
                    integerValue: 1,
                    textValue: null,
                    parameterProvenanceRef),
            ]);
        var plan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            queryFamilyRef,
            rendererProfileRef,
            rendererSourceRef,
            HttpRequestMethod.Post,
            Target,
            RequestTargetBytes(Target).LongLength,
            Sha256(RequestTargetBytes(Target)),
            cardinality,
            new SourceRegistryMemberRef(contentTypeRegistryRef, "application/sparql-query"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            PostBody.LongLength,
            Sha256(PostBody));
        var planRef = MachineQueryPlanIdentity.Create(
            "urn:uuid:10000000-0000-4000-8000-000000000007",
            plan);
        var renderer = new ObservedRenderer(
            rendererProfileRef,
            rendererSourceRef,
            Target,
            PostBody,
            events,
            rendererProducesItsBytes ? rendererProfileBytes : null,
            rendererProducesItsBytes ? rendererSourceBytes : null);
        var capability = MachineQueryBinder.BindForSend(plan, planRef, input, renderer);
        var externalArtifacts = new Dictionary<SourceArtifactRef, byte[]>
        {
            [rendererProfileRef] = rendererProfileBytes,
            [rendererSourceRef] = rendererSourceBytes,
            [contentTypeRegistryRef] = contentTypeRegistryBytes,
            [queryRegistryRef] = queryRegistryBytes,
            [parameterProvenanceRef] = parameterProvenanceBytes,
        };
        return new AsyncBinderFixture(
            capability,
            plan,
            planRef,
            input,
            rendererProfileRef,
            rendererSourceRef,
            contentTypeRegistryRef,
            queryRegistryRef,
            parameterProvenanceRef,
            externalArtifacts,
            renderer,
            events);
    }

    private const string PlanResourceId = "urn:uuid:55555555-5555-4555-8555-555555555555";

    private static MachineQueryPlan PostPlan(
        HttpRequestMethod method = HttpRequestMethod.Post,
        string target = Target,
        SourceRegistryMemberRef? partition = null,
        SourceRegistryMemberRef? queryFamily = null,
        SourceRegistryMemberRef? contentType = null) => new(
        MachineQueryPlan.SchemaId,
        queryFamily ?? QueryFamily(),
        RendererProfile(),
        RendererSource(),
        method,
        target,
        RequestTargetBytes(target).LongLength,
        Sha256(RequestTargetBytes(target)),
        BoundedRows(),
        contentType ?? ContentType(),
        MachineQueryCharset.Utf8,
        MachineQueryInputMode.RendererInputs,
        ParameterRef(),
        partition ?? Partition(),
        PostBody.Length,
        Sha256(PostBody));

    private static byte[] RequestTargetBytes(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var parsed)
            ? Encoding.ASCII.GetBytes(parsed.PathAndQuery)
            : Encoding.ASCII.GetBytes("/sparqlendpoint");

    private static MachineQueryPlan GetPlan() => new(
        MachineQueryPlan.SchemaId,
        QueryFamily(),
        RendererProfile(),
        RendererSource(),
        HttpRequestMethod.Get,
        "https://publisher.example/feed",
        "/feed?cursor=abc%2F123".Length,
        Sha256(Encoding.ASCII.GetBytes("/feed?cursor=abc%2F123")),
        OpaqueResponse(),
        contentType: null,
        charset: null,
        MachineQueryInputMode.RendererInputs,
        GetParameters().ArtifactRef,
        GetParameters().PartitionBinding,
        expectedRequestBodyLength: null,
        expectedRequestBodySha256: null);

    private static StubRenderer Renderer(
        MachineQueryPlan plan,
        string requestedUri,
        byte[] body) => new(
        plan.RendererProfileRef,
        plan.RendererSourceRef,
        requestedUri,
        body);

    private static MachineQueryInputArtifact Parameters() => MachineQueryInputArtifact.Create(
        "urn:uuid:66666666-6666-4666-8666-666666666666",
        QueryFamily(),
        "jolux-resource-keyset",
        BoundedRows(),
        QueryParameters());

    private static MachineQueryInputArtifact GetParameters() => MachineQueryInputArtifact.Create(
        "urn:uuid:66666666-6666-4666-8666-666666666666",
        QueryFamily(),
        "jolux-resource-keyset",
        OpaqueResponse(),
        QueryParameters());

    private static MachineQueryParameter[] QueryParameters() =>
    [
        new(
            "lower_bound",
            MachineQueryParameterKind.PublisherCursor,
            integerValue: null,
            textValue: "jolux:0001",
            Artifact("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", 'b')),
    ];

    private static SourceRegistryMemberRef QueryFamily() =>
        new(Artifact("11111111-1111-4111-8111-111111111111", '1'), "jolux-resource-page");

    private static SourceArtifactRef RendererProfile() =>
        Artifact("22222222-2222-4222-8222-222222222222", '2');

    private static SourceArtifactRef RendererSource() =>
        Artifact("44444444-4444-4444-8444-444444444444", '4');

    private static MachineResponseCardinality BoundedRows() => new(
        MachineResponseCardinalityKind.BoundedRowSetPage,
        rowLimit: 500,
        expectedPartitionRowCount: 13_207_454,
        expectedPartitionRowCountEvidenceRef: CountEvidenceRef());

    private static MachineResponseCardinality OpaqueResponse() => new(
        MachineResponseCardinalityKind.OpaqueBody,
        rowLimit: null,
        expectedPartitionRowCount: null,
        expectedPartitionRowCountEvidenceRef: null);

    private static SourceArtifactRef CountEvidenceRef() =>
        Artifact("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", 'a');

    private static SourceRegistryMemberRef ContentType(
        string memberKey = "application/x-www-form-urlencoded") =>
        new(Artifact("33333333-3333-4333-8333-333333333333", '3'), memberKey);

    private static SourceArtifactRef ParameterRef() => Parameters().ArtifactRef;

    private static SourceRegistryMemberRef Partition() => Parameters().PartitionBinding;

    private static SourceArtifactRef ReceiptRef(MachineQueryRenderReceipt receipt) =>
        MachineQueryRenderReceiptIdentity.Create(
            "urn:uuid:77777777-7777-4777-8777-777777777777",
            receipt);

    private static SourceArtifactRef ObservationRef() =>
        Artifact("88888888-8888-4888-8888-888888888888", '8');

    private static SourceArtifactRef Artifact(string id, char digestFill) =>
        Artifact($"urn:uuid:{id}", new string(digestFill, 64));

    private static SourceArtifactRef Artifact(string resourceId, string digest) => new(
        resourceId.StartsWith("urn:uuid:", StringComparison.Ordinal)
            ? resourceId
            : "urn:uuid:" + resourceId,
        digest);

    private static SourceArtifactRef ArtifactBytes(
        string resourceId,
        ReadOnlySpan<byte> canonicalBytes) => new(resourceId, Sha256(canonicalBytes));

    private static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed record StubRenderer(
        SourceArtifactRef RendererProfileRef,
        SourceArtifactRef RendererSourceRef,
        string RequestedUri,
        byte[] Body) : IMachineQueryRenderer
    {
        public int RenderCount { get; private set; }

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan,
            MachineQueryInputArtifact orderedParameterSet)
        {
            RenderCount++;
            return new MachineQueryRenderOutput(RequestedUri, Body);
        }
    }

    private sealed record AsyncBinderFixture(
        BoundMachineRequest Capability,
        MachineQueryPlan Plan,
        SourceArtifactRef PlanRef,
        MachineQueryInputArtifact Input,
        SourceArtifactRef RendererProfileRef,
        SourceArtifactRef RendererSourceRef,
        SourceArtifactRef ContentTypeRegistryRef,
        SourceArtifactRef QueryRegistryRef,
        SourceArtifactRef ParameterProvenanceRef,
        IReadOnlyDictionary<SourceArtifactRef, byte[]> ExternalArtifacts,
        ObservedRenderer Renderer,
        List<string> Events);

    private sealed class ObservedRenderer(
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        string requestedUri,
        byte[] body,
        List<string> events,
        byte[]? profileBytes = null,
        byte[]? sourceBytes = null) : IMachineQueryRenderer
    {
        public SourceArtifactRef RendererProfileRef { get; } = rendererProfileRef;

        public SourceArtifactRef RendererSourceRef { get; } = rendererSourceRef;

        // Written as statements, not as "profileBytes is null ? null : new ReadOnlyMemory<byte>(
        // profileBytes)". That expression compiles and is wrong: measured in both Debug and
        // Release here, it hands the binder a present, empty memory when profileBytes is null, so
        // a renderer that produces nothing is read as producing zero bytes. Every conversion into
        // ReadOnlyMemory<byte>? in this change is spelled out for that reason.
        public ReadOnlyMemory<byte>? CopyRendererProfileBytes()
        {
            if (profileBytes is null)
            {
                return null;
            }

            return new ReadOnlyMemory<byte>(profileBytes);
        }

        public ReadOnlyMemory<byte>? CopyRendererSourceBytes()
        {
            if (sourceBytes is null)
            {
                return null;
            }

            return new ReadOnlyMemory<byte>(sourceBytes);
        }

        public int RenderCount { get; private set; }

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan,
            MachineQueryInputArtifact orderedParameterSet)
        {
            RenderCount++;
            events.Add("render");
            return new MachineQueryRenderOutput(requestedUri, body);
        }
    }

    private sealed class RecordingArtifactResolver(
        IReadOnlyDictionary<SourceArtifactRef, byte[]> externalArtifacts,
        List<string> events,
        SourceArtifactRef? missingRef = null,
        SourceArtifactRef? corruptRef = null,
        IReadOnlyCollection<SourceArtifactRef>? absentRefs = null) : IMachineQueryArtifactResolver
    {
        private readonly List<SourceArtifactRef> _openedReferences = new();

        private readonly List<SourceArtifactRef> _retainedReferences = new();

        internal IReadOnlyList<SourceArtifactRef> OpenedReferences => _openedReferences;

        /// <summary>The references this run put under its own custody rather than assumed.</summary>
        internal IReadOnlyList<SourceArtifactRef> RetainedReferences => _retainedReferences;

        public Task<ReadOnlyMemory<byte>> RetainAndReopenAsync(
            SourceArtifactRef reference,
            ReadOnlyMemory<byte> producerBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record(reference);
            _retainedReferences.Add(reference);
            return Task.FromResult(producerBytes);
        }

        public Task<ReadOnlyMemory<byte>> ReopenAsync(
            SourceArtifactRef reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record(reference);
            if (reference == missingRef ||
                (absentRefs is not null && absentRefs.Contains(reference)))
            {
                // What a real content-addressed store does for an artifact this run never held.
                throw new FileNotFoundException("The requested fixture artifact is absent.");
            }

            var bytes = externalArtifacts[reference].ToArray();
            if (reference == corruptRef)
            {
                bytes[0] ^= 0xff;
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }

        private void Record(SourceArtifactRef reference)
        {
            _openedReferences.Add(reference);
            events.Add("open");
        }
    }

    private sealed record TypedLimitRenderer(
        SourceArtifactRef RendererProfileRef,
        SourceArtifactRef RendererSourceRef,
        string RequestedUri) : IMachineQueryRenderer
    {
        public int RenderCount { get; private set; }

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan,
            MachineQueryInputArtifact orderedParameterSet)
        {
            RenderCount++;
            var pageSize = orderedParameterSet.ResponseCardinality.RowLimit
                ?? throw new InvalidOperationException("The page-size input must be an integer.");
            var body = Encoding.UTF8.GetBytes(
                "query=SELECT%20%2A%20WHERE%20%7B%20%3Fs%20%3Fp%20%3Fo%20%7D%20LIMIT%20" +
                pageSize.ToString(CultureInfo.InvariantCulture));
            return new MachineQueryRenderOutput(RequestedUri, body);
        }
    }

    private sealed class MutableRenderer(
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        string requestedUri,
        byte[] requestBody) : IMachineQueryRenderer
    {
        public SourceArtifactRef RendererProfileRef { get; set; } = rendererProfileRef;

        public SourceArtifactRef RendererSourceRef { get; set; } = rendererSourceRef;

        public string RequestedUri { get; set; } = requestedUri;

        public byte[] RequestBody { get; set; } = requestBody.ToArray();

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan,
            MachineQueryInputArtifact orderedParameterSet) =>
            new(RequestedUri, RequestBody);
    }

    private sealed class FakeBoundMachineRequest(
        string requestedUri,
        byte[] requestBody,
        MachineQueryRenderReceipt renderReceipt) : BoundMachineRequest
    {
        public override string RequestedUri { get; } = requestedUri;

        public override MachineQueryRenderReceipt RenderReceipt { get; } = renderReceipt;

        public override byte[] CopyRequestBody() => requestBody.ToArray();
    }

    private sealed class ChangingParameterList(
        IReadOnlyList<MachineQueryParameter> firstEnumeration) : IReadOnlyList<MachineQueryParameter>
    {
        public int EnumerationCount { get; private set; }

        public int Count => firstEnumeration.Count;

        public MachineQueryParameter this[int index] => firstEnumeration[index];

        public IEnumerator<MachineQueryParameter> GetEnumerator()
        {
            EnumerationCount++;
            return (EnumerationCount == 1
                    ? firstEnumeration
                    : new MachineQueryParameter[] { null! })
                .GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}

internal static class MachineQueryValidationVectors
{
    public static IReadOnlyList<string> CultureNames { get; } = Array.AsReadOnly(
        new[] { "fr-FR", "en-US" });

    public static IReadOnlyList<string> RuntimeRejectedTargets { get; } = Array.AsReadOnly(
        new[]
        {
            "https://DATA.legilux.public.lu/sparqlendpoint",
            "https://127.0.0.1/sparqlendpoint",
            "https://data.legilux.public.lu/%2E/sparqlendpoint",
            "https://data.legilux.public.lu/a%2Fb",
            "https://data.legilux.public.lu/a%5Cb",
            "https://data.legilux.public.lu/%252e/sparqlendpoint",
            "https://data.legilux.public.lu:99999/sparqlendpoint",
            "https://data.legilux.public.lu:443/sparqlendpoint",
            "http://data.legilux.public.lu:80/sparqlendpoint",
            "http://data.legilux.public.lu/sparqlendpoint",
            "https://data.legilux.public.lu/a/../b",
            "https://data.legilux.public.lu/a/./b",
            "https://user@data.legilux.public.lu/sparqlendpoint",
            "https://data.legilux.public.lu/sparqlendpoint?query=hidden",
            "https://data.legilux.public.lu/sparqlendpoint#fragment",
        });

    public static IReadOnlyList<string> RuntimeRejectedMediaTypes { get; } = Array.AsReadOnly(
        new[]
        {
            "application/x*foo",
            "application/x%foo",
            "application/x'foo",
            "application/x`foo",
            "application/x|foo",
            "application/x~foo",
        });
}

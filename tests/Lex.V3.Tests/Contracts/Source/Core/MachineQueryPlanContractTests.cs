using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void FinalEvidenceReferencesRatherThanEmbedsTheReceiptAndObservation()
    {
        var plan = PostPlan();
        var planRef = MachineQueryPlanIdentity.Create(PlanResourceId, plan);
        var receipt = MachineQueryBinder.BindForSend(
            plan,
            planRef,
            Parameters(),
            Renderer(plan, Target, PostBody)).RenderReceipt;
        var evidence = MachineRequestEvidence.FromReceipt(
            planRef,
            ReceiptRef(receipt),
            receipt,
            ObservationRef());

        using var document = JsonDocument.Parse(ContractJson.Serialize(evidence));
        CollectionAssert.AreEquivalent(
            new[]
            {
                "schema",
                "query_plan_ref",
                "query_plan_schema",
                "rerender_receipt_ref",
                "request_target_length",
                "request_target_sha256",
                "request_body_length",
                "request_body_sha256",
                "http_observation_ref",
            },
            document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.IsFalse(document.RootElement.GetRawText().Contains("SELECT", StringComparison.Ordinal));
        Assert.AreEqual(evidence, ContractJson.Deserialize<MachineRequestEvidence>(
            document.RootElement.GetRawText()));
        var invalid = JsonNode.Parse(ContractJson.Serialize(evidence))!.AsObject();
        invalid["request_body_length"] = 0;
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<MachineRequestEvidence>(invalid.ToJsonString()));
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
        Assert.AreEqual(0, typeof(MachineRequestEvidence).GetConstructors().Length);
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

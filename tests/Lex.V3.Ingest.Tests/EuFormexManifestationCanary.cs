using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>The separately governed Formex v2 manifestation canary proposed on #344.</summary>
/// <remarks>
/// This test is offline unless <c>LEX_FORMEX_MANIFESTATION_CANARY_V2=1</c>. The fresh gate prevents
/// checkpoint 31's spent v1 authorization from activating this changed publisher question. When enabled it first
/// reopens the accepted clean Stage 1 EU evidence, verifies its pinned digest, and confirms that the
/// exact expression is derived from its belongs-to-work and language rows. Only then does it create
/// the six-attempt budget and call the production manifestation-enumeration producer.
///
/// The custody store, discovery plan, executor, proof reopen and decoder are production types. The
/// source-profile witness is the test assembly's existing EU SPARQL witness because no shipped
/// composition root exists before Stage 6. This run enumerates publisher-asserted manifestation
/// types. It does not fetch a Formex ZIP or body and cannot prove package retrievability.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuFormexManifestationCanary
{
    private const string EnableVariable = "LEX_FORMEX_MANIFESTATION_CANARY_V2";
    private const int WireCeiling = 6;
    private const int ProductCeiling = 4;
    private const string Endpoint = "https://publications.europa.eu/webapi/rdf/sparql";
    private const string Work =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string Expression = Work + ".0024";
    private const string UsesLanguage =
        "http://publications.europa.eu/ontology/cdm#expression_uses_language";
    private const string BelongsToWork =
        "http://publications.europa.eu/ontology/cdm#expression_belongs_to_work";
    private const string AcceptedEvidenceSha256 =
        "bdc14319f2f02a43c7a8a58059eb1e1c3835cd3edf708e27d9d4f0d48f4e4f0b";
    private const string PlanV2Sha256 = "__PIN__";

    [TestMethod]
    public void TheGovernedInvocationPinsItsCoordinateMethodEndpointAndCeilings()
    {
        var expression = ExpressionFromAcceptedEvidence(AcceptedEvidenceBody());
        var plan = EuFormexManifestationDiscoveryPlan.Create();
        var bound = plan.BindCount(
            expression.Identity,
            EuFormexManifestationQueryPass.Pass1,
            "urn:uuid:1c25bcee-0bed-42f6-9992-e29dfcb725a2",
            "urn:uuid:a0fbbd58-a697-45c9-b52b-08095f984e71",
            RendererSource(CheckoutRoot()));
        var opened = MachineQueryBinder.OpenForSend(bound.Request);
        var query = Encoding.UTF8.GetString(opened.CopyRequestBody());

        Assert.AreEqual(Work, expression.Identity.PublisherWorkId);
        Assert.AreEqual(Expression, expression.Identity.PublisherExpressionId);
        Assert.AreEqual("eu-formex-manifestations-by-expression-v2-",
            EuFormexManifestationDiscoveryPlan.PartitionKeyPrefix);
        Assert.AreEqual(PlanV2Sha256, plan.ArtifactRef.Sha256);
        Assert.AreEqual(plan.ArtifactRef, plan.CountQueryFamilyRef.RegistryRef);
        Assert.AreEqual(plan.ArtifactRef, plan.PageQueryFamilyRef.RegistryRef);
        Assert.AreEqual("eu-formex-manifestations-by-expression.count",
            plan.CountQueryFamilyRef.MemberKey);
        Assert.AreEqual("eu-formex-manifestations-by-expression.page",
            plan.PageQueryFamilyRef.MemberKey);
        Assert.AreEqual(HttpRequestMethod.Post, bound.MachinePlan.Method);
        Assert.AreEqual(Endpoint, bound.MachinePlan.TargetOriginAndPath);
        Assert.AreEqual(Endpoint, opened.RequestedUri);
        StringAssert.Contains(query, "VALUES (?work ?expression)");
        StringAssert.Contains(query, "<" + Work + "> <" + Expression + ">");
        Assert.IsFalse(query.Contains("fmx4", StringComparison.Ordinal),
            "eligibility is derived from all manifestation types, never a Formex-prefiltered query.");
        Assert.AreEqual(WireCeiling, WireRequestBudget.OfWireRequests(WireCeiling).Limit);
    }

    [TestMethod]
    public async Task TheAuthorizedCanaryRunsOnceAndRetainsItsResult()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Live publisher canary. Set {EnableVariable}=1 for the single owner-authorized run; "
                + "it is skipped by default so ordinary suites send no publisher traffic.");
            return;
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(
            checkout,
            "artifacts",
            "formex-v2-manifestation-canary-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);

        // Authorization precondition. This completes before the budget, producer or HTTP session
        // exists, so missing, changed or non-member evidence cannot spend even a robots request.
        var acceptedBytes = await File.ReadAllBytesAsync(AcceptedEvidencePath(checkout));
        Assert.AreEqual(AcceptedEvidenceSha256, Sha256(acceptedBytes),
            "the accepted derivation input changed; refuse before traffic.");
        var acceptedReceipt = await store.CreateAsync(
            acceptedBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var reopened = await store.ReadAsync(acceptedReceipt.Reference, CancellationToken.None);
        var expression = ExpressionFromAcceptedEvidence(reopened.Span, acceptedReceipt);

        var plan = EuFormexManifestationDiscoveryPlan.Create();
        var renderer = RendererSource(checkout);
        var preflight = plan.BindCount(
            expression.Identity,
            EuFormexManifestationQueryPass.Pass1,
            "urn:uuid:1c25bcee-0bed-42f6-9992-e29dfcb725a2",
            "urn:uuid:a0fbbd58-a697-45c9-b52b-08095f984e71",
            renderer);
        Assert.AreEqual(HttpRequestMethod.Post, preflight.MachinePlan.Method);
        Assert.AreEqual(Endpoint, preflight.MachinePlan.TargetOriginAndPath);

        var budget = WireRequestBudget.OfWireRequests(WireCeiling);
        var producer = new EuFormexManifestationEnumerationProducer(store, TimeProvider.System);
        var result = await producer.RunAsync(
            new EuFormexManifestationRunRequest(
                plan,
                expression,
                "urn:uuid:1c25bcee-0bed-42f6-9992-e29dfcb725a2",
                renderer,
                budget),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        var summaryBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "formex_manifestation_v2_canary_result/1",
            acceptedEvidenceSha256 = AcceptedEvidenceSha256,
            acceptedEvidenceReceipt = acceptedReceipt.Reference,
            work = Work,
            expression = Expression,
            endpoint = Endpoint,
            method = "POST",
            wireCeiling = WireCeiling,
            productCeiling = ProductCeiling,
            plan = new
            {
                plan.ArtifactRef,
                plan.CountQueryFamilyRef,
                plan.PageQueryFamilyRef,
                partitionKey = EuFormexManifestationDiscoveryPlan.PartitionKeyFor(expression.Identity),
            },
            result.Delivered,
            refusal = result.Refusal.ToString(),
            result.Detail,
            result.ProductRequestCount,
            wireSpent = result.WireBudget.Spent,
            wireLimit = result.WireBudget.Limit,
            result.IsFormexEligible,
            proof = result.Proof is null ? null : new
            {
                result.Proof.FamilyKey,
                result.Proof.DeliveredRowCount,
                result.Proof.CanonicalKeyDigest,
                acquisitionRunSha256 = result.Proof.AcquisitionRunRef.Sha256,
            },
            manifestations = result.ManifestationTypes?.Select(static manifestation => new
            {
                manifestation.PublisherManifestationIri,
                manifestation.PublisherType,
                manifestation.Multiplicity,
                manifestation.SourceObservationId,
            }).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true });
        var summaryPath = Path.Combine(root, "result.json");
        await File.WriteAllBytesAsync(summaryPath, summaryBytes);
        var summaryReceipt = await store.CreateAsync(
            summaryBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        Console.WriteLine($"CANARY|root|{root}");
        Console.WriteLine($"CANARY|summary|{summaryPath}|{summaryReceipt.Reference.ContentSha256}");

        Assert.IsLessThanOrEqualTo(WireCeiling, result.WireBudget.Spent);
        Assert.IsLessThanOrEqualTo(ProductCeiling, result.ProductRequestCount);
        Assert.AreEqual(WireCeiling, result.WireBudget.Limit);
        Assert.AreEqual(expression.Identity, result.ExpressionIdentity);
        if (!result.Delivered)
        {
            Assert.Inconclusive(
                $"The single authorized run retained refusal {result.Refusal}: {result.Detail}. "
                + $"Evidence: {summaryPath}");
        }
    }

    private static LanguageScopedExpression ExpressionFromAcceptedEvidence(
        ReadOnlySpan<byte> bytes,
        DurableBlobWriteReceipt? retainedReceipt = null)
    {
        using var document = JsonDocument.Parse(bytes.ToArray());
        var rows = document.RootElement.GetProperty("results").GetProperty("bindings")
            .EnumerateArray()
            .Where(static row => String(row, "parent") == Work && String(row, "object") == Expression)
            .ToArray();
        var belongs = rows.Count(row =>
            String(row, "predicate") == BelongsToWork && String(row, "value") == Work);
        var languages = rows.Where(row => String(row, "predicate") == UsesLanguage)
            .Select(row => String(row, "value"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (belongs != 1 || languages.Length != 1)
        {
            throw new InvalidDataException(
                "The accepted derivation evidence must contain exactly one work membership and one language for the pinned expression.");
        }

        var digest = Sha256(bytes);
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, digest, bytes.Length, CustodyClass.NightlyFloor90d);
        var receipt = retainedReceipt ?? new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                null,
                CustodyProtection.NotEnforced,
                DateTimeOffset.Parse("2026-09-05T00:00:00Z"),
                null));
        var profile = new SourceArtifactRef(
            "urn:uuid:9ff87208-d7cf-4dc8-bb80-17b5bf063d51",
            "9e8ecd81b2b10be6aab9c1af6f8b49c11712388cea6fa6d348def9c39a7b49d7");
        var canonicalKey = "eu-language-scoped-expression:" + Expression;
        return LanguageScopedExpression.FromRetainedSource(
            new LanguageScopedExpressionIdentity(Work, Expression),
            languages[0],
            null,
            new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Cellar,
                new SourceRegistryMemberRef(profile, "expression"),
                Expression,
                canonicalKey,
                Sha256(Encoding.UTF8.GetBytes(canonicalKey)),
                profile,
                null),
            LanguageScopedExpressionLineage.FromContributions(
            [
                new(LanguageScopedExpressionContribution.IdentityAndLanguage, receipt),
            ]));
    }

    private static string String(JsonElement row, string name) =>
        row.GetProperty(name).GetProperty("value").GetString()
        ?? throw new InvalidDataException($"{name} has no string value.");

    private static byte[] AcceptedEvidenceBody() => Encoding.UTF8.GetBytes(
        "{\"results\":{\"bindings\":["
        + Row(BelongsToWork, Work) + "," + Row(UsesLanguage,
            "http://publications.europa.eu/resource/authority/language/SWE") + "]}}");

    private static string Row(string predicate, string value) =>
        "{\"parent\":{" + Term(Work) + "},\"object\":{" + Term(Expression)
        + "},\"predicate\":{" + Term(predicate) + "},\"value\":{" + Term(value) + "}}";

    private static string Term(string value) => "\"value\":" + JsonSerializer.Serialize(value);

    private static MachineQueryRendererSource RendererSource(string checkout)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            checkout, "src", "Lex.V3.Contracts", "Source", "Europe",
            "EuFormexManifestationDiscoveryPlan.cs"));
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:a74acb6f-735a-445d-a983-0424fb9003ca",
                Sha256(bytes)),
            bytes);
    }

    private static string AcceptedEvidencePath(string checkout) => Path.GetFullPath(Path.Combine(
        checkout,
        "..", "..", "coordination", "acceptance-evidence-stage1-eu", "seat-run-23de0372",
        "nightly-floor-90d", AcceptedEvidenceSha256));

    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}

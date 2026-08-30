using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

[TestClass]
public sealed class PreviewSecurityBoundaryTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void ContractWriterRejectsANullRoot()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ContractJson.Serialize<PreviewPayload>(null!));
    }

    [TestMethod]
    public void FreshnessBindsAndValidatesTheWireHealthValue()
    {
        var freshness = new PreviewFreshness(
            DateTimeOffset.Parse("2026-08-30T18:00:00Z"),
            PreviewUpstreamHealth.NotApplicableSynthetic);
        var json = ContractJson.Serialize(freshness);

        StringAssert.Contains(json, "\"upstream_health\":\"not_applicable_synthetic\"");
        Assert.ThrowsExactly<JsonException>(() => ContractJson.Deserialize<PreviewFreshness>(
            json.Replace("not_applicable_synthetic", "healthy", StringComparison.Ordinal)));
        Assert.ThrowsExactly<ArgumentException>(() => new PreviewFreshness(
            DateTimeOffset.Parse("2026-08-30T18:00:00Z"),
            (PreviewUpstreamHealth)999));
    }

    [TestMethod]
    [DataRow("req_0123456789abcdef0123456789abcde")]
    [DataRow("req_0123456789abcdef0123456789abcdef0")]
    [DataRow("req_0123456789ABCDEF0123456789ABCDEF")]
    [DataRow("req_can_i_be_fired_while_sick_000000")]
    [DataRow("req_11111111111111111111111111111111")]
    public void RequestReferencesRejectAnythingExceptOneOpaqueShape(string requestRef)
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateContext(requestRef: requestRef));
    }

    [TestMethod]
    public void RequestedCoordinatesRejectProseAndFamilyMismatches()
    {
        _ = CreateRefusal(IdentifierFamily.Eli, "eli/synthetic-preview");

        Assert.ThrowsExactly<ArgumentException>(() => CreateRefusal(
            IdentifierFamily.Eli,
            "can I be fired while sick"));
        Assert.ThrowsExactly<ArgumentException>(() => CreateRefusal(
            IdentifierFamily.Eli,
            "eli/lu/loi/2099/01/01/n1"));
        Assert.ThrowsExactly<ArgumentException>(() => CreateRefusal(
            IdentifierFamily.Eli,
            "eli/health_status_cancer_termination"));
        Assert.ThrowsExactly<ArgumentException>(() => CreateRefusal(
            IdentifierFamily.Celex,
            "eli/synthetic-preview"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateRefusal(
            (IdentifierFamily)999,
            "eli/synthetic-preview"));
    }

    [TestMethod]
    public void PublisherSearchActionsAreFixedAndCarryNoUserInput()
    {
        Assert.AreEqual(
            "https://legilux.public.lu/search",
            PublisherSearchAction.Create(PublisherId.LuLegilux).Uri.AbsoluteUri);
        Assert.AreEqual(
            "https://eur-lex.europa.eu/advanced-search-form.html",
            PublisherSearchAction.Create(PublisherId.EuEurLex).Uri.AbsoluteUri);

        foreach (var hostile in new[]
                 {
                     "http://legilux.public.lu/search",
                     "https://evil.example/search",
                     "https://legilux.public.lu:444/search",
                     "https://legilux.public.lu/search?q=health",
                     "https://legilux.public.lu/search#health",
                     "https://user@legilux.public.lu/search",
                     "https://legilux.public.lu/other",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new PublisherSearchAction(
                "publisher_search",
                PublisherId.LuLegilux,
                new Uri(hostile, UriKind.Absolute)), hostile);
        }

        Assert.ThrowsExactly<ArgumentException>(() => new PublisherSearchAction(
            "other_action",
            PublisherId.LuLegilux,
            new Uri("https://legilux.public.lu/search", UriKind.Absolute)));

        var action = JsonNode.Parse(ContractJson.Serialize(
            PublisherSearchAction.Create(PublisherId.LuLegilux)))!.AsObject();
        action.Remove("kind");
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<PublisherSearchAction>(action.ToJsonString()));
    }

    [TestMethod]
    public void HeldRecordCandidatesCarryIdentityNotCallerControlledNavigation()
    {
        var candidate = new HeldRecordCandidate(
            "preview:held:lu-legilux",
            "Loi de l'été",
            PublisherId.LuLegilux);
        var json = ContractJson.Serialize(candidate);

        Assert.IsFalse(json.Contains("permalink", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("uri", StringComparison.Ordinal));
        foreach (var hostile in new[]
                 {
                     "preview:held:lu-other",
                     "can I be fired while sick",
                     "https://evil.example/phish",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new HeldRecordCandidate(
                hostile,
                "Candidate",
                PublisherId.LuLegilux), hostile);
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HeldRecordCandidate(
            "preview:held:lu-legilux",
            "Candidate",
            (PublisherId)999));
    }

    [TestMethod]
    public void RefusalCollectionsRejectDuplicateLogicalCandidatesAndNonCanonicalOrder()
    {
        var luFirst = new HeldRecordCandidate(
            "preview:held:lu-legilux",
            "Candidate A",
            PublisherId.LuLegilux);
        var luDuplicate = new HeldRecordCandidate(
            "preview:held:lu-legilux",
            "Candidate B",
            PublisherId.LuLegilux);
        var eu = new HeldRecordCandidate(
            "preview:held:eu-eurlex",
            "Candidate EU",
            PublisherId.EuEurLex);

        Assert.ThrowsExactly<ArgumentException>(() => IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            new[] { PublisherId.LuLegilux },
            new[] { luFirst, luDuplicate },
            new[] { PublisherSearchAction.Create(PublisherId.LuLegilux) },
            new[] { WhatWouldAnswerAction.CorrectedIdentifier }));
        Assert.ThrowsExactly<ArgumentException>(() => IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            new[] { PublisherId.LuLegilux, PublisherId.EuEurLex },
            new[] { eu, luFirst },
            new[]
            {
                PublisherSearchAction.Create(PublisherId.LuLegilux),
                PublisherSearchAction.Create(PublisherId.EuEurLex),
            },
            new[] { WhatWouldAnswerAction.CorrectedIdentifier }));
        Assert.ThrowsExactly<ArgumentException>(() => IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            new[] { PublisherId.LuLegilux },
            Array.Empty<HeldRecordCandidate>(),
            new[] { PublisherSearchAction.Create(PublisherId.LuLegilux) },
            new[]
            {
                WhatWouldAnswerAction.NewOfficialObservation,
                WhatWouldAnswerAction.CorrectedIdentifier,
            }));
    }

    [TestMethod]
    public void ObjectSetsAndCatalogsRejectNonCanonicalOrder()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "preview-objects",
            new PreviewObject[]
            {
                CreateCoordinate(
                    BodyHoldingState.NotHeld,
                    PreviewBodyDispositionReason.UnknownPendingEvidence,
                    body: null,
                    bodySha256: null,
                    objectId: "preview-object-b"),
                CreateCoordinate(
                    BodyHoldingState.NotHeld,
                    PreviewBodyDispositionReason.UnknownPendingEvidence,
                    body: null,
                    bodySha256: null,
                    objectId: "preview-object-a"),
            }));

        Assert.ThrowsExactly<ArgumentException>(() => new PreviewOperationCatalog(
            V3SchemaIds.PreviewOperationCatalog,
            "preview-catalog",
            new[] { CreateOperation("search"), CreateOperation("resolve") }));
    }

    [TestMethod]
    public void AbstractContractUnionsCannotBeSubclassedOutsideTheAssembly()
    {
        foreach (var type in new[] { typeof(PreviewObject), typeof(PreviewEnvelope) })
        {
            var constructors = type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.IsNotEmpty(constructors, type.Name);
            Assert.IsTrue(
                constructors.All(static constructor => constructor.IsFamilyAndAssembly),
                $"{type.Name} constructors must remain private protected.");
        }
    }

    [TestMethod]
    public void CandidateTitlesUseExactUnicodeScalarAndVisibleTextRules()
    {
        var atLimit = string.Concat(Enumerable.Repeat("😀", 512));
        _ = new HeldRecordCandidate(
            "preview:held:lu-legilux",
            atLimit,
            PublisherId.LuLegilux);

        Assert.ThrowsExactly<ArgumentException>(() => new HeldRecordCandidate(
            "preview:held:lu-legilux",
            atLimit + "😀",
            PublisherId.LuLegilux));
        foreach (var invalid in new[] { " ", "\u2003", "a\u0085b", "a\u2028b", "a\u2029b", "\ud800" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new HeldRecordCandidate(
                "preview:held:lu-legilux",
                invalid,
                PublisherId.LuLegilux), invalid);
        }

        _ = new HeldRecordCandidate(
            "preview:held:lu-legilux",
            "\u180e\ufeff",
            PublisherId.LuLegilux);
    }

    [TestMethod]
    public void PreviewWhitespaceSetIsFrozenRatherThanFrameworkDependent()
    {
        var whitespace = new[]
        {
            0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0020, 0x0085, 0x00a0,
            0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006,
            0x2007, 0x2008, 0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f,
            0x3000,
        };
        foreach (var scalar in whitespace)
        {
            Assert.IsTrue(ContractValidation.IsPreviewWhitespace(new Rune(scalar)));
        }

        foreach (var scalar in new[] { 0x0008, 0x000e, 0x001f, 0x0021, 0x180e, 0x200b, 0xfeff })
        {
            Assert.IsFalse(ContractValidation.IsPreviewWhitespace(new Rune(scalar)));
        }
    }

    [TestMethod]
    public void PublicPreviewBodyRequiresItsExactStrictUtf8Digest()
    {
        const string body = "texte synthétique";
        var digest = PreviewSchemaExporter.ComputeSha256(
            new UTF8Encoding(false, true).GetBytes(body));
        var accepted = CreateCoordinate(
            BodyHoldingState.HeldPublic,
            PreviewBodyDispositionReason.SyntheticFixture,
            body,
            digest);

        Assert.AreEqual(body, accepted.Body);
        Assert.ThrowsExactly<ArgumentException>(() => CreateCoordinate(
            BodyHoldingState.HeldPublic,
            PreviewBodyDispositionReason.SyntheticFixture,
            body + "!",
            digest));
        Assert.ThrowsExactly<ArgumentException>(() => CreateCoordinate(
            BodyHoldingState.HeldPublic,
            PreviewBodyDispositionReason.SyntheticFixture,
            "\ud800",
            Digest));
    }

    [TestMethod]
    public void LiteralReplacementScalarRemainsDistinctFromInvalidUtf16BeforeHashing()
    {
        const string literalReplacement = "\ufffd";
        var exactBytes = new byte[] { 0xef, 0xbf, 0xbd };
        var digest = PreviewSchemaExporter.ComputeSha256(exactBytes);
        var coordinate = CreateCoordinate(
            BodyHoldingState.HeldPublic,
            PreviewBodyDispositionReason.SyntheticFixture,
            literalReplacement,
            digest);
        var objectSet = new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "replacement-scalar",
            new PreviewObject[] { coordinate });
        var canonical = PreviewSchemaExporter.GetDocumentCanonicalBytes(objectSet);

        Assert.AreEqual(digest, coordinate.BodySha256);
        Assert.IsGreaterThanOrEqualTo(0, canonical.AsSpan().IndexOf(exactBytes));
        Assert.ThrowsExactly<ArgumentException>(() => CreateCoordinate(
            BodyHoldingState.HeldPublic,
            PreviewBodyDispositionReason.SyntheticFixture,
            "\ud800",
            digest));
    }

    [TestMethod]
    public void WithheldAndNotHeldBodiesHaveDistinctEvidenceShapes()
    {
        var withheld = CreateCoordinate(
            BodyHoldingState.HeldWithheld,
            PreviewBodyDispositionReason.SyntheticFixtureWithheld,
            body: null,
            bodySha256: Digest);
        var notHeld = CreateCoordinate(
            BodyHoldingState.NotHeld,
            PreviewBodyDispositionReason.UnknownPendingEvidence,
            body: null,
            bodySha256: null);

        Assert.AreEqual(Digest, withheld.BodySha256);
        Assert.IsNull(notHeld.BodySha256);
        Assert.ThrowsExactly<ArgumentException>(() => CreateCoordinate(
            BodyHoldingState.HeldWithheld,
            PreviewBodyDispositionReason.SyntheticFixtureWithheld,
            "hidden",
            Digest));
        Assert.ThrowsExactly<ArgumentException>(() => CreateCoordinate(
            BodyHoldingState.NotHeld,
            PreviewBodyDispositionReason.UnknownPendingEvidence,
            body: null,
            bodySha256: Digest));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateCoordinate(
            (BodyHoldingState)999,
            PreviewBodyDispositionReason.UnknownPendingEvidence,
            body: null,
            bodySha256: null));
    }

    [TestMethod]
    public void EveryDirectEnumBoundaryRejectsUndefinedValues()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateContext(
            provisionality: (PreviewProvisionality)999));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            new[] { PublisherId.LuLegilux },
            Array.Empty<HeldRecordCandidate>(),
            new[] { PublisherSearchAction.Create(PublisherId.LuLegilux) },
            new[] { (WhatWouldAnswerAction)999 }));
        Assert.ThrowsExactly<ArgumentException>(() => IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            new[] { PublisherId.EuEurLex, PublisherId.LuLegilux },
            Array.Empty<HeldRecordCandidate>(),
            new[]
            {
                PublisherSearchAction.Create(PublisherId.EuEurLex),
                PublisherSearchAction.Create(PublisherId.LuLegilux),
            },
            new[] { WhatWouldAnswerAction.CorrectedIdentifier }));
        Assert.ThrowsExactly<ArgumentNullException>(() => new PreviewAttestation(
            "preview_mechanics_only",
            "ECDSA-P256-SHA256",
            "ieee-p1363",
            null!));
        Assert.ThrowsExactly<ArgumentException>(() => IdentifierUnknownRefusal.Create(
            IdentifierFamily.Eli,
            "eli/synthetic-preview",
            new[] { PublisherId.LuLegilux },
            new HeldRecordCandidate[] { null! },
            new[] { PublisherSearchAction.Create(PublisherId.LuLegilux) },
            new[] { WhatWouldAnswerAction.CorrectedIdentifier }));
    }

    private static PreviewSyntheticCoordinate CreateCoordinate(
        BodyHoldingState state,
        PreviewBodyDispositionReason disposition,
        string? body,
        string? bodySha256,
        string objectId = "preview-object") => new(
        objectId,
        synthetic: true,
        "preview:work:1",
        "preview:version:1",
        "preview:anchor:1",
        state,
        disposition,
        body,
        bodySha256);

    private static PreviewOperationDescriptor CreateOperation(string operationId) => new(
        operationId,
        new ContractReference("preview-request/test", Digest),
        new ContractReference("preview-success/test", Digest),
        new[] { RefusalCode.IdentifierUnknown },
        "identifier_ordinal",
        "preview_mechanics_only",
        "rest/preview",
        "mcp/preview",
        "html/preview");

    private static IdentifierUnknownRefusal CreateRefusal(
        IdentifierFamily family,
        string coordinate) => IdentifierUnknownRefusal.Create(
        family,
        coordinate,
        new[] { PublisherId.LuLegilux },
        Array.Empty<HeldRecordCandidate>(),
        new[] { PublisherSearchAction.Create(PublisherId.LuLegilux) },
        new[] { WhatWouldAnswerAction.CorrectedIdentifier });

    private static PreviewEnvelopeContext CreateContext(
        string requestRef = "req_0123456789abcdef0123456789abcdef",
        PreviewProvisionality provisionality = PreviewProvisionality.All) => new(
        requestRef,
        new PreviewOperationReference("resolve", "preview-catalog", Digest),
        new PreviewRefusalRegistryReference(
            "preview-registry",
            V3SchemaIds.PreviewRefusalRegistry,
            Digest),
        new PreviewSnapshotReference("preview-snapshot", Digest),
        new PreviewArtifactReference("preview-artifact"),
        "preview-index/1",
        new ComponentIdentity("preview-runtime", Digest),
        new ComponentIdentity("preview-builder", Digest),
        PreviewCapabilityState.MechanicsOnly,
        new PreviewFreshness(
            DateTimeOffset.Parse("2026-08-30T18:00:00Z"),
            PreviewUpstreamHealth.NotApplicableSynthetic),
        "synthetic-preview-no-jurisdiction",
        provisionality,
        PreviewSourceContext.SyntheticTest);
}

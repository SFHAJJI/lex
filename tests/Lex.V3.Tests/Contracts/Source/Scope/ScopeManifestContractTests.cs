using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Tests.Contracts.Source.Scope;

[TestClass]
public sealed class ScopeManifestContractTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void SelectorStatesRequireExactTypedEvidenceVariants()
    {
        _ = Present(["ENG", "FRA"]);
        _ = Absent();
        _ = Conflict(["ENG", "FRA"], causeMemberOrdinal: 0);
        _ = NotApplicable(ruleOrdinal: 0);

        Assert.ThrowsExactly<ArgumentException>(() => new ScopeSelectorEvidence(
            ScopeSelectorState.PublisherValuePresent,
            [],
            ScopeSelectorEvidenceKind.ObservedValueSet,
            0,
            null,
            null));
        Assert.ThrowsExactly<ArgumentException>(() => new ScopeSelectorEvidence(
            ScopeSelectorState.PublisherValuePresent,
            ["FRA", "ENG"],
            ScopeSelectorEvidenceKind.ObservedValueSet,
            0,
            null,
            null));
        Assert.ThrowsExactly<ArgumentException>(() => new ScopeSelectorEvidence(
            ScopeSelectorState.PublisherValueAbsent,
            [],
            ScopeSelectorEvidenceKind.ObservedValueSet,
            0,
            null,
            null));
        Assert.ThrowsExactly<ArgumentException>(() => new ScopeSelectorEvidence(
            ScopeSelectorState.PublisherValueConflict,
            ["ENG", "FRA"],
            ScopeSelectorEvidenceKind.ObservedConflictingValueSet,
            0,
            null,
            null));
        Assert.ThrowsExactly<ArgumentException>(() => new ScopeSelectorEvidence(
            ScopeSelectorState.SelectorNotApplicable,
            [],
            null,
            null,
            null,
            null));
    }

    [TestMethod]
    public void ReducerRequiresCompleteUniversesAndResolvedObservationBindings()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("one"));
        var resolver = ExactResolver.For(profile, evidence, [input]);

        _ = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);

        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [Change(input, selectors: input.Selectors.Skip(1).ToArray())],
            resolver));
        Assert.ThrowsExactly<ArgumentException>(() => ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [Change(input, evaluations: input.RuleEvaluations.Skip(1).ToArray())],
            resolver));
        Assert.ThrowsExactly<ArgumentException>(() => ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [Change(
                input,
                evaluations: [.. input.RuleEvaluations, input.RuleEvaluations[0]])],
            resolver));
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [input],
            new RejectAllResolver()));

        var wrongEvidence = new[] { profile.SourceProfileRef };
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            wrongEvidence,
            [input.ObjectRef],
            [input],
            resolver));

        var unusedEvidence = new[]
        {
            evidence[0],
            Artifact("22222222-2222-4222-8222-222222222222"),
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            unusedEvidence,
            [input.ObjectRef],
            [input],
            ExactResolver.For(profile, unusedEvidence, [input])));

        var notApplicableInput = Change(
            input,
            selectors: [NotApplicable(0), NotApplicable(1)]);
        SourceArtifactRef[] noEvidence = [];
        _ = ScopeReducer.Reduce(
            profile,
            noEvidence,
            [input.ObjectRef],
            [notApplicableInput],
            ExactResolver.For(profile, noEvidence, [notApplicableInput]));
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            noEvidence,
            [input.ObjectRef],
            [notApplicableInput],
            new RejectAllResolver()));
    }

    [TestMethod]
    public void ZeroWinnerAndHardPrecedenceFailOrProjectExactly()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("precedence"));

        var noRecord = input.RuleEvaluations.ToArray();
        noRecord[0] = NotMatched(0);
        var noRecordInput = Change(input, evaluations: noRecord);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [noRecordInput],
            ExactResolver.For(profile, evidence, [noRecordInput])));

        var exactDenial = input.RuleEvaluations.ToArray();
        exactDenial[4] = Matched(
            4,
            ScopeRuleEffect.ExactDenial,
            ScopeDisposition.Point,
            [],
            []);
        var exactInput = Change(input, evaluations: exactDenial);
        var exact = ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [exactInput],
            ExactResolver.For(profile, evidence, [exactInput]));
        var exactBody = ScopeReducer.ReduceRequest(
            exact,
            input.ObjectRef,
            [ScopeAxis.Body]).AllAxisResults.Single(result => result.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.Point, exactBody.Disposition);
        Assert.AreEqual(4, exactBody.WinningRuleOrdinal);
        Assert.IsEmpty(exact.Manifest.BodyCandidateOrdinals);

        var positiveNever = input.RuleEvaluations.ToArray();
        positiveNever[4] = Matched(
            4,
            ScopeRuleEffect.Positive,
            ScopeDisposition.NeverIngest,
            [],
            []);
        var neverInput = Change(input, evaluations: positiveNever);
        var never = ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [neverInput],
            ExactResolver.For(profile, evidence, [neverInput]));
        var neverBody = ScopeReducer.ReduceRequest(
            never,
            input.ObjectRef,
            [ScopeAxis.Body]).AllAxisResults.Single(result => result.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.NeverIngest, neverBody.Disposition);
        Assert.IsEmpty(never.Manifest.BodyCandidateOrdinals);
    }

    [TestMethod]
    public void PointQuarantineAndNeverNeverProjectBodyCandidates()
    {
        foreach (var disposition in new[]
                 {
                     ScopeDisposition.Point,
                     ScopeDisposition.TypedQuarantine,
                     ScopeDisposition.NeverIngest,
                 })
        {
            var profile = Profile();
            var evidence = EvidenceArtifacts();
            var input = ValidInput(profile, Object(disposition.ToString()));
            var evaluations = input.RuleEvaluations.ToArray();
            evaluations[1] = Matched(
                1,
                ScopeRuleEffect.Positive,
                disposition,
                [],
                []);
            var changed = Change(input, evaluations: evaluations);
            var verified = ScopeReducer.Reduce(
                profile,
                evidence,
                [input.ObjectRef],
                [changed],
                ExactResolver.For(profile, evidence, [changed]));
            Assert.IsEmpty(verified.Manifest.BodyCandidateOrdinals, disposition.ToString());
        }
    }

    [TestMethod]
    public void RequestReductionRetainsAxesAndUsesIntersectionOnlyForAllAccepted()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("request"));
        var verified = ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [input],
            ExactResolver.For(profile, evidence, [input]));
        var reduction = ScopeReducer.ReduceRequest(
            verified,
            input.ObjectRef,
            [ScopeAxis.Body, ScopeAxis.Relation]);

        Assert.HasCount(4, reduction.AllAxisResults);
        Assert.AreEqual(ScopeDisposition.AcceptedSelected, reduction.CompositeDisposition);
        CollectionAssert.AreEqual(
            new[] { MemberOrdinal(profile, ProfileRef(), "shared") },
            reduction.CompositeCapabilityMemberOrdinals.ToArray());

        var evaluations = input.RuleEvaluations.ToArray();
        evaluations[2] = Matched(
            2,
            ScopeRuleEffect.Positive,
            ScopeDisposition.TypedQuarantine,
            [],
            []);
        var changed = Change(input, evaluations: evaluations);
        var limited = ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [changed],
            ExactResolver.For(profile, evidence, [changed]));
        var limitedReduction = ScopeReducer.ReduceRequest(
            limited,
            input.ObjectRef,
            [ScopeAxis.Body, ScopeAxis.Relation]);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, limitedReduction.CompositeDisposition);
        Assert.IsEmpty(limitedReduction.CompositeCapabilityMemberOrdinals);
        Assert.IsNotEmpty(limitedReduction.AllAxisResults
            .Single(result => result.Axis == ScopeAxis.Body)
            .CapabilityMemberOrdinals);

        Assert.ThrowsExactly<ArgumentException>(() => ScopeReducer.ReduceRequest(
            verified,
            input.ObjectRef,
            []));
        Assert.ThrowsExactly<ArgumentException>(() => ScopeReducer.ReduceRequest(
            verified,
            input.ObjectRef,
            [ScopeAxis.Body, ScopeAxis.Body]));
    }

    [TestMethod]
    public void CompactCanonicalOutputIsOrderIndependentAndTamperingFailsClosed()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var first = ValidInput(profile, Object("first"));
        var second = ValidInput(profile, Object("second"));
        var forward = ScopeReducer.Reduce(
            profile,
            evidence,
            [first.ObjectRef, second.ObjectRef],
            [first, second],
            ExactResolver.For(profile, evidence, [first, second]));
        var reverse = ScopeReducer.Reduce(
            profile,
            evidence,
            [second.ObjectRef, first.ObjectRef],
            [
                Change(second, evaluations: second.RuleEvaluations.Reverse().ToArray()),
                first,
            ],
            ExactResolver.For(profile, evidence, [first, second]));

        CollectionAssert.AreEqual(CanonicalBytes(forward), CanonicalBytes(reverse));
        var canonical = Encoding.UTF8.GetString(CanonicalBytes(forward));
        Assert.AreEqual(1, Count(canonical, "cellar:work:first"));
        StringAssert.Contains(canonical, "\"rule_match_bits_base64_url\":\"Dw\"");
        Assert.IsFalse(canonical.Contains("rule_evaluations", StringComparison.Ordinal));

        var manifest = forward.Manifest;
        var row = manifest.Rows[0];
        var wrongDigest = ReplaceRow(
            manifest,
            0,
            new ScopeManifestRow(
                row.ObjectOrdinal,
                row.Selectors,
                row.RuleMatchBitsBase64Url,
                row.MatchedEvaluations,
                row.AxisWinningRuleOrdinals,
                new string('0', 64)));
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            wrongDigest,
            ExactResolver.For(profile, evidence, [first, second])));

        var wrongWinner = ReplaceRow(
            manifest,
            0,
            new ScopeManifestRow(
                row.ObjectOrdinal,
                row.Selectors,
                row.RuleMatchBitsBase64Url,
                row.MatchedEvaluations,
                [4, .. row.AxisWinningRuleOrdinals.Skip(1)],
                row.RowSha256));
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            wrongWinner,
            ExactResolver.For(profile, evidence, [first, second])));

        var badPaddingBits = ReplaceRow(
            manifest,
            0,
            new ScopeManifestRow(
                row.ObjectOrdinal,
                row.Selectors,
                "_w",
                row.MatchedEvaluations,
                row.AxisWinningRuleOrdinals,
                row.RowSha256));
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            badPaddingBits,
            ExactResolver.For(profile, evidence, [first, second])));

        var firstAccounting = manifest.Accounting[0];
        var wrongAccounting = new ScopeManifest(
            manifest.Schema,
            manifest.Profile,
            manifest.CompleteEnumerationRef,
            manifest.OrderedEvidenceArtifacts,
            manifest.ObservedObjects,
            manifest.Rows,
            [
                new ScopeAccountingSet(
                    firstAccounting.Axis,
                    firstAccounting.Disposition,
                    firstAccounting.ObjectOrdinals.Skip(1).ToArray()),
                .. manifest.Accounting.Skip(1),
            ],
            manifest.BodyCandidateOrdinals);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            wrongAccounting,
            ExactResolver.For(profile, evidence, [first, second])));
    }

    [TestMethod]
    public void EvidenceResolverBindsSelectorValuesCauseAndRuleOutcomes()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var original = ValidInput(profile, Object("evidence-binding"));
        var trustedResolver = ExactResolver.For(profile, evidence, [original]);

        var forgedValueSelectors = original.Selectors.ToArray();
        forgedValueSelectors[0] = Present(["forged-value-not-in-evidence"]);
        var forgedValue = Change(original, selectors: forgedValueSelectors);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            evidence,
            [original.ObjectRef],
            [forgedValue],
            trustedResolver));
        var forgedValueManifest = ScopeReducer.Reduce(
            profile,
            evidence,
            [original.ObjectRef],
            [forgedValue],
            ExactResolver.For(profile, evidence, [forgedValue])).Manifest;
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            forgedValueManifest,
            trustedResolver));

        var cause = MemberOrdinal(profile, ProfileRef(), "cause");
        var otherCause = MemberOrdinal(profile, ProfileRef(), "metadata");
        var conflictSelectors = original.Selectors.ToArray();
        conflictSelectors[0] = Conflict(["formex", "html"], cause);
        var conflict = Change(original, selectors: conflictSelectors);
        var conflictResolver = ExactResolver.For(profile, evidence, [conflict]);
        var forgedCauseSelectors = conflict.Selectors.ToArray();
        forgedCauseSelectors[0] = Conflict(["formex", "html"], otherCause);
        var forgedCause = Change(conflict, selectors: forgedCauseSelectors);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            evidence,
            [original.ObjectRef],
            [forgedCause],
            conflictResolver));
        var forgedCauseManifest = ScopeReducer.Reduce(
            profile,
            evidence,
            [original.ObjectRef],
            [forgedCause],
            ExactResolver.For(profile, evidence, [forgedCause])).Manifest;
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            forgedCauseManifest,
            conflictResolver));

        var forgedEvaluations = original.RuleEvaluations.ToArray();
        forgedEvaluations[1] = Matched(
            1,
            ScopeRuleEffect.Positive,
            ScopeDisposition.NeverIngest,
            [],
            []);
        var forgedOutcome = Change(original, evaluations: forgedEvaluations);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            evidence,
            [original.ObjectRef],
            [forgedOutcome],
            trustedResolver));
        var forgedOutcomeManifest = ScopeReducer.Reduce(
            profile,
            evidence,
            [original.ObjectRef],
            [forgedOutcome],
            ExactResolver.For(profile, evidence, [forgedOutcome])).Manifest;
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            forgedOutcomeManifest,
            trustedResolver));
    }

    [TestMethod]
    public void CompleteEnumerationEvidenceDistinguishesCompletedEmptyFromMissingOrWrong()
    {
        var profile = Profile();
        SourceArtifactRef[] noEvidence = [];
        ScopeObjectReductionInput[] noInputs = [];
        var completedEmptyResolver = ExactResolver.For(profile, noEvidence, noInputs);
        var completedEmpty = ScopeReducer.Reduce(
            profile,
            noEvidence,
            [],
            noInputs,
            completedEmptyResolver);

        Assert.HasCount(0, completedEmpty.Manifest.ObservedObjects);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            completedEmpty.Manifest,
            new RejectAllResolver()));

        var nonemptyInput = ValidInput(profile, Object("not-empty"));
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            completedEmpty.Manifest,
            ExactResolver.For(profile, EvidenceArtifacts(), [nonemptyInput])));

        var wrongEnumeration = new ScopeManifest(
            completedEmpty.Manifest.Schema,
            completedEmpty.Manifest.Profile,
            Artifact("44444444-4444-4444-8444-444444444444"),
            completedEmpty.Manifest.OrderedEvidenceArtifacts,
            completedEmpty.Manifest.ObservedObjects,
            completedEmpty.Manifest.Rows,
            completedEmpty.Manifest.Accounting,
            completedEmpty.Manifest.BodyCandidateOrdinals);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            wrongEnumeration,
            completedEmptyResolver));
    }

    [TestMethod]
    public void OnlyAResolvedVerifiedManifestCanAnswerRequestsOrBePublished()
    {
        var overload = typeof(ScopeReducer).GetMethods()
            .Single(method => method.Name == nameof(ScopeReducer.ReduceRequest));
        Assert.AreEqual(typeof(VerifiedScopeManifest), overload.GetParameters()[0].ParameterType);
        Assert.IsFalse(typeof(VerifiedScopeManifest).GetConstructors().Any());
        Assert.IsFalse(typeof(ScopeManifestCanonicalWriter).GetMethods()
            .Any(method => method.ReturnType == typeof(byte[])));

        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("unverified"));
        var verified = ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [input],
            ExactResolver.For(profile, evidence, [input]));
        var raw = verified.Manifest;
        var invalid = new ScopeManifest(
            raw.Schema,
            raw.Profile,
            raw.CompleteEnumerationRef,
            raw.OrderedEvidenceArtifacts,
            [],
            raw.Rows,
            raw.Accounting,
            raw.BodyCandidateOrdinals);
        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.VerifyAndOpen(
            invalid,
            ExactResolver.For(profile, evidence, [input])));
    }

    [TestMethod]
    public void SchemaIsClosedPositionalDeterministicAndCheckedIn()
    {
        var first = ScopeSchemaExporter.ExportUtf8();
        var second = ScopeSchemaExporter.ExportUtf8();
        CollectionAssert.AreEqual(first, second);

        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("schema"));
        var verified = ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [input],
            ExactResolver.For(profile, evidence, [input]));
        var schema = JsonSchema.FromText(Encoding.UTF8.GetString(first));
        using var document = JsonDocument.Parse(ContractJson.Serialize(verified.Manifest));
        Assert.IsTrue(Evaluate(schema, document.RootElement).IsValid);

        var atScalarLimit = string.Concat(Enumerable.Repeat("\U0001F600", 4_096));
        var aboveScalarLimit = string.Concat(Enumerable.Repeat("\U0001F600", 4_097));
        _ = Present([atScalarLimit]);
        Assert.ThrowsExactly<ArgumentException>(() => Present([aboveScalarLimit]));
        var atScalarLimitDocument = JsonNode.Parse(
            ContractJson.Serialize(verified.Manifest))!.AsObject();
        atScalarLimitDocument["rows"]![0]!["selectors"]![0]!["canonical_values"]![0] =
            atScalarLimit;
        Assert.IsTrue(Evaluate(schema, atScalarLimitDocument).IsValid);
        var aboveScalarLimitDocument = JsonNode.Parse(
            ContractJson.Serialize(verified.Manifest))!.AsObject();
        aboveScalarLimitDocument["rows"]![0]!["selectors"]![0]!["canonical_values"]![0] =
            aboveScalarLimit;
        Assert.IsFalse(Evaluate(schema, aboveScalarLimitDocument).IsValid);

        var unknown = JsonNode.Parse(ContractJson.Serialize(verified.Manifest))!.AsObject();
        unknown["unknown"] = true;
        Assert.IsFalse(Evaluate(schema, unknown).IsValid);

        var invalidSelector = JsonNode.Parse(ContractJson.Serialize(verified.Manifest))!.AsObject();
        invalidSelector["rows"]![0]!["selectors"]![0]!["state"] =
            "publisher_value_absent";
        Assert.IsFalse(Evaluate(schema, invalidSelector).IsValid);

        var wrongAccountingPosition = JsonNode.Parse(
            ContractJson.Serialize(verified.Manifest))!.AsObject();
        wrongAccountingPosition["accounting"]![0]!["axis"] = "body";
        Assert.IsFalse(Evaluate(schema, wrongAccountingPosition).IsValid);

        var shortWinners = JsonNode.Parse(ContractJson.Serialize(verified.Manifest))!.AsObject();
        shortWinners["rows"]![0]!["axis_winning_rule_ordinals"]!.AsArray().RemoveAt(0);
        Assert.IsFalse(Evaluate(schema, shortWinners).IsValid);

        var schemaText = Encoding.UTF8.GetString(first);
        Assert.AreEqual(2, Count(schemaText, "\"prefixItems\""));
        Assert.HasCount(14, ScopeManifestReaderOnlyInvariants.All);

        var checkedPath = Path.Combine(
            RepositoryRoot(),
            "schemas",
            "v3-source",
            "source-scope-manifest.schema.json");
        CollectionAssert.AreEqual(first, File.ReadAllBytes(checkedPath));
    }

    [TestMethod]
    public void RoundTripRetainsTheExactCanonicalWireShape()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("roundtrip"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [input],
            resolver);
        var json = ContractJson.Serialize(verified.Manifest);
        var roundTrip = ContractJson.Deserialize<ScopeManifest>(json);
        var reopened = ScopeReducer.VerifyAndOpen(roundTrip, resolver);

        Assert.AreEqual(
            json,
            Encoding.UTF8.GetString(CanonicalBytes(reopened)).TrimEnd('\n'));

        var numericEnum = JsonNode.Parse(json)!.AsObject();
        numericEnum["accounting"]![0]!["axis"] = 1;
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<ScopeManifest>(numericEnum.ToJsonString()));

        var caseDrift = JsonNode.Parse(json)!.AsObject();
        caseDrift["accounting"]![0]!["axis"] = "Record";
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<ScopeManifest>(caseDrift.ToJsonString()));

        var firstMemberEnd = json.IndexOf(',', StringComparison.Ordinal);
        var duplicateSchema = json.Insert(
            firstMemberEnd + 1,
            "\"schema\":\"lex-v3-source-scope-manifest/1\",");
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<ScopeManifest>(duplicateSchema));
    }

    [TestMethod]
    public void CompactProjectionAndStreamingWriterRemoveTheArrayCeiling()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var oneInput = ValidInput(profile, Object("000000"));
        var one = ScopeReducer.Reduce(
            profile,
            evidence,
            [oneInput.ObjectRef],
            [oneInput],
            ExactResolver.For(profile, evidence, [oneInput]));
        var oneLength = CanonicalBytes(one).Length;

        const int sampleCount = 1_000;
        var inputs = Enumerable.Range(0, sampleCount)
            .Select(index => ValidInput(profile, Object(index.ToString("D6"))))
            .ToArray();
        var canonicalInputs = inputs
            .OrderBy(
                static input => ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
                    input.ObjectRef),
                StringComparer.Ordinal)
            .ToArray();
        var sample = ScopeReducer.Reduce(
            profile,
            evidence,
            canonicalInputs.Select(static input => input.ObjectRef).ToArray(),
            canonicalInputs,
            ExactResolver.For(profile, evidence, canonicalInputs));
        var sampleLength = CanonicalBytes(sample).Length;
        var incrementalBytes = (sampleLength - oneLength) / (double)(sampleCount - 1);
        var projectedBytes = oneLength + (incrementalBytes * (555_000 - 1));
        Assert.IsLessThan(2_000, incrementalBytes);
        Assert.IsLessThan(int.MaxValue, projectedBytes);

        using var streamed = new MemoryStream();
        var streamedReceipt = ScopeManifestCanonicalWriter.WriteStreaming(
            streamed,
            profile,
            evidence,
            sampleCount,
            _ => canonicalInputs,
            ExactResolver.For(profile, evidence, canonicalInputs));
        CollectionAssert.AreEqual(CanonicalBytes(sample), streamed.ToArray());
        Assert.AreEqual(streamed.Length, streamedReceipt.CanonicalByteCount);
        Assert.AreEqual(sampleCount, streamedReceipt.ObjectCount);
        Assert.AreEqual(64, streamedReceipt.ManifestSha256.Length);
        Assert.AreEqual(64, streamedReceipt.InputSequenceSha256.Length);

        var counting = new OffsetCountingStream((long)int.MaxValue + 1);
        var receipt = ScopeManifestCanonicalWriter.WriteStreaming(
            counting,
            profile,
            evidence,
            1,
            _ => [oneInput],
            ExactResolver.For(profile, evidence, [oneInput]));
        Assert.IsGreaterThan((long)int.MaxValue, counting.Position);
        Assert.AreEqual(oneLength, receipt.CanonicalByteCount);
        Assert.AreEqual(64, receipt.ManifestSha256.Length);

        var last = ScopeReducer.ReduceRequest(
            sample,
            canonicalInputs[^1].ObjectRef,
            [ScopeAxis.Record]);
        Assert.AreEqual(canonicalInputs[^1].ObjectRef, last.ObjectRef);
    }

    [TestMethod]
    public void StreamingWriterRejectsOrderingCountAndPassDrift()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var first = ValidInput(profile, Object("stream-first"));
        var second = ValidInput(profile, Object("stream-second"));
        var ordered = new[] { first, second }
            .OrderBy(
                static input => ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
                    input.ObjectRef),
                StringComparer.Ordinal)
            .ToArray();
        var resolver = ExactResolver.For(profile, evidence, ordered);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ScopeManifestCanonicalWriter.WriteStreaming(
                Stream.Null,
                profile,
                evidence,
                2,
                _ => ordered.Reverse(),
                resolver));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ScopeManifestCanonicalWriter.WriteStreaming(
                Stream.Null,
                profile,
                evidence,
                2,
                _ => [ordered[0]],
                resolver));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ScopeManifestCanonicalWriter.WriteStreaming(
                Stream.Null,
                profile,
                evidence,
                1,
                _ => ordered,
                resolver));

        var pass = 0;
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ScopeManifestCanonicalWriter.WriteStreaming(
                Stream.Null,
                profile,
                evidence,
                1,
                _ => ++pass == 1 ? [ordered[0]] : [ordered[1]],
                resolver));
    }

    private static ScopeProfileBinding Profile()
    {
        var profileRef = ProfileRef();
        var tableRef = TableRef();
        var members = new[]
            {
                Member(profileRef, "body_candidate"),
                Member(profileRef, "body_text"),
                Member(profileRef, "cause"),
                Member(profileRef, "metadata"),
                Member(profileRef, "record_identity"),
                Member(profileRef, "relation"),
                Member(profileRef, "shared"),
                Member(profileRef, "supporting_document"),
                Member(tableRef, "body_allow"),
                Member(tableRef, "body_deny"),
                Member(tableRef, "format"),
                Member(tableRef, "language"),
                Member(tableRef, "record_allow"),
                Member(tableRef, "relation_allow"),
                Member(tableRef, "support_allow"),
            }
            .OrderBy(static member => member.RegistryRef.ResourceId, StringComparer.Ordinal)
            .ThenBy(static member => member.RegistryRef.Sha256, StringComparer.Ordinal)
            .ThenBy(static member => member.MemberKey, StringComparer.Ordinal)
            .ToArray();
        int Ordinal(SourceArtifactRef registry, string key) => Array.FindIndex(
            members,
            member => member.RegistryRef == registry && member.MemberKey == key);

        return new ScopeProfileBinding(
            profileRef,
            tableRef,
            members,
            [Ordinal(tableRef, "format"), Ordinal(tableRef, "language")],
            [
                new ScopeRuleBinding(ScopeAxis.Record, Ordinal(tableRef, "record_allow"), 0),
                new ScopeRuleBinding(ScopeAxis.Body, Ordinal(tableRef, "body_allow"), 1),
                new ScopeRuleBinding(ScopeAxis.Relation, Ordinal(tableRef, "relation_allow"), 2),
                new ScopeRuleBinding(
                    ScopeAxis.SupportingDocument,
                    Ordinal(tableRef, "support_allow"),
                    3),
                new ScopeRuleBinding(ScopeAxis.Body, Ordinal(tableRef, "body_deny"), 4),
            ],
            Ordinal(profileRef, "body_candidate"));
    }

    private static ScopeObjectReductionInput ValidInput(
        ScopeProfileBinding profile,
        SourceObjectRef objectRef)
    {
        var shared = MemberOrdinal(profile, ProfileRef(), "shared");
        return new ScopeObjectReductionInput(
            objectRef,
            [Present(["formex"]), Present(["FRA"])],
            [
                Matched(
                    0,
                    ScopeRuleEffect.Positive,
                    ScopeDisposition.AcceptedSelected,
                    [MemberOrdinal(profile, ProfileRef(), "record_identity")],
                    [MemberOrdinal(profile, ProfileRef(), "metadata"), shared]),
                Matched(
                    1,
                    ScopeRuleEffect.Positive,
                    ScopeDisposition.AcceptedSelected,
                    [profile.BodyCandidateRoleMemberOrdinal],
                    [MemberOrdinal(profile, ProfileRef(), "body_text"), shared]),
                Matched(
                    2,
                    ScopeRuleEffect.Positive,
                    ScopeDisposition.AcceptedSelected,
                    [MemberOrdinal(profile, ProfileRef(), "relation")],
                    [shared]),
                Matched(
                    3,
                    ScopeRuleEffect.Positive,
                    ScopeDisposition.AcceptedSelected,
                    [MemberOrdinal(profile, ProfileRef(), "supporting_document")],
                    [shared]),
                NotMatched(4),
            ]);
    }

    private static ScopeSelectorEvidence Present(IReadOnlyList<string> values) => new(
        ScopeSelectorState.PublisherValuePresent,
        values,
        ScopeSelectorEvidenceKind.ObservedValueSet,
        0,
        null,
        null);

    private static ScopeSelectorEvidence Absent() => new(
        ScopeSelectorState.PublisherValueAbsent,
        [],
        ScopeSelectorEvidenceKind.CompleteObservationAbsence,
        0,
        null,
        null);

    private static ScopeSelectorEvidence Conflict(
        IReadOnlyList<string> values,
        int causeMemberOrdinal) => new(
        ScopeSelectorState.PublisherValueConflict,
        values,
        ScopeSelectorEvidenceKind.ObservedConflictingValueSet,
        0,
        null,
        causeMemberOrdinal);

    private static ScopeSelectorEvidence NotApplicable(int ruleOrdinal) => new(
        ScopeSelectorState.SelectorNotApplicable,
        [],
        null,
        null,
        ruleOrdinal,
        null);

    private static ScopeRuleEvaluation Matched(
        int ruleOrdinal,
        ScopeRuleEffect effect,
        ScopeDisposition disposition,
        IReadOnlyList<int> roles,
        IReadOnlyList<int> capabilities) => new(
        ruleOrdinal,
        ScopeRuleEvaluationState.Matched,
        effect,
        disposition,
        roles.Order().ToArray(),
        capabilities.Order().ToArray());

    private static ScopeRuleEvaluation NotMatched(int ruleOrdinal) => new(
        ruleOrdinal,
        ScopeRuleEvaluationState.NotMatched,
        null,
        null,
        [],
        []);

    private static ScopeObjectReductionInput Change(
        ScopeObjectReductionInput input,
        IReadOnlyList<ScopeSelectorEvidence>? selectors = null,
        IReadOnlyList<ScopeRuleEvaluation>? evaluations = null) => new(
        input.ObjectRef,
        selectors ?? input.Selectors,
        evaluations ?? input.RuleEvaluations);

    private static ScopeManifest ReplaceRow(
        ScopeManifest manifest,
        int index,
        ScopeManifestRow replacement) => new(
        manifest.Schema,
        manifest.Profile,
        manifest.CompleteEnumerationRef,
        manifest.OrderedEvidenceArtifacts,
        manifest.ObservedObjects,
        manifest.Rows.Select((row, ordinal) => ordinal == index ? replacement : row).ToArray(),
        manifest.Accounting,
        manifest.BodyCandidateOrdinals);

    private static int MemberOrdinal(
        ScopeProfileBinding profile,
        SourceArtifactRef registry,
        string key) => profile.OrderedMembers
        .Select((member, ordinal) => (member, ordinal))
        .Single(value => value.member.RegistryRef == registry && value.member.MemberKey == key)
        .ordinal;

    private static SourceObjectRef Object(string key) => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        Member(Artifact("44aa505f-d55f-4d6c-aef0-21ddcb46633d"), "work"),
        $"http://publications.europa.eu/resource/cellar/{key}",
        $"cellar:work:{key}",
        Sha256($"cellar:work:{key}"),
        Artifact("08ca1acc-142a-4807-8cc0-d84e412e1d07"),
        null);

    private static IReadOnlyList<SourceArtifactRef> EvidenceArtifacts() =>
        [Artifact("11111111-1111-4111-8111-111111111111")];

    private static SourceArtifactRef EnumerationRef() =>
        Artifact("33333333-3333-4333-8333-333333333333");

    private static SourceArtifactRef ProfileRef() =>
        Artifact("c0e28bb7-f26a-4ea0-9628-d084fd3aaf22");

    private static SourceArtifactRef TableRef() =>
        Artifact("ddaa3f1b-994d-47b8-83c7-e6221a90c388");

    private static SourceRegistryMemberRef Member(SourceArtifactRef registry, string key) =>
        new(registry, key);

    private static SourceArtifactRef Artifact(string id) => new($"urn:uuid:{id}", Digest);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static byte[] CanonicalBytes(VerifiedScopeManifest verified)
    {
        using var output = new MemoryStream();
        var digest = ScopeManifestCanonicalWriter.Write(output, verified);
        var bytes = output.ToArray();
        var domain = Encoding.ASCII.GetBytes("lex-v3-source-scope-manifest/1\n");
        var preimage = new byte[domain.Length + bytes.Length];
        domain.CopyTo(preimage, 0);
        bytes.CopyTo(preimage, domain.Length);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(preimage)).ToLowerInvariant(),
            digest);
        return bytes;
    }

    private static EvaluationResults Evaluate(JsonSchema schema, JsonNode value)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        return Evaluate(schema, document.RootElement);
    }

    private static EvaluationResults Evaluate(JsonSchema schema, JsonElement value) =>
        schema.Evaluate(
            value,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true,
            });

    private static int Count(string value, string needle)
    {
        var count = 0;
        var cursor = 0;
        while ((cursor = value.IndexOf(needle, cursor, StringComparison.Ordinal)) >= 0)
        {
            count++;
            cursor += needle.Length;
        }

        return count;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new AssertFailedException("Unable to find the V3 repository root.");
    }

    private sealed class ExactResolver : IScopeReductionEvidenceResolver
    {
        private readonly HashSet<ScopeSelectorObservationBinding> _bindings;
        private readonly HashSet<ScopeSelectorNotApplicableBinding> _notApplicableBindings;
        private readonly HashSet<ScopeRuleEvaluationBinding> _ruleBindings;
        private readonly ScopeCompleteEnumerationBinding _completeEnumerationBinding;

        private ExactResolver(
            IEnumerable<ScopeSelectorObservationBinding> bindings,
            IEnumerable<ScopeSelectorNotApplicableBinding> notApplicableBindings,
            IEnumerable<ScopeRuleEvaluationBinding> ruleBindings,
            ScopeCompleteEnumerationBinding completeEnumerationBinding)
        {
            _bindings = bindings.ToHashSet();
            _notApplicableBindings = notApplicableBindings.ToHashSet();
            _ruleBindings = ruleBindings.ToHashSet();
            _completeEnumerationBinding = completeEnumerationBinding;
        }

        public SourceArtifactRef CompleteEnumerationRef =>
            _completeEnumerationBinding.CompleteEnumerationRef;

        public static ExactResolver For(
            ScopeProfileBinding profile,
            IReadOnlyList<SourceArtifactRef> evidence,
            IReadOnlyList<ScopeObjectReductionInput> inputs)
        {
            var bindings = new List<ScopeSelectorObservationBinding>();
            var notApplicableBindings = new List<ScopeSelectorNotApplicableBinding>();
            var ruleBindings = new List<ScopeRuleEvaluationBinding>();
            foreach (var input in inputs)
            {
                var objectDigest = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(
                    input.ObjectRef);
                for (var selectorOrdinal = 0;
                     selectorOrdinal < input.Selectors.Count;
                     selectorOrdinal++)
                {
                    var selector = input.Selectors[selectorOrdinal];
                    if (selector.EvidenceArtifactOrdinal is not { } evidenceOrdinal ||
                        selector.EvidenceKind is null)
                    {
                        if (selector.RuleOrdinal is { } ruleOrdinal)
                        {
                            var rule = profile.OrderedRules[ruleOrdinal];
                            notApplicableBindings.Add(new ScopeSelectorNotApplicableBinding(
                                objectDigest,
                                selectorOrdinal,
                                profile.OrderedMembers[
                                    profile.OrderedSelectorMemberOrdinals[selectorOrdinal]],
                                profile.SourceProfileRef,
                                profile.SelectorTableRef,
                                ruleOrdinal,
                                profile.OrderedMembers[rule.RuleMemberOrdinal]));
                        }

                        continue;
                    }

                    bindings.Add(new ScopeSelectorObservationBinding(
                        selector.EvidenceKind.Value,
                        objectDigest,
                        selectorOrdinal,
                        profile.OrderedMembers[
                            profile.OrderedSelectorMemberOrdinals[selectorOrdinal]],
                        profile.SourceProfileRef,
                        profile.SelectorTableRef,
                        evidence[evidenceOrdinal],
                        ScopeManifestCanonicalWriter.ComputeSelectorEvidenceSha256(
                            profile,
                            evidence,
                            selectorOrdinal,
                            selector)));
                }

                var selectorSetSha256 =
                    ScopeManifestCanonicalWriter.ComputeSelectorSetSha256(
                        profile,
                        evidence,
                        input.Selectors);
                foreach (var evaluation in input.RuleEvaluations)
                {
                    var rule = profile.OrderedRules[evaluation.RuleOrdinal];
                    ruleBindings.Add(new ScopeRuleEvaluationBinding(
                        objectDigest,
                        selectorSetSha256,
                        evaluation.RuleOrdinal,
                        profile.OrderedMembers[rule.RuleMemberOrdinal],
                        profile.SourceProfileRef,
                        profile.SelectorTableRef,
                        ScopeManifestCanonicalWriter.ComputeRuleEvaluationSha256(
                            profile,
                            evaluation)));
                }
            }

            var observed = inputs
                .Select(input => new ScopeObservedObjectEntry(
                    input.ObjectRef,
                    ScopeManifestCanonicalWriter.ComputeObjectRefSha256(input.ObjectRef)))
                .OrderBy(static entry => entry, ScopeObservedObjectComparer.Instance)
                .ToArray();
            var enumerationBinding = new ScopeCompleteEnumerationBinding(
                EnumerationRef(),
                profile.SourceProfileRef,
                profile.SelectorTableRef,
                observed.Length,
                ScopeManifestCanonicalWriter.ComputeObservedObjectSequenceSha256(observed));
            return new ExactResolver(
                bindings,
                notApplicableBindings,
                ruleBindings,
                enumerationBinding);
        }

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            _bindings.Contains(binding);

        public bool IsSelectorNotApplicableAdmitted(
            ScopeSelectorNotApplicableBinding binding) =>
            _notApplicableBindings.Contains(binding);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            _ruleBindings.Contains(binding);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            _completeEnumerationBinding == binding;
    }

    private sealed class RejectAllResolver : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef => EnumerationRef();

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            false;

        public bool IsSelectorNotApplicableAdmitted(
            ScopeSelectorNotApplicableBinding binding) => false;

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) => false;

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) => false;
    }

    private sealed class OffsetCountingStream : Stream
    {
        public OffsetCountingStream(long initialPosition)
        {
            Position = initialPosition;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Position;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Position += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Position += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            Position++;
        }
    }
}

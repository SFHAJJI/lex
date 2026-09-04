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
                row.FetchAddress,
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
                row.FetchAddress,
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
                row.FetchAddress,
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
            "\"schema\":\"lex-v3-source-scope-manifest/2\",");
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

        // D1-06c-EU raised this from 2,000: every row now carries a fetch_address object (a
        // NotMinted row's own JSON, e.g. {"status":"not_minted","host":null,...}, is real bytes),
        // observed at ~2,031 per row here. The number stays a rough sanity tripwire against future
        // per-row bloat, not a pinned exact byte count; the invariant that actually matters is the
        // projectedBytes assertion just below, which stays far under int.MaxValue at this size.
        Assert.IsLessThan(2_200, incrementalBytes);
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

    [TestMethod]
    public void ParseAndVerifyAcceptsExactCanonicalBytesAndExposesContent()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("parse-and-verify"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(
            profile,
            evidence,
            [input.ObjectRef],
            [input],
            resolver);
        var bytes = CanonicalBytes(verified);
        var artifactRef = ArtifactRefFor(bytes);

        var reopened = VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver);

        Assert.HasCount(1, reopened.Manifest.Rows);
        Assert.AreEqual(
            input.ObjectRef.CanonicalKey,
            reopened.Manifest.ObservedObjects[0].ObjectRef.CanonicalKey);
        CollectionAssert.AreEqual(bytes, CanonicalBytes(reopened));
    }

    [TestMethod]
    public void ParseAndVerifyReopensTheExactDigestScopeManifestCanonicalWriterWriteReturns()
    {
        // Every other positive test in this file (via ArtifactRefFor / CanonicalBytes) mints its
        // SourceArtifactRef with ScopeManifestCanonicalWriter.ComputeManifestSha256 -- the SAME
        // function ParseAndVerify calls internally to check the ref -- so those tests only prove
        // ComputeManifestSha256 agrees with itself. A real producer never calls ComputeManifestSha256
        // on its own output; it receives the digest ScopeManifestCanonicalWriter.Write already
        // returned from writing the bytes once, and mints the ref from that. This test mints the ref
        // from Write's own returned digest, never recomputing it, so a successful reopen is the
        // actual proof that ComputeManifestSha256 agrees with what the production writer
        // independently computed -- the door's whole purpose.
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("writer-round-trip"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);

        using var output = new MemoryStream();
        var writerDigest = ScopeManifestCanonicalWriter.Write(output, verified);
        var bytes = output.ToArray();
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:99999999-9999-4999-8999-999999999999",
            writerDigest);

        var reopened = VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver);

        Assert.HasCount(1, reopened.Manifest.Rows);
        Assert.AreEqual(
            input.ObjectRef.CanonicalKey,
            reopened.Manifest.ObservedObjects[0].ObjectRef.CanonicalKey);
        CollectionAssert.AreEqual(bytes, CanonicalBytes(reopened));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsNullArguments()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("null-args"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var bytes = CanonicalBytes(verified);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            VerifiedScopeManifest.ParseAndVerify(null!, bytes, resolver));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, null!));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsADigestThatDoesNotReproduce()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("wrong-digest"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var bytes = CanonicalBytes(verified);
        var wrongArtifactRef = new SourceArtifactRef(
            "urn:uuid:99999999-9999-4999-8999-999999999999",
            new string('0', 64));

        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedScopeManifest.ParseAndVerify(wrongArtifactRef, bytes, resolver));
        StringAssert.Contains(
            exception.Message,
            "The scope manifest bytes do not match their artifact reference.");
        Assert.AreEqual("canonicalBytes", exception.ParamName);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsInvalidUtf8()
    {
        // "{", a lone UTF-8 continuation byte with no lead byte, "}", "\n".
        byte[] badBytes = [0x7b, 0x80, 0x7d, (byte)'\n'];
        var artifactRef = ArtifactRefFor(badBytes);

        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, badBytes, new RejectAllResolver()));
        StringAssert.Contains(
            exception.Message,
            "A scope manifest must contain exact valid UTF-8 bytes.");
        Assert.AreEqual("canonicalBytes", exception.ParamName);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsBytesThatAreNotValidCanonicalJson()
    {
        var badBytes = Encoding.UTF8.GetBytes(
            "{\"schema\":\"lex-v3-source-scope-manifest/1\",\n");
        var artifactRef = ArtifactRefFor(badBytes);

        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, badBytes, new RejectAllResolver()));
        StringAssert.Contains(
            exception.Message,
            "The scope manifest bytes are not one valid typed canonical document.");
        Assert.AreEqual("canonicalBytes", exception.ParamName);

        // This outer message is identical to
        // ParseAndVerifyRejectsAnUnsortedEvidenceArtifactTableAtDeserialization's, because both are
        // ParseAndVerify's own catch (JsonException) branch. What tells them apart is the inner
        // exception: truncated JSON syntax fails inside System.Text.Json itself, with no
        // ArgumentException from a record constructor anywhere in the chain, unlike the sortedness
        // guard case.
        var inner = exception.InnerException as JsonException;
        Assert.IsNotNull(inner);
        Assert.IsNotInstanceOfType<ArgumentException>(inner.InnerException);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsNonCanonicalWhitespace()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("noncanonical"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var bytes = CanonicalBytes(verified);
        Assert.AreEqual((byte)'{', bytes[0]);
        var withWhitespace = new byte[bytes.Length + 1];
        withWhitespace[0] = bytes[0];
        withWhitespace[1] = (byte)' ';
        Array.Copy(bytes, 1, withWhitespace, 2, bytes.Length - 1);
        var artifactRef = ArtifactRefFor(withWhitespace);

        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, withWhitespace, resolver));
        StringAssert.Contains(
            exception.Message,
            "The scope manifest is not its exact canonical typed representation.");
        Assert.AreEqual("canonicalBytes", exception.ParamName);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsAnUnsortedEvidenceArtifactTableAtDeserialization()
    {
        var profile = Profile();
        var evidenceA = Artifact("11111111-1111-4111-8111-111111111111");
        var evidenceB = Artifact("22222222-2222-4222-8222-222222222222");
        var evidence = new[] { evidenceA, evidenceB };
        var shared = MemberOrdinal(profile, ProfileRef(), "shared");
        var input = new ScopeObjectReductionInput(
            Object("two-evidence-artifacts"),
            [
                new ScopeSelectorEvidence(
                    ScopeSelectorState.PublisherValuePresent,
                    ["formex"],
                    ScopeSelectorEvidenceKind.ObservedValueSet,
                    0,
                    null,
                    null),
                new ScopeSelectorEvidence(
                    ScopeSelectorState.PublisherValuePresent,
                    ["FRA"],
                    ScopeSelectorEvidenceKind.ObservedValueSet,
                    1,
                    null,
                    null),
            ],
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
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var bytes = CanonicalBytes(verified);

        var node = JsonNode.Parse(Encoding.UTF8.GetString(bytes))!.AsObject();
        var table = node["ordered_evidence_artifacts"]!.AsArray();
        Assert.HasCount(2, table);
        var first = table[0]!.DeepClone();
        var second = table[1]!.DeepClone();
        table[0] = second;
        table[1] = first;
        var swappedBytes = Encoding.UTF8.GetBytes(node.ToJsonString() + "\n");
        var artifactRef = ArtifactRefFor(swappedBytes);

        // This fires inside ContractJson.Deserialize -> the ScopeManifest record constructor's own
        // ScopeValidation.CopySortedArtifacts guard, not a ScopeManifestReaderOnlyInvariant checked
        // by ScopeReducer.VerifyAndOpen: deserialization rejects the document before VerifyAndOpen is
        // ever reached, which is why this test is named for the deserialization guard it actually
        // exercises rather than for a reader-only invariant.
        //
        // ContractJson.Deserialize catches any ArgumentException thrown by a record constructor and
        // rewraps it as JsonException("The contract document violates a typed invariant.", ...), and
        // ParseAndVerify's own catch (JsonException) rewraps THAT as the exact same outer message
        // ParseAndVerifyRejectsBytesThatAreNotValidCanonicalJson asserts for genuinely malformed JSON
        // syntax. The outer exception alone cannot tell the two apart; only the inner exception chain
        // -- the record constructor's own ArgumentException, still attached two levels down -- proves
        // this specific test fired the sortedness guard and not some other deserialization failure.
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, swappedBytes, resolver));
        StringAssert.Contains(
            exception.Message,
            "The scope manifest bytes are not one valid typed canonical document.");
        Assert.AreEqual("canonicalBytes", exception.ParamName);
        var wrapping = exception.InnerException as JsonException;
        Assert.IsNotNull(wrapping);
        StringAssert.Contains(
            wrapping.Message,
            "The contract document violates a typed invariant.");
        var constructorGuard = wrapping.InnerException as ArgumentException;
        Assert.IsNotNull(constructorGuard);
        StringAssert.Contains(
            constructorGuard.Message,
            "Evidence artifacts must be canonically sorted and unique.");
        Assert.AreEqual("orderedEvidenceArtifacts", constructorGuard.ParamName);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsARowDigestThatDoesNotRecompute()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("bad-row-digest"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var row = verified.Manifest.Rows[0];
        var tampered = ReplaceRow(
            verified.Manifest,
            0,
            new ScopeManifestRow(
                row.ObjectOrdinal,
                row.Selectors,
                row.RuleMatchBitsBase64Url,
                row.MatchedEvaluations,
                row.AxisWinningRuleOrdinals,
                row.FetchAddress,
                new string('0', 64)));
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsAnAxisWinnerThatDoesNotRecompute()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("bad-axis-winner"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var row = verified.Manifest.Rows[0];
        var tampered = ReplaceRow(
            verified.Manifest,
            0,
            new ScopeManifestRow(
                row.ObjectOrdinal,
                row.Selectors,
                row.RuleMatchBitsBase64Url,
                row.MatchedEvaluations,
                [4, .. row.AxisWinningRuleOrdinals.Skip(1)],
                row.FetchAddress,
                row.RowSha256));
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsMalformedRuleMatchBitPadding()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("bad-bit-padding"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var row = verified.Manifest.Rows[0];
        var tampered = ReplaceRow(
            verified.Manifest,
            0,
            new ScopeManifestRow(
                row.ObjectOrdinal,
                row.Selectors,
                "_w",
                row.MatchedEvaluations,
                row.AxisWinningRuleOrdinals,
                row.FetchAddress,
                row.RowSha256));
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsAnAccountingPartitionThatDoesNotRecompute()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("bad-accounting"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var manifest = verified.Manifest;
        var firstAccounting = manifest.Accounting[0];
        var tampered = new ScopeManifest(
            manifest.Schema,
            manifest.Profile,
            manifest.CompleteEnumerationRef,
            manifest.OrderedEvidenceArtifacts,
            manifest.ObservedObjects,
            manifest.Rows,
            [
                new ScopeAccountingSet(firstAccounting.Axis, firstAccounting.Disposition, []),
                .. manifest.Accounting.Skip(1),
            ],
            manifest.BodyCandidateOrdinals);
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
    }

    // The two tests below drive ScopeManifestReaderOnlyInvariant.RuleBitAndMatchedPayloadParity (5)
    // and ExactBodyCandidateProjection (10) through the door with the same tampered-constructor
    // technique as ParseAndVerifyRejectsAnAccountingPartitionThatDoesNotRecompute above: a
    // structurally valid ScopeManifest built directly through its public constructors, with exactly
    // one field made semantically inconsistent so only ScopeReducer.VerifyAndOpen -- not the record
    // constructors -- can catch it.
    //
    // A third reader-only invariant, CanonicalRequestValidation (11), is structurally out of reach
    // through this door: it validates a caller-supplied requested-axis list (non-empty, unique)
    // inside ScopeReducer.ReduceRequest, a call path ParseAndVerify never exercises, since
    // ParseAndVerify never takes a requested-axis list at all. No test in this file drives it through
    // ParseAndVerify, and none can;
    // RequestReductionRetainsAxesAndUsesIntersectionOnlyForAllAccepted above already exercises it
    // directly against ReduceRequest instead.

    [TestMethod]
    public void ParseAndVerifyRejectsMatchedEvaluationsThatDoNotEqualTheSetRuleMatchBits()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("bad-bit-parity"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var row = verified.Manifest.Rows[0];
        Assert.IsGreaterThan(0, row.MatchedEvaluations.Count);

        // RuleMatchBitsBase64Url and RowSha256 are left exactly as reduction produced them:
        // ScopeReducer.ExpandAndVerifyEvaluations runs before the row digest is ever recomputed, so
        // dropping one matched-evaluation payload while its rule-match bit stays set is what actually
        // fires here, not a stale digest.
        var tampered = ReplaceRow(
            verified.Manifest,
            0,
            new ScopeManifestRow(
                row.ObjectOrdinal,
                row.Selectors,
                row.RuleMatchBitsBase64Url,
                row.MatchedEvaluations.Take(row.MatchedEvaluations.Count - 1).ToArray(),
                row.AxisWinningRuleOrdinals,
                row.FetchAddress,
                row.RowSha256));
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
        Assert.AreEqual(
            "Every set rule-match bit requires exactly one matched payload.",
            exception.Message);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsBodyCandidateOrdinalsThatDoNotEqualTheExactProjection()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("bad-body-candidates"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var manifest = verified.Manifest;
        Assert.IsNotEmpty(manifest.BodyCandidateOrdinals);

        // Rows and accounting are left untouched: only the manifest's top-level
        // body_candidate_ordinals no longer equals the accepted body-role projection
        // ScopeReducer.VerifyBodyCandidates recomputes from the (unchanged, correctly verified) rows.
        var tampered = new ScopeManifest(
            manifest.Schema,
            manifest.Profile,
            manifest.CompleteEnumerationRef,
            manifest.OrderedEvidenceArtifacts,
            manifest.ObservedObjects,
            manifest.Rows,
            manifest.Accounting,
            []);
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
        Assert.AreEqual(
            "Body candidates do not equal the exact accepted body-role projection.",
            exception.Message);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsRowsThatDoNotExactlyCoverObservedObjects()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("row-coverage"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var manifest = verified.Manifest;
        var tampered = new ScopeManifest(
            manifest.Schema,
            manifest.Profile,
            manifest.CompleteEnumerationRef,
            manifest.OrderedEvidenceArtifacts,
            [],
            manifest.Rows,
            manifest.Accounting,
            manifest.BodyCandidateOrdinals);
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsACompleteEnumerationRefThatDoesNotMatchTheResolver()
    {
        var profile = Profile();
        SourceArtifactRef[] noEvidence = [];
        ScopeObjectReductionInput[] noInputs = [];
        var resolver = ExactResolver.For(profile, noEvidence, noInputs);
        var verified = ScopeReducer.Reduce(profile, noEvidence, [], noInputs, resolver);
        var manifest = verified.Manifest;
        var tampered = new ScopeManifest(
            manifest.Schema,
            manifest.Profile,
            Artifact("44444444-4444-4444-8444-444444444444"),
            manifest.OrderedEvidenceArtifacts,
            manifest.ObservedObjects,
            manifest.Rows,
            manifest.Accounting,
            manifest.BodyCandidateOrdinals);
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        // This tampers the manifest's own complete_enumeration_ref away from what the resolver
        // reports, so ScopeReducer.VerifyCompleteEnumeration's ref-equality check fires before the
        // resolver's IsCompleteEnumerationAdmitted is ever called -- this test is named for that
        // ref-mismatch guard, not for resolver refusal. See
        // ParseAndVerifyRejectsACompleteEnumerationTheResolverRefusesToAdmit below for the admission
        // branch a mismatched ref can never reach.
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
        Assert.AreEqual(
            "The manifest names a different complete-enumeration artifact than the resolver.",
            exception.Message);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsACompleteEnumerationTheResolverRefusesToAdmit()
    {
        var profile = Profile();
        SourceArtifactRef[] noEvidence = [];
        ScopeObjectReductionInput[] noInputs = [];
        var resolver = ExactResolver.For(profile, noEvidence, noInputs);
        var verified = ScopeReducer.Reduce(profile, noEvidence, [], noInputs, resolver);
        var bytes = CanonicalBytes(verified);
        var artifactRef = ArtifactRefFor(bytes);

        // A resolver whose CompleteEnumerationRef matches the manifest exactly, so the ref-equality
        // check the test above drives cannot fire here. Every admission call refuses instead,
        // including IsCompleteEnumerationAdmitted -- the only way through the door to reach that
        // resolver call, since a mismatched ref (as above) is refused before it is ever invoked.
        var refusingResolver = new CompleteEnumerationRefusingResolver(
            verified.Manifest.CompleteEnumerationRef);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, refusingResolver));
        Assert.AreEqual(
            "The complete enumeration was not admitted against its exact observed-object set.",
            exception.Message);
    }

    [TestMethod]
    public void ParseAndVerifyRejectsASelectorValueTheResolverDoesNotAdmit()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var original = ValidInput(profile, Object("forged-selector"));
        var trustedResolver = ExactResolver.For(profile, evidence, [original]);

        var forgedValueSelectors = original.Selectors.ToArray();
        forgedValueSelectors[0] = Present(["forged-value-not-in-evidence"]);
        var forgedValue = Change(original, selectors: forgedValueSelectors);
        var forgedResult = ScopeReducer.Reduce(
            profile,
            evidence,
            [original.ObjectRef],
            [forgedValue],
            ExactResolver.For(profile, evidence, [forgedValue]));
        var bytes = CanonicalBytes(forgedResult);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, trustedResolver));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsARuleEvaluationTheResolverDoesNotAdmit()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var original = ValidInput(profile, Object("forged-outcome"));
        var trustedResolver = ExactResolver.For(profile, evidence, [original]);

        var forgedEvaluations = original.RuleEvaluations.ToArray();
        forgedEvaluations[1] = Matched(
            1,
            ScopeRuleEffect.Positive,
            ScopeDisposition.NeverIngest,
            [],
            []);
        var forgedOutcome = Change(original, evaluations: forgedEvaluations);
        var forgedResult = ScopeReducer.Reduce(
            profile,
            evidence,
            [original.ObjectRef],
            [forgedOutcome],
            ExactResolver.For(profile, evidence, [forgedOutcome]));
        var bytes = CanonicalBytes(forgedResult);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, trustedResolver));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsAnEvidenceArtifactTableWithAnUnreferencedEntry()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("unused-evidence"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var manifest = verified.Manifest;
        var tampered = new ScopeManifest(
            manifest.Schema,
            manifest.Profile,
            manifest.CompleteEnumerationRef,
            [.. manifest.OrderedEvidenceArtifacts, Artifact("22222222-2222-4222-8222-222222222222")],
            manifest.ObservedObjects,
            manifest.Rows,
            manifest.Accounting,
            manifest.BodyCandidateOrdinals);
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
    }

    [TestMethod]
    public void ParseAndVerifyRejectsARoleOrdinalOutsideTheProfileMemberTable()
    {
        var profile = Profile();
        var evidence = EvidenceArtifacts();
        var input = ValidInput(profile, Object("bad-role-ordinal"));
        var resolver = ExactResolver.For(profile, evidence, [input]);
        var verified = ScopeReducer.Reduce(profile, evidence, [input.ObjectRef], [input], resolver);
        var row = verified.Manifest.Rows[0];
        var matched = row.MatchedEvaluations.ToArray();
        matched[0] = new ScopeMatchedEvaluation(
            matched[0].RuleOrdinal,
            matched[0].Effect,
            matched[0].Disposition,
            [9_999],
            matched[0].CapabilityMemberOrdinals);
        var tampered = ReplaceRow(
            verified.Manifest,
            0,
            new ScopeManifestRow(
                row.ObjectOrdinal,
                row.Selectors,
                row.RuleMatchBitsBase64Url,
                matched,
                row.AxisWinningRuleOrdinals,
                row.FetchAddress,
                row.RowSha256));
        var bytes = BytesForUnverified(tampered);
        var artifactRef = ArtifactRefFor(bytes);

        Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedScopeManifest.ParseAndVerify(artifactRef, bytes, resolver));
    }

    private static SourceArtifactRef ArtifactRefFor(byte[] canonicalBytes) => new(
        "urn:uuid:99999999-9999-4999-8999-999999999999",
        ScopeManifestCanonicalWriter.ComputeManifestSha256(canonicalBytes));

    private static byte[] BytesForUnverified(ScopeManifest manifest)
    {
        using var output = new MemoryStream();
        ScopeManifestCanonicalWriter.Write(output, new VerifiedScopeManifest(manifest));
        return output.ToArray();
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
        var domain = Encoding.ASCII.GetBytes("lex-v3-source-scope-manifest/2\n");
        var preimage = new byte[domain.Length + bytes.Length];
        domain.CopyTo(preimage, 0);
        bytes.CopyTo(preimage, domain.Length);
        var handComputedDigest = Convert.ToHexString(SHA256.HashData(preimage)).ToLowerInvariant();
        Assert.AreEqual(handComputedDigest, digest);

        // ComputeManifestSha256 is the digest ParseAndVerify recomputes from durable bytes before
        // trusting them. Anchor it to the same hand-computed preimage above -- a raw SHA-256 over
        // the domain-prefixed bytes, calling neither Write nor ComputeManifestSha256 -- so nothing
        // lets the function drift from what its own domain-prefixed hash is actually defined to be.
        Assert.AreEqual(
            handComputedDigest,
            ScopeManifestCanonicalWriter.ComputeManifestSha256(bytes));
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

    /// <summary>
    /// Like <see cref="RejectAllResolver"/>, but with a caller-supplied
    /// <see cref="CompleteEnumerationRef"/> instead of a fixed one, so a test can make the
    /// ref-equality check in <see cref="ScopeReducer.VerifyCompleteEnumeration"/> pass and drive its
    /// following <see cref="IsCompleteEnumerationAdmitted"/> call instead.
    /// </summary>
    private sealed class CompleteEnumerationRefusingResolver : IScopeReductionEvidenceResolver
    {
        public CompleteEnumerationRefusingResolver(SourceArtifactRef completeEnumerationRef)
        {
            CompleteEnumerationRef = completeEnumerationRef;
        }

        public SourceArtifactRef CompleteEnumerationRef { get; }

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) => false;

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

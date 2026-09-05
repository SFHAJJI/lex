using Lex.V3.Contracts;
using System.Reflection;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Absence;

/// <summary>
/// D1-03, R3.3 lines 477 to 494: the immutable absence key, the stable subject, the complete
/// comparison-policy tuple, and the generation identity that no configuration digest can produce.
/// </summary>
[TestClass]
public sealed class AbsenceKeyTests
{
    private static AbsenceHistoryGenerationId Generation(
        AbsenceSubject subject,
        int ordinal = 1,
        string eventId = "evt-1")
    {
        var id = AbsenceHistoryGenerationId.TryCreate(subject, ordinal, eventId, out var refusal);
        Assert.IsNotNull(id, $"fixture generation identity refused as {refusal}");
        Assert.AreEqual(AbsenceHistoryGenerationIdRefusal.None, refusal);
        return id;
    }

    /// <summary>
    /// The fourteen member names R3.3 lists, in its order, transcribed from the candidate rather
    /// than read back from the code that produces them.
    /// </summary>
    [TestMethod]
    public void TheKeyProjectionCarriesExactlyTheFourteenMembersR33Names()
    {
        var subject = AbsenceFixtures.Subject();
        var projection = AbsenceKey.Projection(
            subject, Generation(subject), AbsenceFixtures.Policy());

        var names = projection
            .Split('\n')
            .Select(line => line[..line.IndexOf('=', StringComparison.Ordinal)])
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "publisher",
                "entity_kind",
                "canonical_publisher_uri",
                "parent_identity_or_null",
                "absence_history_generation_id",
                "root_definition_digest",
                "applicable_scope_policy_digest",
                "discovery_query_digest",
                "selection_query_digest",
                "adapter_digest",
                "request_policy_digest",
                "execution_policy_digest",
                "robots_policy_profile_digest",
                "replacement_coordinate_profile_digest",
            },
            names,
            "the absence key no longer projects exactly the R3.3 tuple");

        Assert.AreEqual(AbsenceKey.MemberCount, names.Length);
    }

    /// <summary>
    /// Every member is load bearing: change one and the key changes. Written as a mutation over the
    /// real member list rather than as a spot check, because a member that silently dropped out of
    /// the projection would let two different configurations compare as one tuple, which is exactly
    /// the failure the tuple exists to prevent.
    /// </summary>
    [TestMethod]
    public void ChangingAnySingleMemberChangesTheKey()
    {
        var subject = AbsenceFixtures.Subject();
        var policy = AbsenceFixtures.Policy();
        var baseline = AbsenceKey.Projection(subject, Generation(subject), policy);

        var mutations = new List<(string Member, string Projection)>();

        var otherPublisher = AbsenceSubject.TryCreate(
            SourceAuthority.Cellar, AbsenceFixtures.EntityKind(), AbsenceFixtures.RootUri, null, out _);
        Assert.IsNotNull(otherPublisher);
        mutations.Add(("publisher",
            AbsenceKey.Projection(otherPublisher, Generation(otherPublisher), policy)));

        var otherKind = AbsenceSubject.TryCreate(
            SourceAuthority.Jolux,
            AbsenceFixtures.EntityKind("expression"),
            AbsenceFixtures.RootUri,
            null,
            out _);
        Assert.IsNotNull(otherKind);
        mutations.Add(("entity_kind",
            AbsenceKey.Projection(otherKind, Generation(otherKind), policy)));

        var otherUri = AbsenceFixtures.Subject(AbsenceFixtures.OtherUri);
        mutations.Add(("canonical_publisher_uri",
            AbsenceKey.Projection(otherUri, Generation(otherUri), policy)));

        var withParent = AbsenceFixtures.Subject(parent: AbsenceFixtures.Parent());
        mutations.Add(("parent_identity_or_null",
            AbsenceKey.Projection(withParent, Generation(withParent), policy)));

        mutations.Add(("absence_history_generation_id",
            AbsenceKey.Projection(subject, Generation(subject, ordinal: 2), policy)));

        foreach (var member in Enum.GetValues<AbsenceComparisonPolicyMember>())
        {
            mutations.Add((ContractWire.NameOf(member),
                AbsenceKey.Projection(
                    subject, Generation(subject), AbsenceFixtures.Policy('b', member))));
        }

        Assert.AreEqual(
            AbsenceKey.MemberCount,
            mutations.Count,
            "one member of the tuple has no mutation, so nothing proves it is read");

        foreach (var (member, mutated) in mutations)
        {
            Assert.AreNotEqual(
                baseline,
                mutated,
                $"changing {member} left the absence key identical, so two configurations compare as one");
        }

        Assert.AreEqual(
            AbsenceKey.MemberCount,
            mutations.Select(static mutation => mutation.Projection).Distinct(StringComparer.Ordinal).Count(),
            "two different mutations produced the same key, so at least one member is aliased");
    }

    /// <summary>
    /// R3.3: a generation ID is "never content-addressed solely by configuration digests". That is
    /// met by the factory taking no policy at all, and the absence of an input cannot be observed
    /// by feeding inputs, so the parameter list itself is the assertion.
    /// </summary>
    [TestMethod]
    public void TheGenerationIdentityTakesNoComparisonPolicyInput()
    {
        var factory = typeof(AbsenceHistoryGenerationId)
            .GetMethod(nameof(AbsenceHistoryGenerationId.TryCreate), BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(factory, "the generation identity factory was renamed or removed");

        CollectionAssert.AreEqual(
            new[]
            {
                typeof(AbsenceSubject),
                typeof(int),
                typeof(string),
                typeof(AbsenceHistoryGenerationIdRefusal).MakeByRefType(),
            },
            factory.GetParameters().Select(static parameter => parameter.ParameterType).ToArray(),
            "the generation identity now takes an input it must not: a policy digest reaching this " +
            "factory is how an A to B to A return reproduces an earlier identity");
    }

    /// <summary>
    /// Two subjects with the same ordinal and event differ only by subject, and the ordinal alone
    /// separates two generations of the same subject. Both properties are what make the identity
    /// unique without any configuration input.
    /// </summary>
    [TestMethod]
    public void GenerationIdentitiesSeparateBySubjectAndByOrdinal()
    {
        var first = AbsenceFixtures.Subject();
        var second = AbsenceFixtures.Subject(AbsenceFixtures.OtherUri);

        Assert.AreNotEqual(Generation(first).Value, Generation(second).Value);
        Assert.AreNotEqual(Generation(first).Value, Generation(first, ordinal: 2).Value);
        Assert.AreNotEqual(Generation(first).Value, Generation(first, eventId: "evt-2").Value);
        Assert.AreEqual(Generation(first).Value, Generation(first).Value);
    }

    [TestMethod]
    public void AGenerationIdentityRefusesANonPositiveOrdinalAndAnUnboundedEvent()
    {
        var subject = AbsenceFixtures.Subject();

        Assert.IsNull(AbsenceHistoryGenerationId.TryCreate(subject, 0, "evt", out var zero));
        Assert.AreEqual(AbsenceHistoryGenerationIdRefusal.OrdinalNotPositive, zero);

        Assert.IsNull(AbsenceHistoryGenerationId.TryCreate(subject, -1, "evt", out var negative));
        Assert.AreEqual(AbsenceHistoryGenerationIdRefusal.OrdinalNotPositive, negative);

        Assert.IsNull(AbsenceHistoryGenerationId.TryCreate(subject, 1, "  ", out var blank));
        Assert.AreEqual(AbsenceHistoryGenerationIdRefusal.OpeningEventIdInvalid, blank);
    }

    /// <summary>
    /// Totality against the closed vocabulary, not against a written count of nine.
    /// </summary>
    [TestMethod]
    public void AComparisonPolicyMustDecideEveryMemberOfTheClosedTuple()
    {
        var members = Enum.GetValues<AbsenceComparisonPolicyMember>();
        Assert.AreEqual(9, members.Length, "the comparison-policy vocabulary changed size");

        foreach (var omitted in members)
        {
            var rows = members
                .Where(member => member != omitted)
                .Select(static member => new AbsenceComparisonPolicyDigest(member, new string('a', 64)))
                .ToArray();

            Assert.IsNull(
                AbsenceComparisonPolicy.TryCreate(rows, out var refusal),
                $"a policy with no {omitted} row was admitted");
            Assert.AreEqual(AbsenceComparisonPolicyRefusal.MemberUndecided, refusal);
        }
    }

    [TestMethod]
    public void AComparisonPolicyRefusesADuplicateAnUndefinedMemberAndANonDigest()
    {
        var complete = Enum.GetValues<AbsenceComparisonPolicyMember>()
            .Select(static member => new AbsenceComparisonPolicyDigest(member, new string('a', 64)))
            .ToList();

        var duplicated = complete.Append(
            new AbsenceComparisonPolicyDigest(
                AbsenceComparisonPolicyMember.AdapterDigest, new string('b', 64))).ToArray();
        Assert.IsNull(AbsenceComparisonPolicy.TryCreate(duplicated, out var duplicate));
        Assert.AreEqual(AbsenceComparisonPolicyRefusal.DuplicateMember, duplicate);

        var undefined = complete.Append(
            new AbsenceComparisonPolicyDigest((AbsenceComparisonPolicyMember)99, new string('a', 64)))
            .ToArray();
        Assert.IsNull(AbsenceComparisonPolicy.TryCreate(undefined, out var unknown));
        Assert.AreEqual(AbsenceComparisonPolicyRefusal.MemberUndefined, unknown);

        var short64 = complete.ToArray();
        short64[0] = new AbsenceComparisonPolicyDigest(short64[0].Member, "not-a-digest");
        Assert.IsNull(AbsenceComparisonPolicy.TryCreate(short64, out var notDigest));
        Assert.AreEqual(AbsenceComparisonPolicyRefusal.DigestNotSha256, notDigest);

        var uppercase = complete.ToArray();
        uppercase[0] = new AbsenceComparisonPolicyDigest(uppercase[0].Member, new string('A', 64));
        Assert.IsNull(AbsenceComparisonPolicy.TryCreate(uppercase, out var upper));
        Assert.AreEqual(AbsenceComparisonPolicyRefusal.DigestNotSha256, upper);
    }

    /// <summary>
    /// Two independently built policies with the same nine digests are the same configuration. This
    /// is the comparison the A to B to A case turns on, and it must answer true; the defense against
    /// reconnecting a streak lives in the generation identity, never in pretending A and A differ.
    /// </summary>
    [TestMethod]
    public void AByteIdenticalReturnToAnEarlierConfigurationComparesEqual()
    {
        var a = AbsenceFixtures.Policy();
        var againA = AbsenceFixtures.Policy();
        var b = AbsenceFixtures.Policy('b', AbsenceComparisonPolicyMember.RootDefinitionDigest);

        Assert.IsTrue(a.SameConfigurationAs(againA));
        Assert.IsFalse(a.SameConfigurationAs(b));
        Assert.IsFalse(b.SameConfigurationAs(againA));
    }

    [TestMethod]
    public void ASubjectRefusesAnUnusableIdentity()
    {
        Assert.IsNull(AbsenceSubject.TryCreate(
            (SourceAuthority)0, AbsenceFixtures.EntityKind(), AbsenceFixtures.RootUri, null, out var publisher));
        Assert.AreEqual(AbsenceSubjectRefusal.PublisherUndefined, publisher);

        Assert.IsNull(AbsenceSubject.TryCreate(
            SourceAuthority.Jolux, AbsenceFixtures.EntityKind(), "not a uri", null, out var uri));
        Assert.AreEqual(AbsenceSubjectRefusal.CanonicalPublisherUriInvalid, uri);

        var foreignRegistry = new SourceRegistryMemberRef(
            new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-0000000000a2", new string('2', 64)),
            "consolidation");
        var foreignParent = new SourceObjectKeyRef(
            foreignRegistry,
            AbsenceFixtures.ParentUri,
            "lu/recueil/2004",
            AbsenceFixtures.Sha256Of("lu/recueil/2004"));
        Assert.IsNull(AbsenceSubject.TryCreate(
            SourceAuthority.Jolux,
            AbsenceFixtures.EntityKind(),
            AbsenceFixtures.RootUri,
            foreignParent,
            out var registry));
        Assert.AreEqual(AbsenceSubjectRefusal.ParentRegistryMismatch, registry);

        var selfParent = new SourceObjectKeyRef(
            AbsenceFixtures.EntityKind(),
            AbsenceFixtures.RootUri,
            "lu/self",
            AbsenceFixtures.Sha256Of("lu/self"));
        Assert.IsNull(AbsenceSubject.TryCreate(
            SourceAuthority.Jolux,
            AbsenceFixtures.EntityKind(),
            AbsenceFixtures.RootUri,
            selfParent,
            out var self));
        Assert.AreEqual(AbsenceSubjectRefusal.ParentIsSelf, self);
    }

    /// <summary>
    /// A subject with a parent whose entity kind differs but whose registry matches is admitted: a
    /// consolidation under an expression parent is ordinary. Without this the mismatch test above
    /// would pass just as well against a guard that refused every parent.
    /// </summary>
    [TestMethod]
    public void ASubjectAdmitsAParentOfADifferentKindInTheSameRegistry()
    {
        var subject = AbsenceSubject.TryCreate(
            SourceAuthority.Jolux,
            AbsenceFixtures.EntityKind(),
            AbsenceFixtures.RootUri,
            AbsenceFixtures.Parent(memberKey: "expression"),
            out var refusal);

        Assert.IsNotNull(subject);
        Assert.AreEqual(AbsenceSubjectRefusal.None, refusal);
    }

    /// <summary>
    /// Every closed vocabulary in the slice, member by member, transcribed rather than derived.
    /// </summary>
    /// <remarks>
    /// These tokens are the contract. Most of them are R3.3's own words, and the rest are refusal
    /// reasons a caller reads to decide what to do next, so a rename is a wire break and an added
    /// member is a state nothing yet demonstrates. Deriving this list from the enums would make the
    /// test agree with whatever the enums say, which is the one thing it must not do.
    /// </remarks>
    [TestMethod]
    public void EveryClosedAbsenceVocabularyIsPinnedMemberByMember()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "AbsenceAppendDisposition: streak_advanced, presence_break_recorded, "
                + "partial_run_no_effect, separation_floor_not_met, clock_source_changed, "
                + "frozen_pending_replacement_review",
                "AbsenceApplicableSet: observed_root_set, normalized_family_set",
                "AbsenceComparisonPolicyMember: root_definition_digest, "
                + "applicable_scope_policy_digest, discovery_query_digest, selection_query_digest, "
                + "adapter_digest, request_policy_digest, execution_policy_digest, "
                + "robots_policy_profile_digest, replacement_coordinate_profile_digest",
                "AbsenceComparisonPolicyRefusal: none, duplicate_member, member_undecided, "
                + "digest_not_sha256, member_undefined",
                "AbsenceCoordinateFieldKind: stable_publisher_field, family_rule, publisher_date",
                "AbsenceCutRefusal: none, run_id_invalid, applicable_set_undefined, "
                + "observations_empty, duplicate_observation_id, duplicate_family_key, "
                + "observed_key_invalid, duplicate_observed_key, "
                + "duplicate_enumeration_proof_family, enumeration_proof_family_not_observed, "
                + "family_enumeration_proof_missing, enumeration_proofs_span_more_than_one_run, "
                + "enumeration_proof_not_floored",
                "AbsenceFamilyEnumerationProofRefusal: none, family_key_invalid, "
                + "partition_is_not_this_family, passes_delivered_different_selections, "
                + "selection_reached_the_row_cap, retained_floor_is_not_receipt_derived",
                "AbsenceFamilyObservationRefusal: none, observation_id_invalid, family_key_invalid, "
                + "timestamp_not_utc, precision_undefined, "
                + "timestamp_finer_than_declared_precision, clock_source_invalid, "
                + "provenance_undefined, skew_negative, uncertainty_interval_not_representable, "
                + "provenance_not_freshly_executed",
                "AbsenceGenerationOpeningEventKind: tracking_started, comparison_policy_transition, "
                + "trustworthy_positive_observation",
                "AbsenceHistoryGenerationCause: initial_tracking, comparison_policy_changed, "
                + "presence_break",
                "AbsenceHistoryGenerationIdRefusal: none, ordinal_not_positive, "
                + "opening_event_id_invalid",
                "AbsenceLedgerRefusal: none, applicable_set_undefined, event_id_invalid, "
                + "event_id_reused, comparison_policy_unchanged, run_id_reused, "
                + "observation_id_reused, cut_axis_not_applicable, "
                + "classification_outside_this_subject",
                "AbsenceObservationProvenance: freshly_executed, "
                + "wrapper_around_earlier_observation, cache_replay, stale_row, incomplete_row",
                "AbsenceReplacementClassificationRefusal: none, cut_id_invalid, cut_ids_identical, "
                + "class_member_invalid, duplicate_class_member",
                "AbsenceReplacementCoordinateProfileRefusal: none, profile_digest_not_sha256, "
                + "fields_empty, field_name_invalid, duplicate_field_name, field_kind_undefined, "
                + "coordinate_is_date_alone",
                "AbsenceReplacementDisposition: coordinate_unchanged, "
                + "ordinary_coordinate_disappearance, ordinary_coordinate_addition, "
                + "replacement_candidate_one_to_one, replacement_collision_full_set",
                "AbsenceReplacementEffect: outside_this_coordinate, may_proceed_to_absence, "
                + "no_absence_event, frozen_pending_review",
                "AbsenceRunCompletion: enumeration_complete, partial",
                "AbsenceState: no_evidence_under_current_generation, present, absent_unconfirmed, "
                + "absent_confirmed, frozen_pending_replacement_review",
                "AbsenceSubjectRefusal: none, publisher_undefined, canonical_publisher_uri_invalid, "
                + "parent_registry_mismatch, parent_is_self",
                "AbsenceTimestampPrecision: hour, minute, second, millisecond, microsecond",
            },
            AbsenceEnums()
                .Select(static type => type.Name + ": " + string.Join(
                    ", ",
                    Enum.GetNames(type).Select(name => WireToken(type, name))))
                .ToArray(),
            "the closed absence vocabulary changed; every entry here is a wire token someone reads");
    }

    /// <summary>
    /// Every refusal vocabulary in this slice states success as zero, so an out-parameter read on a
    /// success path is a true statement rather than whatever the first declared member happened to
    /// be. Every other closed vocabulary here has no zero, because "no cause" and "no disposition"
    /// are not states any of them can be in.
    /// </summary>
    [TestMethod]
    public void EveryRefusalVocabularyCarriesNoneAsZeroAndEveryTokenIsUniqueSnakeCase()
    {
        var absenceEnums = AbsenceEnums();

        Assert.AreEqual(
            21,
            absenceEnums.Length,
            "the absence vocabulary changed size, so this sweep no longer covers what it claims");

        foreach (var type in absenceEnums)
        {
            var tokens = new List<string>();
            foreach (var name in Enum.GetNames(type))
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static)!;
                var attribute = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
                Assert.IsNotNull(attribute, $"{type.Name}.{name} carries no wire token");
                Assert.AreEqual(
                    attribute.Name,
                    attribute.Name.ToLowerInvariant(),
                    $"{type.Name}.{name} is not snake_case");
                Assert.IsTrue(
                    attribute.Name.All(static c => c is (>= 'a' and <= 'z') or '_' or (>= '0' and <= '9')),
                    $"{type.Name}.{name} carries a token outside snake_case");
                tokens.Add(attribute.Name);
            }

            Assert.AreEqual(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count(),
                $"{type.Name} has two members with one token");

            var isRefusal = type.Name.EndsWith("Refusal", StringComparison.Ordinal);
            var hasZero = Enum.GetValues(type).Cast<object>()
                .Any(static value => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 0);
            Assert.AreEqual(
                isRefusal,
                hasZero,
                $"{type.Name} disagrees with the rule that exactly the refusal vocabularies carry a zero member");
        }
    }

    private static Type[] AbsenceEnums() =>
        typeof(AbsenceSubject).Assembly
            .GetTypes()
            .Where(static type => type.IsEnum && type.Namespace == typeof(AbsenceSubject).Namespace)
            .OrderBy(static type => type.Name, StringComparer.Ordinal)
            .ToArray();

    private static string WireToken(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(field, $"{type.Name}.{name} has no static field");
        var attribute = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
        Assert.IsNotNull(attribute, $"{type.Name}.{name} carries no wire token");
        return attribute.Name;
    }
}

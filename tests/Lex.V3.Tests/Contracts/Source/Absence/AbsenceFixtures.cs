using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

using Lex.V3.Contracts.Custody;

namespace Lex.V3.Tests.Contracts.Source.Absence;

/// <summary>
/// Fixture builders for the D1-03 absence lifecycle.
/// </summary>
/// <remarks>
/// Every expectation in these tests is written as a literal beside the builder, never derived from
/// the code under test. A fixture that computes its expectation from the module it exercises agrees
/// with that module by construction and has already let a wrong reviewed policy through here.
/// </remarks>
internal static class AbsenceFixtures
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        (string FamilyKey, int RunSeed), AbsenceFamilyEnumerationProof> Proofs = new();

    public const string RootUri = "https://data.legilux.public.lu/eli/etat/leg/loi/2004/11/12/n1";
    public const string OtherUri = "https://data.legilux.public.lu/eli/etat/leg/loi/2005/01/01/n2";
    public const string ThirdUri = "https://data.legilux.public.lu/eli/etat/leg/rgd/2006/02/02/n3";
    public const string ParentUri = "https://data.legilux.public.lu/eli/etat/leg/recueil/2004";

    public static SourceArtifactRef Registry() =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000a1", new string('1', 64));

    public static SourceRegistryMemberRef EntityKind(string memberKey = "consolidation") =>
        new(Registry(), memberKey);

    public static SourceArtifactRef Artifact(char fill) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000b1", new string(fill, 64));

    /// <summary>An artifact reference whose digest differs from every other fixture's.</summary>
    public static SourceArtifactRef ObservedSet(string hexTail) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000c1",
            new string('0', 64 - hexTail.Length) + hexTail);

    public static AbsenceSubject Subject(string uri = RootUri, SourceObjectKeyRef? parent = null)
    {
        var subject = AbsenceSubject.TryCreate(
            SourceAuthority.Jolux, EntityKind(), uri, parent, out var refusal);
        if (subject is null)
        {
            throw new InvalidOperationException($"fixture subject refused as {refusal}");
        }

        return subject;
    }

    /// <summary>
    /// A complete comparison policy. <paramref name="variant"/> changes exactly the member named by
    /// <paramref name="changed"/>, so a caller can build A, B and a byte-identical return to A.
    /// </summary>
    public static AbsenceComparisonPolicy Policy(
        char variant = 'a',
        AbsenceComparisonPolicyMember? changed = null)
    {
        var rows = Enum.GetValues<AbsenceComparisonPolicyMember>()
            .Select(member => new AbsenceComparisonPolicyDigest(
                member,
                changed is null || changed == member
                    ? new string(variant, 64)
                    : new string('a', 64)))
            .ToArray();

        var policy = AbsenceComparisonPolicy.TryCreate(rows, out var refusal);
        if (policy is null)
        {
            throw new InvalidOperationException($"fixture policy refused as {refusal}");
        }

        return policy;
    }

    public static AbsenceFamilyObservation Observation(
        string observationId,
        DateTimeOffset at,
        string familyKey = "lu_root_family",
        string clockSource = "lex-ops-ntp-1",
        AbsenceTimestampPrecision precision = AbsenceTimestampPrecision.Second,
        TimeSpan? skew = null,
        AbsenceObservationProvenance provenance = AbsenceObservationProvenance.FreshlyExecuted)
    {
        var observation = AbsenceFamilyObservation.TryCreate(
            observationId,
            familyKey,
            at,
            precision,
            clockSource,
            skew ?? TimeSpan.FromSeconds(30),
            provenance,
            out var refusal);
        if (observation is null)
        {
            throw new InvalidOperationException($"fixture observation refused as {refusal}");
        }

        return observation;
    }

    /// <summary>
    /// The same real proof as <see cref="Proof"/>, minted for a run whose artifacts are held without
    /// an enforced retention floor. Not memoized: one caller, and the point is the class it carries.
    /// </summary>
    public static AbsenceFamilyEnumerationProof UnflooredProof(
        string familyKey = "lu_root_family", int runSeed = 930)
    {
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            familyKey,
            AbsenceEnumerationProofFixture.Delivery(familyKey, runSeed),
            CustodyMembership.RetainedUnenforced,
            out var refusal);
        if (proof is null)
        {
            throw new InvalidOperationException($"fixture proof refused as {refusal}");
        }

        return proof;
    }

    /// <summary>
    /// A real proof that one family's enumeration was delivered whole, built from a verified
    /// delivery comparison rather than stubbed. Memoized because assembling one costs four
    /// canonicalized evidence tuples and the ledger tests build many cuts; the objects are
    /// immutable, so sharing one across cuts changes nothing a test can observe.
    /// </summary>
    public static AbsenceFamilyEnumerationProof Proof(
        string familyKey = "lu_root_family", int runSeed = 930) =>
        Proofs.GetOrAdd((familyKey, runSeed), static key =>
        {
            var proof = AbsenceFamilyEnumerationProof.TryCreate(
                key.FamilyKey,
                AbsenceEnumerationProofFixture.Delivery(key.FamilyKey, key.RunSeed),
                CustodyMembership.Floored,
                out var refusal);
            if (proof is null)
            {
                throw new InvalidOperationException($"fixture proof refused as {refusal}");
            }

            return proof;
        });

    /// <summary>A real proof over a caller-named row set, for a door that binds to the rows.</summary>
    /// <remarks>
    /// Not memoized on the row set alone - the family key and seed are part of the identity too, and
    /// two families sharing one row set must not share one proof.
    /// </remarks>
    public static AbsenceFamilyEnumerationProof ProofOver(
        string familyKey,
        string rowValues,
        int runSeed = 930)
    {
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            familyKey,
            AbsenceEnumerationProofFixture.DeliveryOf(familyKey, runSeed, rowValues),
            CustodyMembership.Floored,
            out var refusal);
        if (proof is null)
        {
            throw new InvalidOperationException($"fixture proof refused as {refusal}");
        }

        return proof;
    }

    /// <summary>
    /// A proof and the canonical keys the rows it proves must carry, for a door that binds the two.
    /// </summary>
    /// <remarks>
    /// A citation may no longer be minted beside just any honest proof: the door re-derives the
    /// delivered rows' canonical-key digest and requires it to equal the proof's. So a fixture can no
    /// longer build rows and reach for a shared proof - the rows and the proof have to be one
    /// delivery. This returns both halves of exactly one. Set each row's canonical key to
    /// <c>Keys[i]</c> and its terms to whatever the family under test decodes; the producers read
    /// only the terms, so the two are independent by design.
    /// </remarks>
    /// <summary>
    /// A proof whose canonical keys ARE the supplied subjects, for a family that keys on its subject.
    /// </summary>
    /// <remarks>
    /// The Luxembourg inventory keys on <c>STR(?draft)</c>, so its citation door derives the proven
    /// population from the first key component rather than from the row terms - which a caller can
    /// replace while keeping a real proof's keys. A fixture for that door therefore has to prove the
    /// subjects themselves, not stand-in row values.
    /// </remarks>
    public static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRdfTerm[][] Keys) DeliveryOfSubjects(
        string familyKey,
        IReadOnlyList<string> subjects,
        int runSeed = 930)
    {
        // Cursors must strictly increase over the delivered order, so the subjects are keyed in
        // their own sorted order and the caller is told which order that was.
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

        // UNDER THIS FAMILY'S OWN PROFILE when it is this family. The inventory citation door binds
        // a proof to the Luxembourg inventory's exact interpretation profile, so a proof read under
        // the generic fixture profile evidences nothing there - which is precisely what a reviewer
        // demonstrated with a same-subject proof of another family.
        var lux = string.Equals(
            familyKey,
            LuxembourgInitialDraftInventoryDiscoveryPlan.PartitionMemberKeyForFixtures,
            StringComparison.Ordinal);

        // The request inventory is a separate family with its own profile, so a delivery built from
        // the draft plan evidences nothing at its citation door however alike the rows look.
        var request = string.Equals(
            familyKey,
            LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures,
            StringComparison.Ordinal);

        var delivery = lux
            ? AbsenceEnumerationProofFixture.LuxembourgInventoryDelivery(ordered, runSeed)
            : request
                ? AbsenceEnumerationProofFixture.LuxembourgOpinionRequestInventoryDelivery(
                    ordered, runSeed)
                : AbsenceEnumerationProofFixture.DeliveryOf(
                    familyKey, runSeed, string.Join(',', ordered), rawKeys: true);

        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            familyKey, delivery, CustodyMembership.Floored, out var refusal);
        if (proof is null)
        {
            throw new InvalidOperationException($"fixture proof refused as {refusal}");
        }

        // The family's own rows key on [key_1, key_2]; the generic ones key on a single id.
        var keys = ordered
            .Select(subject => lux || request
                ? new[]
                {
                    RepeatedEnumerationRdfTerm.Literal(subject, null, null),
                    RepeatedEnumerationRdfTerm.Literal(
                        LuxembourgInitialDraftInventoryDiscoveryPlan.IriKind, null, null),
                }
                : [RepeatedEnumerationRdfTerm.Iri(subject)])
            .ToArray();
        return (proof, keys);
    }

    /// <summary>
    /// An OpinionRequest inventory delivery in which one member is not addressable.
    /// </summary>
    /// <remarks>
    /// The keys carry the blank-node marker as delivered, so the proof covers them. A test cannot
    /// reach the citation door's blank-node rule by rewriting a key afterwards: that changes the
    /// canonical-key digest, and the proof binding refuses before the rule is consulted.
    /// </remarks>
    public static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRdfTerm[][] Keys)
        OpinionRequestInventoryWithNonAddressableMember(
            IReadOnlyList<string> subjects,
            int nonAddressableAt,
            int runSeed = 933)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var familyKey = LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures;
        var delivery = AbsenceEnumerationProofFixture.LuxembourgOpinionRequestInventoryDelivery(
            ordered, runSeed, nonAddressableAt: nonAddressableAt);

        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            familyKey, delivery, CustodyMembership.Floored, out var refusal);
        if (proof is null)
        {
            throw new InvalidOperationException($"fixture proof refused as {refusal}");
        }

        var keys = ordered
            .Select((subject, index) => new[]
            {
                RepeatedEnumerationRdfTerm.Literal(subject, null, null),
                RepeatedEnumerationRdfTerm.Literal(
                    AbsenceEnumerationProofFixture.KindAt(index, nonAddressableAt), null, null),
            })
            .ToArray();
        return (proof, keys);
    }

    /// <summary>
    /// A proven request-graph batch delivery of NAMED rows, terms and keys together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the coverage door actually consumes. It reads each row's terms and requires them to
    /// describe that row's proof-covered key, so the rows a test delivers are the rows the matrix
    /// reads - there is no second, unbound projection to hand it.
    /// </para>
    /// <para>
    /// SORTED INTO KEY ORDER HERE. A proven delivery is necessarily key-ordered, because Source/Core
    /// requires cursors to strictly increase, so a fixture wanting a proof has to deliver in that
    /// order rather than in whatever order a test listed its rows.
    /// </para>
    /// </remarks>
    public static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRow[] Rows)
        OpinionRequestGraphRows(
            string partitionKey,
            IReadOnlyList<(string Subject, string Predicate, string? Value, bool ValueIsIri)> rows,
            int runSeed = 938)
    {
        const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;

        static string Kind(bool isIri) =>
            isIri ? LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind : "literal";

        var ordered = rows
            .OrderBy(static row => row.Subject, StringComparer.Ordinal)
            .ThenBy(static row => row.Predicate, StringComparer.Ordinal)
            .ThenBy(
                static row => LuxembourgPublisherCursorCodec.ComputeKey(row.Value ?? string.Empty),
                StringComparer.Ordinal)
            .ThenBy(static row => Kind(row.ValueIsIri), StringComparer.Ordinal)
            .ToArray();

        var delivery = AbsenceEnumerationProofFixture.LuxembourgOpinionRequestGraphRowDelivery(
            partitionKey, ordered, runSeed);

        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            partitionKey, delivery, CustodyMembership.Floored, out var refusal);
        if (proof is null)
        {
            throw new InvalidOperationException($"fixture proof refused as {refusal}");
        }

        var built = ordered
            .Select(row =>
            {
                var valueKind = Kind(row.ValueIsIri);
                var valueTerm = row.ValueIsIri
                    ? RepeatedEnumerationRdfTerm.Iri(row.Value!)
                    : RepeatedEnumerationRdfTerm.Literal(row.Value ?? string.Empty, null, null);
                var key = new[]
                {
                    RepeatedEnumerationRdfTerm.Literal(row.Subject, null, null),
                    RepeatedEnumerationRdfTerm.Literal(IriKind, null, null),
                    RepeatedEnumerationRdfTerm.Literal(row.Predicate, null, null),
                    RepeatedEnumerationRdfTerm.Literal(
                        LuxembourgPublisherCursorCodec.ComputeKey(row.Value ?? string.Empty),
                        null,
                        null),
                    RepeatedEnumerationRdfTerm.Literal(valueKind, null, null),
                    RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
                    RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
                };

                // The projection's own order: the seven keys are projected columns too.
                var terms = new[]
                {
                    RepeatedEnumerationRdfTerm.Iri(row.Subject),
                    RepeatedEnumerationRdfTerm.Literal(IriKind, null, null),
                    RepeatedEnumerationRdfTerm.Iri(row.Predicate),
                    valueTerm,
                    RepeatedEnumerationRdfTerm.Literal(valueKind, null, null),
                    RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
                    RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
                    RepeatedEnumerationRdfTerm.Literal("1", "http://www.w3.org/2001/XMLSchema#integer", null),
                }.Concat(key).ToArray();

                return new RepeatedEnumerationRow(terms, key, key);
            })
            .ToArray();

        return (proof, built);
    }

    /// <summary>
    /// A batch's graph delivery, proven under the request graph plan's own interpretation profile.
    /// </summary>
    /// <remarks>
    /// The batch citation door binds that profile, so this is the only shape an honest batch proof
    /// can have. <see cref="ProofNamingFamilyUnderAnotherProfile"/> is its mirror: same partition,
    /// same rows, another profile, and the door must refuse it.
    /// </remarks>
    public static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRdfTerm[][] Keys)
        OpinionRequestGraphBatchDelivery(
            string partitionKey,
            IReadOnlyList<string> rowValues,
            int runSeed = 934)
    {
        var ordered = rowValues.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var delivery = AbsenceEnumerationProofFixture.LuxembourgOpinionRequestGraphDelivery(
            partitionKey, ordered, runSeed);

        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            partitionKey, delivery, CustodyMembership.Floored, out var refusal);
        if (proof is null)
        {
            throw new InvalidOperationException($"fixture proof refused as {refusal}");
        }

        const string IriKind = LuxembourgOpinionRequestInventoryDiscoveryPlan.IriKind;
        const string Predicate = LuxembourgOpinionRequestGraphDiscoveryPlan.ReferralDatePredicateIri;
        var keys = ordered
            .Select(value => new[]
            {
                RepeatedEnumerationRdfTerm.Literal("urn:delivered:" + value, null, null),
                RepeatedEnumerationRdfTerm.Literal(IriKind, null, null),
                RepeatedEnumerationRdfTerm.Literal(Predicate, null, null),
                RepeatedEnumerationRdfTerm.Literal(
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))),
                    null,
                    null),
                RepeatedEnumerationRdfTerm.Literal("literal", null, null),
                RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
                RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            })
            .ToArray();
        return (proof, keys);
    }

    /// <summary>
    /// A proof carrying a family's NAME but read under this fixture's generic profile.
    /// </summary>
    /// <remarks>
    /// The sharpest form of the authority question: the label is right and the subjects are right,
    /// and the enumeration was still read under another dialect, projection and query family. A door
    /// that only compares family keys accepts it.
    /// </remarks>
    /// <summary>
    /// A proof read under the Luxembourg inventory's own profile, but OF another family.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="ProofNamingFamilyUnderAnotherProfile"/>: right profile, wrong
    /// family. Without it the citation door's family check has nothing that fails only because of
    /// it, and a guard nothing exercises is not a guard.
    /// </remarks>
    public static AbsenceFamilyEnumerationProof ProofOfAnotherFamilyUnderTheInventoryProfile(
        string familyKey,
        IReadOnlyList<string> subjects,
        int runSeed = 932)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            familyKey,
            AbsenceEnumerationProofFixture.LuxembourgInventoryDelivery(ordered, runSeed, familyKey),
            CustodyMembership.Floored,
            out var refusal);
        if (proof is null)
        {
            throw new InvalidOperationException($"fixture proof refused as {refusal}");
        }

        return proof;
    }

    public static AbsenceFamilyEnumerationProof ProofNamingFamilyUnderAnotherProfile(
        string familyKey,
        IReadOnlyList<string> subjects,
        int runSeed = 931)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            familyKey,
            AbsenceEnumerationProofFixture.DeliveryOf(
                familyKey, runSeed, string.Join(',', ordered), rawKeys: true),
            CustodyMembership.Floored,
            out var refusal);
        if (proof is null)
        {
            throw new InvalidOperationException($"fixture proof refused as {refusal}");
        }

        return proof;
    }

    public static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRdfTerm[][] Keys) Delivery(
        string familyKey,
        int rowCount,
        int runSeed = 930)
    {
        // KEYED BY FAMILY, not by position alone. Two enumerations of different families deliver
        // different subjects, so their canonical keys must differ - otherwise a proof of one family
        // digests identically to a proof of another and a door binding on that digest cannot tell
        // them apart. Found by the unrelated-proof regression, which passed for the wrong reason
        // while every family shared one key set.
        //
        // Zero-padded after the family token so lexical order still matches emission order: the
        // delivery proof requires cursors to strictly increase, and "d10" sorts before "d2".
        var token = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(familyKey)))[..8];
        var values = Enumerable.Range(0, rowCount)
            .Select(index => token + "-d" + index.ToString("D6"))
            .ToArray();
        var proof = ProofOver(familyKey, string.Join(',', values), runSeed);
        var keys = values
            .Select(static value => new[]
            {
                RepeatedEnumerationRdfTerm.Iri(AbsenceEnumerationProofFixture.CanonicalKeyFor(value)),
            })
            .ToArray();
        return (proof, keys);
    }

    public static AbsenceCut Cut(
        string runId,
        DateTimeOffset at,
        IReadOnlyList<string> observedKeys,
        AbsenceRunCompletion completion = AbsenceRunCompletion.EnumerationComplete,
        AbsenceApplicableSet applicableSet = AbsenceApplicableSet.ObservedRootSet,
        string? observedSetTail = null,
        IReadOnlyList<AbsenceFamilyObservation>? observations = null)
    {
        var members = observations ?? [Observation(runId + "-obs-1", at)];
        var cut = completion == AbsenceRunCompletion.EnumerationComplete
            ? AbsenceCut.TryCreateComplete(
                runId,
                applicableSet,
                members,
                members.Select(static member => Proof(member.FamilyKey)).ToArray(),
                Artifact('e'),
                ObservedSet(observedSetTail ?? "1"),
                observedKeys,
                out var refusal)
            : AbsenceCut.TryCreatePartial(
                runId,
                applicableSet,
                members,
                Artifact('e'),
                ObservedSet(observedSetTail ?? "1"),
                observedKeys,
                out refusal);
        if (cut is null)
        {
            throw new InvalidOperationException($"fixture cut refused as {refusal}");
        }

        return cut;
    }

    public static AbsenceHistoryLedger Ledger(
        AbsenceSubject? subject = null,
        AbsenceComparisonPolicy? policy = null,
        AbsenceApplicableSet axis = AbsenceApplicableSet.ObservedRootSet,
        string trackingEventId = "track-1")
    {
        var ledger = AbsenceHistoryLedger.TryOpen(
            subject ?? Subject(), axis, policy ?? Policy(), trackingEventId, out var refusal);
        if (ledger is null)
        {
            throw new InvalidOperationException($"fixture ledger refused as {refusal}");
        }

        return ledger;
    }

    public static AbsenceReplacementCoordinateProfile CoordinateProfile()
    {
        var profile = AbsenceReplacementCoordinateProfile.TryCreate(
            new string('f', 64),
            [
                new AbsenceCoordinateField("memorial_series", AbsenceCoordinateFieldKind.StablePublisherField),
                new AbsenceCoordinateField("act_family", AbsenceCoordinateFieldKind.FamilyRule),
                new AbsenceCoordinateField("publication_date", AbsenceCoordinateFieldKind.PublisherDate),
            ],
            out var refusal);
        if (profile is null)
        {
            throw new InvalidOperationException($"fixture profile refused as {refusal}");
        }

        return profile;
    }

    /// <summary>The base instant every timing fixture measures from. Aligned to whole seconds.</summary>
    public static DateTimeOffset Base { get; } =
        new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A parent key reference bound to the same entity-kind registry as the child.</summary>
    public static SourceObjectKeyRef Parent(
        string uri = ParentUri,
        string memberKey = "consolidation",
        string canonicalKey = "lu/recueil/2004") =>
        new(EntityKind(memberKey), uri, canonicalKey, Sha256Of(canonicalKey));

    public static string Sha256Of(string value) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}

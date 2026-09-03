using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;

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
                out var refusal);
            if (proof is null)
            {
                throw new InvalidOperationException($"fixture proof refused as {refusal}");
            }

            return proof;
        });

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

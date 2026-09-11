using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// What it takes to mint an OpinionRequest inventory citation, and to batch the population it names.
/// </summary>
/// <remarks>
/// The citation is what every later conclusion rests on: batches, coverage and reconciliation all
/// assume it names a population some enumeration actually delivered. Requiring a proof makes it
/// impossible to write out of nothing; these cases are about the gap between that and binding to
/// the right enumeration.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionRequestBatchingTests
{
    private const string Family =
        LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKeyForFixtures;

    private static string Request(int index) =>
        $"http://data.legilux.public.lu/eli/dl/pl/2000/{index:D3}/evenement/sace/1";

    /// <summary>An honest pairing mints, or every refusal below passes by refusing everything.</summary>
    [TestMethod]
    public void AProvenDeliveryMintsACitationOverItsOwnPopulation()
    {
        var subjects = Subjects(4);
        var (proof, rows) = Delivery(subjects);

        var citation = LuxembourgOpinionRequestInventoryCitation.MintedOver(
            proof, rows, subjects);

        Assert.AreEqual(Family, citation.FamilyKey);
        Assert.AreEqual(subjects.Count, citation.SubjectCount);
        Assert.AreEqual(
            LuxembourgOpinionRequestGraphDiscoveryPlan.SelectionDigestFor(subjects),
            citation.SelectionDigest,
            "the citation digests the population it hands to batching.");
    }

    /// <summary>
    /// A proof of another family proves nothing here, whatever subjects it carries.
    /// </summary>
    /// <remarks>
    /// Membership is not authority. Every subject can be genuinely proven and the run that proved
    /// them still not be this family's inventory.
    /// </remarks>
    [TestMethod]
    public void AProofOfAnotherFamilyMintsNothing()
    {
        var subjects = Subjects(3);
        var (elsewhere, keys) = AbsenceFixtures.DeliveryOfSubjects(
            "unrelated-enumeration-family", subjects);
        var rows = subjects
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)],
                keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestInventoryCitation.MintedOver(
                elsewhere, rows, subjects),
            "proving these subjects somewhere is not proving this family's inventory.");
    }

    /// <summary>
    /// The draft inventory's own proof is refused here, which is the closest wrong answer.
    /// </summary>
    /// <remarks>
    /// THE CASE A NEAR-COPY MAKES LIKELY. The two inventories differ only in class, subject variable
    /// and coordinates; a delivery built from the other family's plan looks structurally identical.
    /// It is refused because the door binds the proof to this family's partition key and to its
    /// interpretation profile, not to the shape of its rows.
    /// </remarks>
    [TestMethod]
    public void TheDraftInventorysProofIsRefusedByThisFamilysDoor()
    {
        var subjects = Subjects(3);
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (draftProof, keys) = AbsenceFixtures.DeliveryOfSubjects(
            LuxembourgInitialDraftInventoryDiscoveryPlan.PartitionMemberKeyForFixtures, ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();

        var refusal = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestInventoryCitation.MintedOver(
                draftProof, rows, ordered),
            "the other inventory's enumeration is not this one, however alike the rows look.");
        StringAssert.Contains(
            refusal.Message, LuxembourgOpinionRequestInventoryDiscoveryPlan.PartitionMemberKey,
            "and the refusal names the family this proof is not of.");
    }

    /// <summary>
    /// The right family name, read under another profile, is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CASE THAT MAKES THE PROFILE CHECK LOAD-BEARING, and it was missing. Deleting that check
    /// left every other case here green: the near-copy case above is refused by the family-key
    /// check alone, so the profile check was a guard nothing exercised.
    /// </para>
    /// <para>
    /// Here the label and the subjects both match and only the dialect, projection and query family
    /// differ. A door comparing family keys accepts it; this family is defined by its own profile,
    /// not only by its name.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheRightFamilyNameReadUnderAnotherProfileIsRefused()
    {
        var subjects = Subjects(3);
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var wrongProfile = AbsenceFixtures.ProofNamingFamilyUnderAnotherProfile(Family, ordered);
        var (_, keys) = AbsenceFixtures.DeliveryOfSubjects(Family, ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();

        var refusal = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestInventoryCitation.MintedOver(
                wrongProfile, rows, ordered),
            "this family is defined by its own profile, not only by its name.");

        // THE MESSAGE, BECAUSE THE EXCEPTION TYPE CANNOT TELL THE GUARDS APART. Every door here
        // throws ArgumentException, so a bare type assertion passes when the profile check is
        // deleted and the key-digest check refuses instead - measured: deleting it left 8 of 8
        // green. Naming the guard is what makes this case pin the profile check rather than merely
        // observe that something refused.
        StringAssert.Contains(
            refusal.Message, "read under another interpretation profile",
            "the refusal must come from the profile check, not from a later one.");
    }

    /// <summary>A population naming a subject the delivery never proved is refused.</summary>
    /// <remarks>
    /// The population is read from the proven canonical KEY, not from the terms: a row carries
    /// Terms and CanonicalKey as independently settable lists and only the keys are covered by the
    /// proof's digest.
    /// </remarks>
    [TestMethod]
    public void APopulationNamingAnUndeliveredSubjectIsRefused()
    {
        var subjects = Subjects(3);
        var (proof, rows) = Delivery(subjects);
        const string Unobserved =
            "http://data.legilux.public.lu/eli/dl/pl/1999/999/evenement/sace/1";

        var termsRewritten = rows
            .Select(row => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(Unobserved)], row.CanonicalKey, row.Cursor))
            .ToArray();

        var refusal = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestInventoryCitation.MintedOver(
                proof, termsRewritten, [Unobserved]),
            "the proved keys do not name this subject, whatever the terms beside them say.");
        StringAssert.Contains(
            refusal.Message, "not the addressable inventory this proof proves",
            "the refusal must be about the population, not about the rows or the family.");
    }

    /// <summary>A population repeating a subject is refused rather than recorded.</summary>
    /// <remarks>
    /// It would digest identically to one naming that subject once, and only the count separates
    /// them — so the batch arithmetic downstream would be over a population nobody enumerated.
    /// </remarks>
    [TestMethod]
    public void APopulationRepeatingASubjectIsRefused()
    {
        var subjects = Subjects(3);
        var (proof, rows) = Delivery(subjects);

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestInventoryCitation.MintedOver(
                proof, rows, [.. subjects, subjects[0]]),
            "an inventory population names each subject once.");
    }

    /// <summary>
    /// A population omitting a proven subject is refused: subset membership is not completeness.
    /// </summary>
    /// <remarks>
    /// THE DEFECT THIS REPLACES. The binder checked only that each named subject appeared among the
    /// proven keys, so three of four proven subjects minted a citation over a population no run
    /// enumerated - and every later batch, coverage and reconciliation would have rested on it.
    /// </remarks>
    [TestMethod]
    public void APopulationOmittingAProvenSubjectIsRefused()
    {
        var subjects = Subjects(4);
        var (proof, rows) = Delivery(subjects);

        var refusal = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestInventoryCitation.MintedOver(
                proof, rows, subjects.Take(3).ToArray()),
            "naming a subset of the delivery is not naming the inventory.");
        StringAssert.Contains(refusal.Message, "omitted");
    }

    /// <summary>An empty population beside a non-empty delivery is the same defect at its largest.</summary>
    [TestMethod]
    public void AnEmptyPopulationBesideANonEmptyDeliveryIsRefused()
    {
        var subjects = Subjects(4);
        var (proof, rows) = Delivery(subjects);

        var refusal = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, []),
            "an early return for an empty list let a caller name nothing and mint a citation.");
        StringAssert.Contains(refusal.Message, "addressable inventory");
    }

    /// <summary>
    /// A non-addressable member stops the inventory rather than vanishing from it.
    /// </summary>
    /// <remarks>
    /// The plan reports a blank-node member instead of filtering it, precisely so this door can
    /// refuse. Reading only the subject key ignored the kind entirely, so the plan's rule was
    /// unenforced here and such a member would simply have been absent from the population.
    /// </remarks>
    [TestMethod]
    public void ANonAddressableMemberRefusesTheCitation()
    {
        var subjects = Subjects(3);
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

        // DELIVERED non-addressable, not rewritten afterwards. Editing a canonical key changes the
        // key digest, so the proof binding refuses first and this rule is never reached - which is
        // how the first version of this test passed for the wrong reason.
        var (proof, keys) = AbsenceFixtures.OpinionRequestInventoryWithNonAddressableMember(
            ordered, nonAddressableAt: 1);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();

        var refusal = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, ordered),
            "no exact membership list exists over an identity that does not survive its delivery.");
        StringAssert.Contains(refusal.Message, "non-addressable");
    }

    /// <summary>An assignment cannot be built for a population the citation does not name.</summary>
    [TestMethod]
    public void AnAssignmentCannotBeBuiltForSubjectsTheCitationDoesNotName()
    {
        var subjects = Subjects(4);
        var citation = Citation(subjects);

        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestBatchAssignment.Over(subjects.Take(3).ToArray(), citation),
            "a shorter list is not the population this citation digests.");
        Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestBatchAssignment.Over(
                [.. subjects.Take(3), Request(999)], citation),
            "nor is a same-length list with a substituted member.");
    }

    /// <summary>
    /// Every member appears in exactly one batch, and the batches reassemble the population.
    /// </summary>
    /// <remarks>
    /// The reviewer disposition requires it directly. Asserted over a population that spans batch
    /// boundaries, because chunking that is obviously right for one batch is where an off-by-one
    /// hides.
    /// </remarks>
    [TestMethod]
    public void EveryMemberAppearsInExactlyOneBatchAndTheBatchesReassembleThePopulation()
    {
        var capacity = LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity;
        var subjects = Subjects((capacity * 2) + 7);
        var assignments = LuxembourgOpinionRequestBatchAssignment.Over(subjects, Citation(subjects));

        Assert.HasCount(3, assignments, "two full batches and a short final one.");
        CollectionAssert.AreEqual(
            subjects.ToArray(),
            assignments.SelectMany(static value => value.Requests).ToArray(),
            "the batches reassemble the population exactly once, in its own order.");
        Assert.AreEqual(
            subjects.Count,
            assignments.SelectMany(static value => value.Requests).Distinct(StringComparer.Ordinal).Count(),
            "no member appears twice.");
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 }, assignments.Select(static value => value.Ordinal).ToArray());
        Assert.HasCount(7, assignments[^1].Requests, "the short final batch is not padded here.");

        // Each batch carries its OWN partition key, or the batches are indistinguishable in their
        // own receipts and no omitted or duplicated batch could be detected from them.
        Assert.AreEqual(
            assignments.Count,
            assignments.Select(static value => value.PartitionKey).Distinct(StringComparer.Ordinal).Count(),
            "a constant key would make a multi-batch cover unimplementable.");
        foreach (var assignment in assignments)
        {
            StringAssert.StartsWith(assignment.PartitionKey, "legilux-opinion-request-graph-batch-");
        }
    }

    /// <summary>
    /// One proven inventory has one partition, whatever order its population arrives in.
    /// </summary>
    /// <remarks>
    /// The citation digests the SET, so it accepts any permutation of its own population. Chunking
    /// the caller's order then gave the same inventory different batches with different partition
    /// keys - so which batch a subject belonged to, and what any batch citation named, depended on
    /// an argument order nothing recorded.
    /// </remarks>
    [TestMethod]
    public void OneProvenInventoryHasOnePartitionWhateverOrderItArrivesIn()
    {
        var subjects = Subjects(LuxembourgOpinionRequestGraphDiscoveryPlan.BatchCapacity + 1);
        var citation = Citation(subjects);
        var reversed = subjects.Reverse().ToArray();

        var forward = LuxembourgOpinionRequestBatchAssignment.Over(subjects, citation);
        var backward = LuxembourgOpinionRequestBatchAssignment.Over(reversed, citation);

        CollectionAssert.AreEqual(
            forward.Select(static value => value.PartitionKey).ToArray(),
            backward.Select(static value => value.PartitionKey).ToArray(),
            "the same inventory must not partition differently because a caller reversed a list.");
        CollectionAssert.AreEqual(
            forward.SelectMany(static value => value.Requests).ToArray(),
            backward.SelectMany(static value => value.Requests).ToArray(),
            "and every subject must land in the same batch either way.");
    }

    /// <summary>
    /// A batch citation is derived from the assignment and its proof, not from caller arguments.
    /// </summary>
    /// <remarks>
    /// Every field a caller could have stated is taken from the batch the inventory issued or from
    /// the bound delivery: the selection digest and requested count come from the assignment, the
    /// delivered count from the rows, the run from the proof. There is nothing left to assert.
    /// </remarks>
    [TestMethod]
    public void ABatchCitationDerivesEveryFieldFromTheAssignmentAndItsProof()
    {
        var subjects = Subjects(4);
        var assignment = LuxembourgOpinionRequestBatchAssignment.Over(subjects, Citation(subjects))[0];
        var (proof, rows) = BatchDelivery(assignment);

        var citation = LuxembourgOpinionRequestBatchCitation.ForDelivery(proof, rows, assignment);

        Assert.AreEqual(assignment.PartitionKey, citation.PartitionKey);
        Assert.AreEqual(assignment.Requests.Count, citation.RequestedCount);
        Assert.AreEqual(rows.Length, citation.DeliveredRowCount);
        Assert.AreEqual(
            LuxembourgOpinionRequestGraphDiscoveryPlan.SelectionDigestFor(assignment.Requests),
            citation.SelectionDigest,
            "the digest is over the members the inventory issued, not a caller's list.");
        Assert.AreEqual(proof.AcquisitionRunRef, citation.AcquisitionRunRef);
    }

    /// <summary>
    /// A batch citing a delivery whose proof proves another partition is refused.
    /// </summary>
    [TestMethod]
    public void ABatchCitationMustNameThePartitionItsProofProves()
    {
        var subjects = Subjects(4);
        var assignment = LuxembourgOpinionRequestBatchAssignment.Over(subjects, Citation(subjects))[0];

        // An honest delivery of a DIFFERENT partition, under this family's own profile: right
        // shape, right dialect, wrong batch. Only the partition differs, so only the partition
        // check can refuse it.
        var ordered = assignment.Requests.OrderBy(static v => v, StringComparer.Ordinal).ToArray();
        var (otherProof, otherKeys) = AbsenceFixtures.OpinionRequestGraphBatchDelivery(
            "legilux-opinion-request-graph-batch-elsewhere", ordered);
        var otherRows = ordered
            .Select((value, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri("urn:delivered:" + value)], otherKeys[index],
                [RepeatedEnumerationRdfTerm.Iri("urn:delivered:" + value)]))
            .ToArray();

        var refusal = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestBatchCitation.ForDelivery(otherProof, otherRows, assignment),
            "a proof of another partition says nothing about this batch.");
        StringAssert.Contains(refusal.Message, "names partition");
    }

    /// <summary>
    /// A delivery of this exact partition, read under another profile, mints nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PARTITION KEY IS NOT FAMILY AUTHORITY. It digests the batch's members, so a delivery
    /// some other query family produced over the same members carries the same key: the partition
    /// check agrees, the rows bind to their own proof, and until the profile was bound the citation
    /// minted over a delivery read under another dialect, projection and keyset entirely.
    /// </para>
    /// <para>
    /// The assertion is on the guard's OWN message, not merely on the exception type. Every refusal
    /// on this door is an ArgumentException, so a case that only asserted the type would pass by
    /// falling through to the partition or row binding beside it - which is how the interpretation
    /// check on the inventory door was found to be unexercised.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void ABatchDeliveryReadUnderAnotherProfileMintsNothing()
    {
        var subjects = Subjects(4);
        var assignment = LuxembourgOpinionRequestBatchAssignment.Over(subjects, Citation(subjects))[0];
        var (honestProof, rows) = BatchDelivery(assignment);

        // Same partition, same delivered rows - and a proof of it read under another profile.
        var foreignProof = AbsenceFixtures.ProofNamingFamilyUnderAnotherProfile(
            assignment.PartitionKey, assignment.Requests);

        Assert.AreEqual(
            honestProof.FamilyKey, foreignProof.FamilyKey,
            "the partition check cannot tell these apart, which is why the profile must.");

        var refusal = Assert.ThrowsExactly<ArgumentException>(
            () => LuxembourgOpinionRequestBatchCitation.ForDelivery(foreignProof, rows, assignment),
            "another query family's delivery does not evidence this family's batch.");
        StringAssert.Contains(refusal.Message, "another interpretation profile");
        StringAssert.Contains(refusal.Message, "this family's graph");
    }

    /// <summary>
    /// A batch cites a delivery that names none of its members, and that is not a defect.
    /// </summary>
    /// <remarks>
    /// THE CASE THE ROW/POPULATION SPLIT EXISTS FOR. A batch asks about requests the publisher may
    /// hold nothing for, so a member legitimately appears in no delivered row. Binding a batch the
    /// way an inventory is bound - requiring the rows to name the exact membership - would refuse
    /// honest deliveries; and collapsing the two back into one method with an early return on an
    /// empty list is what previously let an INVENTORY citation be minted over no population at all.
    /// </remarks>
    [TestMethod]
    public void ABatchMayCiteADeliveryThatNamesNoneOfItsMembers()
    {
        var subjects = Subjects(4);
        var assignment = LuxembourgOpinionRequestBatchAssignment.Over(subjects, Citation(subjects))[0];

        // A proven delivery of this partition whose rows are about something else entirely - the
        // publisher held nothing for these members.
        var (proof, rows) = BatchDelivery(assignment, ["row-a", "row-b"]);

        var citation = LuxembourgOpinionRequestBatchCitation.ForDelivery(proof, rows, assignment);

        Assert.AreEqual(
            assignment.Requests.Count, citation.RequestedCount,
            "the batch still records what it asked about.");
        Assert.AreEqual(
            rows.Length, citation.DeliveredRowCount,
            "and what came back, which may be about none of them.");
    }

    /// <summary>
    /// A proven delivery of this batch's own partition, read under this family's graph profile.
    /// </summary>
    /// <remarks>
    /// UNDER THE GRAPH PLAN'S PROFILE, not the generic fixture one. The citation door binds that
    /// profile, and it was a generic-profile proof that minted a citation here before the door did.
    /// </remarks>
    private static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRow[] Rows) BatchDelivery(
        LuxembourgOpinionRequestBatchAssignment assignment,
        IReadOnlyList<string>? rowValues = null)
    {
        var values = rowValues ?? assignment.Requests;
        var ordered = values.OrderBy(static v => v, StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.OpinionRequestGraphBatchDelivery(
            assignment.PartitionKey, ordered);
        var rows = ordered
            .Select((value, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri("urn:delivered:" + value)], keys[index],
                [RepeatedEnumerationRdfTerm.Iri("urn:delivered:" + value)]))
            .ToArray();
        return (proof, rows);
    }

    private static IReadOnlyList<string> Subjects(int count) =>
        Enumerable.Range(0, count).Select(Request)
            .OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Keyed on the subjects themselves, because the door derives the proven population from the
    /// first key component. Ordered to match the fixture's own delivered order.
    /// </summary>
    private static (AbsenceFamilyEnumerationProof Proof, RepeatedEnumerationRow[] Rows) Delivery(
        IReadOnlyList<string> subjects)
    {
        var ordered = subjects.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var (proof, keys) = AbsenceFixtures.DeliveryOfSubjects(Family, ordered);
        var rows = ordered
            .Select((subject, index) => new RepeatedEnumerationRow(
                [RepeatedEnumerationRdfTerm.Iri(subject)],
                keys[index],
                [RepeatedEnumerationRdfTerm.Iri(subject)]))
            .ToArray();
        return (proof, rows);
    }

    private static LuxembourgOpinionRequestInventoryCitation Citation(IReadOnlyList<string> subjects)
    {
        var (proof, rows) = Delivery(subjects);
        return LuxembourgOpinionRequestInventoryCitation.MintedOver(proof, rows, subjects);
    }
}

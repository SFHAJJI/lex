using System;
using System.Linq;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// All thirteen CDM predicate IRIs <see cref="EuObjectFactsDiscoveryPlan.CdmIri"/> mints, pinned as
/// literals.
/// </summary>
/// <remarks>
/// <para>
/// TWO INDEPENDENT REASONS, and the packet carries both because either alone would be weaker.
/// ONE, Decision 80: <c>CdmIri</c> became a PUBLIC door in D1-05g so the adapter could stop
/// hand-copying one of these IRIs as a string literal, and a public door has to be pinned.
/// TWO, and this is the one that makes it coverage rather than ceremony: THE MAPPING WAS UNPINNED.
/// Every one of the six test uses of <c>CdmIri</c> CALLS it to build its own expected value, so
/// both sides of those assertions move together and a wrong IRI is invisible to all of them. That
/// is the self-referential fixture this repository has been caught by before.
/// </para>
/// <para>
/// WHAT A WRONG IRI COSTS, measured rather than imagined. D1-05g exists because one predicate IRI
/// was wrong in exactly this way: the adapter asked for <c>resource_legal_type</c> while the switch
/// it fed spoke <c>work_has_resource-type</c>'s vocabulary, the two conditions could not both hold
/// against the publisher's data, and the guard was dead for as long as it existed with every test
/// green. These are the strings this repository sends to a publisher; nothing else checks them.
/// </para>
/// <para>
/// TRANSCRIBED FROM PRINTED OUTPUT, never typed by hand and never derived from the switch inside
/// this test. Re-derive the same way: print <c>CdmIri</c> over <see cref="EuCdmPredicate"/>'s
/// values from a throwaway test and paste the block.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuCdmPredicateIriTests
{
    [TestMethod]
    public void EveryClosedCdmPredicateMintsItsExactIri()
    {
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#resource_legal_id_celex",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalIdCelex));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#expression_belongs_to_work",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#resource_legal_type",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalType));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#work_has_resource-type",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkHasResourceType));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#work_date_document",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkDateDocument));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#act_consolidated_date",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ActConsolidatedDate));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#date_creation_legacy",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.DateCreationLegacy));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#resource_legal_in-force",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalInForce));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#expression_uses_language",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#expression_title",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionTitle));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#expression_title_short",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionTitleShort));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#work_is_about_concept_eurovoc",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkIsAboutConceptEurovoc));
            Assert.AreEqual(
                "http://publications.europa.eu/ontology/cdm#resource_legal_is_about_concept_directory-code",
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalIsAboutConceptDirectoryCode));
    }

    [TestMethod]
    public void ThePinCoversEveryMemberOfTheClosedVocabulary()
    {
        // Without this, adding a member to EuCdmPredicate leaves it unpinned and the test above
        // still passes, which is the same hole the vocabulary census exists to close one level up.
        Assert.AreEqual(
            13,
            Enum.GetValues<EuCdmPredicate>().Length,
            "EuCdmPredicate gained or lost a member. Re-print the IRI block above and paste it, "
                + "rather than editing this number to match.");

        // Every minted IRI is distinct: two predicates sharing one IRI would make the plan ask the
        // same question twice and silently drop the other predicate's facts.
        var minted = Enum.GetValues<EuCdmPredicate>()
            .Select(EuObjectFactsDiscoveryPlan.CdmIri)
            .ToArray();
        Assert.AreEqual(
            minted.Length,
            minted.Distinct(StringComparer.Ordinal).Count(),
            "two closed predicates mint the same IRI: " + string.Join(", ", minted));
    }
}

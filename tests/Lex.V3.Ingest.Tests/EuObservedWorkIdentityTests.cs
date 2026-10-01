using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuObservedWorkIdentityTests
{
    private const string State = "http://publications.europa.eu/resource/cellar/aaaaaaaa-0000-0000-0000-00000000000a";
    private const string StringType = "http://www.w3.org/2001/XMLSchema#string";

    [TestMethod]
    public void ConsolidatedWorkUsesItsOwnObservedCelexWhileOriginalKeepsTheReviewedSeed()
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder.Single(seed => seed.Celex == "32016R0679");
        var facts = Facts(RepeatedEnumerationRdfTerm.Literal("02016R0679-20160504", StringType, null));
        Assert.AreEqual("02016R0679-20160504", EuObservedWorkIdentity.Resolve(facts, State));
        Assert.AreEqual("32016R0679", EuObservedWorkIdentity.Resolve(facts, seed.WorkRoot));
        Assert.IsNull(EuObservedWorkIdentity.Resolve(facts, State, originalOnly: true));
    }

    [TestMethod]
    [DataRow("02016R0679-20160230")]
    [DataRow("02019R0947-20160504")]
    [DataRow("32016R0679")]
    [DataRow("02016R0679-20160504R(01)")]
    public void InvalidOrUnrelatedIdentifierDoesNotAdmitAConsolidatedPackage(string celex) =>
        Assert.IsNull(EuObservedWorkIdentity.Resolve(Facts(RepeatedEnumerationRdfTerm.Literal(celex, StringType, null)), State));

    [TestMethod]
    public void MissingAmbiguousOrWrongRdfKindsAreTypedAsUnadmitted()
    {
        var valid = RepeatedEnumerationRdfTerm.Literal("02016R0679-20160504", StringType, null);
        foreach (var term in new[] { RepeatedEnumerationRdfTerm.Unbound(),
                     RepeatedEnumerationRdfTerm.Iri(valid.Value!),
                     RepeatedEnumerationRdfTerm.Literal(valid.Value!, null, "fr"),
                     RepeatedEnumerationRdfTerm.Literal(valid.Value!, null, null) })
            Assert.IsNull(EuObservedWorkIdentity.Resolve(Facts(term), State));
        Assert.IsNull(EuObservedWorkIdentity.Resolve(Facts(valid,
            RepeatedEnumerationRdfTerm.Literal("02016R0679-20160505", StringType, null)), State));
        Assert.IsNull(EuObservedWorkIdentity.Resolve(Facts(), State));
    }

    [TestMethod]
    public void WrongCensusRootDoesNotAdmitAnOtherwiseValidIdentifier()
    {
        var facts = Facts(RepeatedEnumerationRdfTerm.Literal("02016R0679-20160504", StringType, null));
        Assert.IsNull(EuObservedWorkIdentity.Resolve([facts[0] with { RootWorkIri = State }], State));
        Assert.IsNull(EuObservedWorkIdentity.Resolve([facts[0] with { SeedCelex = "32019R0947" }], State));
    }

    private static EuObservedWorkFacts[] Facts(params RepeatedEnumerationRdfTerm[] terms)
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder.Single(seed => seed.Celex == "32016R0679");
        var proof = new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-00000000a801", new string('a', 64));
        return [new(seed.Celex, seed.WorkRoot, State,
            terms.Select(term => new EuWorkFactObservation(State,
                EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalIdCelex), term, proof)).ToArray())];
    }
}

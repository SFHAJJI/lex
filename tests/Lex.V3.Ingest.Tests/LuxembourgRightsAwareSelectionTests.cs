using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.TestSupport;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgRightsAwareSelectionTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string Authority = "http://data.legilux.public.lu/resource/authority/";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Work = "http://data.legilux.public.lu/eli/etat/leg/code/selection";
    private const string Expression = Work + "/fr";
    private const string XmlManifestation = Expression + "/xml";
    private const string PdfManifestation = Expression + "/pdf";
    private const string Filestore = "http://data.legilux.public.lu/filestore/eli/etat/leg/code/selection/fr/";
    private static readonly SourceArtifactRef ObservationRef = new(
        "urn:uuid:ec04474d-b405-4b39-a929-fd853a3b5a8e", new string('a', 64));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AWithheldXmlCannotDisplaceAnAcceptedPdfWhenTheWorkHasBoth(bool inFileChannelObserved)
    {
        var profile = LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            ObservationRef, ObservationRef, VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, []));
        var observation = LuxembourgQueryExecutionAdapter.BuildResourceObservation(Work,
        [
            Iri(Work, RdfType, Jolux + "Act"),
            Iri(Work, Jolux + "typeDocument", Authority + "resource-type/TC"),
            Iri(Work, Jolux + "isRealizedBy", Expression),
            Iri(Expression, RdfType, Jolux + "Expression"),
            Iri(Expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            Iri(Expression, Jolux + "isEmbodiedBy", XmlManifestation),
            Iri(Expression, Jolux + "isEmbodiedBy", PdfManifestation),
            Iri(XmlManifestation, RdfType, Jolux + "Manifestation"),
            Iri(XmlManifestation, Jolux + "userFormat", Authority + "user-format/xml-akomantoso"),
            Iri(XmlManifestation, Jolux + "isExemplifiedBy", Filestore + "xml/document.xml"),
            Iri(XmlManifestation, Jolux + "license", VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl),
            Iri(PdfManifestation, RdfType, Jolux + "Manifestation"),
            Iri(PdfManifestation, Jolux + "userFormat", Authority + "user-format/pdf"),
            Iri(PdfManifestation, Jolux + "isExemplifiedBy", Filestore + "pdf/document.pdf"),
            Iri(PdfManifestation, Jolux + "license", VerifiedLuxembourgSourceProfile.AdmittingLicence),
        ], ObservationRef, profile.ScopeBinding.SourceProfileRef);
        if (inFileChannelObserved)
        {
            // The same candidate exclusion must survive final resolution after both channels
            // agree on the XML's SCL licence. The other manifestation remains independently usable.
            var indexRef = new SourceArtifactRef(
                "urn:uuid:07255946-17e6-4659-86ee-bc7715475862", new string('b', 64));
            observation = new LuxembourgResourceObservation(observation.ObjectRef, ObservationRef,
                observation.Assertions, observation.Relations, observation.SparqlRightsObservations,
                new LuxembourgInFileRightsChannelObservations(ObservationRef, indexRef,
                [
                    new(XmlManifestation, ObservationRef, new SourceArtifactRef(
                        "urn:uuid:80d004d2-70d6-4eb3-a9f3-53dfe3a214c9", new string('c', 64)),
                        [VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl]),
                    new(PdfManifestation, ObservationRef, new SourceArtifactRef(
                        "urn:uuid:3d8e1dc2-6c4f-4caa-a9a8-eab7993a04f9", new string('d', 64)),
                        [VerifiedLuxembourgSourceProfile.AdmittingLicence]),
                ]));
        }

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(profile.Resolve(
            LuxembourgProvenResourceObservations.RequireProven(AbsenceFixtures.Proof(), [observation])));
        var resource = resolved.Resources.Single();
        Assert.AreEqual(LuScopeTerminalState.AcceptedCandidate, resource.Dimensions.Body.State);
        var withheld = resource.BodyJoin.Candidates.Single(candidate =>
            candidate.WemiCandidate.ManifestationIri == XmlManifestation);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl,
            withheld.RightsResolution.Disposition);
        Assert.AreEqual(LuxembourgBodyCandidateDisposition.Withheld, withheld.Disposition);
        Assert.AreEqual(LuxembourgBodyCandidateDisposition.AcceptedCandidate,
            resource.BodyJoin.Candidates.Single(candidate =>
                candidate.WemiCandidate.ManifestationIri == PdfManifestation).Disposition);

        // Exercise the production collection door that receives resolved candidate rights,
        // rather than the lower-level structural WEMI helper that has no rights argument.
        var selected = LuxembourgQueryExecutionAdapter.MintDocumentFetchAddresses(resolved)[resource.ObjectRef];
        Assert.AreEqual(Filestore + "pdf/document.pdf", selected.StoreFileUri.Value.AbsoluteUri,
            "Format preference applies among accepted candidates; a withheld XML cannot borrow its sibling's acceptance.");
        Assert.AreEqual(LuxembourgUserFormatToken.Pdf, selected.UserFormatToken);
    }

    private static LuxembourgObservedAssertion Iri(string subject, string predicate, string value) =>
        new(subject, predicate, LuxembourgAssertionObjectKind.Iri, value, "", "", ObservationRef);
}

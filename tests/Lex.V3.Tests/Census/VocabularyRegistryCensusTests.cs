using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Census;

/// <summary>
/// Every static token registry in the swept assemblies, with the size of each collection it holds
/// and the name of each string token. 58 of them when this was written.
/// </summary>
/// <remarks>
/// <para>
/// A vocabulary does not have to be an enum. A static class holding a frozen set of predicate URIs,
/// a schema-id table or a run of wire tokens is a closed vocabulary with the same failure mode, and
/// until this pin existed most of them had no gate that a new entry would break.
/// </para>
/// <para>
/// A token is any string a reader sees, whatever member carries it. This pin once rendered only
/// <c>const</c> fields, and the hole had a measured shape: a <c>public static readonly string</c>
/// added to a schema-id table passed all of both suites, while the same token declared <c>const</c>
/// failed at a named element. Constants, static readonly strings and static get-only string
/// properties are all rendered now, each with the kind that carries it.
/// </para>
/// <para>
/// Why it is a sweep. The selection is structural: a static class holding at least one static
/// collection member, or two or more string tokens. A registry added tomorrow matches that
/// description without anyone updating a list, so it appears here and fails the pin.
/// </para>
/// <para>
/// What it does not do. It pins each collection's element count, not its elements: pinning the
/// contents would copy a large amount of publisher text into a second place that nobody would think
/// to update, and each registry's own tests already own its contents. So a token added or removed
/// fails this; a token swapped for another of the same kind does not, and the registry's own test
/// is the control for that. Token members are pinned by name, not by value, for the same reason. A
/// member whose static initializer throws is reported as <c>unreadable</c> rather than dropped,
/// because a member that quietly leaves a sweep is the failure this file exists to prevent.
/// </para>
/// <para>
/// When a real change makes this fail, that is the pin working rather than a defect in it, and the
/// fix is not to hand edit the array until it matches. Re-derive it: print
/// <c>ClosedSurfaceCensus.RenderForTranscription</c> over
/// <c>ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere)</c>
/// from a throwaway test, read the diff, and paste the printed block between the braces below.
/// That renderer emits the exact
/// wrapping and escaping used here, so the paste is the whole edit. Never build the expected side
/// from VocabularyRegistries inside this test: it would then agree with whatever the code happens to say, which
/// is the one thing a pin must not do, and it is how a large array quietly stops being evidence.
/// </para>
/// </remarks>
[TestClass]
public sealed class VocabularyRegistryCensusTests
{
    [TestMethod]
    public void EveryStaticTokenRegistryInTheSweptAssembliesIsPinnedAtItsCurrentSize()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "Lex.V3.Api.SyntheticPreviewTrustConfiguration: const EnvironmentBinding, "
                    + "const IssuerId, const KeyId, const PublicKeySha256",
                "Lex.V3.Artifacts.AdmissionHeaderReader: AttestationMembers=4, "
                    + "ContractReferenceMembers=3, ContractSetMembers=4, EnvironmentMembers=2, "
                    + "IssuerMembers=3, PayloadMembers=6, RootMembers=11",
                "Lex.V3.Contracts.ContractValidation: const IdentifierPattern, "
                    + "const SyntheticCelexCoordinate, const SyntheticEliCoordinate, "
                    + "const SyntheticEuHeldRecordIdentifier, "
                    + "const SyntheticHistoricalLegalIdCoordinate, "
                    + "const SyntheticLuHeldRecordIdentifier, const SyntheticMemorialCoordinate, "
                    + "const SyntheticRequestReference",
                "Lex.V3.Contracts.Custody.CustodySchemaIds: const CustodyPolicyEvidence, "
                    + "const DurableBlobRef, const DurableBlobWriteReceipt",
                "Lex.V3.Contracts.EuScopeVocabulary: ActForms=12, BindingStatuses=3, "
                    + "CdmPredicates=13, Channels=3, ConsolidationStatuses=2, ExtractionProfiles=4, "
                    + "Hierarchies=2, OfficialLanguages=24, ReadRelationFamilies=4, "
                    + "RelationFamilies=13",
                "Lex.V3.Contracts.EuSeedResolutionPlan: Batches=2, Seeds=82, "
                    + "const PlainLiteralDriftProbeCelex, const PlainLiteralDriftProbeSparql, "
                    + "const SeedListSha256, static property PositiveControlCelex, "
                    + "static property XsdStringDatatypeIri",
                "Lex.V3.Contracts.Facts.FactsSchemaExporter: CommonDefinitionTypes=4, SchemaFiles=8, "
                    + "SchemaTypes=7, AllSchemaIds=8",
                "Lex.V3.Contracts.Facts.FactsSchemaHardener: ContractSignatures=4, EuOnlyFamilies=4, "
                    + "LuOnlyFamilies=2, ReaderOnlyInvariants=7, const CelexBody, "
                    + "const CelexPattern, const CellarDottedSuffix, const CellarHost, "
                    + "const CellarPathSegment, const CellarPsiPattern, const CellarResourcePattern, "
                    + "const CellarWorkPattern, const EcliPattern, const End, const EuEliPattern, "
                    + "const LeapYear, const LuEliPattern, const PathPrintable, const Printable, "
                    + "const Sha256Pattern, const TimezonePattern, const Uuid, const Year4, "
                    + "const YyyyMmDd",
                "Lex.V3.Contracts.Facts.FactsSchemaIds: const DerivedInverseRelation, "
                    + "const FactsCommon, const LocalInboundView, const PublisherDate, "
                    + "const PublisherDateFact, const PublisherRelation, const RelationFact, "
                    + "const VocabularyDrift",
                "Lex.V3.Contracts.Facts.FactsSchemaResourceIds: const DerivedInverseRelation, "
                    + "const FactsCommon, const LocalInboundView, const PublisherDate, "
                    + "const PublisherDateFact, const PublisherRelation, const RelationFact, "
                    + "const VocabularyDrift",
                "Lex.V3.Contracts.Facts.FactsVocabularies: KindsByType=8, AllKinds=8",
                "Lex.V3.Contracts.PreviewOfficialPublisherLinks: const EuSearch, const LuSearch",
                "Lex.V3.Contracts.PreviewSchemaExporter: SchemaFiles=6, SchemaTypes=6",
                "Lex.V3.Contracts.PreviewSchemaGraph: ContractSetSchemaIds=4, SchemaIds=6",
                "Lex.V3.Contracts.PreviewSchemaHardener: IdentifierPropertyNames=9, "
                    + "const Sha256Pattern, const SignaturePattern",
                "Lex.V3.Contracts.Source.Core.GlobalBlockerRegistry: Families=12, const DigestScope, "
                    + "const SchemaId",
                "Lex.V3.Contracts.Source.Core.ReleaseArtifactKindRegistry: ByWireKey=10",
                "Lex.V3.Contracts.Source.Core.SourceCoreSchemaExporter: CommonDefinitionTypes=3, "
                    + "SchemaFiles=5, SchemaTypes=4, AllSchemaIds=5",
                "Lex.V3.Contracts.Source.Core.SourceCoreSchemaHardener: const DnsLabelPattern, "
                    + "const End, const MachineMemberPattern, "
                    + "const MachineTargetOriginAndPathPattern, const MediaTypePattern, "
                    + "const NonDefaultPortPattern, const PublisherUriPattern, const Sha256Pattern, "
                    + "const UuidUrnPattern",
                "Lex.V3.Contracts.Source.Core.SourceCoreSchemaIds: const Common, "
                    + "const MachineQueryPlan, const MachineQueryRenderReceipt, "
                    + "const SourceObjectRef, const SourceProfileTopology",
                "Lex.V3.Contracts.Source.Core.SourceCoreSchemaResourceIds: const Common, "
                    + "const MachineQueryPlan, const MachineQueryRenderReceipt, "
                    + "const SourceObjectRef, const SourceProfileTopology",
                "Lex.V3.Contracts.Source.Core.SourceObjectRefReaderOnlyInvariants: All=4",
                "Lex.V3.Contracts.Source.Europe.EuAmendmentRelationVocabulary: AssertedPredicates=4, "
                    + "const AmendedByPredicateUri, const AmendsPredicateUri, "
                    + "const AnnotationNamespace, const ConsolidatedBasedOnPredicateUri, "
                    + "const ConsolidatedConsolidatesPredicateUri, const EndOfValidityUri, "
                    + "const LocationAuthorityListUri, const OntologyUri, const OntologyVersion, "
                    + "const ReferenceToModifiedLocationUri, const RepealsPredicateUri, "
                    + "const Role2Uri, const RoleAuthorityListUri, const StartOfValidityUri, "
                    + "const TypeOfLinkTargetUri",
                "Lex.V3.Contracts.Source.Europe.EuAppendixASeedMap: SeedLines=82, PackRoots=82, "
                    + "SeedsInCelexOrder=82, const AppendixASha256",
                "Lex.V3.Contracts.Source.Europe.EuCaseLawPredicateVocabulary: Pinned=2, "
                    + "const CaseLawInterpretesResourceLegalPredicateUri, "
                    + "const WorkCitesWorkPredicateUri",
                "Lex.V3.Contracts.Source.Europe.EuCellarObjectDecode: const "
                    + "ConsolidatedActResourceTypeIri, const EnglishLanguageAuthorityIri, "
                    + "const FrenchLanguageAuthorityIri",
                "Lex.V3.Contracts.Source.Europe.EuConsolidationTerm: const RdfLangStringDatatypeIri, "
                    + "const XsdDateDatatypeIri",
                "Lex.V3.Contracts.Source.Europe.EuDateQualifierVocabulary: PinnedQualifiers=3, "
                    + "const DeadlinePredicateUri, const EndOfValidityPredicateUri, "
                    + "const EntryIntoForceAndApplicationPredicateUri, "
                    + "const SignatureDatePredicateUri",
                "Lex.V3.Contracts.Source.Europe.EuDoNotIndexTerm: const DatatypeIri, const Lexical",
                "Lex.V3.Contracts.Source.Europe.EuLegislationSummaryPredicateVocabulary: Pinned=1, "
                    + "const SummarizesResourceLegalPredicateUri",
                "Lex.V3.Contracts.Source.Europe.EuPackRootCanonicalForm: const HttpScheme, "
                    + "const HttpsScheme",
                "Lex.V3.Contracts.Source.Europe.EuScopeProfile: ProjectionRules=4, SelectorKeys=7, "
                    + "const BodyCandidateRoleKey, const Candidate4Sha256, const ProfileResourceId, "
                    + "static readonly ProfileSha256, const SelectorTableResourceId, "
                    + "static readonly SelectorTableSha256",
                "Lex.V3.Contracts.Source.Http.HttpAcquisitionReasonRegistry: "
                    + "CanonicalArtifactBytes=1112, const CanonicalArtifact, const ResourceId, "
                    + "const Schema, const Sha256",
                "Lex.V3.Contracts.Source.Http.OutboundCrawlerIdentity: static property Schema, "
                    + "static property Token",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionVocabulary: "
                    + "ActForceDatePredicates=2, ConsolidationApplicabilityDatePredicates=2, "
                    + "Predicates=26",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgBodyJoin: CurrentMilestoneBlockers=8",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgItemUriFamily: const "
                    + "CurrentPathPrefix, const Origin, const PreviousPathPrefix",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgLiteralCanonicalizer: "
                    + "ContextDependentDatatypes=3, GrandfatheredLanguageTags=26, "
                    + "SupportedDatatypes=3, SupportedDatatypeIris=3, const RdfLangString, "
                    + "const RdfXmlLiteral, const XsdDate, const XsdNotation, const XsdQName, "
                    + "const XsdString",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryPlanSchemaExporter: const "
                    + "FileName, const ResourceId",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgRelationVocabulary: "
                    + "AcquisitionStates=4, Authorities=2, Predicates=18",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgScopeResolver: AccTypes=1, "
                    + "AdmittedNonShelfTypes=23, MetadataSupportClasses=10, NeverFormats=3, "
                    + "NeverTypes=2, OrdinaryCandidateTypes=17, PointFormats=3, "
                    + "PointSupportClasses=16, PointTypes=2, PriorityCandidateTypes=3, "
                    + "QuarantinedTypes=2, RegulatorTypes=3, StructuredFormats=2, TcRectTypes=2, "
                    + "const IsEmbodiedBy, const IsExemplifiedBy, const IsMemberOf, "
                    + "const IsRealizedBy, const Language, const LegalValue, "
                    + "const PreviousIsExemplifiedBy, const TypeDocument, const UserFormat",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgSourceProfileTopology: const "
                    + "RegistryDomain, const RegistryResourceId, "
                    + "const SinglePublisherStoreMemberKey",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgTypedRoleDisclosures: const "
                    + "ConsolidationWithoutLegalEffect, "
                    + "const ConstitutionalReviewDecisionNeverStatutoryText, "
                    + "const CorrectiveMaterialNeverCorrectedAct",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgWemiTopology: WemiTypes=4, const Act, "
                    + "const Consolidation, const Expression, const IsEmbodiedBy, "
                    + "const IsExemplifiedBy, const IsRealizedBy, const Jolux, const Language, "
                    + "const Manifestation, const PreviousIsExemplifiedBy, const RdfType, "
                    + "const UserFormat",
                "Lex.V3.Contracts.Source.Quarantine.QuarantineCoordinateValidation: "
                    + "ForbiddenLawContentExtensions=4",
                "Lex.V3.Contracts.Source.Scope.ScopeManifestCanonicalWriter: const "
                    + "InputSequenceDomain, const ManifestDomain, const ObjectRefDomain, "
                    + "const ObservedObjectSequenceDomain, const RowDomain, "
                    + "const RuleEvaluationDomain, const SelectorEvidenceDomain, "
                    + "const SelectorSetDomain",
                "Lex.V3.Contracts.Source.Scope.ScopeManifestReaderOnlyInvariants: All=14",
                "Lex.V3.Contracts.Source.Scope.ScopeValidation: AllAxes=4, AllDispositions=4",
                "Lex.V3.Contracts.SyntheticSliceSchemaExporter: SchemaFiles=3, SchemaTypes=3",
                "Lex.V3.Contracts.SyntheticSliceSchemaGraph: OwnedSchemaIds=3, SchemaIds=6",
                "Lex.V3.Contracts.SyntheticSliceSchemaHardener: const RequestReferencePattern, "
                    + "const Sha256Pattern, const SignaturePattern",
                "Lex.V3.Contracts.V3ContractVocabulary: CompositionTypes=7, CoreObjectTypes=11, "
                    + "OperationIds=27",
                "Lex.V3.Contracts.V3SchemaIds: const PreviewArtifact, "
                    + "const PreviewArtifactSignature, const PreviewEnvelope, "
                    + "const PreviewObjectSet, const PreviewOperationCatalog, const PreviewPayload, "
                    + "const PreviewRefusalRegistry, const SyntheticResolveEnvelope, "
                    + "const SyntheticSliceArtifact, const SyntheticSliceControl",
                "Lex.V3.Contracts.V3SchemaResourceIds: const PreviewArtifact, const PreviewEnvelope, "
                    + "const PreviewObjectSet, const PreviewOperationCatalog, const PreviewPayload, "
                    + "const PreviewRefusalRegistry, const SyntheticResolveEnvelope, "
                    + "const SyntheticSliceArtifact, const SyntheticSliceControl",
                "Lex.V3.Custody.Probe.CustodyProbeApplication: "
                    + "AlternateManagedIdentitySourceVariables=5, ForbiddenCredentialVariables=7",
                "Lex.V3.Preview.SyntheticPreviewBuildContract: const CandidateCoordinate, "
                    + "const CandidateEvidenceBasis, const CanonicalSourceText, const Publisher, "
                    + "const UpstreamHealth, static property HeldCoordinate, "
                    + "static property NormalizationProfileDescriptor, "
                    + "static property NormalizationProfileIdentity, "
                    + "static property NormalizationProfileSha256, "
                    + "static property SqliteSchemaIdentity",
                "Lex.V3.Preview.SyntheticPublicGraphBuilder: const BuilderComponentId, "
                    + "const ManifestFileName, const SnapshotId",
                "Lex.V3.Preview.SyntheticSqliteIndex: const Ddl, static property DdlSha256",
            },
            ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere).ToArray());
    }
}

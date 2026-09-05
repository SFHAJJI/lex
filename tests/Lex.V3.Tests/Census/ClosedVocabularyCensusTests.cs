using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Census;

/// <summary>
/// Every closed vocabulary in the swept assemblies, member by member. 211 of them when this was
/// written, and the count is the point: before this pin existed nobody had asked how many there
/// were.
/// </summary>
/// <remarks>
/// <para>
/// The defect this answers. On 2026-09-04 four closed vocabularies were found carrying no pin of
/// any kind, one at a time and each by accident: a member added to any of them would have broken
/// nothing. Four found by accident says nothing about how many there are, so this sweeps the whole
/// assembly rather than the vocabularies somebody thought of. An enum added tomorrow arrives as a
/// new element here and fails this test; a member added to an existing one changes that
/// vocabulary's element and fails it too.
/// </para>
/// <para>
/// Why it is a sweep rather than a list checking itself. <see cref="ClosedSurfaceCensus"/> selects
/// on <c>Type.IsEnum</c> and nothing else, so the selection cannot be narrowed by the answer below.
/// A completeness test that filtered an assembly scan through the names it then compared against
/// was written in this repository on the same day, and it could only ever return names already in
/// the list. Ask what change to the assembly would flip this assertion: adding an enum anywhere in
/// any swept assembly, in any namespace, at any visibility, nested or not.
/// </para>
/// <para>
/// What it does not do. It pins member names in <see cref="Enum.GetNames(Type)"/> order, which is
/// by underlying value, so a renumbering that reorders members fails and a dense renumbering that
/// preserves the order passes. It pins names, not wire tokens: the per-type pins that call
/// <c>AssertTokens</c> or read <c>JsonStringEnumMemberNameAttribute</c> own the tokens, and this
/// does not replace them. It also does not claim a member is reachable, only that it is declared,
/// which is the other half of the same defect and needs a producer scan rather than a member scan.
/// </para>
/// <para>
/// When a real change makes this fail, that is the pin working rather than a defect in it, and the
/// fix is not to hand edit the array until it matches. Re-derive it: print
/// <c>ClosedSurfaceCensus.RenderForTranscription</c> over
/// <c>ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)</c>
/// from a throwaway test, read the diff, and paste the printed block between the braces below.
/// That renderer emits the exact
/// wrapping and escaping used here, so the paste is the whole edit. Never build the expected side
/// from ClosedVocabularies inside this test: it would then agree with whatever the code happens to say, which
/// is the one thing a pin must not do, and it is how a large array quietly stops being evidence.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClosedVocabularyCensusTests
{
    [TestMethod]
    public void EveryClosedVocabularyInTheSweptAssembliesIsPinnedMemberByMember()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "Lex.V3.Api.SyntheticResolutionDisposition: Held, CandidateOnly",
                "Lex.V3.Artifacts.ArtifactAdmissionFailureCode: HeaderTooLarge, MalformedHeader, "
                    + "DuplicateMember, UnknownMember, PreviewSchemaForbidden, "
                    + "SyntheticFlagForbidden, SyntheticEvidenceForbidden, SyntheticSourceForbidden, "
                    + "EnvironmentForbidden, IssuerRoleForbidden, ReleaseSchemaUnsupported, "
                    + "IssuerUntrusted, KeyUntrusted, AlgorithmUnsupported, SignatureInvalid, "
                    + "PayloadSizeMismatch, PayloadDigestMismatch, GraphSchemaUnsupported, "
                    + "GraphIncomplete, ControlTooLarge, ControlSizeMismatch, ControlDigestMismatch, "
                    + "BlobTooLarge, BlobSizeMismatch, BlobDigestMismatch, "
                    + "CandidateReadBudgetExceeded, SchemaReadBudgetExceeded, "
                    + "DerivedContentMismatch",
                "Lex.V3.Artifacts.SyntheticDerivationFailureCode: InvalidUtf8, Utf8BomForbidden, "
                    + "NoVisibleContent",
                "Lex.V3.Contracts.BodyHoldingState: HeldPublic, HeldWithheld, NotHeld",
                "Lex.V3.Contracts.Custody.CustodyClass: NightlyFloor90d, LegalHoldEvidence",
                "Lex.V3.Contracts.Custody.CustodyMembership: ReadOnce, RetainedUnenforced, Floored",
                "Lex.V3.Contracts.Custody.CustodyProtection: NotEnforced, LockedTime, "
                    + "ActiveLegalHold",
                "Lex.V3.Contracts.Custody.CustodyVerificationProfile: FileSystemUnenforced1, "
                    + "ImmutableObject1",
                "Lex.V3.Contracts.EuActForm: Directive, Regulation, DelegatedRegulation, "
                    + "ImplementingRegulation, Treaty, Corrigendum, DelegatedDirective, "
                    + "ImplementingDecision, Decision, DecisionEntscheid, ImplementingDirective, "
                    + "DelegatedDecision",
                "Lex.V3.Contracts.EuBindingStatus: InForce, NotInForce, Unknown",
                "Lex.V3.Contracts.EuCdmPredicate: ResourceLegalIdCelex, ExpressionBelongsToWork, "
                    + "ResourceLegalType, WorkHasResourceType, WorkDateDocument, "
                    + "ActConsolidatedDate, DateCreationLegacy, ResourceLegalInForce, "
                    + "ExpressionUsesLanguage, ExpressionTitle, ExpressionTitleShort, "
                    + "WorkIsAboutConceptEurovoc, ResourceLegalIsAboutConceptDirectoryCode",
                "Lex.V3.Contracts.EuChannel: CellarSparqlEndpoint, PublicationsRestResource, "
                    + "EurLexPortal",
                "Lex.V3.Contracts.EuChannelAdmission: Admitted, Excluded",
                "Lex.V3.Contracts.EuConsolidationStatus: Published, OriginalOfficialExpression",
                "Lex.V3.Contracts.EuExtractionProfile: Formex4, Xhtml, XhtmlXlinkContext, "
                    + "HtmlTolerant",
                "Lex.V3.Contracts.EuHierarchy: PrimaryEuLaw, SecondaryEuLaw",
                "Lex.V3.Contracts.EuLanguageBodyState: BodyCandidate, BodyNotHeldPoint",
                "Lex.V3.Contracts.EuOfficialLanguage: Bulgarian, Czech, Danish, German, Greek, "
                    + "English, Estonian, Finnish, French, Irish, Croatian, Hungarian, Italian, "
                    + "Latvian, Lithuanian, Maltese, Dutch, Polish, Portuguese, Romanian, Slovak, "
                    + "Slovenian, Spanish, Swedish",
                "Lex.V3.Contracts.EuRelationAcquisitionState: Unacquired, Incomplete, Uncertain, "
                    + "Complete",
                "Lex.V3.Contracts.EuRelationAuthority: PublisherAsserted, OntologyAuthorizedInverse, "
                    + "LocalInboundView",
                "Lex.V3.Contracts.EuRelationFamily: Amends, AmendedBy, Corrects, BasedOn, Repeals, "
                    + "ImplicitlyRepeals, ProposesToAmend, ConsolidatedBasedOn, "
                    + "ConsolidatedConsolidates, CaseLawInterpretes, CaseLawDeclaresVoid, "
                    + "SubmitsPreliminaryQuestion, RequestsAnnulment",
                "Lex.V3.Contracts.Facts.CelexProfile: BaseAct, ConsolidatedAct, Corrigendum, "
                    + "TreatyPart, NationalImplementingMeasure",
                "Lex.V3.Contracts.Facts.DateOpenSentinel: NotOpen, OpenEnded",
                "Lex.V3.Contracts.Facts.DatePrecision: Year, YearMonth, YearMonthDay",
                "Lex.V3.Contracts.Facts.DateSemanticRole: DocumentDate, PublicationDate, "
                    + "SignatureDate, EntryIntoForce, ApplicationDate, EndOfValidity, "
                    + "PublisherDeadline, TranspositionDeadline, NotificationDate, "
                    + "RoleNotStatedByPublisher",
                "Lex.V3.Contracts.Facts.EcliState: EcliPresent, EcliNotInThisSet, EcliNotApplicable",
                "Lex.V3.Contracts.Facts.FactsIdentifierFamily: Eli, Celex, Ecli, CellarWorkUri, "
                    + "CellarResourceUri, CellarPsiUri, Memorial, HistoricalLegalId",
                "Lex.V3.Contracts.Facts.RelationAssertionKind: PublisherAsserted, "
                    + "OntologyAuthorizedInverse, LocalInboundView",
                "Lex.V3.Contracts.Facts.TargetBodyScope: BodyInScopeHeld, BodyInScopeNotHeld, "
                    + "BodyOutsideScope",
                "Lex.V3.Contracts.Facts.TranspositionEvidence: None, DirectiveQualifier, NimRecord",
                "Lex.V3.Contracts.Facts.VocabularyKind: RelationAssertionKind, IdentifierFamily, "
                    + "EcliState, TargetBodyScope, DateSemanticRole, TranspositionEvidence, "
                    + "DatePrecision, DateOpenSentinel",
                "Lex.V3.Contracts.IdentifierFamily: Eli, Celex, Memorial, HistoricalLegalId",
                "Lex.V3.Contracts.LuScopeTerminalState: AcceptedMetadata, AcceptedCandidate, Point, "
                    + "NeverIngest, TypedQuarantine, MissingPublisherValue, NotApplicable",
                "Lex.V3.Contracts.PreviewBodyDispositionReason: SyntheticFixture, "
                    + "SyntheticFixtureWithheld, UnknownPendingEvidence",
                "Lex.V3.Contracts.PreviewCapabilityState: MechanicsOnly",
                "Lex.V3.Contracts.PreviewProvisionality: All",
                "Lex.V3.Contracts.PreviewSourceKind: SyntheticTest",
                "Lex.V3.Contracts.PreviewUpstreamHealth: NotApplicableSynthetic",
                "Lex.V3.Contracts.PublisherId: LuLegilux, EuEurLex",
                "Lex.V3.Contracts.RefusalCode: IdentifierUnknown",
                "Lex.V3.Contracts.RetrievalOutcome: MetadataOnly",
                "Lex.V3.Contracts.Source.Absence.AbsenceAppendDisposition: StreakAdvanced, "
                    + "PresenceBreakRecorded, PartialRunNoEffect, SeparationFloorNotMet, "
                    + "ClockSourceChanged, FrozenPendingReplacementReview",
                "Lex.V3.Contracts.Source.Absence.AbsenceApplicableSet: ObservedRootSet, "
                    + "NormalizedFamilySet",
                "Lex.V3.Contracts.Source.Absence.AbsenceComparisonPolicyMember: "
                    + "RootDefinitionDigest, ApplicableScopePolicyDigest, DiscoveryQueryDigest, "
                    + "SelectionQueryDigest, AdapterDigest, RequestPolicyDigest, "
                    + "ExecutionPolicyDigest, RobotsPolicyProfileDigest, "
                    + "ReplacementCoordinateProfileDigest",
                "Lex.V3.Contracts.Source.Absence.AbsenceComparisonPolicyRefusal: None, "
                    + "DuplicateMember, MemberUndecided, DigestNotSha256, MemberUndefined",
                "Lex.V3.Contracts.Source.Absence.AbsenceCoordinateFieldKind: StablePublisherField, "
                    + "FamilyRule, PublisherDate",
                "Lex.V3.Contracts.Source.Absence.AbsenceCutRefusal: None, RunIdInvalid, "
                    + "ApplicableSetUndefined, ObservationsEmpty, DuplicateObservationId, "
                    + "DuplicateFamilyKey, ObservedKeyInvalid, DuplicateObservedKey, "
                    + "DuplicateEnumerationProofFamily, EnumerationProofFamilyNotObserved, "
                    + "FamilyEnumerationProofMissing, EnumerationProofsSpanMoreThanOneRun, "
                    + "EnumerationProofNotFloored",
                "Lex.V3.Contracts.Source.Absence.AbsenceFamilyEnumerationProofRefusal: None, "
                    + "FamilyKeyInvalid, PartitionIsNotThisFamily, "
                    + "PassesDeliveredDifferentSelections, SelectionReachedTheRowCap, "
                    + "RetainedFloorIsNotReceiptDerived",
                "Lex.V3.Contracts.Source.Absence.AbsenceFamilyObservationRefusal: None, "
                    + "ObservationIdInvalid, FamilyKeyInvalid, TimestampNotUtc, PrecisionUndefined, "
                    + "TimestampFinerThanDeclaredPrecision, ClockSourceInvalid, ProvenanceUndefined, "
                    + "SkewNegative, UncertaintyIntervalNotRepresentable, "
                    + "ProvenanceNotFreshlyExecuted",
                "Lex.V3.Contracts.Source.Absence.AbsenceGenerationOpeningEventKind: TrackingStarted, "
                    + "ComparisonPolicyTransition, TrustworthyPositiveObservation",
                "Lex.V3.Contracts.Source.Absence.AbsenceHistoryGenerationCause: InitialTracking, "
                    + "ComparisonPolicyChanged, PresenceBreak",
                "Lex.V3.Contracts.Source.Absence.AbsenceHistoryGenerationIdRefusal: None, "
                    + "OrdinalNotPositive, OpeningEventIdInvalid",
                "Lex.V3.Contracts.Source.Absence.AbsenceLedgerRefusal: None, ApplicableSetUndefined, "
                    + "EventIdInvalid, EventIdReused, ComparisonPolicyUnchanged, RunIdReused, "
                    + "ObservationIdReused, CutAxisNotApplicable, ClassificationOutsideThisSubject",
                "Lex.V3.Contracts.Source.Absence.AbsenceObservationProvenance: FreshlyExecuted, "
                    + "WrapperAroundEarlierObservation, CacheReplay, StaleRow, IncompleteRow",
                "Lex.V3.Contracts.Source.Absence.AbsenceReplacementClassificationRefusal: None, "
                    + "CutIdInvalid, CutIdsIdentical, ClassMemberInvalid, DuplicateClassMember",
                "Lex.V3.Contracts.Source.Absence.AbsenceReplacementCoordinateProfileRefusal: None, "
                    + "ProfileDigestNotSha256, FieldsEmpty, FieldNameInvalid, DuplicateFieldName, "
                    + "FieldKindUndefined, CoordinateIsDateAlone",
                "Lex.V3.Contracts.Source.Absence.AbsenceReplacementDisposition: CoordinateUnchanged, "
                    + "OrdinaryCoordinateDisappearance, OrdinaryCoordinateAddition, "
                    + "ReplacementCandidateOneToOne, ReplacementCollisionFullSet",
                "Lex.V3.Contracts.Source.Absence.AbsenceReplacementEffect: OutsideThisCoordinate, "
                    + "MayProceedToAbsence, NoAbsenceEvent, FrozenPendingReview",
                "Lex.V3.Contracts.Source.Absence.AbsenceRunCompletion: EnumerationComplete, Partial",
                "Lex.V3.Contracts.Source.Absence.AbsenceState: NoEvidenceUnderCurrentGeneration, "
                    + "Present, AbsentUnconfirmed, AbsentConfirmed, FrozenPendingReplacementReview",
                "Lex.V3.Contracts.Source.Absence.AbsenceSubjectRefusal: None, PublisherUndefined, "
                    + "CanonicalPublisherUriInvalid, ParentRegistryMismatch, ParentIsSelf",
                "Lex.V3.Contracts.Source.Absence.AbsenceTimestampPrecision: Hour, Minute, Second, "
                    + "Millisecond, Microsecond",
                "Lex.V3.Contracts.Source.Core.CutReleaseBlockReason: None, "
                    + "EnumerationCompletionFalseOrMissing, AcquisitionCompletionFalseOrMissing, "
                    + "RegistryDigestMismatch, MissingFamily, DuplicateFamily, EvaluationError, "
                    + "CountLedgerMismatch, NonzeroBlockerCount, UnknownArtifactKindOrReleaseClass",
                "Lex.V3.Contracts.Source.Core.CutReleaseVerdict: CutReleaseEligible, "
                    + "CutReleaseBlocked",
                "Lex.V3.Contracts.Source.Core.EnumerationDeliveryOutcome: EqualSelections, "
                    + "DifferentSelections",
                "Lex.V3.Contracts.Source.Core.GlobalBlockerFamily: ManifestSelectorConflict, "
                    + "ManifestBoundaryDrift, RootDefinitionConflict, DuplicateClosure, "
                    + "MissingClosure, ClosureReconciliationConflict, WitnessReconciliationConflict, "
                    + "PagingPartitionOrTruncationConflict, RobotsPolicyConflict, "
                    + "PositiveFeedReconciliationConflict, ImplementationError, "
                    + "UnclassifiedGlobalBlocker",
                "Lex.V3.Contracts.Source.Core.HttpRequestMethod: Get, Post",
                "Lex.V3.Contracts.Source.Core.MachineQueryCharset: Utf8",
                "Lex.V3.Contracts.Source.Core.MachineQueryInputMode: RendererInputs",
                "Lex.V3.Contracts.Source.Core.MachineQueryParameterKind: BoundedInteger, "
                    + "PublisherCursor, PublisherLiteral",
                "Lex.V3.Contracts.Source.Core.MachineResponseCardinalityKind: OpaqueBody, "
                    + "BoundedRowSetPage",
                "Lex.V3.Contracts.Source.Core.ReleaseArtifactKind: EnumerationEvidence, "
                    + "PublicCorpus, Index, Body, Metadata, Relation, Gap, Absence, Withdrawal, "
                    + "CapabilityRelease",
                "Lex.V3.Contracts.Source.Core.ReleaseClass: EnumerationEvidenceOnly, "
                    + "AcquisitionOrProduct",
                "Lex.V3.Contracts.Source.Core.RepeatedEnumerationRdfTermKind: Iri, BlankNode, "
                    + "Literal, Unbound",
                "Lex.V3.Contracts.Source.Core.RepeatedEnumerationReceiptRefusal: None, "
                    + "SendClosureMemberNotHeld, MembershipDisagreesOnADigest, "
                    + "DeliveryComparisonRefused, MembershipIsNotReceiptDerived",
                "Lex.V3.Contracts.Source.Core.RepeatedEnumerationRowsOpenRefusal: None, "
                    + "PageChainInvalid, DeliveredRowCountMismatch, CanonicalKeyDigestMismatch, "
                    + "CursorDigestMismatch, CanonicalRowDigestMismatch",
                "Lex.V3.Contracts.Source.Core.RepeatedEnumerationSparqlJsonDialect: "
                    + "LuxembourgVirtuoso, EuropeanUnionVirtuoso",
                "Lex.V3.Contracts.Source.Core.RepeatedEnumerationTerminalPagePolicy: "
                    + "ShortPageTerminal, EmptySuccessorAfterShortPage",
                "Lex.V3.Contracts.Source.Core.RepeatedEnumerationThresholdAssessment: BelowMaximum, "
                    + "PartitionRequired",
                "Lex.V3.Contracts.Source.Core.RobotsPolicyEvaluationResult: Allowed, Denied, "
                    + "UnsafeToInterpret",
                "Lex.V3.Contracts.Source.Core.SourceAuthority: Jolux, Cellar",
                "Lex.V3.Contracts.Source.Core.SourceObjectRefReaderOnlyInvariant: "
                    + "CanonicalKeySha256ExactBytes, CanonicalKeyUtf8Maximum4096Bytes, "
                    + "ParentRegistryMatchesChild, ParentIsNotSelf",
                "Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason: BodyDeadline, "
                    + "BodyReadFailure, ByteBoundPreventedCompletion, CallerCancelledAfterHeaders, "
                    + "DeclaredLengthShortRead, MissingCompletionProof, TransferCodingConflict, "
                    + "InvalidContentLength, UnsupportedTransferCoding, HeaderDeadline, "
                    + "TransportBeforeHeaders, RevalidationRequestNotAdmitted, "
                    + "StatusContentForbidden, StatusFramingConflict, "
                    + "RequestedRepresentationNotServed, WrongAcceptToken, "
                    + "RedirectTargetOriginNotAdmitted, RobotsDisallowed, NotFound, Gone, "
                    + "RetryExhausted, UnexpectedPublisherStatus",
                "Lex.V3.Contracts.Source.Corpus.CorpusBodyPendingAcquisitionReasonKind: "
                    + "NotYetAcquired, AcquisitionRefused",
                "Lex.V3.Contracts.Source.Corpus.CorpusBodyRecordKind: Held, NotHeld, "
                    + "PendingAcquisition",
                "Lex.V3.Contracts.Source.Europe.EuAppendixASeedMapRefusal: None, "
                    + "SeedCountNotEightyTwo, CelexNotStrictlyAscending, CelexRepeated, "
                    + "WorkRootRepeated, WorkRootNotCanonical, CanonicalBytesDigestMismatch",
                "Lex.V3.Contracts.Source.Europe.EuCaseLawGranularity: ActLevel",
                "Lex.V3.Contracts.Source.Europe.EuCaseLawLinkCaseSide: Source, Target",
                "Lex.V3.Contracts.Source.Europe.EuCellarObjectDecodeRefusal: None, "
                    + "FamilyRowTermKindMismatch, DuplicateSingleValuedBinding, "
                    + "ObjectSnapshotRejected, ObjectFactRowTermKindMismatch, "
                    + "ObjectFactRowNotInClosure, ExpressionFactRowTermKindMismatch, "
                    + "ExpressionParentNotInClosure, ExpressionSubjectNotSelfClosed, "
                    + "ConsolidatedBasedOnEdgeDisagreesWithFamily, "
                    + "ContentClassClosurePositionMismatch, ManifestationListingRefused",
                "Lex.V3.Contracts.Source.Europe.EuCellarObjectSnapshotRefusal: None, "
                    + "WorkRootNotCanonical, WorkRootOutsideAppendixAPack, "
                    + "PredicateObservationMissing, PredicateObservationRepeated, "
                    + "RelationFamilyObservationMissing, RelationFamilyObservationRepeated",
                "Lex.V3.Contracts.Source.Europe.EuConsolidationDateStatus: OneObservedCandidate, "
                    + "AmbiguousVersion",
                "Lex.V3.Contracts.Source.Europe.EuConsolidationQueryPass: Pass1, Pass2",
                "Lex.V3.Contracts.Source.Europe.EuConsolidationQuerySet: Family, TemporalFacts",
                "Lex.V3.Contracts.Source.Europe.EuConsolidationTemporalPredicate: Date, Layer, "
                    + "Version, Number",
                "Lex.V3.Contracts.Source.Europe.EuConstituentClosureRefusal: None, UnresolvedMember, "
                    + "CyclicChain, CrossRootMember, UnexplainedMismatch",
                "Lex.V3.Contracts.Source.Europe.EuConstituentMemberResolution: Resolved, Unresolved",
                "Lex.V3.Contracts.Source.Europe.EuContentClass: Metadata, Consolidation, Summary, "
                    + "OriginalLegalText, EditorialContent",
                "Lex.V3.Contracts.Source.Europe.EuDoNotIndexClassification: Absent, ExactMarker, "
                    + "ScopeDrift",
                "Lex.V3.Contracts.Source.Europe.EuDocumentFetchAddressRefusal: None, "
                    + "PsNameShapeInvalid, PsIdShapeInvalid",
                "Lex.V3.Contracts.Source.Europe.EuDocumentFetchRefusal: WrongAcceptToken, "
                    + "RequestedRepresentationNotServed",
                "Lex.V3.Contracts.Source.Europe.EuDocumentLanguage: Eng, Fra",
                "Lex.V3.Contracts.Source.Europe.EuExcludedSelector: NonLuxNationalImplementing, "
                    + "Sector3OutsideReviewedClosure, UnreviewedSectorOrTreatyVersion, "
                    + "DossierContainedSector5Body, WholesaleSector2, WholesaleSector5, "
                    + "EuJudgmentText, CellarDoNotIndex, SyntheticConsolidation, Akn4EuLegalBody, "
                    + "EurLexPortalFallback, InboundTreatyBasedOnExpansion",
                "Lex.V3.Contracts.Source.Europe.EuExpressionObservationState: NotObserved, "
                    + "ExpressionObservedBodyCandidate, ExpressionObservedBodyNotHeld",
                "Lex.V3.Contracts.Source.Europe.EuFeedEntrySetRefusal: None, CanonicalEntryRepeated, "
                    + "TraversalStepsDoNotShareOnePlan",
                "Lex.V3.Contracts.Source.Europe.EuFeedIntersectionRefusal: None, PackRootSetEmpty, "
                    + "PackRootBlank, PackRootRepeated, DiscoveredFamilyRowOutsideThePack, "
                    + "PackRootNotCanonical",
                "Lex.V3.Contracts.Source.Europe.EuFeedObservationRefusal: None, "
                    + "ResolvedWorkRootBlank, ResolvedWorkRootRepeated, "
                    + "UnresolvedObservationCarriesResolutionOutput, ProjectionKeyBlank, "
                    + "ResolvedWorkRootNotCanonical",
                "Lex.V3.Contracts.Source.Europe.EuFeedOutOfPackReason: None, "
                    + "NotAMemberOfTheDiscoveredPackRootSet",
                "Lex.V3.Contracts.Source.Europe.EuFeedReconciliationConflict: "
                    + "UnresolvedOrAmbiguousTerminal, ProjectionMissingFromItsDiscoveredFamily, "
                    + "DuplicateTerminalAccounting, EntryWithoutATerminal, "
                    + "TerminalOutsideTheCanonicalEntrySet",
                "Lex.V3.Contracts.Source.Europe.EuFeedTerminal: InPack, OutOfPack, MixedScope, "
                    + "UnresolvedOrAmbiguous",
                "Lex.V3.Contracts.Source.Europe.EuFeedUnresolvedCause: None, "
                    + "IdentityResolutionDidNotClose, WatermarkMembershipDidNotClose, "
                    + "FamilyProjectionDidNotClose, PartitionDidNotClose",
                "Lex.V3.Contracts.Source.Europe.EuFirstCutWatermarkBootstrapRefusal: None, "
                    + "NoCensusObservations, InvalidCensusEntry, DuplicateCensusEntry",
                "Lex.V3.Contracts.Source.Europe.EuFormatBodyAdmission: BodyAdmitted, "
                    + "BodyNotAdmitted",
                "Lex.V3.Contracts.Source.Europe.EuFormexItemRole: MainText, Descriptor",
                "Lex.V3.Contracts.Source.Europe.EuFormexPackageRefusal: None, "
                    + "ExpressionDisagreement, LanguageDisagreement, ManifestationDisagreement",
                "Lex.V3.Contracts.Source.Europe.EuFormexRoleRefusal: None, UnrecognisedStreamName, "
                    + "OriginalActNaming, LanguageDisagreement, WorkDisagreement, DuplicateRole, "
                    + "MainTextAbsent, WorkIdentityDisagreement, StemDisagreement, "
                    + "ExpectedWorkNotABaseAct",
                "Lex.V3.Contracts.Source.Europe.EuJudgmentBodyDisposition: "
                    + "LinkOnlyNeverHeldOrFetched",
                "Lex.V3.Contracts.Source.Europe.EuManifestationFormat: Formex4, Xhtml, Xhtml5, Html, "
                    + "Pdf, PdfA1a, PdfA1b, PdfA2a, Print, NoneAdmitted",
                "Lex.V3.Contracts.Source.Europe.EuManifestationListingRefusal: None, "
                    + "ListingRowTermKindMismatch, ListingParentNotInClosure, "
                    + "ListingContradictsItsOwnAbsenceRow",
                "Lex.V3.Contracts.Source.Europe.EuManifestationMediaType: XhtmlXml, ZipMtypeFmx4, "
                    + "PdfTypePdfa2a, RdfXml, RdfXmlNoticeTree, XmlNoticeBranch, XmlNoticeObject, "
                    + "XmlNoticeIdentifier, TextHtml, ApplicationPdf",
                "Lex.V3.Contracts.Source.Europe.EuObjectFactsQueryPass: Pass1, Pass2",
                "Lex.V3.Contracts.Source.Europe.EuObjectFactsQuerySet: ObjectFacts, ExpressionFacts, "
                    + "RootWatermark, ManifestationFacts",
                "Lex.V3.Contracts.Source.Europe.EuPacingBasis: ChosenAbsentPublishedGuidance, "
                    + "PublishedCrawlDelay",
                "Lex.V3.Contracts.Source.Europe.EuPackRootCanonicalFormRefusal: None, "
                    + "RootUriUnparseable, RootSchemeNotHttpOrHttps, RootUriHasQuery, "
                    + "RootUriHasFragment, RootUriHasDoubleSlash",
                "Lex.V3.Contracts.Source.Europe.EuPredicateObservationState: NotObserved, "
                    + "ObservedPresent, ObservedAbsent",
                "Lex.V3.Contracts.Source.Europe.EuPrimaryEnumerationRefusal: None, "
                    + "ResolvedRootBlank, ResolvedRootNotCanonical, ResolvedRootRepeated, "
                    + "ResolvedRootOutsideAppendixAPack",
                "Lex.V3.Contracts.Source.Europe.EuPrimaryWitnessReconciliationRefusal: None, "
                    + "ClosureIdentityNotStructurallyIndependentFromWitness, "
                    + "WitnessInPackRootMissingFromPrimaryEnumeration",
                "Lex.V3.Contracts.Source.Europe.EuReuseBasis: Cc0, CcBy40, "
                    + "EurLexLegalNoticePermission",
                "Lex.V3.Contracts.Source.Europe.EuRightsExceptionChannel: ThirdPartyMaterial, "
                    + "DocumentSpecificTerms, IndustrialPropertyRights, "
                    + "IdentifiablePrivateIndividuals",
                "Lex.V3.Contracts.Source.Europe.EuRightsMatrixRefusal: None, DuplicateContentClass, "
                    + "ContentClassUndecided, DuplicateExceptionChannel, ExceptionChannelUndecided",
                "Lex.V3.Contracts.Source.Europe.EuScopeManifestBindingProofRefusal: None, "
                    + "ProfileResourceIdentityMismatch, SelectorTableIdentityMismatch",
                "Lex.V3.Contracts.Source.Europe.EuSelectionPolicy: Point, NeverIngest, "
                    + "NeverIngestBody, NeverExpand",
                "Lex.V3.Contracts.Source.Europe.EuSelectionRowSetRefusal: None, DuplicateSelector, "
                    + "SelectorUndecided",
                "Lex.V3.Contracts.Source.Europe.EuTranspositionDeadlineOutcome: NotADeadline, "
                    + "TranspositionDeadlineEvidenceInsufficient, AcceptedTranspositionDeadline",
                "Lex.V3.Contracts.Source.Europe.EuValidityDateShape: HyphenatedIso8601, "
                    + "SlashSeparated",
                "Lex.V3.Contracts.Source.Europe.EuWatermarkLexicalShape: OutsideTheMeasuredSet, "
                    + "FractionalSecondsSignedOffset, WholeSecondsSignedOffset",
                "Lex.V3.Contracts.Source.Europe.EuWatermarkPlanRefusal: None, "
                    + "EndpointNotTheOfficialCellarEndpoint, PredicateNotTheWatermarkPredicate, "
                    + "PageLimitBelowMinimum, PageLimitAboveSortedResultWindow, "
                    + "StartPositionShapeWithoutFrozenOrderSemantics, "
                    + "PositionShapeWithoutFrozenOrderSemantics",
                "Lex.V3.Contracts.Source.Europe.EuWatermarkRefusal: None, DateOnlyCursor, "
                    + "WatermarkAbsent, BoundaryEntrySkipped, BoundaryEntryDuplicated, "
                    + "PageNotOrderedAfterCursor",
                "Lex.V3.Contracts.Source.Europe.EuWatermarkStepRefusal: None, "
                    + "CrossingCursorNotInRetainedTieSet, PageExceedsPlanLimit, "
                    + "WatermarkShapeWithoutFrozenOrderSemantics, PageNotStrictlyAscending, "
                    + "PageBelowBoundaryWatermark, CrossingDoesNotDescribeThisPage, "
                    + "TraversalCannotAdvance",
                "Lex.V3.Contracts.Source.Europe.EuWemiRole: Work, Expression, Manifestation, Item",
                "Lex.V3.Contracts.Source.Europe.EuWorkKind: Directive, Regulation",
                "Lex.V3.Contracts.Source.Http.HeldAcquisitionPublisher: LuLegilux, EuEurLex",
                "Lex.V3.Contracts.Source.Http.HttpCompletionUnprovenReason: MissingCompletionProof, "
                    + "TransferCodingConflict, InvalidContentLength, UnsupportedTransferCoding",
                "Lex.V3.Contracts.Source.Http.HttpPartialBodyReason: BodyDeadline, BodyReadFailure, "
                    + "ByteBoundPreventedCompletion, CallerCancelledAfterHeaders, "
                    + "DeclaredLengthShortRead",
                "Lex.V3.Contracts.Source.Http.HttpPreHeaderFailureClass: HeaderDeadline, "
                    + "TransportBeforeHeaders",
                "Lex.V3.Contracts.Source.Http.HttpResponseSemanticsReason: "
                    + "RevalidationRequestNotAdmitted, StatusContentForbidden, "
                    + "StatusFramingConflict",
                "Lex.V3.Contracts.Source.Http.HttpRouteIncompleteReason: HopIncomplete, "
                    + "SourceProfileStale, RedirectRefused, RedirectLoop, RedirectLimitExceeded, "
                    + "RedirectTargetUnobserved, RobotsPolicyUnavailable, PublisherServerFailure, "
                    + "RedirectTargetOriginNotAdmitted",
                "Lex.V3.Contracts.Source.Http.HttpStatusDisposition: DerivableStatus, "
                    + "RedirectObserved, RevalidationReferenceOnly, SemanticNoEntityStatus, "
                    + "RangeNotApproved, NonDerivableStatus, NegotiationChoiceOffered",
                "Lex.V3.Contracts.Source.Http.LuxembourgDocumentGetOutcomeKind: Retrieved, NotFound, "
                    + "Gone, RobotsDisallowed, RetryExhausted, UnexpectedPublisherStatus",
                "Lex.V3.Contracts.Source.Http.LuxembourgFileUriRefusalReason: NotAbsoluteUri, "
                    + "UnsupportedScheme, UnexpectedHost, NonDefaultPort, UserInfoPresent, "
                    + "QueryPresent, FragmentPresent, PathNotUnderFilestore",
                "Lex.V3.Contracts.Source.Http.LuxembourgManifestationFormat: Xml, PdfA",
                "Lex.V3.Contracts.Source.Http.LuxembourgManifestationSelectionOutcome: Selected, "
                    + "NoManifestationAvailable",
                "Lex.V3.Contracts.Source.Http.OfficialHttpAcquisitionOutcomeKind: "
                    + "ExecutedObservation, PublisherDenial, LocalSafetyRefusal, OperationalFailure, "
                    + "IntegrityFailure",
                "Lex.V3.Contracts.Source.Http.OfficialHttpOperationalFailureReason: NetworkFailure, "
                    + "PublisherServerFailure, RobotsPolicyExpired, SourceProfileStale, "
                    + "CustodyUnavailable",
                "Lex.V3.Contracts.Source.Http.OfficialHttpPacingScope: ProcessActualNetworkOrigin",
                "Lex.V3.Contracts.Source.Http.OfficialMachineQueryRetryCondition: RequestTimeout, "
                    + "TransportFailure, Http408, Http429, Http500, Http502, Http503, Http504",
                "Lex.V3.Contracts.Source.Http.OfficialMachineQuerySourceProfileId: LuxembourgSparql, "
                    + "EuropeanUnionSparql, EuropeanUnionDocumentFetch, LuxembourgDocumentFetch",
                "Lex.V3.Contracts.Source.Http.RepresentationChainAppendDisposition: "
                    + "BaselineEstablished, BaselineConfirmedUnchanged, ReplacementRecorded, "
                    + "AppendedAsEvidenceOnly",
                "Lex.V3.Contracts.Source.Http.RepresentationChainAppendRefusal: None, "
                    + "ObservationIdReused, EffectiveUriMismatch, RequestedUriMismatch",
                "Lex.V3.Contracts.Source.Http.RobotsPolicyFreshness: Current, Expired",
                "Lex.V3.Contracts.Source.Http.RobotsRevalidationMode: FullGetWithoutValidators",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgActForceDatePredicate: "
                    + "DateEntryInForce, DateNoLongerInForce",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionDisposition: Accepted, "
                    + "TypedQuarantine",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionFactKind: ActForce, "
                    + "ConsolidationApplicability, DescriptiveDate, ActIdentity, ResourceType, "
                    + "WemiStructural, ExpressionLanguageOrTitle, ManifestationFormat, "
                    + "LegalValueAssertion, RightsAndProvenance",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionObjectKind: Iri, Literal",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgAssertionPredicate: RdfType, "
                    + "DateApplicability, DateDocument, DateEndApplicability, DateEntryInForce, "
                    + "DateNoLongerInForce, HistoricalLegalId, InForceStatus, IsEmbodiedBy, "
                    + "IsExemplifiedBy, IsMemberOf, IsPartOf, IsRealizedBy, Language, LegalValue, "
                    + "License, PreviousIsExemplifiedBy, PublicationDate, Publisher, "
                    + "ResponsibilityOf, Rights, RightsHolder, Title, TitleShort, TypeDocument, "
                    + "UserFormat",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgBodyBlockerCode: "
                    + "AssertionEnumerationUnproven, RightsChannelEnumerationUnproven, "
                    + "TextPublicNotCleared, LicenceContractResultMissing, RobotsEvidenceUnbound, "
                    + "HttpEvidenceUnbound, DerivationUnverified, IntegrityUnverified, "
                    + "WemiTupleTypedQuarantine, RightsChannelsNotAgreed, WemiRootMismatch, "
                    + "WemiObservationRunMismatch",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgBodyCandidateDisposition: Withheld, "
                    + "AcceptedCandidate",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgBodyRootBlockerCode: "
                    + "PublisherRealizationPathUnproven",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgConsolidatesDirection: "
                    + "AssertedSubjectToObject",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgConsolidatesShapeState: "
                    + "AcceptedTcToCompatibleAct, TypedQuarantineSubjectClassMissing, "
                    + "TypedQuarantineSubjectClassIncompatible, TypedQuarantineSubjectTypeMissing, "
                    + "TypedQuarantineSubjectTypeMultiple, TypedQuarantineSubjectTypeNotTc, "
                    + "TypedQuarantineTargetResourceMissing, TypedQuarantineTargetClassMissing, "
                    + "TypedQuarantineTargetClassIncompatible, TypedQuarantineTargetTypeMissing, "
                    + "TypedQuarantineTargetTypeMultiple, TypedQuarantineTargetRoleIncompatible, "
                    + "TypedQuarantineTargetTypeUnruled",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgConsolidationApplicabilityDatePredica"
                    + "te: DateApplicability, DateEndApplicability",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgDatasetGraphKind: DefaultGraph",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgDimension: Record, Body, Relation, "
                    + "SupportingDocument, PublicationFamily, Language, Format, Authenticity, "
                    + "Rights, Transport",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgLiteralDisposition: Accepted, "
                    + "TypedQuarantine",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgLiteralReason: "
                    + "AcceptedXsdStringIdentity, AcceptedRdfLangStringIdentity, "
                    + "AcceptedXsdDateCanonical, TypedQuarantineUnsupportedDatatype, "
                    + "TypedQuarantineContextDependentDatatype, TypedQuarantineIllTyped",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgPartitionCoverBasis: "
                    + "RootCountVerified, LeafTilingOnly",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgPartitionCoverRefusal: None, "
                    + "LeafReceiptMissing, LeafPartitionKeyMismatch, LeafSelectionsDiffer, "
                    + "LeafPartitionRequired, LeafRunIdentityDiffers, LeafProfileDiffers, "
                    + "RootCountDoesNotEqualTheLeafSum",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgPreviousItemDisposition: "
                    + "PointReplacedFile, TypedQuarantineManifestationUnproven, "
                    + "TypedQuarantineUnruledUriFamily",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgProfileResolutionFailureCode: "
                    + "InvalidPublisherIri, IncompleteVocabulary, UnknownVocabularyDrift, "
                    + "SelectorConflict, EvidenceBindingRejected",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryKeyKind: AbsoluteIriUtf8, "
                    + "CompositeLiteralUtf8",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryPass: Pass1, Pass2",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryRequestKind: Page, Count",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQuerySetAcquisition: PublisherQuery, "
                    + "LocalMaterialization",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgRelationAcquisitionState: Unacquired, "
                    + "Incomplete, Uncertain, Complete",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgRelationAuthority: PublisherAsserted, "
                    + "LocalInboundView",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgRelationDisposition: Accepted, "
                    + "TypedQuarantine",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgRelationPredicate: Modifies, Repeals, "
                    + "Rectifies, BasedOn, Transposes, ModifiedTempBy, HasIndirectImpact, "
                    + "LegalAnalysisHasLegalResourceImpact, ImpactFromLegalResource, "
                    + "ImpactToLegalResource, ImpactToExpression, "
                    + "LegalResourceImpactHasDateEntryInForce, LegalResourceImpactHasType, "
                    + "ImpactConsolidatedBy, ImpactConsolidatedByExpression, BasicAct, Consolidates, "
                    + "Cites",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgRelationSemantic: AssertedRelation, "
                    + "AssertedCitation, ConsolidatesShapeRequired",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelDisposition: "
                    + "ChannelEnumerationUnproven, MissingValue, Stale, EvidenceNotIndependent, "
                    + "Multiple, Conflict, AgreedSameRunCcBy, NonAdmittingLicenceScl, "
                    + "TypedQuarantineUnruledLicence",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgSelectorCardinality: Missing, Single, "
                    + "Multiple",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgTypedRoleKind: NotApplicable, "
                    + "CoordinatedText, Corrigendum, ConstitutionalReviewDecision",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgVocabularyKind: ResourceClass, "
                    + "TypeDocument, UserFormat, RelationPredicate, Language, PublicationFamily, "
                    + "LegalValue, Licence, Rights, RightsHolder, Publisher, ForceStatus, "
                    + "AssertionPredicate",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgWemiBlockerCode: ObservationMismatch, "
                    + "RootTypeObjectInvalid, RootTypeMissing, RootTypeMismatch, RootTypeConflict, "
                    + "RealizationObjectInvalid, RealizationMissing, ExpressionTypeObjectInvalid, "
                    + "ExpressionTypeMissing, ExpressionTypeMismatch, ExpressionTypeConflict, "
                    + "ExpressionLanguageObjectInvalid, ExpressionLanguageMissing, "
                    + "EmbodimentObjectInvalid, EmbodimentMissing, ManifestationTypeObjectInvalid, "
                    + "ManifestationTypeMissing, ManifestationTypeMismatch, "
                    + "ManifestationTypeConflict, ManifestationFormatObjectInvalid, "
                    + "ManifestationFormatMissing, CoordinateConflict, ExpressionLanguageConflict, "
                    + "ManifestationFormatConflict, ManifestationItemObjectInvalid, "
                    + "ManifestationItemMissing, ManifestationItemConflict, "
                    + "ManifestationItemUriFamilyNotAdmitted, PreviousItemObjectInvalid",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgWemiCandidateDisposition: "
                    + "StructurallyConsistent, TypedQuarantine",
                "Lex.V3.Contracts.Source.Quarantine.QuarantineInventoryRefusal: None, "
                    + "ReproductionRolesNotDistinct, ReproducerIdentitiesNotDistinct, "
                    + "ReproductionCountMismatch, ReproductionsDisagree, PriorIndexPairHashInvalid",
                "Lex.V3.Contracts.Source.Quarantine.QuarantineReproducerRole: Primary, "
                    + "IndependentReviewer",
                "Lex.V3.Contracts.Source.Quarantine.QuarantineReproductionRefusal: None, "
                    + "RoleUndefined, ReproducerIdentityInvalid, CoordinatesEmpty, "
                    + "CoordinatesTooMany, DuplicateCoordinate",
                "Lex.V3.Contracts.Source.Scope.ScopeAxis: Record, Body, Relation, "
                    + "SupportingDocument",
                "Lex.V3.Contracts.Source.Scope.ScopeDisposition: AcceptedSelected, TypedQuarantine, "
                    + "Point, NeverIngest",
                "Lex.V3.Contracts.Source.Scope.ScopeManifestFetchAddressAbsenceReason: "
                    + "NoPublisherRouteYet",
                "Lex.V3.Contracts.Source.Scope.ScopeManifestFetchAddressStatus: Minted, NotMinted",
                "Lex.V3.Contracts.Source.Scope.ScopeManifestReaderOnlyInvariant: "
                    + "CanonicalTablesSortedAndUnique, EveryOrdinalResolves, "
                    + "RowsExactlyCoverObservedObjects, RuleBitVectorLengthAndPadding, "
                    + "RuleBitAndMatchedPayloadParity, TypedSelectorObservationAdmission, "
                    + "AxisWinnerRecomputation, ExpandedRowDigestRecomputation, "
                    + "ExactAccountingPartitions, ExactBodyCandidateProjection, "
                    + "CanonicalRequestValidation, EvidenceArtifactTableExactCoverage, "
                    + "ExactRuleEvaluationAdmission, CompleteEnumerationAdmission",
                "Lex.V3.Contracts.Source.Scope.ScopeRuleEffect: Positive, ExactDenial",
                "Lex.V3.Contracts.Source.Scope.ScopeRuleEvaluationState: NotMatched, Matched",
                "Lex.V3.Contracts.Source.Scope.ScopeSelectorEvidenceKind: ObservedValueSet, "
                    + "CompleteObservationAbsence, ObservedConflictingValueSet",
                "Lex.V3.Contracts.Source.Scope.ScopeSelectorState: PublisherValuePresent, "
                    + "PublisherValueAbsent, PublisherValueConflict, SelectorNotApplicable",
                "Lex.V3.Contracts.SyntheticSliceBlobKind: SourceTransport, DerivedText, SqliteIndex",
                "Lex.V3.Contracts.TimelineSemantics: PublisherApplicability, "
                    + "OfficialConsolidationState",
                "Lex.V3.Contracts.WhatWouldAnswerAction: CorrectedIdentifier, "
                    + "NewOfficialObservation, ExpandedOfficialScope",
                "Lex.V3.Custody.Probe.CustodyProbeApplication+ProbeMode: Write, Read, ReadReceipt",
                "Lex.V3.Preview.SyntheticBuildFailpoint: SourcePartialWritten, SourceFlushed, "
                    + "SourceRenamed, BeforeDecode, RejectedReceiptFlushed, RejectedReceiptRenamed, "
                    + "DerivedFlushed, DerivedRenamed",
                "Lex.V3.Preview.SyntheticRecoveryKind: EmptyOrPartial, TransportPersisted, "
                    + "DecodeRejected, SuccessfulOutputPresent",
            },
            ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere).ToArray());
    }
}

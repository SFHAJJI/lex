using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// Every static token registry in the swept assemblies, with the size of each collection it holds
/// and the name of each string token. 0 of them when this was written.
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
/// failed at a named element. Constants, static readonly strings and readable static string
/// properties are all rendered now, each with the kind that carries it. Readable means it has a
/// getter; one that also has a setter is admitted, because that is mutable state and a registry
/// reassignable at run time is the more interesting case rather than the less.
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
/// This assembly holds no static token registry at all, so the pin is an empty array.
/// An empty baseline that can only ever be compared with itself would pass forever, so
/// say what makes this one real: the sweep is over the assembly, not over a list, and the
/// first registry added to it becomes an element the empty expectation does not have.
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
                "Lex.V3.Ingest.Europe.EuLocatedAmendmentAxiomDecode: const "
                    + "AnnotatedPropertyPredicateIri, const AnnotatedSourcePredicateIri, "
                    + "const AnnotatedTargetPredicateIri, const OwlAxiomClassIri, "
                    + "const RdfTypePredicateIri",
                "Lex.V3.Ingest.Europe.EuLocatedAmendmentProductionCanonicalWriter: const "
                    + "DigestDomain, const Schema",
                "Lex.V3.Ingest.Europe.EuObjectFactsBatchFactory: SetsOverObservedObjects=5, "
                    + "SetsOverPackRootsOnly=1",
                "Lex.V3.Ingest.Europe.EuOfficialJournalNumberForms: All=2, const ActNumber, const SeriesNumber",
                "Lex.V3.Ingest.Europe.EuOfficialJournalReferenceKinds: All=2, const ActPublication, "
                    + "const ConsolidatedActPublication",
                "Lex.V3.Ingest.Europe.EuropeForceDateRoles: InOrder=6, Named=4, const Application, "
                    + "const Deadline, const EndOfValidity, const EntryIntoForce, const NotPinned, const Signature",
                "Lex.V3.Ingest.Europe.EuropeIndexBuilder: const AnnotatedPropertyIri, const AnnotatedSourceIri, "
                    + "const AnnotatedTargetIri, const CommentOnDateIri, static readonly Ddl, const DigestDdl, "
                    + "const DigestSchema, const ForceDatesDdl, static readonly ForceDatesFullDdl, const ForceDatesSchema, "
                    + "const LegacyDdl, const LegacySchema, const Schema, "
                    + "const SourceDdl, const SourceSchema, const StatesDdl, static readonly StatesFullDdl, "
                    + "const StatesSchema, const TitlesDdl, const TypeOfDateIri",
                "Lex.V3.Ingest.Europe.EuropeTitleKinds: const ShortTitle, const Title",
                "Lex.V3.Ingest.Europe.EuropeTitleSources: InOrder=3, const CellarExpressionTitle, "
                    + "const CellarExpressionTitleShort, const FormexTitle",
                "Lex.V3.Ingest.LexCorpus6Builder: Domain=13, const Schema",
                "Lex.V3.Ingest.Luxembourg.LuxembourgInFileRightsReader: const ReadingTwoProfileName, "
                    + "const ReadingTwoProfileText, static property ReadingTwoProfileSha256",
                "Lex.V3.Ingest.Luxembourg.LuxembourgIndexBuilder: EdgeTypeByPredicate=4, "
                    + "ImpactParts=6, StatePredicates=8, const ArticleDateBasic, const "
                    + "ArticleDateExtended, const ArticleDateNone, const ArticleDateNotRead, const "
                    + "BoundaryDerived, const BoundaryHalfOpen, const BoundaryInclusiveSuspected, "
                    + "const BoundaryInvalid, const BuiltAtFormat, const ComplexWorkIri, const "
                    + "Ddl, const EdgeModifiedTemporarilyBy, const EdgeModifies, const "
                    + "EdgeRectifies, const EdgeRepeals, const EventLogDdl, const EventLogSchema, "
                    + "const FrenchLanguage, const FrenchLanguageIri, const IntervalContiguous, "
                    + "const IntervalDerived, const IntervalHole, const "
                    + "IntervalInclusiveSuspected, const IntervalInvalid, const IntervalOverlap, "
                    + "const IntervalSameDate, const JoluxNamespace, const KindAsPublished, const "
                    + "KindCode, const KindConsolidation, const LegacyDdl6, const LegacyDdl8, "
                    + "const LegacyDdl9, const LegacyEventLogDdl6, const LegacyEventLogDdl8, const "
                    + "LegacyEventLogSchema8, const LegacySchema6, const LegacySchema8, const "
                    + "LegacySchema9, const LegacyTablesDdl, const LegiluxEliRoot, const "
                    + "PublisherRelationsDdl, const RoleApplicability, const "
                    + "RoleApplicabilityInFile, const RoleEntryIntoForce, const Schema, const "
                    + "SourceDerived, const SourcePublisher, static readonly StateRuleDigest, "
                    + "const StateRuleProfile, const TablesDdl, const TextHeld, const "
                    + "TextNotAcquired, const TextNotAvailableDerivation, const "
                    + "TextNotAvailableMisfiled, const TextNotAvailablePdf, const "
                    + "TextWithheldRights, static property StateRuleProfileSha256",
                "Lex.V3.Ingest.Luxembourg.LuxembourgIndexQueries: const ActingFacts, const "
                    + "AnchorArticles, const ArticleIds, const CatalogueStatesOfWork, const "
                    + "CitationsTo, const EventCount, const EventLog, const EventsAfter, const "
                    + "EventsOfNameAfter, const HeldWorks, const HeldWorksNine, const "
                    + "IntervalsOfWork, const MemberOutcomes, const Observations, const "
                    + "ObservationsNine, const PublisherImpactsOf, const PublisherRelationsOf, "
                    + "const RevisingEventsAfter, const SearchTexts, const StateArticles, const "
                    + "StateArticlesNine, const StateCitations, const StateColumnsNine, const "
                    + "StateDocumentOutcomes, const StateSources, const StateTextSources, const "
                    + "StatesOfExpressions, const StatesOfExpressionsNine, const SubjectFacts, "
                    + "const SubjectFactsUnder, const WorkEventsUpTo, const WorkRecords, const "
                    + "WorkRecordsNine, const WorkTitles",
                "Lex.V3.Ingest.Luxembourg.LuxembourgPublisherPdfActScopeProducer: const "
                    + "AdministrativeMemorialPrefix, const ExpectedResourceHost, "
                    + "const LegislativeMemorialPrefix, const RuleProfile, "
                    + "static property RuleProfileSha256",
                "Lex.V3.Ingest.Luxembourg.LuxembourgRederivation: ImplementedRuleSets=3, "
                    + "const InFileEvidenceTwoSchema, const NoPublisherSession, "
                    + "const Release1RuleSet, const Release2RuleSet, const Release3RuleSet, const RightsAsAcquired, "
                    + "const RightsReResolved",
                "Lex.V3.Ingest.Luxembourg.LuxembourgReferralDateComposition: const HasOpinion, "
                    + "const IriKind, const ReferralDate",
                "Lex.V3.Ingest.Names.IndexingRecord: const FileName, const Schema",
                "Lex.V3.Ingest.Names.NamesMeasurement: const EuropeIndexFile, const EuropeSchema, "
                    + "const EuropeTitlesSchema, const LuxembourgIndexFile, const LuxembourgSchema, const "
                    + "LuxembourgSchemaTen, const MeasurementFileName, const MeasurementSchema",
                "Lex.V3.Ingest.Names.NamesRecord: const EuropePublisher, const FileName, "
                    + "const LuxembourgPublisher, const Rule, const Schema",
                "Lex.V3.Ingest.Names.PrivateIndividuals: Classes=11, NamingClasses=9, Particulars=5, "
                    + "ReasonCodes=7, const BirthDateCode, const BirthLabelSignal, const BirthPlaceCode, "
                    + "const CapitalToken, const ClassCode, const DomicileCode, const EuropeGeneral, "
                    + "const EuropeRestrictiveMeasures, const EuropeTextNamesPerson, const Honorific, "
                    + "const IdentityDocumentCode, const Lower, const LuxembourgCivilStatusOfNamedPerson, "
                    + "const LuxembourgGeneral, const LuxembourgGrantToNamedPerson, const LuxembourgNaturalisation, "
                    + "const LuxembourgNobility, const LuxembourgPropertyOfNamedPerson, "
                    + "const LuxembourgTextNamesPerson, const LuxembourgTitleNamesPerson, const MeasureVersion, "
                    + "const MonthsEnglish, const MonthsFrench, const NameToken, const NationalNumberCode, "
                    + "const Office, const OldHonorific, const PersonNamedCode, const Reason, const Upper",
                "Lex.V3.Ingest.V3CorpusMountWriter: GenerationFiles=6, "
                    + "const GenerationsDirectoryName, const ReportFileName, "
                    + "const ReportSchema, const RetentionFileName",
                "Lex.V3.Ingest.V3GenerationRetention: const MonthlyKeeper, const Nightly, "
                    + "const PolicyId, const Referenced",
                "Lex.V3.Ingest.V3OfflineMount: const RederivationSchema, const Schema",
            },
            ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere).ToArray(),
            "Observed registries for reviewed transcription:\n" + string.Join("\n", ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere)));
    }
}

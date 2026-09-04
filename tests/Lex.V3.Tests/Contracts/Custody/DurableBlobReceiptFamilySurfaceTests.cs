using Lex.V3.Contracts.Custody;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Custody;

/// <summary>
/// The construction surface of the three receipt types <see cref="RoutedHttpEvidenceSurfaceTests"/>
/// documents as forgeable: <see cref="DurableBlobWriteReceipt"/>, <see cref="DurableBlobRef"/> and
/// <see cref="CustodyPolicyEvidence"/> all have public constructors, by design (Decision 71: the two
/// genuine custody stores live in other assemblies, so Contracts cannot constructor-seal these types
/// against them). Decision 80's fold-in three: since visibility alone proves nothing, the doc
/// comment on <c>RoutedHttpEvidence.Create</c> says so plainly, and this file fences the actual gap
/// structurally instead -- by pinning the exact, closed, reviewed set of real producers of these
/// three types across Contracts and the two store assemblies the test assembly can see
/// (<c>Lex.V3.Artifacts</c> and <c>Lex.V3.Custody.Azure</c>). <c>Lex.V3.Ingest</c> is pinned
/// separately in <c>Lex.V3.Ingest.Tests</c>, which is the only project that references it.
///
/// <para>
/// A producer here includes holders (a field, property or <c>Deconstruct</c> out-parameter that
/// merely carries an already-minted instance), not only constructors: a positive-holder count is not
/// a finding by itself, the finding is a <em>new</em> entry, exactly as
/// <see cref="RoutedHttpEvidenceSurfaceTests.EveryOtherHolderOfEvidenceInContractsIsPinned"/> already
/// establishes for evidence itself.
/// </para>
/// </summary>
[TestClass]
public sealed class DurableBlobReceiptFamilySurfaceTests
{
    private const string Receipt = "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt";
    private const string Ref = "Lex.V3.Contracts.Custody.DurableBlobRef";
    private const string Policy = "Lex.V3.Contracts.Custody.CustodyPolicyEvidence";
    private const string Custody = "Lex.V3.Contracts.Custody.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";
    private const string Corpus = "Lex.V3.Contracts.Source.Corpus.";
    private const string Http = "Lex.V3.Contracts.Source.Http.";
    private const string Luxembourg = "Lex.V3.Contracts.Source.Luxembourg.";

    [TestMethod]
    public void ReceiptIsMintedByExactlyThreeDeclaredPaths()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Receipt + "::.ctor(" + Receipt + ") -> " + Receipt,
                "constructor public instance " + Receipt + "::.ctor(System.String, " + Ref + ", " + Policy + ") -> " + Receipt,
                "method public instance " + Receipt + "::<Clone>$() -> " + Receipt,
            },
            ConstructionSurface.Of(typeof(DurableBlobWriteReceipt)).ToArray(),
            "a new path onto the receipt itself must be justified in review, not discovered later");
    }

    [TestMethod]
    public void RefIsMintedByExactlyThreeDeclaredPaths()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Ref + "::.ctor(" + Ref + ") -> " + Ref,
                "constructor public instance " + Ref + "::.ctor(System.String, System.String, System.Int64, " + Custody + "CustodyClass) -> " + Ref,
                "method public instance " + Ref + "::<Clone>$() -> " + Ref,
            },
            ConstructionSurface.Of(typeof(DurableBlobRef)).ToArray());
    }

    [TestMethod]
    public void PolicyEvidenceIsMintedByExactlyFourDeclaredPaths()
    {
        // A fourth path that is not a constructor of the type in the ordinary sense: NightlyFloor's
        // static field initializer becomes a static constructor, which GetConstructors(Everything)
        // reports like any other. It never returns an instance and is not a hole; it is here because
        // this guard reads signatures, not intent, and a real new instance producer would look the
        // same as an unrelated fourth line if this one were filtered out by hand instead of pinned.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Policy + "::.ctor(" + Policy + ") -> " + Policy,
                "constructor private static " + Policy + "::.cctor() -> " + Policy,
                "constructor public instance " + Policy + "::.ctor(System.String, " + Ref + ", " + Custody + "CustodyVerificationProfile, System.Nullable<System.Guid>, " + Custody + "CustodyProtection, System.DateTimeOffset, System.Nullable<System.DateTimeOffset>) -> " + Policy,
                "method public instance " + Policy + "::<Clone>$() -> " + Policy,
            },
            ConstructionSurface.Of(typeof(CustodyPolicyEvidence)).ToArray());
    }

    [TestMethod]
    public void EveryOtherHolderOfReceiptInContractsIsPinned()
    {
        // LuxembourgObservedTransport (Lex.V3.Contracts.Source.Luxembourg) joined this list with
        // the D1-03 repeated-enumeration executor: a record holding the four transport facts of one
        // observation the executor has already read back out of custody, so it carries the write
        // receipt beside them the same way RepeatedEnumerationResolvedEvidence does above it. Same
        // three producer shapes (Deconstruct out-parameter, backing field, property), reviewed and
        // pinned rather than discovered later.
        //
        // CorpusBodyRecord (Lex.V3.Contracts.Source.Corpus, D1-06a) joined with the corpus/6 record
        // contract: its own held-body variant carries the receipt beside the Decision-71 floor that
        // receipt's own policy evidence proves, so it is a plain field-and-property holder, never a
        // positional record (it declares no primary constructor for the compiler to generate a
        // Deconstruct from), which is why only two new lines appear here rather than three.
        CollectionAssert.AreEqual(
            new[]
            {
                "by-ref-method public instance " + Custody + "CustodiedDecode<T>::Deconstruct(out " + Receipt + "&, out T&) -> System.Void",
                "by-ref-method public instance " + Core + "RepeatedEnumerationResolvedEvidence::Deconstruct("
                + "out " + Core + "MachineQueryPlan&, out " + Core + "MachineQueryInputArtifact&, "
                + "out " + Core + "MachineQueryRenderReceipt&, out " + Core + "IMachineQueryRenderer&, "
                + "out " + Http + "HttpLogicalRequest&, "
                + "out " + Http + "RoutedHttpEvidence&, out " + Receipt + "&, "
                + "out System.ReadOnlyMemory<System.Byte>&) -> System.Void",
                "by-ref-method public instance " + Luxembourg + "LuxembourgObservedTransport::Deconstruct("
                + "out " + Http + "HttpLogicalRequest&, out " + Http + "RoutedHttpEvidence&, out " + Receipt + "&, "
                + "out System.ReadOnlyMemory<System.Byte>&) -> System.Void",
                "field private instance " + Custody + "CustodiedDecode<T>::<Receipt>k__BackingField -> " + Receipt,
                "field private instance " + Core + "RepeatedEnumerationResolvedEvidence::<DurableWriteReceipt>k__BackingField -> " + Receipt,
                "field private instance " + Corpus + "CorpusBodyRecord::<Receipt>k__BackingField -> " + Receipt,
                "field private instance " + Luxembourg + "LuxembourgObservedTransport::<DurableWriteReceipt>k__BackingField -> " + Receipt,
                "method public instance " + Custody + "ICustodyStore::CreateAsync(System.ReadOnlyMemory<System.Byte>, "
                + Custody + "CustodyClass, System.Threading.CancellationToken) -> System.Threading.Tasks.Task<" + Receipt + ">",
                "property public instance " + Custody + "CustodiedDecode<T>::Receipt() -> " + Receipt,
                "property public instance " + Core + "RepeatedEnumerationResolvedEvidence::DurableWriteReceipt() -> " + Receipt,
                "property public instance " + Corpus + "CorpusBodyRecord::Receipt() -> " + Receipt,
                "property public instance " + Luxembourg + "LuxembourgObservedTransport::DurableWriteReceipt() -> " + Receipt,
            },
            ConstructionSurface.ProducersIn(typeof(DurableBlobWriteReceipt).Assembly, typeof(DurableBlobWriteReceipt), true).ToArray());
    }

    [TestMethod]
    public void EveryOtherHolderOfRefInContractsIsPinned()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + Policy + "::<Reference>k__BackingField -> " + Ref,
                "field private instance " + Receipt + "::<Reference>k__BackingField -> " + Ref,
                "property public instance " + Policy + "::Reference() -> " + Ref,
                "property public instance " + Receipt + "::Reference() -> " + Ref,
            },
            ConstructionSurface.ProducersIn(typeof(DurableBlobRef).Assembly, typeof(DurableBlobRef), true).ToArray());
    }

    [TestMethod]
    public void EveryOtherHolderOfPolicyEvidenceInContractsIsPinned()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + Receipt + "::<PolicyEvidence>k__BackingField -> " + Policy,
                "property public instance " + Receipt + "::PolicyEvidence() -> " + Policy,
            },
            ConstructionSurface.ProducersIn(typeof(CustodyPolicyEvidence).Assembly, typeof(CustodyPolicyEvidence), true).ToArray());
    }

    /// <summary>
    /// Exactly the genuine store: <see cref="Lex.V3.Artifacts.FileSystemCustodyStore"/>'s own
    /// <c>CreateAsync</c>, whose return type is what makes it visible to a signature scan. The store
    /// also directly constructs <see cref="DurableBlobRef"/> and <see cref="CustodyPolicyEvidence"/>
    /// inside that method body, which this guard cannot see (it reads signatures, not bodies, by
    /// design) and does not need to: nothing else in this assembly's signatures can hand out a
    /// receipt, so a genuine one can only have come from here.
    /// </summary>
    [TestMethod]
    public void ExactlyTheFileSystemStoreProducesReceiptsInArtifacts()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "method public instance Lex.V3.Artifacts.FileSystemCustodyStore::CreateAsync(System.ReadOnlyMemory<System.Byte>, "
                + Custody + "CustodyClass, System.Threading.CancellationToken) -> System.Threading.Tasks.Task<" + Receipt + ">",
            },
            ConstructionSurface.ProducersIn(typeof(Lex.V3.Artifacts.FileSystemCustodyStore).Assembly, typeof(DurableBlobWriteReceipt), true).ToArray());
    }

    [TestMethod]
    public void NoProducerOfRefOrPolicyEvidenceIsVisibleInArtifacts()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(typeof(Lex.V3.Artifacts.FileSystemCustodyStore).Assembly, typeof(DurableBlobRef), true).ToArray());
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(typeof(Lex.V3.Artifacts.FileSystemCustodyStore).Assembly, typeof(CustodyPolicyEvidence), true).ToArray());
    }

    /// <summary>
    /// Exactly the genuine store: three members of <see cref="Lex.V3.Custody.Azure.AzureBlobCustodyStore"/>
    /// and nothing else in this assembly.
    /// </summary>
    [TestMethod]
    public void ExactlyTheAzureStoreProducesReceiptsInCustodyAzure()
    {
        const string Store = "Lex.V3.Custody.Azure.AzureBlobCustodyStore";
        CollectionAssert.AreEqual(
            new[]
            {
                "by-ref-method private instance " + Store + "::TryCreateReceipt(" + Ref + ", "
                + Store + "+RemoteObservation, Lex.V3.Custody.Azure.AzureContainerPolicyObservation, out " + Receipt + "&) -> System.Boolean",
                "method private instance " + Store + "::CreateCoreAsync(System.ReadOnlyMemory<System.Byte>, "
                + Custody + "CustodyClass, System.Threading.CancellationToken) -> System.Threading.Tasks.Task<" + Receipt + ">",
                "method public instance " + Store + "::CreateAsync(System.ReadOnlyMemory<System.Byte>, "
                + Custody + "CustodyClass, System.Threading.CancellationToken) -> System.Threading.Tasks.Task<" + Receipt + ">",
            },
            ConstructionSurface.ProducersIn(typeof(Lex.V3.Custody.Azure.AzureBlobCustodyStore).Assembly, typeof(DurableBlobWriteReceipt), true).ToArray());
    }

    [TestMethod]
    public void NoProducerOfRefOrPolicyEvidenceIsVisibleInCustodyAzure()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(typeof(Lex.V3.Custody.Azure.AzureBlobCustodyStore).Assembly, typeof(DurableBlobRef), true).ToArray());
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(typeof(Lex.V3.Custody.Azure.AzureBlobCustodyStore).Assembly, typeof(CustodyPolicyEvidence), true).ToArray());
    }
}

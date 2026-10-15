using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A held Luxembourg body population of several members, each at its own ordinal and under its
/// own selected WEMI, built through the same doors a run uses: one custody receipt per member, one
/// corpus record per member in a verified record set, one selected fetch per record, then
/// <c>LuxembourgHeldBodyDerivationPopulation.TryCreate</c>.
/// </summary>
/// <remarks>
/// This is the single-body fixture of <c>LuxembourgAknArticleInventoryProducerTests</c> made general,
/// so a test can hold two states of one work, a file shared by two states, or a sample of a real
/// run at its real ordinals. Every member gets its own receipt even when two bodies are the same
/// bytes, because a run writes each selected body once per member and the shared-file check reads
/// the receipts' content digests, not object identity.
/// </remarks>
internal static class LuxembourgHeldPopulationBuilder
{
    internal const string Eli = "http://data.legilux.public.lu/eli/etat/leg/";
    internal const string French = "http://publications.europa.eu/resource/authority/language/FRA";
    private const string FormatBase = "http://data.legilux.public.lu/resource/authority/user-format/";

    /// <summary>
    /// One held member. The defaults follow the publisher's own shape: the expression is the root
    /// plus <c>/fr</c>, the manifestation the expression plus <c>/xml</c> or <c>/pdf</c>, and the item
    /// the manifestation's filestore file.
    /// </summary>
    internal sealed record Member(
        string Root,
        byte[] Body,
        LuxembourgUserFormatToken Token = LuxembourgUserFormatToken.XmlAkomaNtoso,
        int? Ordinal = null,
        string? Expression = null,
        string? Manifestation = null,
        string? Item = null,
        string? Language = null);

    internal static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        LuxembourgHeldBodyDerivationPopulation Population)> CreateAsync(params Member[] members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var held = new (Member Member, DurableBlobWriteReceipt Receipt)[members.Length];
        for (var index = 0; index < members.Length; index++)
        {
            held[index] = (members[index], await store.CreateAsync(
                members[index].Body, CustodyClass.NightlyFloor90d, CancellationToken.None));
        }

        return (store, Create(held));
    }

    /// <summary>
    /// The same population over receipts the caller already holds, such as the run-15 probe's
    /// receipts for bytes that stay in the run's own custody. Each member's <c>Body</c> is not read.
    /// </summary>
    internal static LuxembourgHeldBodyDerivationPopulation Create(
        IReadOnlyList<(Member Member, DurableBlobWriteReceipt Receipt)> held)
    {
        ArgumentNullException.ThrowIfNull(held);
        Assert.IsNotEmpty(held, "a population has at least one member");
        var enumeration = Artifact('a', "enumeration"u8);
        var manifest = Artifact('b', "manifest"u8);
        var run = Artifact('c', "run"u8);
        var kind = new SourceRegistryMemberRef(enumeration, "lu_document_get_root");

        var records = new List<CorpusRecord>(held.Count);
        var outcomes = new Dictionary<int, CorpusAcquisitionOutcome>();
        var fetches = new Dictionary<SourceObjectRef, LuxembourgSelectedDocumentFetch>();
        var previousOrdinal = -1;
        for (var index = 0; index < held.Count; index++)
        {
            var (member, receipt) = held[index];
            var ordinal = member.Ordinal ?? index;
            Assert.IsGreaterThan(previousOrdinal, ordinal, "member ordinals are strictly increasing");
            previousOrdinal = ordinal;

            var key = "lu-akn:" + member.Root;
            var objectRef = new SourceObjectRef(
                SourceCoreSchemaIds.SourceObjectRef,
                SourceAuthority.Jolux,
                kind,
                member.Root,
                key,
                Sha(Encoding.UTF8.GetBytes(key)),
                enumeration,
                null);
            records.Add(new CorpusRecord(
                CorpusRecordSchemaIds.Record,
                objectRef,
                ordinal,
                ScopeDisposition.AcceptedSelected,
                ScopeDisposition.AcceptedSelected,
                ScopeDisposition.AcceptedSelected,
                ScopeDisposition.AcceptedSelected,
                CorpusBodyRecord.Held(receipt),
                manifest,
                run));
            outcomes.Add(ordinal, CorpusAcquisitionOutcome.Held(receipt));

            var pdf = member.Token is LuxembourgUserFormatToken.Pdf or LuxembourgUserFormatToken.PdfA;
            var expression = member.Expression ?? member.Root + "/fr";
            var manifestation = member.Manifestation ?? expression + (pdf ? "/pdf" : "/xml");
            var item = member.Item ?? FilestoreItem(manifestation, pdf);
            var address = LuxembourgDocumentFetchAddress.Create(
                LuxembourgFileUri.RequireValid(item),
                member.Token,
                LuxembourgLegalValue.Unstated,
                new Uri(expression, UriKind.Absolute).AbsolutePath);
            var candidate = new LuxembourgWemiCandidate(
                member.Root,
                expression,
                manifestation,
                address.StoreFileUri.Value.AbsoluteUri,
                member.Language ?? French,
                FormatBase + FormatName(member.Token),
                enumeration,
                LuxembourgWemiCandidateDisposition.StructurallyConsistent,
                []);
            fetches.Add(objectRef, new LuxembourgSelectedDocumentFetch(address, candidate));
        }

        var set = new CorpusRecordSet(CorpusRecordSetSchemaIds.Set, manifest, run, records);
        using var canonical = new MemoryStream();
        var setDigest = CorpusRecordSetCanonicalWriter.Write(canonical, set);
        var verified = VerifiedCorpusRecordSet.ParseAndVerify(
            new SourceArtifactRef("urn:uuid:00000000-0000-0000-0000-00000000000d", setDigest),
            canonical.ToArray());
        var population = LuxembourgHeldBodyDerivationPopulation.TryCreate(
            verified, outcomes, fetches, out var refusal, out var detail);
        Assert.AreEqual(LuxembourgHeldBodyDerivationPopulationRefusal.None, refusal, detail);
        Assert.IsNotNull(population);
        return population;
    }

    /// <summary>The token's own user-format name, as the publisher's authority IRI spells it.</summary>
    internal static string FormatName(LuxembourgUserFormatToken token) => token switch
    {
        LuxembourgUserFormatToken.XmlAkomaNtoso => "xml-akomantoso",
        LuxembourgUserFormatToken.Xml => "xml",
        LuxembourgUserFormatToken.PdfA => "pdfa",
        LuxembourgUserFormatToken.Pdf => "pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };

    /// <summary>The token a run's format name selects: the inverse of <see cref="FormatName"/>.</summary>
    internal static LuxembourgUserFormatToken Token(string formatName) => formatName switch
    {
        "xml-akomantoso" => LuxembourgUserFormatToken.XmlAkomaNtoso,
        "xml" => LuxembourgUserFormatToken.Xml,
        "pdfa" => LuxembourgUserFormatToken.PdfA,
        "pdf" => LuxembourgUserFormatToken.Pdf,
        _ => throw new ArgumentOutOfRangeException(nameof(formatName), formatName, null),
    };

    internal static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string FilestoreItem(string manifestation, bool pdf)
    {
        var path = new Uri(manifestation, UriKind.Absolute).AbsolutePath.TrimStart('/');
        return "http://data.legilux.public.lu/filestore/" + path + "/" + path.Replace('/', '-') +
            (pdf ? ".pdf" : ".xml");
    }

    private static SourceArtifactRef Artifact(char suffix, ReadOnlySpan<byte> bytes) => new(
        $"urn:uuid:00000000-0000-0000-0000-00000000000{suffix}", Sha(bytes));
}

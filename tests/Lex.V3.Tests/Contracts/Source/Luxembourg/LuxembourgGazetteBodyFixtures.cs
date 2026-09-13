using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// Shared fixtures for #419 slice 6a: body joins over one as-published act, rights observations
/// in every state the Gazette channel must type, and custody receipts for retained bytes. Same
/// recipe as <c>LuxembourgBodyJoinTests</c>, parameterized by root.
/// </summary>
internal static class LuxembourgGazetteBodyFixtures
{
    public const string ActLoi1 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
    public const string ActRgd2 = "http://data.legilux.public.lu/eli/etat/leg/rgd/2026/02/02/a2/jo";
    public const string ActLoi3 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/03/03/a3/jo";
    public const string ActAmin5 = "http://data.legilux.public.lu/eli/etat/leg/amin/2026/05/05/a5/jo";
    public const string ActNeverHeld = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/09/09/a9/jo";

    public const string LanguageFra = "http://publications.europa.eu/resource/authority/language/FRA";
    public const string LanguageDeu = "http://publications.europa.eu/resource/authority/language/DEU";
    public const string FormatXml = "http://data.legilux.public.lu/resource/authority/user-format/xml";
    public const string FormatPdfA = "http://data.legilux.public.lu/resource/authority/user-format/pdfa";
    public const string FormatPdf = "http://data.legilux.public.lu/resource/authority/user-format/pdf";
    public const string CcBy40 = "http://creativecommons.org/licenses/by/4.0/";
    public const string LicenceScl = "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    public static SourceArtifactRef Run { get; } = Artifact("cbe6e64c-789a-4c73-854d-19e464728a50", '1');
    public static SourceArtifactRef OtherRun { get; } = Artifact("37530abe-232e-4ad0-a9ab-319d9f0823bb", '2');
    public static SourceArtifactRef SparqlEnumeration { get; } = Artifact("388b94c5-c812-494a-8414-11659c742d7f", '3');
    public static SourceArtifactRef InFileEnumeration { get; } = Artifact("43b2af70-9a13-4a0f-a202-f5bb00199239", '4');
    public static SourceArtifactRef SparqlEvidence { get; } = Artifact("5f4c1a2e-0b7d-4e7f-9c1a-2b3c4d5e6f70", '5');
    public static SourceArtifactRef InFileEvidence { get; } = Artifact("6a5b2c3d-1e8f-4a0b-8d2c-3e4f5a6b7c81", '6');
    public static SourceArtifactRef FetchEvidence { get; } = Artifact("7b6c3d4e-2f90-4b1c-9e3d-4f5a6b7c8d92", '7');

    public static string ManifestationOf(string act, string language, string format) =>
        act + "/" + language + "/" + format;

    public static string ItemOf(string act, string language, string format) =>
        "http://data.legilux.public.lu/filestore/" + act[(act.LastIndexOf("leg/", StringComparison.Ordinal) + 4)..]
            .Replace('/', '-') + "-" + language + "." + format;

    /// <summary>One structurally consistent listing of <paramref name="act"/>, unless told otherwise.</summary>
    public static LuxembourgWemiCandidate Candidate(
        string act,
        string language,
        string format,
        string? rootOverride = null,
        LuxembourgWemiCandidateDisposition disposition = LuxembourgWemiCandidateDisposition.StructurallyConsistent,
        IReadOnlyList<LuxembourgWemiBlockerCode>? blockers = null)
    {
        var languageIri = language == "fr" ? LanguageFra : LanguageDeu;
        var formatIri = format switch { "pdfa" => FormatPdfA, "pdf" => FormatPdf, _ => FormatXml };
        return new LuxembourgWemiCandidate(
            rootOverride ?? act,
            act + "/" + language,
            ManifestationOf(act, language, format),
            ItemOf(act, language, format),
            languageIri,
            formatIri,
            Run,
            disposition,
            blockers ?? []);
    }

    public static LuxembourgWemiTopologyResolution Topology(params LuxembourgWemiCandidate[] candidates) => new(
        candidates,
        [],
        [],
        candidates.Select(static c => c.ExpressionIri).Distinct(StringComparer.Ordinal)
            .OrderBy(static v => v, StringComparer.Ordinal).ToArray(),
        candidates.Select(static c => c.ManifestationIri).Distinct(StringComparer.Ordinal)
            .OrderBy(static v => v, StringComparer.Ordinal).ToArray());

    public static LuxembourgRightsChannelObservation Sparql(string manifestationIri, params string[] licences) =>
        new(manifestationIri, Run, SparqlEvidence, licences);

    public static LuxembourgRightsChannelObservation InFileRead(string manifestationIri, params string[] licences) =>
        new(manifestationIri, Run, InFileEvidence, licences);

    /// <summary>Both channels agree on the admitting licence for every listed manifestation.</summary>
    public static LuxembourgBodyJoinResolution JoinAgreedCcBy(string act, params LuxembourgWemiCandidate[] candidates) =>
        LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration, candidates.Select(c => Sparql(c.ManifestationIri, CcBy40)).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                Run, InFileEnumeration, candidates.Select(c => InFileRead(c.ManifestationIri, CcBy40)).ToArray()));

    /// <summary>Both channels say the publisher marked every listed manifestation not reusable.</summary>
    public static LuxembourgBodyJoinResolution JoinLicenceScl(string act, params LuxembourgWemiCandidate[] candidates) =>
        LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration, candidates.Select(c => Sparql(c.ManifestationIri, LicenceScl)).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                Run, InFileEnumeration, candidates.Select(c => InFileRead(c.ManifestationIri, LicenceScl)).ToArray()));

    /// <summary>
    /// The realistic PDF case today: SPARQL says CC BY; the in-file channel ran, could not read the
    /// PDF, and rejected the reading (unsupported representation) - so it holds no row and names the
    /// manifestation among its rejected ones.
    /// </summary>
    public static LuxembourgBodyJoinResolution JoinSecondChannelCannotReadPdf(string act, params LuxembourgWemiCandidate[] candidates) =>
        LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration, candidates.Select(c => Sparql(c.ManifestationIri, CcBy40)).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                Run,
                InFileEnumeration,
                [],
                acquisitionCompleted: true,
                rejectedManifestationIris: candidates.Select(static c => c.ManifestationIri).ToArray()));

    /// <summary>A custody receipt for bytes whose digest is <paramref name="digestCharacter"/> x 64.</summary>
    public static DurableBlobWriteReceipt Receipt(char digestCharacter, long length = 1234)
    {
        var digest = new string(digestCharacter, 64);
        var blob = new DurableBlobRef(CustodySchemaIds.DurableBlobRef, digest, length, CustodyClass.NightlyFloor90d);
        var time = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            blob,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                blob,
                CustodyVerificationProfile.ImmutableObject1,
                new Guid("0f8b2c4d-6e7a-4b9c-8d1e-2f3a4b5c6d7e"),
                CustodyProtection.LockedTime,
                time,
                time.AddDays(91)));
    }

    public static SourceArtifactRef Artifact(string id, char digestCharacter) =>
        new("urn:uuid:" + id, new string(digestCharacter, 64));
}

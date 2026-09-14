using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why retained Formex bytes did not prove an annex inventory. Closed.</summary>
public enum EuFormexAnnexInventoryRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("profile_digest_mismatch")]
    ProfileDigestMismatch = 1,

    [JsonStringEnumMemberName("profile_invalid")]
    ProfileInvalid = 2,

    [JsonStringEnumMemberName("profile_does_not_name_transport")]
    ProfileDoesNotNameTransport = 3,

    [JsonStringEnumMemberName("retained_bytes_unavailable")]
    RetainedBytesUnavailable = 4,

    [JsonStringEnumMemberName("package_unreadable")]
    PackageUnreadable = 5,

    [JsonStringEnumMemberName("package_entry_invalid")]
    PackageEntryInvalid = 6,

    [JsonStringEnumMemberName("xml_invalid")]
    XmlInvalid = 7,

    [JsonStringEnumMemberName("annex_document_reference_missing")]
    AnnexDocumentReferenceMissing = 8,

    [JsonStringEnumMemberName("annex_sequence_missing")]
    AnnexSequenceMissing = 9,

    [JsonStringEnumMemberName("annex_page_extent_missing")]
    AnnexPageExtentMissing = 10,

    [JsonStringEnumMemberName("annex_page_extent_invalid")]
    AnnexPageExtentInvalid = 11,

    [JsonStringEnumMemberName("annex_page_extent_contradictory")]
    AnnexPageExtentContradictory = 12,

    [JsonStringEnumMemberName("duplicate_annex_identity")]
    DuplicateAnnexIdentity = 13,

    [JsonStringEnumMemberName("package_does_not_identify_formex")]
    PackageDoesNotIdentifyFormex = 14,

    [JsonStringEnumMemberName("annex_title_missing")]
    AnnexTitleMissing = 15,
}

/// <summary>One annex unit derived from a retained Formex package.</summary>
public sealed record EuFormexAnnexInventoryMember(
    string PackageEntry,
    string Sequence,
    string DocumentReferenceFile,
    string DocumentReferenceValue,
    int PageFirst,
    int PageLast,
    int PageTotal,
    string Title);

/// <summary>The complete ordered annex population proven by one retained Formex package.</summary>
public sealed class EuFormexAnnexInventory
{
    internal EuFormexAnnexInventory(
        DurableBlobWriteReceipt sourceReceipt,
        SourceArtifactRef profileRef,
        IReadOnlyList<EuFormexAnnexInventoryMember> members)
    {
        SourceReceipt = sourceReceipt;
        ProfileRef = profileRef;
        Members = members.ToArray();
        IdentitySha256 = IdentityOf(sourceReceipt, profileRef, Members);
    }

    public DurableBlobWriteReceipt SourceReceipt { get; }

    public SourceArtifactRef ProfileRef { get; }

    public IReadOnlyList<EuFormexAnnexInventoryMember> Members { get; }

    public string IdentitySha256 { get; }

    private static string IdentityOf(
        DurableBlobWriteReceipt receipt,
        SourceArtifactRef profileRef,
        IReadOnlyList<EuFormexAnnexInventoryMember> members)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-eu-formex-annex-inventory/1");
        Append(hash, receipt.Reference.ContentSha256);
        Append(hash, profileRef.ResourceId);
        Append(hash, profileRef.Sha256);
        foreach (var member in members)
        {
            Append(hash, member.PackageEntry);
            Append(hash, member.Sequence);
            Append(hash, member.DocumentReferenceFile);
            Append(hash, member.DocumentReferenceValue);
            Append(hash, member.PageFirst.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, member.PageLast.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, member.PageTotal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, member.Title);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

/// <summary>One complete Formex annex inventory, or one named refusal.</summary>
public sealed class EuFormexAnnexInventoryProductionResult
{
    private EuFormexAnnexInventoryProductionResult(
        EuFormexAnnexInventory? inventory,
        EuFormexAnnexInventoryRefusal refusal,
        string? detail)
    {
        Inventory = inventory;
        Refusal = refusal;
        Detail = detail;
    }

    public EuFormexAnnexInventory? Inventory { get; }

    public EuFormexAnnexInventoryRefusal Refusal { get; }

    public string? Detail { get; }

    public bool Produced => Refusal == EuFormexAnnexInventoryRefusal.None;

    internal static EuFormexAnnexInventoryProductionResult Success(EuFormexAnnexInventory inventory) =>
        new(inventory, EuFormexAnnexInventoryRefusal.None, null);

    internal static EuFormexAnnexInventoryProductionResult Refused(
        EuFormexAnnexInventoryRefusal refusal,
        string detail) => new(null, refusal, detail);
}

/// <summary>
/// Reopens one retained Formex ZIP and derives every ANNEX unit and its declared PDF page extent.
/// </summary>
public sealed class EuFormexAnnexInventoryProducer
{
    private const string ProfileHeader = "lex-v3-eu-formex-annex-inventory-profile/1";
    private const int MaxEntries = 4_096;
    private const long MaxXmlEntryBytes = 16 * 1024 * 1024;
    private const long MaxXmlPackageBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ICustodyStore _custodyStore;

    public EuFormexAnnexInventoryProducer(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<EuFormexAnnexInventoryProductionResult> RunAsync(
        DurableBlobWriteReceipt retainedFormexBytes,
        ReadOnlyMemory<byte> profileBytes,
        SourceArtifactRef profileRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retainedFormexBytes);
        ArgumentNullException.ThrowIfNull(profileRef);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(
                CustodyDigest.Of(profileBytes.Span, cancellationToken),
                profileRef.Sha256,
                StringComparison.Ordinal))
        {
            return EuFormexAnnexInventoryProductionResult.Refused(
                EuFormexAnnexInventoryRefusal.ProfileDigestMismatch,
                "the profile bytes do not carry the digest their reference names");
        }

        if (!TryReadProfile(profileBytes.Span, out var transportDigest, out var profileFailure))
        {
            return EuFormexAnnexInventoryProductionResult.Refused(
                EuFormexAnnexInventoryRefusal.ProfileInvalid, profileFailure!);
        }

        if (!string.Equals(
                transportDigest,
                retainedFormexBytes.Reference.ContentSha256,
                StringComparison.Ordinal))
        {
            return EuFormexAnnexInventoryProductionResult.Refused(
                EuFormexAnnexInventoryRefusal.ProfileDoesNotNameTransport,
                "the profile names different retained Formex bytes");
        }

        ReadOnlyMemory<byte> packageBytes;
        try
        {
            packageBytes = await CustodyRestore.ReadCheckedAsync(
                    _custodyStore, retainedFormexBytes.Reference, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is CustodyRequiredException
            or CustodyIntegrityException or CustodyPolicyException)
        {
            return EuFormexAnnexInventoryProductionResult.Refused(
                EuFormexAnnexInventoryRefusal.RetainedBytesUnavailable, exception.Message);
        }

        var parsed = ParsePackage(packageBytes, cancellationToken);
        if (parsed.Refusal != EuFormexAnnexInventoryRefusal.None)
        {
            return EuFormexAnnexInventoryProductionResult.Refused(parsed.Refusal, parsed.Detail!);
        }

        return EuFormexAnnexInventoryProductionResult.Success(
            new EuFormexAnnexInventory(retainedFormexBytes, profileRef, parsed.Members!));
    }

    private static (IReadOnlyList<EuFormexAnnexInventoryMember>? Members,
        EuFormexAnnexInventoryRefusal Refusal, string? Detail) ParsePackage(
        ReadOnlyMemory<byte> packageBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(packageBytes.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxEntries)
            {
                return Refused(EuFormexAnnexInventoryRefusal.PackageUnreadable,
                    "the Formex package exceeds the entry bound");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            long xmlBytes = 0;
            var units = new List<(string EntryName, XElement Root)>();
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsSafeEntryName(entry.FullName) || !names.Add(entry.FullName))
                {
                    return Refused(EuFormexAnnexInventoryRefusal.PackageEntryInvalid,
                        "the Formex package contains an unsafe or duplicate entry name");
                }

                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.Length < 0 || entry.Length > MaxXmlEntryBytes
                    || xmlBytes > MaxXmlPackageBytes - entry.Length)
                {
                    return Refused(EuFormexAnnexInventoryRefusal.PackageUnreadable,
                        "the Formex XML payload exceeds the admitted bound");
                }

                xmlBytes += entry.Length;
                XDocument document;
                try
                {
                    using var entryStream = entry.Open();
                    using var reader = XmlReader.Create(entryStream, new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersInDocument = MaxXmlEntryBytes,
                        IgnoreComments = true,
                    });
                    document = XDocument.Load(reader, LoadOptions.None);
                }
                catch (Exception exception) when (exception is InvalidDataException or XmlException)
                {
                    return Refused(EuFormexAnnexInventoryRefusal.XmlInvalid,
                        $"Formex XML entry {entry.FullName} is invalid");
                }

                if (document.Root is { } root)
                {
                    units.Add((entry.FullName, root));
                }
            }

            var documentEntries = units
                .Where(static unit =>
                    string.Equals(unit.Root.Name.LocalName, "DOC", StringComparison.Ordinal)
                    && IsFormexUnit(unit.Root))
                .Select(static unit => unit.EntryName)
                .ToHashSet(StringComparer.Ordinal);
            if (documentEntries.Count == 0)
            {
                return Refused(EuFormexAnnexInventoryRefusal.PackageDoesNotIdentifyFormex,
                    "the retained ZIP contains no Formex DOC unit");
            }

            var members = new List<EuFormexAnnexInventoryMember>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unit in units.Where(static unit =>
                         string.Equals(unit.Root.Name.LocalName, "ANNEX", StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsFormexUnit(unit.Root))
                {
                    return Refused(EuFormexAnnexInventoryRefusal.PackageDoesNotIdentifyFormex,
                        $"XML entry {unit.EntryName} claims ANNEX without a Formex schema");
                }

                var member = ParseAnnex(unit.EntryName, unit.Root);
                if (member.Refusal != EuFormexAnnexInventoryRefusal.None)
                {
                    return (null, member.Refusal, member.Detail);
                }

                if (!documentEntries.Contains(member.Member!.DocumentReferenceFile))
                {
                    return Refused(EuFormexAnnexInventoryRefusal.AnnexDocumentReferenceMissing,
                        $"Formex ANNEX entry {unit.EntryName} names no DOC member in the package");
                }

                var identity = member.Member!.DocumentReferenceFile + "\n" + member.Member.Sequence;
                if (!identities.Add(identity))
                {
                    return Refused(EuFormexAnnexInventoryRefusal.DuplicateAnnexIdentity,
                        "two Formex ANNEX units claim the same document reference and sequence");
                }

                members.Add(member.Member);
            }

            members.Sort(static (left, right) =>
            {
                var sequence = StringComparer.Ordinal.Compare(left.Sequence, right.Sequence);
                return sequence != 0
                    ? sequence
                    : StringComparer.Ordinal.Compare(left.PackageEntry, right.PackageEntry);
            });
            return (members, EuFormexAnnexInventoryRefusal.None, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return Refused(EuFormexAnnexInventoryRefusal.PackageUnreadable,
                "the retained bytes are not a readable Formex ZIP package");
        }
    }

    private static (EuFormexAnnexInventoryMember? Member,
        EuFormexAnnexInventoryRefusal Refusal, string? Detail) ParseAnnex(
        string entryName,
        XElement root)
    {
        var bibliography = SingleDirect(root, "BIB.INSTANCE");
        var documentReference = bibliography is null ? null : SingleDirect(bibliography, "DOCUMENT.REF");
        var documentFile = documentReference?.Attribute("FILE")?.Value.Trim();
        var documentValue = documentReference?.Value.Trim();
        if (string.IsNullOrWhiteSpace(documentFile) || !IsSafeEntryName(documentFile)
            || string.IsNullOrWhiteSpace(documentValue))
        {
            return AnnexRefused(EuFormexAnnexInventoryRefusal.AnnexDocumentReferenceMissing,
                $"Formex ANNEX entry {entryName} has no complete document reference");
        }

        var sequence = SingleDirect(bibliography!, "NO.SEQ")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(sequence))
        {
            return AnnexRefused(EuFormexAnnexInventoryRefusal.AnnexSequenceMissing,
                $"Formex ANNEX entry {entryName} has no sequence");
        }

        var firstText = SingleDirect(bibliography!, "PAGE.FIRST")?.Value.Trim();
        var lastText = SingleDirect(bibliography!, "PAGE.LAST")?.Value.Trim();
        var totalText = SingleDirect(bibliography!, "PAGE.TOTAL")?.Value.Trim();
        if (firstText is null || lastText is null || totalText is null)
        {
            return AnnexRefused(EuFormexAnnexInventoryRefusal.AnnexPageExtentMissing,
                $"Formex ANNEX entry {entryName} has no complete page extent");
        }

        if (!int.TryParse(firstText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(lastText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var last)
            || !int.TryParse(totalText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var total)
            || first <= 0 || last <= 0 || total <= 0
            || last < first)
        {
            return AnnexRefused(EuFormexAnnexInventoryRefusal.AnnexPageExtentInvalid,
                $"Formex ANNEX entry {entryName} has an invalid page extent");
        }

        if (last - first + 1 != total)
        {
            return AnnexRefused(EuFormexAnnexInventoryRefusal.AnnexPageExtentContradictory,
                $"Formex ANNEX entry {entryName} has a contradictory page extent");
        }

        var title = SingleDirect(root, "TITLE")?.Value.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            return AnnexRefused(EuFormexAnnexInventoryRefusal.AnnexTitleMissing,
                $"Formex ANNEX entry {entryName} has no publisher title");
        }

        return (new EuFormexAnnexInventoryMember(
            entryName, sequence, documentFile, documentValue,
            first, last, total, title), EuFormexAnnexInventoryRefusal.None, null);
    }

    private static XElement? SingleDirect(XElement parent, string localName)
    {
        using var matching = parent.Elements()
            .Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal))
            .Take(2)
            .GetEnumerator();
        if (!matching.MoveNext())
        {
            return null;
        }

        var value = matching.Current;
        return matching.MoveNext() ? null : value;
    }

    private static bool IsSafeEntryName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.StartsWith('/')
        && !name.StartsWith('\\')
        && !name.Contains('\\')
        && !name.Contains(':')
        && name.Split('/').All(static segment => segment is not ("" or "." or ".."));

    private static bool IsFormexUnit(XElement root)
    {
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var schema = root.Attribute(xsi + "noNamespaceSchemaLocation")?.Value;
        return schema is not null && schema.StartsWith(
            "http://formex.publications.europa.eu/schema/formex-",
            StringComparison.Ordinal);
    }

    private static bool TryReadProfile(
        ReadOnlySpan<byte> bytes,
        out string? transportDigest,
        out string? failure)
    {
        transportDigest = null;
        failure = null;
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            failure = "the profile is not UTF-8";
            return false;
        }

        var lines = text.Split('\n');
        if (lines.Length != 4 || lines[3].Length != 0
            || !string.Equals(lines[0], ProfileHeader, StringComparison.Ordinal)
            || !lines[1].StartsWith("transport_sha256=", StringComparison.Ordinal)
            || !string.Equals(lines[2], "annex_root=ANNEX", StringComparison.Ordinal))
        {
            failure = "the profile does not have the exact Formex annex inventory shape";
            return false;
        }

        transportDigest = lines[1]["transport_sha256=".Length..];
        if (!CustodyDigest.IsLowercaseSha256(transportDigest))
        {
            failure = "the profile transport digest is not lowercase SHA-256";
            return false;
        }

        return true;
    }

    private static (IReadOnlyList<EuFormexAnnexInventoryMember>? Members,
        EuFormexAnnexInventoryRefusal Refusal, string? Detail) Refused(
        EuFormexAnnexInventoryRefusal refusal,
        string detail) => (null, refusal, detail);

    private static (EuFormexAnnexInventoryMember? Member,
        EuFormexAnnexInventoryRefusal Refusal, string? Detail) AnnexRefused(
        EuFormexAnnexInventoryRefusal refusal,
        string detail) => (null, refusal, detail);
}

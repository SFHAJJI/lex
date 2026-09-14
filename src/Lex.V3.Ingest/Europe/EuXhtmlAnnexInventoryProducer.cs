using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why retained publisher XHTML did not prove an annex correspondence.</summary>
public enum EuXhtmlAnnexInventoryRefusal
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

    [JsonStringEnumMemberName("xhtml_invalid")]
    XhtmlInvalid = 5,

    [JsonStringEnumMemberName("publisher_annex_convention_absent")]
    PublisherAnnexConventionAbsent = 6,

    [JsonStringEnumMemberName("publisher_annex_convention_invalid")]
    PublisherAnnexConventionInvalid = 7,

    [JsonStringEnumMemberName("work_eli_missing")]
    WorkEliMissing = 8,
}

/// <summary>One annex correspondence asserted by the publisher's XHTML rendition.</summary>
public sealed record EuXhtmlAnnexInventoryMember(
    string PublisherUnitId,
    string FormexPackageEntry,
    string PublisherAnnexId,
    string Title);

/// <summary>The ordered publisher annex correspondences proven by one retained XHTML rendition.</summary>
public sealed class EuXhtmlAnnexInventory
{
    internal EuXhtmlAnnexInventory(
        DurableBlobWriteReceipt sourceReceipt,
        SourceArtifactRef profileRef,
        string workEli,
        IReadOnlyList<EuXhtmlAnnexInventoryMember> members)
    {
        SourceReceipt = sourceReceipt;
        ProfileRef = profileRef;
        WorkEli = workEli;
        Members = members.ToArray();
        IdentitySha256 = IdentityOf(sourceReceipt, profileRef, workEli, Members);
    }

    public DurableBlobWriteReceipt SourceReceipt { get; }

    public SourceArtifactRef ProfileRef { get; }

    public string WorkEli { get; }

    public IReadOnlyList<EuXhtmlAnnexInventoryMember> Members { get; }

    public string IdentitySha256 { get; }

    private static string IdentityOf(
        DurableBlobWriteReceipt receipt,
        SourceArtifactRef profileRef,
        string workEli,
        IReadOnlyList<EuXhtmlAnnexInventoryMember> members)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-eu-xhtml-annex-inventory/1");
        Append(hash, receipt.Reference.ContentSha256);
        Append(hash, profileRef.ResourceId);
        Append(hash, profileRef.Sha256);
        Append(hash, workEli);
        foreach (var member in members)
        {
            Append(hash, member.PublisherUnitId);
            Append(hash, member.FormexPackageEntry);
            Append(hash, member.PublisherAnnexId);
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

/// <summary>One publisher XHTML annex inventory, or one named refusal.</summary>
public sealed class EuXhtmlAnnexInventoryProductionResult
{
    private EuXhtmlAnnexInventoryProductionResult(
        EuXhtmlAnnexInventory? inventory,
        EuXhtmlAnnexInventoryRefusal refusal,
        string? detail)
    {
        Inventory = inventory;
        Refusal = refusal;
        Detail = detail;
    }

    public EuXhtmlAnnexInventory? Inventory { get; }

    public EuXhtmlAnnexInventoryRefusal Refusal { get; }

    public string? Detail { get; }

    public bool Produced => Refusal == EuXhtmlAnnexInventoryRefusal.None;

    internal static EuXhtmlAnnexInventoryProductionResult Success(EuXhtmlAnnexInventory inventory) =>
        new(inventory, EuXhtmlAnnexInventoryRefusal.None, null);

    internal static EuXhtmlAnnexInventoryProductionResult Refused(
        EuXhtmlAnnexInventoryRefusal refusal,
        string detail) => new(null, refusal, detail);
}

/// <summary>
/// Reopens retained publisher XHTML and extracts only explicit Formex-unit and annex identifiers.
/// It does not mint a structural-location token or associate PDF pages.
/// </summary>
public sealed class EuXhtmlAnnexInventoryProducer
{
    private const string ProfileHeader = "lex-v3-eu-xhtml-annex-inventory-profile/1";
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const int MaxCharacters = 16 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ICustodyStore _custodyStore;

    public EuXhtmlAnnexInventoryProducer(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<EuXhtmlAnnexInventoryProductionResult> RunAsync(
        DurableBlobWriteReceipt retainedXhtmlBytes,
        ReadOnlyMemory<byte> profileBytes,
        SourceArtifactRef profileRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retainedXhtmlBytes);
        ArgumentNullException.ThrowIfNull(profileRef);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(
                CustodyDigest.Of(profileBytes.Span, cancellationToken),
                profileRef.Sha256,
                StringComparison.Ordinal))
        {
            return EuXhtmlAnnexInventoryProductionResult.Refused(
                EuXhtmlAnnexInventoryRefusal.ProfileDigestMismatch,
                "the profile bytes do not carry the digest their reference names");
        }

        if (!TryReadProfile(profileBytes.Span, out var transportDigest, out var profileFailure))
        {
            return EuXhtmlAnnexInventoryProductionResult.Refused(
                EuXhtmlAnnexInventoryRefusal.ProfileInvalid, profileFailure!);
        }

        if (!string.Equals(
                transportDigest,
                retainedXhtmlBytes.Reference.ContentSha256,
                StringComparison.Ordinal))
        {
            return EuXhtmlAnnexInventoryProductionResult.Refused(
                EuXhtmlAnnexInventoryRefusal.ProfileDoesNotNameTransport,
                "the profile names different retained XHTML bytes");
        }

        ReadOnlyMemory<byte> xhtmlBytes;
        try
        {
            xhtmlBytes = await CustodyRestore.ReadCheckedAsync(
                    _custodyStore, retainedXhtmlBytes.Reference, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is CustodyRequiredException
            or CustodyIntegrityException or CustodyPolicyException)
        {
            return EuXhtmlAnnexInventoryProductionResult.Refused(
                EuXhtmlAnnexInventoryRefusal.RetainedBytesUnavailable, exception.Message);
        }

        var parsed = Parse(xhtmlBytes, cancellationToken);
        if (parsed.Refusal != EuXhtmlAnnexInventoryRefusal.None)
        {
            return EuXhtmlAnnexInventoryProductionResult.Refused(parsed.Refusal, parsed.Detail!);
        }

        return EuXhtmlAnnexInventoryProductionResult.Success(new EuXhtmlAnnexInventory(
            retainedXhtmlBytes, profileRef, parsed.WorkEli!, parsed.Members!));
    }

    private static (string? WorkEli, IReadOnlyList<EuXhtmlAnnexInventoryMember>? Members,
        EuXhtmlAnnexInventoryRefusal Refusal, string? Detail) Parse(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        if (bytes.Length > MaxCharacters)
        {
            return Refused(EuXhtmlAnnexInventoryRefusal.XhtmlInvalid,
                "the retained XHTML exceeds the admitted byte bound");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes.Span);
        }
        catch (DecoderFallbackException)
        {
            return Refused(EuXhtmlAnnexInventoryRefusal.XhtmlInvalid,
                "the retained XHTML is not UTF-8");
        }

        const string admittedDoctype =
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML//EN\" \"xhtml-strict.dtd\">";
        var doctypeStart = text.IndexOf("<!DOCTYPE", StringComparison.Ordinal);
        if (doctypeStart >= 0 && !text.AsSpan(doctypeStart).StartsWith(admittedDoctype, StringComparison.Ordinal))
        {
            return Refused(EuXhtmlAnnexInventoryRefusal.XhtmlInvalid,
                "the retained XHTML carries an unadmitted document type declaration");
        }

        XDocument document;
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = MaxCharacters,
                IgnoreComments = true,
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            return Refused(EuXhtmlAnnexInventoryRefusal.XhtmlInvalid,
                "the retained bytes are not admitted publisher XHTML");
        }

        cancellationToken.ThrowIfCancellationRequested();
        XNamespace xhtml = XhtmlNamespace;
        if (document.Root?.Name != xhtml + "html"
            || document.Root.Element(xhtml + "body") is not { } body)
        {
            return Refused(EuXhtmlAnnexInventoryRefusal.XhtmlInvalid,
                "the retained document does not have the XHTML root and body");
        }

        var wrappers = body.Elements(xhtml + "div")
            .Where(static element => element.Attribute("id")?.Value.EndsWith(
                ".fmx", StringComparison.Ordinal) == true)
            .ToArray();
        if (wrappers.Length == 0)
        {
            return Refused(EuXhtmlAnnexInventoryRefusal.PublisherAnnexConventionAbsent,
                "the retained XHTML carries no publisher Formex-unit annex wrapper");
        }

        var workElis = body.Elements(xhtml + "p")
            .Select(static element => element.Value.Trim())
            .Where(static value => value.StartsWith("ELI: ", StringComparison.Ordinal))
            .Select(static value => value["ELI: ".Length..])
            .Where(static value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                && string.Equals(uri.Host, "data.europa.eu", StringComparison.Ordinal)
                && uri.AbsolutePath.StartsWith("/eli/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (workElis.Length != 1)
        {
            return Refused(EuXhtmlAnnexInventoryRefusal.WorkEliMissing,
                "the retained XHTML does not name exactly one work ELI");
        }

        var members = new List<EuXhtmlAnnexInventoryMember>(wrappers.Length);
        var unitIds = new HashSet<string>(StringComparer.Ordinal);
        var annexIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wrapper in wrappers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unitId = wrapper.Attribute("id")!.Value;
            var annexes = wrapper.Elements(xhtml + "div")
                .Where(static element => HasClass(element, "eli-container"))
                .ToArray();
            if (!IsPublisherUnitId(unitId) || annexes.Length != 1
                || !IsPublisherAnnexId(annexes[0].Attribute("id")?.Value))
            {
                return Refused(EuXhtmlAnnexInventoryRefusal.PublisherAnnexConventionInvalid,
                    "a publisher Formex-unit annex wrapper is incomplete or malformed");
            }

            var annexId = annexes[0].Attribute("id")!.Value;
            var titles = annexes[0].Elements(xhtml + "p")
                .Where(static element => HasClass(element, "oj-doc-ti"))
                .Select(static element => element.Value.Trim())
                .Where(static value => value.Length > 0)
                .Take(2)
                .ToArray();
            if (titles.Length != 1 || !unitIds.Add(unitId) || !annexIds.Add(annexId))
            {
                return Refused(EuXhtmlAnnexInventoryRefusal.PublisherAnnexConventionInvalid,
                    "publisher annex identifiers or titles are absent or ambiguous");
            }

            members.Add(new EuXhtmlAnnexInventoryMember(
                unitId, unitId + ".xml", annexId, titles[0]));
        }

        members.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.PublisherAnnexId, right.PublisherAnnexId));
        return (workElis[0], members, EuXhtmlAnnexInventoryRefusal.None, null);
    }

    private static bool HasClass(XElement element, string value) =>
        (element.Attribute("class")?.Value ?? string.Empty)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Contains(value, StringComparer.Ordinal);

    private static bool IsPublisherUnitId(string value) =>
        value.EndsWith(".fmx", StringComparison.Ordinal)
        && value.Length > ".fmx".Length
        && value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsPublisherAnnexId(string? value) =>
        value is not null
        && value.StartsWith("anx_", StringComparison.Ordinal)
        && value.Length > "anx_".Length
        && value["anx_".Length..].All(static character => character is >= '0' and <= '9');

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
            || !string.Equals(lines[2], "xhtml_namespace=" + XhtmlNamespace, StringComparison.Ordinal))
        {
            failure = "the profile does not have the exact XHTML annex inventory shape";
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

    private static (string? WorkEli, IReadOnlyList<EuXhtmlAnnexInventoryMember>? Members,
        EuXhtmlAnnexInventoryRefusal Refusal, string? Detail) Refused(
        EuXhtmlAnnexInventoryRefusal refusal,
        string detail) => (null, null, refusal, detail);
}

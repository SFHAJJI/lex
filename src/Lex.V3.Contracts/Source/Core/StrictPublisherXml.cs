using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.XPath;

namespace Lex.V3.Contracts.Source.Core;

public enum StrictPublisherXmlFailure
{
    [JsonStringEnumMemberName("input_exceeds_limit")]
    InputExceedsLimit = 1,

    [JsonStringEnumMemberName("invalid_xml")]
    InvalidXml = 2,
}

/// <summary>
/// A reviewed publisher-XML byte ceiling. The private constructor prevents a caller from deriving
/// the trust boundary from the untrusted input it is meant to bound.
/// </summary>
public sealed class StrictPublisherXmlProfile
{
    private StrictPublisherXmlProfile(int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        MaximumBytes = maximumBytes;
    }

    /// <summary>The accepted Formex-package ceiling from OPS-EU-FORMAT.</summary>
    public static StrictPublisherXmlProfile EuFormexPackage { get; } = new(67_108_864);

    internal int MaximumBytes { get; }
}

/// <summary>A deterministic failure at the publisher XML trust boundary.</summary>
public sealed class StrictPublisherXmlException : Exception
{
    internal StrictPublisherXmlException(StrictPublisherXmlFailure failure)
        : base(FailureMessage(failure))
    {
        Failure = failure;
    }

    public StrictPublisherXmlFailure Failure { get; }

    private static string FailureMessage(StrictPublisherXmlFailure failure) => failure switch
    {
        StrictPublisherXmlFailure.InputExceedsLimit =>
            "Publisher XML exceeds its profile byte ceiling.",
        StrictPublisherXmlFailure.InvalidXml =>
            "Publisher XML is not one well-formed DTD-free XML document.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}

/// <summary>
/// Parses already-retained publisher bytes without fetching, selecting a member, assigning a role,
/// or granting publication authority. A reviewed source profile supplies the byte ceiling; the
/// untrusted document cannot supply or derive it. A successful parse returns only a read-only XML
/// data model.
/// </summary>
public static class StrictPublisherXml
{
    public const string Identity = "strict-publisher-xml/1";

    /// <summary>
    /// Parses one complete XML document. DTD processing is prohibited and no resolver is available.
    /// See https://learn.microsoft.com/dotnet/api/system.xml.xmlreadersettings?view=net-10.0.
    /// </summary>
    public static XPathDocument Parse(
        ReadOnlyMemory<byte> retainedBytes,
        StrictPublisherXmlProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (retainedBytes.Length > profile.MaximumBytes)
        {
            throw new StrictPublisherXmlException(
                StrictPublisherXmlFailure.InputExceedsLimit);
        }

        var bytes = retainedBytes.ToArray();

        var settings = new XmlReaderSettings
        {
            Async = false,
            CheckCharacters = true,
            CloseInput = true,
            ConformanceLevel = ConformanceLevel.Document,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            IgnoreWhitespace = false,
            ValidationType = ValidationType.None,
            XmlResolver = null,
        };

        try
        {
            using var source = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(source, settings);
            return new XPathDocument(reader, XmlSpace.Preserve);
        }
        catch (XmlException)
        {
            throw new StrictPublisherXmlException(StrictPublisherXmlFailure.InvalidXml);
        }
    }
}

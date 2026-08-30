using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Lex.V3.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HeldRecordCandidate
{
    [JsonConstructor]
    public HeldRecordCandidate(
        string identifier,
        string title,
        PublisherId publisher,
        Uri permalink)
    {
        Identifier = ContractValidation.RequireIdentifier(identifier, nameof(identifier));
        Title = ContractValidation.RequireIdentifier(title, nameof(title));
        Publisher = publisher;
        Permalink = RequireHttps(permalink, nameof(permalink));
    }

    public string Identifier { get; }

    public string Title { get; }

    public PublisherId Publisher { get; }

    public Uri Permalink { get; }

    internal static Uri RequireHttps(Uri value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!value.IsAbsoluteUri || !string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("Contract links must be absolute HTTPS URIs.", parameterName);
        }

        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublisherSearchAction
{
    [JsonConstructor]
    public PublisherSearchAction(PublisherId publisher, Uri uri)
    {
        Publisher = publisher;
        Uri = HeldRecordCandidate.RequireHttps(uri, nameof(uri));

        var expectedHost = publisher switch
        {
            PublisherId.LuLegilux => "legilux.public.lu",
            PublisherId.EuEurLex => "eur-lex.europa.eu",
            _ => throw new ArgumentOutOfRangeException(nameof(publisher)),
        };

        if (!string.Equals(Uri.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The search action must point to its official publisher host.", nameof(uri));
        }
    }

    public PublisherId Publisher { get; }

    public Uri Uri { get; }

    public static PublisherSearchAction Create(PublisherId publisher, Uri uri) => new(publisher, uri);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentifierUnknownRefusal
{
    [JsonConstructor]
    public IdentifierUnknownRefusal(
        RefusalCode code,
        IdentifierFamily checkedIdentifierFamily,
        string requestedCoordinate,
        IReadOnlyList<PublisherId> publisherContextsChecked,
        IReadOnlyList<HeldRecordCandidate> possibleHeldRecords,
        IReadOnlyList<PublisherSearchAction> officialSearchActions,
        IReadOnlyList<WhatWouldAnswerAction> whatWouldAnswer,
        bool assertsAbsenceOfLaw)
    {
        if (code != RefusalCode.IdentifierUnknown)
        {
            throw new ArgumentException("The payload code must be identifier_unknown.", nameof(code));
        }

        if (assertsAbsenceOfLaw)
        {
            throw new ArgumentException("An identifier refusal cannot assert absence of law.", nameof(assertsAbsenceOfLaw));
        }

        Code = code;
        CheckedIdentifierFamily = checkedIdentifierFamily;
        RequestedCoordinate = ContractValidation.RequireIdentifier(requestedCoordinate, nameof(requestedCoordinate));
        PublisherContextsChecked = RequireDistinctNonEmpty(
            publisherContextsChecked,
            nameof(publisherContextsChecked));
        PossibleHeldRecords = Array.AsReadOnly(
            (possibleHeldRecords ?? throw new ArgumentNullException(nameof(possibleHeldRecords))).ToArray());
        OfficialSearchActions = RequireDistinctNonEmpty(
            officialSearchActions,
            nameof(officialSearchActions));
        WhatWouldAnswer = RequireDistinctNonEmpty(whatWouldAnswer, nameof(whatWouldAnswer));
        AssertsAbsenceOfLaw = false;
    }

    public RefusalCode Code { get; }

    public IdentifierFamily CheckedIdentifierFamily { get; }

    public string RequestedCoordinate { get; }

    public IReadOnlyList<PublisherId> PublisherContextsChecked { get; }

    public IReadOnlyList<HeldRecordCandidate> PossibleHeldRecords { get; }

    public IReadOnlyList<PublisherSearchAction> OfficialSearchActions { get; }

    public IReadOnlyList<WhatWouldAnswerAction> WhatWouldAnswer { get; }

    public bool AssertsAbsenceOfLaw { get; }

    public static IdentifierUnknownRefusal Create(
        IdentifierFamily checkedIdentifierFamily,
        string requestedCoordinate,
        IReadOnlyList<PublisherId> publisherContextsChecked,
        IReadOnlyList<HeldRecordCandidate> possibleHeldRecords,
        IReadOnlyList<PublisherSearchAction> officialSearchActions,
        IReadOnlyList<WhatWouldAnswerAction> whatWouldAnswer) =>
        new(
            RefusalCode.IdentifierUnknown,
            checkedIdentifierFamily,
            requestedCoordinate,
            publisherContextsChecked,
            possibleHeldRecords,
            officialSearchActions,
            whatWouldAnswer,
            assertsAbsenceOfLaw: false);

    private static ReadOnlyCollection<T> RequireDistinctNonEmpty<T>(
        IReadOnlyList<T> values,
        string parameterName)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToArray();
        if (copy.Length == 0)
        {
            throw new ArgumentException("The collection must not be empty.", parameterName);
        }

        if (copy.Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("The collection must not contain duplicates.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}

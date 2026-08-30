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
        PublisherId publisher)
    {
        Publisher = ContractValidation.RequireDefined(publisher, nameof(publisher));
        Identifier = ContractValidation.RequireHeldRecordIdentifier(
            Publisher,
            identifier,
            nameof(identifier));
        Title = ContractValidation.RequireDisplayTitle(title, nameof(title));
    }

    public string Identifier { get; }

    public string Title { get; }

    public PublisherId Publisher { get; }

}

internal static class PreviewOfficialPublisherLinks
{
    public const string LuSearch = "https://legilux.public.lu/search";
    public const string EuSearch = "https://eur-lex.europa.eu/advanced-search-form.html";

    public static Uri Search(PublisherId publisher) => publisher switch
    {
        PublisherId.LuLegilux => new Uri(LuSearch, UriKind.Absolute),
        PublisherId.EuEurLex => new Uri(EuSearch, UriKind.Absolute),
        _ => throw new ArgumentOutOfRangeException(nameof(publisher)),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublisherSearchAction
{
    [JsonConstructor]
    public PublisherSearchAction(string kind, PublisherId publisher, Uri uri)
    {
        if (!string.Equals(kind, "publisher_search", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Preview publisher actions must use the publisher_search kind.",
                nameof(kind));
        }

        Kind = kind;
        Publisher = ContractValidation.RequireDefined(publisher, nameof(publisher));
        ArgumentNullException.ThrowIfNull(uri);
        var expected = PreviewOfficialPublisherLinks.Search(publisher);

        if (!string.Equals(uri.OriginalString, expected.AbsoluteUri, StringComparison.Ordinal) ||
            !string.Equals(uri.AbsoluteUri, expected.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Publisher search actions are fixed generic official links and carry no user input.",
                nameof(uri));
        }

        Uri = expected;
    }

    public string Kind { get; }

    public PublisherId Publisher { get; }

    public Uri Uri { get; }

    public static PublisherSearchAction Create(PublisherId publisher) =>
        new("publisher_search", publisher, PreviewOfficialPublisherLinks.Search(publisher));
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
        CheckedIdentifierFamily = ContractValidation.RequireDefined(
            checkedIdentifierFamily,
            nameof(checkedIdentifierFamily));
        RequestedCoordinate = ContractValidation.RequireRequestedCoordinate(
            checkedIdentifierFamily,
            requestedCoordinate,
            nameof(requestedCoordinate));
        PublisherContextsChecked = RequireDistinctNonEmpty(
            publisherContextsChecked,
            nameof(publisherContextsChecked));
        foreach (var publisher in PublisherContextsChecked)
        {
            ContractValidation.RequireDefined(publisher, nameof(publisherContextsChecked));
        }

        if (PublisherContextsChecked.Count == 2 &&
            (PublisherContextsChecked[0] != PublisherId.LuLegilux ||
             PublisherContextsChecked[1] != PublisherId.EuEurLex))
        {
            throw new ArgumentException(
                "Checked publisher contexts must use canonical LU then EU ordering.",
                nameof(publisherContextsChecked));
        }

        var candidateCopy = (possibleHeldRecords ?? throw new ArgumentNullException(nameof(possibleHeldRecords)))
            .ToArray();
        if (candidateCopy.Any(static candidate => candidate is null) ||
            candidateCopy
                .Select(static candidate => (candidate.Publisher, candidate.Identifier))
                .Distinct()
                .Count() != candidateCopy.Length)
        {
            throw new ArgumentException(
                "Held-record candidates must be non-null and unique by publisher and identifier.",
                nameof(possibleHeldRecords));
        }


        if (!candidateCopy.SequenceEqual(candidateCopy
                .OrderBy(static candidate => candidate.Publisher)
                .ThenBy(static candidate => candidate.Identifier, StringComparer.Ordinal)))
        {
            throw new ArgumentException(
                "Held-record candidates must use publisher then identifier order.",
                nameof(possibleHeldRecords));
        }

        PossibleHeldRecords = Array.AsReadOnly(candidateCopy);
        OfficialSearchActions = RequireDistinctNonEmpty(
            officialSearchActions,
            nameof(officialSearchActions));
        WhatWouldAnswer = RequireDistinctNonEmpty(whatWouldAnswer, nameof(whatWouldAnswer));
        foreach (var action in WhatWouldAnswer)
        {
            ContractValidation.RequireDefined(action, nameof(whatWouldAnswer));
        }

        if (!WhatWouldAnswer.SequenceEqual(WhatWouldAnswer.OrderBy(static action => action)))
        {
            throw new ArgumentException(
                "What-would-answer actions must use declared enum order.",
                nameof(whatWouldAnswer));
        }

        if (PossibleHeldRecords.Any(candidate => !PublisherContextsChecked.Contains(candidate.Publisher)) ||
            !OfficialSearchActions
                .Select(static action => action.Publisher)
                .SequenceEqual(PublisherContextsChecked))
        {
            throw new ArgumentException(
                "Candidate records and official actions must be bound to the checked publishers.",
                nameof(officialSearchActions));
        }

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
        if (copy.Length == 0 || copy.Any(static value => value is null))
        {
            throw new ArgumentException("The collection must be non-empty and contain no null.", parameterName);
        }

        if (copy.Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("The collection must not contain duplicates.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}

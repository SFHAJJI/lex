using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

/// <summary>
/// One ordered parameter a document-fetch GET plan binds onto its own
/// <see cref="MachineQueryInputArtifact"/>, and the outbound request header (if any) that
/// parameter's value fills.
/// </summary>
/// <param name="ParameterName">
/// The exact <see cref="MachineQueryParameter.Name"/> the plan mints and the session verifies.
/// </param>
/// <param name="HeaderName">
/// The lowercase outbound header this parameter's text value becomes, or null when the parameter
/// is carried for a reason other than a request header. The Luxembourg route's own single
/// parameter is the second kind: it carries the act's ELI page path, which is a robots-evaluation
/// input and never a header, because that route negotiates nothing.
/// </param>
public sealed record DocumentFetchParameter(string ParameterName, string? HeaderName);

/// <summary>
/// The ordered parameter contract one document-fetch GET route declares. D1-06c-LU-2 fold-in three
/// (READY verdict lex-event-20260904T175623600Z-7d8ea851a9a54278b97e1eb33a0af29e, design ruled in
/// SCOPE_RULING lex-event-20260904T173606578Z-44305cbdf86043ae9a5a502282aebcd5): before this type,
/// <c>RoutedHttpAcquisitionSession.CreateDocumentFetchRequest</c> hardcoded the EU route's own two
/// parameter-name literals, so the one generic GET mechanism was EU shaped and a second publisher
/// could only be added by a second parallel branch. The ruling refused that duplication: the route
/// that binds a GET declares its own expected ordered parameter names, and the session verifies the
/// reopened input against that declaration alone.
/// </summary>
/// <remarks>
/// The declaration lives here, in the route layer, rather than as a member of each publisher's own
/// plan class, for one mechanical reason: the session never holds a plan instance. It resolves an
/// <see cref="OfficialMachineQuerySourceProfile"/> from the reopened request's own URI and has
/// nothing else to key on. Putting the table here and having each plan build its parameters FROM
/// it keeps one declaration rather than two that can drift, and keeps the dependency pointing the
/// way it already points (<c>Source/Europe</c> and <c>Source/Luxembourg</c> use
/// <c>Source/Http</c>), which a per-plan static member would have inverted.
/// </remarks>
public sealed class DocumentFetchParameterContract
{
    private DocumentFetchParameterContract(IReadOnlyList<DocumentFetchParameter> parameters)
    {
        Parameters = parameters;
    }

    /// <summary>The exact ordered parameters this route's bound input must carry, and only those.</summary>
    public IReadOnlyList<DocumentFetchParameter> Parameters { get; }

    /// <summary>
    /// The EU Cellar dissemination route: two observed content-negotiation headers, carried on the
    /// bound input because they are headers rather than request-target text and so cannot be
    /// recovered from the requested URI (see
    /// <c>Lex.V3.Contracts.Source.Europe.EuDocumentFetchPlan</c>'s own remarks).
    /// </summary>
    public static DocumentFetchParameterContract EuropeanUnionDocumentFetch { get; } = new(
    [
        new DocumentFetchParameter("eu_document_fetch_accept", "accept"),
        new DocumentFetchParameter("eu_document_fetch_accept_language", "accept-language"),
    ]);

    /// <summary>
    /// The Legilux filestore route: no negotiation at all. It fetches one exact filestore URI and
    /// the format is decided by which file that URI names, so this route declares no header
    /// parameter. Its one declared parameter is the act's own ELI page path, which RULING
    /// lex-event-20260904T180444431Z-13c6f8f86ddf4f02857cf4001c202143 makes a required third robots
    /// path for every Luxembourg manifestation; it is store-derived (manifestation to expression to
    /// work), cannot be derived from the filestore path, and is carried here so it travels inside
    /// the bound request's own retained canonical bytes rather than only as a call argument.
    /// </summary>
    public static DocumentFetchParameterContract LuxembourgDocumentFetch { get; } = new(
    [
        new DocumentFetchParameter("lu_document_fetch_act_eli_page_path", HeaderName: null),
    ]);

    /// <summary>
    /// The declaration for one document-fetch profile, or null for a profile that is not a
    /// document-fetch route at all (both SPARQL POST channels).
    /// </summary>
    public static DocumentFetchParameterContract? For(OfficialMachineQuerySourceProfileId id) => id switch
    {
        OfficialMachineQuerySourceProfileId.EuropeanUnionDocumentFetch => EuropeanUnionDocumentFetch,
        OfficialMachineQuerySourceProfileId.LuxembourgDocumentFetch => LuxembourgDocumentFetch,
        OfficialMachineQuerySourceProfileId.LuxembourgSparql => null,
        OfficialMachineQuerySourceProfileId.EuropeanUnionSparql => null,
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    /// <summary>
    /// Verifies one reopened ordered-parameter set against this declaration and returns the text
    /// values in declared order. Every parameter must be present, in the declared position, under
    /// the declared name, carrying a text value. Returns false rather than throwing so the caller
    /// decides what an unverifiable input means for its own route.
    /// </summary>
    public bool TryReadDeclaredValues(
        MachineQueryInputArtifact input,
        out IReadOnlyList<string> orderedValues)
    {
        ArgumentNullException.ThrowIfNull(input);
        orderedValues = [];
        if (input.OrderedParameters.Count != Parameters.Count)
        {
            return false;
        }

        var values = new string[Parameters.Count];
        for (var index = 0; index < Parameters.Count; index++)
        {
            var observed = input.OrderedParameters[index];
            if (!string.Equals(observed.Name, Parameters[index].ParameterName, StringComparison.Ordinal) ||
                observed.TextValue is not { } textValue)
            {
                return false;
            }

            values[index] = textValue;
        }

        orderedValues = values;
        return true;
    }
}

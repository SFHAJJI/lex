using System.Runtime.CompilerServices;
using Lex.Law;

namespace Lex.Sources.EurLex;

/// <summary>
/// EUR-Lex / Cellar adapter (Tier A). Text reuse is permitted with attribution under
/// Commission Decision 2011/833/EU — this is the text-bearing publisher of increment B.
/// Increment A ships the adapter seam; the CDM queries land with increment B.
/// </summary>
public sealed class EurLexAdapter : ISourceAdapter
{
    public PublisherDescriptor Describe() => new(
        new Publisher(
            Id: "eu-eurlex",
            Name: "Publications Office of the EU (EUR-Lex / Cellar)",
            Jurisdiction: "EU",
            Homepage: "https://eur-lex.europa.eu",
            Tier: Tier.A,
            Attribution: "© European Union, https://eur-lex.europa.eu. Reuse permitted under Commission Decision 2011/833/EU.",
            SourceTermsUrl: "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html"),
        DocumentTypes: [],
        Languages: ["fr", "de", "en"],
        TextIncluded: true,
        HistoryBegins: "publisher");

    public async IAsyncEnumerable<WorkRef> EnumerateWorks([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break; // increment B
    }

    public Task<IReadOnlyList<VersionRecord>> FetchVersions(WorkRef work, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<VersionRecord>>([]);

    public Task<string?> FetchBody(VersionRecord version, ExpressionRecord expression, CancellationToken ct)
        => Task.FromResult<string?>(null);
}

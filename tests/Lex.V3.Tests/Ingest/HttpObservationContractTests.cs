using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Ingest;

[TestClass]
public sealed class HttpObservationContractTests
{
    [TestMethod]
    public void StatusClassificationExhaustivelyKeepsFramingSeparateFromDerivability()
    {
        for (var status = 100; status <= 599; status++)
        {
            foreach (var hasContentRange in new[] { false, true })
            {
                var expected = status == 300
                    ? HttpStatusDisposition.NegotiationChoiceOffered
                    : status == 206 || hasContentRange
                        ? HttpStatusDisposition.RangeNotApproved
                        : status switch
                        {
                            200 => HttpStatusDisposition.DerivableStatus,
                            301 or 302 or 303 or 307 or 308 =>
                                HttpStatusDisposition.RedirectObserved,
                            304 => HttpStatusDisposition.RevalidationReferenceOnly,
                            204 or 205 => HttpStatusDisposition.SemanticNoEntityStatus,
                            _ => HttpStatusDisposition.NonDerivableStatus,
                        };

                Assert.AreEqual(
                    expected,
                    HttpStatusClassifier.Classify(status, hasContentRange),
                    $"status={status}, content-range={hasContentRange}");
            }
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            HttpStatusClassifier.Classify(99, hasContentRange: false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            HttpStatusClassifier.Classify(600, hasContentRange: false));
    }

    [TestMethod]
    public void StatusDispositionsAreExactlyTheClosedVocabulary()
    {
        (HttpStatusDisposition Value, int Number, string Symbol, string Wire)[] expected =
        [
            (HttpStatusDisposition.DerivableStatus, 1, nameof(HttpStatusDisposition.DerivableStatus), "derivable_status"),
            (HttpStatusDisposition.RedirectObserved, 2, nameof(HttpStatusDisposition.RedirectObserved), "redirect_observed"),
            (HttpStatusDisposition.RevalidationReferenceOnly, 3, nameof(HttpStatusDisposition.RevalidationReferenceOnly), "revalidation_reference_only"),
            (HttpStatusDisposition.SemanticNoEntityStatus, 4, nameof(HttpStatusDisposition.SemanticNoEntityStatus), "semantic_no_entity_status"),
            (HttpStatusDisposition.RangeNotApproved, 5, nameof(HttpStatusDisposition.RangeNotApproved), "range_not_approved"),
            (HttpStatusDisposition.NonDerivableStatus, 6, nameof(HttpStatusDisposition.NonDerivableStatus), "non_derivable_status"),
            (HttpStatusDisposition.NegotiationChoiceOffered, 7, nameof(HttpStatusDisposition.NegotiationChoiceOffered), "negotiation_choice_offered"),
        ];

        CollectionAssert.AreEqual(
            expected.Select(static row => row.Symbol).ToArray(),
            Enum.GetNames<HttpStatusDisposition>());
        CollectionAssert.AreEqual(
            expected.Select(static row => row.Number).ToArray(),
            Enum.GetValues<HttpStatusDisposition>()
                .Select(static value => (int)value)
                .ToArray());
        Assert.IsFalse(Enum.IsDefined((HttpStatusDisposition)0));

        foreach (var (value, _, _, wire) in expected)
        {
            Assert.AreEqual($"\"{wire}\"", ContractJson.Serialize(value));
            Assert.AreEqual(
                value,
                ContractJson.Deserialize<HttpStatusDisposition>($"\"{wire}\""));
        }

        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<HttpStatusDisposition>("1"));
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<HttpStatusDisposition>("\"DerivableStatus\""));
        Assert.ThrowsExactly<JsonException>(() =>
            ContractJson.Deserialize<HttpStatusDisposition>("\"unknown\""));
    }

    [TestMethod]
    public void OutboundCrawlerIdentityIsOneExactNonOverrideableToken()
    {
        Assert.AreEqual(
            "Lex/0.1 (+https://github.com/SFHAJJI/lex)",
            OutboundCrawlerIdentity.Token);
        Assert.AreEqual("outbound_crawler_identity/1", OutboundCrawlerIdentity.Schema);

        foreach (var name in new[]
                 {
                     nameof(OutboundCrawlerIdentity.Schema),
                     nameof(OutboundCrawlerIdentity.Token),
                 })
        {
            var property = typeof(OutboundCrawlerIdentity).GetProperty(name);
            Assert.IsNotNull(property);
            Assert.IsNull(property.SetMethod);
        }
    }
}

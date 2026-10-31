using System.Text.Json;
using Json.Schema;
using Lex.V3.Contracts.Platform.Assistant;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The handoff registry (R42): a versioned data file that holds its schema, only the entries the specification or the
/// repository already names, every one "not yet reviewed by a person", no adviser, and no invented cost, limit, language or
/// contact detail.
/// </summary>
[TestClass]
public sealed class HandoffRegistryTests
{
    [TestMethod]
    public void TheCommittedFileHoldsItsSchema()
    {
        var schema = JsonSchema.FromText(System.Text.Encoding.UTF8.GetString(V3HandoffRegistry.SchemaUtf8.Span));
        using var document = JsonDocument.Parse(V3HandoffRegistry.Utf8);
        var result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.IsTrue(result.IsValid, "handoff-registry.json does not satisfy handoff-registry.schema.json");
        Assert.AreEqual(V3HandoffRegistry.Schema, document.RootElement.GetProperty("schema").GetString());
        Assert.AreEqual(1, V3HandoffRegistry.Version);

        // The schema can fail: an invented role is refused.
        var invented = System.Text.Encoding.UTF8.GetString(V3HandoffRegistry.Utf8.Span).Replace("\"role\": \"information\"", "\"role\": \"lawyer\"", StringComparison.Ordinal);
        using var mutated = JsonDocument.Parse(invented);
        Assert.IsFalse(schema.Evaluate(mutated.RootElement).IsValid, "the deliberate failure: a role outside the closed set");
    }

    [TestMethod]
    public void ItHoldsOnlyTheNamedEntriesEachNotYetReviewedAndNoAdviser()
    {
        CollectionAssert.AreEqual(
            new[] { "legilux", "eur-lex", "guichet-lu", "cnpd" },
            V3HandoffRegistry.Entries.Select(static entry => entry.Id).ToArray(),
            "Legilux, EUR-Lex as a link, guichet.lu and the CNPD as where to complain: the entries the specification names");
        foreach (var entry in V3HandoffRegistry.Entries)
        {
            Assert.AreEqual(V3HandoffEntry.NotYetReviewed, entry.Review, entry.Id);
            Assert.IsFalse(entry.ReviewedByPerson, entry.Id);
            Assert.IsNull(entry.Cost, $"{entry.Id}: no cost is invented");
            Assert.IsNull(entry.Limits, $"{entry.Id}: no limit is invented");
            Assert.IsNull(entry.Languages, $"{entry.Id}: no language is invented");
            Assert.IsNull(entry.LinkCheckedOn, $"{entry.Id}: no link check has run");
            StringAssert.StartsWith(entry.Link, "https://", entry.Id);
        }

        Assert.IsFalse(V3HandoffRegistry.ReviewedByPerson);
        Assert.AreEqual(0, V3HandoffRegistry.OfRole(V3HandoffRole.Advice).Count, "no adviser is named before a person writes and reviews one");
        Assert.AreEqual("where to complain", V3HandoffRegistry.Get("cnpd").IntroducedAs, "R42: the CNPD is introduced as where to complain");
        Assert.AreEqual(V3HandoffRole.Complaint, V3HandoffRegistry.Get("cnpd").Role);
        Assert.AreEqual("https://eur-lex.europa.eu/", V3HandoffRegistry.Get("eur-lex").Link, "a link only: nothing is sent to it (D83)");
        Assert.AreEqual(V3HandoffRole.OfficialSource, V3HandoffRegistry.Get("legilux").Role);
        Assert.AreEqual(V3HandoffRole.Information, V3HandoffRegistry.Get("guichet-lu").Role);
    }
}

using System;
using System.Linq;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

/// <summary>
/// The event names the pipeline may mint are the Stage 4 closed event registry less exactly one name.
/// The registry is spelled here in full (tests are outside the scope-line sweep of production source),
/// so the code's twelve are checked against the contract's thirteen rather than against themselves.
/// </summary>
[TestClass]
public sealed class V3EventRegistryTests
{
    /// <summary>Issue #348's frozen body, in its own order: "The closed event registry explicitly names ...".</summary>
    private static readonly string[] StageFourRegistry =
    [
        "first_sighting",
        "resighted",
        "metadata_revised",
        "validity_revised",
        "interval_closed",
        "withdrawn_from_source",
        "expression_added",
        "file_replaced",
        "relation_asserted",
        "relation_retracted",
        "future_state_scheduled",
        "future_state_activated",
        "coverage_changed",
    ];

    /// <summary>B42 finding 5.2: the gate must not be excused by an event the same pipeline mints.</summary>
    private const string NeverMinted = "coverage_changed";

    [TestMethod]
    public void TheMintableNamesAreTheRegistryLessTheSelfExcusingCoverageEventInTheRegistrysOrder()
    {
        CollectionAssert.AreEqual(
            StageFourRegistry.Where(static name => name != NeverMinted).ToArray(),
            V3EventRegistry.Mintable.ToArray(),
            "the mintable names drifted from the Stage 4 registry; admitting the coverage event is the owner's ruling.");
        CollectionAssert.DoesNotContain(V3EventRegistry.Mintable.ToArray(), NeverMinted);
    }

    [TestMethod]
    public void TheRevisingEventsAreTheTwoTheSpecEnumeratesDriftFromAndBothAreMintable()
    {
        CollectionAssert.AreEqual(new[] { "validity_revised", "interval_closed" }, V3EventRegistry.Revising.ToArray());
        foreach (var name in V3EventRegistry.Revising)
        {
            CollectionAssert.Contains(V3EventRegistry.Mintable.ToArray(), name);
        }
    }

    [TestMethod]
    public void TheEventsRequestAdmitsExactlyTheMintableNames()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(V3PlatformSchemaExporter.ExportRequestUtf8("events"));
        var admitted = schema.RootElement.GetProperty("properties").GetProperty("parameters")
            .GetProperty("properties").GetProperty("event").GetProperty("enum")
            .EnumerateArray().Select(static value => value.GetString()).ToArray();
        CollectionAssert.AreEqual(V3EventRegistry.Mintable.ToArray(), admitted);
    }
}

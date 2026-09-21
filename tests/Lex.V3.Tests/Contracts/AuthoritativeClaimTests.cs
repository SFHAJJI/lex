using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// S4-A04's binding rule, tested: a claim is a fixed template bound to typed facts, rendered by
/// substitution, and there is no way to put a sentence into one.
/// </summary>
/// <remarks>
/// <para>
/// The clause says authoritative claims are <i>"fixed templates bound to typed facts"</i> with
/// <i>"model gloss … disabled"</i>, and specifies no shape. These tests hold the shape proposed in
/// <see cref="V3AuthoritativeClaim"/> at the strength the clause states it: the set is closed, both
/// directions of the binding refuse, rendering is substitution and nothing else, and <b>the type
/// offers no path that takes free text</b>.
/// </para>
/// <para>
/// <b>The last one is the load-bearing test and it is the hardest to write honestly.</b> "No model
/// gloss" is a claim about everything the API does not let you do, which is a quantifier over an open
/// set. It is pinned here the only way it can be: over the type's own public surface, read by
/// reflection, requiring that every way to obtain a claim goes through the template set.
/// </para>
/// </remarks>
[TestClass]
public sealed class AuthoritativeClaimTests
{
    /// <summary>
    /// The templates the closed set holds, named here independently of the set, so removing one from
    /// the set fails rather than removing its own assertion with it.
    /// </summary>
    private static readonly string[] ExpectedTemplates =
        ["no_state_for_date", "publisher_reference", "state_interval", "text_on_date"];

    [TestMethod]
    public void TheTemplateSetIsExactlyTheOnesNamedHere()
    {
        CollectionAssert.AreEqual(
            ExpectedTemplates,
            V3ClaimTemplates.All.Select(static template => template.TemplateId)
                .OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
            "The claim template set has changed. It is closed the way the refusal registry is closed: "
            + "a template added or removed changes what this product is able to assert, so it is a "
            + "reviewed change and not a string written at a call site.");

        foreach (var id in ExpectedTemplates)
        {
            Assert.IsTrue(V3ClaimTemplates.IsDefined(id), $"{id} is named here and not in the set.");
        }
    }

    [TestMethod]
    public void EveryTemplateNamesAtLeastOnePlaceholderAndEachOneExactlyOnceInItsList()
    {
        foreach (var template in V3ClaimTemplates.All)
        {
            Assert.IsNotEmpty(
                template.Placeholders,
                $"{template.TemplateId} binds no typed fact, so it says the same thing about every "
                + "instrument, which is not a claim about one.");
            CollectionAssert.AllItemsAreUnique(
                template.Placeholders.ToArray(),
                $"{template.TemplateId} lists a placeholder twice.");

            var inText = Regex.Matches(template.Text, @"\{([a-z][a-z0-9_]*)\}")
                .Select(static match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                inText,
                template.Placeholders.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                $"{template.TemplateId}'s placeholder list is not what its text names.");
        }
    }

    [TestMethod]
    public void ARenderedClaimIsTheTemplateWithEachPlaceholderReplacedAndNothingElse()
    {
        var claim = V3AuthoritativeClaim.Bind(V3ClaimTemplates.NoStateForDate,
        [
            V3TypedFact.Of("work", "work_key", "loi-1991-08-10-n3"),
            V3TypedFact.Of("date", "calendar_date", "2021-03-15"),
            V3TypedFact.Of("nearest_date", "calendar_date", "2024-02-01"),
        ]);

        Assert.AreEqual(
            "This index holds no state of loi-1991-08-10-n3 for 2021-03-15; the nearest it holds is "
            + "2024-02-01, and that is what this index holds rather than what exists.",
            claim.Rendered);

        // Substitution and nothing else: no brace at all, and every value is present. Asked as "no brace"
        // rather than "no placeholder the product's pattern matches", because a test that re-runs the
        // product's own regex cannot see a marker that regex is wrong about.
        Assert.IsFalse(claim.Rendered.Contains('{', StringComparison.Ordinal),
            "a rendered claim carries an opening brace");
        Assert.IsFalse(claim.Rendered.Contains('}', StringComparison.Ordinal),
            "a rendered claim carries a closing brace");
        foreach (var fact in claim.Facts)
        {
            StringAssert.Contains(claim.Rendered, fact.Value, $"{fact.Name}'s value is not in the text");
        }

        // The facts come back in the template's placeholder order, so a reader can follow the sentence.
        CollectionAssert.AreEqual(
            V3ClaimTemplates.Get(V3ClaimTemplates.NoStateForDate).Placeholders.ToArray(),
            claim.Facts.Select(static fact => fact.Name).ToArray());
    }

    [TestMethod]
    public void AClaimWithAPlaceholderNobodyBoundRefusesRatherThanRenderingAHole()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() =>
            V3AuthoritativeClaim.Bind(V3ClaimTemplates.NoStateForDate,
            [
                V3TypedFact.Of("work", "work_key", "loi-1991-08-10-n3"),
                V3TypedFact.Of("date", "calendar_date", "2021-03-15"),
            ]));

        StringAssert.Contains(thrown.Message, "nearest_date");
        StringAssert.Contains(thrown.Message, "unbound");
    }

    [TestMethod]
    public void AClaimCarryingAFactItsWordingNeverStatesRefuses()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() =>
            V3AuthoritativeClaim.Bind(V3ClaimTemplates.NoStateForDate,
            [
                V3TypedFact.Of("work", "work_key", "loi-1991-08-10-n3"),
                V3TypedFact.Of("date", "calendar_date", "2021-03-15"),
                V3TypedFact.Of("nearest_date", "calendar_date", "2024-02-01"),
                V3TypedFact.Of("court_opinion", "prose", "the court would likely find"),
            ]));

        StringAssert.Contains(thrown.Message, "court_opinion");
        StringAssert.Contains(thrown.Message, "never");
    }

    [TestMethod]
    public void TwoFactsForOnePlaceholderRefuseRatherThanOneWinning()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() =>
            V3AuthoritativeClaim.Bind(V3ClaimTemplates.NoStateForDate,
            [
                V3TypedFact.Of("work", "work_key", "loi-1991-08-10-n3"),
                V3TypedFact.Of("work", "work_key", "loi-1984-02-24-n1"),
                V3TypedFact.Of("date", "calendar_date", "2021-03-15"),
                V3TypedFact.Of("nearest_date", "calendar_date", "2024-02-01"),
            ]));

        StringAssert.Contains(thrown.Message, "twice");
    }

    [TestMethod]
    public void ATemplateOutsideTheClosedSetRefusesAndSaysWhereWordingComesFrom()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() =>
            V3AuthoritativeClaim.Bind("the_court_would_likely_find",
                [V3TypedFact.Of("x", "prose", "y")]));

        StringAssert.Contains(thrown.Message, "closed set");
        StringAssert.Contains(thrown.Message, "never a sentence written at a call site");
    }

    [TestMethod]
    public void NoPublicPathProducesAClaimWithoutGoingThroughTheTemplateSet()
    {
        // "Model gloss remains disabled" is a claim about everything the API does not allow, which no
        // example can establish. It is pinned over the type's own surface instead: the only way to
        // obtain a claim is Bind, which takes a template id, and nothing exposes the rendered text
        // for writing.
        var claim = typeof(V3AuthoritativeClaim);

        // Static AND instance. An earlier version asked only about static methods, so a public
        // instance method building a claim from arbitrary text passed it; a reviewer measured that.
        // The guarded-construction census does catch it, because an instance method is a new door on
        // the type, but a test named for this property should not need the census to hold it.
        // <Clone>$ is the record's own compiler-generated copy method and is not a way in.
        var factories = claim.GetMethods()
            .Where(static method => method.IsPublic
                && method.ReturnType == typeof(V3AuthoritativeClaim)
                && method.Name != "<Clone>$")
            .Select(static method => method.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { "Bind" }, factories,
            "A second way to obtain an authoritative claim has appeared. Every claim must come from "
            + "the closed template set; a factory beside Bind, static or instance, is where prose "
            + "gets in.");

        Assert.IsEmpty(
            claim.GetConstructors(),
            "V3AuthoritativeClaim has a public constructor, so a claim can be built without a template.");

        foreach (var property in claim.GetProperties())
        {
            Assert.IsNull(
                property.SetMethod,
                $"{property.Name} is settable, so a claim's {property.Name} can be replaced after it "
                + "was bound, which is model gloss with an extra step.");
        }
    }

    [TestMethod]
    public void EveryTemplateRendersWithNoBraceLeftWhenOnlyItsListedPlaceholdersAreBound()
    {
        // The sweep the single-template test cannot do: bind every template's own listed placeholders and
        // require the rendered text to carry no brace. Counted on the output rather than matched against
        // the product's pattern, so a marker the pattern is wrong about shows up here as text a reader
        // would have seen.
        foreach (var template in V3ClaimTemplates.All)
        {
            var claim = V3AuthoritativeClaim.Bind(template.TemplateId,
                template.Placeholders.Select(static name => V3TypedFact.Of(name, "probe", "<" + name + ">")).ToArray());

            Assert.IsFalse(claim.Rendered.Contains('{', StringComparison.Ordinal),
                $"{template.TemplateId} renders with an opening brace nothing bound.");
            Assert.IsFalse(claim.Rendered.Contains('}', StringComparison.Ordinal),
                $"{template.TemplateId} renders with a closing brace nothing bound.");
        }
    }

    [TestMethod]
    public void ATemplateCarryingAMarkerThePlaceholderRuleCannotReadIsRefusedWhenItIsDefined()
    {
        // The set is closed, so the sweep above can only fail on a template somebody added. This is the
        // other end of it: the moment of definition refuses, so the bad template never reaches the set.
        var thrown = Assert.ThrowsExactly<ArgumentException>(() =>
            V3ClaimTemplate.Define("marker_the_rule_cannot_read",
                "On {date}, {Work} read as the text with content hash {text_sha256}."));

        StringAssert.Contains(thrown.Message, "brace this rule cannot read");
    }

    /// <summary>
    /// The no-gloss guarantee as a property, checked against an independent implementation over
    /// hostile values: rendered is the template with each placeholder replaced by its own value, and
    /// a value is never itself read as a template.
    /// </summary>
    /// <remarks>
    /// This is the test that had to exist. A reviewer found rendering was repeated replacement over
    /// the running text, so a value carrying another placeholder's marker was rewritten by a later
    /// pass, and whether it was depended on the order the placeholders were listed in. No example
    /// with well-behaved values can see that. The expected side here is built by cutting the template
    /// at its own placeholders and rejoining it around the values, which shares no code with the
    /// product, and the values are the ones the old mechanism mishandled.
    /// </remarks>
    [TestMethod]
    [DataRow("{work}", "{date}")]
    [DataRow("{date}", "{work}")]
    [DataRow("{", "}}")]
    [DataRow("}{nearest_date}{", "{{")]
    public void AValueIsNeverReadAsATemplateWhicheverOrderTheFactsArriveIn(string first, string second)
    {
        var template = V3ClaimTemplates.Get(V3ClaimTemplates.NoStateForDate);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["work"] = first,
            ["date"] = second,
            ["nearest_date"] = "2024-02-01",
        };

        // Supplied in reverse of the template's order, so binding cannot depend on arrival order.
        var claim = V3AuthoritativeClaim.Bind(V3ClaimTemplates.NoStateForDate,
            template.Placeholders.Reverse()
                .Select(name => V3TypedFact.Of(name, "probe", values[name])).ToArray());

        Assert.AreEqual(Expected(template.Text, values), claim.Rendered);
    }

    /// <summary>
    /// The template rendered by an implementation sharing nothing with the product: the text is cut
    /// at each placeholder and rejoined around the values, so no value is ever scanned for a marker.
    /// </summary>
    private static string Expected(string text, IReadOnlyDictionary<string, string> values)
    {
        var built = new StringBuilder();
        var at = 0;
        while (at < text.Length)
        {
            var open = text.IndexOf('{', at);
            if (open < 0)
            {
                built.Append(text, at, text.Length - at);
                break;
            }

            var close = text.IndexOf('}', open);
            var name = text[(open + 1)..close];
            built.Append(text, at, open - at).Append(values[name]);
            at = close + 1;
        }

        return built.ToString();
    }

    [TestMethod]
    public void TheFactsComeBackInTheTemplatesOrderAndNotTheOrderTheyWereSupplied()
    {
        // Supplied reversed. A claim that hands them back as supplied passes whenever the caller
        // happened to supply them in the template's order, which every earlier test here did.
        var template = V3ClaimTemplates.Get(V3ClaimTemplates.NoStateForDate);
        var supplied = new[]
        {
            V3TypedFact.Of("nearest_date", "calendar_date", "2024-02-01"),
            V3TypedFact.Of("date", "calendar_date", "2021-03-15"),
            V3TypedFact.Of("work", "work_key", "loi-1991-08-10-n3"),
        };

        var claim = V3AuthoritativeClaim.Bind(V3ClaimTemplates.NoStateForDate, supplied);

        CollectionAssert.AreEqual(
            template.Placeholders.ToArray(),
            claim.Facts.Select(static fact => fact.Name).ToArray(),
            "A claim's facts must read in the order its sentence states them, whatever order the "
            + "producer supplied them in, because a reader follows the sentence.");
        CollectionAssert.AreNotEqual(
            supplied.Select(static fact => fact.Name).ToArray(),
            claim.Facts.Select(static fact => fact.Name).ToArray(),
            "this case is meant to supply them in an order the template does not use");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void AClaimBoundToAnEmptyValueRefusesRatherThanRenderingAGap(string value)
    {
        // Built through the record's public primary constructor, because V3TypedFact.Of is not the
        // only way in and Bind is the door every claim comes through.
        var thrown = Assert.ThrowsExactly<ArgumentException>(() =>
            V3AuthoritativeClaim.Bind(V3ClaimTemplates.NoStateForDate,
            [
                new V3TypedFact("work", "work_key", "loi-1991-08-10-n3"),
                new V3TypedFact("date", "calendar_date", value),
                new V3TypedFact("nearest_date", "calendar_date", "2024-02-01"),
            ]));

        StringAssert.Contains(thrown.Message, "date");
        StringAssert.Contains(thrown.Message, "empty value");
    }

    [TestMethod]
    public void AFactWithNoKindRefusesBecauseAnUntypedFactIsABareString()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() =>
            V3AuthoritativeClaim.Bind(V3ClaimTemplates.NoStateForDate,
            [
                new V3TypedFact("work", "", "loi-1991-08-10-n3"),
                new V3TypedFact("date", "calendar_date", "2021-03-15"),
                new V3TypedFact("nearest_date", "calendar_date", "2024-02-01"),
            ]));

        StringAssert.Contains(thrown.Message, "no name or no kind");
    }

    [TestMethod]
    public void ATemplateThatBindsNoTypedFactIsRefusedWhenItIsDefined()
    {
        // Every template in the set names placeholders, so nothing else reaches this refusal.
        var thrown = Assert.ThrowsExactly<ArgumentException>(() =>
            V3ClaimTemplate.Define("says_the_same_thing_about_everything",
                "This index holds a state of the instrument you asked about."));

        StringAssert.Contains(thrown.Message, "names no placeholder");
    }

    [TestMethod]
    public void ATemplateIdIsMatchedExactlyAndIsDefinedAnswersRatherThanThrows()
    {
        Assert.IsFalse(V3ClaimTemplates.IsDefined("TEXT_ON_DATE"), "ids are matched exactly");
        Assert.IsFalse(V3ClaimTemplates.IsDefined("Text_On_Date"));
        Assert.IsFalse(V3ClaimTemplates.IsDefined(null!), "a predicate answers about a missing id");
        Assert.IsFalse(V3ClaimTemplates.IsDefined(""));
        Assert.ThrowsExactly<ArgumentException>(() => V3ClaimTemplates.Get("TEXT_ON_DATE"));
    }

    /// <summary>
    /// The exact wording of every template. "Fixed templates" is the clause: a change to what one
    /// says, or drops, changes what this product is able to assert, so it has to be a line in the
    /// diff somebody reads rather than a silent edit that fails nothing.
    /// </summary>
    [TestMethod]
    public void EveryTemplatesWordingIsExactlyThis()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [V3ClaimTemplates.TextOnDate] =
                "On {date}, {work} article {anchor} read as the text with content hash {text_sha256}, "
                + "as published by {publisher} at {source_uri}.",
            [V3ClaimTemplates.StateInterval] =
                "{work} has a held state {state_sha256} that {publisher} records as applying "
                + "from {applicable_from} to {applicable_to}, under {interval_semantics}.",
            [V3ClaimTemplates.NoStateForDate] =
                "This index holds no state of {work} for {date}; the nearest it holds is {nearest_date}, "
                + "and that is what this index holds rather than what exists.",
            [V3ClaimTemplates.PublisherReference] =
                "In {work} article {anchor}, {publisher} wrote a reference labelled {label} to {target}; "
                + "this records that the reference was written and not what it means.",
        };

        CollectionAssert.AreEquivalent(
            expected.Keys.ToArray(),
            V3ClaimTemplates.All.Select(static template => template.TemplateId).ToArray(),
            "a template was added or removed without its wording being pinned here");
        foreach (var template in V3ClaimTemplates.All)
        {
            Assert.AreEqual(
                expected[template.TemplateId], template.Text,
                $"{template.TemplateId}'s wording changed. That changes what this product asserts.");
        }
    }

    [TestMethod]
    public void EveryTemplateSaysWhatItDoesNotAssert()
    {
        // The three templates that could be read as more than a record of what is held carry the
        // limit in their own wording, because a reader sees the sentence and not this file.
        var limits = new (string TemplateId, string Says)[]
        {
            (V3ClaimTemplates.NoStateForDate, "what this index holds rather than what exists"),
            (V3ClaimTemplates.PublisherReference, "records that the reference was written and not what it means"),
            (V3ClaimTemplates.StateInterval, "records as applying"),
        };

        foreach (var (templateId, says) in limits)
        {
            StringAssert.Contains(
                V3ClaimTemplates.Get(templateId).Text, says,
                $"{templateId} no longer says what it does not assert. A claim that drops its own "
                + "limit reads as a statement about the law rather than about what is held.");
        }
    }
}

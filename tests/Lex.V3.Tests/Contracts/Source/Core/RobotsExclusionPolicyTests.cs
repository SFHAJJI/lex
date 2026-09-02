using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Tests.Contracts.Source.Core;

[TestClass]
public sealed class RobotsExclusionPolicyTests
{
    [TestMethod]
    public void ExactProductTokenMatchingIsCaseInsensitiveAndMergesMatchingGroups()
    {
        var policy = Bytes(
            """
            User-agent: LEX
            Disallow: /first
            User-agent: Other
            Disallow: /
            User-agent: lex
            Disallow: /second
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/first"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/second"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/third"));
    }

    [TestMethod]
    public void ExactProductTokenGroupTakesPrecedenceOverWildcardFallback()
    {
        var policy = Bytes(
            """
            User-agent: *
            Disallow: /
            User-agent: Lex
            Allow: /public
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/private"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            RobotsExclusionPolicy.Evaluate(policy, "AnotherBot", "/private"));
    }

    [TestMethod]
    public void WildcardGroupAppliesOnlyWhenNoExactProductTokenGroupExists()
    {
        var policy = Bytes(
            """
            User-agent: *
            Disallow: /private
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/private/file"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/public/file"));
    }

    [TestMethod]
    public void LongestOctetMatchWinsAndAllowWinsAnEquivalentTie()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow: /records
            Allow: /records/public
            Disallow: /same
            Allow: /same
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/records/public/1"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/records/private/1"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/same"));
    }

    [TestMethod]
    public void LongestMatchCountsTheNormalizedRuleOctets()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Allow: /page
            Disallow: /*.html
            Allow: /x/page.
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/page.html"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/x/page.html"));

        var equivalent = Bytes(
            """
            User-agent: Lex
            Disallow: /plain/baz
            Allow: /plain/%62%61%7A
            """);
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(equivalent, "/plain/baz"));
    }

    [TestMethod]
    public void WildcardAndTerminalAnchorMatchThePathAndQuery()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow: /*.xml$
            Disallow: /search?*secret=true
            Disallow: /a*b$
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/law/file.xml"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/law/file.xml?download=1"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/search?q=x&secret=true"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/axbxb"));
    }

    [TestMethod]
    [Timeout(10_000)]
    public void AdversarialWildcardInputUsesBoundedMatchingWork()
    {
        var repeated = new string('a', 128 * 1024);
        var policy = Bytes($"User-agent: Lex\nDisallow: /*{repeated}b$");

        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            Evaluate(policy, $"/{repeated}{repeated}"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            Evaluate(policy, $"/{repeated}{repeated}b"));
    }

    [TestMethod]
    public void MatchingIsCaseSensitive()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow: /Private
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/Private/file"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/private/file"));
    }

    [TestMethod]
    public void RawUtf8AndPercentEncodedUnreservedOctetsCanonicalizeBeforeComparison()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow: /unicode/ツ
            Disallow: /plain/%62%61%7A
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/unicode/%E3%83%84"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/unicode/ツ"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/plain/baz"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/plain/%62%61%7a"));
    }

    [TestMethod]
    public void ReservedEscapesStayDistinctAndEscapedSpecialCharactersAreLiteral()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow: /a%2Fb
            Disallow: /literal-%2A-%24
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/a%2fb"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/a/b"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/literal-*-$"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/literal-anything-x"));

        var dollar = Bytes("User-agent: Lex\nDisallow: /money/%24");
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(dollar, "/money/$"));
    }

    [TestMethod]
    public void RawPrintablePatternCharactersMatchTheirEncodedRequestOctets()
    {
        foreach (var character in "\"<>[]\\^`{|}")
        {
            var policy = Bytes($"User-agent: Lex\nDisallow: /a{character}b");
            var encodedPath = $"/a%{(byte)character:X2}b";

            Assert.AreEqual(
                RobotsPolicyEvaluationResult.Denied,
                Evaluate(policy, encodedPath),
                $"Raw U+{(int)character:X4} must match its encoded request octet.");
        }

        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            Evaluate(Bytes("User-agent: Lex\nDisallow: /a%62c"), "/abc"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            Evaluate(Bytes("User-agent: Lex\nDisallow: /a%2Ab"), "/a*b"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            Evaluate(Bytes("User-agent: Lex\nDisallow: /money/$/receipt"), "/money/$/receipt"));
    }

    [TestMethod]
    public void CommentsUnknownRecordsAndRulesOutsideGroupsDoNotChangeApplicableRules()
    {
        var policy = Bytes(
            "Allow: /outside\r\n" +
            "User-agent: Lex # exact group\r" +
            "Sitemap: https://example.invalid/map\n" +
            "Disallow: /private # retained rule\r\n");

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/private"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/outside"));
    }

    [TestMethod]
    public void OtherRecordsDoNotTerminateAGroupAndMultipleWildcardGroupsMerge()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Sitemap: https://example.invalid/map
            User-agent: *
            Disallow: /first
            User-agent: *
            Disallow: /second
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/first"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/second"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            RobotsExclusionPolicy.Evaluate(policy, "AnotherBot", "/first"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            RobotsExclusionPolicy.Evaluate(policy, "AnotherBot", "/second"));
    }

    [TestMethod]
    public void ProductTokenMatchingIsExactRatherThanPrefixBased()
    {
        var policy = Bytes(
            """
            User-agent: LexCrawler
            Disallow: /
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/private"));
    }

    [TestMethod]
    public void UnrecognizedLinesAreIgnoredWhileLaterParseableRulesRemainEffective()
    {
        var policy = Bytes(
            """
            User-agent Lex
            Disallow: /
            User-agent: Lex
            Sitemap: https://example.invalid/%ZZ
            Disallow: /private
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/public"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/private"));
    }

    [TestMethod]
    public void MalformedRuleMakesOnlyItsGroupUnsafeToInterpret()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow: /bad%ZZ
            User-agent: Other
            Disallow: /private
            """);

        Assert.AreEqual(
            RobotsPolicyEvaluationResult.UnsafeToInterpret,
            Evaluate(policy, "/private"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            RobotsExclusionPolicy.Evaluate(policy, "Other", "/private"));
    }

    [TestMethod]
    [DataRow("User-agent: Lex\nUser-agent: Bad Token\nDisallow: /private")]
    [DataRow("User-agent: Bad Token\nUser-agent: Lex\nDisallow: /private")]
    [DataRow("User-agent: *\nUSER-AGENT: Bad Token\nDisallow: /private")]
    public void InvalidUserAgentInAnApplicableHeaderSequenceIsUnsafe(string policy)
    {
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.UnsafeToInterpret,
            Evaluate(Bytes(policy), "/private"));
    }

    [TestMethod]
    public void InvalidUserAgentAfterARuleStartsAnUnrelatedQuarantinedGroup()
    {
        var policy = Bytes(
            """
            User-agent: *
            Disallow: /c/portal/
            User-agent: Sogou web spider
            Disallow: /
            """);

        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            Evaluate(policy, "/resource/cellar/abc"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            Evaluate(policy, "/c/portal/"));
    }

    [TestMethod]
    public void InvalidRecognizedRuleMakesThePolicyUnsafeButEmptyDisallowStaysValid()
    {
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.UnsafeToInterpret,
            Evaluate(Bytes("User-agent: Lex\nDisallow: /bad%ZZ"), "/public"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.UnsafeToInterpret,
            Evaluate(Bytes("User-agent: Lex\nAllow: /bad%ZZ"), "/public"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            Evaluate(Bytes("Allow: /bad%ZZ"), "/public"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            Evaluate(Bytes("User-agent: Lex\nDisallow:"), "/public"));
    }

    [TestMethod]
    public void UnrelatedMalformedRuleGroupDoesNotTaintTheApplicableGroup()
    {
        var policy = Bytes(
            """
            User-agent: Other
            Disallow: /bad%ZZ
            User-agent: Lex
            Disallow: /private
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/private"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/public"));
    }

    [TestMethod]
    public void ExactGroupsKeepPrecedenceOverUnsafeWildcardGroups()
    {
        var policy = Bytes(
            """
            User-agent: *
            Disallow: /bad%ZZ
            User-agent: Lex
            Allow: /private
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/private"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.UnsafeToInterpret,
            RobotsExclusionPolicy.Evaluate(policy, "Other", "/private"));

        var unsafeExact = Bytes(
            """
            User-agent: *
            Allow: /private
            User-agent: Lex
            Disallow: /bad%ZZ
            """);
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.UnsafeToInterpret,
            Evaluate(unsafeExact, "/private"));
    }

    [TestMethod]
    [DataRow("private")]
    [DataRow("$")]
    [DataRow("%2Fprivate")]
    public void InvalidRuleStartMakesThePolicyUnsafeToInterpret(string pattern)
    {
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.UnsafeToInterpret,
            Evaluate(Bytes($"User-agent: Lex\nDisallow: {pattern}"), "/private"));
    }

    [TestMethod]
    public void SlashAndWildcardRuleStartsRemainValid()
    {
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            Evaluate(Bytes("User-agent: Lex\nDisallow: /private"), "/private"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Denied,
            Evaluate(Bytes("User-agent: Lex\nDisallow: *.gif$"), "/image.gif"));
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            Evaluate(Bytes("User-agent: Lex\nDisallow:"), "/private"));
    }

    [TestMethod]
    public void PublicEvaluatorSurfaceAndVerdictsStayClosed()
    {
        var type = typeof(RobotsExclusionPolicy);
        Assert.IsTrue(type.IsPublic);
        Assert.IsTrue(type.IsAbstract);
        Assert.IsTrue(type.IsSealed);

        var members = type.GetMembers(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.DeclaredOnly);
        Assert.HasCount(1, members);
        Assert.AreEqual(System.Reflection.MemberTypes.Method, members[0].MemberType);

        var method = (System.Reflection.MethodInfo)members[0];
        Assert.AreEqual(nameof(RobotsExclusionPolicy.Evaluate), method.Name);
        Assert.AreEqual(typeof(RobotsPolicyEvaluationResult), method.ReturnType);
        CollectionAssert.AreEqual(
            new[] { typeof(ReadOnlySpan<byte>), typeof(string), typeof(string) },
            method.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());
        CollectionAssert.AreEqual(
            new[] { "policyBytes", "productToken", "pathAndQuery" },
            method.GetParameters().Select(static parameter => parameter.Name).ToArray());

        CollectionAssert.AreEqual(
            new[] { "Allowed", "Denied", "UnsafeToInterpret" },
            Enum.GetNames<RobotsPolicyEvaluationResult>());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3 },
            Enum.GetValues<RobotsPolicyEvaluationResult>().Select(static value => (int)value).ToArray());
        Assert.IsNull(
            type.Assembly.GetType("Lex.V3.Contracts.Source.Core.RobotsPathVerdict"));
    }

    [TestMethod]
    public void Utf8ByteOrderMarkAtTheStartDoesNotHideTheFirstGroup()
    {
        var policy = Encoding.UTF8.GetPreamble()
            .Concat(Bytes("User-agent: Lex\nDisallow: /private"))
            .ToArray();

        Assert.AreEqual(RobotsPolicyEvaluationResult.Denied, Evaluate(policy, "/private"));
    }

    [TestMethod]
    public void EmptyOrAbsentApplicableRulesAllowAndRobotsTxtIsImplicitlyAllowed()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow:
            User-agent: Other
            Disallow: /
            """);

        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(policy, "/anything"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(Bytes("User-agent: Lex\nDisallow: /"), "/robots.txt"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate(Bytes("User-agent: Lex\nDisallow: /"), "/robots%2Etxt"));
        Assert.AreEqual(RobotsPolicyEvaluationResult.Allowed, Evaluate([], "/anything"));
    }

    [TestMethod]
    public void MalformedPolicyBytesAndInvalidEvaluationInputsFailClosed()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Evaluate([0xc3, 0x28], "/"));
        var malformedPolicy = Assert.ThrowsExactly<ArgumentException>(() =>
            Evaluate(Bytes("User-agent: Lex\nDisallow: /\0public"), "/"));
        Assert.AreEqual("policyBytes", malformedPolicy.ParamName);

        var invalidProductToken = Assert.ThrowsExactly<ArgumentException>(() =>
            RobotsExclusionPolicy.Evaluate(Bytes("User-agent: Lex"), "Lex/0.1", "/"));
        Assert.AreEqual("productToken", invalidProductToken.ParamName);

        var invalidPath = Assert.ThrowsExactly<ArgumentException>(() =>
            Evaluate(Bytes("User-agent: Lex"), "not-a-path"));
        Assert.AreEqual("pathAndQuery", invalidPath.ParamName);

        Assert.ThrowsExactly<ArgumentException>(() => Evaluate(Bytes("User-agent: Lex"), "/bad%2"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Evaluate(
                Enumerable.Repeat((byte)' ', RobotsExclusionPolicy.MaximumPolicyBytes + 1).ToArray(),
                "/"));
    }

    [TestMethod]
    public void PathAndQueryAcceptsOnlyCanonicalRfc3986Characters()
    {
        Assert.AreEqual(
            RobotsPolicyEvaluationResult.Allowed,
            Evaluate([], "/p:@!$&'()*+,;=~?q=/?:@!$&'()*+,;=~"));

        foreach (var invalidCharacter in "\\\"<>[]^`{|}")
        {
            var exception = Assert.ThrowsExactly<ArgumentException>(() =>
                Evaluate([], $"/private{invalidCharacter}file"),
                $"Raw U+{(int)invalidCharacter:X4} must be refused.");
            Assert.AreEqual("pathAndQuery", exception.ParamName);
        }
    }

    private static RobotsPolicyEvaluationResult Evaluate(byte[] policy, string pathAndQuery) =>
        RobotsExclusionPolicy.Evaluate(policy, "Lex", pathAndQuery);

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}

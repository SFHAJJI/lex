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

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/first"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/second"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/third"));
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

        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/private"));
        Assert.AreEqual(
            RobotsPathVerdict.Denied,
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

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/private/file"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/public/file"));
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

        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/records/public/1"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/records/private/1"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/same"));
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

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/page.html"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/x/page.html"));

        var equivalent = Bytes(
            """
            User-agent: Lex
            Disallow: /plain/baz
            Allow: /plain/%62%61%7A
            """);
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(equivalent, "/plain/baz"));
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

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/law/file.xml"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/law/file.xml?download=1"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/search?q=x&secret=true"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/axbxb"));
    }

    [TestMethod]
    [Timeout(10_000)]
    public void AdversarialWildcardInputUsesBoundedMatchingWork()
    {
        var repeated = new string('a', 128 * 1024);
        var policy = Bytes($"User-agent: Lex\nDisallow: /*{repeated}b$");

        Assert.AreEqual(
            RobotsPathVerdict.Allowed,
            Evaluate(policy, $"/{repeated}{repeated}"));
        Assert.AreEqual(
            RobotsPathVerdict.Denied,
            Evaluate(policy, $"/{repeated}{repeated}b"));
    }

    [TestMethod]
    public void MatchingIsCaseSensitiveAndStartsAtTheFirstPathOctet()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow: /Private
            Disallow: private
            """);

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/Private/file"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/private/file"));
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

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/unicode/%E3%83%84"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/unicode/ツ"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/plain/baz"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/plain/%62%61%7a"));
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

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/a%2fb"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/a/b"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/literal-*-$"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/literal-anything-x"));

        var dollar = Bytes("User-agent: Lex\nDisallow: /money/%24");
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(dollar, "/money/$"));
    }

    [TestMethod]
    public void CommentsUnknownRecordsAndRulesOutsideGroupsDoNotChangeApplicableRules()
    {
        var policy = Bytes(
            "Allow: /outside\r\n" +
            "User-agent: Lex # exact group\r" +
            "Sitemap: https://example.invalid/map\n" +
            "Disallow: /private # retained rule\r\n");

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/private"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/outside"));
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

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/first"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/second"));
        Assert.AreEqual(
            RobotsPathVerdict.Denied,
            RobotsExclusionPolicy.Evaluate(policy, "AnotherBot", "/first"));
        Assert.AreEqual(
            RobotsPathVerdict.Denied,
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

        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/private"));
    }

    [TestMethod]
    public void InvalidLinesAreIgnoredWhileLaterParseableRulesRemainEffective()
    {
        var policy = Bytes(
            """
            User-agent Lex
            Disallow: /
            User-agent: Lex
            Disallow: /bad%ZZ
            Disallow: /private
            """);

        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/public"));
        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/private"));
    }

    [TestMethod]
    public void MalformedRuleStillEndsTheUserAgentHeaderSequence()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            Disallow: /bad%ZZ
            User-agent: Other
            Disallow: /private
            """);

        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/private"));
        Assert.AreEqual(
            RobotsPathVerdict.Denied,
            RobotsExclusionPolicy.Evaluate(policy, "Other", "/private"));
    }

    [TestMethod]
    public void InvalidUserAgentQuarantinesFollowingRulesFromThePriorGroup()
    {
        var policy = Bytes(
            """
            User-agent: *
            Disallow: /private
            User-agent: Invalid Agent
            Allow: /private
            """);

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/private"));
    }

    [TestMethod]
    public void InvalidUserAgentBeforeRulesDoesNotEraseTheValidHeader()
    {
        var policy = Bytes(
            """
            User-agent: Lex
            User-agent: Invalid Agent
            Disallow: /private
            """);

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/private"));
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
        Assert.AreEqual(typeof(RobotsPathVerdict), method.ReturnType);
        CollectionAssert.AreEqual(
            new[] { typeof(ReadOnlySpan<byte>), typeof(string), typeof(string) },
            method.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());
        CollectionAssert.AreEqual(
            new[] { "policyBytes", "productToken", "pathAndQuery" },
            method.GetParameters().Select(static parameter => parameter.Name).ToArray());

        CollectionAssert.AreEqual(new[] { "Allowed", "Denied" }, Enum.GetNames<RobotsPathVerdict>());
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            Enum.GetValues<RobotsPathVerdict>().Select(static value => (int)value).ToArray());
    }

    [TestMethod]
    public void Utf8ByteOrderMarkAtTheStartDoesNotHideTheFirstGroup()
    {
        var policy = Encoding.UTF8.GetPreamble()
            .Concat(Bytes("User-agent: Lex\nDisallow: /private"))
            .ToArray();

        Assert.AreEqual(RobotsPathVerdict.Denied, Evaluate(policy, "/private"));
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

        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(policy, "/anything"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(Bytes("User-agent: Lex\nDisallow: /"), "/robots.txt"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate(Bytes("User-agent: Lex\nDisallow: /"), "/robots%2Etxt"));
        Assert.AreEqual(RobotsPathVerdict.Allowed, Evaluate([], "/anything"));
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
            RobotsPathVerdict.Allowed,
            Evaluate([], "/p:@!$&'()*+,;=~?q=/?:@!$&'()*+,;=~"));

        foreach (var invalidCharacter in "\\\"<>[]^`{|}")
        {
            var exception = Assert.ThrowsExactly<ArgumentException>(() =>
                Evaluate([], $"/private{invalidCharacter}file"),
                $"Raw U+{(int)invalidCharacter:X4} must be refused.");
            Assert.AreEqual("pathAndQuery", exception.ParamName);
        }
    }

    private static RobotsPathVerdict Evaluate(byte[] policy, string pathAndQuery) =>
        RobotsExclusionPolicy.Evaluate(policy, "Lex", pathAndQuery);

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}

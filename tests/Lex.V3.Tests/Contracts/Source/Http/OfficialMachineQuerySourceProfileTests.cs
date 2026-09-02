using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class OfficialMachineQuerySourceProfileTests
{
    private const string UserAgent = "Lex/0.1 (+https://github.com/SFHAJJI/lex)";
    private const string ResultsJson = "application/sparql-results+json";

    [TestMethod]
    public void CatalogIsClosedAndProfilesCannotBeCallerConstructed()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                OfficialMachineQuerySourceProfileId.LuxembourgSparql,
                OfficialMachineQuerySourceProfileId.EuropeanUnionSparql,
            },
            Enum.GetValues<OfficialMachineQuerySourceProfileId>());

        Assert.AreEqual(
            0,
            typeof(OfficialMachineQuerySourceProfile).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance).Length);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            OfficialMachineQuerySourceProfiles.Resolve(
                (OfficialMachineQuerySourceProfileId)int.MaxValue));

        var publicMethods = typeof(OfficialMachineQuerySourceProfiles)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.AreEqual(1, publicMethods.Length);
        Assert.AreEqual(nameof(OfficialMachineQuerySourceProfiles.ResolveFor), publicMethods[0].Name);
        CollectionAssert.AreEqual(
            new[] { typeof(BoundMachineRequest) },
            publicMethods[0].GetParameters()
                .Select(static parameter => parameter.ParameterType)
                .ToArray());
    }

    [TestMethod]
    public void LuxembourgSparqlProfilePinsTheExactRequestAndDirectRobotsRoute()
    {
        var profile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.LuxembourgSparql);

        AssertCommonProfile(profile, OfficialMachineQuerySourceProfileId.LuxembourgSparql);
        Assert.AreEqual(
            "https://data.legilux.public.lu/sparqlendpoint",
            profile.RequestTarget);
        Assert.AreEqual("application/x-www-form-urlencoded", profile.RequestContentType);
        Assert.AreEqual("data.legilux.public.lu", profile.RobotsRoute.InitialAuthority.Host);
        Assert.AreEqual(443, profile.RobotsRoute.InitialAuthority.EffectivePort);
        AssertRoute(
            profile,
            new RobotsPolicyRouteStep(
                "https://data.legilux.public.lu/robots.txt",
                200,
                null));
    }

    [TestMethod]
    public void EuropeanUnionSparqlProfilePinsTheInitialAuthorityAndExactRedirect()
    {
        var profile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);

        AssertCommonProfile(profile, OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);
        Assert.AreEqual(
            "https://publications.europa.eu/webapi/rdf/sparql",
            profile.RequestTarget);
        Assert.AreEqual("application/sparql-query", profile.RequestContentType);
        Assert.AreEqual("publications.europa.eu", profile.RobotsRoute.InitialAuthority.Host);
        Assert.AreEqual(443, profile.RobotsRoute.InitialAuthority.EffectivePort);
        AssertRoute(
            profile,
            new RobotsPolicyRouteStep(
                "https://publications.europa.eu/robots.txt",
                301,
                "https://op.europa.eu/robots.txt"),
            new RobotsPolicyRouteStep(
                "https://op.europa.eu/robots.txt",
                200,
                null));

        Assert.AreNotEqual(
            profile.RobotsRoute.InitialAuthority.Host,
            new Uri(profile.RobotsRoute.Steps[^1].RequestedUri).Host,
            "A redirected policy remains scoped to the initial authority.");
    }

    [TestMethod]
    public void RobotsPolicyExpiresAtTheTwentyFourHourBoundary()
    {
        var profile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);
        var observedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

        Assert.AreEqual(
            RobotsPolicyFreshness.Current,
            profile.EvaluateRobotsPolicyFreshness(
                observedAt,
                observedAt.Add(profile.MaximumRobotsPolicyAge).AddTicks(-1)));
        Assert.AreEqual(
            RobotsPolicyFreshness.Expired,
            profile.EvaluateRobotsPolicyFreshness(
                observedAt,
                observedAt.Add(profile.MaximumRobotsPolicyAge)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            profile.EvaluateRobotsPolicyFreshness(observedAt, observedAt.AddTicks(-1)));
    }

    [TestMethod]
    public void OutcomeVocabularySeparatesProfileStalenessFromIntegrityFailure()
    {
        AssertWireValues<OfficialHttpAcquisitionOutcomeKind>(
            "executed_observation",
            "publisher_denial",
            "local_safety_refusal",
            "operational_failure",
            "integrity_failure");
        AssertWireValues<OfficialHttpOperationalFailureReason>(
            "network_failure",
            "publisher_server_failure",
            "robots_policy_expired",
            "source_profile_stale");

        Assert.IsFalse(
            Enum.GetValues<OfficialHttpOperationalFailureReason>()
                .Any(value => value.ToString().Contains("Integrity", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ProfileIdentityIsCultureInvariantAndCanonical()
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in Enum.GetValues<OfficialMachineQuerySourceProfileId>())
        {
            var french = UnderCulture(
                "fr-FR",
                () => OfficialMachineQuerySourceProfiles.Resolve(id).ProfileSha256);
            var english = UnderCulture(
                "en-US",
                () => OfficialMachineQuerySourceProfiles.Resolve(id).ProfileSha256);

            Assert.AreEqual(french, english);
            Assert.AreEqual(64, french.Length);
            Assert.IsTrue(french.All(static value =>
                value is >= '0' and <= '9' or >= 'a' and <= 'f'));
            Assert.IsTrue(identities.Add(french), "Every exact profile needs a distinct identity.");
            Assert.AreEqual(ExpectedProfileSha256(id), french);

            var profile = OfficialMachineQuerySourceProfiles.Resolve(id);
            var canonicalBytes = profile.CopyCanonicalBytes();
            Assert.AreEqual(french, Sha256(canonicalBytes));
            Assert.AreEqual(
                new SourceArtifactRef(ExpectedResourceId(id), french),
                profile.ArtifactRef);

            canonicalBytes[0] ^= 0xff;
            Assert.AreEqual(french, Sha256(profile.CopyCanonicalBytes()));
        }
    }

    [TestMethod]
    public void BoundRequestDerivesItsProfileAndCannotSelectAnotherChannel()
    {
        var resolveFor = typeof(OfficialMachineQuerySourceProfiles).GetMethod(
            nameof(OfficialMachineQuerySourceProfiles.ResolveFor));
        CollectionAssert.AreEqual(
            new[] { typeof(BoundMachineRequest) },
            resolveFor!.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());

        Assert.AreEqual(
            OfficialMachineQuerySourceProfileId.LuxembourgSparql,
            OfficialMachineQuerySourceProfiles.ResolveFor(BoundPost(
                "https://data.legilux.public.lu/sparqlendpoint",
                "application/x-www-form-urlencoded")).Id);
        Assert.AreEqual(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql,
            OfficialMachineQuerySourceProfiles.ResolveFor(BoundPost(
                "https://publications.europa.eu/webapi/rdf/sparql",
                "application/sparql-query")).Id);

        Assert.ThrowsExactly<ArgumentException>(() =>
            OfficialMachineQuerySourceProfiles.ResolveFor(BoundPost(
                "https://publications.europa.eu/webapi/rdf/sparql",
                "application/x-www-form-urlencoded")));
        Assert.ThrowsExactly<ArgumentException>(() =>
            OfficialMachineQuerySourceProfiles.ResolveFor(BoundPost(
                "https://legilux.public.lu/filestore/example.xml",
                "application/x-www-form-urlencoded")));
    }

    [TestMethod]
    public void RouteStepsAreImmutableAndProfileExposesNoReleaseCapability()
    {
        var profile = OfficialMachineQuerySourceProfiles.Resolve(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql);
        var steps = profile.RobotsRoute.Steps;

        Assert.IsNotInstanceOfType<RobotsPolicyRouteStep[]>(steps);
        if (steps is IList<RobotsPolicyRouteStep> list)
        {
            Assert.IsTrue(list.IsReadOnly);
            Assert.ThrowsExactly<NotSupportedException>(() => list.RemoveAt(0));
        }

        Assert.AreEqual(2, profile.RobotsRoute.Steps.Count);

        var publicSurface = typeof(OfficialMachineQuerySourceProfile)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static member => member.MemberType is MemberTypes.Property or MemberTypes.Method)
            .Where(static member => member is not MethodInfo method || !method.IsSpecialName)
            .Select(static member => member.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "Accept",
                "ArtifactRef",
                "CopyCanonicalBytes",
                "CrawlerUserAgent",
                "EvaluateRobotsPolicyFreshness",
                "Id",
                "InitialRetryDelay",
                "MaximumAttempts",
                "MaximumResponseBytes",
                "MaximumRetryDelay",
                "MaximumRobotsPolicyAge",
                "Method",
                "MinimumRequestInterval",
                "PacingScope",
                "ProfileSha256",
                "RequestCharset",
                "RequestContentType",
                "RequestTarget",
                "RequestTimeout",
                "RetryConditions",
                "RobotsFreshnessBasis",
                "RobotsParserIdentity",
                "RobotsProductToken",
                "RobotsRevalidation",
                "RobotsRoute",
            },
            publicSurface);
    }

    private static void AssertCommonProfile(
        OfficialMachineQuerySourceProfile profile,
        OfficialMachineQuerySourceProfileId expectedId)
    {
        Assert.AreEqual(expectedId, profile.Id);
        Assert.AreEqual(HttpRequestMethod.Post, profile.Method);
        Assert.AreEqual(MachineQueryCharset.Utf8, profile.RequestCharset);
        Assert.AreEqual(ResultsJson, profile.Accept);
        Assert.AreEqual(UserAgent, profile.CrawlerUserAgent);
        Assert.AreEqual("Lex", profile.RobotsProductToken);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1500), profile.MinimumRequestInterval);
        Assert.AreEqual(OfficialHttpPacingScope.ProcessActualNetworkOrigin, profile.PacingScope);
        Assert.AreEqual(4, profile.MaximumAttempts);
        CollectionAssert.AreEqual(
            new[]
            {
                OfficialMachineQueryRetryCondition.RequestTimeout,
                OfficialMachineQueryRetryCondition.TransportFailure,
                OfficialMachineQueryRetryCondition.Http408,
                OfficialMachineQueryRetryCondition.Http429,
                OfficialMachineQueryRetryCondition.Http500,
                OfficialMachineQueryRetryCondition.Http502,
                OfficialMachineQueryRetryCondition.Http503,
                OfficialMachineQueryRetryCondition.Http504,
            },
            profile.RetryConditions.ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(1), profile.InitialRetryDelay);
        Assert.AreEqual(TimeSpan.FromSeconds(30), profile.MaximumRetryDelay);
        Assert.AreEqual(TimeSpan.FromSeconds(60), profile.RequestTimeout);
        Assert.AreEqual(CustodyBounds.MaxObjectBytes, profile.MaximumResponseBytes);
        Assert.AreEqual(TimeSpan.FromHours(24), profile.MaximumRobotsPolicyAge);
        Assert.AreEqual("rfc9309_2_4", profile.RobotsFreshnessBasis);
        Assert.AreEqual("robots-exclusion-policy/1", profile.RobotsParserIdentity);
        Assert.AreEqual(RobotsRevalidationMode.FullGetWithoutValidators, profile.RobotsRevalidation);
        Assert.AreEqual("https", profile.RobotsRoute.InitialAuthority.Scheme);
    }

    private static void AssertRoute(
        OfficialMachineQuerySourceProfile profile,
        params RobotsPolicyRouteStep[] expected)
    {
        Assert.AreEqual(expected.Length, profile.RobotsRoute.Steps.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index], profile.RobotsRoute.Steps[index]);
        }
    }

    private static void AssertWireValues<T>(params string[] expected)
        where T : struct, Enum
    {
        var actual = Enum.GetValues<T>()
            .Select(static value => typeof(T)
                .GetField(value.ToString())!
                .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name)
            .ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }

    private static T UnderCulture<T>(string name, Func<T> action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static BoundMachineRequest BoundPost(string target, string contentType)
    {
        var body = Encoding.UTF8.GetBytes("ASK{}");
        var targetBytes = Encoding.ASCII.GetBytes(new Uri(target).PathAndQuery);
        var registry = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000aa",
            new string('a', 64));
        var receipt = new MachineQueryRenderReceipt(
            MachineQueryRenderReceipt.SchemaId,
            registry,
            MachineQueryPlan.SchemaId,
            registry,
            registry,
            registry,
            new SourceRegistryMemberRef(registry, contentType),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            HttpRequestMethod.Post,
            targetBytes.LongLength,
            Sha256(targetBytes),
            body.LongLength,
            Sha256(body));
        return new BoundMachineRequest(target, body, receipt);
    }

    private static string ExpectedProfileSha256(OfficialMachineQuerySourceProfileId id) => id switch
    {
        OfficialMachineQuerySourceProfileId.LuxembourgSparql =>
            "7cec68b0c435654506188a7c20ba55a77dddaf8c170741fdf0f292af439052e3",
        OfficialMachineQuerySourceProfileId.EuropeanUnionSparql =>
            "69ce2949fc1e0c19acb841fe60c14cc9cad93bfd2480b8e7fdd2d4238ce22418",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static string ExpectedResourceId(OfficialMachineQuerySourceProfileId id) => id switch
    {
        OfficialMachineQuerySourceProfileId.LuxembourgSparql =>
            "urn:uuid:911499a3-087c-42ec-9dca-5c9131ccec47",
        OfficialMachineQuerySourceProfileId.EuropeanUnionSparql =>
            "urn:uuid:f08afb3b-e30f-41cc-b9be-cf29da97bb76",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

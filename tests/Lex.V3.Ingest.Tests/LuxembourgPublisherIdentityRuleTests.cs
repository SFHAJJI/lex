using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The census join excludes a key no observation can carry (2026-10-09). Ingest's predicate form of the publisher-URI
/// rule has to be the rule every <see cref="SourceObjectRef"/> applies, exactly, and the scope resolver's identity rule
/// has to be stricter where it differs, or an excluded key would be one that would have resolved, or a kept key one
/// that fails the run.
/// </summary>
[TestClass]
public sealed class LuxembourgPublisherIdentityRuleTests
{
    [TestMethod]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a")]
    [DataRow("https://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a")]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/code/travail/art._l._542-4_à_l._542-6./20201101")]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/code/a b")]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/code/a%zz")]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/code/a%2F")]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/code/a?q=1")]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/code/a#f")]
    [DataRow("http://user@data.legilux.public.lu/eli/a")]
    [DataRow("ftp://data.legilux.public.lu/eli/a")]
    [DataRow("http://data.legilux.public.lu:8080/eli/a")]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/code/art.\\1")]
    [DataRow("urn:uuid:7d0c4bb5-6b0a-4d2b-9f54-2f8c2b7f3c11")]
    public void TheIngestPublisherUriPredicateIsTheContractsRule(string value)
    {
        Assert.AreEqual(ContractsAccepts(value), LuxembourgTranspositionProducer.IsPublisherUri(value), value);
    }

    [TestMethod]
    public void TheLengthBoundIsTheContractsBound()
    {
        var prefix = "http://data.legilux.public.lu/eli/";
        foreach (var length in new[] { 4_096, 4_097 })
        {
            var value = prefix + new string('a', length - prefix.Length);
            Assert.AreEqual(ContractsAccepts(value), LuxembourgTranspositionProducer.IsPublisherUri(value), $"{length}");
        }
    }

    [TestMethod]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a", true)]
    [DataRow("http://data.legilux.public.lu/eli/etat/leg/code/art.\\1", false)]
    [DataRow("http://data.legilux.public.lu:8080/eli/a", false)]
    [DataRow("http://example.org/eli/a", false)]
    public void TheResolverIdentityRuleIsWhatTheCensusJoinAdds(string value, bool admitted)
    {
        Assert.AreEqual(admitted, VerifiedLuxembourgSourceProfile.AdmitsObservationIdentity(value), value);
        if (admitted)
        {
            Assert.IsTrue(LuxembourgTranspositionProducer.IsPublisherUri(value), "an admitted identity is also a publisher URI");
        }
    }

    private static bool ContractsAccepts(string value)
    {
        try
        {
            SourceCoreValidation.RequirePublisherUri(value, nameof(value));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

using System.Reflection;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// Which relation families any query in this repository actually asks the publisher for.
/// </summary>
/// <remarks>
/// <para>
/// THE EXECUTABLE REACHABILITY MATRIX Candidate 5 R5.3 requires. Before this there was none: the
/// only record of what is read was a sentence in <see cref="EuRelationFamily"/>'s own remarks and a
/// hand-named list in <c>EuScopeVocabulary.ReadRelationFamilies</c>, and the one test over that list
/// fires in the wrong direction — it fails when a family is ADDED to the list, never when a family
/// silently becomes unreachable, and it consults no plan, executor or producer at all.
/// </para>
/// <para>
/// The prose went stale exactly as an unenforced claim does. It said all three case-law families
/// were read by nothing; E6's query has asked the publisher for two of them since its executor
/// integrated, and nothing failed. A claim about coverage that no test can falsify is the kind this
/// programme treats as a defect, so this derives the answer from THE QUERIES THEMSELVES.
/// </para>
/// <para>
/// How it derives. Every discovery plan in the contracts assembly that renders a query exposes a
/// count and a page template and a parameterless <c>Create</c>; they are found by reflection rather
/// than listed, so a plan added later is swept the day it appears. A family is ASKED FOR when its
/// predicate IRI appears inside angle brackets in one of those templates — the exact form a SPARQL
/// triple pattern uses, so a mention in a comment or a prose remark cannot count as reaching it.
/// </para>
/// <para>
/// What it does not claim. Asked for is not the same as consumed: a family whose predicate a query
/// sends can still have no producer that reads its rows into anything. This matrix answers the
/// narrower question honestly rather than the broader one loosely.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuRelationFamilyReachabilityTests
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Every plan in the contracts assembly that renders a publisher query, and all of its queries.
    /// </summary>
    /// <remarks>
    /// A PLAN CAN HOLD SEVERAL QUERIES behind an enum — <c>EuObjectFactsDiscoveryPlan</c> holds five
    /// — and the first version of this sweep looked only for a <c>CountTemplate</c> on the plan
    /// itself. It therefore missed that plan and the consolidation plan entirely, and would have
    /// reported the four families they ask for as asked for by nothing: the exact false coverage
    /// claim this matrix exists to prevent, made by the matrix. The reach check below is what caught
    /// it, which is why that check is a test and not a comment.
    /// </remarks>
    private static IReadOnlyList<(string Plan, IReadOnlyList<string> Templates)> RenderedQueries()
    {
        var found = new List<(string, IReadOnlyList<string>)>();

        foreach (var type in typeof(EuScopeVocabulary).Assembly.GetTypes()
                     .Where(static type => type is { IsClass: true, IsAbstract: false })
                     .OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            var create = type.GetMethod(
                "Create", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (create is null || create.ReturnType != type)
            {
                continue;
            }

            var plan = create.Invoke(null, null)!;
            var templates = new List<string>();
            Collect(plan, templates);

            // Queries selected by an enum, invoked for every member of it.
            foreach (var selector in type
                         .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(static method => method.GetParameters().Length == 1
                             && method.GetParameters()[0].ParameterType.IsEnum
                             && method.ReturnType.GetProperty("CountTemplate", Members) is not null))
            {
                foreach (var member in Enum.GetValues(selector.GetParameters()[0].ParameterType))
                {
                    try
                    {
                        if (selector.Invoke(plan, [member]) is { } definition)
                        {
                            Collect(definition, templates);
                        }
                    }
                    catch (TargetInvocationException)
                    {
                        // A selector that refuses a member is not a query; nothing to collect.
                    }
                }
            }

            if (templates.Count > 0)
            {
                found.Add((type.FullName!, templates));
            }
        }

        return found;
    }

    private static void Collect(object holder, List<string> into)
    {
        var type = holder.GetType();
        var count = type.GetProperty("CountTemplate", Members);
        var page = type.GetProperty("PageTemplate", Members);
        if (count?.PropertyType == typeof(string) && page?.PropertyType == typeof(string))
        {
            into.Add((string)count.GetValue(holder)!);
            into.Add((string)page.GetValue(holder)!);
        }
    }

    /// <summary>
    /// The predicate token a family carries, from its own wire vocabulary.
    /// </summary>
    /// <remarks>
    /// Read from the enum's <c>JsonStringEnumMemberName</c> rather than from
    /// <c>EuObjectFactsDiscoveryPlan.RelationIri</c>, which maps only the four families that plan
    /// reads and throws for the other nine — a partial map cannot answer a question about all
    /// thirteen. The token is the predicate's final segment, so it is matched anchored to the end of
    /// an IRI inside a triple pattern and cannot be satisfied by a mention in prose.
    /// </remarks>
    private static string TokenOf(EuRelationFamily family) =>
        typeof(EuRelationFamily).GetField(family.ToString())!
            .GetCustomAttributes<JsonStringEnumMemberNameAttribute>(false)
            .Single().Name;

    private static bool AskedFor(EuRelationFamily family, IReadOnlyList<string> templates)
    {
        var token = TokenOf(family);
        return templates.Any(template =>
            template.Contains("#" + token + ">", StringComparison.Ordinal)
            || template.Contains("/" + token + ">", StringComparison.Ordinal));
    }

    /// <summary>
    /// The plans this sweep found. Pinned so the sweep going blind is a failure, not a quiet pass.
    /// </summary>
    /// <remarks>
    /// A reachability matrix that discovered no plans would report every family unreached and read
    /// as a clean result. This is the reach check that stops that: the emptiness above only proves
    /// something if the search that produced it can find anything at all.
    /// </remarks>
    [TestMethod]
    public void TheSweepFindsEveryPlanThatRendersAPublisherQuery()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "Lex.V3.Contracts.Source.Europe.EuCaseLawDiscoveryPlan=1",
                "Lex.V3.Contracts.Source.Europe.EuConsolidationDiscoveryPlan=2",
                "Lex.V3.Contracts.Source.Europe.EuNationalImplementingMeasureDiscoveryPlan=1",
                "Lex.V3.Contracts.Source.Europe.EuObjectFactsDiscoveryPlan=5",
                "Lex.V3.Contracts.Source.Europe.EuProcedureEventDiscoveryPlan=1",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgOpinionDiscoveryPlan=1",
                "Lex.V3.Contracts.Source.Luxembourg.LuxembourgTranspositionIdentityDiscoveryPlan=1",
            },
            RenderedQueries()
                .Select(static found => found.Plan + "=" + (found.Templates.Count / 2))
                .ToArray(),
            "a plan or a query not swept here leaves this matrix blind to what it asks for.");
    }

    /// <summary>
    /// Exactly these relation families are asked for by a real query today.
    /// </summary>
    /// <remarks>
    /// Re-derive rather than hand-edit when this fails: print the computed set from a throwaway
    /// test and paste it. Building the expected side from the same sweep inside this test would make
    /// it agree with whatever the code says, which is the one thing a pin must not do.
    /// </remarks>
    [TestMethod]
    public void ExactlyTheseRelationFamiliesAreAskedForByARealQuery()
    {
        var templates = RenderedQueries().SelectMany(static found => found.Templates).ToArray();

        var asked = Enum.GetValues<EuRelationFamily>()
            .Where(family => AskedFor(family, templates))
            .OrderBy(static family => (int)family)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                EuRelationFamily.Amends,
                EuRelationFamily.Corrects,
                EuRelationFamily.BasedOn,
                EuRelationFamily.ConsolidatedBasedOn,
                EuRelationFamily.CaseLawInterpretes,
                EuRelationFamily.CaseLawDeclaresVoidByPreliminaryRuling,
            },
            asked,
            "a family becoming asked for, or ceasing to be, is a change to what this corpus covers.");

        Assert.HasCount(
            7, Enum.GetValues<EuRelationFamily>().Except(asked).ToArray(),
            "seven of the thirteen are asked for by nothing, which is the honest residue.");
    }

    /// <summary>
    /// The two case-law families E6 asks for are asked for WITHOUT widening the read-relation list.
    /// </summary>
    /// <remarks>
    /// That separation is deliberate and worth a guard of its own. <c>ReadRelationFamilies</c> names
    /// the families the object-facts snapshot pipeline reads into
    /// <c>EuRelationFamilyDisposition</c>; E6 deliberately did not join it, because widening that
    /// list would mint vocabulary the authority has not proven. So "asked for by a query" and "read
    /// into the relation disposition" are two different sets, and the stale sentence this matrix
    /// replaces was ambiguous between them.
    /// </remarks>
    [TestMethod]
    public void AskedForByAQueryAndReadIntoTheDispositionAreDeliberatelyDifferentSets()
    {
        CollectionAssert.DoesNotContain(
            EuScopeVocabulary.ReadRelationFamilies.ToArray(),
            EuRelationFamily.CaseLawInterpretes,
            "E6 asks for this predicate without joining the read-relation list.");
        CollectionAssert.DoesNotContain(
            EuScopeVocabulary.ReadRelationFamilies.ToArray(),
            EuRelationFamily.CaseLawDeclaresVoidByPreliminaryRuling,
            "and the same for the second of E6's two.");

        Assert.HasCount(
            4, EuScopeVocabulary.ReadRelationFamilies,
            "the read-relation list is unchanged by E6 having a live query.");
    }

    /// <summary>
    /// The third case-law family is asked for by nothing, which is a fact about coverage.
    /// </summary>
    /// <remarks>
    /// <see cref="EuRelationFamily.SubmitsPreliminaryQuestion"/> is not among E6's five pinned
    /// predicates, so of the three case-law families exactly one remains unasked. Stated as its own
    /// guard because that is the residue the prose used to describe wholesale and wrongly.
    /// </remarks>
    [TestMethod]
    public void TheThirdCaseLawFamilyRemainsAskedForByNothing()
    {
        var templates = RenderedQueries().SelectMany(static found => found.Templates).ToArray();

        Assert.IsFalse(
            AskedFor(EuRelationFamily.SubmitsPreliminaryQuestion, templates),
            "no query asks for the preliminary-question family; that absence is the honest record.");

        // And it is a COMMUNICATION-CASE predicate, not a case-law one. The prose this matrix
        // replaces spoke of "all three case-law families", but only two families carry the
        // case-law_ prefix and both are now asked for; the third it counted is this one.
        StringAssert.StartsWith(
            TokenOf(EuRelationFamily.SubmitsPreliminaryQuestion), "communication_case_new_",
            "the third family the old sentence counted is not a case-law predicate at all.");
        StringAssert.StartsWith(
            TokenOf(EuRelationFamily.CaseLawInterpretes), "case-law_");
        StringAssert.StartsWith(
            TokenOf(EuRelationFamily.CaseLawDeclaresVoidByPreliminaryRuling), "case-law_");
    }
}

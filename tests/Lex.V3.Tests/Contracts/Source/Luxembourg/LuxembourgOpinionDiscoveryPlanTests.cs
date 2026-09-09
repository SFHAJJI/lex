using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// Stage 2 item E8's live half, slice one: the Legilux family that enumerates Conseil d'État opinion
/// events with the locator of their resulting document and their own date.
/// </summary>
/// <remarks>
/// The class and predicate IRIs below are written out as independent literals rather than read from
/// <see cref="LuxembourgOpinionLinkOnlyVocabulary"/>. Comparing the plan against the same constants
/// the plan is built from would be a comparison between a value and itself, which is a defect this
/// repository has already had to fix once in the E6 test file.
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionDiscoveryPlanTests
{
    private const string OpinionClass =
        "http://data.legilux.public.lu/resource/ontology/jolux#OpinionConseilEtat";
    private const string ResultingDocument =
        "http://data.legilux.public.lu/resource/ontology/jolux#hasResultingOpinionDocument";
    private const string OpinionDate =
        "http://data.legilux.public.lu/resource/ontology/jolux#opinionDate";

    private static MachineQueryRendererSource Source()
    {
        var bytes = Encoding.UTF8.GetBytes("lu-conseil-etat-opinion-renderer-source/1\n");
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:4e8b3a51-7c26-4d0f-9b84-1a6d5e2c7f39",
                Convert.ToHexStringLower(SHA256.HashData(bytes))),
            bytes);
    }

    private static string SentBody(LuxembourgOpinionBoundQuery bound) =>
        Uri.UnescapeDataString(
            Encoding.UTF8.GetString(bound.Request.CopyRequestBody())["query=".Length..]);

    /// <summary>
    /// The query asks for a locator and a date, and has no way to ask for the opinion's text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is E8's link-only decision written one layer earlier than the record that enforces it.
    /// <see cref="LuxembourgOpinionLinkOnlyRecord"/> has nowhere to put a body; this plan never
    /// fetches one to put there. A licence would have to change both, which is the point of writing
    /// it twice rather than relying on the record alone.
    /// </para>
    /// <para>
    /// Asserted as an exact predicate census rather than a search for words like "text" or
    /// "content": a blocklist only refuses the shapes somebody thought of, while pinning the whole
    /// set of publisher terms the query names refuses everything nobody thought of too.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheQueryNamesOnlyTheThreePublisherTermsItNeedsAndNoTextPredicate()
    {
        var plan = LuxembourgOpinionDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            var iris = System.Text.RegularExpressions.Regex
                .Matches(template, "<(?<iri>[^<>]+)>")
                .Select(match => match.Groups["iri"].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { ResultingDocument, OpinionDate, OpinionClass }
                    .Order(StringComparer.Ordinal).ToArray(),
                iris,
                "the query names exactly the class it sweeps and the two edges it reads.");
        }
    }

    /// <summary>
    /// Both absences are asked for explicitly, and neither is left to be inferred from silence.
    /// </summary>
    /// <remarks>
    /// Roughly half the 13,009 opinion events carry no resulting document and a quarter carry no
    /// date, so absence here is the ordinary case rather than the corner. A query that simply failed
    /// to match those events would report a corpus half its real size while looking complete. The
    /// pinned triple in each branch is what makes this more than a word search: a template that
    /// mentioned the predicate anywhere would satisfy a substring check, and this seat has already
    /// shipped exactly that weaker guard on the E6 plan and had it found in review.
    /// </remarks>
    [TestMethod]
    public void EachEdgeHasItsOwnAskedForAbsenceBranchRatherThanBeingLeftToSilence()
    {
        var plan = LuxembourgOpinionDiscoveryPlan.Create();

        foreach (var template in new[] { plan.CountTemplate, plan.PageTemplate })
        {
            StringAssert.Contains(
                template,
                $"FILTER NOT EXISTS {{ ?opinion <{ResultingDocument}> ?missing_document }}",
                "a document-less opinion is delivered, not dropped.");
            StringAssert.Contains(
                template,
                $"FILTER NOT EXISTS {{ ?opinion <{OpinionDate}> ?missing_date }}",
                "a date-less opinion is delivered, not dropped.");
            StringAssert.Contains(template, "BIND(\"unbound\" AS ?document_kind)");
            StringAssert.Contains(template, "BIND(\"unbound\" AS ?date_kind)");

            // The present branch pins its triple too, so the marker cannot be the only evidence the
            // edge was asked about.
            StringAssert.Contains(template, $"?opinion <{ResultingDocument}> ?document .");
            StringAssert.Contains(template, $"?opinion <{OpinionDate}> ?opinion_date .");
        }
    }

    /// <summary>The delivery profile pins the whole row and names no caller selection.</summary>
    /// <remarks>
    /// The family's scope is a class, not a caller's list, so there is nothing for a caller to
    /// widen. That is asserted rather than assumed, because an empty selection is also what makes
    /// this plan's parameter ordering trivially correct.
    /// </remarks>
    [TestMethod]
    public void TheDeliveryProfilePinsTheWholeRowAndOffersNoCallerSelection()
    {
        var profile = LuxembourgOpinionDiscoveryPlan.Create().CreateDeliveryProfile();

        Assert.AreEqual(RepeatedEnumerationSparqlJsonDialect.LuxembourgVirtuoso, profile.Dialect);
        CollectionAssert.AreEqual(
            new[]
            {
                "opinion", "document", "document_kind", "opinion_date", "date_kind", "multiplicity",
                "key_1", "key_2", "key_3",
            },
            profile.ProjectionVariables.ToArray());
        CollectionAssert.AreEqual(
            new[] { "key_1", "key_2", "key_3" }, profile.CursorVariables.ToArray());
        CollectionAssert.AreEqual(
            profile.CanonicalKeyVariables.ToArray(), profile.CursorVariables.ToArray());
        Assert.IsEmpty(profile.SelectionParameterNames, "this family's scope is a class, not a batch.");
        Assert.AreEqual(
            RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, profile.TerminalPagePolicy);
    }

    /// <summary>
    /// The bound input names the roles the delivery proof expects, in the order it compares them.
    /// </summary>
    /// <remarks>
    /// <c>RepeatedEnumerationDeliveryProof.RequireInputRoleShape</c> builds its expectation as
    /// <c>SelectionParameterNames.Append(PassParameterName)</c> and compares the ordered roles by
    /// sequence. The EU case-law family bound <c>pass_id</c> ahead of its fifty selection parameters
    /// and therefore could refuse correctly but never succeed — every honest delivery reached
    /// <c>DeliveryProofRefused</c>. This family's selection is empty, so the two orders coincide;
    /// the guard exists so that a later slice adding a selection cannot reintroduce the defect
    /// silently.
    /// </remarks>
    [TestMethod]
    public void TheBoundInputNamesTheRolesTheDeliveryProofComparesBySequence()
    {
        var plan = LuxembourgOpinionDiscoveryPlan.Create();
        var profile = plan.CreateDeliveryProfile();

        var count = plan.BindCount(
            LuxembourgQueryPass.Pass1,
            "urn:uuid:5f9c2b60-8d37-4e1a-ac95-2b7e6f3d84a1",
            "urn:uuid:6a0d3c71-9e48-4f2b-bd06-3c8f7a4e95b2",
            Source());

        CollectionAssert.AreEqual(
            profile.SelectionParameterNames.Append(profile.PassParameterName).ToArray(),
            count.InputArtifact.OrderedParameters.Select(static parameter => parameter.Name).ToArray(),
            "this is the exact expectation RequireInputRoleShape builds and compares by sequence.");
    }

    /// <summary>Binding fills every slot and sends no renderer placeholder.</summary>
    [TestMethod]
    public void BindingFillsEverySlotAndSendsNoPlaceholder()
    {
        var plan = LuxembourgOpinionDiscoveryPlan.Create();
        var source = Source();

        var count = plan.BindCount(
            LuxembourgQueryPass.Pass1,
            "urn:uuid:7b1e4d82-af59-4a3c-ce17-4d90b8f5a6c3",
            "urn:uuid:8c2f5e93-b06a-4b4d-df28-5ea1c9061b7d",
            source);
        var page = plan.BindPage(
            LuxembourgQueryPass.Pass2, null, 0, count.InputArtifact.ArtifactRef,
            "urn:uuid:9d306fa4-c17b-4c5e-e039-6fb2da172c8e",
            "urn:uuid:ae4170b5-d28c-4d6f-f14a-70c3eb283d9f",
            source);

        foreach (var body in new[] { SentBody(count), SentBody(page) })
        {
            Assert.IsFalse(body.Contains("{pass_id:uint}", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains(":sparql_string}", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("{page_limit:uint}", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("{has_cursor:uint}", StringComparison.Ordinal));
            StringAssert.Contains(body, OpinionClass);
        }

        // The two passes ask the same question and are distinguishable, which is what makes an
        // independent second delivery a second observation rather than a repeat of the first.
        Assert.AreNotEqual(SentBody(count), SentBody(page));
    }

    /// <summary>A cursor of the wrong length is refused rather than padded or truncated.</summary>
    /// <remarks>
    /// The message names the plan's own key count rather than a transcribed number, so a keyset that
    /// grows cannot leave this refusal describing the old shape.
    /// </remarks>
    [TestMethod]
    public void ACursorThatIsNotTheKeysetIsRefused()
    {
        var plan = LuxembourgOpinionDiscoveryPlan.Create();
        var source = Source();
        var count = plan.BindCount(
            LuxembourgQueryPass.Pass1,
            "urn:uuid:bf5281c6-e39d-4e70-025b-81d4fc394ea0",
            "urn:uuid:c063920d-f4ae-4f81-136c-92e50d4a5fb1",
            source);

        Assert.ThrowsExactly<ArgumentException>(() => plan.BindPage(
            LuxembourgQueryPass.Pass1, ["only", "two"], 0, count.InputArtifact.ArtifactRef,
            "urn:uuid:d174a31e-05bf-4092-247d-a3f61e5b60c2",
            "urn:uuid:e285b42f-16c0-40a3-358e-b4072f6c71d3",
            source));
    }

    /// <summary>
    /// A count query has no way to carry a cursor, because its door has no cursor parameter.
    /// </summary>
    /// <remarks>
    /// Written as a signature assertion after the first version of this test failed. That version
    /// called <see cref="LuxembourgOpinionDiscoveryPlan.BindPage"/> with a cursor and expected a
    /// refusal, which was simply wrong — a page may carry one. Looking for the real refusal found
    /// that the sibling plan's "a count query cannot carry a cursor" branch is unreachable:
    /// <c>BindCount</c> has no cursor parameter, so nothing can reach it. The invariant is real and
    /// the enforcement is the signature, so that is what is pinned here rather than a branch no
    /// caller can execute.
    /// </remarks>
    [TestMethod]
    public void TheCountDoorHasNoCursorParameterToCarry()
    {
        var parameters = typeof(LuxembourgOpinionDiscoveryPlan)
            .GetMethod(nameof(LuxembourgOpinionDiscoveryPlan.BindCount))!
            .GetParameters()
            .Select(static parameter => parameter.Name)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "pass", "machinePlanResourceId", "inputResourceId", "rendererSource" },
            parameters,
            "a count asks how many rows exist, so there is nothing for a cursor to continue.");

        // The page door does take one, which is what makes the absence above a decision.
        CollectionAssert.Contains(
            typeof(LuxembourgOpinionDiscoveryPlan)
                .GetMethod(nameof(LuxembourgOpinionDiscoveryPlan.BindPage))!
                .GetParameters()
                .Select(static parameter => parameter.Name)
                .ToArray(),
            "cursor");
    }

    /// <summary>
    /// The plan's identity is a digest of the question it asks, so the templates are inside it.
    /// </summary>
    /// <remarks>
    /// A plan whose identity did not cover its own query text could change what it asks the
    /// publisher while every retained artifact still named the same source, which would make the
    /// custody record a claim about a question nobody could reconstruct.
    /// </remarks>
    [TestMethod]
    public void TheCanonicalIdentityCoversTheQueriesItSends()
    {
        var plan = LuxembourgOpinionDiscoveryPlan.Create();
        var identity = Encoding.UTF8.GetString(plan.CopyCanonicalIdentityBytes());

        StringAssert.Contains(identity, plan.CountTemplate.Trim());
        StringAssert.Contains(identity, plan.PageTemplate.Trim());
        StringAssert.Contains(identity, OpinionClass);
        StringAssert.Contains(identity, ResultingDocument);
        StringAssert.Contains(identity, OpinionDate);

        // Two independently created plans are the same question, so the same identity.
        Assert.AreEqual(plan.ArtifactRef.Sha256, LuxembourgOpinionDiscoveryPlan.Create().ArtifactRef.Sha256);
    }
}

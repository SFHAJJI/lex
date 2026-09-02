using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The Union manifestation and rights scope.
///
/// The load-bearing test is the enlargement counterexample: format availability is a property of a
/// language expression, and any type that lets it be stated per work produces a false absence the
/// first time it meets a special edition.
/// </summary>
[TestClass]
public sealed class EuManifestationScopeTests
{
    private const string Boundary = EuManifestationScope.CanonicalFormexAvailableFrom;

    [TestMethod]
    public void EveryOfferedFormatIsAMemberIncludingTheOnesNeverFetched()
    {
        // Pinned by hand, in declaration order. A format missing from the vocabulary would mean
        // "not considered" rather than "considered and refused".
        AssertTokens<EuManifestationFormat>(
            "fmx4", "xhtml", "xhtml5", "html", "pdf", "pdfa1a", "pdfa1b", "pdfa2a", "print");
        AssertTokens<EuFormatBodyAdmission>("body_admitted", "body_not_admitted");
    }

    [TestMethod]
    public void Akn4EuIsNotAMemberBecauseThePublisherDisseminatesNoLegalActInIt()
    {
        // Listing it would imply we chose not to fetch something on offer. The AKN4EU types that
        // exist belong to schema releases, not to legal acts.
        foreach (var name in Enum.GetNames<EuManifestationFormat>())
        {
            Assert.IsFalse(
                name.Contains("Akn", StringComparison.OrdinalIgnoreCase),
                $"{name} implies the publisher offers AKN4EU for legal acts");
        }
    }

    [TestMethod]
    public void FormatDispositionsAreExhaustiveAndAtLeastOneAdmitsABody()
    {
        foreach (var missing in Enum.GetValues<EuManifestationFormat>())
        {
            var partial = FullFormats().Where(d => d.Format != missing).ToArray();
            if (!partial.Any(d => d.Admission == EuFormatBodyAdmission.BodyAdmitted))
            {
                continue;
            }

            var thrown = Assert.ThrowsExactly<ArgumentException>(() => Scope(partial));
            StringAssert.Contains(thrown.Message, missing.ToString());
        }

        var noneAdmitted = Enum.GetValues<EuManifestationFormat>()
            .Select(f => Format(f, EuFormatBodyAdmission.BodyNotAdmitted))
            .ToArray();
        var refused = Assert.ThrowsExactly<ArgumentException>(() => Scope(noneAdmitted));
        StringAssert.Contains(refused.Message, "no text could ever be held");
    }

    [TestMethod]
    public void PrintCanNeverBeAdmittedAsABodySourceAndTheRuleLivesInTheContract()
    {
        // Walked over all seven rather than sampled, because the interesting claim is as much about
        // which formats the rule does not cover as which it does. Print is a physical manifestation
        // and no configuration reads a digital body off paper, so the contract refuses it. Every
        // other format is a per-scope judgement and must stay constructible both ways, or the type
        // would be inventing a publisher fact nobody established.
        foreach (var format in Enum.GetValues<EuManifestationFormat>())
        {
            var dispositions = Enum.GetValues<EuManifestationFormat>()
                .Select(f => Format(
                    f,
                    f == format || f != EuManifestationFormat.Print
                        ? EuFormatBodyAdmission.BodyAdmitted
                        : EuFormatBodyAdmission.BodyNotAdmitted))
                .ToArray();

            if (format == EuManifestationFormat.Print)
            {
                var thrown = Assert.ThrowsExactly<ArgumentException>(
                    () => Scope(dispositions),
                    "print was admitted as a body source");
                StringAssert.Contains(thrown.Message, "can never carry a body");
            }
            else
            {
                _ = Scope(dispositions);
            }
        }

        CollectionAssert.AreEquivalent(
            new[] { EuManifestationFormat.Print },
            EuManifestationScope.FormatsThatCanNeverCarryABody.ToArray());
    }

    [TestMethod]
    public void ThePrintRuleHoldsOnTheWireToo()
    {
        // A document could otherwise carry a shape the constructor refuses.
        var json = ContractJson.Serialize(Scope(FullFormats()));
        var hostile = json.Replace(
            "\"format\":\"print\",\"admission\":\"body_not_admitted\"",
            "\"format\":\"print\",\"admission\":\"body_admitted\"",
            StringComparison.Ordinal);

        Assert.AreNotEqual(json, hostile, "the hostile rewrite did not match the serialized shape");
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuManifestationScope>(hostile));
    }

    /// <summary>
    /// The reuse basis is read from the publisher's notice, and a record claiming another is
    /// refused.
    /// </summary>
    /// <remarks>
    /// Pinned against literals rather than against <c>BasisFor</c>, because a test that asks the
    /// mapping what the mapping says holds under every change to it. Both hostile pairings are the
    /// ones that constructed before this boundary existed.
    /// </remarks>
    [TestMethod]
    public void TheReuseBasisIsReadFromTheNoticeAndAMismatchIsRefused()
    {
        AssertTokens<EuContentClass>(
            "metadata", "consolidation", "summary", "original_legal_text", "editorial_content");
        AssertTokens<EuReuseBasis>("cc0", "cc_by_4_0", "decision_2011_833_eu");

        Assert.AreEqual(EuReuseBasis.Cc0, EuRightsDisposition.BasisFor(EuContentClass.Metadata));
        Assert.AreEqual(
            EuReuseBasis.CcBy40,
            EuRightsDisposition.BasisFor(EuContentClass.EditorialContent));
        Assert.AreEqual(EuReuseBasis.CcBy40, EuRightsDisposition.BasisFor(EuContentClass.Summary));
        Assert.AreEqual(
            EuReuseBasis.CcBy40,
            EuRightsDisposition.BasisFor(EuContentClass.Consolidation));
        Assert.AreEqual(
            EuReuseBasis.Decision2011833Eu,
            EuRightsDisposition.BasisFor(EuContentClass.OriginalLegalText));

        // Metadata as CC BY states an attribution obligation over a public domain dedication.
        Assert.ThrowsExactly<ArgumentException>(
            () => new EuRightsDisposition(
                EuContentClass.Metadata, EuReuseBasis.CcBy40, Evidence("bb")));

        // Original legal text as CC0 states a public domain dedication over published law, whose
        // basis reserves an exception. This is the pairing that mattered most.
        Assert.ThrowsExactly<ArgumentException>(
            () => new EuRightsDisposition(
                EuContentClass.OriginalLegalText, EuReuseBasis.Cc0, Evidence("bb")));

        // And every class refuses every basis that is not its own, so the two above are examples
        // of a rule rather than the whole of it.
        foreach (var contentClass in Enum.GetValues<EuContentClass>())
        {
            foreach (var basis in Enum.GetValues<EuReuseBasis>())
            {
                if (basis == EuRightsDisposition.BasisFor(contentClass))
                {
                    continue;
                }

                Assert.ThrowsExactly<ArgumentException>(
                    () => new EuRightsDisposition(contentClass, basis, Evidence("bb")),
                    $"{contentClass} was allowed to carry {basis}");
            }
        }
    }

    /// <summary>
    /// Third-party material and document-specific terms are an axis, not content classes, and
    /// neither can be resolved for an item yet.
    /// </summary>
    /// <remarks>
    /// The surface is pinned exactly rather than by a list of forbidden words. A member named
    /// <c>Present</c>, <c>Resolved</c> or <c>Absent</c> would let a caller state for one document
    /// what only an observation could establish, and a denylist only refuses the words somebody
    /// thought of.
    /// </remarks>
    [TestMethod]
    public void TheExceptionChannelsAreAnAxisAndCarryNoItemResolution()
    {
        AssertTokens<EuRightsExceptionChannel>("third_party_material", "document_specific_terms");

        foreach (var name in Enum.GetNames<EuContentClass>())
        {
            Assert.IsFalse(
                name.Contains("ThirdParty", StringComparison.Ordinal)
                || name.Contains("Restricted", StringComparison.Ordinal)
                || name.Contains("Specific", StringComparison.Ordinal),
                $"{name} makes an element-level exception into a whole-object class");
        }

        var declared = typeof(EuRightsExceptionDisposition)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(member => $"{member.MemberType} {member}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "Constructor Void .ctor(Lex.V3.Contracts.Source.Europe."
                + "EuRightsExceptionChannel, Lex.V3.Contracts.Source.Core.SourceArtifactRef)",
                "Method Boolean Equals(Lex.V3.Contracts.Source.Europe.EuRightsExceptionDisposition)",
                "Method Boolean Equals(System.Object)",
                "Method Int32 GetHashCode()",
                "Method Lex.V3.Contracts.Source.Core.SourceArtifactRef get_EvidenceRef()",
                "Method Lex.V3.Contracts.Source.Europe.EuRightsExceptionChannel get_Channel()",
                "Method Lex.V3.Contracts.Source.Europe.EuRightsExceptionDisposition <Clone>$()",
                "Method System.String ToString()",
                "Property Lex.V3.Contracts.Source.Core.SourceArtifactRef EvidenceRef",
                "Property Lex.V3.Contracts.Source.Europe.EuRightsExceptionChannel Channel",
            },
            declared,
            "the exception disposition's surface changed; it now declares "
            + string.Join(" | ", declared));

        foreach (var channel in Enum.GetValues<EuRightsExceptionChannel>())
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => new EuRightsExceptionDisposition(channel, null!),
                $"{channel} was allowed to exist with no evidence that it exists");
        }

        // Membership, not only vocabulary. The first version of this test pinned the enum and the
        // disposition's surface and never asked whether a scope carried them, so both types could
        // sit beside the model asserting nothing while a serialized scope stayed silent about the
        // two conditions that can override its class answers.
        var scope = Scope(FullFormats());
        CollectionAssert.AreEqual(
            Enum.GetValues<EuRightsExceptionChannel>(),
            scope.Exceptions.Select(disposition => disposition.Channel).ToArray());

        foreach (var missing in Enum.GetValues<EuRightsExceptionChannel>())
        {
            var partial = FullExceptions().Where(d => d.Channel != missing).ToArray();
            var thrown = Assert.ThrowsExactly<ArgumentException>(
                () => Scope(FullFormats(), exceptions: partial));
            StringAssert.Contains(thrown.Message, missing.ToString());
        }

        var doubled = FullExceptions()
            .Append(new EuRightsExceptionDisposition(
                EuRightsExceptionChannel.ThirdPartyMaterial, Evidence("cc")))
            .ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => Scope(FullFormats(), exceptions: doubled));

        Assert.ThrowsExactly<ArgumentException>(
            () => Scope(FullFormats(), exceptions: new EuRightsExceptionDisposition[] { null! }));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuManifestationScope(
                FullFormats(), FullRights(), null!, Boundary, Evidence("aa")));

        // A caller keeping its own reference cannot reach in afterwards.
        var supplied = FullExceptions();
        var retained = Scope(FullFormats(), exceptions: supplied);
        supplied[0] = new EuRightsExceptionDisposition(
            EuRightsExceptionChannel.DocumentSpecificTerms, Evidence("cc"));
        Assert.AreEqual(
            EuRightsExceptionChannel.ThirdPartyMaterial,
            retained.Exceptions[0].Channel,
            "the scope followed a caller's later edit");

        // And the same closure holds on the wire, not only in the constructor. The document is
        // edited as a node tree rather than as text, because hand-quoting JSON inside a C# string
        // is how a test ends up proving that a malformed document is rejected.
        var document = JsonNode.Parse(ContractJson.Serialize(retained))!.AsObject();
        var channels = document["exceptions"]!.AsArray();

        // The deserializer wraps a constructor refusal, so the inner exception is asserted too.
        // Without that this would pass on any malformed document, which is the vacuous shape: it
        // would prove the parser works rather than that the closure holds.
        static void RefusedOnTheWire(JsonObject document, string why)
        {
            var thrown = Assert.ThrowsExactly<JsonException>(
                () => ContractJson.Deserialize<EuManifestationScope>(document.ToJsonString()),
                why);
            Assert.IsInstanceOfType<ArgumentException>(
                thrown.InnerException,
                $"{why}: refused, but not by the closure");
        }

        var dropped = channels.Deserialize<JsonArray>()!;
        dropped.RemoveAt(dropped.Count - 1);
        document["exceptions"] = dropped;
        RefusedOnTheWire(document, "a scope missing an exception channel survived deserialization");

        var duplicated = channels.Deserialize<JsonArray>()!;
        duplicated.Add(JsonNode.Parse(duplicated[0]!.ToJsonString()));
        document["exceptions"] = duplicated;
        RefusedOnTheWire(document, "a duplicated channel survived deserialization");

        var unknown = channels.Deserialize<JsonArray>()!;
        unknown[0]!.AsObject()["channel"] = "no_such_channel";
        document["exceptions"] = unknown;
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuManifestationScope>(document.ToJsonString()),
            "an unknown channel token survived deserialization");
    }

    [TestMethod]
    public void EveryContentClassCarriesOneBasis()
    {
        foreach (var missing in Enum.GetValues<EuContentClass>())
        {
            var partial = FullRights().Where(d => d.ContentClass != missing).ToArray();
            var thrown = Assert.ThrowsExactly<ArgumentException>(
                () => Scope(FullFormats(), partial));
            StringAssert.Contains(thrown.Message, missing.ToString());
        }
    }

    [TestMethod]
    public void EveryBasisNeedsItsEvidence()
    {
        // Walked over the classes rather than over the bases, because the basis is no longer a
        // caller's choice: each class is paired with the one the notice gives it, which reaches
        // every basis while constructing nothing the notice does not say.
        foreach (var contentClass in Enum.GetValues<EuContentClass>())
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => new EuRightsDisposition(
                    contentClass,
                    EuRightsDisposition.BasisFor(contentClass),
                    null!),
                $"{contentClass} was allowed to carry no evidence");
        }
    }

    [TestMethod]
    public void FormatAvailabilityIsAnExpressionFactAndTheEnlargementCaseProvesWhy()
    {
        // 32004R0139 carries no Formex in its English or French expressions and does carry it in
        // Bulgarian, Croatian and Romanian from the 2007 and 2013 enlargement special editions, so a
        // per-work claim is false for three languages while being true for the one somebody checked.
        //
        // The work is no longer restated on the fact. It is the expression's parent, which is where
        // the publisher puts it and the only place it can be proved rather than asserted.
        foreach (var language in new[]
                 {
                     EuOfficialLanguage.Bulgarian,
                     EuOfficialLanguage.Croatian,
                     EuOfficialLanguage.Romanian,
                 })
        {
            var fact = new EuExpressionFormatFact(
                IdentityBoundary(),
                language,
                Expression($"{WorkUuid}.0006"),
                EuManifestationFormat.Formex4,
                Evidence("cc"));
            Assert.AreEqual(language, fact.Language);
            Assert.AreEqual(WorkUuid, fact.ExpressionRef.ParentKeyRef!.CanonicalKey);
        }

        // The other half is structural: there is no way to record that English lacks Formex,
        // because nothing in this slice can establish it. TheTypeCannotStateAnAbsenceAtAll holds it.
    }

    [TestMethod]
    public void TheTypeCannotStateAnAbsenceAtAll()
    {
        // A content-bound observation reference proves which bytes were named. It does not prove
        // the manifestation enumeration for that expression ran to completion, and only a complete
        // bounded observation supports an absence. Formats do not get a second, weaker completion
        // mechanism than the relation families already follow, so a negative is deferred to the
        // later source-completion validator and cannot be expressed here at all.
        //
        // Before this rule the constructor took a present flag, and passing false minted exactly
        // that absence from an arbitrary artifact reference.
        var members = typeof(EuExpressionFormatFact)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(member => member.Name)
            .ToArray();

        foreach (var forbidden in new[] { "Present", "Absent", "Missing", "NotHeld", "IsHeld" })
        {
            Assert.IsFalse(
                members.Contains(forbidden, StringComparer.Ordinal),
                $"{forbidden} lets this slice mint an absence with no completion evidence");
        }

        foreach (var parameter in typeof(EuExpressionFormatFact)
                     .GetConstructors().Single().GetParameters())
        {
            Assert.AreNotEqual(
                typeof(bool),
                parameter.ParameterType,
                $"the parameter {parameter.Name} can carry a negative into the fact");
        }
    }

    [TestMethod]
    public void ThereIsNoWayToStateFormatAbsenceForAWork()
    {
        // The type must not offer the shape at all. A work-level answer could only be produced by
        // quantifying over expressions this type does not hold, so any such member would be a false
        // absence wearing a convenient name.
        var members = typeof(EuExpressionFormatFact)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(member => member.Name)
            .ToArray();

        foreach (var member in members)
        {
            Assert.IsFalse(
                member.Contains("WorkHas", StringComparison.Ordinal) ||
                member.Contains("AbsentForWork", StringComparison.Ordinal) ||
                member.Contains("AnyLanguage", StringComparison.Ordinal),
                $"{member} answers a format question at the work level");
        }

        // Language is required to construct one at all, which is the structural half of the rule.
        var required = typeof(EuExpressionFormatFact)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType);
        CollectionAssert.Contains(required.ToArray(), typeof(EuOfficialLanguage));
    }

    [TestMethod]
    public void APositiveFactStillCarriesTheObservationThatFoundIt()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuExpressionFormatFact(
                IdentityBoundary(),
                EuOfficialLanguage.English,
                Expression($"{WorkUuid}.0006"),
                EuManifestationFormat.Formex4,
                observationRef: null!));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuExpressionFormatFact(
                boundary: null!,
                EuOfficialLanguage.English,
                Expression($"{WorkUuid}.0006"),
                EuManifestationFormat.Formex4,
                Evidence("cc")));
    }

    [TestMethod]
    public void TheFormexBoundaryTravelsInTheRetainedBytesAndClassifiesNothing()
    {
        // A const never reaches the wire. An artifact carrying only the evidence reference would
        // not state which date that evidence supports, and editing the constant later would
        // reinterpret old bytes without changing them or their schema. So the boundary is an
        // instance value, and the serialized document must actually contain it.
        var scope = Scope(FullFormats());
        // Read through reflection rather than compared to its own literal, which the compiler
        // folds so the assertion cannot fail within a single compilation.
        Assert.AreEqual(
            "2004-05-01",
            typeof(EuManifestationScope)
                .GetField(nameof(EuManifestationScope.CanonicalFormexAvailableFrom))!
                .GetRawConstantValue());
        Assert.AreEqual(Boundary, scope.FormexAvailableFrom);

        var json = ContractJson.Serialize(scope);
        StringAssert.Contains(json, Boundary);
        Assert.AreEqual(
            Boundary,
            ContractJson.Deserialize<EuManifestationScope>(json).FormexAvailableFrom);

        // Carried for checking and partition planning only. A method here that decided availability
        // from a date would erase the two cases the record documents: consolidations of pre-2004
        // acts do carry Formex, and enlargement special editions carry it for languages whose
        // original publication predates the boundary.
        foreach (var name in new[] { "HasFormex", "IsFormexAvailable", "FormexExpectedFor" })
        {
            Assert.IsNull(
                typeof(EuManifestationScope).GetMethod(name),
                $"{name} turns the availability boundary into a classifier");
        }
    }

    [TestMethod]
    public void TheBoundaryMustBeAnExactCalendarDateAndIsRequired()
    {
        foreach (var hostile in new[]
                 {
                     "", "   ", "2004-5-1", "2004-05-01T00:00:00Z", "not-a-date", "2004-02-30",

                     // Well-formed and wrong. These are the dangerous ones: a shape check alone
                     // accepts them and serializes them beside the same evidence reference, so the
                     // retained bytes look verified while naming a boundary nobody established.
                     "2004-05-02", "2004-04-30", "1999-01-01", "2038-01-19",
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => Scope(FullFormats(), FullRights(), hostile),
                $"the boundary \"{hostile}\" was accepted");
        }

        // The canonical value itself still constructs, or the guard would refuse everything.
        Assert.AreEqual(
            EuManifestationScope.CanonicalFormexAvailableFrom,
            Scope(FullFormats()).FormexAvailableFrom);

        // And a document carrying a well-formed wrong boundary is refused on the wire too.
        var json = ContractJson.Serialize(Scope(FullFormats()));
        var drifted = json.Replace("2004-05-01", "2004-05-02", StringComparison.Ordinal);
        Assert.AreNotEqual(json, drifted, "the boundary is not in the serialized document");
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuManifestationScope>(drifted));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuManifestationScope(FullFormats(), FullRights(), FullExceptions(), Boundary, null!));
    }

    [TestMethod]
    public void MutatingTheCallersListsAfterConstructionCannotChangeTheScope()
    {
        // IReadOnlyList is a view, not a guarantee: a caller can hand a List through it and clear
        // it afterwards. Both axes must keep their own snapshot, or a scope that satisfied its
        // closure check at construction reports a different vocabulary later, and the check that
        // every member carries exactly one disposition becomes a statement about a list nobody
        // holds any more.
        var formats = new List<EuFormatDisposition>(FullFormats());
        var rights = new List<EuRightsDisposition>(FullRights());
        var exceptions = new List<EuRightsExceptionDisposition>(FullExceptions());
        var scope = new EuManifestationScope(
            formats, rights, exceptions, Boundary, Evidence("aa"));

        formats.Clear();
        rights.Clear();

        Assert.AreEqual(9, scope.Formats.Count);
        Assert.AreEqual(Enum.GetValues<EuContentClass>().Length, scope.Rights.Count);
        Assert.IsTrue(scope.Formats.Any(d => d.Admission == EuFormatBodyAdmission.BodyAdmitted));
    }

    [TestMethod]
    public void TheConstructorsRefuseUndefinedEnums()
    {
        // UnknownVocabularyFailsClosedInEveryClosedSet reaches these types through JSON, where the
        // converter does the refusing. A cast reaches the constructor without passing a converter,
        // so without these cases only the wire is closed.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new EuRightsDisposition(EuContentClass.Metadata, (EuReuseBasis)99, Evidence("bb")));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new EuExpressionFormatFact(
                IdentityBoundary(),
                (EuOfficialLanguage)999,
                Expression($"{WorkUuid}.0006"),
                EuManifestationFormat.Formex4,
                Evidence("cc")));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new EuExpressionFormatFact(
                IdentityBoundary(),
                EuOfficialLanguage.English,
                Expression($"{WorkUuid}.0006"),
                (EuManifestationFormat)99,
                Evidence("cc")));
    }

    [TestMethod]
    public void TheScopeRoundTripsAndRefusesAnUnknownFormat()
    {
        var scope = Scope(FullFormats());
        var json = ContractJson.Serialize(scope);

        StringAssert.Contains(json, "fmx4");
        StringAssert.Contains(json, "original_legal_text");

        var restored = ContractJson.Deserialize<EuManifestationScope>(json);
        Assert.AreEqual(scope.Formats.Count, restored.Formats.Count);
        Assert.AreEqual(scope.Rights.Count, restored.Rights.Count);

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuManifestationScope>(
                json.Replace("\"fmx4\"", "\"fmx5\"", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void UnknownVocabularyFailsClosedInEveryClosedSet()
    {
        AssertScopeDrift<EuManifestationFormat>("akn4eu");
        AssertScopeDrift<EuManifestationFormat>("pdfa");
        AssertScopeDrift<EuFormatBodyAdmission>("body_conditional");
        AssertScopeDrift<EuContentClass>("case_law");
        AssertScopeDrift<EuReuseBasis>("cc_by_sa_4_0");
    }

    [TestMethod]
    public void CaseLawFormatsAreOutOfScopeHereRatherThanAbsentFromThePublisher()
    {
        // The scope of this vocabulary is legal acts plus the summary class. Case-law formats are a
        // different question: an ECLI is an identifier and a relation target, and admitting one does
        // not mean this profile ingests court text. Those formats belong to the later E6 source
        // profile, which covers case-law link metadata rather than bodies.
        //
        // This is the one place where absence from the vocabulary means "another profile answers
        // this" rather than "considered and refused", so the type must not grow a member that reads
        // as a case-law disposition.
        foreach (var name in Enum.GetNames<EuManifestationFormat>())
        {
            foreach (var forbidden in new[] { "Ecli", "CaseLaw", "Judgment", "Xml" })
            {
                Assert.IsFalse(
                    name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"{name} reads as a case-law disposition in a legal-act profile");
            }
        }

        // The token still fails closed, which is correct: an unrecognised token is refused rather
        // than guessed. That refusal is about this profile and is NOT a claim that Cellar lacks
        // case-law xml, which it does serve.
        AssertScopeDrift<EuManifestationFormat>("xml");
    }

    [TestMethod]
    public void TheFactDelegatesIdentityToTheSharedBoundaryRatherThanRepeatingIt()
    {
        // One case, not a matrix. EuWemiIdentityTests owns the full attack set: wrong authority,
        // wrong registry, wrong identity profile, wrong role, malformed key, wrong suffix depth and
        // an expression of another work. Repeating it here would be a second copy of a rule that
        // must have exactly one, which is the duplication O10 objected to.
        //
        // What this proves is only that the fact routes through the boundary at all.
        var refused = Assert.ThrowsExactly<ArgumentException>(
            () => new EuExpressionFormatFact(
                IdentityBoundary(),
                EuOfficialLanguage.English,
                Expression(WorkUuid, EuWemiRole.Work),
                EuManifestationFormat.Formex4,
                Evidence("cc")));
        StringAssert.Contains(refused.Message, "eu_cellar_expression");

        // And a legitimate expression still constructs, or the delegation would be refusing all.
        var fact = new EuExpressionFormatFact(
            IdentityBoundary(),
            EuOfficialLanguage.English,
            Expression($"{WorkUuid}.0006"),
            EuManifestationFormat.Formex4,
            Evidence("cc"));
        Assert.AreEqual(SourceAuthority.Cellar, fact.ExpressionRef.Authority);
    }

    [TestMethod]
    public void TwoExpressionsOfOneWorkInOneLanguageStayDistinct()
    {
        // Work plus language does not identify an expression: a corrigendum republishes the same
        // work in the same language as a second Cellar expression, which the accepted inventory
        // records as a real shape rather than a hypothetical one.
        var first = new EuExpressionFormatFact(
            IdentityBoundary(), EuOfficialLanguage.English, Expression($"{WorkUuid}.0006"),
            EuManifestationFormat.Formex4, Evidence("cc"));
        var second = new EuExpressionFormatFact(
            IdentityBoundary(), EuOfficialLanguage.English, Expression($"{WorkUuid}.0007"),
            EuManifestationFormat.Formex4, Evidence("cc"));

        Assert.AreEqual(first.Language, second.Language);
        Assert.AreEqual(
            first.ExpressionRef.ParentKeyRef!.CanonicalKey,
            second.ExpressionRef.ParentKeyRef!.CanonicalKey,
            "the two expressions should share one work");
        Assert.AreNotEqual(
            first.ExpressionRef.CanonicalKey, second.ExpressionRef.CanonicalKey);
        Assert.AreNotEqual(first, second, "the two expressions collapsed into one fact");

        // Same observation reference on both, so the distinction cannot be coming from our bytes.
        Assert.AreEqual(first.ObservationRef, second.ObservationRef);
    }

    [TestMethod]
    public void TheRetainedBytesDoNotDependOnTheOrderTheCallerSupplied()
    {
        // Both axes are set-like maps with no semantic order, but ContractJson emits list order and
        // the canonicaliser preserves arrays. Without an imposed order, two scopes with identical
        // content digest differently purely because of how a caller built its list, which breaks
        // the deterministic retained profile.
        var forward = new EuManifestationScope(
            FullFormats(), FullRights(), FullExceptions(), Boundary, Evidence("aa"));
        var reversed = new EuManifestationScope(
            FullFormats().Reverse().ToArray(),
            FullRights().Reverse().ToArray(),
            FullExceptions().Reverse().ToArray(),
            Boundary,
            Evidence("aa"));

        var forwardJson = ContractJson.Serialize(forward);
        Assert.AreEqual(forwardJson, ContractJson.Serialize(reversed));
        Assert.AreEqual(
            Sha256Hex(forwardJson),
            Sha256Hex(ContractJson.Serialize(reversed)),
            "the same content produced two digests");

        // Ordered by the closed key, not by arrival.
        CollectionAssert.AreEqual(
            Enum.GetValues<EuManifestationFormat>(),
            reversed.Formats.Select(d => d.Format).ToArray());
        CollectionAssert.AreEqual(
            Enum.GetValues<EuContentClass>(),
            reversed.Rights.Select(d => d.ContentClass).ToArray());
    }

    private const string WorkUuid = "3e485e15-11bd-11e6-ba9a-01aa75ed71a1";

    private static EuWemiIdentityBoundary IdentityBoundary() => new(Evidence("ee"), Evidence("ff"));

    private static SourceObjectRef Expression(string key, EuWemiRole role = EuWemiRole.Expression)
    {
        var registry = Evidence("ee");
        var parentRole = EuWemiIdentityBoundary.ParentRoleOf(role);
        SourceObjectKeyRef? parent = null;
        if (parentRole is not null)
        {
            var cut = key.LastIndexOf('.');
            var parentKey = cut < 0 ? key : key[..cut];
            parent = new SourceObjectKeyRef(
                new SourceRegistryMemberRef(
                    registry, EuWemiIdentityBoundary.MemberKeyOf(parentRole.Value)),
                CellarUri(parentKey), parentKey, Sha256Hex(parentKey));
        }

        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(registry, EuWemiIdentityBoundary.MemberKeyOf(role)),
            CellarUri(key),
            key,
            Sha256Hex(key),
            Evidence("ff"),
            parent);
    }

    private static string CellarUri(string key) =>
        "http://publications.europa.eu/resource/cellar/" + key;

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static EuManifestationScope Scope(
        IReadOnlyList<EuFormatDisposition> formats,
        IReadOnlyList<EuRightsDisposition>? rights = null,
        string? boundary = null,
        IReadOnlyList<EuRightsExceptionDisposition>? exceptions = null) =>
        new(
            formats,
            rights ?? FullRights(),
            exceptions ?? FullExceptions(),
            boundary ?? Boundary,
            Evidence("aa"));

    private static EuRightsExceptionDisposition[] FullExceptions() =>
        Enum.GetValues<EuRightsExceptionChannel>()
            .Select(channel => new EuRightsExceptionDisposition(channel, Evidence("cc")))
            .ToArray();

    private static EuFormatDisposition[] FullFormats() =>
        Enum.GetValues<EuManifestationFormat>()
            .Select(format => Format(
                format,
                format is EuManifestationFormat.Print or EuManifestationFormat.PdfA1b
                    ? EuFormatBodyAdmission.BodyNotAdmitted
                    : EuFormatBodyAdmission.BodyAdmitted))
            .ToArray();

    private static EuFormatDisposition Format(
        EuManifestationFormat format,
        EuFormatBodyAdmission admission) =>
        new(format, admission, "reason_code", Evidence("dd"));

    private static EuRightsDisposition[] FullRights() =>
        Enum.GetValues<EuContentClass>()
            .Select(contentClass => new EuRightsDisposition(
                contentClass,
                EuRightsDisposition.BasisFor(contentClass),
                Evidence("bb")))
            .ToArray();

    private static SourceArtifactRef Evidence(string seed) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000" + seed, new string(seed[0], 64));

    private static void AssertTokens<TEnum>(params string[] expected)
        where TEnum : struct, Enum
    {
        var members = Enum.GetValues<TEnum>();
        Assert.AreEqual(expected.Length, members.Length, $"{typeof(TEnum).Name} member count");
        for (var index = 0; index < members.Length; index++)
        {
            Assert.AreEqual("\"" + expected[index] + "\"", ContractJson.Serialize(members[index]));
        }
    }

    private static void AssertScopeDrift<TEnum>(string hostile)
        where TEnum : struct, Enum
    {
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<TEnum>(JsonSerializer.Serialize(hostile)),
            $"{typeof(TEnum).Name} accepted the unknown token {hostile}");
    }
}

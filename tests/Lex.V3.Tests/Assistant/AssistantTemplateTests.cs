using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Platform.Assistant;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The assistant's closed template set (R31, R43): both locales for every template, the specification's masters word for
/// word, the reviewed French's typography and vocabulary, the binding rules in both directions, and provenance that claims
/// no review nobody did. Also the two result types apart (R45(a)).
/// </summary>
[TestClass]
public sealed class AssistantTemplateTests
{
    [TestMethod]
    public void EveryTemplateHasItsFrenchAndItsEnglishAndNothingElse()
    {
        Assert.AreEqual(79, V3AnswerTemplates.Ids.Count, "the set's size; a new sentence is a reviewed change to the set");
        Assert.AreEqual(V3AnswerTemplates.Ids.Count * 2, V3AnswerTemplates.All.Count);
        foreach (var id in V3AnswerTemplates.Ids)
        {
            var fr = V3AnswerTemplates.Get(id, V3AnswerLocale.Fr);
            var en = V3AnswerTemplates.Get(id, V3AnswerLocale.En);
            CollectionAssert.AreEquivalent(en.Placeholders.ToArray(), fr.Placeholders.ToArray(), $"{id}: both locales bind the same facts");
            foreach (var name in en.Placeholders)
            {
                Assert.AreEqual(en.KindOf(name), fr.KindOf(name), $"{id}.{name}");
            }
        }

        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerTemplates.Get("a.sentence.nobody.reviewed", V3AnswerLocale.En));
        Assert.IsFalse(V3AnswerTemplates.IsDefined("a.sentence.nobody.reviewed"));
    }

    [TestMethod]
    public void TheSpecificationsMastersAreWordForWord()
    {
        Assert.AreEqual(
            "I can show you exactly what the published text says, at any date, and how it changed, with citations. I cannot tell you whether or how it applies to your situation: that depends on facts about you, and this service does not apply the law to individual cases. Here is the governing text as it stood on {date}, and here is who can advise you.",
            V3AnswerTemplates.Get(V3AnswerTemplates.BoundaryWhole, V3AnswerLocale.En).Text,
            "R14's English master, [date] its one placeholder");
        Assert.AreEqual(
            "Lex shows the published text of the law. It does not tell you whether or how it applies to your situation and is not legal advice.",
            V3AnswerTemplates.Get(V3AnswerTemplates.NoticeNotLegalAdvice, V3AnswerLocale.En).Text, "R50");
        Assert.AreEqual(
            "Lex montre le texte de la loi. Il ne dit pas ce qui s’applique à votre situation, et ce n’est pas un conseil juridique.",
            V3AnswerTemplates.Get(V3AnswerTemplates.NoticeNotLegalAdvice, V3AnswerLocale.Fr).Text, "R50's French, J6's");
        Assert.AreEqual(
            "All official language versions of the act as published in the Official Journal are equally authentic. This consolidated text, in any language, is documentation without legal effect.",
            V3AnswerTemplates.Get(V3AnswerTemplates.DisclosureAuthenticityEu, V3AnswerLocale.En).Text, "R19");
        string[][] labels =
        [
            [V3AnswerTemplates.VerdictAnswer, "From the official text"],
            [V3AnswerTemplates.VerdictPoint, "Not held here: official source"],
            [V3AnswerTemplates.VerdictClarify, "Needs one detail"],
            [V3AnswerTemplates.VerdictRefuse, "Outside what this service does"],
            [V3AnswerTemplates.VerdictSplit, "Two questions"],
        ];
        foreach (var label in labels)
        {
            Assert.AreEqual(label[1], V3AnswerTemplates.Get(label[0], V3AnswerLocale.En).Text, "R28's plain labels");
        }

        var notice = V3AnswerTemplates.Get(V3AnswerTemplates.NoticeInput, V3AnswerLocale.En).Text;
        foreach (var said in new[] { "automated assistant", "published legal texts", "does not give legal advice", "apply the law to your situation", "names, health details or other personal details" })
        {
            StringAssert.Contains(notice, said, "R44's notice says it");
        }

        Assert.AreEqual(V3CorpusMount.AskContainmentReason, V3AnswerTemplates.Get(V3AnswerTemplates.PresentationAssistantUnavailable, V3AnswerLocale.En).Text,
            "the switched-off card's sentence is the presentation template's English");
    }

    [TestMethod]
    public void TheWebPanelsNoticeIsTheTemplatesWordForWord()
    {
        var panel = File.ReadAllText(Path.Combine(RepositoryRoot(), "web", "scripts", "assistant-panel.mjs"));
        foreach (var locale in new[] { V3AnswerLocale.En, V3AnswerLocale.Fr })
        {
            var text = V3AnswerTemplates.Get(V3AnswerTemplates.NoticeInput, locale).Text;
            StringAssert.Contains(panel, "'" + text + "'", $"the panel's {locale} notice is notice.input's");
        }
    }

    [TestMethod]
    public void TheFrenchKeepsItsTypographyAndNoTemplateUsesTheRefusedWords()
    {
        foreach (var template in V3AnswerTemplates.All)
        {
            var text = template.Text;
            Assert.IsFalse(Regex.IsMatch(text, "\\b(in force|applied)\\b", RegexOptions.IgnoreCase) && !text.Contains("entry into force", StringComparison.Ordinal),
                $"{template.TemplateId} ({template.Locale}): never 'in force' or 'applied' on an interval (R21)");
            Assert.IsFalse(text.Contains("texte applicable", StringComparison.OrdinalIgnoreCase), $"{template.TemplateId}: R14 never says 'texte applicable'");
            Assert.IsFalse(Regex.IsMatch(text, "(?<!entrée )en vigueur"), $"{template.TemplateId}: 'en vigueur' only as the typed entry into force (R21)");
            if (template.Locale == V3AnswerLocale.Fr)
            {
                Assert.IsFalse(Regex.IsMatch(text, " [:;?!»]|« "), $"{template.TemplateId}: a no-break space before : ; ? ! » and after «");
                Assert.IsFalse(text.Contains('\''), $"{template.TemplateId}: the French apostrophe is U+2019");
            }
        }

        StringAssert.Contains(V3AnswerTemplates.Get(V3AnswerTemplates.BoundaryWholeNoAdviser, V3AnswerLocale.Fr).Text, "tel qu’il se lisait le {date}", "R14's French form");
    }

    [TestMethod]
    public void ProvenanceClaimsNoReviewNobodyDid()
    {
        Assert.IsFalse(V3AnswerTemplates.QualifiedReviewComplete, "no template has had R43's qualified-person review");
        foreach (var template in V3AnswerTemplates.All)
        {
            var provenance = template.Provenance;
            Assert.IsFalse(provenance.QualifiedPersonReview, template.TemplateId);
            Assert.IsTrue((provenance.SourceMaster is null) != (provenance.DraftedBy is null), $"{template.TemplateId}: a master or a draft");
            if (provenance.ReviewedBy is not null)
            {
                StringAssert.Contains(provenance.ReviewedBy, "AI reviewer", template.TemplateId);
                StringAssert.Contains(provenance.ReviewedBy, "not R43's qualified person", template.TemplateId);
            }

            if (template.Locale == V3AnswerLocale.Fr && provenance.SourceMaster is null)
            {
                Assert.IsNotNull(provenance.ReviewedBy, $"{template.TemplateId}: every French draft was reviewed");
            }
        }
    }

    [TestMethod]
    public void AClaimBindsBothWaysAndWritesItsValuesByKind()
    {
        var claim = V3AnswerClaim.Bind(V3AnswerTemplates.PlanDateToday, V3AnswerLocale.Fr, new V3AnswerFact("date", V3AnswerSlotKind.CalendarDate, "2024-02-01"));
        StringAssert.EndsWith(claim.Text, "le 1er février 2024.");
        Assert.AreEqual("Your question names no date: Lex read the law as it stood today, 10 August 1991.",
            V3AnswerClaim.Bind(V3AnswerTemplates.PlanDateToday, V3AnswerLocale.En, new V3AnswerFact("date", V3AnswerSlotKind.CalendarDate, "1991-08-10")).Text);
        Assert.AreEqual("Lire l’article 1er de /lu-legilux/x tel qu’il se lisait le 2 mars 2025",
            V3AnswerClaim.Bind(V3AnswerTemplates.ActionReadArticle, V3AnswerLocale.Fr,
                new V3AnswerFact("anchor", V3AnswerSlotKind.Anchor, "art_1er"),
                new V3AnswerFact("work", V3AnswerSlotKind.WorkAddress, "/lu-legilux/x"),
                new V3AnswerFact("date", V3AnswerSlotKind.CalendarDate, "2025-03-02")).Text);

        // A hole, an unused fact, a wrong kind, a value its kind refuses: each refused.
        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerClaim.Bind(V3AnswerTemplates.PlanDateToday, V3AnswerLocale.En));
        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerClaim.Bind(V3AnswerTemplates.VerdictAnswer, V3AnswerLocale.En, new V3AnswerFact("date", V3AnswerSlotKind.CalendarDate, "2024-02-01")));
        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerClaim.Bind(V3AnswerTemplates.PlanDateToday, V3AnswerLocale.En, new V3AnswerFact("date", V3AnswerSlotKind.QuestionSpan, "2024-02-01")));
        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerClaim.Bind(V3AnswerTemplates.PlanDateToday, V3AnswerLocale.En, new V3AnswerFact("date", V3AnswerSlotKind.CalendarDate, "1 Feb 2024")));
        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerClaim.Bind(V3AnswerTemplates.LawContentHash, V3AnswerLocale.En, new V3AnswerFact("hash", V3AnswerSlotKind.ContentHash, "not-a-hash")));
        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerClaim.Bind(V3AnswerTemplates.RefusalOther, V3AnswerLocale.En, new V3AnswerFact("code", V3AnswerSlotKind.RefusalCode, "assistant_v3_unavailable")),
            "a presentation result is no refusal code");

        // A value is output and never read again as a placeholder.
        Assert.AreEqual("Lex read two questions in yours: “{second}” and “b”.",
            V3AnswerClaim.Bind(V3AnswerTemplates.PlanSplit, V3AnswerLocale.En,
                new V3AnswerFact("first", V3AnswerSlotKind.QuestionSpan, "{second}"),
                new V3AnswerFact("second", V3AnswerSlotKind.QuestionSpan, "b")).Text);
    }

    [TestMethod]
    public void TheSetsDigestIsOverItsCanonicalBytes()
    {
        var bytes = V3AnswerTemplates.CanonicalUtf8();
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(bytes)), V3AnswerTemplates.Sha256);
        using var document = JsonDocument.Parse(bytes);
        Assert.AreEqual(V3AnswerTemplates.SetId, document.RootElement.GetProperty("schema").GetString());
        Assert.AreEqual(V3AnswerTemplates.All.Count, document.RootElement.GetProperty("templates").GetArrayLength());
    }

    [TestMethod]
    public void TheSixVerdictsAndTheTwoPresentationResultsAreTypesApart()
    {
        // R45(a): two enums, no shared token, and every verdict token is V3Verdicts' (the envelope's vocabulary).
        var verdicts = Enum.GetValues<V3AssistantVerdict>().Select(static verdict => ContractWire.NameOf(verdict)).ToArray();
        var presentations = Enum.GetValues<V3AskPresentationResult>().Select(static result => ContractWire.NameOf(result)).ToArray();
        CollectionAssert.AreEqual(V3Verdicts.All.ToArray(), verdicts);
        CollectionAssert.AreEqual(new[] { "assistant_v3_unavailable", "localization_unavailable" }, presentations);
        Assert.IsFalse(verdicts.Intersect(presentations).Any());
        foreach (var presentation in presentations)
        {
            Assert.IsFalse(V3OperationRegistry.Reviewed.DeclaresRefusal(presentation), $"{presentation} is never a refusal code (Decision 91)");
            Assert.IsFalse(V3Verdicts.IsKnown(presentation), $"{presentation} is never a verdict");
        }
    }

    internal static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lex.V3.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cannot locate the V3 repository root.");
    }
}

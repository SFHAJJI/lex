using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Platform.Assistant;
using Lex.V3.Contracts.Platform.Assistant.Bricks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The bricks (V4), brick by brick, on the law of 27 July 1997 on the insurance contract as the index holds it
/// (<see cref="BricksFixture"/>): the line tables and their build checks (B), the dictionary counted on the law (D), the
/// question's row (C), the advice boundary's narrowed cue, the dispatcher's rules (E), the detectors and the sweep (I), the
/// model's contracts and the answer validator with answers made wrong on purpose (G, J), and the whole pipeline with its
/// gates and its replay (H). No test here asks a model: a scripted one hands in recorded replies.
/// </summary>
[TestClass]
public sealed class BricksTests
{
    private static readonly V4Dictionary Dictionary = V4Dictionary.Insurance;
    private static readonly V4CorpusIndex Index = V4CorpusIndex.Insurance;

    private static V4QuestionRow Row(string question) => V4QuestionParser.Parse(question, V3AnswerLocale.Fr, Dictionary, Index);

    // ---- Step 1 of brick C: the matching copy ----------------------------------------------------------------------------

    [TestMethod]
    public void TheMatchingCopyKeepsEveryOffsetAndFoldsOnlyWhatIsCompared()
    {
        const string text = "L’assureur ne peut RÉSILIER le contrat : « hormis » les cas — prévus.";
        var folded = V4Text.Fold(text);
        Assert.AreEqual(text.Length, folded.Length, "one unit for one unit");
        Assert.AreEqual("l'assureur ne peut resilier le contrat : « hormis » les cas - prevus.", folded);
        CollectionAssert.AreEqual(new[] { "l", "assureur", "ne", "peut", "resilier" }, V4Text.Words(folded).Take(5).Select(static word => word.Text).ToArray());
        Assert.AreEqual("assureur", text[V4Text.Words(folded)[1].Start..V4Text.Words(folded)[1].End], "a word's place is its place in the text as published");
    }

    [TestMethod]
    public void FormsOfOneWordMeetByRuleAndAPhraseStaysWhole()
    {
        foreach (var form in new[] { "lésée", "lésé", "lésés", "lésées" })
        {
            Assert.AreEqual(V4Text.Stem(V4Text.Fold("lésée")), V4Text.Stem(V4Text.Fold(form)), form);
        }

        Assert.AreNotEqual(V4Text.Stem("assureur"), V4Text.Stem("assure"), "the insurer and the insured are two words");
        Assert.AreEqual("les", V4Text.Stem("les"), "a short word is its own stem");

        var key = V4Key.Read("personne lésée")!;
        Assert.IsTrue(key.In(V4Text.Words(V4Text.Fold("c'est quoi une personne lésé"))), "the reader's masculine form");
        Assert.IsFalse(key.In(V4Text.Words(V4Text.Fold("les personnes sont lésées"))), "the words apart are not the phrase");
        Assert.IsTrue(V4Key.Read("résili*")!.In(V4Text.Words(V4Text.Fold("il peut résilier"))), "a prefix");
        Assert.IsTrue(V4Key.Read("prend effet")!.In(V4Text.Words(V4Text.Fold("quand la garantie prend-elle effet ?"))), "a question's inverted pronoun stands inside the key");
        Assert.IsFalse(V4Key.Read("lésé")!.In(V4Text.Words("les contrats")), "'les' is not 'lésé'");
    }

    // ---- Brick B: the tables of the act ----------------------------------------------------------------------------------

    [TestMethod]
    public void TheLinesOfEveryArticleLieInsideItsStoredTextInOrder()
    {
        var tables = BricksFixture.Tables;
        Assert.AreEqual(147, tables.Summary.Articles);
        Assert.AreEqual(1, tables.Summary.EmptyArticles, "article 7 holds no text");
        Assert.AreEqual(0, tables.Summary.UnboundArticles, "every article's pieces give back its stored text");
        Assert.IsTrue(tables.LinesAreInOrder());
        Assert.AreEqual("article", tables.Summary.Levels, "the index holds no chapter level, and the summary says so");
        CollectionAssert.AreEqual(new[] { "art_6", "art_62-6" }, tables.Summary.NumberingNotFromOne.ToArray(), "numbering that does not start at 1");
        CollectionAssert.AreEqual(new[] { "art_57" }, tables.Summary.LabelsNotCanonical.ToArray(), "a label with a typo is kept as published and listed");
        Assert.AreEqual("empty", tables.Article("art_7")!.Status);
        Assert.IsFalse(tables.LinesOf(tables.Article("art_7")!).Any(), "an empty article gives no line to quote");
    }

    [TestMethod]
    public void ALineIsAPositionInTheArticleAndReadsItsLabelItsHeadingAndItsPoints()
    {
        var tables = BricksFixture.Tables;
        var article = tables.Article("art_39")!;
        Assert.AreEqual("Article 39", article.Label);
        Assert.AreEqual("Cas, formes et effets de la résiliation", article.Heading);
        var rule = BricksFixture.Line("art_39#8");
        Assert.AreEqual("Hormis les cas prévus par la loi, l'assureur ne peut résilier le contrat.", rule.Text);
        Assert.AreEqual("3.", rule.Point);
        Assert.AreEqual("paragraph", rule.Kind);
        CollectionAssert.AreEqual(
            new[] { "label", "heading", "marker", "paragraph", "marker", "paragraph", "paragraph", "marker", "paragraph" },
            tables.LinesOf(article).Select(static line => line.Kind).ToArray());
        Assert.AreEqual("Durée des obligations", tables.Article("art_38")!.Heading, "an amendment mark before a heading does not hide it");
        Assert.AreEqual("Article 1er", tables.Article("art_1er")!.Label, "an ordinal's suffix belongs to its number");
        StringAssert.EndsWith(BricksFixture.Line("art_1er#28").Text, "la loi du 7 décembre 2015 sur le secteur des assurances.", "a reference the publisher marked stays inside its sentence");
    }

    [TestMethod]
    public void TheTermsTheLawDefinesAreRowsAndADefinitionOfSeveralLinesIsOne()
    {
        var tables = BricksFixture.Tables;
        var terms = tables.Definitions.Where(static definition => definition.Article == "art_1er").Select(static definition => definition.Term).ToArray();
        foreach (var term in new[]
                 {
                     "Contrat d'assurance", "Opération de capitalisation", "Preneur", "preneur d'assurance", "Assuré", "Bénéficiaire", "Personne lésée", "Prime",
                     "Prestation d'assurance", "Assurance de dommages", "Assurance de personnes", "Assurance à caractère indemnitaire", "Assurance à caractère forfaitaire",
                     "Note de couverture", "Proposition d'assurance", "Police présignée", "Support durable", "Loi sur le secteur des assurances",
                 })
        {
            CollectionAssert.Contains(terms, term);
        }

        var injured = tables.Definitions.Single(static definition => definition.Term == "Personne lésée");
        Assert.AreEqual((13, 13), (injured.FirstLine, injured.LastLine));
        Assert.AreEqual("Personne lésée: dans une assurance de responsabilité, la personne victime d'un dommage dont l'assuré est responsable.", BricksFixture.Line("art_1er#13").Text);
        var insured = tables.Definitions.Single(static definition => definition.Term == "Assuré");
        Assert.AreEqual((9, 11), (insured.FirstLine, insured.LastLine), "the two lines under 'Assuré:' continue it and are no terms of their own");
        CollectionAssert.Contains(tables.Definitions.Select(static definition => definition.Term).ToArray(), "avocat", "a term defined in a sentence ('on entend par «avocat» toute personne')");
        Assert.IsFalse(tables.Definitions.Any(static definition => definition.Term.StartsWith("dans une assurance", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ReferencesInPlainWordsAreResolvedOrListedAndNeverDropped()
    {
        var tables = BricksFixture.Tables;
        Assert.IsTrue(tables.References.Any(static reference => reference is { FromArticle: "art_41", Kind: "article", Target: "art_39", Resolved: true }), "'par dérogation à l'article 39 point 3'");
        Assert.IsTrue(tables.References.Any(static reference => reference is { FromArticle: "art_38", Target: "art_39", Resolved: true }));
        Assert.IsTrue(tables.References.Any(static reference => reference is { FromLine: "art_39#8", Kind: "unnamed_cases" }), "'les cas prévus par la loi' names none");
        Assert.IsTrue(tables.Summary.ReferencesExternal > 0, "an article of another act is not searched in this one");
        Assert.AreEqual(tables.References.Count(static reference => reference is { Kind: "article", Resolved: false }), tables.Summary.ReferencesUnresolved, "an unresolved reference is counted");
    }

    // ---- Brick D: the dictionary ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void EveryWordTheDictionaryCallsTheLawsOwnIsOnALineOfItsHomeAct()
    {
        var tables = BricksFixture.Tables;
        var missing = new List<string>();
        foreach (var concept in Dictionary.Concepts.Where(static concept => concept.HomeActs.Count == 1 && concept.HomeActs[0] == BricksFixture.Work))
        {
            foreach (var keyword in Dictionary.KeywordsOf(concept.Id, "law", "answer"))
            {
                if (!tables.Lines.Any(line => line.Kind != "label" && keyword.Key.In(line.Words)))
                {
                    missing.Add(concept.Id + ": " + keyword.Keyword);
                }
            }
        }

        Assert.AreEqual(string.Empty, string.Join("; ", missing), "a word that is nowhere in the law is a reader's word, and the file must say so");
    }

    [TestMethod]
    public void TheSweepCountsEveryFormOnEveryLine()
    {
        var tables = BricksFixture.Tables;
        (int Lines, int Articles) Count(string keyword)
        {
            var key = V4Key.Read(keyword)!;
            var lines = tables.Lines.Where(line => line.Kind != "label" && key.In(line.Words)).ToList();
            return (lines.Count, lines.Select(static line => line.Article).Distinct().Count());
        }

        Assert.AreEqual((49, 25), Count("résili*"), "every form of the concept: 49 lines in 25 articles");
        Assert.AreEqual((32, 19), Count("résiliation"));
        Assert.AreEqual((8, 5), Count("peut résilier"));
        Assert.AreEqual((2, 2), Count("ne peut résilier"));
        Assert.AreEqual((22, 11), Count("personne lésée"), "21 lines of text and one heading");
        Assert.AreEqual((0, 0), Count("franchise"), "a reader's word the law never uses");
        Assert.AreEqual((0, 0), Count("unilatérale"));
        Assert.AreEqual((0, 0), Count("préavis"));
    }

    [TestMethod]
    public void TheLookupReadsTheLongestKeyFirstAndARoleApartFromAConcept()
    {
        var after = Row("L'assureur peut-il résilier après un sinistre ?");
        CollectionAssert.AreEqual(new[] { "resiliation_apres_sinistre" }, after.Concepts.Select(static concept => concept.Concept).ToArray(), "the phrase is one key; its words are not read again alone");
        CollectionAssert.AreEqual(new[] { "assureur" }, after.Roles.Select(static role => role.Concept).ToArray());

        var named = Row("Que dit la loi sur le contrat d'assurance sur la prescription ?");
        Assert.AreEqual(BricksFixture.Work, named.Acts.Single().Work, "an act's short name is read before the words it holds");
        CollectionAssert.AreEqual(new[] { "prescription" }, named.Concepts.Select(static concept => concept.Concept).ToArray());

        Assert.IsTrue(Row("Quelle est la cotisation à la caisse nationale de santé ?").OtherDomain, "assurance has two meanings: a word of social insurance sends the question elsewhere");
        Assert.IsFalse(Row("Quel est le délai de prescription ?").OtherDomain);
        Assert.IsTrue(Dictionary.IsTerm("résiliation") && Dictionary.IsTerm("personne lésée") && !Dictionary.IsTerm("une phrase libre"));
    }

    // ---- Brick C: the question's row -----------------------------------------------------------------------------------------

    [TestMethod]
    public void TheRowOfTheFirstQuestionIsReadByCodeAlone()
    {
        var row = Row("c'est quoi une personne lésé");
        Assert.AreEqual("c'est quoi une personne lese", row.Folded);
        Assert.AreEqual("personne_lesee", row.Roles.Single().Concept);
        Assert.AreEqual("definition", row.Kinds.Single().Kind);
        Assert.AreEqual(0, row.Unmatched.Count);
        var term = V4Retrieval.DefinedTerms(BricksFixture.Tables, row.Words).Single();
        Assert.AreEqual(("Personne lésée", "art_1er", 13), (term.Definition.Term, term.Definition.Article, term.Definition.FirstLine));
    }

    [TestMethod]
    public void TheRowOfTheSecondQuestionKeepsTheWordNothingRead()
    {
        const string question = "l'assureur peut résilier le contrat de façon unilatérale ?";
        var row = Row(question);
        Assert.AreEqual("resiliation", row.Concepts.Single().Concept);
        Assert.AreEqual("assureur", row.Roles.Single().Concept);
        Assert.AreEqual("rule", row.Kinds.First().Kind);
        Assert.AreEqual("unilatérale", question[row.Unmatched.Single().Start..row.Unmatched.Single().End], "kept, reported, never used to block");
        Assert.AreEqual(0, V4Retrieval.LinesHolding(BricksFixture.Tables, "unilaterale"));
    }

    [TestMethod]
    public void AGreetingIsNoLegalQuestionAndAQuestionIsNoGreeting()
    {
        Assert.IsTrue(Row("Bonjour").Greeting);
        Assert.IsTrue(Row("merci beaucoup").Greeting);
        Assert.IsFalse(Row("Bonjour, quel est le délai de prescription ?").Greeting);
    }

    // ---- The advice boundary, as the owner narrowed it (decision 4) --------------------------------------------------------

    [TestMethod]
    [DataRow("L'assureur peut-il résilier le contrat ?", true)]
    [DataRow("Le preneur d'assurance doit-il déclarer une aggravation du risque ?", true)]
    [DataRow("L'assureur a-t-il le droit de résilier le contrat après un sinistre ?", true)]
    [DataRow("Mon assureur peut-il résilier mon contrat ?", false)]
    [DataRow("L'assureur peut-il résilier le contrat de mon frère ?", false)]
    [DataRow("L'assureur peut-il me résilier ?", false)]
    [DataRow("L'assureur peut-il résilier le contrat après 2 sinistres ?", false)]
    [DataRow("Dans mon cas, l'assureur peut-il résilier le contrat ?", false)]
    [DataRow("Peut-on résilier un contrat d'assurance ?", false)]
    [DataRow("Un salarié peut-il être licencié pendant un congé de maladie ?", false)]
    [DataRow("L'assureur va-t-il gagner devant le tribunal ? Quelles sont ses chances de gagner ?", false)]
    public void AQuestionOnTheRuleNamesARoleOfTheLawAndNoCaseOfTheReadersOwn(string question, bool onTheRule)
    {
        var cues = V3AssistantParsers.ApplicationCues(question, 0, question.Length);
        Assert.IsTrue(cues.Count > 0, "every one of these holds a cue of R15");
        Assert.AreEqual(onTheRule, V4Boundary.RuleQuestion(question, 0, question.Length, cues));
    }

    [TestMethod]
    public void AWordSpokenToLexIsNoMarkerOfACaseOfTheReadersOwn()
    {
        Assert.AreEqual(0, V4Boundary.PersonalMarkers("Pouvez-vous me dire ce que prévoit la loi ?", 0, 43).Count);
        Assert.AreEqual(0, V4Boundary.PersonalMarkers("Explain to me what the law says", 0, 31).Count);
        Assert.AreEqual(2, V4Boundary.PersonalMarkers("Mon assureur a résilié mon contrat", 0, 34).Count);
        Assert.AreEqual(1, V4Boundary.PersonalMarkers("J'ai déclaré le sinistre", 0, 24).Count);
        Assert.IsFalse(V4Boundary.StatesFactNumber("Que dit l'article 39 de la loi du 27 juillet 1997 ?", 0, 51), "a citation's numbers state no fact");
        Assert.IsTrue(V4Boundary.StatesFactNumber("après 2 sinistres", 0, 17));
    }

    // ---- Brick E: the dispatcher, one test per rule ----------------------------------------------------------------------------

    private static V4Decision Decide(string question, bool application = false, bool euNotHeld = false, bool held = true, bool headings = true, bool defined = false, bool model = true) =>
        V4Dispatcher.Decide(new V4DispatchInput(Row(question), application, euNotHeld, held, headings, defined, model), Dictionary);

    [TestMethod]
    public void TheDispatcherTakesTheCheapestRouteThatFits()
    {
        Assert.AreEqual("greeting", Decide("Bonjour").Route);
        Assert.AreEqual("not_held", Decide("Que dit Solvabilité II sur les fonds propres ?", euNotHeld: true).Route);
        Assert.AreEqual("absence", Decide("Quel est le délai de prescription ?", held: false).Route, "no retrieval on a text Lex cannot show");
        Assert.AreEqual("own_case", Decide("Mon assureur peut-il résilier mon contrat ?", application: true).Route);
        Assert.AreEqual("definition", Decide("c'est quoi une personne lésé", defined: true).Route);
        Assert.AreEqual("list", Decide("Dans quels cas l'assureur peut-il résilier le contrat ?").Route);
        Assert.AreEqual("rule", Decide("L'assureur peut-il résilier le contrat ?").Route);
        Assert.AreEqual("open", Decide("Quel est le délai de prescription ?").Route);
    }

    [TestMethod]
    public void TheSameQuestionOnTheSameDataTakesTheSameRouteWithItsReasons()
    {
        var first = Decide("L'assureur peut-il résilier le contrat ?");
        var again = Decide("L'assureur peut-il résilier le contrat ?");
        Assert.AreEqual(first.Route + first.Reason, again.Route + again.Reason);
        CollectionAssert.AreEqual(first.Activations.ToArray(), again.Activations.ToArray());
        V4Activation Of(V4Decision decision, string id) => decision.Activations.Single(activation => activation.Id == id);

        Assert.AreEqual(new V4Activation("title", true, "act_has_headings"), Of(first, "title"));
        Assert.AreEqual(new V4Activation("title", false, "act_has_no_headings"), Of(Decide("L'assureur peut-il résilier le contrat ?", headings: false), "title"));
        Assert.AreEqual(new V4Activation("cooccurrence", true, "row_has_actor"), Of(first, "cooccurrence"));
        Assert.AreEqual(new V4Activation("cooccurrence", false, "row_has_no_actor"), Of(Decide("Quel est le délai de prescription ?"), "cooccurrence"));
        Assert.AreEqual(new V4Activation("type_pattern", true, "kind_has_pattern"), Of(first, "type_pattern"));
        Assert.AreEqual(new V4Activation("embeddings", false, "off_by_default"), Of(first, "embeddings"));
        Assert.AreEqual(new V4Activation("outline_descent", false, "act_has_no_chapter_levels"), Of(first, "outline_descent"), "an act with no chapter levels switches the descent off, and a note says so");
        Assert.AreEqual(new V4Activation("arbiter", true, "candidates_to_rank"), Of(first, "arbiter"));
        Assert.AreEqual(new V4Activation("arbiter", false, "no_model_wired"), Of(Decide("L'assureur peut-il résilier le contrat ?", model: false), "arbiter"));
        Assert.AreEqual(new V4Activation("extraction", true, "all_candidates_together"), Of(Decide("Dans quels cas l'assureur peut-il résilier le contrat ?"), "extraction"));
        Assert.AreEqual(new V4Activation("arbiter", false, "defining_line_known"), Of(Decide("c'est quoi une personne lésé", defined: true), "arbiter"));
        Assert.AreEqual(new V4Activation("arbiter", false, "own_case"), Of(Decide("Mon assureur peut-il résilier mon contrat ?", application: true), "arbiter"));
    }

    [TestMethod]
    public void ACitedArticleAndAChangeOverTimeStayLexsOwnRoutes()
    {
        Assert.IsTrue(V4Dispatcher.CitesArticle(Row("Que dit l'article 39 de la loi sur le contrat d'assurance ?")));
        Assert.IsTrue(V4Dispatcher.CitesArticle(Row("art. 62-3 de la loi du 27 juillet 1997")));
        Assert.IsFalse(V4Dispatcher.CitesArticle(Row("L'assureur peut-il résilier le contrat ?")));
        Assert.IsTrue(V4Dispatcher.AsksChange(Row("Qu'est-ce qui a changé sur la résiliation ?"), 0));
        Assert.IsTrue(V4Dispatcher.AsksChange(Row("La résiliation entre le 1er janvier 2020 et le 1er janvier 2024"), 2));
        Assert.IsFalse(V4Dispatcher.AsksChange(Row("L'assureur peut-il résilier le contrat ?"), 0));
        Assert.AreEqual("duration", V4Dispatcher.KindOf(Row("c'est quoi le délai de prescription ?"), Dictionary, termDefined: false), "a definition is asked of a term the act defines; else the next kind the words ask for");
    }

    // ---- Brick I: the detectors, the margin and the sweep -----------------------------------------------------------------------

    private static V4RetrievalResult Retrieve(string question)
    {
        var row = Row(question);
        var kind = V4Dispatcher.KindOf(row, Dictionary, termDefined: false);
        var concepts = row.Concepts.Count > 0 ? row.Concepts : row.Roles;
        return V4Retrieval.Run(
            new V4Brief(concepts.Select(static read => read.Concept).ToList(), row.Concepts.Count > 0 ? row.Roles.FirstOrDefault()?.Concept : null, kind, row.Concepts.Concat(row.Roles).Select(static read => read.Keyword).ToList()),
            BricksFixture.Tables,
            Dictionary);
    }

    [TestMethod]
    public void TheDetectorsPutTheLineOfTheRuleFirstOnTheSecondQuestion()
    {
        var result = Retrieve("l'assureur peut résilier le contrat de façon unilatérale ?");
        Assert.AreEqual("art_39#8", result.Candidates[0].Line.Id);
        CollectionAssert.AreEqual(
            new[] { "keyword", "title", "cooccurrence", "type_pattern", "answer_words", "direct_form", "general_rule" },
            result.Candidates[0].Detectors.ToArray(),
            "which detectors found it is kept beside it");
        CollectionAssert.AreEqual(new[] { "art_22", "art_39", "art_41", "art_79" }, result.TitleArticles.ToArray(), "four headings hold the word");
        var counts = result.Detectors.ToDictionary(static detector => detector.Id);
        Assert.AreEqual((45, 25), (counts["keyword"].Lines, counts["keyword"].Articles), "the lines of text that hold a form of the concept");
        Assert.AreEqual((30, 16), (counts["cooccurrence"].Lines, counts["cooccurrence"].Articles), "the concept and the actor on one line");
        Assert.IsFalse(counts["embeddings"].Ran);
        Assert.AreEqual("off_by_default", counts["embeddings"].SkippedBecause);
        Assert.IsFalse(result.ClearLead, "a rule comes with its cases: no fast route");
        var articles = result.Shortlist(14).Select(static candidate => candidate.Article.Id).Distinct().ToArray();
        foreach (var expected in new[] { "art_39", "art_22", "art_41", "art_13", "art_34" })
        {
            CollectionAssert.Contains(articles, expected);
        }

        Assert.IsTrue(result.Shortlist(14).GroupBy(static candidate => candidate.Article.Id).All(static group => group.Count() <= 2), "one article does not crowd out the others");
    }

    [TestMethod]
    [DataRow("Quel est le délai de prescription ?", "art_44#3")]
    [DataRow("Quand la garantie d'un contrat d'assurance prend-elle effet ?", "art_37#3")]
    [DataRow("Quel est le délai pour déclarer un sinistre ?", "art_26#3")]
    [DataRow("Quel est le délai de rétractation ?", "art_62-3#3")]
    public void TheFirstCandidateIsTheLineThatAnswers(string question, string line)
    {
        Assert.AreEqual(line, Retrieve(question).Candidates[0].Line.Id);
    }

    [TestMethod]
    public void AConceptThatNeedsAValueCountsOnlyNextToAValueAndTwoScopesBothLead()
    {
        var result = Retrieve("Quel est le délai de rétractation ?");
        var top = result.Candidates.Take(2).Select(static candidate => candidate.Line.Id).ToArray();
        CollectionAssert.AreEquivalent(new[] { "art_62-3#3", "art_100#3" }, top, "fourteen days at a distance, thirty for life insurance: neither is picked silently");
        Assert.IsFalse(result.ClearLead);
        var fourteen = result.Candidates[0].Values.Single();
        Assert.AreEqual("quatorze jours calendrier", result.Candidates[0].Line.Text.Substring(fourteen.Start, fourteen.Length), "a number in words");
        var hundred = result.Candidates.First(static candidate => candidate.Line.Id == "art_100#3");
        Assert.AreEqual(2, hundred.Values.Count, "one line can hold two durations: 'six mois' and '30 jours calendrier'");
        Assert.IsTrue(result.Candidates.Where(static candidate => candidate.Values.Count == 0).All(candidate => candidate.Score < result.Candidates[0].Score - 3));
    }

    [TestMethod]
    public void TheSweepIsTheProofOfAnAbsence()
    {
        var result = Retrieve("C'est quoi la franchise ?");
        Assert.AreEqual(0, result.Candidates.Count);
        var row = result.Sweep.Single();
        Assert.AreEqual(("franchise", 0, 0), (Dictionary.Keyword(row.Keyword)!.Keyword, row.Lines, row.Articles));
        Assert.IsTrue(Dictionary.Concept("franchise")!.Complete, "only a complete row may support 'the text does not use these terms'");
    }

    [TestMethod]
    public void AValueIsReadInDigitsAndInWordsAndALineCanLeaveItToTheContract()
    {
        string[] Durations(string text) => V4Patterns.Values("duration", V4Text.Fold(text)).Select(value => text.Substring(value.Start, value.Length)).ToArray();
        CollectionAssert.AreEqual(new[] { "trois ans" }, Durations("Le délai de prescription est de trois ans."));
        CollectionAssert.AreEqual(new[] { "quatorze jours calendrier" }, Durations("dispose d’un délai de quatorze jours calendrier pour se rétracter"));
        CollectionAssert.AreEqual(new[] { "six mois", "30 jours calendrier" }, Durations("d’une durée supérieure à six mois dispose d’un délai de 30 jours calendrier"));
        CollectionAssert.AreEqual(new[] { "quatre-vingt-dix jours" }, Durations("dans les quatre-vingt-dix jours"));
        CollectionAssert.AreEqual(new[] { "un mois" }, Durations("à l'expiration d'un délai d'un mois minimum"));
        Assert.AreEqual(0, Durations("le contrat est conclu pour la journée").Length);
        Assert.IsTrue(V4Patterns.LeftToContract(BricksFixture.Line("art_26#3").Folded), "'dans le délai fixé par le contrat'");
        Assert.IsTrue(V4Patterns.Confirms("rule", BricksFixture.Line("art_39#8").Folded));
    }

    // ---- Bricks G and J: the model's contracts, and answers made wrong on purpose ------------------------------------------

    private static readonly V4Line[] Sent =
    [
        BricksFixture.Line("art_39#3"), BricksFixture.Line("art_39#8"), BricksFixture.Line("art_22#3"), BricksFixture.Line("art_44#3"), BricksFixture.Line("art_100#3"), BricksFixture.Line("art_26#3"),
    ];

    private static (List<V4Generation.Checked>? Items, string? Failed) Check(string reply, string kind)
    {
        var extraction = V4Generation.ReadExtraction(reply);
        Assert.IsNotNull(extraction, "the reply is of the form");
        var items = V4Generation.Validate(extraction, kind, Sent, out var failed);
        return (items, failed);
    }

    [TestMethod]
    public void AnAnswerThatFitsItsKindPassesAndCodeKeepsThePatternsOwnSpanOfAValue()
    {
        var rule = Check("""{"found":true,"complete":false,"items":[{"line":"L2","role":"rule","value":""},{"line":"L3","role":"case","value":""}],"caveats":["conditions_apply"]}""", "rule");
        Assert.IsNull(rule.Failed);
        CollectionAssert.AreEqual(new[] { "art_39#8", "art_22#3" }, rule.Items!.Select(static item => item.Line.Id).ToArray());

        var value = Check("""{"found":true,"complete":true,"items":[{"line":"L4","role":"value","value":"Trois ans"}],"caveats":[]}""", "duration");
        Assert.IsNull(value.Failed);
        var span = value.Items!.Single().Value!.Value;
        Assert.AreEqual("trois ans", Sent[3].Text.Substring(span.Start, span.Length), "the words shown are the line's, at the pattern's position, never the model's string");

        var none = Check("""{"found":false,"complete":false,"items":[],"caveats":["left_to_contract"]}""", "duration");
        Assert.IsNull(none.Failed);
        Assert.AreEqual(0, none.Items!.Count, "zero items means not found");
    }

    [TestMethod]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L9","role":"rule","value":""}],"caveats":[]}""", "rule", "line_was_not_sent")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"art_39","role":"rule","value":""}],"caveats":[]}""", "rule", "line_was_not_sent")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L4","role":"value","value":"cinq ans"}],"caveats":[]}""", "duration", "value_not_on_the_line")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L1","role":"value","value":"un mois"}],"caveats":[]}""", "duration", "value_not_on_the_line")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L4","role":"value","value":""}],"caveats":[]}""", "duration", "value_missing")]
    [DataRow("""{"found":true,"complete":true,"items":[],"caveats":[]}""", "rule", "found_and_items_disagree")]
    [DataRow("""{"found":false,"complete":false,"items":[{"line":"L2","role":"rule","value":""}],"caveats":[]}""", "rule", "found_and_items_disagree")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L2","role":"value","value":""}],"caveats":[]}""", "rule", "shape_not_of_the_kind")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L2","role":"rule","value":""},{"line":"L3","role":"rule","value":""}],"caveats":[]}""", "rule", "shape_not_of_the_kind")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L3","role":"case","value":""}],"caveats":[]}""", "rule", "shape_not_of_the_kind")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L1","role":"rule","value":""}],"caveats":[]}""", "rule", "rule_line_has_no_rule_form")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L2","role":"rule","value":""},{"line":"L2","role":"case","value":""}],"caveats":[]}""", "rule", "line_cited_twice")]
    [DataRow("""{"found":true,"complete":true,"items":[{"line":"L2","role":"rule","value":""}],"caveats":["trust_me"]}""", "rule", "caveat_outside_list")]
    public void AnAnswerMadeWrongOnPurposeIsRefusedWithTheCheckThatFailed(string reply, string kind, string check)
    {
        var (items, failed) = Check(reply, kind);
        Assert.IsNull(items);
        Assert.AreEqual(check, failed);
    }

    [TestMethod]
    public void AReplyThatIsNotOfItsFormIsNoReply()
    {
        Assert.IsNull(V4Generation.ReadExtraction("the insurer may terminate the contract"));
        Assert.IsNull(V4Generation.ReadExtraction("""{"found":true}"""));
        Assert.IsNull(V4Generation.ReadArbiter("""{"candidates":[{"id":"c1","role":"primary","reason":"states_rule"}]}""", 2), "a candidate that was given is missing");
        Assert.IsNull(V4Generation.ReadArbiter("""{"candidates":[{"id":"c1","role":"primary","reason":"states_rule"},{"id":"c9","role":"discarded","reason":"other_subject"}]}""", 2), "an id that was not given");
        Assert.IsNull(V4Generation.ReadArbiter("""{"candidates":[{"id":"c1","role":"best","reason":"states_rule"},{"id":"c2","role":"discarded","reason":"other_subject"}]}""", 2), "a role outside the closed list");
        Assert.IsNull(V4Generation.ReadArbiter("""{"candidates":[{"id":"c1","role":"primary","reason":"because I say so"},{"id":"c2","role":"discarded","reason":"other_subject"}]}""", 2), "a reason is a code, never a sentence");
        Assert.AreEqual(2, V4Generation.ReadArbiter("""{"candidates":[{"id":"c2","role":"supporting","reason":"states_case"},{"id":"c1","role":"primary","reason":"states_rule"}]}""", 2)!.Count);
    }

    [TestMethod]
    public void TheModelsCallsCarryTheMaskedQuestionLinesOfTheLawAndNoOperation()
    {
        const string question = "l'assureur peut résilier le contrat de façon unilatérale ?";
        var row = Row(question);
        var result = Retrieve(question);
        var arbiter = V4Generation.ArbiterCall(row, "rule", BricksFixture.Tables, result.Shortlist(V4Bricks.MaximumCandidates), Dictionary);
        var extraction = V4Generation.ExtractionCall(row, "rule", BricksFixture.Tables, Sent, [], Dictionary);
        StringAssert.StartsWith(arbiter.System, "lex-v4-arbiter-prompt/1");
        StringAssert.StartsWith(extraction.System, "lex-v4-extraction-prompt/1");
        StringAssert.Contains(extraction.System, "KIND: rule", "the base, plus only the fragment this question needs");
        Assert.IsFalse(extraction.System.Contains("KIND: duration", StringComparison.Ordinal));
        using var sent = JsonDocument.Parse(arbiter.User);
        Assert.AreEqual(question, sent.RootElement.GetProperty("question").GetString());
        Assert.AreEqual("assureur", sent.RootElement.GetProperty("actor").GetString());
        Assert.AreEqual(V4Bricks.MaximumCandidates, sent.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.AreEqual("c1", sent.RootElement.GetProperty("candidates")[0].GetProperty("id").GetString(), "the model is given its own short ids, never a way to name a line it was not given");
        foreach (var call in new[] { arbiter, extraction })
        {
            foreach (var operation in V3AssistantPlanValidator.AllowedOperations)
            {
                Assert.IsFalse(call.System.Contains(operation, StringComparison.Ordinal) && operation.Contains('_', StringComparison.Ordinal), $"no instruction names the operation {operation}");
                Assert.IsFalse(call.SchemaJson.Contains("\"" + operation + "\"", StringComparison.Ordinal), $"no reply can name the operation {operation}");
            }

            using var schema = JsonDocument.Parse(call.SchemaJson);
            Assert.IsFalse(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        }
    }

    // ---- Brick H: the whole pipeline, its gates and its replay ---------------------------------------------------------------

    private static V3AnswerDossier Answered(V3AskOutcome outcome)
    {
        if (outcome is V3AskPresented presented)
        {
            Assert.Fail($"not answered: {presented.Reason} {presented.PlanRefusal}");
        }

        return ((V3AskAnswered)outcome).Dossier;
    }

    private static string[] Templates(V3AnswerDossier dossier) =>
        dossier.Blocks.OfType<V3ClaimBlock>().Select(static block => block.Claim.TemplateId).ToArray();

    private static JsonElement Bricks(V3AnswerDossier dossier)
    {
        using var document = JsonDocument.Parse(dossier.ToUtf8());
        return document.RootElement.GetProperty("trace").GetProperty("plan").GetProperty("bricks").Clone();
    }

    [TestMethod]
    public void TheFirstQuestionIsAnsweredByOneLineOfTheLawAndNoModel()
    {
        const string question = "c'est quoi une personne lésé";
        var executor = new BricksFixture.Executor();
        var dossier = Answered(BricksFixture.Ask(question, executor: executor));
        Assert.AreEqual(V3AssistantVerdict.Answer, dossier.Verdict);
        var quote = dossier.Quotes.Single();
        Assert.AreEqual("Personne lésée: dans une assurance de responsabilité, la personne victime d'un dommage dont l'assuré est responsable.", quote.Text);
        Assert.AreEqual("art_1er", quote.Anchor);
        Assert.IsTrue(quote.Offset > 0, "one line at its position, not the article from its start");
        CollectionAssert.Contains(Templates(dossier), V3AnswerTemplates.AnswerDefinition);
        CollectionAssert.Contains(Templates(dossier), V3AnswerTemplates.LawExtractHeading);
        var heading = dossier.Blocks.OfType<V3ClaimBlock>().Single(static block => block.Claim.TemplateId == V3AnswerTemplates.LawExtractHeading).Claim;
        Assert.AreEqual("Définitions", heading.Facts.Single(static fact => fact.Kind == V3AnswerSlotKind.LawWords).Value, "the article's own heading, verbatim");
        var note = dossier.Blocks.OfType<V3ClaimBlock>().Single(static block => block.Claim.TemplateId == V3AnswerTemplates.NoteLawTerm).Claim;
        Assert.AreEqual("Vous avez écrit « personne lésé » ; le terme du texte est « personne lésée ».", note.Text);
        Assert.AreEqual(10, dossier.Blocks.OfType<V3ActionBlock>().Count(), "the articles that use the term are one step away");
        CollectionAssert.Contains(dossier.Notices.Select(static notice => notice.TemplateId).ToArray(), V3AnswerTemplates.NoticeNoModel);
        CollectionAssert.DoesNotContain(dossier.Notices.Select(static notice => notice.TemplateId).ToArray(), V3AnswerTemplates.NoticeAssembledByModel, "no model planned anything");
        CollectionAssert.Contains(dossier.PlanLines.Select(static line => line.TemplateId).ToArray(), V3AnswerTemplates.PlanBricksRouteDefinition);
        CollectionAssert.Contains(dossier.PlanLines.Select(static line => line.TemplateId).ToArray(), V3AnswerTemplates.PlanBricksModelNone);
        Assert.AreEqual(1, executor.Calls.Count, "one article read");
        var bricks = Bricks(dossier);
        Assert.AreEqual("definition", bricks.GetProperty("route").GetString());
        Assert.AreEqual(0, bricks.GetProperty("model_calls").GetArrayLength());
        Assert.AreEqual(V4Dictionary.Insurance.Sha256, bricks.GetProperty("dictionary").GetString(), "the answer names the dictionary it was made with");
    }

    [TestMethod]
    public void TheSecondQuestionShowsTheRuleItsCasesAndTheWordTheLawDoesNotUseWithNoModel()
    {
        const string question = "l'assureur peut résilier le contrat de façon unilatérale ?";
        var dossier = Answered(BricksFixture.Ask(question));
        Assert.AreEqual(V3AssistantVerdict.Answer, dossier.Verdict);
        var quotes = dossier.Quotes.ToArray();
        Assert.AreEqual("Hormis les cas prévus par la loi, l'assureur ne peut résilier le contrat.", quotes[0].Text);
        Assert.AreEqual("art_39", quotes[0].Anchor);
        Assert.AreEqual(V3AssistantPipeline.MaximumQuotes, quotes.Length, "the rule and four cases");
        CollectionAssert.Contains(quotes.Select(static quote => quote.Anchor).ToArray(), "art_22");
        var templates = Templates(dossier);
        Assert.IsTrue(Array.IndexOf(templates, V3AnswerTemplates.AnswerRule) < Array.IndexOf(templates, V3AnswerTemplates.AnswerRuleCases), "the rule first, then its cases");
        CollectionAssert.Contains(templates, V3AnswerTemplates.CaveatCasesFromSweep);
        var absent = dossier.Blocks.OfType<V3ClaimBlock>().Single(static block => block.Claim.TemplateId == V3AnswerTemplates.NoteWordNotInAct).Claim;
        Assert.AreEqual("unilatérale", absent.Facts.Single().Value);
        CollectionAssert.Contains(dossier.PlanLines.Select(static line => line.TemplateId).ToArray(), V3AnswerTemplates.PlanBricksPassSweepCases);
        Assert.IsFalse(dossier.Blocks.OfType<V3ClaimBlock>().Any(static block => block.Claim.Text.StartsWith("Oui", StringComparison.Ordinal) || block.Claim.Text.StartsWith("Non,", StringComparison.Ordinal)), "no yes and no no is written");
        Assert.IsTrue(dossier.Parts.Single().Application is false && dossier.Parts.Single().Cues.Count == 0);
    }

    /// <summary>A model that ranks and points as a careful one would on the second question, and records what it was sent.</summary>
    private static BricksFixture.Model CarefulModel() => new(static call =>
    {
        using var sent = JsonDocument.Parse(call.User);
        if (call.Step == "arbiter")
        {
            var rows = sent.RootElement.GetProperty("candidates").EnumerateArray().Select(static candidate =>
            {
                var line = candidate.GetProperty("line").GetString()!;
                var article = candidate.GetProperty("article").GetString()!;
                var (role, reason) = line.StartsWith("Hormis les cas", StringComparison.Ordinal) ? ("primary", "states_rule")
                    : article is "Article 22" or "Article 13" or "Article 34" or "Article 41" && line.Contains("assureur", StringComparison.Ordinal) && !line.Contains("le preneur d'assurance dispose", StringComparison.Ordinal) ? ("supporting", "states_case")
                    : article is "Article 38" ? ("tangential", "other_party")
                    : ("tangential", "mentions_only");
                return (JsonNode)new JsonObject { ["id"] = candidate.GetProperty("id").GetString(), ["role"] = role, ["reason"] = reason };
            }).ToArray();
            return new JsonObject { ["candidates"] = new JsonArray(rows) }.ToJsonString();
        }

        var lines = sent.RootElement.GetProperty("lines").EnumerateArray().ToArray();
        string Id(string start) => lines.First(line => line.GetProperty("text").GetString()!.StartsWith(start, StringComparison.Ordinal)).GetProperty("id").GetString()!;
        return new JsonObject
        {
            ["found"] = true,
            ["complete"] = false,
            ["items"] = new JsonArray(
                new JsonObject { ["line"] = Id("Hormis les cas"), ["role"] = "rule", ["value"] = string.Empty },
                new JsonObject { ["line"] = Id("L'assureur a le droit de résilier le contrat dix jours"), ["role"] = "case", ["value"] = string.Empty },
                new JsonObject { ["line"] = Id("Si l'assureur apporte la preuve qu'il n'aurait en aucun cas assuré le risque, il peut"), ["role"] = "case", ["value"] = string.Empty }),
            ["caveats"] = new JsonArray("conditions_apply"),
        }.ToJsonString();
    });

    [TestMethod]
    public void WithAModelTheRuleAndItsCasesAreTheOnesItPointedAtAndCodeChecked()
    {
        const string question = "l'assureur peut résilier le contrat de façon unilatérale ?";
        var model = CarefulModel();
        var dossier = Answered(BricksFixture.Ask(question, model));
        CollectionAssert.AreEqual(new[] { "arbiter", "extraction" }, model.Calls.Select(static call => call.Step).ToArray(), "two calls, and no third");
        CollectionAssert.AreEqual(new[] { "art_39", "art_22", "art_13" }, dossier.Quotes.Select(static quote => quote.Anchor).ToArray());
        CollectionAssert.Contains(dossier.Notices.Select(static notice => notice.TemplateId).ToArray(), V3AnswerTemplates.NoticeModelAssisted);
        var lines = dossier.PlanLines.Select(static line => line.TemplateId).ToArray();
        CollectionAssert.Contains(lines, V3AnswerTemplates.PlanBricksModelArbiter);
        CollectionAssert.Contains(lines, V3AnswerTemplates.PlanBricksModelExtraction);
        var bricks = Bricks(dossier);
        Assert.AreEqual(2, bricks.GetProperty("model_calls").GetArrayLength());
        var first = bricks.GetProperty("candidates").EnumerateArray().First(static candidate => candidate.GetProperty("line").GetString() == "art_39#8");
        Assert.AreEqual(("primary", "states_rule"), (first.GetProperty("role").GetString(), first.GetProperty("reason").GetString()), "the arbiter's role and reason live in the trace");
        Assert.IsTrue(dossier.Blocks.OfType<V3ActionBlock>().Any(), "the cases the answer does not quote stay one step away");

        // The model saw the masked question, the act's lines and nothing else of the reader.
        foreach (var call in model.Calls)
        {
            using var sent = JsonDocument.Parse(call.User);
            Assert.AreEqual(question, sent.RootElement.GetProperty("question").GetString());
        }
    }

    [TestMethod]
    public void AReplyThatFailsTheChecksIsAskedAgainOnceWithAnotherContextThenTheAnswerIsCodesOwn()
    {
        const string question = "l'assureur peut résilier le contrat de façon unilatérale ?";
        var careful = CarefulModel();
        var extractions = 0;
        var model = new BricksFixture.Model(call =>
        {
            if (call.Step == "arbiter")
            {
                return careful.Complete(call, CancellationToken.None).Content;
            }

            extractions++;
            return """{"found":true,"complete":true,"items":[{"line":"L99","role":"rule","value":""}],"caveats":[]}""";
        });
        var dossier = Answered(BricksFixture.Ask(question, model));
        Assert.AreEqual(2, extractions, "one retry, and it changes something: the context");
        Assert.AreNotEqual(model.Calls[1].User, model.Calls[2].User, "every retry should change something");
        var bricks = Bricks(dossier);
        Assert.IsTrue(bricks.GetProperty("passes").EnumerateArray().Any(static pass => pass.GetProperty("trigger").GetString() == "validator_rejected" && pass.GetProperty("outcome").GetString() == "line_was_not_sent"), "the pass is written down with what started it");
        CollectionAssert.Contains(bricks.GetProperty("answer").GetProperty("caveats").EnumerateArray().Select(static caveat => caveat.GetString()).ToArray(), "model_unavailable");
        CollectionAssert.Contains(dossier.PlanLines.Select(static line => line.TemplateId).ToArray(), V3AnswerTemplates.PlanBricksModelUnavailable);
        Assert.AreEqual("Hormis les cas prévus par la loi, l'assureur ne peut résilier le contrat.", dossier.Quotes.First().Text, "a typed 'not answered by the model' rather than a wrong value: the lines code found");
    }

    [TestMethod]
    public void AModelThatFailsLeavesTheAnswerCodeFound()
    {
        var model = new BricksFixture.Model(static _ => throw new HttpRequestException("the provider is unreachable"));
        var dossier = Answered(BricksFixture.Ask("Quel est le délai de rétractation ?", model));
        Assert.AreEqual(V3AssistantVerdict.Answer, dossier.Verdict);
        CollectionAssert.Contains(dossier.PlanLines.Select(static line => line.TemplateId).ToArray(), V3AnswerTemplates.PlanBricksModelUnavailable);
        CollectionAssert.AreEquivalent(new[] { "art_62-3", "art_100" }, dossier.Quotes.Select(static quote => quote.Anchor).Take(2).ToArray());
    }

    [TestMethod]
    public void AValueIsRestatedOnlyAsTheWordsWrittenOnTheQuotedLine()
    {
        var model = new BricksFixture.Model(static call =>
        {
            using var sent = JsonDocument.Parse(call.User);
            if (call.Step == "arbiter")
            {
                var rows = sent.RootElement.GetProperty("candidates").EnumerateArray().Select(static candidate => (JsonNode)new JsonObject
                {
                    ["id"] = candidate.GetProperty("id").GetString(),
                    ["role"] = candidate.GetProperty("line").GetString()!.StartsWith("Le délai de prescription de toute action", StringComparison.Ordinal) ? "primary" : "supporting",
                    ["reason"] = "gives_value",
                }).ToArray();
                return new JsonObject { ["candidates"] = new JsonArray(rows) }.ToJsonString();
            }

            var line = sent.RootElement.GetProperty("lines").EnumerateArray().First(static candidate => candidate.GetProperty("text").GetString()!.StartsWith("Le délai de prescription de toute action", StringComparison.Ordinal));
            return new JsonObject
            {
                ["found"] = true,
                ["complete"] = true,
                ["items"] = new JsonArray(new JsonObject { ["line"] = line.GetProperty("id").GetString(), ["role"] = "value", ["value"] = "trois ans" }),
                ["caveats"] = new JsonArray(),
            }.ToJsonString();
        });
        var dossier = Answered(BricksFixture.Ask("Quel est le délai de prescription ?", model));
        Assert.AreEqual("Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.", dossier.Quotes.Single().Text);
        var value = dossier.Blocks.OfType<V3ClaimBlock>().Single(static block => block.Claim.TemplateId == V3AnswerTemplates.AnswerValueWords).Claim;
        Assert.AreEqual("Écrit sur cette ligne : « trois ans ».", value.Text);
        Assert.AreEqual(V3AnswerSlotKind.LawWords, value.Facts.Single().Kind);
    }

    [TestMethod]
    public void AValueTheLawLeavesToTheContractIsNotInvented()
    {
        var dossier = Answered(BricksFixture.Ask("Quel est le délai pour déclarer un sinistre ?"));
        var templates = Templates(dossier);
        CollectionAssert.Contains(templates, V3AnswerTemplates.AbsenceNoAnswerInLines);
        CollectionAssert.Contains(templates, V3AnswerTemplates.CaveatLeftToContract);
        CollectionAssert.DoesNotContain(templates, V3AnswerTemplates.AnswerValueWords, "never a default value");
        StringAssert.Contains(dossier.Quotes.First().Text, "dans le délai fixé par le contrat", "the line that says why is quoted");
    }

    [TestMethod]
    public void AnAbsenceShowsItsProofAndSaysWhatItProves()
    {
        var executor = new BricksFixture.Executor();
        var dossier = Answered(BricksFixture.Ask("C'est quoi la franchise ?", executor: executor));
        Assert.AreEqual(V3AssistantVerdict.Answer, dossier.Verdict);
        Assert.AreEqual(0, dossier.Quotes.Count());
        Assert.AreEqual(0, executor.Calls.Count, "nothing to read: the sweep is the answer");
        var claims = dossier.Blocks.OfType<V3ClaimBlock>().Select(static block => block.Claim).ToArray();
        Assert.AreEqual("Lex a parcouru /lu-legilux/loi-1997-07-27-n10 (913 lignes, 147 articles), tel qu’il se lisait le 18 novembre 2026, à la recherche de ces termes :", claims[0].Text);
        Assert.AreEqual("« franchise » : 0 lignes, dans 0 articles.", claims[1].Text);
        Assert.AreEqual(V3AnswerTemplates.AbsenceTermsNowhere, claims[^1].TemplateId);
    }

    [TestMethod]
    public void TheReadersOwnCaseIsRefusedWithTheLawBesideItAndNoModelIsAsked()
    {
        var model = new BricksFixture.Model(static _ => throw new AssertFailedException("no model is asked on the reader's own case"));
        var dossier = Answered(BricksFixture.Ask("Mon assureur peut-il résilier mon contrat ?", model));
        Assert.AreEqual(V3AssistantVerdict.Refuse, dossier.Verdict);
        Assert.IsTrue(dossier.Parts.Single().Application);
        Assert.AreEqual(V3AnswerTemplates.BoundaryWholeNoAdviser, Templates(dossier)[0], "the refusal opens the answer");
        Assert.AreEqual("Hormis les cas prévus par la loi, l'assureur ne peut résilier le contrat.", dossier.Quotes.Single().Text, "the descriptive maximum: the law found, verbatim and dated");
        Assert.AreEqual(0, model.Calls.Count);
        Assert.IsTrue(dossier.Blocks.OfType<V3HandoffBlock>().Any());
    }

    [TestMethod]
    public void TheSameQuestionOnTheRuleIsAnsweredAndSaysWhy()
    {
        var dossier = Answered(BricksFixture.Ask("L'assureur peut-il résilier le contrat ?"));
        Assert.AreEqual(V3AssistantVerdict.Answer, dossier.Verdict);
        var part = dossier.Parts.Single();
        Assert.IsFalse(part.Application);
        Assert.AreEqual(1, part.Cues.Count, "the cue is still listed");
        CollectionAssert.Contains(dossier.PlanLines.Select(static line => line.TemplateId).ToArray(), V3AnswerTemplates.PlanBricksRuleQuestion);
    }

    [TestMethod]
    public void AGreetingGetsADirectReplyAndAnEuActNotHeldIsNamedAsSuch()
    {
        var executor = new BricksFixture.Executor();
        var greeting = Answered(BricksFixture.Ask("Bonjour", executor: executor));
        Assert.AreEqual(V3AssistantVerdict.Clarify, greeting.Verdict);
        Assert.AreEqual(V3AnswerTemplates.DirectGreeting, Templates(greeting).Single());
        Assert.AreEqual(0, executor.Calls.Count, "no pipeline at all");

        var notHeld = Answered(BricksFixture.Ask("Que prévoit Solvabilité II sur les fonds propres ?"));
        Assert.AreEqual(V3AssistantVerdict.Point, notHeld.Verdict);
        var claim = notHeld.Blocks.OfType<V3ClaimBlock>().First().Claim;
        Assert.AreEqual("Votre question nomme directive 2009/138/CE (Solvabilité II). Lex ne détient pas encore le texte de cet acte.", claim.Text);
        Assert.IsTrue(notHeld.Blocks.OfType<V3ClaimBlock>().Any(static block => block.Claim.Text.Contains("https://eur-lex.europa.eu/eli/dir/2009/138/oj", StringComparison.Ordinal)));
    }

    private sealed class NamedPlanner(string json) : IAssistantPlanner
    {
        public int Asked { get; private set; }

        public string PlannerId => "fallback-test-planner/1";

        public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken)
        {
            Asked++;
            return new V3PlannerOutput(json, 0);
        }
    }

    [TestMethod]
    [DataRow("Que dit l'article 39 de la loi sur le contrat d'assurance ?")]
    [DataRow("Qu'est-ce qui a changé dans la loi sur le contrat d'assurance en 2025 ?")]
    [DataRow("Quelle est la durée maximale de la période d'essai ?")]
    [DataRow("Quelle est la cotisation d'assurance maladie à la CNS ?")]
    public void AQuestionTheBricksDoNotAnswerGoesToThePlannerThatAnsweredItBefore(string question)
    {
        var fallback = new NamedPlanner("not a plan");
        var outcome = BricksFixture.Ask(question, fallback: fallback);
        Assert.AreEqual(1, fallback.Asked);
        Assert.AreEqual(V3AskOutcomeReason.PlanRefused, outcome.Reason, "and what that planner answers is validated as before");
        Assert.AreEqual(V3AskOutcomeReason.PlannerFailed, BricksFixture.Ask(question).Reason, "with no other planner the question is not answered");
    }

    [TestMethod]
    public void AReplayGivesTheSameBytesWithoutAskingAnyModel()
    {
        foreach (var question in new[] { "c'est quoi une personne lésé", "l'assureur peut résilier le contrat de façon unilatérale ?", "C'est quoi la franchise ?", "Mon assureur peut-il résilier mon contrat ?", "Bonjour" })
        {
            var dossier = Answered(BricksFixture.Ask(question, question.StartsWith("l'assureur", StringComparison.Ordinal) ? CarefulModel() : null));
            using var document = JsonDocument.Parse(dossier.ToUtf8());
            var trace = document.RootElement.GetProperty("trace").GetRawText();
            var replayed = V3AssistantPipeline.Replay(trace, _ => new BricksFixture.Executor().Run, BricksFixture.Environment);
            Assert.IsFalse(replayed.SnapshotMismatch);
            CollectionAssert.AreEqual(dossier.ToUtf8(), Answered(replayed.Outcome!).ToUtf8(), question);
        }
    }

    [TestMethod]
    public void AnExtractThatIsNotTheTextTheStepAnsweredIsNotServed()
    {
        var executor = new BricksFixture.Executor();
        executor.Replaced["art_1er"] = "Article 1er Définitions Un autre libellé de cet article, plus long que la ligne citée, pour que la position existe encore dans ce texte. " + new string('x', 2000);
        var outcome = BricksFixture.Ask("c'est quoi une personne lésé", executor: executor);
        Assert.AreEqual(V3AskOutcomeReason.GateFailed, outcome.Reason, "the article's digest is not the one the line was cut from: nothing is quoted");
    }

    // ---- The plan /2, read as untrusted ---------------------------------------------------------------------------------------

    private static (string Json, V3PlanContext Context) PlanOf(string question, IV4Model? model = null)
    {
        var intake = V3AssistantParsers.Intake(question)!;
        var dates = V3AssistantParsers.DatesIn(intake.Text);
        var context = new V3PlanContext(intake, dates, V3AssistantParsers.RangesIn(intake.Text, dates), [], ["fra"]);
        var json = new V4BricksPlanner(new BricksFixture.Corpus(), model, null).Plan(new V3PlannerInput(intake.Masked, V3AnswerLocale.Fr, context.References(), ["fra"], BricksFixture.Today), CancellationToken.None).PlanJson;
        return (json, context);
    }

    private static V3PlanValidation Forged(string question, Action<JsonObject> change)
    {
        var (json, context) = PlanOf(question);
        var plan = JsonNode.Parse(json)!.AsObject();
        change(plan);
        return V3AssistantPlanValidator.Validate(plan.ToJsonString(), context);
    }

    [TestMethod]
    public void ThePlanTheBricksWriteIsAdmittedAndKeepsItsBytes()
    {
        var (json, context) = PlanOf("l'assureur peut résilier le contrat de façon unilatérale ?");
        var validation = V3AssistantPlanValidator.Validate(json, context);
        Assert.IsTrue(validation.Admitted, validation.Detail);
        Assert.AreEqual(json, validation.Plan!.CanonicalJson, "the canonical bytes are the bytes written");
        Assert.AreEqual(5, validation.Plan.Steps.Count);
        Assert.IsTrue(validation.Plan.Steps.All(static step => step.OperationId == "evidence_bundle" && step.Extracts!.Count == 1 && step.Arguments["identifier"].Kind == V3ArgumentSourceKind.Corpus));
        Assert.AreEqual("rule", validation.Plan.Bricks!.Kind);
        StringAssert.StartsWith(json, "{\"schema\":\"lex-v3-assistant-plan/2\"");
    }

    [TestMethod]
    public void AForgedPlanIsRefusedWhereItLeavesItsClosedForm()
    {
        const string question = "c'est quoi une personne lésé";
        JsonObject BricksOf(JsonObject plan) => plan["bricks"]!.AsObject();
        JsonObject Step(JsonObject plan) => plan["steps"]![0]!.AsObject();

        Assert.IsTrue(Forged(question, static _ => { }).Admitted);
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => BricksOf(plan)["route"] = "whatever the model likes").Refusal, "a sentence cannot ride in the record");
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => BricksOf(plan)["route"] = "teleport").Refusal);
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => BricksOf(plan)["dictionary"] = new string('0', 64)).Refusal, "another dictionary than this build's");
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => BricksOf(plan)["answer"]!["items"]![0]!["extract"] = 7).Refusal, "an item names an extract the plan does not hold");
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => BricksOf(plan)["answer"]!["caveats"] = new JsonArray("trust_me")).Refusal);
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => BricksOf(plan)["roles"]![0]!["span"] = new JsonArray(0, 4000)).Refusal, "a span outside the question");
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => BricksOf(plan)["answer"]!["more"] = new JsonArray("art_2 or anything else")).Refusal);
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => Step(plan)["arguments"]!["identifier"]!["corpus"] = "/lu-legilux/Some Words Here").Refusal, "an address of the corpus has a closed form");
        Assert.AreEqual(V3PlanRefusal.OutsideClosedSet, Forged(question, plan => Step(plan)["arguments"]!["article"]!["corpus"] = "the whole law").Refusal);
        Assert.AreEqual(V3PlanRefusal.SpanOutOfBounds, Forged(question, plan => Step(plan)["extracts"]![0]!["length"] = 900000).Refusal);
        Assert.AreEqual(V3PlanRefusal.Malformed, Forged(question, plan => Step(plan)["text_sha256"] = "not a digest").Refusal);
        Assert.AreEqual(V3PlanRefusal.UnknownMember, Forged(question, plan => Step(plan).Remove("text_sha256")).Refusal, "extracts travel with the digest of the text they were cut from");
        Assert.AreEqual(V3PlanRefusal.UnknownMember, Forged(question, plan => Step(plan)["operation_id"] = "search").Refusal);
        Assert.AreEqual(V3PlanRefusal.UnknownMember, Forged(question, plan => plan["schema"] = "lex-v3-assistant-plan/1").Refusal, "a plan /1 carries no bricks record");
        Assert.IsFalse(Forged(question, plan =>
        {
            plan["schema"] = "lex-v3-assistant-plan/1";
            plan.Remove("bricks");
        }).Admitted, "and no address of the corpus: under /1 every argument is the reader's words, a reference, a result, a choice or today");
    }

    [TestMethod]
    public void TheLawsWordsInASentenceAreHeldToTheTextAStepAnswered()
    {
        var executor = new BricksFixture.Executor();
        var dossier = Answered(BricksFixture.Ask("c'est quoi une personne lésé", executor: executor));
        var outcomes = executor.Calls.Select((call, at) => (Id: "s" + (at + 1), Outcome: new BricksFixture.Executor().Run(call.Operation, call.Parameters))).ToDictionary(static pair => pair.Id, static pair => pair.Outcome);
        Assert.IsTrue(V3AnswerGates.LawWordsBound(dossier, outcomes));
        Assert.IsTrue(V3AnswerGates.Check(dossier, outcomes));
        var other = new BricksFixture.Executor();
        other.Replaced["art_1er"] = "Article 1er Un tout autre texte.";
        var elsewhere = executor.Calls.Select((call, at) => (Id: "s" + (at + 1), Outcome: other.Run(call.Operation, call.Parameters))).ToDictionary(static pair => pair.Id, static pair => pair.Outcome);
        Assert.IsFalse(V3AnswerGates.LawWordsBound(dossier, elsewhere), "'Définitions' is not in the text that step would have answered");
        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerClaim.Bind(V3AnswerTemplates.NoteLawTerm, V3AnswerLocale.Fr,
            new V3AnswerFact("words", V3AnswerSlotKind.QuestionSpan, "résilier"), new V3AnswerFact("term", V3AnswerSlotKind.DictionaryTerm, "une phrase qu'un modèle aurait écrite")), "a term is one the dictionary writes");
        Assert.ThrowsExactly<ArgumentException>(() => V3AnswerClaim.Bind(V3AnswerTemplates.AnswerTermUsage, V3AnswerLocale.Fr,
            new V3AnswerFact("lines", V3AnswerSlotKind.Count, "beaucoup"), new V3AnswerFact("articles", V3AnswerSlotKind.Count, "3")), "a count is digits");
    }

    [TestMethod]
    public void TheBricksPlannerNamesItselfByEverythingThatDecidesAnAnswer()
    {
        var alone = new V4BricksPlanner(new BricksFixture.Corpus(), null, null).PlannerId;
        var withModel = new V4BricksPlanner(new BricksFixture.Corpus(), CarefulModel(), null).PlannerId;
        var withFallback = new V4BricksPlanner(new BricksFixture.Corpus(), null, new NamedPlanner("{}")).PlannerId;
        StringAssert.Matches(alone, new System.Text.RegularExpressions.Regex("^lex-v4-bricks/code-only/prompt-[0-9a-f]{12}$"));
        StringAssert.StartsWith(withModel, "lex-v4-bricks/scripted-model/prompt-");
        Assert.AreNotEqual(alone, withFallback, "another fallback planner is another planner");
        Assert.AreEqual(64, V4Generation.PromptsSha256.Length);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(V4Generation.ArbiterPrompt) > 500);
    }
}

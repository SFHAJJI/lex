// The live pages' interface copy in French, beside the English table (`live-chrome.mjs`), and the French of the
// sentences the screens say for a refusal.
//
// Decision 41: French interface copy ships only after evidence-based legal-language review; machine translation alone is
// not sufficient. This copy was reviewed by Claude (AI reviewer), under the owner's delegation of 2026-10-02: an AI
// legal-language review of the driver's draft, with its evidence (Legilux's own French labels, the Interinstitutional
// Style Guide, the Luxembourg statute on legal consultation), and not a review by a person. Its record, every changed
// entry with its reason, is `C:\lex-v3\lanes\fr-review\review.md`; the receipt is `CHROME_REVIEWS.fr` in
// `localization.mjs`. The owner may revise any entry: `node web/scripts/refusal-sentences.mjs` prints every one
// beside its English.
//
// Entries written after the review were reviewed in its addendum of 2026-10-03, by the same reviewer under the same
// delegation (the record's addendum section; STATUS-WEB.md lists them): `reading.idle` (the review's own proposal),
// `reading.intro`, `dossier.intro`, `dossier.euExpressions` and `search.euWording` (the EU time view serves
// consolidated wordings, so the original is no longer the one wording held), the separators the pages wrote in their
// markup (`dossier.titleGroup`, `search.ambiguousWork`, `common.listSeparator`), an EU refusal card's offered wordings
// and their note (`refusalCard.europeCandidate`, `refusalCard.europeCandidateWithdrawalNotStated`,
// `refusalCard.europePublished`, `refusalCard.europeNotes.ambiguous_version`), the time view's
// `reading.europeHoldsUntil`, `reading.europeSameDateWorks` and `reading.europeUnplaced` (PR #909), and the search
// page's `search.intro`, `search.idle` and `search.noHit` once search matches only the text its rights admit (PR #910),
// and `common.europeAnnexes`, the line for the annexes an EU wording does not serve as text (PR #919; both forms changed
// in its review: a search covers the text it "interroge", and an annex is served "sous forme de texte").
// The export composer's entries for the EU export were reviewed by the same reviewer, under the same delegation, in the
// review of PR #920 (the record's addendum; all four approved): `export.intro` (changed to name EU text, the
// acknowledgement and authenticity statement it is served with, and what an export excludes), `export.europeRights`,
// `export.europeItem` and `export.europeExcluded`. The acknowledgement is "la mention de la source" (the Publications
// Office's own term for the condition it meets) and the authenticity statement "la déclaration sur l’édition qui fait
// foi" (never "authentique").
// The site's header, menu, footer and home page (`shell.title`, `site.*`, `home.*`, PR #955) were reviewed by the same
// reviewer under the same delegation in the addendum of 2026-10-14 (6 of 19 changed: the EU's law is "droit de l’UE", a
// work on the home page an "acte", the radar's noun "modifications", and the authentic Luxembourg publication the
// "Journal officiel du Grand-Duché de Luxembourg (Mémorial A)", as the loi du 23 décembre 2016 names it; the English
// footer changed with it).
// The notice a permalink's reading shows when the version (or EU wording) served is not the one the link pins
// (`reading.pinReplaced`, `reading.europePinReplaced`) was reviewed by the same reviewer under the same delegation in a
// second addendum of 2026-10-14 (both approved; a link "épingle" what it pins, the export's verb).
// A Luxembourg act held only as first published (`common.asPublished`: its label, the later versions this site does not
// hold, the consolidated versions the publisher's record names, its end-of-force date and the three authenticity
// sentences) and the Compare card's sentence for one state on both dates (`compare.sameVersion`, with `compare.idle`
// changed so the page never says "s’appliquaient"), the plan's item 0.6, were reviewed by the same reviewer under the
// same delegation in the addendum of 2026-10-15 (13 forms, 2 changed in review; the label and the authenticity
// sentences go to the lawyer's read, the owner's item O4). What Compare says when both dates select the one state of an
// act held only as published, and what its publisher dates between the two dates (`compare.sameTextHeld`,
// `compare.sameTextHeldOfSeveral`, `compare.betweenNamed`, `compare.betweenEndOfForce`; the review of #964), were
// reviewed by the same reviewer under the same delegation in that addendum's section for the review of #964 (5 forms,
// 3 changed in review: "le même texte détenu", and "pour cet acte" before the count in both counted forms).
// A service failure's panel (`failure.line`, `failure.retry`, `failure.official`, `failure.sites.legilux`,
// `failure.sites.eurlex`), the 404 page (`notFound.title`, `notFound.eyebrow`, `notFound.heading`, `notFound.intro`,
// `notFound.home`) and the maintenance page (`maintenance.title`, `maintenance.eyebrow`, `maintenance.heading`,
// `maintenance.intro`), the plan's item 6.9 (R60), were reviewed by the same reviewer under the same delegation in the
// second addendum of 2026-10-15 (14 forms, 1 changed in review: no comma before "ou" in `notFound.intro`; the reader's
// request is a "demande", as on the refusal card's retry line, and "not an answer about the law" is "pas une réponse sur
// le droit").
// The home page and the sentence that says what Lex is not (the plan's items 4.1 and 6.8; R47, R52, R11, R13, R50):
// `search.placeholder`, `search.dated`, `search.undated`, `site.notAdvice`, `site.authenticity` (the second sentence of the
// withdrawn `site.advice`, unchanged), `home.aboutHeading`, `home.coverage`, `home.coverageUndated`, `home.acts`,
// `home.versions` and `home.articles` (each `one` and `other`), `home.links` and `form.today`, and the search page's refusal
// sentence for an EU act searched at a date, were reviewed by the same reviewer under the same delegation in the
// addendum of 2026-10-16 (17 forms, none changed). `site.notAdvice` is J6's French as R50 fixes it, "conseil juridique"
// included against T10's "consultation juridique": the lawyer's read settles it (the owner's item O4), with the two other
// points the addendum records. In the review of #971 (the same addendum's section of 2026-10-17, 5 forms, none changed),
// `search.dated` and `search.undated` were replaced to say what a dated search searched (the latest held version
// applicable from the date or an earlier one, an act held only as published searched as published) and that only
// Luxembourg acts were searched, and `search.noHitLuxembourg`, `search.luxembourgOnly` and `search.searchPage` say the
// home page's box searches Luxembourg law and lead to the search page for an EU act.
// The reading's lead at a law's address (the plan's item 3.2b; R64, R67): a stale permalink's notice with the current link
// (`reading.pinNotHeld`, `reading.europePinNotHeld`), the cited article's title, its source and its whole act
// (`reading.citedTitle`, `reading.citedFrom`, `reading.citedWithoutText`, `reading.wholeAct`, `reading.europeWholeAct`),
// the changed-since banner and its link (`reading.newerVersion`, `reading.europeNewerWording`, `reading.openToday`), and
// the two refusal sentences for `pinned_digest_mismatch` in `REFUSALS_FR.sentences`, were reviewed by the same reviewer
// under the same delegation in the addendum of 2026-10-16 (11 forms, 1 changed in review: a quotation is "cité d’après" its
// version, never "extrait de", the table's word for Lex's own extraction; `reading.citedWithoutText` is the review's own
// rendering, given for the article held without text, which the review found the page said "quoted from"). What the EU
// banner says of a next wording whose text this site does not hold in the page's language (`reading.europeLaterWordingNotHeld`;
// the review of #972's finding F1) was reviewed by the same reviewer under the same delegation in the addendum of 2026-10-17
// (1 form, changed in review: "pour cet acte" before the wording, so its date and its text cannot be read as the act's).
// The one citation generator's words and labels (the plan's item 3.3; R65): the cited article's citation labels
// (`reading.cite`, `reading.citeConventional`, `reading.citeWithPermalink`, `reading.citeEvidence`,
// `reading.citeNotComposed`), each exported article's usual citation (`export.usualCitation`), and the whole `citation`
// section, said in the language of the text cited (`citation.months.*`, `citation.date`, `citation.firstOfMonth`,
// `citation.withPermalink`, `citation.luxembourg.*`, `citation.europe.*`, `citation.evidence.*`), were reviewed by the
// same reviewer under the same delegation in the addendum of 2026-10-17 on the citation generator (65 forms, 3 changed in
// review: the evidence form's label is "Avec ses éléments de vérification", never "probante", which would claim the
// probative value its own authenticity line denies; a Luxembourg sole article is "Art. unique", as the act prints it; and
// the date Lex was consulted is "(date au Luxembourg)", never a place).
// Exact references first (the plan's item 4.2; R59, R90, R13): the line under the home page's box, now that an identifier
// or a citation typed there opens its act (`search.searchScope`, replacing `search.luxembourgOnly`); what a reference was
// read as (`search.recognised`, `search.recognisedArticle`, `search.recognisedDate`, `search.recognisedArticleDate`), the
// date read when it names none (`search.recognisedToday`, `search.recognisedSearchDate`), the address it opens
// (`search.opensReading`, `search.opensDossier`) and why it opened nothing (`search.notOpenedDate`,
// `search.notOpenedTwoDates`); a date in words (`common.longDate`, `common.firstDay`, the twelve `common.months`); and the
// phase's four refusal sentences in `REFUSALS_FR.sentences`, were reviewed by the same reviewer under the same delegation
// in the addendum of 2026-10-17 for that pull request (the record names each, with what changed in review). Its repair
// round (the review of #974): the home line's "lorsque ce site le détient" (`search.searchScope`); a citation by a title
// this site holds no act under, searched as words (`search.titleNotHeld`); an act of a list that only the publisher's
// catalogue names (`search.candidateNotHeld`), and under an absent act's card (`search.catalogueNamesIt`); and five refusal
// sentences in `REFUSALS_FR.sentences`: the list's, which no longer says the acts are held or that there are several; the EU
// article's, which says "libellé" where the Luxembourg one says "version"; and the three said of an act this site holds
// (a Luxembourg version it does not hold; a version, or an EU wording, it holds with no article), were reviewed by an
// independent reviewer (Claude, AI reviewer, not the author) under the same delegation in the addendum of 2026-10-17 for
// the repair, "the repair of exact references (the review of #974)" (the record names each, with what changed in review).
// Reading by page (the plan's item 3.7; R73): a long version's contents and its pages (`reading.contents`,
// `reading.contentsPage`, `reading.pageShown`, `reading.pagesShown`), the navigation and its links (`reading.pager`,
// `reading.previousPage`, `reading.nextPage`, `reading.nextPageFailed`), the export composer's pin of every article
// (`export.pinAll`, `export.readingPages`, `export.pagesFailed`), and the two refusal sentences for an article a version
// does not hold (`anchor_not_in_version`) in `REFUSALS_FR.sentences`, were reviewed by an independent reviewer (Claude, AI
// reviewer, not the author) under the same delegation in the addendum of 2026-10-18 on reading by page (13 forms, 2 changed
// in review: the refusal sentences say what the index holds, "Cet index ne contient aucun article portant cet identifiant
// dans cette version", never that the version has no such article, which an article not admitted would make false; the
// English changed with them). The forms that review gave for its points 4 and 6 (`reading.contentsPageOne`,
// `reading.pageShownOne`, a page of one article named once; `export.readingPages`, "pages lues : {done} sur {pages}") ship
// as it gave them, with the English it named.
// A page past a version's last (the review of #978, F2): its sentence for a Luxembourg version and for an EU wording
// (`reading.pageNotHeld`, `reading.europePageNotHeld`, each `one` and `other`) and the links after it (`reading.firstPage`,
// `reading.lastPage`) were reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the same
// delegation in the addendum of 2026-10-18 on a page past a version's last (6 forms, 4 changed in review: the second clause
// says "il n’y a donc pas de page {page}", the site's page, never that the version has none, since the publisher's text has
// printed pages of its own; the English changed with it).
// Content /3's deleted wording and unmarked notes (slice S3b): the labels a screen reader hears at the start and end of
// wording the publisher marks deleted (`reading.deletedWordingStart`, `reading.deletedWordingEnd`), the sentence under an
// article that holds some (`reading.deletedWordingNote`) and the bracket of a note whose reference has no marker
// (`reading.unmarkedNote`) were reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the same
// delegation in the addendum of 2026-10-18 on deleted wording and unmarked notes (4 forms, 1 changed in review: the struck
// text is "celui que l’éditeur marque comme supprimé", never "un texte", which in legal French is an instrument, and the
// text a search covers is "interrogé").
// Search results by act and the plain no-hit card (the plan's items 4.3 and 4.5; R59, R21, R11, R90): the search page's
// introduction (`search.intro`), the title ladder's answer on top (`search.titleExact`, `search.titleStarts`,
// `search.titleWords`, `search.severalTitles`, `search.moreTitles`), what was found and what was searched (`search.found`,
// `search.passages`, `search.searched`, `search.searchedUndated`, `search.withheld`, `search.severalOnDate`), each act and
// its versions (`search.untitled`, `search.actPassages`, `search.version`, `search.versionAsPublished`), the no-hit card
// (`search.searchesRun`, `search.runReference`, `search.runReferenceNotRun`, `search.runTitles`, `search.runTitlesMatched`,
// `search.runTitlesNotRun`, `search.titleMatching`, `search.runText`, `search.runTextWords`, `search.exactOnly`,
// `search.officialSearch`, `search.officialSites`), each counted entry's `one` and `other`, and the search phase's two
// `anchor_not_in_version` sentences in `REFUSALS_FR.sentences` (the renderings the addendum on reading by page gave in its
// point 3, word for word), were reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the same
// delegation in the addendum of 2026-10-18 on search results by act (36 forms, 10 changed in review: the title stands last
// after "l’intitulé d’un acte :", since French puts no bare title after "de" and most titles end with their own full stop;
// one title is said in the singular; the text alone is said to be matched exactly, "Le texte n’a été interrogé que par
// correspondance exacte", since the titles are matched with case and accents folded, and "appliquée" is a word R21 refuses;
// "aucun article interrogé"; a date's acts named with their date; the introduction's scope set inside its second member;
// the English changed with each). The forms that review gave for its points 4, 6 and 7 (`search.titleMatching`,
// `search.actPassages` with its full stop, `search.officialSites.eurlex` naming the advanced search) ship as it gave them,
// with the English it named. `search.namesWork`, `search.severalWorks`, `search.candidate` and `search.hit` were removed.
// A Luxembourg state's date said with its role and a missing article said in place (slice S7a; R21): the reading's headings
// for a state dated by its entry into force (`reading.stateHeadingEntryIntoForce`, `reading.stateHeadingEntryIntoForceNext`),
// the notice before an article the version lacks (`reading.missingBefore`, `one` and `other`), the dossier's mark and its note
// (`dossier.entryIntoForceDate`, `dossier.entryIntoForceNote`), the citation generator's two forms
// (`citation.luxembourg.versionEntryIntoForce`, `citation.evidence.versionEntryIntoForce`), the search page's version line
// (`search.versionAsPublishedEntryIntoForce`) and the line under a passage an undated search folded (`search.sameWording`,
// `one` and `other`) were reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the same delegation
// in the addendum of 2026-10-18 on dates by role and missing articles (11 forms, 6 changed in review: the notice says "À cet
// endroit", since "ici" is this table's "on this site"; the dossier's cell answers its column with "aucune date indiquée" and
// gives the entry into force beside it, and the note classifies the value instead of saying when the act applies; a
// semicolon parts the act as published from its entry into force; the four English forms changed with them). Its optional
// points 6 and 7 were taken ("exacte"; the versions "interrogées par ce site"). "entrée en vigueur" is the noun of a typed
// publisher date, never "en vigueur" over an interval: the time-word guard lets the noun through and refuses the rest.
// In the repair round of that pull request (the review of #987), six forms were reviewed by the same reviewer under the
// same delegation, in the addendum "S7a's repair round (the review of #987)" of 2026-10-19. Its section "three forms" (3
// changed in review, the English with them): `reading.validityConflictEntryIntoForce` (an article whose own date differs
// from a state dated by its entry into force: "sa version est l’acte tel que publié, daté de son entrée en vigueur", so "son"
// is the act's), `search.dated` (the version whose publisher dates cover the date, "les dates de l’éditeur", and "ce site ne
// détient pas le texte de cette version") and `radar.reason.no_held_baseline` ("il n’y a donc pas de version de référence",
// never "rien à quoi la comparer", which an earlier held version makes false). Its section "the history's dates by role" (5
// strings approved as drafted): `history.carriedEntryIntoForce` and `history.carriedInEntryIntoForce` (the lineage's first row
// dated by its entry into force, "la première étant l’acte tel que publié, daté de son entrée en vigueur", never "à partir
// du") and `history.absentRowEntryIntoForce` (an absent state so dated).
// The catalogue between versions, the continuity breaks, the dates by role elsewhere, index 9's refusal sentences and the
// version caveats (slices S7b and S7c): the dossier's span by role (`dossier.statesEntryIntoForce`, `one` and `other`,
// `statesFromEntryIntoForce`, `statesInEntryIntoForce`, `statesInFromEntryIntoForce`) and its chain (`dossier.chainHeading`,
// `textlessHeading`, `textlessRow`, `gap`, `overlap`, `sameDate`, `unclearEndDay`, `invalidInterval`, `chainNote`); the
// reading's label and caveats (`reading.translation`, `consolidationCaveat`, `codeCaveat`); the history's breaks
// (`history.breaksHeading`, `breakTo`, `breakOpen`, the nine `breakReasons`, `afterBreak`, `breaksNote`); Compare by role
// (`compare.sideEntryIntoForce`, `sideEntryIntoForceNext`, `sameVersionEntryIntoForce`, `sameTextHeldEntryIntoForce`,
// `sameTextHeldOfSeveralEntryIntoForce`); the export by role (`export.itemEntryIntoForce`, `excludedEntryIntoForce`); the
// refusal card's (`refusalCard.candidateEntryIntoForce`, `candidateEntryIntoForceWithdrawalNotStated`, the six `reasons`,
// `baseActEndOfForce`, the four `versions`, the five `textNotHeld`, `textless`, `textlessNamed`); and the coverage page's
// changed note (`coverageAnswer.countsProvenance`) were reviewed by an independent reviewer (Claude, AI reviewer, not the
// author) under the same delegation in the addendum of 2026-10-19 on the catalogue's chain and the version caveats (59
// forms, 10 changed in review: "ce site n’a pas acquis cette version", which suits a version named "l’acte tel que publié";
// "fait foi", never "authentique", and "un outil de documentation"; a gap and a break named by their ends' roles, the
// break's end exclusive, "à la veille du", the English changed with these two; "les textes" after a plural; "un vide
// juridique"; an apposition's comma in the two spans; "de l’acte, publiée par l’éditeur"), with the form its point 1
// asked for (`refusalCard.publishedAct`, "publié le", an act's candidate), reviewed with them. The consolidation caveat
// ships in R20's words, which the review offered for the owner (L5): "Seuls font foi les textes publiés au Journal officiel
// (Mémorial A) : l’acte tel que publié, …, et les actes qui l’ont modifié, dont Lex ne détient pas encore la liste."
// `search.dated` and `reading.validityConflict` are S7a's repair's, and not this review's.
// The combined repair round of that pull request (the review of #987, 2026-10-26): five forms reviewed by an independent
// reviewer (Claude, AI reviewer, not the author) under the same delegation, in the addendum of 2026-10-26 "the closed end's
// day, an identifier outside its version, a candidate naming no expression and the radar's entry into force" (4 forms and 1
// added; 3 changed in review, the English too in 1): `refusalCard.reasons.after_closed_end` ("jusqu’à la veille du
// {closed}", the end being the first day not covered, and "la version dont la fin est la plus tardive", since after a nested
// version the last to begin is not the last to end), `refusalCard.reasons.outside_named_version` (an identifier "désigne"),
// `refusalCard.candidateUnexpressed` ("cette version", never "elle" after "l’acte tel que publié"; "propose"),
// `radar.rowEntryIntoForce` (approved) and `radar.notComparedAct` (added: "non comparé" after the act). With them, the
// review's proposals taken as worded: the search card's head for `outside_named_version` (the refusal translations),
// `dossier.overlap` ("à la veille du {to}") and `radar.reason.ambiguous_version` ("couvrent cette date", never "s’appliquent"
// after an entry into force).
// Today beside the dates and a search kept in the address (the plan's items 3.4 and 3.5; R13, R109): the search page's
// introduction (`search.intro`, its last sentence: where the phrase goes and where the page keeps it), today beside an older
// index's date (`search.searchedOlder`, `home.coverageOlder`), a fragment the form will not send (`search.addressUnread`) and
// a readable address read on another day than today (`reading.notToday`), with the reviewed `form.today` in its new places
// (the reading form and the export composer, Compare, Changes, the dossier), were reviewed by an independent reviewer
// (Claude, AI reviewer, not the author) under the same delegation in the addendum of 2026-10-19 on today and the search's
// address (5 forms, 2 changed in review, the English with them: the phrase is never in "the request's address", not "an
// address a server could record", which a shared link is; the part after "#" is what the browser never sends, never the
// page's address; a reading of another day "porte sur" the act, true over a refusal too, as this site holds it "pour cette
// date"). "au Luxembourg, nous sommes aujourd’hui le {today}" is the table's form of today inside a sentence.
// The five legal pages (the plan's item 7.1; R49, R50, R62, R70, R74, R92): every form of the sections `legal`,
// `legalNotice`, `privacy`, `terms`, `accessibility` and `cookies` (97 forms) was reviewed by an independent reviewer
// (Claude, AI reviewer, not the author) under the same delegation in the addendum of 2026-10-18 "the five legal pages"
// (14 changed in review: "l’éditeur de Lex" for the site's publisher, never bare "éditeur", which stays the law's
// publishers; a test's condition and a search engine's permission are "ne doit" and "ne sont autorisés à", never "ne
// peut"; the EU texts' acknowledgement quoted in English, as they carry it, through `{eurlexAcknowledgement}`; the
// quotation "le texte cité", the reference "citation"; "Veuillez ne faire figurer"; "fournisseur de messagerie"; no
// "servir"). Its entry 13 recommended "non conforme" for the accessibility status, with the English "non-conformant": the
// owner chose it on 2026-10-19 (STATUS-WEB.md), so the statement ships entry 13's recommended English and French word for
// word, in place of R62's "partiellement conforme". The lawyer's read (O4) of these pages is the owner's choice and has
// not been done.
// The legal pages without a person or a contact (the owner's decision of 2026-10-19: no person, no postal address, no
// e-mail address, no contact): the forms added, changed or moved in the five pages, `legalNotice.intro`,
// `legalNotice.sections.what` (`heading`, `text`, `notAdvice`), `legalNotice.sections.status` (`heading`, `text`,
// `prevails`), `legalNotice.sections.sources` (`heading`, `luxembourg`, `europe`, `changes`), `privacy.intro`,
// `privacy.sections.reading.request`, `.host` and `.nothingHeld`, `terms.intro`, `terms.sections.use` (`heading`, `text`),
// `terms.sections.texts.licences`, `terms.sections.warranty.text`, `accessibility.intro` and
// `accessibility.sections.law.text` (22 forms), were reviewed by an independent reviewer (Claude, AI reviewer, not the
// author) under the same delegation in the addendum of 2026-10-19 "the legal pages without a person or a contact" (7
// changed in review, the English with two of them: Lex is read "au fil du temps", never "dans le temps"; a text "que Lex
// montre", never "sur Lex" or "de Lex"; the reader "est libre de" use the texts, a permission, never "peut"; the part
// after "#" is the only antecedent of what the browser never sends; "Lex ne propose ni compte ni inscription"; the terms
// announce how to cite the texts, not Lex).
// The reader's right to complain to the CNPD, kept on the privacy notice as a driver decision of 2026-10-19 (one sentence, the
// regulator's site linked, no postal address): `privacy.sections.rights.heading` and `privacy.sections.rights.complaint`,
// entries 52 and 54 of 2026-10-18 less "aussi" and the address, were reviewed by an independent reviewer (Claude, AI
// reviewer, not the author) under the same delegation in the addendum of 2026-10-19 "the CNPD complaint line kept" (2
// forms, both approved).
// PR #990's review (2026-10-26): `legalNotice.sections.what.text`, qualified "pour les textes et les dates qu’il détient"
// as the terms' service sentence is, and `freshness.noAnswer`, the footer strip of a page that ships no script (the legal
// pages and the maintenance page), which never has an answer, were reviewed by an independent reviewer (Claude, AI
// reviewer, not the author) under the same delegation in the addendum of 2026-10-26 "the legal notice qualified and the
// freshness strip on a page with no answer" (2 forms, both approved; "ne contient aucune réponse", never "n’interroge pas
// le serveur", which the privacy notice's requests would contradict).
// The EU authenticity statement chosen by the act's date (the owner's decision of 2026-10-18 on PR #975's question, O4):
// `citation.europe.authenticity`, one string approved in the addendum of 2026-10-17 on the citation generator, is now two
// entries keyed by the platform's basis, `citation.europe.authenticity.electronic_edition` (that string, unchanged) and
// `citation.europe.authenticity.edition_by_publication_date` (new: "Pour un acte publié avant le 1er juillet 2013, l’édition
// imprimée du Journal officiel de l’Union européenne fait foi …", and "depuis cette date" for the electronic one); both
// were reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the same delegation in the addendum
// of 2026-10-18 on the EU authenticity statement (2 forms, none changed; the reviewer's optional points on the owner's
// English go to the lawyer's read, O4).
// Freshness and the commits (the plan's items 6.1 and 6.5; R87, R23, R69): the footer strip's region label and as-built
// line (`freshness.heading`, `freshness.asBuilt`), the index's build date (`freshness.built`, `freshness.builtUndated`),
// the two publisher channels (`freshness.channels.lu-legilux`, `freshness.channels.eu-eurlex`), each channel's last
// complete collection with its lag (`freshness.collected`, `one` and `other`, and `freshness.collectedUnstated`), the two
// commit labels and their unstated value (`freshness.indexCommit`, `freshness.serviceCommit`, `freshness.commitUnstated`),
// and the staleness banner (`freshness.staleHeading`, `freshness.stale`, `one` and `other`), 15 forms, were reviewed by an
// independent reviewer (Claude, AI reviewer, not the author) under the same delegation in the addendum of 2026-10-19 on
// freshness and the commits (12 changed in review: the count is whole days elapsed up to the server's answer, so it is said
// "jours entiers avant la réponse donnée par le serveur le {today}", never "avant le {today}", which the printed dates could
// contradict by one; the lag is "le décalage entre publication et collecte", never "délai"; a commit is a "révision du
// code", never "commit" or "version"; a channel's texts are "provenant de" their publisher; the region is "Mise à jour des
// données"; the English changed with the count and the banner, and says "(not recorded)").
// The three reference pages (Get help, How to read Lex and its glossary, Developers; the specification of record's launch
// set, R42, R46, R48, R50, R55): every form of the sections `reference`, `help`, `guide` and `developers` (242 forms) was
// reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the same delegation in the addendum of
// 2026-10-27 "the three reference pages, Get help, How to read Lex and its glossary, and Developers" (59 changed in review,
// the English too in 31: a Luxembourg version runs "jusqu’à la veille de la date de fin que Legilux lui donne", never "jusqu’à
// la fin", since the end the publisher states is the first day not covered; Lex never chooses between two versions "et que
// leurs textes diffèrent", since it serves one of several held with one text; an article held without text is named apart,
// never listed with the others; a permalink says first when the text it shows is not the one it pins; the freshness strip is
// a region a screen reader names, and the staleness banner stands only on a page with an answer; an adviser is a
// "conseiller"; the MCP transport keeps its name, "Streamable HTTP"; this release is "ce déploiement"), with the English of
// `developers.operations.notServed` the review's point 6 suggested. Its vocabulary rows are added below.
// No French entry ships unreviewed.
//
// Vocabulary: a work is an "acte" (a publisher's Cellar work, one record of an EU act, is an "œuvre de l’éditeur"), a
// Luxembourg state a "version", the publisher's article id an "identifiant d’article"; a wording (of an EU act, or of a
// Luxembourg article from state to state) is a "libellé", never a "rédaction" or a "formulation", and an EU date is never
// "applicable"; held is "détenu", never "disponible"; a build is a "déploiement"; legal advice is "consultation
// juridique"; the publisher's record is "les données de l’éditeur" (never "registre", which is the operations registry).
// On the reference pages (the addendum of 2026-10-27): an adviser is a "conseiller", never "conseil", which stays the advice;
// this release (the server that runs) is "ce déploiement"; the MCP transport is "le transport Streamable HTTP", its name kept,
// never "HTTP en flux"; a text "sert de réponse pour" a date, never "répond à"; a gap in the catalogue is an "intervalle sans
// version", "vide" being kept for "vide juridique"; the site "connaît une défaillance", never "est en défaut".
// An EU act's force dates (the plan's item 2.1; R79's E1, R21): the seventeen forms of `common.europeForceDates` (the
// heading, the six roles, `notPinnedNoCode`, `openEnd`, `qualifier`, the four role names, `notHeld`, `conflict` and
// `notWording`) were reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the same delegation
// in the addendum of 2026-10-19 on EU force dates (17 forms, 11 changed in review, the English too in 5: the heading says
// what this site holds, never "the act's dates", which the wording headings use for the Formex date; MA is "mise en
// application"; an end of validity is said "totale ou partielle", since 20 of run 16's 24 are partial; the qualifier is
// "la mention de l’éditeur", in parentheses after its date; and `notWording` says that no date above chooses a wording).
// Typography: a no-break space (U+00A0) stands before : ; ! ? % and » and after «, and the apostrophe is ’.
//
// This module is data only: the live pages' bundles carry it, so it imports nothing and reads nothing.

export const LIVE_CHROME_FR = Object.freeze({
  coverage: Object.freeze({
    title: 'Confiance et couverture',
    eyebrow: 'Passerelle',
    heading: 'Confiance et couverture',
    intro: 'Ce que contient le corpus monté sur ce serveur et ce qu’il a consigné comme manquant, demandé au serveur au chargement de la page. La requête ne contient aucun texte de recherche.',
    loading: 'Ce serveur est interrogé sur son rapport de couverture.',
  }),
  search: Object.freeze({
    title: 'Recherche',
    eyebrow: 'Recherche',
    heading: 'Rechercher dans le texte détenu',
    intro: 'Trouve les actes dont l’intitulé correspond à l’expression et, dans le texte que ce serveur détient et qu’il est autorisé à interroger, les articles qui la contiennent exactement telle que saisie : un texte que ses droits ne permettent pas de communiquer n’entre pas dans la recherche. L’expression est envoyée à ce serveur dans le corps de la requête, jamais dans l’adresse de la requête ; cette page ne conserve la recherche que dans sa propre adresse, après « # », dans une partie que le navigateur n’envoie jamais, de sorte qu’un lien vers cette adresse partage la recherche.',
    idle: 'Saisissez une expression pour rechercher dans le texte des articles que ce serveur détient et qu’il est autorisé à interroger.',
    population: Object.freeze({
      one: '« {query} » en {language} : {strict} avec l’expression exacte, {relaxed} avec chacun des mots, dans {works} acte.',
      other: '« {query} » en {language} : {strict} avec l’expression exacte, {relaxed} avec chacun des mots, dans {works} actes.',
    }),
    notCounted: 'non compté',
    lane: Object.freeze({ strict: 'expression exacte', relaxed: 'chacun des mots' }),
    titleExact: 'L’expression est l’intitulé d’un acte : {title}',
    titleStarts: 'L’expression est le début de l’intitulé d’un acte : {title}',
    titleWords: 'Chacun des mots de l’expression figure dans l’intitulé d’un acte : {title}',
    severalTitles: Object.freeze({
      one: 'L’expression correspond à l’intitulé de {count} acte :',
      other: 'L’expression correspond aux intitulés de {count} actes :',
    }),
    moreTitles: Object.freeze({
      one: '{count} autre acte',
      other: '{count} autres actes',
    }),
    noTitlesHeld: 'Cet index ne contient aucun intitulé d’acte ; l’expression a donc été recherchée dans le seul texte des articles.',
    ambiguousHeading: 'Actes ayant plusieurs versions au {date}',
    ambiguousNote: 'Ces actes ont plus d’une version applicable à cette date ; aucune n’est donc retenue et aucune ne fournit de résultat.',
    ambiguousWork: '{work} : {candidates}',
    noHit: 'Aucun article du texte que ce serveur détient et qu’il est autorisé à interroger ne contient « {query} ».',
    noText: 'Cet index ne contient aucun texte interrogeable en {language} ; il contient du texte en {languages}.',
    nextPage: 'Page suivante',
    euWording: 'Chaque résultat se trouve dans le libellé original de {celex} que ce serveur détient en {language}, daté du {date} et épinglé par son empreinte : {permalink}',
    euHit: '{heading} de {celex}, libellé du {date}',
    notHeldHeading: 'Ce que cette recherche ne couvre pas',
    placeholder: 'Rechercher par intitulé, numéro, ELI ou CELEX, ou par des mots du texte',
    dated: 'Seuls des actes luxembourgeois ont été interrogés. Pour chacun, la version dont les dates de l’éditeur couvrent le {date} a été interrogée ; un acte sans une telle version, ou pour lequel ce site ne détient pas le texte de cette version, ne l’a pas été. Un acte détenu uniquement tel que publié est interrogé tel que publié, et peut avoir été modifié depuis.',
    undated: 'Seuls des actes luxembourgeois ont été interrogés, sans date : toutes les versions que ce site détient ont été interrogées, et chaque résultat indique la date de sa version.',
    noHitLuxembourg: 'Aucun article du texte luxembourgeois que ce serveur détient et qu’il est autorisé à interroger ne contient « {query} ».',
    searchScope: 'La recherche par mots porte sur le droit luxembourgeois. Un ELI, un numéro CELEX, une référence au Mémorial, un permalien ou la citation d’un article d’un acte, avec ou sans date, ouvre cet acte, luxembourgeois ou de l’UE, lorsque ce site le détient. Pour interroger un acte de l’UE, désignez-le par son numéro CELEX sur la {link}.',
    searchPage: 'page de recherche',
    recognised: 'Référence reconnue : {act}.',
    recognisedArticle: 'Référence reconnue : {act}, article {article}.',
    recognisedDate: 'Référence reconnue : {act}, au {date}.',
    recognisedArticleDate: 'Référence reconnue : {act}, article {article}, au {date}.',
    recognisedToday: 'Aucune date n’a été indiquée : la date lue est celle d’aujourd’hui au Luxembourg, le {date}.',
    recognisedSearchDate: 'La référence n’indique aucune date : la date lue est celle de la recherche, le {date}.',
    opensReading: 'Ouverture de la lecture : {link}',
    opensDossier: 'Ouverture du dossier : {link}',
    notOpenedDate: '« {date} » ne désigne aucun jour du calendrier ; rien n’a donc été ouvert, et aucun mot n’a été recherché.',
    notOpenedTwoDates: 'La référence indique deux dates qui ne concordent pas ; rien n’a donc été ouvert, et aucun mot n’a été recherché.',
    titleNotHeld: 'Ce site ne détient aucun acte sous l’intitulé que cite l’expression ; ses mots ont donc été recherchés.',
    candidateNotHeld: '{act} (ce site n’en détient aucune version)',
    catalogueNamesIt: 'Les données de l’éditeur désignent cet acte ({works}), et ce site n’en détient aucune version.',
    found: '« {query} » en {language} : {passages} avec l’expression exacte, dans {acts}.',
    passages: Object.freeze({
      one: '{count} passage',
      other: '{count} passages',
    }),
    searched: 'Recherche effectuée dans {acts} du droit luxembourgeois en {language}, en {versions} comptant {articles} au texte interrogeable, d’après l’index construit le {date}.',
    searchedUndated: 'Recherche effectuée dans {acts} du droit luxembourgeois en {language}, en {versions} comptant {articles} au texte interrogeable ; l’index que ce site détient ne consigne aucune date de construction.',
    searchedOlder: 'Recherche effectuée dans {acts} du droit luxembourgeois en {language}, en {versions} comptant {articles} au texte interrogeable, d’après l’index construit le {date} ; au Luxembourg, nous sommes aujourd’hui le {today}.',
    addressUnread: 'La recherche contenue dans l’adresse de cette page n’a pas pu être lue ; aucune recherche n’a donc été effectuée.',
    withheld: Object.freeze({
      one: '{count} version dont les droits ne permettent pas de communiquer le texte n’a pas été interrogée.',
      other: '{count} versions dont les droits ne permettent pas de communiquer le texte n’ont pas été interrogées.',
    }),
    severalOnDate: Object.freeze({
      one: '{count} acte ayant plusieurs versions au {date} n’a pas été interrogé, aucune n’étant retenue.',
      other: '{count} actes ayant plusieurs versions au {date} n’ont pas été interrogés, aucune n’étant retenue.',
    }),
    untitled: '{work} (cet index ne contient aucun intitulé pour cet acte)',
    actPassages: Object.freeze({
      one: '{count} passage dans cet acte.',
      other: '{count} passages dans cet acte.',
    }),
    version: 'Version applicable à partir du {date} (éditeur)',
    versionAsPublished: 'L’acte tel que publié, applicable à partir du {date} (éditeur)',
    versionAsPublishedEntryIntoForce: 'L’acte tel que publié ; entrée en vigueur le {date} (éditeur)',
    sameWording: Object.freeze({
      one: 'Le même libellé figure dans {count} autre version interrogée par ce site.',
      other: 'Le même libellé figure dans {count} autres versions interrogées par ce site.',
    }),
    searchesRun: 'Ce qui a été recherché',
    runReference: 'Références exactes : l’expression n’est aucune des références que ce site sait lire (un ELI, un numéro CELEX, une référence au Mémorial, un permalien ou une citation).',
    runReferenceNotRun: 'Références exactes : non lues, un acte ayant été désigné.',
    runTitles: 'Intitulés : aucun intitulé d’un acte que ce site détient ne correspond à l’expression.',
    runTitlesMatched: Object.freeze({
      one: 'Intitulés : l’expression correspond à l’intitulé indiqué ci-dessus.',
      other: 'Intitulés : l’expression correspond aux intitulés indiqués ci-dessus.',
    }),
    titleMatching: 'Les intitulés sont comparés sans tenir compte des majuscules ni des accents.',
    runTitlesNotRun: 'Intitulés : non recherchés, un acte ayant été désigné.',
    runText: 'Texte : aucun article interrogé ne contient l’expression exactement telle que saisie.',
    runTextWords: 'Texte, chacun des mots dans un ordre quelconque : aucun article interrogé ne les contient tous.',
    exactOnly: 'Le texte n’a été interrogé que par correspondance exacte : les majuscules, les accents et chacun des autres caractères de l’expression comptent, et rien n’est ramené à une forme commune, réduit à son radical ni étendu. Une recherche moins stricte dans le texte reste désactivée tant que sa propre vérification n’est pas réussie.',
    officialSearch: 'Les pages de recherche des éditeurs :',
    officialSites: Object.freeze({
      legilux: 'Recherche sur Legilux, pour le droit luxembourgeois',
      eurlex: 'Recherche avancée sur EUR-Lex, pour le droit de l’UE',
    }),
  }),
  dossier: Object.freeze({
    title: 'Dossier',
    eyebrow: 'Dossier',
    heading: 'Le dossier d’un acte',
    intro: 'Ce que ce serveur détient pour un acte : les intitulés et les versions datées par l’éditeur d’un acte luxembourgeois, ou les expressions d’un acte de l’UE et le libellé original de chacune, et ce que le dossier ne contient pas. L’identifiant est envoyé à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte pour consulter ce que ce serveur détient à son sujet.',
    noTitle: 'Cet index ne contient aucun intitulé pour cet acte.',
    shortTitle: '{title} (intitulé court)',
    titleGroup: '{language} : {titles}',
    heldIn: '{iri}, détenu en {languages}.',
    states: Object.freeze({
      one: '{count} version, du {from} au {to}.',
      other: '{count} versions, du {from} au {to}.',
    }),
    statesIn: Object.freeze({
      one: '{count} version en {language}, du {from} au {to}.',
      other: '{count} versions en {language}, du {from} au {to}.',
    }),
    articlesHeld: 'Articles détenus',
    articlesNotAdmitted: 'Articles non admis',
    notHeldHeading: 'Ce que ce dossier ne contient pas',
    euExpressions: Object.freeze({
      one: '{count} expression détenue, présentée avec son libellé original.',
      other: '{count} expressions détenues, chacune présentée avec son libellé original.',
    }),
    wordingDate: 'Date du libellé',
    statesEntryIntoForce: Object.freeze({
      one: '{count} version, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur).',
      other: '{count} versions, l’acte tel que publié dans chacune de ses langues, daté de son entrée en vigueur le {from} (éditeur).',
    }),
    statesFromEntryIntoForce: '{count} versions : la première, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ; la plus récente, applicable à partir du {to}.',
    statesInEntryIntoForce: '{count} version en {language}, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur).',
    statesInFromEntryIntoForce: '{count} versions en {language} : la première, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ; la plus récente, applicable à partir du {to}.',
    chainHeading: 'Le catalogue de l’éditeur entre les versions',
    textlessHeading: 'Versions dont ce site ne détient pas les textes',
    textlessRow: '{version} : {why}.',
    gap: 'Du {from}, fin que le catalogue de l’éditeur indique pour une version, au {to}, début de la suivante, ce catalogue ne recense aucune version : ce site ne comble pas cet intervalle.',
    overlap: 'Du {from} à la veille du {to}, deux versions du catalogue de l’éditeur sont toutes deux applicables (éditeur).',
    sameDate: 'Pour le {date}, le catalogue de l’éditeur recense {count} versions.',
    unclearEndDay: 'Le {date} est la fin que le catalogue de l’éditeur indique pour une version, la veille du début de la suivante ; on ne sait pas si ce jour lui appartient, et ce site ne l’attribue à aucune des deux.',
    invalidInterval: 'La version du {date} a, dans le catalogue de l’éditeur, une fin, le {to}, antérieure ou égale à son propre début : elle ne couvre aucune date ; son propre lien l’ouvre toujours.',
    chainNote: 'Ce sont les dates du catalogue de l’éditeur telles que ce site les a lues ; un intervalle sans version dans le catalogue n’est pas un vide juridique.',
    entryIntoForceDate: 'aucune date indiquée (entrée en vigueur : {date})',
    entryIntoForceNote: 'La mention « aucune date indiquée » signifie que le fichier de l’acte n’indique aucune date d’applicabilité exacte ; la date entre parenthèses est celle de l’entrée en vigueur de l’acte dans les données de l’éditeur, et non une date d’applicabilité.',
  }),
  reading: Object.freeze({
    pinReplaced: 'Ce lien épingle {pinned}, qui n’est pas la version que ce site communique pour cette date ; la version qu’il communique est affichée ci-dessous.',
    europePinReplaced: 'Ce lien épingle {pinned}, qui n’est pas le libellé qui sert de réponse pour cette date sur ce site ; celui qui sert de réponse est affiché ci-dessous.',
    pinNotHeld: 'Ce lien épingle {pinned}, qui n’est pas la version que ce site détient pour cette date ; la version qu’il détient est {current}.',
    europePinNotHeld: 'Ce lien épingle {pinned}, qui n’est pas un libellé que ce site détient pour cette date ; le libellé qui sert de réponse pour cette date sur ce site est {current}.',
    citedTitle: '{work}, {article}',
    citedFrom: 'Cité d’après {source}.',
    citedWithoutText: 'Détenu sans texte dans {source}.',
    wholeAct: 'L’acte entier dans cette version',
    europeWholeAct: 'L’acte entier dans ce libellé',
    newerVersion: 'Une version plus récente est applicable à partir du {date} (éditeur).',
    europeNewerWording: 'Un libellé ultérieur de cet acte est détenu : le libellé du {date}.',
    europeLaterWordingNotHeld: 'Les données de l’éditeur nomment, pour cet acte, un libellé ultérieur du {date}, dont ce site ne détient pas le texte dans cette langue.',
    openToday: 'Lire cet acte à la date du jour',
    notToday: 'Cette page porte sur l’acte à la date du {date}, tel que ce site le détient pour cette date ; au Luxembourg, nous sommes aujourd’hui le {today}.',
    title: 'Lecture',
    eyebrow: 'Lecture',
    heading: 'Le texte à une date',
    intro: 'Le texte d’un acte luxembourgeois tel qu’il se présentait à une date, ou celui d’un acte de l’UE dans le libellé qui sert de réponse pour cette date (son libellé original ou un libellé consolidé ultérieur), article par article, tel que l’éditeur l’a écrit, avec ce qu’il faut pour le citer. L’identifiant et la date sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et une date pour lire son texte à cette date.',
    rights: 'Texte communiqué sous {rights}. Lu au {date}.',
    rightsIn: 'Texte communiqué sous {rights}. Lu au {date} en {language}.',
    stateHeading: '{work}, {language}, la version applicable à partir du {from}',
    stateHeadingNext: '{work}, {language}, la version applicable à partir du {from} (la version suivante détenue s’applique à partir du {next})',
    stateHeadingEntryIntoForce: '{work}, {language}, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur)',
    stateHeadingEntryIntoForceNext: '{work}, {language}, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) (la version suivante détenue s’applique à partir du {next})',
    missingBefore: Object.freeze({
      one: 'À cet endroit, le document de l’éditeur comporte un article que ce site ne détient pas dans cette version.',
      other: 'À cet endroit, le document de l’éditeur comporte {count} articles que ce site ne détient pas dans cette version.',
    }),
    counts: Object.freeze({
      one: '{count} article cité ; détenus sans texte : {withoutText} ; non admis : {notAdmitted} ; date propre différente de celle de la version : {conflicts}.',
      other: '{count} articles cités ; détenus sans texte : {withoutText} ; non admis : {notAdmitted} ; date propre différente de celle de la version : {conflicts}.',
    }),
    validityConflict: 'La date propre de cet article est le {own} ; sa version s’applique à partir du {state}.',
    validityConflictEntryIntoForce: 'La date propre de cet article est le {own} ; sa version est l’acte tel que publié, daté de son entrée en vigueur le {state} (éditeur).',
    translation: 'Ce texte est l’expression en {language} de l’acte, publiée par l’éditeur à côté de son expression française {french}. Les données de l’éditeur n’indiquent pas quelle version linguistique fait foi.',
    consolidationCaveat: 'Une version consolidée est un outil de documentation sans valeur probante. Seuls font foi les textes publiés au Journal officiel (Mémorial A) : l’acte tel que publié, {act}, et les actes qui l’ont modifié, dont Lex ne détient pas encore la liste.',
    codeCaveat: 'Une version d’un code est un outil de documentation sans valeur probante : les textes qui font foi sont les actes publiés au Journal officiel (Mémorial A) qu’elle rassemble.',
    deletedWordingStart: 'Texte que l’éditeur marque comme supprimé :',
    deletedWordingEnd: 'Fin du texte marqué comme supprimé.',
    deletedWordingNote: 'Le texte barré est celui que l’éditeur marque comme supprimé dans son texte. Il ne fait pas partie du texte que ce site cite ou interroge.',
    unmarkedNote: 'sans appel de note',
    evidence: 'Empreinte du texte {text}, empreinte du corps {body}, source officielle {source}, {permalink}',
    withoutText: 'Détenus sans texte : {articles}.',
    notHeldHeading: 'Ce que cette lecture ne contient pas',
    europeWordingHeading: '{celex}, {language}, le libellé original du {date} (la date de l’acte dans le paquet Formex de l’éditeur ; aucun libellé ultérieur n’est détenu)',
    europeOriginalHeading: '{celex}, {language}, le libellé original du {date} (la date de l’acte dans le paquet Formex de l’éditeur)',
    europeConsolidatedHeading: '{celex}, {language}, le libellé consolidé du {date} (la date de consolidation de l’éditeur)',
    europeHoldsUntil: 'Ce libellé sert de réponse pour les dates du {date} à la veille du {next}, date du libellé suivant.',
    europeSameDateWorks: Object.freeze({
      one: '{count} autre œuvre de l’éditeur porte cette date ; avec ce même texte détenu ici : {held} ; sans texte détenu ici : {notHeld}.',
      other: '{count} autres œuvres de l’éditeur portent cette date ; avec ce même texte détenu ici : {held} ; sans texte détenu ici : {notHeld}.',
    }),
    europeUnplaced: Object.freeze({
      one: '{count} libellé de cet acte n’a pas de date de l’éditeur exploitable et n’est pas situé dans le temps ; avec ce même texte détenu ici : {held} ; sans texte détenu ici : {notHeld}.',
      other: '{count} libellés de cet acte n’ont pas de date de l’éditeur exploitable et ne sont pas situés dans le temps ; avec ce même texte détenu ici : {held} ; sans texte détenu ici : {notHeld}.',
    }),
    europeLatest: 'Il s’agit du dernier libellé détenu : il sert de réponse pour toute date ultérieure, et une modification que l’éditeur n’a pas encore consolidée n’y figure pas.',
    europeCounts: Object.freeze({
      one: '{count} article cité ; détenus sans texte : {withoutText}.',
      other: '{count} articles cités ; détenus sans texte : {withoutText}.',
    }),
    cite: 'Citer cet article',
    citeConventional: 'Citation usuelle',
    citeWithPermalink: 'Avec son permalien',
    citeEvidence: 'Avec ses éléments de vérification',
    citeNotComposed: 'Lex ne compose pas de citation usuelle pour cet article ; son permalien le cite.',
    contents: 'Sommaire',
    contentsPage: 'Page {number} : {first} à {last}',
    contentsPageOne: 'Page {number} : {first}',
    pageShown: 'Page {number} sur {pages} : {first} à {last}.',
    pageShownOne: 'Page {number} sur {pages} : {first}.',
    pagesShown: 'Pages {from} à {to} sur {pages} : {first} à {last}.',
    pager: 'Pages',
    previousPage: 'Articles précédents',
    nextPage: 'Articles suivants',
    nextPageFailed: 'Les articles suivants n’ont pas pu être chargés ici ; le lien les ouvre sur une page qui leur est propre.',
    pageNotHeld: Object.freeze({
      one: 'Cette version se lit ici sur {count} page ; il n’y a donc pas de page {page}.',
      other: 'Cette version se lit ici sur {count} pages ; il n’y a donc pas de page {page}.',
    }),
    europePageNotHeld: Object.freeze({
      one: 'Ce libellé se lit ici sur {count} page ; il n’y a donc pas de page {page}.',
      other: 'Ce libellé se lit ici sur {count} pages ; il n’y a donc pas de page {page}.',
    }),
    firstPage: 'Première page',
    lastPage: 'Dernière page (page {pages})',
  }),
  history: Object.freeze({
    title: 'Historique d’une disposition',
    eyebrow: 'Historique d’une disposition',
    heading: 'Un article à travers ses versions',
    intro: 'Quelles versions détenues d’un acte luxembourgeois comportent l’identifiant d’article de l’éditeur, si son libellé a changé d’une version à la suivante, et quelles versions détenues ne le comportent pas. L’identifiant de l’acte et l’identifiant d’article sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et l’identifiant d’article de l’éditeur pour le suivre à travers les versions détenues.',
    anchorHeading: '{anchor} dans {work}',
    carried: Object.freeze({
      one: 'Présent dans {count} version détenue, à partir du {from} ;',
      other: 'Présent dans {count} versions détenues, à partir du {from} ;',
    }),
    carriedIn: Object.freeze({
      one: 'Présent dans {count} version détenue en {language}, à partir du {from} ;',
      other: 'Présent dans {count} versions détenues en {language}, à partir du {from} ;',
    }),
    carriedEntryIntoForce: Object.freeze({
      one: 'Présent dans {count} version détenue, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ;',
      other: 'Présent dans {count} versions détenues, la première étant l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ;',
    }),
    carriedInEntryIntoForce: Object.freeze({
      one: 'Présent dans {count} version détenue en {language}, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ;',
      other: 'Présent dans {count} versions détenues en {language}, la première étant l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ;',
    }),
    absent: Object.freeze({
      one: '{count} version détenue ne le comporte pas.',
      other: '{count} versions détenues ne le comportent pas.',
    }),
    runs: Object.freeze({
      one: '{language} : {count} séquence de libellé, libellés distincts : {distinct}',
      other: '{language} : {count} séquences de libellé, libellés distincts : {distinct}',
    }),
    wording: Object.freeze({ first: 'premier libellé détenu', changed: 'libellé modifié', unchanged: 'libellé inchangé' }),
    wordingColumn: 'Libellé',
    ownDateColumn: 'Date propre de l’article',
    ownDateNotStated: 'non indiquée',
    ownDateDiffers: '{date} (diffère de celle de la version)',
    absentHeading: 'Versions détenues qui ne comportent pas {anchor}',
    absentRow: '{language}, à partir du {from} : {permalink}',
    absentRowEntryIntoForce: '{language}, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) : {permalink}',
    breaksHeading: 'Dates qu’aucune version dont le texte est détenu ne couvre',
    breakTo: 'En {language}, du {from} à la veille du {to} : {reasons}.',
    breakOpen: 'En {language}, à partir du {from}, sans version ultérieure dont ce site détient le texte : {reasons}.',
    breakReasons: Object.freeze({
      withheld_rights: 'une version dont les droits n’ont pas permis d’admettre le texte',
      not_available_pdf: 'une version dont le fichier de l’éditeur est un PDF, que ce site ne lit pas',
      not_available_derivation: 'une version dont le texte n’a pas pu être lu dans le fichier de l’éditeur',
      not_available_misfiled: 'une version dont le fichier de l’éditeur est celui d’une autre version',
      not_acquired: 'une version que ce site n’a pas acquise',
      catalogue_hole: 'un intervalle sans version dans le catalogue de l’éditeur',
      publisher_end_convention_unclear: 'un jour que le catalogue de l’éditeur laisse incertain entre deux versions',
      publisher_interval_invalid: 'une version dont la fin est antérieure ou égale à son début',
      no_expression_in_language: 'une version sans expression détenue dans cette langue',
    }),
    afterBreak: 'après des dates qu’aucune version dont le texte est détenu ne couvre',
    breaksNote: 'Aucune version dont ce site détient le texte ne couvre ces dates : un libellé inchangé de part et d’autre n’en dit donc rien.',
  }),
  compare: Object.freeze({
    title: 'Comparer',
    eyebrow: 'Comparer',
    heading: 'Deux versions, article par article',
    intro: 'Les versions d’un acte luxembourgeois applicables à deux dates, comparées selon les identifiants d’article et le libellé de l’éditeur, sans rien affirmer sur l’effet juridique. L’identifiant et les dates sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et deux dates pour comparer les versions applicables à ces dates.',
    summary: '{work} : {from} comparé au {to}.',
    summaryIn: '{work} : {from} comparé au {to}, en {language}.',
    sideFrom: 'Date de départ',
    sideTo: 'Date d’arrivée',
    side: '{side} : la version applicable à partir du {from} ; articles : {articles} ; date propre différente de celle de la version : {conflicts} ; {permalink}',
    sideNext: '{side} : la version applicable à partir du {from} (la version suivante détenue s’applique à partir du {next}) ; articles : {articles} ; date propre différente de celle de la version : {conflicts} ; {permalink}',
    sideEntryIntoForce: '{side} : l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ; articles : {articles} ; date propre différente de celle de la version : {conflicts} ; {permalink}',
    sideEntryIntoForceNext: '{side} : l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) (la version suivante détenue s’applique à partir du {next}) ; articles : {articles} ; date propre différente de celle de la version : {conflicts} ; {permalink}',
    counts: 'Modifiés : {changed} ; ajoutés : {added} ; supprimés : {removed} ; inchangés : {unchanged}.',
    sameVersion: 'Les deux dates relèvent de la même version, applicable à partir du {from} (éditeur).',
    sameTextHeld: 'Pour les deux dates, le texte retenu est le seul texte détenu, l’acte tel que publié, applicable à partir du {from} (éditeur) ; les versions ultérieures ne sont pas détenues.',
    sameTextHeldOfSeveral: 'Pour les deux dates, le texte retenu est le même texte détenu, l’acte tel que publié, applicable à partir du {from} (éditeur) ; aucune version consolidée n’est détenue.',
    sameVersionEntryIntoForce: 'Les deux dates relèvent de la même version, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur).',
    sameTextHeldEntryIntoForce: 'Pour les deux dates, le texte retenu est le seul texte détenu, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ; les versions ultérieures ne sont pas détenues.',
    sameTextHeldOfSeveralEntryIntoForce: 'Pour les deux dates, le texte retenu est le même texte détenu, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ; aucune version consolidée n’est détenue.',
    betweenNamed: Object.freeze({
      one: 'Les données de l’éditeur nomment, pour cet acte, {count} version consolidée dont la date d’applicabilité se situe après le {after} et au plus tard le {onOrBefore} :',
      other: 'Les données de l’éditeur nomment, pour cet acte, {count} versions consolidées dont les dates d’applicabilité se situent après le {after} et au plus tard le {onOrBefore} :',
    }),
    betweenEndOfForce: 'Les données de l’éditeur indiquent le {date} comme date de fin de vigueur de cet acte, date qui se situe après le {after} et au plus tard le {onOrBefore}.',
    status: Object.freeze({ changed: 'modifié', added: 'ajouté', removed: 'supprimé', unchanged: 'inchangé' }),
    row: '{article} : {status} (libellé {from} → {to})',
    noWording: 'aucun',
    kept: Object.freeze({
      one: '{count} article inchangé',
      other: '{count} articles inchangés',
    }),
    bound: Object.freeze({ from: 'de départ', to: 'd’arrivée' }),
    notCompared: '{language} n’est pas comparé : {reason} (date {bound}).',
  }),
  radar: Object.freeze({
    title: 'Radar',
    eyebrow: 'Radar',
    heading: 'Le radar des modifications',
    intro: 'Les versions datées par l’éditeur des actes luxembourgeois que ce serveur détient dans un intervalle de dates, chacune avec la version qu’elle a remplacée, en indiquant si son libellé a changé. Les dates et tout identifiant sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez deux dates pour lister les versions datées par l’éditeur dans cet intervalle.',
    summary: 'Du {from} au {to} : {states} de {works} ; actes détenus : {held}.',
    states: Object.freeze({
      one: '{count} version',
      other: '{count} versions',
    }),
    works: Object.freeze({
      one: '{count} acte',
      other: '{count} actes',
    }),
    noRow: 'Aucune version détenue n’est datée dans cet intervalle.',
    windowMisses: 'Cet intervalle ne recoupe pas ce que cet index détient.',
    windowMissesRange: 'Cet intervalle ne recoupe pas ce que cet index détient, du {first} au {last}.',
    row: '{work}, {language}, à partir du {from} : {verdict}. {permalink}',
    rowEntryIntoForce: '{work}, {language}, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) : {verdict}. {permalink}',
    wording: Object.freeze({ changed: 'libellé modifié', unchanged: 'libellé inchangé' }),
    compared: '{wording} par rapport à la version du {date}{baseline} ; modifiés : {changed} ; ajoutés : {added} ; supprimés : {removed} ; inchangés : {unchanged}',
    notCompared: 'non comparée : {reason}{named}',
    notComparedAct: 'non comparé : {reason}{named}',
    reason: Object.freeze({
      first_held_state: 'c’est la première version que cet index détient pour cet acte et cette langue ; il n’y a donc rien à quoi la comparer',
      ambiguous_version: 'plusieurs versions de l’acte couvrent cette date ; aucune n’est donc comparée',
      ambiguous_baseline: 'plusieurs versions s’appliquent la veille de cette date ; aucune version de référence n’est donc retenue',
      profiles_differ: 'cette version et sa version de référence ont été lues selon des profils de règles différents ; elles ne sont donc pas comparées',
      no_held_baseline: 'la veille de cette date n’est couverte par aucune version dont ce site détient le texte ; il n’y a donc pas de version de référence',
    }),
    named: '({label} {permalinks})',
    baseline: 'référence',
    candidates: 'candidates',
    truncated: 'La liste s’arrête avant le {date} ; une nouvelle demande reprend à partir de cette date.',
  }),
  export: Object.freeze({
    title: 'Composition d’export',
    eyebrow: 'Composition d’export',
    heading: 'Emporter des articles, avec leurs citations',
    intro: 'Lisez un acte luxembourgeois à une date, ou un acte de l’UE dans le libellé qui sert de réponse pour cette date, épinglez les articles dont vous avez besoin et enregistrez-les en JSON, CSV ou PDF. Chaque article exporté porte sa citation, l’empreinte de son texte et sa source officielle, avec les droits sous lesquels il a été communiqué ou, pour un texte de l’UE, la mention de la source avec laquelle il a été communiqué et la déclaration sur l’édition qui fait foi ; chaque export porte le filigrane et indique ce qu’il exclut, avec la raison de chaque exclusion. L’identifiant et la date sont envoyés à ce serveur dans la requête et nulle part ailleurs ; le fichier est créé dans cette page, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et une date, puis épinglez les articles à emporter.',
    readOn: 'Lu au {date}. Épinglez les articles à exporter.',
    readOnIn: 'Lu au {date} en {language}. Épinglez les articles à exporter.',
    pin: 'Épingler {article}',
    withoutTextHeading: 'Détenus sans texte',
    withoutTextNote: 'détenu sans texte ; un export le consigne comme exclu, avec sa raison.',
    panelHeading: 'Export',
    nothingPinned: 'Rien n’est encore épinglé. Épinglez un article ci-dessus : son export apparaît ici, avec ce qu’il contient.',
    counts: Object.freeze({
      one: '{count} article épinglé ; exportés avec texte : {withText} ; exclus : {excluded}.',
      other: '{count} articles épinglés ; exportés avec texte : {withText} ; exclus : {excluded}.',
    }),
    rights: 'Texte communiqué sous {rights}.',
    snapshot: 'Lu au {date}, à partir de l’instantané observé le {observedAt}. Corpus {corpus}, index {index}, registre {registry}.',
    item: '{article} ({language}, applicable à partir du {from}) : {citation}, empreinte du texte {digest}, source officielle {source}',
    excluded: '{article} ({language}, applicable à partir du {from}) : exclu, {reason}, {citation}',
    itemEntryIntoForce: '{article} ({language}, l’acte tel que publié ; entrée en vigueur le {from}) : {citation}, empreinte du texte {digest}, source officielle {source}',
    excludedEntryIntoForce: '{article} ({language}, l’acte tel que publié ; entrée en vigueur le {from}) : exclu, {reason}, {citation}',
    europeRights: 'Texte communiqué avec la mention de la source {acknowledgement}, que porte chaque article exporté, et avec la déclaration ci-dessous sur l’édition qui fait foi.',
    europeItem: '{article} ({language}, libellé du {date}) : {citation}, empreinte du texte {digest}, source officielle {source}',
    europeExcluded: '{article} ({language}, libellé du {date}) : exclu, {reason}, {citation}',
    save: Object.freeze({ json: 'Enregistrer en JSON', csv: 'Enregistrer en CSV', pdf: 'Enregistrer en PDF' }),
    formatRefused: 'Le format {format} n’est pas proposé pour cet export : {reason}.',
    composeFailed: 'Cet export ne peut pas être composé : {reason}.',
    jsonSummary: 'Le JSON tel qu’il sera enregistré',
    usualCitation: 'Citation usuelle : {citation}',
    pinAll: 'Épingler tous les articles',
    readingPages: 'Lecture de toutes les pages ; pages lues : {done} sur {pages}.',
    pagesFailed: 'Les pages n’ont pas toutes pu être lues ; rien de plus n’a été épinglé.',
  }),
  card: Object.freeze({
    heading: 'Fiche d’évaluation',
    target: 'Portée de l’exécution : {target}',
    machineReadable: 'La même fiche pour les machines, en JSON',
    clean: 'Chaque critère automatique de cette fiche est satisfait, et chaque contrôle par permutation a détecté sa permutation.',
    notClean: '{gates}, et {controls}.',
    gatesNotPassing: Object.freeze({
      one: '{count} critère n’est pas satisfait',
      other: '{count} critères ne sont pas satisfaits',
    }),
    controlsNotCaught: Object.freeze({
      one: '{count} contrôle par permutation n’a pas détecté la permutation',
      other: '{count} contrôles par permutation n’ont pas détecté la permutation',
    }),
    listed: '{summary} ({list})',
    gateListed: '{gate} dans {set}, {arm} : {verdict}',
    controlListed: '{control} dans {set}, {arm} : {verdict}',
    verdict: Object.freeze({ pass: 'satisfait', fail: 'non satisfait', not_measured: 'non mesuré' }),
    controlVerdict: Object.freeze({
      caught_the_shuffle: 'a détecté la permutation',
      missed_the_shuffle: 'n’a pas détecté la permutation',
      not_applicable: 'sans objet',
    }),
    caption: '{set}, {arm} : {cases} cas, empreinte {digest}',
    columns: Object.freeze({
      gate: 'Critère',
      verdict: 'Résultat',
      value: 'Valeur',
      threshold: 'Seuil',
      cases: 'Cas',
      wilson: 'Wilson 95 %',
      ruleOfThree: 'Règle de trois (95 %)',
    }),
    verdictReason: '{verdict} ({reason})',
    none: 'aucune',
    notARate: 'pas un taux',
    interval: 'de {low} à {high}',
    ruleOfThree: 'taux d’échec inférieur à {bound}',
    controlsHeading: 'Contrôles par permutation',
    control: '{control} sur {set}, {arm} : {verdict}, {reason} (graine {seed}, {cases} cas, empreinte {digest}).',
    controlNote: '{control} sur {set}, {arm} : {verdict}, {reason} (graine {seed}, {cases} cas, empreinte {digest}). {note}.',
    statisticalHeading: 'Lignes statistiques',
    statistical: '{dataset}, {name} : pas encore annoté. Critères {gates}. {governedBy}.',
    negativeHeading: 'Résultats négatifs',
    negative: 'Hypothèse : {hypothesis}. Jeu de données : {dataset}. Résultat : {result}. Décision : {decision}. Ce qui l’infirmerait : {reverse}.',
  }),
  refusalCard: Object.freeze({
    tokenLabel: 'refus typé',
    retry: 'Cette demande mérite d’être réessayée.',
    absenceNote: 'Voici ce que ce service contient, et ne contient pas. Ce n’est pas un élément de preuve de l’inexistence de l’acte ou de la règle de droit.',
    absenceHeading: 'Ce qui permettrait de répondre',
    routes: Object.freeze({
      corrected_identifier: 'un identifiant corrigé, si vous pensiez à un autre acte',
      new_official_observation: 'une nouvelle observation, si l’éditeur publie ce texte',
      expanded_official_scope: 'une extension du corpus examiné à cette catégorie d’actes',
    }),
    notes: Object.freeze({
      anchor_not_in_version: 'Lex ne se rabat pas sur une recherche en texte intégral pour une disposition d’un acte connu. Une autre disposition n’est pas une réponse approchée.',
      ambiguous_version: 'L’éditeur ne classe aucune des deux versions. Il n’y a ni choix par défaut ni choix mémorisé.',
      profiles_differ: 'Ce refus ne peut pas être contourné. Les deux versions ont été extraites selon des profils différents : une différence entre elles ferait passer un désaccord entre analyseurs pour une modification législative.',
    }),
    nullSentences: Object.freeze({
      nearest_earlier: 'Aucune version antérieure n’est détenue : la date demandée précède cet historique.',
      nearest_later: 'Aucune version postérieure n’est détenue : la date demandée suit toutes les versions détenues.',
    }),
    europeNullSentences: Object.freeze({
      nearest_earlier: 'Aucun libellé antérieur n’est détenu : la date demandée précède cet historique.',
      nearest_later: 'Aucun libellé postérieur n’est détenu : la date demandée suit tous les libellés détenus.',
    }),
    candidate: 'applicable à partir du {validFrom}, empreinte {hash}, {published}',
    candidateWithdrawalNotStated: 'applicable à partir du {validFrom}, empreinte {hash}, {published}, retrait non indiqué par la plateforme',
    published: 'publiée le {date}',
    publicationNotStated: 'date de publication non indiquée par la plateforme',
    europeCandidate: 'libellé du {validFrom}, empreinte {hash}, {published}',
    europeCandidateWithdrawalNotStated: 'libellé du {validFrom}, empreinte {hash}, {published}, retrait non indiqué par la plateforme',
    europePublished: 'publié le {date}',
    europeNotes: Object.freeze({
      ambiguous_version: 'L’éditeur ne classe aucun des deux libellés. Il n’y a ni choix par défaut ni choix mémorisé.',
    }),
    publishedAct: 'publié le {date}',
    candidateEntryIntoForce: 'l’acte tel que publié, daté de son entrée en vigueur le {validFrom} (éditeur), empreinte {hash}, {published}',
    candidateEntryIntoForceWithdrawalNotStated: 'l’acte tel que publié, daté de son entrée en vigueur le {validFrom} (éditeur), empreinte {hash}, {published}, retrait non indiqué par la plateforme',
    candidateUnexpressed: '{version}, empreinte {hash} : le catalogue de l’éditeur n’en nomme aucune expression ; ce site n’en détient donc aucun texte et ne propose aucun lien vers cette version',
    reasons: Object.freeze({
      before_history: 'Le catalogue de l’éditeur, tel que ce site l’a lu au plus tard le {read}, ne recense aucune version de cet acte avant le {begins}.',
      catalogue_hole: 'Le catalogue de l’éditeur, tel que ce site l’a lu au plus tard le {read}, ne recense aucune version de cet acte du {from}, fin que l’éditeur indique pour la version précédente, au {to}, début de la version suivante ; ce site ne comble pas cet intervalle avec une autre version.',
      after_closed_end: 'Dans le catalogue de l’éditeur, tel que ce site l’a lu au plus tard le {read}, la version dont la fin est la plus tardive est applicable jusqu’à la veille du {closed} (éditeur), et aucune version ne commence le {closed} ni après : c’est là que s’arrêtent les versions du catalogue, ce qui ne dit rien de la fin éventuelle de la règle de droit.',
      publisher_end_convention_unclear: 'Le catalogue de l’éditeur indique le {date}, veille du début de la version suivante, comme fin de la version précédente : on ne sait donc pas si ce jour appartient à cette version, et ce site ne l’attribue à aucune des deux.',
      publisher_interval_invalid: 'Le catalogue de l’éditeur donne à la version qui s’appliquerait à cette date une fin antérieure ou égale à son propre début : ce site ne lit donc aucun intervalle pour elle et ne communique aucune version pour cette date ; le lien propre de cette version l’ouvre toujours.',
      no_expression_in_language: 'Une version française de cet acte couvre cette date, et ce site n’en détient aucune expression en {language}.',
      outside_named_version: 'La version que désigne cet identifiant ne couvre pas le {date} dans le catalogue de l’éditeur, tel que ce site l’a lu au plus tard le {read} : une autre version couvre cette date, et ce site ne l’ouvre pas à la place de la version désignée.',
    }),
    baseActEndOfForce: 'Les données de l’éditeur indiquent le {dates} comme date de fin de vigueur de l’acte (dateNoLongerInForce).',
    versions: Object.freeze({
      as_published: 'l’acte tel que publié, applicable à partir du {date} (éditeur)',
      as_published_entry_into_force: 'l’acte tel que publié, daté de son entrée en vigueur le {date} (éditeur)',
      consolidation: 'la version consolidée applicable à partir du {date} (éditeur)',
      code: 'la version du code applicable à partir du {date} (éditeur)',
    }),
    textNotHeld: Object.freeze({
      withheld_rights: 'ses droits n’ont pas permis d’admettre son texte',
      not_available_pdf: 'le fichier de l’éditeur est un PDF, que ce site ne lit pas',
      not_available_derivation: 'son texte n’a pas pu être lu dans le fichier de l’éditeur',
      not_available_misfiled: 'le fichier de l’éditeur qui lui est associé est celui d’une autre version',
      not_acquired: 'ce site n’a pas acquis cette version',
    }),
    textless: 'La version qui couvre cette date, {version}, figure dans le catalogue de l’éditeur, et ce site n’en détient aucun texte : {why}. Aucune autre version n’est présentée à sa place.',
    textlessNamed: 'Cette version, {version}, figure dans le catalogue de l’éditeur, et ce site n’en détient aucun texte : {why}.',
  }),
  coverageAnswer: Object.freeze({
    headings: Object.freeze({
      about: 'L’objet de cette page',
      counted: 'Comment ces décomptes sont établis',
      holds: 'Ce que ce montage contient',
      recorded: 'Ce que le corpus a consigné pour ses membres',
      asked: 'Ce qui peut être demandé à ce montage',
      history: 'Ce que ce montage conserve de son historique',
      measured: 'Ce à quoi ce montage a mesuré pouvoir répondre',
      notHeld: 'Ce que ce montage ne contient pas',
    }),
    facts: Object.freeze({
      publisher: 'éditeur',
      corpus: 'corpus',
      index: 'index',
      registry: 'registre des opérations',
      works: 'actes',
      states: 'versions',
      articles: 'articles',
      members: 'membres',
      answered: 'servies',
      notRouted: 'enregistrées, sans route sur ce montage',
      snapshotsInLog: 'constructions consignées dans le journal monté',
      historyBegins: 'première construction consignée',
      retentionPolicy: 'règle de conservation',
      nightlyDays: 'jours de conservation de la dernière construction de chaque jour',
      retentionEvaluatedAt: 'conservation décidée au',
      snapshotsWithoutText: 'constructions nommées par le journal dont ce montage ne détient pas le texte',
    }),
    languagesHeld: 'Langues détenues : {languages}',
    captions: Object.freeze({
      languages: 'Actes, versions et articles détenus, par langue',
      outcomes: 'Membres selon le résultat consigné par le corpus',
      gaps: 'Codes de lacune consignés par le corpus, comptés par membre',
      articleOutcomes: 'Résultats de contenu juridique consignés par le corpus, par classement',
      capabilities: 'Capacités mesurées, par opération, colonne, champ, langue et période',
      notServedData: 'Les données qui serviraient chaque opération sans route',
      snapshots: 'Instantanés dont ce montage détient le texte',
    }),
    scrollable: '{caption}, zone défilante',
    columns: Object.freeze({
      language: 'langue',
      works: 'actes',
      states: 'versions',
      articles: 'articles',
      searchableTextHeld: 'texte interrogeable détenu',
      articlesWithSearchableText: 'articles avec texte interrogeable',
      articlesWithoutPublisherDate: 'articles sans date de l’éditeur',
      firstState: 'première version',
      lastState: 'dernière version',
      outcome: 'résultat',
      members: 'membres',
      gap: 'lacune',
      disposition: 'classement',
      outcomes: 'résultats',
      operation: 'opération',
      column: 'colonne',
      field: 'champ',
      from: 'du',
      to: 'au',
      population: 'population',
      dataNeeded: 'données qui la serviraient',
      snapshot: 'instantané',
      observation: 'observation',
      builtAt: 'construit le',
      keptAs: 'conservé au titre de',
    }),
    held: Object.freeze({ true: 'oui', false: 'non' }),
    none: 'aucune',
    countsProvenance: 'Les décomptes du corpus et de l’index ci-dessous proviennent des artefacts désignés plus haut. Rien ici n’indique leur actualité : les dates de construction de la section historique disent quand la construction qui a acquis chaque corpus a lu son acquisition, ce qui borne le moment où ce corpus a été observé sans dater aucun décompte. Les empreintes désignent exactement les artefacts comptés, ce qu’une date ne ferait pas. Les dates calendaires des tableaux des langues et des capacités sont celles de l’éditeur, relatives au droit, et non au moment du décompte.',
    noLanguageRows: 'Ce montage ne contient aucune version dans aucune langue ; les totaux ci-dessus n’ont donc pas de ventilation par langue.',
    stateRange: 'Ce sont les première et dernière dates d’applicabilité données par l’éditeur, et non un relevé de la date de collecte. L’éditeur date des versions à l’avance, de sorte que la dernière peut se situer dans l’avenir ; ce montage ne détient aucune date du jour à laquelle les comparer et ne fait aucune comparaison de ce genre.',
    noArticleOutcomes: 'Aucun résultat de contenu juridique n’est compté ici ; cette page ne peut donc pas dire ce que le corpus a consigné pour les articles des documents acquis.',
    noGapTokens: 'Aucun code de lacune n’est compté ici ; là où un membre ci-dessus a consigné une lacune, cette page ne peut donc pas dire laquelle.',
    noRetentionPolicy: 'aucune ; ce montage ne conserve donc aucune construction antérieure à côté de la sienne',
    noBuildsRecorded: 'Le journal monté ne consigne aucune construction ; cette page ne peut donc nommer aucun instantané, ni aucune construction antérieure que ce montage conserverait.',
    capabilityAbsentAll: 'Ce montage n’a mesuré aucune capacité ; rien ici n’indique donc ce qui peut lui être demandé pour une période donnée.',
    capabilityAbsentLanguage: 'Aucune capacité n’est mesurée pour {language}. Cela ne dit rien des autres langues que contient ce montage : une réponse restreinte ne porte que les capacités de la langue demandée.',
    gaps: 'Membres ayant consigné une lacune : {withGaps} sur {members}.',
    served: 'Opérations enregistrées servies ici : {served} sur {registered}.',
    narrowed: 'Cette réponse a été restreinte à {language} lors de la demande. Seules les lignes par langue et les capacités mesurées sont celles de cette langue. Les totaux, les membres ci-dessous et tout le reste de cette page concernent l’ensemble du montage.',
    unserved: 'L’index a mesuré une capacité pour {operations}, que ce montage ne sert pas. Ce que l’index a mesuré et ce que le montage sert sont deux faits distincts, et ici ils divergent.',
    notHeldRow: '{item} : {reason}',
  }),
  shell: Object.freeze({
    title: '{title} - Lex',
    localeNav: 'Langue de l’interface',
  }),
  site: Object.freeze({
    brand: 'Lex',
    tagline: 'Le droit luxembourgeois et de l’UE que ce site détient, à la date de votre choix',
    skip: 'Aller au contenu',
    menu: 'Menu principal',
    search: 'Recherche',
    reading: 'Lecture',
    dossier: 'Dossier',
    history: 'Historique',
    compare: 'Comparaison',
    radar: 'Modifications',
    export: 'Export',
    coverage: 'Ce que ce site détient',
    notAdvice: 'Lex montre le texte de la loi. Il ne dit pas ce qui s’applique à votre situation, et ce n’est pas un conseil juridique.',
    authenticity: 'Les textes consolidés n’ont aucun effet juridique : seuls font foi les textes publiés au Journal officiel du Grand-Duché de Luxembourg (Mémorial A) et au Journal officiel de l’Union européenne.',
    sources: 'Les textes proviennent de Legilux et de l’Office des publications de l’Union européenne, tels que ce site les détient ; chaque article cité indique sa source officielle.',
  }),
  breadcrumbs: Object.freeze({
    label: 'Fil d’Ariane',
    publishers: Object.freeze({
      'lu-legilux': 'Droit luxembourgeois',
      'eu-eurlex': 'Droit de l’UE',
    }),
    state: 'Version applicable à partir du {date}',
    stateEntryIntoForce: 'Acte tel que publié, daté de son entrée en vigueur le {date}',
    europeOriginal: 'Libellé original du {date}',
    europeConsolidated: 'Libellé consolidé du {date}',
  }),
  freshness: Object.freeze({
    heading: 'Mise à jour des données',
    asBuilt: 'Chaque réponse du serveur indique quand l’index a été construit, quand les textes de chaque éditeur ont été collectés pour la dernière fois, ainsi que le code qui a construit l’index et celui qui le sert ; cette page affiche ces indications dès qu’elle a reçu une réponse.',
    noAnswer: 'Cette page ne contient aucune réponse du serveur ; il n’y a donc rien à afficher ici. Sur une page qui en contient une, cette partie indique, d’après cette réponse, quand l’index a été construit, quand les textes de chaque éditeur ont été collectés pour la dernière fois, ainsi que le code qui a construit l’index et celui qui le sert.',
    built: 'Index construit le {date}.',
    builtUndated: 'L’index ne consigne aucune date de construction.',
    channels: Object.freeze({
      'lu-legilux': 'Droit luxembourgeois, provenant de Legilux',
      'eu-eurlex': 'Droit de l’UE, provenant de l’Office des publications de l’Union européenne',
    }),
    collected: Object.freeze({
      one: '{channel} : la dernière collecte complète s’est achevée le {date}, {count} jour entier avant la réponse donnée par le serveur le {today} ; le décalage entre publication et collecte n’est pas encore mesuré.',
      other: '{channel} : la dernière collecte complète s’est achevée le {date}, {count} jours entiers avant la réponse donnée par le serveur le {today} ; le décalage entre publication et collecte n’est pas encore mesuré.',
    }),
    collectedUnstated: '{channel} : la date à laquelle sa dernière collecte complète s’est achevée n’est pas consignée avec cet index ; le décalage entre publication et collecte n’est pas encore mesuré.',
    indexCommit: 'Révision du code ayant construit l’index',
    serviceCommit: 'Révision du code du service en fonctionnement',
    commitUnstated: 'non consignée',
    staleHeading: 'Certains textes peuvent ne pas être à jour',
    stale: Object.freeze({
      one: '{channel} : sa dernière collecte complète s’est achevée le {date}, plus de {count} jour avant la réponse donnée par le serveur le {today}. L’éditeur peut avoir publié des modifications depuis ; consultez son site officiel.',
      other: '{channel} : sa dernière collecte complète s’est achevée le {date}, plus de {count} jours avant la réponse donnée par le serveur le {today}. L’éditeur peut avoir publié des modifications depuis ; consultez son site officiel.',
    }),
  }),
  home: Object.freeze({
    title: 'Le droit luxembourgeois et de l’UE à la date de votre choix',
    eyebrow: 'Rechercher dans le droit',
    heading: 'Le droit luxembourgeois et de l’UE à la date de votre choix',
    intro: 'Recherchez dans les textes que ce site détient, ouvrez un acte tel qu’il se présentait à la date de votre choix, voyez comment chaque article a changé, comparez deux dates et exportez avec des citations que chacun peut vérifier auprès de la source officielle. Lorsque ce site ne détient pas un texte ou une date, il le dit.',
    aboutHeading: 'Ce qu’est Lex et ce qu’il n’est pas',
    coverage: 'Ce site détient {acts} du droit luxembourgeois, en {versions} comptant {articles}, d’après l’index construit le {date}.',
    coverageUndated: 'Ce site détient {acts} du droit luxembourgeois, en {versions} comptant {articles} ; l’index qu’il détient ne consigne aucune date de construction.',
    coverageOlder: 'Ce site détient {acts} du droit luxembourgeois, en {versions} comptant {articles}, d’après l’index construit le {date} ; au Luxembourg, nous sommes aujourd’hui le {today}.',
    acts: Object.freeze({
      one: '{count} acte',
      other: '{count} actes',
    }),
    versions: Object.freeze({
      one: '{count} version',
      other: '{count} versions',
    }),
    articles: Object.freeze({
      one: '{count} article',
      other: '{count} articles',
    }),
    links: 'Ailleurs sur ce site',
    examplesHeading: 'Exemples de recherche',
    examplesIntro: 'Chacun recherche un article que ce site détient, cité comme on l’écrit d’habitude, et mène à son texte.',
  }),
  failure: Object.freeze({
    line: 'Lex n’a pas pu mener à bien cette demande. Ce n’est pas une réponse sur le droit.',
    retry: 'Réessayer',
    official: 'Les sites officiels des éditeurs :',
    sites: Object.freeze({
      legilux: 'Legilux, pour le droit luxembourgeois',
      eurlex: 'EUR-Lex, pour le droit de l’UE',
    }),
  }),
  notFound: Object.freeze({
    title: 'Page introuvable',
    eyebrow: 'Page introuvable',
    heading: 'Il n’y a aucune page à cette adresse',
    intro: 'Vérifiez l’adresse ou recherchez ci-dessous dans les textes que ce site détient. Une page absente ici ne dit rien du droit, seulement des pages de ce site.',
    home: 'Aller à la page d’accueil',
  }),
  maintenance: Object.freeze({
    title: 'Maintenance',
    eyebrow: 'Maintenance',
    heading: 'Lex est en cours de mise à jour',
    intro: 'Ce site est en cours de mise à jour et ne peut pas répondre pour le moment. Cette page n’est pas une réponse sur le droit. Réessayez dans quelques instants.',
  }),
  legal: Object.freeze({
    menu: 'Informations légales',
    updated: 'Dernière mise à jour : {date}.',
    inline: Object.freeze({
      legalNotice: 'mentions légales',
      privacy: 'page sur la protection des données',
      terms: 'conditions d’utilisation',
      accessibility: 'déclaration d’accessibilité',
      cookies: 'page sur les cookies',
    }),
  }),
  legalNotice: Object.freeze({
    title: 'Mentions légales',
    eyebrow: 'À propos de ce site',
    heading: 'Mentions légales',
    intro: 'Ce qu’est Lex, d’où viennent ses textes et sous quelles licences, et qui l’héberge.',
    sections: Object.freeze({
      what: Object.freeze({
        heading: 'Ce qu’est Lex',
        text: 'Lex est un outil indépendant et non commercial pour lire le droit luxembourgeois et le droit de l’UE au fil du temps : pour les textes et les dates qu’il détient, il montre un texte tel qu’il se présentait à la date de votre choix, et comment il a changé. Il est gratuit, ne comporte aucune publicité et ne demande la création d’aucun compte.',
        notAdvice: '{notAdvice}',
      }),
      status: Object.freeze({
        heading: 'Aucun statut officiel',
        text: 'Lex n’a aucun statut officiel. Il n’est un service ni de l’État luxembourgeois, ni du Service central de législation, qui publie Legilux, ni de l’Office des publications de l’Union européenne, et aucun d’eux ne l’approuve.',
        prevails: '{authenticity} Lorsqu’un texte que Lex montre diffère de sa publication officielle, c’est la publication officielle qui prévaut.',
      }),
      sources: Object.freeze({
        heading: 'Sources et licences',
        luxembourg: 'Les textes luxembourgeois proviennent de Legilux, publié par le Service central de législation. Lex ne montre le texte d’un fichier que lorsque les propres données de Legilux sur ce fichier lui attribuent la licence Creative Commons Attribution 4.0 International (CC BY 4.0) ; pour tout autre fichier, il montre le lien officiel et non le texte.',
        europe: 'Les textes de l’UE proviennent de l’Office des publications de l’Union européenne et portent, en anglais, sa mention de la source, « {eurlexAcknowledgement} ». Ils sont réutilisés comme la politique de réutilisation de l’Union européenne le permet (décision 2011/833/UE de la Commission relative à la réutilisation des documents de la Commission).',
        changes: 'Lex extrait et normalise les textes qu’il montre, et chaque article cité indique sa source officielle.',
      }),
      hosting: Object.freeze({
        heading: 'Hébergement',
        text: 'Le site est hébergé par Microsoft, sur son service en nuage Microsoft Azure, dans la région France Central, en France.',
      }),
    }),
  }),
  privacy: Object.freeze({
    title: 'Protection des données',
    eyebrow: 'À propos de ce site',
    heading: 'Protection des données',
    intro: 'Ce qu’il advient de vos données lorsque vous consultez Lex : il ne vous en demande aucune, ne dépose aucun cookie et ne conserve rien qui vous concerne.',
    sections: Object.freeze({
      reading: Object.freeze({
        heading: 'Lorsque vous consultez le site',
        request: 'Chaque page que vous ouvrez et chaque recherche que vous envoyez est une requête de votre navigateur au serveur de ce site. La requête comporte l’adresse IP de votre appareil, l’adresse de la page et les informations techniques que tout navigateur envoie. Les mots d’une recherche sont envoyés dans le corps de la requête, jamais dans l’adresse de la requête ; un lien qui conserve une recherche la contient après « # », dans une partie que votre navigateur n’envoie jamais.',
        host: 'Microsoft, qui héberge le site, achemine chaque requête jusqu’au serveur et sa réponse jusqu’à vous, et traite à cette fin les données de connexion nécessaires, selon ses propres conditions.',
        kept: 'Le serveur n’utilise la requête que pour y répondre, et n’en conserve rien une fois qu’il a répondu : il ne tient aucun journal des accès ni aucun relevé de ce qui est lu ou recherché, et l’hébergement est configuré sans stockage de journaux ni service de surveillance qui conserverait les requêtes. Cela est vérifié : le serveur est soumis à des requêtes portant des valeurs repérables (une expression recherchée, une adresse, le nom d’un navigateur), dont aucune ne doit apparaître dans ce qu’il écrit.',
        nothingHeld: 'Lex ne propose ni compte ni inscription : il ne sait donc jamais qui vous êtes. Comme il ne conserve rien d’une visite, il ne détient rien vous concernant qu’il pourrait montrer, rectifier ou effacer.',
      }),
      nothingElse: Object.freeze({
        heading: 'Ni cookie, ni traceur, ni autre site',
        cookies: 'Le site ne dépose aucun cookie et n’écrit rien dans le stockage de votre navigateur (voir la {cookiesPage}). Il ne charge rien depuis un autre site : ni mesure d’audience, ni publicité, ni police de caractères, script ou image provenant d’ailleurs.',
        links: 'Les liens vers les sites des éditeurs, comme Legilux et EUR-Lex, sont des liens ordinaires. Si vous en suivez un, votre navigateur s’adresse directement à ce site, selon la politique de protection des données propre à celui-ci ; Lex demande à votre navigateur de ne pas indiquer à ce site de quelle page vous venez.',
      }),
      named: Object.freeze({
        heading: 'Personnes nommées dans les actes publiés',
        why: 'Certains actes publiés nomment des personnes, par exemple une loi conférant la nationalité. Lex reproduit les textes tels que les journaux officiels les publient, le Journal officiel du Grand-Duché de Luxembourg et le Journal officiel de l’Union européenne, afin que le droit puisse être lu à une date et vérifié auprès de sa source officielle.',
        searchEngines: 'Les moteurs de recherche ne sont autorisés à indexer la page d’un acte qu’une fois qu’une vérification a constaté que l’acte ne nomme aucune personne privée ; la page de tout autre acte leur demande de ne pas l’indexer.',
      }),
      rights: Object.freeze({
        heading: 'Vos droits',
        complaint: 'Vous pouvez introduire une réclamation auprès de la Commission nationale pour la protection des données (CNPD), {cnpdSite}.',
      }),
    }),
  }),
  terms: Object.freeze({
    title: 'Conditions d’utilisation',
    eyebrow: 'À propos de ce site',
    heading: 'Conditions d’utilisation',
    intro: 'Comment Lex peut être utilisé, sous quelles licences ses textes sont montrés, ce qu’il ne promet pas et comment citer les textes.',
    sections: Object.freeze({
      what: Object.freeze({
        heading: 'Ce qu’est Lex',
        notAdvice: '{notAdvice}',
        service: 'C’est un service d’information gratuit : il montre les textes qu’il détient, aux dates auxquelles il les détient, et le dit lorsqu’il ne détient pas de texte ou pas de date. {authenticity}',
      }),
      use: Object.freeze({
        heading: 'Utilisation de Lex',
        text: 'Chacun est libre de lire les textes que Lex montre, d’y faire des recherches, de les citer et de les exporter, gratuitement et sans compte.',
      }),
      texts: Object.freeze({
        heading: 'Les textes et leurs licences',
        licences: 'Chaque texte conserve la licence de sa source : la licence Creative Commons Attribution 4.0 International (CC BY 4.0) pour les textes luxembourgeois que Lex montre, et la politique de réutilisation de l’Union européenne (décision 2011/833/UE de la Commission) pour les textes de l’UE. Les {legalNoticePage} indiquent d’où viennent les textes et la mention de la source que chacun porte. Les présentes conditions n’ajoutent aucune restriction à la réutilisation des textes que leurs propres licences permettent.',
      }),
      warranty: Object.freeze({
        heading: 'Absence de garantie',
        text: 'Lex est proposé tel quel. Il ne garantit pas que les textes soient complets, à jour ou exempts d’erreurs, ni que le site soit toujours accessible. Vérifiez le texte officiel avant de vous y fier. Rien dans les présentes conditions n’exclut ni ne limite une responsabilité que la loi ne permet pas d’exclure ou de limiter.',
      }),
      citing: Object.freeze({
        heading: 'Comment citer',
        text: 'Citez le texte officiel, par sa référence officielle. Lex donne, pour chaque article qu’il cite, une citation et un permalien vers le texte montré, afin que chacun puisse vérifier le texte cité auprès de sa source officielle.',
      }),
    }),
  }),
  accessibility: Object.freeze({
    title: 'Déclaration d’accessibilité',
    eyebrow: 'À propos de ce site',
    heading: 'Déclaration d’accessibilité',
    intro: 'Le degré d’accessibilité de Lex, ce qui a été fait et ce qui n’a pas encore été vérifié.',
    sections: Object.freeze({
      status: Object.freeze({
        heading: 'État de conformité',
        text: 'Lex est conçu pour respecter les Règles pour l’accessibilité des contenus Web (WCAG) 2.2 au niveau AA. Il n’a pas encore été audité et ne prétend donc pas les respecter : tant qu’un audit du site en service n’a pas été réalisé, il est non conforme, comme est déclaré au Luxembourg le site d’un organisme du secteur public qui n’a fait l’objet d’aucun audit.',
      }),
      done: Object.freeze({
        heading: 'Ce qui a été fait',
        list: Object.freeze({
          language: 'Chaque page indique sa langue, et chaque texte de loi cité est marqué de la langue dans laquelle il est rédigé.',
          skip: 'Un lien en haut de chaque page mène directement à son contenu.',
          server: 'Les pages d’un acte et de son texte arrivent complètes du serveur et se lisent sans script.',
          dates: 'Les dates sont toujours des dates du calendrier, jamais des dates relatives.',
          colour: 'Aucune information n’est donnée par la seule couleur : les mots trouvés par une recherche sont en gras en plus d’être surlignés.',
          whole: 'Une comparaison est une seule liste, lue dans l’ordre, et les tableaux et listes de conditions d’un texte de loi sont toujours montrés en entier.',
          keyboard: 'Chaque formulaire s’utilise au clavier seul, et chaque commande montre quand elle a le focus : des tests automatisés le vérifient dans un navigateur à chaque modification du code du site.',
          announced: 'Une réponse est annoncée aux lecteurs d’écran à son arrivée.',
        }),
      }),
      limits: Object.freeze({
        heading: 'Ce qui n’a pas encore été vérifié',
        list: Object.freeze({
          audit: 'Aucun audit par un expert en accessibilité, et aucun test avec un lecteur d’écran tel que NVDA ou VoiceOver, n’a encore été réalisé.',
          reflow: 'L’adaptation des pages aux écrans étroits et aux forts agrandissements n’a pas été vérifiée au regard des règles.',
          pdf: 'Les fichiers exportés en PDF ne sont pas balisés, de sorte qu’un lecteur d’écran ne peut pas en suivre la structure ; l’export JSON contient le même texte.',
        }),
      }),
      law: Object.freeze({
        heading: 'La loi',
        text: 'Au Luxembourg, la {accessibilityLaw} fixe les règles d’accessibilité des organismes du secteur public. Lex n’est pas édité par un organisme du secteur public et publie cette déclaration de sa propre initiative.',
      }),
      prepared: Object.freeze({
        heading: 'Établissement de cette déclaration',
        text: 'Cette déclaration a été établie à partir du code du site et de ses tests automatisés. Aucun audit externe n’a été réalisé.',
      }),
    }),
  }),
  cookies: Object.freeze({
    title: 'Cookies',
    eyebrow: 'À propos de ce site',
    heading: 'Cookies',
    intro: 'Lex ne dépose aucun cookie.',
    sections: Object.freeze({
      none: Object.freeze({
        heading: 'Aucun cookie, aucun stockage dans le navigateur',
        text: 'Ce site ne dépose aucun cookie, ni le sien ni celui d’un tiers, et n’écrit rien dans le stockage de votre navigateur. Il ne charge rien depuis d’autres sites, si bien qu’aucun autre site ne peut en déposer par son intermédiaire. C’est pourquoi il ne vous demande aucun consentement.',
      }),
      checked: Object.freeze({
        heading: 'Comment cela est vérifié',
        text: 'Des tests automatisés échouent si une page du site dépose un cookie, écrit dans le stockage du navigateur ou demande quoi que ce soit à un autre site, et, avant chaque mise en production, il est vérifié qu’aucune réponse du nouveau déploiement ne dépose de cookie.',
      }),
    }),
  }),
  reference: Object.freeze({
    menu: 'Aide et documentation',
    contents: 'Sur cette page',
    inline: Object.freeze({
      guide: 'Comment lire Lex',
      developers: 'page Développeurs',
    }),
    info: 'Comment lire ceci',
    infoNamed: 'Comment lire ceci : {section}',
  }),
  help: Object.freeze({
    title: 'Obtenir de l’aide',
    eyebrow: 'Aide',
    heading: 'Obtenir de l’aide',
    intro: 'Ce que Lex peut faire pour vous et ce qu’il ne peut pas faire, comment lire sa réponse lorsqu’il ne peut pas donner celle que vous avez demandée, et où se trouvent les textes officiels.',
    sections: Object.freeze({
      what: Object.freeze({
        heading: 'Ce qu’est Lex',
        text: 'Lex montre le droit luxembourgeois et le droit de l’UE qu’il détient tels que les éditeurs les ont publiés : un texte tel qu’il se présentait à la date de votre choix, comment chaque article d’un acte luxembourgeois a changé, et une citation que chacun peut vérifier auprès de la source officielle.',
        notAdvice: '{notAdvice}',
        adviser: 'Pour savoir ce qu’un texte signifie pour vous, ou ce que vous devriez faire, adressez-vous à un avocat ou à un autre conseiller de votre choix.',
      }),
      not: Object.freeze({
        heading: 'Ce que Lex ne fait pas',
        list: Object.freeze({
          apply: 'Il n’applique le droit à la situation de personne, ne calcule ni délai ni montant et ne dit pas qui a raison.',
          effect: 'Il ne dit pas ce qu’une modification change en pratique, ne prédit aucune décision et ne rédige aucun texte.',
          consolidation: 'Il n’établit lui-même aucune consolidation : tout texte consolidé qu’il montre est celui de l’éditeur.',
          caseLaw: 'Il ne détient aucune jurisprudence ni aucune décision de justice.',
          choose: 'Il ne choisit jamais à votre place entre deux versions lorsque les dates de l’éditeur laissent les deux possibles et que leurs textes diffèrent, et ne comble jamais un intervalle sans version dans le catalogue.',
          record: 'Il ne garde aucune trace de qui le lit ni de ce qui est recherché.',
        }),
      }),
      refusals: Object.freeze({
        heading: 'Lorsque Lex ne peut pas répondre',
        text: 'Lorsque Lex ne peut pas donner la réponse que vous avez demandée, il dit pourquoi, d’abord en une phrase simple : par exemple, qu’il ne détient aucune version de l’acte pour la date choisie, que deux versions couvrent cette date sans que l’éditeur dise laquelle s’applique, ou que la licence d’un fichier ne permet pas à Lex d’en montrer le texte. Il indique ensuite ce qui permettrait de répondre, comme les dates détenues les plus proches, et donne le lien officiel.',
        absence: 'Une telle réponse porte sur ce que Lex détient, jamais sur le droit : un texte absent de Lex peut exister chez l’éditeur.',
        failure: 'Lorsque le site lui-même connaît une défaillance, il le dit en d’autres termes : « {failureLine} » Réessayez ou consultez les sites officiels ci-dessous.',
        details: 'Après la phrase viennent le code du refus et ses détails, à l’intention des programmes ; la {developersPage} recense chaque code.',
      }),
      official: Object.freeze({
        heading: 'Où se trouvent les textes officiels',
        authenticity: '{authenticity}',
        check: 'Chaque article que Lex cite indique sa source officielle, pour qu’il puisse y être vérifié :',
        list: Object.freeze({
          legilux: '{legiluxSite}, pour le droit luxembourgeois : le Journal officiel du Grand-Duché de Luxembourg et les textes consolidés',
          eurlex: '{eurlexSite}, pour le droit de l’UE : le Journal officiel de l’Union européenne',
        }),
      }),
      advice: Object.freeze({
        heading: 'Qui peut vous conseiller',
        text: 'Lex n’indique aucun conseiller : une liste, par matière, de ceux qui peuvent vous conseiller doit d’abord être vérifiée par une personne, et ne l’a pas encore été.',
      }),
      contact: Object.freeze({
        heading: 'Aucun contact',
        text: 'Lex ne propose aucun moyen de contact : ni adresse électronique, ni formulaire, ni numéro de téléphone, ni adresse postale, et il ne nomme personne. Personne ne peut être joint par l’intermédiaire de Lex, que ce soit pour une question sur le droit ou sur le site, ou pour signaler un problème ; Lex ne reçoit donc aucun message et n’en conserve aucun.',
        complaint: 'Pour introduire une réclamation concernant la manière dont ce site traite des données personnelles, la {privacyPage} indique l’autorité à saisir.',
      }),
      guide: Object.freeze({
        heading: 'Lire Lex',
        text: 'La page {guidePage} explique les dates que Lex affiche, ce qu’est une version et les termes qu’il emploie, avec un glossaire.',
      }),
    }),
  }),
  guide: Object.freeze({
    title: 'Comment lire Lex',
    eyebrow: 'Guide',
    heading: 'Comment lire Lex',
    intro: 'Ce que signifient les dates que Lex affiche, ce qu’est une version, comment Lex dit ce qu’il détient et ce qu’il ne détient pas, et les termes qu’il emploie, avec un glossaire.',
    sections: Object.freeze({
      clocks: Object.freeze({
        heading: 'Deux sortes de dates',
        text: 'Lex distingue deux sortes de dates et ne les confond jamais, ni dans ce qu’il écrit ni dans ce qu’entend un lecteur d’écran.',
        luxembourg: 'La première sorte dit, d’après les dates de l’éditeur, quel texte sert de réponse pour une date. Une version luxembourgeoise est « applicable à partir du » jour que Legilux lui attribue, jusqu’à la veille de la date de fin que Legilux lui donne ou, à défaut, jusqu’à la veille du début de la version suivante ; « (éditeur) » après une date indique qu’il s’agit de la date de l’éditeur, jamais d’une date que Lex aurait calculée.',
        europe: 'Pour un acte de l’UE, Lex montre des libellés : le libellé original, qui porte la date de l’acte dans le paquet Formex de l’éditeur, et chaque libellé consolidé, qui porte la date de consolidation de l’éditeur. Un libellé sert de réponse pour les dates allant de la sienne à la veille de celle du libellé suivant, et Lex n’en dit pas davantage : un libellé consolidé est un outil de documentation, non une déclaration selon laquelle l’acte liait qui que ce soit à cette date.',
        second: 'La seconde sorte dit quand un texte a été publié, et quand Lex l’a collecté. Une date de publication est celle de l’éditeur ; la date de construction de l’index et la dernière collecte complète de chaque éditeur, au bas d’une page qui contient une réponse du serveur, sont celles de Lex. Ni l’une ni l’autre ne dit à partir de quand un texte est applicable.',
        never: 'Lex ne dit rien de plus d’une période que ce qu’en disent les dates de l’éditeur : il n’affirme jamais qu’un texte liait qui que ce soit d’une date à une autre.',
      }),
      roles: Object.freeze({
        heading: 'Chaque date avec ce qu’elle est',
        text: 'Les données de l’éditeur peuvent attribuer plusieurs dates à un acte : son entrée en vigueur, sa mise en application, la fin de sa validité, une date limite, sa signature. Lex affiche chacune comme une valeur distincte, sous son propre nom, et n’en présente jamais une comme une autre.',
        entryIntoForce: 'Lorsque le fichier d’un acte tel que publié à l’origine n’indique aucune date à partir de laquelle il est applicable, Lex le date de son entrée en vigueur selon les données de l’éditeur et le signale par la mention « {noDateMark} ».',
        calendar: 'Les dates sont toujours des dates du calendrier, jamais des dates relatives comme « hier ».',
      }),
      versions: Object.freeze({
        heading: 'Tel que publié, et consolidé',
        published: 'Un acte tel que publié est l’acte tel que le journal officiel l’a publié. Une version consolidée est un texte établi par l’éditeur : celui de l’acte à une date donnée, avec les modifications que des actes ultérieurs y ont apportées.',
        authenticity: '{authenticity}',
        asPublished: 'Lorsque Lex ne détient un acte luxembourgeois que tel que publié à l’origine, il l’indique par la mention « {asPublishedLabel} » : des actes ultérieurs peuvent l’avoir modifié, et Lex ne détient aucune version intégrant ces modifications.',
        languages: 'Lorsque l’éditeur a publié un acte en français et dans une autre langue, Lex montre chacune des versions linguistiques de l’éditeur qu’il détient, et le signale lorsque les données de l’éditeur n’indiquent pas laquelle fait foi.',
      }),
      textless: Object.freeze({
        heading: 'Versions dont Lex ne détient pas les textes',
        text: 'Lex peut savoir, par le catalogue de l’éditeur, qu’une version existe sans en détenir le texte : son fichier peut être un PDF, que Lex ne lit pas ; sa licence peut ne pas permettre à Lex de le montrer ; ou Lex peut ne pas l’avoir collecté. Lex nomme alors la version et dit pourquoi il n’en détient pas le texte. Il ne montre jamais le texte d’une autre version à sa place.',
        article: 'Un article détenu sans texte est nommé à part, sous la mention « {withoutText} », et n’est jamais cité, interrogé ni exporté comme texte.',
        annexes: 'Une annexe d’un acte de l’UE que l’éditeur ne fournit que sous forme d’images n’est jamais communiquée sous forme de texte, interrogée, citée ni exportée ; Lex en donne la source officielle.',
      }),
      gaps: Object.freeze({
        heading: 'Intervalles sans version',
        text: 'Lorsque le catalogue de l’éditeur ne recense aucune version pour certaines dates entre deux versions, Lex montre cet intervalle sans version tel qu’il est : il n’étend pas la version qui le précède à ces dates, et un libellé inchangé de part et d’autre de l’intervalle ne dit rien de ces dates.',
        law: 'Un intervalle sans version dans le catalogue n’est pas un vide juridique, et un texte absent de Lex n’est absent que de ce que Lex détient : lorsque Lex ne détient aucun texte pour une date, ce n’est pas un élément de preuve de l’inexistence de l’acte ou de la règle de droit.',
      }),
      ambiguity: Object.freeze({
        heading: 'Deux versions pour une même date',
        text: 'Les dates de l’éditeur peuvent faire que deux versions couvrent la même date sans qu’il dise laquelle s’applique. Lorsque leurs textes diffèrent, ou que Lex ne détient pas le texte de chacune, il n’en choisit aucune : il montre les deux, chacune avec ses dates et son lien, et c’est vous qui choisissez celle à lire. Lorsqu’il détient le texte de chacune et que c’est le même, il peut montrer ce texte une seule fois, comme celui de l’acte tel que publié ou de la version dont le début est le plus tardif.',
      }),
      freshness: Object.freeze({
        heading: 'Dans quelle mesure Lex est à jour',
        text: 'Au bas de chaque page, dans la partie qu’un lecteur d’écran annonce sous le nom « {freshnessHeading} », une page qui contient une réponse du serveur indique quand l’index sur lequel Lex se fonde a été construit et, pour chaque éditeur, quand Lex a terminé pour la dernière fois la collecte de ses textes. Un texte que l’éditeur a publié après cette collecte n’est pas encore détenu par Lex.',
        stale: 'Lorsque la dernière collecte complète d’un éditeur est plus ancienne que ce que Lex admet, un bandeau le signale sous l’en-tête de chaque page qui contient une réponse du serveur : « {staleHeading} ». L’éditeur peut avoir publié des modifications depuis ; consultez son site officiel.',
        lag: 'Tant que Lex n’a pas mesuré le temps que mettent les nouveaux textes d’un éditeur à lui parvenir, la ligne de chaque éditeur indique que le décalage entre publication et collecte n’est pas encore mesuré.',
      }),
      links: Object.freeze({
        heading: 'Permaliens et citations',
        permalink: 'Un permalien désigne une version une fois pour toutes : son adresse comporte l’éditeur, l’acte, la date de la version et l’empreinte de son texte, la langue et l’article ; pour un acte de l’UE, il désigne de la même manière un libellé. Il affiche ce texte, et indique au-dessus lorsqu’une version plus récente est applicable ou qu’un libellé ultérieur est détenu. Si Lex communique un autre texte pour cette date, ou ne détient plus celui-ci, il le dit d’abord, et affiche le texte qu’il communique ou renvoie à celui qu’il détient.',
        readable: 'L’adresse lisible d’un acte, sans date ni empreinte, suit l’acte : elle ouvre la version, ou pour un acte de l’UE le libellé, qui sert de réponse pour la date du jour au Luxembourg.',
        cite: 'Sous un article cité, « {cite} » le donne sous trois formes : « {conventional} », telle qu’on l’écrit d’ordinaire ; « {withPermalink} », la même suivie du permalien ; et « {evidence} », avec les identifiants, les dates et l’empreinte qui permettent à chacun de vérifier le texte cité. Dans un document, citez le texte officiel par sa référence officielle.',
        digest: 'Une empreinte est calculée sur un texte exact et l’identifie : si un seul caractère change, l’empreinte change. Lex en calcule une pour chaque texte qu’il cite, afin que le texte cité puisse être vérifié caractère par caractère.',
      }),
      keyboard: Object.freeze({
        heading: 'Au clavier',
        text: 'Lex n’a aucun raccourci clavier qui lui soit propre : aucune touche ne fait rien que vous n’ayez demandé. Tab passe au lien, au champ ou au bouton suivant, et Maj et Tab au précédent ; Entrée suit un lien ou envoie un formulaire.',
        skip: 'Le premier lien de chaque page, « {skip} », mène directement au contenu, après les menus.',
      }),
    }),
    glossary: Object.freeze({
      heading: 'Glossaire',
      intro: 'Les termes que Lex emploie, tels que ses pages les affichent, et ce que chacun signifie.',
      terms: Object.freeze({
        appliesFrom: 'Dans le tableau des versions d’un dossier : la date à partir de laquelle l’éditeur indique qu’une version luxembourgeoise est applicable. C’est la date de Legilux, jamais une date que Lex calcule.',
        versionApplicable: 'La manière dont un résultat de recherche, une lecture ou une citation désigne une version luxembourgeoise : par la date à partir de laquelle l’éditeur indique qu’elle est applicable, « (éditeur) » signalant que la date est celle de l’éditeur.',
        asPublished: 'Lex ne détient cet acte luxembourgeois que tel que le journal officiel l’a publié à l’origine : des actes ultérieurs peuvent l’avoir modifié, et Lex ne détient aucune version consolidée intégrant leurs modifications.',
        entryIntoForce: 'La date d’entrée en vigueur d’un acte selon les données de l’éditeur. C’est une date à part entière, distincte de la date à partir de laquelle une version est applicable, et Lex ne présente jamais l’une comme l’autre.',
        application: 'Pour un acte de l’UE : la date de mise en application de l’acte, ou d’une partie de l’acte, selon les données de l’éditeur.',
        endOfValidity: 'Pour un acte de l’UE : la date à laquelle, selon les données de l’éditeur, l’acte, ou une partie de l’acte, cesse de produire ses effets.',
        wordingDate: 'Pour un acte de l’UE : la date que porte le libellé affiché, celle de l’acte pour le libellé original et la date de consolidation de l’éditeur pour un libellé consolidé.',
        wordingChanged: 'Entre deux versions d’un acte luxembourgeois : le texte de l’article diffère de celui de la version précédente. Lex ne dit rien de ce que signifie la modification.',
        wordingUnchanged: 'Entre deux versions d’un acte luxembourgeois : le texte de l’article est le même que dans la version précédente.',
        catalogue: 'Dans un dossier : ce que dit le catalogue de l’éditeur entre les versions d’un acte, comme un intervalle sans version, deux versions pour une même date, ou une version dont Lex ne détient pas le texte.',
        withoutText: 'Se dit d’articles que Lex connaît par les données de l’éditeur, mais dont il ne détient pas le texte, ou n’est pas autorisé à le montrer. Ils sont listés à part, sans jamais être cités comme texte, et un export les consigne comme exclus, avec la raison.',
        notAdmitted: 'Dans un dossier : le nombre d’articles d’une version que Lex consigne sans les admettre comme texte qu’il cite ; la note sous le tableau, fournie par le serveur, indique ce que ce nombre recouvre.',
        absence: 'Sous un refus : ce qui en ferait une réponse, comme un identifiant corrigé, ou un texte que l’éditeur publierait plus tard.',
        permalink: 'Une adresse qui désigne une fois pour toutes une version, ou un libellé d’un acte de l’UE : l’éditeur, l’acte, la date et l’empreinte de cette version ou de ce libellé, la langue et l’article. Elle affiche ce texte ou, sinon, dit d’abord pourquoi.',
        usualCitation: 'La citation d’un article telle qu’on l’écrit d’ordinaire, avec la date de sa version, ou de son libellé pour un acte de l’UE, lorsque rien n’établit que ce texte est celui qui sert de réponse pour la date du jour.',
        withPermalink: 'La citation usuelle, suivie du permalien du texte cité.',
        evidenceForm: 'La citation avec ce qu’il faut pour la vérifier : l’identifiant de l’acte, ses dates, la référence au journal officiel lorsque Lex la détient, l’empreinte du texte cité et l’indication du texte qui fait foi.',
        textDigest: 'Une valeur calculée sur le texte exact d’un article, qui l’identifie : si un seul caractère change, l’empreinte change, ce qui permet à chacun de vérifier que le texte cité est celui que Lex a communiqué.',
        today: 'La date du jour au Luxembourg, à laquelle Lex lit un texte lorsque vous n’indiquez aucune date.',
        dossier: 'Ce que Lex détient pour un acte : les intitulés et les versions d’un acte luxembourgeois, ou les expressions d’un acte de l’UE, avec les dates de l’éditeur, et ce que Lex ne détient pas.',
        changes: 'La page qui liste les versions luxembourgeoises datées dans un intervalle de dates, chacune avec la version qu’elle suit, en indiquant si son libellé a changé.',
        coverage: 'La page qui dit ce que Lex détient et ce qui lui manque, tel que son index le compte, et comment se sont passés ses contrôles de qualité.',
        freshness: 'Au bas de chaque page qui contient une réponse du serveur : quand l’index a été construit, et quand les textes de chaque éditeur ont été collectés pour la dernière fois.',
        stale: 'Un bandeau affiché sous l’en-tête de chaque page qui contient une réponse du serveur lorsque les textes d’un éditeur ont été collectés pour la dernière fois il y a plus longtemps que ce que Lex admet.',
        indexCommit: 'La révision du code de Lex qui a construit l’index dont provient une réponse.',
        serviceCommit: 'La révision du code de Lex qui a fourni la réponse.',
      }),
    }),
  }),
  developers: Object.freeze({
    title: 'Développeurs',
    eyebrow: 'Développeurs',
    heading: 'L’API de Lex, pour les programmes et les agents d’IA',
    codeSeparator: ', ',
    intro: 'Les réponses que montrent les pages de ce site viennent d’une même API, et ces mêmes réponses sont ouvertes à tout programme, en REST et en MCP, à l’adresse même de ce site. L’usage de l’API est gratuit et anonyme : il n’y a ni clé, ni compte, ni formule payante. Cette page est générée à partir du registre des opérations et des schémas au regard desquels ce déploiement vérifie chaque requête et chaque réponse ; elle décrit donc ce que fait le serveur.',
    access: Object.freeze({
      heading: 'Deux accès, une seule réponse',
      rest: 'REST : {request}, avec un corps JSON qui nomme l’opération et ses paramètres, {body}. Le chemin n’accepte aucune chaîne de requête, et toute autre méthode ne reçoit qu’un échec sous l’enveloppe.',
      mcp: 'MCP : des messages JSON-RPC envoyés par POST à {endpoint}, par le transport Streamable HTTP, version du protocole {version}. Le serveur répond à {methods} ; chaque opération qu’il sert est un outil du même nom, dont le schéma d’entrée est le membre {parameters} du schéma de requête de l’opération.',
      same: 'Les deux donnent la même enveloppe pour la même requête : un appel d’outil est converti en document de requête REST et traité par le même code, et son résultat porte l’enveloppe comme contenu structuré. Un refus est une réponse, non une erreur : REST l’envoie comme une réponse HTTP réussie, et MCP comme un résultat d’outil avec {isError}.',
      restExample: 'Par exemple, le rapport de couverture, demandé avec curl :',
      mcpExample: 'La même requête en MCP, sous forme de message tools/call :',
      client: 'Un client qui prend en charge le transport Streamable HTTP reçoit l’adresse {endpoint} ; pour un client qui lit la liste de ses serveurs dans un fichier JSON, par exemple :',
    }),
    operations: Object.freeze({
      heading: 'Les opérations',
      summary: Object.freeze({
        one: 'Le registre recense {count} opération ; ce serveur en sert {served}. Chacune indique son chemin REST, son outil MCP, ses paramètres tels que son schéma de requête les énonce, les objets que porte sa réponse, et ses deux schémas.',
        other: 'Le registre recense {count} opérations ; ce serveur en sert {served}. Chacune indique son chemin REST, son outil MCP, ses paramètres tels que son schéma de requête les énonce, les objets que porte sa réponse, et ses deux schémas.',
      }),
      europe: 'Pour un identifiant de l’UE, certaines opérations servies refusent avec {code}, en nommant les modes disponibles ; lorsqu’un index de l’UE est monté, le membre {member} de la réponse de couverture les recense.',
      rest: 'REST',
      tool: 'Outil MCP',
      notATool: 'aucun, puisque ce serveur ne sert pas cette opération',
      parameters: 'Paramètres',
      noParameters: 'aucun',
      required: 'obligatoire',
      optional: 'facultatif',
      objects: 'La réponse porte',
      schemas: 'Schémas',
      requestSchema: 'requête',
      resultSchema: 'réponse',
      notServed: 'Opération non servie : une requête qui la vise reçoit l’échec de transport {code}, et la réponse de couverture nomme les données qui permettraient de la servir : {data}.',
      descriptions: Object.freeze({
        answer_drift: 'Les réponses datées passées qu’une révision ultérieure de l’éditeur a invalidées, retrouvées à partir du journal des événements.',
        article_history: 'Un article d’un acte luxembourgeois à travers les versions détenues : si son libellé a changé de l’une à la suivante, et quelles versions ne le contiennent pas.',
        as_observed: 'La version d’un acte luxembourgeois applicable à une date, telle qu’une construction du journal des événements la détenait, l’heure de cette construction étant une borne supérieure de son observation.',
        as_of: 'La version d’un acte luxembourgeois applicable à une date, avec ses articles ; pour un acte de l’UE, le libellé qui sert de réponse pour cette date.',
        ask: 'L’assistant, qui est désactivé : chaque question reçoit le résultat de présentation qui le dit, en nommant les opérations qui répondent à partir du droit que Lex détient. Aucun modèle ne rédige de réponse.',
        browse: 'Les actes luxembourgeois détenus, en une seule liste ordonnée et paginée, avec leurs identifiants, leurs langues, les dates de leurs versions et les types de documents de l’éditeur.',
        changes_in_period: 'Les versions luxembourgeoises dont l’applicabilité commence dans une période, pour un acte ou dans tout l’index.',
        citation: 'Les références que l’éditeur a écrites dans le texte des articles d’une version luxembourgeoise.',
        cited_by: 'Les références, dans toute version luxembourgeoise détenue, dont la cible est un acte donné.',
        classification: 'Les données typées de l’éditeur sur un acte luxembourgeois, telles que l’éditeur les énonce : type de document, valeur juridique, organe responsable, identifiants historiques, dates de publication et du document.',
        concepts: 'Les concepts que l’Office des publications attache aux actes de l’UE : descripteurs EuroVoc, codes du répertoire et matières.',
        coverage: 'Ce que ce serveur détient et sert, compté à partir de son index, et ce qu’il ne détient pas.',
        diff: 'La différence, article par article, entre les versions d’un acte luxembourgeois applicables à deux dates.',
        dossier: 'La fiche d’un acte telle que l’index la détient : les intitulés et les versions d’un acte luxembourgeois, ou les expressions d’un acte de l’UE.',
        events: 'Le journal des événements de ce que chaque construction de l’index a constaté, interrogé par curseur et délivré au moins une fois.',
        evidence_bundle: 'Ce qu’il faut pour citer une version, une page à la fois : le texte de chaque article avec ses empreintes, le permalien de la version, ses sources et ses identités officielles, selon la règle des droits ; pour un acte de l’UE, il en va de même du libellé qui sert de réponse pour la date.',
        in_force_on: 'Les actes luxembourgeois ayant une version applicable à une date.',
        knowable_on: 'Ce qu’un lecteur pouvait savoir à une date : la publication de chaque version à côté du moment où Lex l’a observée.',
        manifestation: 'Les manifestations, chez l’éditeur, des expressions d’un acte luxembourgeois, avec leurs formats et leurs fichiers, celle que Lex a conservée étant signalée avec son empreinte.',
        provenance: 'Les sources, les profils de règles et les empreintes sur lesquels repose une version luxembourgeoise.',
        relations: 'Les références d’un acte luxembourgeois dans les deux sens, en une seule liste ordonnée et paginée.',
        resolve: 'L’acte et l’expression que désigne un identifiant luxembourgeois ou un permalien épinglé.',
        search: 'Une référence exacte ouvre son acte ; toute autre expression est recherchée dans le texte des articles luxembourgeois dont les droits le permettent, ou dans le texte d’un acte de l’UE désigné par son identifiant.',
        status_on: 'Les propres énoncés de l’éditeur sur l’entrée en vigueur d’un acte luxembourgeois et sur sa fin, mot pour mot, à côté de la version applicable à une date.',
        timeline: 'Chaque version détenue d’un acte luxembourgeois, dans l’ordre d’applicabilité de l’éditeur ; pour un acte de l’UE, chaque libellé découvert, dans l’ordre des dates.',
        transposition: 'Les liens entre les directives de l’UE et les actes luxembourgeois qui les transposent, tels que chaque éditeur les affirme.',
        verify: 'Vérifie un permalien épinglé au regard de l’empreinte que détient l’index, ou donne les empreintes actuelles à épingler.',
      }),
    }),
    envelope: Object.freeze({
      heading: 'L’enveloppe',
      text: 'Chaque réponse, en REST comme en MCP, est une enveloppe, {schema}, avec ces membres :',
      members: Object.freeze({
        schema: 'L’identité du schéma de l’enveloppe.',
        version: 'La version de la plateforme.',
        object_type: 'Le type de l’objet : une enveloppe.',
        request_ref: 'Une référence opaque à cette seule requête, à l’usage propre du client.',
        operation_id: 'L’opération qui a répondu.',
        registry_schema: 'L’identité du schéma du registre des opérations.',
        registry_sha256: 'L’empreinte du registre des opérations qu’exécute ce serveur.',
        context: 'Ce sur quoi la réponse a été donnée, ci-dessous.',
        verdict: 'La nature de la réponse, ci-dessous.',
        result: 'L’objet de la réponse, avec son propre schéma et son type, ou null lorsque l’opération refuse.',
        refusal: 'Le refus, avec son code et sa charge utile, ou null lorsque l’opération répond.',
      }),
      contextText: 'Son membre {context} indique ce sur quoi la réponse a été donnée :',
      context: Object.freeze({
        commits: 'Les révisions du code de Lex qui ont construit l’index, {builder} (« {builderLabel} »), et qui servent la réponse, {runtime} (« {runtimeLabel} »).',
        freshness: 'Dans quelle mesure les données sont à jour : quand l’index a été construit, la dernière collecte complète de chaque éditeur avec son âge et le décalage entre publication et collecte, l’âge maximal admis, et si les données sont plus anciennes que cet âge.',
        jurisdiction: 'L’ordre juridique du droit concerné : le Luxembourg ou l’UE.',
        provisional: 'Si la réponse est provisoire ; ce serveur envoie toujours {value}.',
        publisher: 'L’éditeur dont le droit fait l’objet de la réponse.',
        snapshot: 'L’instantané du corpus dont l’index a été dérivé, par son identité et son empreinte.',
        status: 'Si l’opération a répondu ou refusé.',
        timeline_semantics: 'Comment lire les dates de la réponse : {luxembourg} pour le Luxembourg, les dates à partir desquelles l’éditeur indique que ses versions sont applicables ; {europe} pour l’UE, les dates des libellés de l’éditeur.',
      }),
      verdictText: 'Son membre {verdict} relève de l’ensemble fermé du registre :',
      verdicts: Object.freeze({
        answer: 'L’opération a répondu.',
        answer_with_enrichment: 'Réservé à l’assistant, qui est désactivé ; ce serveur ne l’envoie pour aucune réponse.',
        point: 'La réponse renvoie ailleurs : c’est ce que donne {ask}, puisque l’assistant est désactivé.',
        clarify: 'Réservé à l’assistant, qui est désactivé ; ce serveur ne l’envoie pour aucune réponse.',
        refuse: 'L’opération a refusé, par un refus typé.',
        split: 'Réservé à l’assistant, qui est désactivé ; ce serveur ne l’envoie pour aucune réponse.',
      }),
      registry: '{member} vaut {digest} pour ce déploiement : c’est l’empreinte du registre, qui couvre chaque opération, l’empreinte de chaque schéma et chaque code de refus avec les champs que sa charge utile doit porter ; elle change donc dès que l’un d’eux change. L’identité de chaque schéma se termine par sa version.',
    }),
    refusals: Object.freeze({
      heading: 'Refus',
      text: Object.freeze({
        one: 'Lorsqu’une opération ne peut pas répondre, elle refuse avec un code du registre, qui en compte {count}, jamais par une réponse vide : le verdict de l’enveloppe est {refuse}, son résultat est null, et son refus, {schema}, porte le code et une charge utile. Le registre fixe les champs que la charge utile de chaque code doit porter ; une charge utile peut en porter davantage.',
        other: 'Lorsqu’une opération ne peut pas répondre, elle refuse avec l’un des {count} codes, jamais par une réponse vide : le verdict de l’enveloppe est {refuse}, son résultat est null, et son refus, {schema}, porte le code et une charge utile. Le registre fixe les champs que la charge utile de chaque code doit porter ; une charge utile peut en porter davantage.',
      }),
      pages: 'Sur les pages de ce site, la phrase simple d’un refus vient en premier, et son code et sa charge utile la suivent.',
      fields: 'Sa charge utile porte {fields}.',
      codes: Object.freeze({
        advice_boundary: 'La question demande un conseil : Lex montre le texte, et ne l’applique à la situation de personne.',
        ambiguous_identifier: 'L’identifiant désigne plus d’un acte ; les candidats sont listés, et aucun n’est choisi.',
        ambiguous_version: 'Plus d’une version couvre la date et l’éditeur ne dit pas laquelle s’applique ; chaque version candidate est listée, et aucune n’est choisie.',
        anchor_not_in_version: 'L’index ne contient aucun article portant cet identifiant dans la version ; les identifiants d’article les plus proches sont donnés, et aucune recherche dans le texte n’est lancée à la place.',
        derivation_refused: 'Lex refuse de dériver du texte qu’il détient ce qui est demandé ; la charge utile indique ce qui est refusé et pourquoi.',
        format_not_available: 'Le format demandé n’est pas proposé pour cette réponse ; les formats proposés sont listés.',
        identifier_unknown: 'Aucun acte détenu par l’index ne porte cet identifiant ; la charge utile donne la recherche officielle et ce qui permettrait de répondre.',
        language_not_available: 'Le texte n’est pas détenu dans cette langue ; les langues détenues sont listées.',
        no_corpus_mounted: 'Ce serveur ne détient aucun droit pour le moment.',
        no_version_for_date: 'Aucune version détenue par l’index ne couvre la date ; la charge utile donne la date à laquelle commence son historique et les dates détenues les plus proches, avant et après.',
        not_transposable: 'Seule une directive est transposée, et cet instrument n’en est pas une ; la charge utile dit pourquoi.',
        out_of_corpus_scope: 'L’acte est d’une sorte que Lex ne collecte pas ; la charge utile nomme sa source officielle.',
        pinned_digest_mismatch: 'L’empreinte qu’épingle un permalien n’est pas une empreinte que Lex détient pour cette version ; la charge utile donne l’empreinte actuelle et son permalien.',
        profiles_differ: 'Les deux versions ont été lues selon des profils de règles différents ; elles ne sont donc pas comparées.',
        rate_limited: 'Trop de requêtes ; la charge utile dit quand demander de nouveau.',
        retrieval_mode_unavailable: 'L’opération n’est pas servie dans le mode demandé, par exemple pour un identifiant de l’UE ; les modes disponibles sont listés.',
        snapshot_unknown: 'L’instantané ou le journal que nomme la requête n’est pas l’un de ceux que ce serveur détient ; la charge utile dit ce qui permettrait de répondre.',
        text_not_available: 'Le fichier de l’éditeur ne contient aucun texte que Lex puisse lire, comme une annexe fournie uniquement sous forme d’images ; son identité officielle et sa source sont données.',
        text_withheld: 'La licence du texte ne permet pas à Lex de le montrer ; son lien officiel et son empreinte sont donnés.',
        upstream_unreachable: 'Une source dont Lex dépend n’a pas pu être jointe ; la charge utile dit s’il faut demander de nouveau.',
      }),
      presentationHeading: 'Deux réponses qui ne sont pas des refus',
      presentation: Object.freeze({
        assistant_v3_unavailable: '{code} : ce que {ask} répond à chaque question, puisque l’assistant est désactivé ; il nomme les opérations qui répondent à partir du droit que Lex détient.',
        localization_unavailable: '{code} : la page présentée à un lecteur en allemand ou en luxembourgeois, puisque seuls le français et l’anglais sont relus ; elle renvoie aux pages dans ces deux langues.',
      }),
      transportHeading: 'Échecs sous l’enveloppe',
      transport: 'Une requête que le serveur ne peut ni lire ni acheminer ne reçoit pas d’enveloppe : il y est répondu par un document de problème, {schema}, avec un statut HTTP et l’un de ces codes. À la différence d’un refus, ce n’est pas une réponse sur le droit.',
      transportRow: '{code}, HTTP {status} : {title}',
    }),
    citing: Object.freeze({
      heading: 'Citer',
      quote: 'Chaque article cité porte {text}, l’empreinte de son texte ; {body}, l’empreinte du fichier de l’éditeur dont il a été lu ; {source}, sa source officielle ; et {permalink}, qui épingle l’éditeur, l’acte, la date et l’empreinte de la version ou du libellé de l’UE, la langue et l’article : {shape}.',
      verify: '{verify} vérifie l’empreinte d’un permalien au regard de celle que détient l’index, à partir de ce que détient ce serveur : il n’interroge jamais l’éditeur.',
      forms: 'Sur la page de lecture, un article est cité sous trois formes, « {conventional} », « {withPermalink} » et « {evidence} », et un export porte les trois. Dans un document, citez le texte officiel par sa référence officielle.',
    }),
    limits: Object.freeze({
      heading: 'Limites',
      anonymous: 'Il n’y a ni clé ni compte, et le serveur ne garde aucune trace d’une requête : voir la {privacyPage}.',
      size: 'Le corps d’une requête compte au plus {bytes} octets ; il est répondu à un corps plus grand par {code}.',
      paging: 'Les listes sont paginées : {limit} fixe la taille d’une page, dans les bornes qu’énonce chaque schéma de requête, et {after} reprend là où la dernière page s’est arrêtée.',
      rate: 'Ce serveur ne fixe à ce jour aucune limite de débit par client, et le service est de petite taille, si bien qu’un usage intensif ralentit tous les lecteurs : envoyez une requête à la fois et interrogez avec modération. Le registre contient le refus {code}, qui porte {retryAfter} : un client qui le reçoit attend cette durée avant de demander de nouveau.',
      availability: 'Aucune disponibilité n’est garantie : voir les {termsPage}.',
    }),
    events: Object.freeze({
      heading: 'Suivre les modifications',
      text: '{events} lit le journal de ce que chaque construction de l’index a constaté : au début d’un journal, un événement {first} pour chaque version détenue, puis les événements de comparaison de chaque construction ultérieure, chacun daté par sa construction, jamais par le moment de son observation. Interrogez-le avec {after}, le curseur que la dernière réponse a donné comme {nextAfter}, et {limit}, et filtrez-le avec {event}.',
      atLeastOnce: 'La livraison se fait au moins une fois : un même curseur peut renvoyer les mêmes événements, si bien qu’un client conserve le dernier curseur et ignore les événements déjà vus, par {seq} au sein de {logId}. Il n’y a ni webhook ni envoi spontané : interrogez.',
      names: 'Les événements qu’un journal peut contenir :',
      cursor: 'Un curseur correspond à {pattern}.',
      drift: '{drift} liste les réponses datées passées qu’une révision ultérieure de l’éditeur a invalidées, retrouvées à partir des événements de révision du journal.',
      platform: 'La réponse des événements énonce sa propre livraison, telle que le serveur l’envoie :',
    }),
    schemas: Object.freeze({
      heading: 'Les schémas',
      text: 'Chaque schéma est un document JSON Schema ({dialect}), servi ici octet pour octet tel que le serveur le détient, avec l’empreinte de ses octets ; lorsque le serveur charge un schéma, il vérifie cette empreinte au regard de celle que nomme le registre.',
      envelope: 'L’enveloppe : {link}',
      refusal: 'Le refus : {link}',
      digest: 'empreinte {digest}',
    }),
  }),
  common: Object.freeze({
    loading: 'Interrogation de ce serveur.',
    languageNames: Object.freeze({ fra: 'français', deu: 'allemand', eng: 'anglais' }),
    language: 'Langue',
    appliesFrom: 'S’applique à partir du',
    nextFrom: 'Version suivante à partir du',
    permalink: 'Permalien',
    noneHeld: 'aucune détenue',
    notHeldRow: '{item} : {reason}',
    listSeparator: ' ; ',
    longDate: '{day} {month} {year}',
    firstDay: '{day}er',
    months: Object.freeze(['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre']),
    europeAnnexes: Object.freeze({
      one: '{count} annexe du libellé en {language} n’est pas communiquée sous forme de texte et n’est jamais interrogée, citée ni exportée : {reason}. Source officielle {source}.',
      other: '{count} annexes du libellé en {language} ne sont pas communiquées sous forme de texte et ne sont jamais interrogées, citées ni exportées : {reason}. Source officielle {source}.',
    }),
    europeForceDates: Object.freeze({
      heading: 'Les dates de l’éditeur que ce site détient pour cet acte',
      roles: Object.freeze({
        entry_into_force: 'Entrée en vigueur : {date}',
        application: 'Mise en application : {date}',
        end_of_validity: 'Fin de validité, totale ou partielle : {date}',
        deadline: 'Date limite : {date}',
        signature: 'Signature : {date}',
        not_pinned: 'Une date portant le code {code} de l’éditeur, à laquelle ce site n’attribue aucun rôle : {date}',
      }),
      notPinnedNoCode: 'Une date sans code de l’éditeur, à laquelle ce site n’attribue aucun rôle : {date}',
      openEnd: 'Fin de validité : aucune date (l’éditeur enregistre {date}, sa marque d’une fin ouverte)',
      qualifier: '(mention de l’éditeur, mot pour mot : {qualifier})',
      roleNames: Object.freeze({
        entry_into_force: 'entrée en vigueur',
        application: 'mise en application',
        end_of_validity: 'fin de validité',
        deadline: 'date limite',
      }),
      notHeld: 'Dates non détenues pour cet acte : {roles}. Les déclarations datées de l’éditeur que ce site détient pour cet acte n’indiquent aucune date de ce type, ce qui ne dit rien de l’existence d’une telle date.',
      conflict: 'L’éditeur enregistre plus d’une date d’entrée en vigueur pour cet acte ({dates}) : chacune est indiquée, et ce site n’en choisit aucune.',
      notWording: 'Aucune des dates ci-dessus ne sert à choisir un libellé : chaque libellé ci-dessous porte sa propre date.',
    }),
    asPublished: Object.freeze({
      label: 'tel que publié ; peut avoir été modifié depuis ; aucun texte consolidé détenu',
      laterNotHeld: 'Versions ultérieures : non détenues.',
      laterNamed: Object.freeze({
        one: 'Versions ultérieures : non détenues. Les données de l’éditeur nomment {count} version consolidée de cet acte, dont ce site ne détient pas le texte :',
        other: 'Versions ultérieures : non détenues. Les données de l’éditeur nomment {count} versions consolidées de cet acte, dont ce site ne détient pas les textes :',
      }),
      consolidationFrom: '{iri}, applicable à partir du {from} (éditeur)',
      consolidationFromTo: '{iri}, applicable du {from} au {to} (éditeur)',
      consolidationUndated: '{iri}, sans date d’applicabilité unique indiquée par l’éditeur',
      endOfForce: 'Les données de l’éditeur indiquent le {date} comme date de fin de vigueur de cet acte.',
      authenticity: Object.freeze({
        electronic_journal_officiel: 'Ce texte a été extrait et normalisé par Lex, à titre d’information ; il ne fait pas foi. Depuis le 1er janvier 2017, le Journal officiel du Grand-Duché de Luxembourg (Mémorial A) publié sous forme électronique fait seul foi (loi du 23 décembre 2016, art. 5). L’acte tel que publié : {act}.',
        printed_memorial: 'Ce texte a été extrait et normalisé par Lex, à titre d’information ; il ne fait pas foi. Pour un acte publié avant le 1er janvier 2017, le Mémorial imprimé fait foi, et le PDF de l’éditeur en est la reproduction numérique. L’acte tel que publié : {act}.',
        publication_date_not_stated: 'Ce texte a été extrait et normalisé par Lex, à titre d’information ; il ne fait pas foi. Les données de l’éditeur n’indiquent aucune date de publication unique pour cet acte ; Lex n’indique donc pas quelle édition fait foi. L’acte tel que publié : {act}.',
      }),
    }),
  }),
  citation: Object.freeze({
    months: Object.freeze(['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre']),
    date: '{day} {month} {year}',
    firstOfMonth: '{day}er {month} {year}',
    withPermalink: '{citation} ({permalink})',
    luxembourg: Object.freeze({
      article: 'Art. {number}',
      articleFirst: 'Art. {number}er',
      articleUnique: 'Art. unique',
      acts: Object.freeze({
        loi: '{article} de la loi du {date}',
        rgd: '{article} du règlement grand-ducal du {date}',
        agd: '{article} de l’arrêté grand-ducal du {date}',
        rmin: '{article} du règlement ministériel du {date}',
        amin: '{article} de l’arrêté ministériel du {date}',
        code: '{article} du {code}',
      }),
      titles: Object.freeze({
        loi: 'Loi du {date}',
        rgd: 'Règlement grand-ducal du {date}',
        agd: 'Arrêté grand-ducal du {date}',
        rmin: 'Règlement ministériel du {date}',
        amin: 'Arrêté ministériel du {date}',
      }),
      version: '{citation}, dans sa version applicable au {date}',
      versionEntryIntoForce: '{citation}, dans sa version publiée, entrée en vigueur le {date}',
    }),
    europe: Object.freeze({
      article: 'Article {number}',
      acts: Object.freeze({
        R: '{article} du règlement {designation}',
        L: '{article} de la directive {designation}',
        D: '{article} de la décision {designation}',
      }),
      domains: Object.freeze({ EU: 'UE', EC: 'CE', EEC: 'CEE' }),
      numbered: '({domain}) n° {number}/{year}',
      numberedNoDomain: 'n° {number}/{year}',
      yearFirst: '({domain}) {year}/{number}',
      suffixed: '{year}/{number}/{domain}',
      plain: '{year}/{number}',
      version: '{citation}, au {date}',
      authenticity: Object.freeze({
        electronic_edition: 'Seul le Journal officiel de l’Union européenne publié sous forme électronique fait foi et produit des effets juridiques (règlement (UE) n° 216/2013, article 1er, paragraphe 2) ; ce texte est une reproduction lue dans le paquet Formex de l’Office des publications, et non l’édition qui fait foi.',
        edition_by_publication_date: 'Pour un acte publié avant le 1er juillet 2013, l’édition imprimée du Journal officiel de l’Union européenne fait foi ; l’édition électronique fait foi depuis cette date (règlement (UE) n° 216/2013). Ce texte est une reproduction lue dans le paquet Formex de l’Office des publications, et non l’édition qui fait foi.',
      }),
    }),
    evidence: Object.freeze({
      permalink: 'Permalien : {permalink}',
      eli: 'ELI : {eli}',
      celex: 'CELEX : {celex}',
      title: 'Intitulé (éditeur) : {title}',
      version: 'Version : applicable à partir du {date} (éditeur)',
      versionEntryIntoForce: 'Version : l’acte tel que publié ; entrée en vigueur le {date} (éditeur)',
      memorial: 'Publication : Mémorial A n° {number} de {year} ({iri})',
      memorialIri: 'Publication : {iri}',
      memorialNotHeld: 'Publication : le numéro du Mémorial A n’est pas détenu par cet index.',
      publicationDate: 'Date de publication (éditeur) : {date}',
      originalWording: 'Libellé : le libellé original du {date}',
      consolidatedWording: 'Libellé : le libellé consolidé du {date}',
      officialJournalNotHeld: 'Publication : la référence au Journal officiel n’est pas détenue par cet index.',
      textSha256: 'Empreinte du texte (SHA-256) : {digest}',
      noText: 'Empreinte du texte (SHA-256) : aucune, l’article est détenu sans texte.',
      consulted: 'Consulté sur Lex le {date} (date au Luxembourg).',
    }),
  }),
  form: Object.freeze({
    workIdentifier: 'Identifiant de l’acte',
    workIdentifierOptional: 'Identifiant de l’acte (facultatif)',
    phrase: 'Expression',
    date: 'Date',
    datePlaceholder: 'aaaa-mm-jj',
    today: 'Aujourd’hui au Luxembourg : {date}.',
    articleIdPlaceholder: 'art_15',
    from: 'Du',
    to: 'Au',
    articleId: 'Identifiant d’article',
    language: 'Langue',
    anyLanguage: 'Toute langue détenue',
    submit: Object.freeze({
      search: 'Rechercher',
      dossier: 'Lire le dossier',
      reading: 'Lire',
      history: 'Retracer',
      compare: 'Comparer',
      radar: 'Lister les modifications',
      export: 'Lire pour exporter',
    }),
  }),
});

/**
 * The French of the sentences the screens say for a refusal, reviewed with the table, each keyed by the English the
 * screens hold (`live-*.mjs`, `live-refusals.mjs`), so a sentence the screens no longer say keeps no French and one
 * they start to say has none until it is written (`refusal-sentences.test.mjs` holds the two level):
 * - `sentences`: the sentence each screen says for a refusal code;
 * - `templates`: per screen, the two sentences that name a refusal only by its code, with `{code}` and `{reason}`;
 * - `hints`: what a refusal whose card cannot be shown still carries, with `{date}` and `{ids}`.
 */
export const REFUSALS_FR = Object.freeze({
  sentences: Object.freeze({
    'This build has no index mounted.':
      'Ce déploiement n’a aucun index monté.',
    'This build has no Luxembourg index mounted.':
      'Ce déploiement n’a pas d’index luxembourgeois monté.',
    'This build has no EU index mounted.':
      'Ce déploiement n’a pas d’index de l’UE monté.',
    "This build has no index mounted for this request's publisher.":
      'Ce déploiement n’a pas d’index monté pour l’éditeur de cette demande.',
    'This index holds no searchable text in the language asked for.':
      'Cet index ne contient aucun texte interrogeable dans la langue demandée.',
    'This index holds no work under that identifier.':
      'Cet index ne contient aucun acte sous cet identifiant.',
    'That identifier names more than one held work or expression, so none is searched.':
      'Cet identifiant désigne plusieurs actes ou expressions détenus ; la recherche ne porte donc sur aucun d’eux.',
    'An EU act is searched in the wording this server holds, not at a date: leave the date empty to search it.':
      'Un acte de l’UE est interrogé dans le libellé que ce serveur détient, et non à une date : laissez la date vide pour l’interroger.',
    'This work is not held in the language asked for.':
      'Cet acte n’est pas détenu dans la langue demandée.',
    'That identifier names more than one held work, so no dossier is chosen.':
      'Cet identifiant désigne plusieurs actes détenus ; aucun dossier n’est donc retenu.',
    'No state of this work that this index holds applies on that date.':
      'Aucune version de cet acte contenue dans cet index ne s’applique à cette date.',
    'Several states of this work apply on that date, and none is chosen.':
      'Plusieurs versions de cet acte s’appliquent à cette date, et aucune n’est retenue.',
    "This state's text is withheld: its rights did not admit it.":
      'Le texte de cette version n’est pas communiqué : ses droits ne le permettaient pas.',
    'This index holds this state but no text for it.':
      'Cet index contient cette version, mais aucun texte de celle-ci.',
    "This index cannot read this work's text.":
      'Cet index ne peut pas lire le texte de cet acte.',
    'No state of this work that this index holds carries that article id.':
      'Aucune version de cet acte contenue dans cet index ne comporte cet identifiant d’article.',
    "This index cannot trace this work's articles.":
      'Cet index ne peut pas retracer les articles de cet acte.',
    'No state of this work that this index holds applies on one of the dates.':
      'À l’une des deux dates, aucune version de cet acte contenue dans cet index ne s’applique.',
    'Several states of this work apply on one of the dates, and none is chosen.':
      'Plusieurs versions de cet acte s’appliquent à l’une des deux dates, et aucune n’est retenue.',
    'The two states were read under different rule profiles, so they are not compared.':
      'Les deux versions ont été lues selon des profils de règles différents ; elles ne sont donc pas comparées.',
    "This index cannot compare this work's states.":
      'Cet index ne peut pas comparer les versions de cet acte.',
    'This index holds no state in the language asked for.':
      'Cet index ne contient aucune version dans la langue demandée.',
    "This index cannot list this work's changes.":
      'Cet index ne peut pas lister les modifications de cet acte.',
    'This index holds no wording of this EU act for that date.':
      'Cet index ne contient aucun libellé de cet acte de l’UE pour cette date.',
    'This index holds different texts of this EU act for that date, so none is chosen.':
      'Cet index contient des textes différents de cet acte de l’UE pour cette date ; aucun n’est donc retenu.',
    "This wording's text is withheld: its rights did not admit it.":
      'Le texte de ce libellé n’est pas communiqué : ses droits ne le permettaient pas.',
    'This index knows this wording of the EU act but holds no text for it.':
      'Cet index connaît ce libellé de l’acte de l’UE, mais n’en contient aucun texte.',
    'The version this permalink pins is not the one this index holds for its date.':
      'La version que ce permalien épingle n’est pas celle que cet index contient pour sa date.',
    'The wording this permalink pins is not one this index holds for its date.':
      'Le libellé que ce permalien épingle n’est pas un libellé que cet index contient pour sa date.',
    'This site holds no act the reference names, so nothing was opened and no word was searched.':
      'Ce site ne détient aucun acte que la référence désigne ; rien n’a donc été ouvert, et aucun mot n’a été recherché.',
    'The reference does not name a single act or article for certain, and none is chosen.':
      'La référence ne désigne pas avec certitude un seul acte ou article, et aucun n’est retenu.',
    'This site holds the act the reference names and the version it names, but that version does not cover that date, so nothing was opened and no word was searched.':
      'Ce site détient l’acte que la référence désigne et la version qu’elle désigne, mais cette version ne couvre pas cette date ; rien n’a donc été ouvert, et aucun mot n’a été recherché.',
    'This site holds the act the reference names, but not the version it names, so nothing was opened and no word was searched.':
      'Ce site détient l’acte que la référence désigne, mais non la version qu’elle désigne ; rien n’a donc été ouvert, et aucun mot n’a été recherché.',
    'This site holds the act the reference names and the version the date selects, but no article of that version, so nothing was opened and no word was searched.':
      'Ce site détient l’acte que la référence désigne et la version que la date retient, mais aucun article de cette version ; rien n’a donc été ouvert, et aucun mot n’a été recherché.',
    'This site holds the act the reference names and the wording selected, but no article of that wording, so nothing was opened and no word was searched.':
      'Ce site détient l’acte que la référence désigne et le libellé retenu, mais aucun article de ce libellé ; rien n’a donc été ouvert, et aucun mot n’a été recherché.',
    'This site holds no article with that number in the wording the date selects, and no other article is opened in its place.':
      'Ce site ne détient aucun article de ce numéro dans le libellé que la date retient, et aucun autre article n’est ouvert à sa place.',
    'This site holds no article with that number in the version the date selects, and no other article is opened in its place.':
      'Ce site ne détient aucun article de ce numéro dans la version que la date retient, et aucun autre article n’est ouvert à sa place.',
    'This server holds this EU act in one wording only, and answers no date for it: leave the date out of the reference.':
      'Ce serveur ne détient cet acte de l’UE que dans un seul libellé, et ne répond à aucune date pour celui-ci : retirez la date de la référence.',
    'This index holds no article with that id in this version.':
      'Cet index ne contient aucun article portant cet identifiant dans cette version.',
    'This index holds no article with that id in this wording.':
      'Cet index ne contient aucun article portant cet identifiant dans ce libellé.',
  }),
  templates: Object.freeze({
    coverage: Object.freeze({
      unexpected: 'Le rapport de couverture a été refusé avec le code {code}.',
      unshown: 'Le rapport de couverture a été refusé avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
    }),
    search: Object.freeze({
      unexpected: 'La recherche a été refusée avec le code {code}.',
      unshown: 'La recherche a été refusée avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
    }),
    dossier: Object.freeze({
      unexpected: 'Le dossier a été refusé avec le code {code}.',
      unshown: 'Le dossier a été refusé avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
    }),
    reading: Object.freeze({
      unexpected: 'La lecture a été refusée avec le code {code}.',
      unshown: 'La lecture a été refusée avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
    }),
    history: Object.freeze({
      unexpected: 'L’historique de la disposition a été refusé avec le code {code}.',
      unshown: 'L’historique de la disposition a été refusé avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
    }),
    compare: Object.freeze({
      unexpected: 'La comparaison a été refusée avec le code {code}.',
      unshown: 'La comparaison a été refusée avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
    }),
    radar: Object.freeze({
      unexpected: 'Le radar des modifications a été refusé avec le code {code}.',
      unshown: 'Le radar des modifications a été refusé avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
    }),
  }),
  hints: Object.freeze({
    'The history this index holds for this work begins on {date}.':
      'L’historique que cet index contient pour cet acte commence le {date}.',
    'The nearest article ids this index holds are {ids}.':
      'Les identifiants d’article les plus proches que cet index contient sont {ids}.',
  }),
});

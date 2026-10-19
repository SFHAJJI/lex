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
// No French entry ships unreviewed.
//
// Vocabulary: a work is an "acte" (a publisher's Cellar work, one record of an EU act, is an "œuvre de l’éditeur"), a
// Luxembourg state a "version", the publisher's article id an "identifiant d’article"; a wording (of an EU act, or of a
// Luxembourg article from state to state) is a "libellé", never a "rédaction" or a "formulation", and an EU date is never
// "applicable"; held is "détenu", never "disponible"; a build is a "déploiement"; legal advice is "consultation
// juridique"; the publisher's record is "les données de l’éditeur" (never "registre", which is the operations registry).
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
    intro: 'Trouve les actes dont l’intitulé correspond à l’expression et, dans le texte que ce serveur détient et qu’il est autorisé à interroger, les articles qui la contiennent exactement telle que saisie : un texte que ses droits ne permettent pas de communiquer n’entre pas dans la recherche. L’expression est envoyée à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
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
    statesFromEntryIntoForce: '{count} versions : la première, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ; la plus récente applicable à partir du {to}.',
    statesInEntryIntoForce: '{count} version en {language}, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur).',
    statesInFromEntryIntoForce: '{count} versions en {language} : la première, l’acte tel que publié, daté de son entrée en vigueur le {from} (éditeur) ; la plus récente applicable à partir du {to}.',
    chainHeading: 'Le catalogue de l’éditeur entre les versions',
    textlessHeading: 'Versions dont ce site ne détient pas le texte',
    textlessRow: '{version} : {why}.',
    gap: 'Du {from} au {to}, le catalogue de l’éditeur ne recense aucune version : ce site ne comble pas cet intervalle.',
    overlap: 'Du {from} au {to}, deux versions du catalogue de l’éditeur sont toutes deux applicables (éditeur).',
    sameDate: 'Pour le {date}, le catalogue de l’éditeur recense {count} versions.',
    unclearEndDay: 'Le {date} est la fin que le catalogue de l’éditeur indique pour une version, la veille du début de la suivante ; on ne sait pas si ce jour lui appartient, et ce site ne l’attribue à aucune des deux.',
    invalidInterval: 'La version du {date} a, dans le catalogue de l’éditeur, une fin, le {to}, antérieure ou égale à son propre début : elle ne couvre aucune date ; son propre lien l’ouvre toujours.',
    chainNote: 'Ce sont les dates du catalogue de l’éditeur telles que ce site les a lues ; un intervalle sans version dans le catalogue n’est pas un vide du droit.',
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
    translation: 'Ce texte est l’expression en {language} de l’acte publiée par l’éditeur, à côté de son expression française {french}. Les données de l’éditeur n’indiquent pas quelle version linguistique fait foi.',
    consolidationCaveat: 'Une version consolidée est une documentation sans valeur probante : le texte authentique est l’acte tel que publié au Journal officiel (Mémorial A), {act}.',
    codeCaveat: 'Une version d’un code est une documentation sans valeur probante : les textes authentiques sont les actes publiés au Journal officiel (Mémorial A) qu’elle rassemble.',
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
    breakTo: 'En {language}, du {from} au {to} : {reasons}.',
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
    wording: Object.freeze({ changed: 'libellé modifié', unchanged: 'libellé inchangé' }),
    compared: '{wording} par rapport à la version du {date}{baseline} ; modifiés : {changed} ; ajoutés : {added} ; supprimés : {removed} ; inchangés : {unchanged}',
    notCompared: 'non comparée : {reason}{named}',
    reason: Object.freeze({
      first_held_state: 'c’est la première version que cet index détient pour cet acte et cette langue ; il n’y a donc rien à quoi la comparer',
      ambiguous_version: 'plusieurs versions de l’acte s’appliquent à cette date ; aucune n’est donc comparée',
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
    candidateEntryIntoForce: 'l’acte tel que publié, daté de son entrée en vigueur le {validFrom} (éditeur), empreinte {hash}, {published}',
    candidateEntryIntoForceWithdrawalNotStated: 'l’acte tel que publié, daté de son entrée en vigueur le {validFrom} (éditeur), empreinte {hash}, {published}, retrait non indiqué par la plateforme',
    reasons: Object.freeze({
      before_history: 'Le catalogue de l’éditeur, tel que ce site l’a lu au plus tard le {read}, ne recense aucune version de cet acte avant le {begins}.',
      catalogue_hole: 'Le catalogue de l’éditeur, tel que ce site l’a lu au plus tard le {read}, ne recense aucune version de cet acte du {from}, fin que l’éditeur indique pour la version précédente, au {to}, début de la version suivante ; ce site ne comble pas cet intervalle avec une autre version.',
      after_closed_end: 'La dernière version du catalogue de l’éditeur, tel que ce site l’a lu au plus tard le {read}, est applicable jusqu’au {closed} (éditeur), et le catalogue ne recense aucune version ultérieure : c’est là que s’arrêtent les versions du catalogue, ce qui ne dit rien de la fin éventuelle de la règle de droit.',
      publisher_end_convention_unclear: 'Le catalogue de l’éditeur indique le {date}, veille du début de la version suivante, comme fin de la version précédente : on ne sait donc pas si ce jour appartient à cette version, et ce site ne l’attribue à aucune des deux.',
      publisher_interval_invalid: 'Le catalogue de l’éditeur donne à la version qui s’appliquerait à cette date une fin antérieure ou égale à son propre début : ce site ne lit donc aucun intervalle pour elle et ne communique aucune version pour cette date ; le lien propre de cette version l’ouvre toujours.',
      no_expression_in_language: 'Une version française de cet acte couvre cette date, et ce site n’en détient aucune expression en {language}.',
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
      not_acquired: 'ce site ne l’a pas acquise',
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
  home: Object.freeze({
    title: 'Le droit luxembourgeois et de l’UE à la date de votre choix',
    eyebrow: 'Rechercher dans le droit',
    heading: 'Le droit luxembourgeois et de l’UE à la date de votre choix',
    intro: 'Recherchez dans les textes que ce site détient, ouvrez un acte tel qu’il se présentait à la date de votre choix, voyez comment chaque article a changé, comparez deux dates et exportez avec des citations que chacun peut vérifier auprès de la source officielle. Lorsque ce site ne détient pas un texte ou une date, il le dit.',
    aboutHeading: 'Ce qu’est Lex et ce qu’il n’est pas',
    coverage: 'Ce site détient {acts} du droit luxembourgeois, en {versions} comptant {articles}, d’après l’index construit le {date}.',
    coverageUndated: 'Ce site détient {acts} du droit luxembourgeois, en {versions} comptant {articles} ; l’index qu’il détient ne consigne aucune date de construction.',
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
      authenticity: 'Seul le Journal officiel de l’Union européenne publié sous forme électronique fait foi et produit des effets juridiques (règlement (UE) n° 216/2013, article 1er, paragraphe 2) ; ce texte est une reproduction lue dans le paquet Formex de l’Office des publications, et non l’édition qui fait foi.',
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

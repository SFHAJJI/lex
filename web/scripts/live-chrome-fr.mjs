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
// page's `search.intro`, `search.idle` and `search.noHit` once search matches only the text its rights admit (PR #910).
// No French entry ships unreviewed.
//
// Vocabulary: a work is an "acte" (a publisher's Cellar work, one record of an EU act, is an "œuvre de l’éditeur"), a
// Luxembourg state a "version", the publisher's article id an "identifiant d’article"; a wording (of an EU act, or of a
// Luxembourg article from state to state) is a "libellé", never a "rédaction" or a "formulation", and an EU date is never
// "applicable"; held is "détenu", never "disponible"; a build is a "déploiement"; legal advice is "consultation
// juridique". Typography: a no-break space (U+00A0) stands before : ; ! ? % and » and after «, and the apostrophe is ’.
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
    intro: 'Trouve les articles dont le texte contient l’expression exactement telle que saisie, ou chacun de ses mots, dans le texte que ce serveur détient et qu’il est autorisé à interroger : un texte que ses droits ne permettent pas de communiquer n’entre pas dans la recherche. L’expression est envoyée à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez une expression pour rechercher dans le texte des articles que ce serveur détient et qu’il est autorisé à interroger.',
    population: Object.freeze({
      one: '« {query} » en {language} : {strict} avec l’expression exacte, {relaxed} avec chacun des mots, dans {works} acte.',
      other: '« {query} » en {language} : {strict} avec l’expression exacte, {relaxed} avec chacun des mots, dans {works} actes.',
    }),
    notCounted: 'non compté',
    lane: Object.freeze({ strict: 'expression exacte', relaxed: 'chacun des mots' }),
    namesWork: 'L’expression désigne l’acte « {title} » ({identifier}).',
    severalWorks: 'L’expression correspond aux intitulés de plusieurs actes :',
    candidate: '« {title} » ({identifier})',
    noTitlesHeld: 'Cet index ne contient aucun intitulé d’acte ; l’expression a donc été recherchée dans le seul texte des articles.',
    ambiguousHeading: 'Actes ayant plusieurs versions au {date}',
    ambiguousNote: 'Ces actes ont plus d’une version applicable à cette date ; aucune n’est donc retenue et aucune ne fournit de résultat.',
    ambiguousWork: '{work} : {candidates}',
    hit: '{article} dans {work}, version du {date}',
    noHit: 'Aucun article du texte que ce serveur détient et qu’il est autorisé à interroger ne contient « {query} ».',
    noText: 'Cet index ne contient aucun texte interrogeable en {language} ; il contient du texte en {languages}.',
    nextPage: 'Page suivante',
    euWording: 'Chaque résultat se trouve dans le libellé original de {celex} que ce serveur détient en {language}, daté du {date} et épinglé par son empreinte : {permalink}',
    euHit: '{heading} de {celex}, libellé du {date}',
    notHeldHeading: 'Ce que cette recherche ne couvre pas',
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
  }),
  reading: Object.freeze({
    title: 'Lecture',
    eyebrow: 'Lecture',
    heading: 'Le texte à une date',
    intro: 'Le texte d’un acte luxembourgeois tel qu’il se présentait à une date, ou celui d’un acte de l’UE dans le libellé qui sert de réponse pour cette date (son libellé original ou un libellé consolidé ultérieur), article par article, tel que l’éditeur l’a écrit, avec ce qu’il faut pour le citer. L’identifiant et la date sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et une date pour lire son texte à cette date.',
    rights: 'Texte communiqué sous {rights}. Lu au {date}.',
    rightsIn: 'Texte communiqué sous {rights}. Lu au {date} en {language}.',
    stateHeading: '{work}, {language}, la version applicable à partir du {from}',
    stateHeadingNext: '{work}, {language}, la version applicable à partir du {from} (la version suivante détenue s’applique à partir du {next})',
    counts: Object.freeze({
      one: '{count} article cité ; détenus sans texte : {withoutText} ; non admis : {notAdmitted} ; date propre différente de celle de la version : {conflicts}.',
      other: '{count} articles cités ; détenus sans texte : {withoutText} ; non admis : {notAdmitted} ; date propre différente de celle de la version : {conflicts}.',
    }),
    validityConflict: 'La date propre de cet article est le {own} ; sa version s’applique à partir du {state}.',
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
  }),
  compare: Object.freeze({
    title: 'Comparer',
    eyebrow: 'Comparer',
    heading: 'Deux versions, article par article',
    intro: 'Les versions d’un acte luxembourgeois applicables à deux dates, comparées selon les identifiants d’article et le libellé de l’éditeur, sans rien affirmer sur l’effet juridique. L’identifiant et les dates sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et deux dates pour comparer les versions qui s’appliquaient à ces dates.',
    summary: '{work} : {from} comparé au {to}.',
    summaryIn: '{work} : {from} comparé au {to}, en {language}.',
    sideFrom: 'Date de départ',
    sideTo: 'Date d’arrivée',
    side: '{side} : la version applicable à partir du {from} ; articles : {articles} ; date propre différente de celle de la version : {conflicts} ; {permalink}',
    sideNext: '{side} : la version applicable à partir du {from} (la version suivante détenue s’applique à partir du {next}) ; articles : {articles} ; date propre différente de celle de la version : {conflicts} ; {permalink}',
    counts: 'Modifiés : {changed} ; ajoutés : {added} ; supprimés : {removed} ; inchangés : {unchanged}.',
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
    intro: 'Lisez un acte luxembourgeois à une date, épinglez les articles dont vous avez besoin et enregistrez-les en JSON, CSV ou PDF. Chaque article exporté porte sa citation, l’empreinte de son texte, sa source officielle et les droits sous lesquels il a été communiqué, et chaque export porte le filigrane. L’identifiant et la date sont envoyés à ce serveur dans la requête et nulle part ailleurs ; le fichier est créé dans cette page, et cette page ne conserve rien.',
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
    save: Object.freeze({ json: 'Enregistrer en JSON', csv: 'Enregistrer en CSV', pdf: 'Enregistrer en PDF' }),
    formatRefused: 'Le format {format} n’est pas proposé pour cet export : {reason}.',
    composeFailed: 'Cet export ne peut pas être composé : {reason}.',
    jsonSummary: 'Le JSON tel qu’il sera enregistré',
    europeNotComposed: 'Il s’agit d’un texte de l’UE. Son export n’est pas encore composé : l’outil de composition d’export n’épingle que les articles des actes luxembourgeois ; aucun fichier n’est donc proposé pour un texte de l’UE.',
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
    countsProvenance: 'Les décomptes du corpus et de l’index ci-dessous proviennent des artefacts désignés plus haut. Rien ici n’indique leur actualité : les dates de construction de la section historique disent quand chaque construction a eu lieu, ce qui borne le moment où son corpus a été observé sans dater aucun décompte. Les empreintes désignent exactement les artefacts comptés, ce qu’une date ne ferait pas. Les dates calendaires des tableaux des langues et des capacités sont celles de l’éditeur, relatives au droit, et non au moment du décompte.',
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
    title: '{title} - Lex V3 en direct',
    bannerLead: 'Déploiement de développement en direct.',
    banner: 'Cette page affiche ce que répond le serveur depuis lequel elle a été chargée. Lorsqu’un rapport de couverture arrive, ses empreintes désignent le corpus qu’il a compté ; jusque-là, ou si aucun corpus n’est monté, rien de ce qui suit n’en décrit un. Ce n’est ni une mise en production ni une consultation juridique.',
    localeNav: 'Langue de l’interface',
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
  }),
  form: Object.freeze({
    workIdentifier: 'Identifiant de l’acte',
    workIdentifierOptional: 'Identifiant de l’acte (facultatif)',
    phrase: 'Expression',
    date: 'Date',
    datePlaceholder: 'aaaa-mm-jj',
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

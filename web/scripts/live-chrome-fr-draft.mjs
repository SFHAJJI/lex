// The French draft of the live pages' interface copy, beside the English table (`live-chrome.mjs`), for review.
//
// Decision 41: French interface copy ships only after evidence-based legal-language review; machine translation
// alone is not sufficient. This is the driver's draft for that review, as the refusal sentences were
// (`refusal-sentences.mjs`): nothing in the product imports it, and `liveChrome('fr')` refuses until the reviewed
// wording becomes a table of its own. A test holds this draft to the English table's exact shape, so an entry added
// in English without a draft is caught.
//
//   node web/scripts/live-chrome-fr-draft.mjs > chrome-fr.md
//
// Vocabulary, as in the refusal drafts: a work is an "acte", a state a "version", the publisher's article id an
// "identifiant d'article".

import { pathToFileURL } from 'node:url';

import { liveChrome } from './live-chrome.mjs';

export const LIVE_CHROME_FR_DRAFT = Object.freeze({
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
    intro: 'Trouve les articles dont le texte contient l’expression exactement telle que saisie, ou chacun de ses mots, dans le texte que ce serveur détient. L’expression est envoyée à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez une expression pour rechercher dans le texte des articles que ce serveur détient.',
    population: Object.freeze({
      one: '« {query} » en {language} : {strict} avec l’expression exacte, {relaxed} avec chacun des mots, dans {works} acte.',
      other: '« {query} » en {language} : {strict} avec l’expression exacte, {relaxed} avec chacun des mots, dans {works} actes.',
    }),
    notCounted: 'non compté',
    lane: Object.freeze({ strict: 'expression exacte', relaxed: 'chacun des mots' }),
    namesWork: 'L’expression désigne l’acte « {title} » ({identifier}).',
    severalWorks: 'L’expression correspond aux intitulés de plusieurs actes :',
    candidate: '« {title} » ({identifier})',
    noTitlesHeld: 'Cet index ne contient aucun intitulé d’acte ; l’expression a donc été recherchée dans le seul texte des articles.',
    ambiguousHeading: 'Actes ayant plusieurs versions au {date}',
    ambiguousNote: 'Ces actes ont plus d’une version applicable à cette date ; aucune n’est donc retenue et aucune ne fournit de résultat.',
    hit: '{article} dans {work}, version du {date}',
    noHit: 'Aucun article du texte que ce serveur détient ne contient « {query} ».',
    noText: 'Cet index ne contient aucun texte consultable en {language} ; il contient du texte en {languages}.',
    nextPage: 'Page suivante',
  }),
  dossier: Object.freeze({
    title: 'Dossier',
    eyebrow: 'Dossier',
    heading: 'Le dossier d’un acte',
    intro: 'Ce que ce serveur détient pour un acte luxembourgeois : ses intitulés, ses versions datées par l’éditeur et ce que le dossier ne contient pas. L’identifiant est envoyé à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte pour consulter ce que ce serveur détient à son sujet.',
    noTitle: 'Cet index ne contient aucun intitulé pour cet acte.',
    shortTitle: '{title} (intitulé abrégé)',
    heldIn: '{iri}, disponible en {languages}.',
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
  }),
  reading: Object.freeze({
    title: 'Lecture',
    eyebrow: 'Lecture',
    heading: 'Le texte à une date',
    intro: 'Le texte d’un acte luxembourgeois tel qu’il se présentait à une date, article par article, tel que l’éditeur l’a écrit, avec ce qu’il faut pour le citer. L’identifiant et la date sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et une date pour lire le texte qui s’appliquait à cette date.',
    rights: 'Texte communiqué sous {rights}. Lu au {date}.',
    rightsIn: 'Texte communiqué sous {rights}. Lu au {date} en {language}.',
    stateHeading: '{work}, {language}, la version applicable à partir du {from}',
    stateHeadingNext: '{work}, {language}, la version applicable à partir du {from} (la version suivante détenue s’applique à partir du {next})',
    counts: Object.freeze({
      one: '{count} article cité ; sans texte : {withoutText} ; non admis : {notAdmitted} ; date propre différente de celle de la version : {conflicts}.',
      other: '{count} articles cités ; sans texte : {withoutText} ; non admis : {notAdmitted} ; date propre différente de celle de la version : {conflicts}.',
    }),
    validityConflict: 'La date propre de cet article est le {own} ; sa version s’applique à partir du {state}.',
    evidence: 'Empreinte du texte {text}, empreinte du corps {body}, source officielle {source}, {permalink}',
    withoutText: 'Détenus sans texte : {articles}.',
    notHeldHeading: 'Ce que cette lecture ne contient pas',
  }),
  history: Object.freeze({
    title: 'Historique d’une disposition',
    eyebrow: 'Historique d’une disposition',
    heading: 'Un article à travers ses versions',
    intro: 'Quelles versions détenues d’un acte luxembourgeois comportent l’identifiant d’article de l’éditeur, si sa formulation a changé d’une version à la suivante, et quelles versions détenues ne le comportent pas. L’identifiant de l’acte et l’identifiant d’article sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et l’identifiant d’article de l’éditeur pour le suivre à travers les versions détenues.',
    anchorHeading: '{anchor} dans {work}',
    carried: Object.freeze({
      one: 'Présent dans {count} version détenue, à partir du {from} ;',
      other: 'Présent dans {count} versions détenues, à partir du {from} ;',
    }),
    carriedIn: Object.freeze({
      one: 'Présent dans {count} version détenue en {language}, à partir du {from} ;',
      other: 'Présent dans {count} versions détenues en {language}, à partir du {from} ;',
    }),
    absent: Object.freeze({
      one: '{count} version détenue ne le comporte pas.',
      other: '{count} versions détenues ne le comportent pas.',
    }),
    runs: Object.freeze({
      one: '{language} : {count} séquence de libellé, libellés distincts : {distinct}',
      other: '{language} : {count} séquences de libellé, libellés distincts : {distinct}',
    }),
    wording: Object.freeze({ first: 'premier libellé détenu', changed: 'libellé modifié', unchanged: 'libellé inchangé' }),
    wordingColumn: 'Libellé',
    ownDateColumn: 'Date propre de l’article',
    ownDateNotStated: 'non indiquée',
    ownDateDiffers: '{date} (diffère de celle de la version)',
    absentHeading: 'Versions détenues qui ne comportent pas {anchor}',
    absentRow: '{language}, à partir du {from} : {permalink}',
  }),
  compare: Object.freeze({
    title: 'Comparer',
    eyebrow: 'Comparer',
    heading: 'Deux versions, article par article',
    intro: 'Les versions d’un acte luxembourgeois applicables à deux dates, comparées par les identifiants d’article et la formulation de l’éditeur, sans rien affirmer sur l’effet juridique. L’identifiant et les dates sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et deux dates pour comparer les versions qui s’appliquaient à ces dates.',
    summary: '{work} : {from} comparé au {to}.',
    summaryIn: '{work} : {from} comparé au {to}, en {language}.',
    sideFrom: 'Du',
    sideTo: 'Au',
    side: '{side} : la version applicable à partir du {from} ; articles : {articles} ; date propre différente de celle de la version : {conflicts} ; {permalink}',
    sideNext: '{side} : la version applicable à partir du {from} (la version suivante détenue s’applique à partir du {next}) ; articles : {articles} ; date propre différente de celle de la version : {conflicts} ; {permalink}',
    counts: 'Modifiés : {changed} ; ajoutés : {added} ; supprimés : {removed} ; inchangés : {unchanged}.',
    status: Object.freeze({ changed: 'modifié', added: 'ajouté', removed: 'supprimé', unchanged: 'inchangé' }),
    row: '{article} : {status} (libellé {from} → {to})',
    noWording: 'aucun',
    kept: Object.freeze({
      one: '{count} article inchangé',
      other: '{count} articles inchangés',
    }),
    bound: Object.freeze({ from: 'de départ', to: 'd’arrivée' }),
    notCompared: '{language} n’est pas comparé : {reason} (date {bound}).',
  }),
  radar: Object.freeze({
    title: 'Radar',
    eyebrow: 'Radar',
    heading: 'Le radar des modifications',
    intro: 'Les versions datées par l’éditeur des actes luxembourgeois que ce serveur détient dans une période donnée, chacune avec la version qu’elle a remplacée et l’indication d’un changement de formulation. Les dates et tout identifiant sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
    idle: 'Saisissez deux dates pour lister les versions datées par l’éditeur dans cet intervalle.',
    summary: 'Du {from} au {to} : {states} de {works}, sur {held} détenus.',
    states: Object.freeze({
      one: '{count} version',
      other: '{count} versions',
    }),
    works: Object.freeze({
      one: '{count} acte',
      other: '{count} actes',
    }),
    noRow: 'Aucune version détenue n’est datée dans cet intervalle.',
    windowMisses: 'Cet intervalle ne rejoint pas ce que cet index détient.',
    windowMissesRange: 'Cet intervalle ne rejoint pas ce que cet index détient, du {first} au {last}.',
    row: '{work}, {language}, à partir du {from} : {verdict}. {permalink}',
    wording: Object.freeze({ changed: 'libellé modifié', unchanged: 'libellé inchangé' }),
    compared: '{wording} par rapport à la version du {date}{baseline} : modifiés : {changed} ; ajoutés : {added} ; supprimés : {removed} ; inchangés : {unchanged}',
    notCompared: 'non comparée : {reason}{named}',
    reason: Object.freeze({
      first_held_state: 'c’est la première version que cet index détient pour cet acte et cette langue ; il n’y a donc rien à quoi la comparer',
      ambiguous_version: 'plusieurs versions de l’acte s’appliquent à cette date ; aucune n’est donc comparée',
      ambiguous_baseline: 'plusieurs versions s’appliquent à la veille ; aucune version de référence n’est donc retenue',
      profiles_differ: 'cette version et sa version de référence ont été lues selon des profils de règles différents ; elles ne sont donc pas comparées',
    }),
    named: '({label} {permalinks})',
    baseline: 'référence',
    candidates: 'candidates',
    truncated: 'La liste s’arrête avant le {date} ; une nouvelle demande reprend à partir de cette date.',
  }),
  export: Object.freeze({
    title: 'Composition d’export',
    eyebrow: 'Composition d’export',
    heading: 'Emporter des articles, avec leurs citations',
    intro: 'Lisez un acte luxembourgeois à une date, épinglez les articles dont vous avez besoin et enregistrez-les en JSON, CSV ou PDF. Chaque article exporté porte sa citation, l’empreinte de son texte, sa source officielle et les droits sous lesquels il a été servi, et chaque export porte le filigrane. L’identifiant et la date sont envoyés à ce serveur dans la requête et nulle part ailleurs ; le fichier est créé dans cette page, et cette page ne conserve rien.',
    idle: 'Saisissez un identifiant d’acte et une date, puis épinglez les articles à emporter.',
    readOn: 'Lu au {date}. Épinglez les articles à exporter.',
    readOnIn: 'Lu au {date} en {language}. Épinglez les articles à exporter.',
    pin: 'Épingler {article}',
    withoutTextHeading: 'Détenus sans texte',
    withoutTextNote: 'détenu sans texte ; un export le consigne comme exclu, avec sa raison.',
    panelHeading: 'Export',
    nothingPinned: 'Rien n’est encore épinglé. Épinglez un article ci-dessus : son export apparaît ici, avec ce qu’il contient.',
    counts: Object.freeze({
      one: '{count} article épinglé ; exportés avec texte : {withText} ; exclus : {excluded}.',
      other: '{count} articles épinglés ; exportés avec texte : {withText} ; exclus : {excluded}.',
    }),
    rights: 'Texte communiqué sous {rights}.',
    snapshot: 'Lu au {date}, à partir de l’instantané observé le {observedAt}. Corpus {corpus}, index {index}, registre {registry}.',
    item: '{article} ({language}, applicable à partir du {from}) : {citation}, empreinte du texte {digest}, source officielle {source}',
    excluded: '{article} ({language}, applicable à partir du {from}) : exclu, {reason}, {citation}',
    save: Object.freeze({ json: 'Enregistrer en JSON', csv: 'Enregistrer en CSV', pdf: 'Enregistrer en PDF' }),
    formatRefused: 'Le format {format} n’est pas proposé pour cet export : {reason}.',
    composeFailed: 'Cet export ne peut pas être composé : {reason}.',
    jsonSummary: 'Le JSON tel qu’il sera enregistré',
  }),
  card: Object.freeze({
    heading: 'Fiche d’évaluation',
    target: 'Portée de l’exécution : {target}',
    clean: 'Chaque contrôle automatique de cette fiche est satisfait, et chaque contrôle par permutation a détecté sa permutation.',
    notClean: '{gates}, et {controls}.',
    gatesNotPassing: Object.freeze({
      one: '{count} contrôle n’est pas satisfait',
      other: '{count} contrôles ne sont pas satisfaits',
    }),
    controlsNotCaught: Object.freeze({
      one: '{count} contrôle par permutation n’a pas détecté la permutation',
      other: '{count} contrôles par permutation n’ont pas détecté la permutation',
    }),
    listed: '{summary} ({list})',
    gateListed: '{gate} dans {set}, {arm} : {verdict}',
    controlListed: '{control} dans {set}, {arm} : {verdict}',
    verdict: Object.freeze({ pass: 'satisfait', fail: 'non satisfait', not_measured: 'non mesuré' }),
    controlVerdict: Object.freeze({
      caught_the_shuffle: 'a détecté la permutation',
      missed_the_shuffle: 'n’a pas détecté la permutation',
      not_applicable: 'sans objet',
    }),
    caption: '{set}, {arm} : cas : {cases}, empreinte {digest}',
    columns: Object.freeze({
      gate: 'Contrôle',
      verdict: 'Résultat',
      value: 'Valeur',
      threshold: 'Seuil',
      cases: 'Cas',
      wilson: 'Wilson 95 %',
      ruleOfThree: 'Règle de trois (95 %)',
    }),
    verdictReason: '{verdict} ({reason})',
    none: 'aucune',
    notARate: 'pas un taux',
    interval: 'de {low} à {high}',
    ruleOfThree: 'taux d’échec inférieur à {bound}',
    controlsHeading: 'Contrôles par permutation',
    control: '{control} sur {set}, {arm} : {verdict}, {reason} (graine {seed}, cas : {cases}, empreinte {digest}).',
    controlNote: '{control} sur {set}, {arm} : {verdict}, {reason} (graine {seed}, cas : {cases}, empreinte {digest}). {note}.',
    statisticalHeading: 'Lignes statistiques',
    statistical: '{dataset}, {name} : pas encore annoté. Contrôles {gates}. {governedBy}.',
    negativeHeading: 'Résultats négatifs',
    negative: 'Hypothèse : {hypothesis}. Jeu de données : {dataset}. Résultat : {result}. Décision : {decision}. Ce qui l’infirmerait : {reverse}.',
  }),
  shell: Object.freeze({
    title: '{title} - Lex V3 en direct',
    bannerLead: 'Version de développement en direct.',
    banner: 'Cette page affiche ce que répond le serveur depuis lequel elle a été chargée. Lorsqu’un rapport de couverture arrive, ses empreintes désignent le corpus qu’il a compté ; jusque-là, ou si aucun corpus n’est monté, rien de ce qui suit n’en décrit un. Ce n’est ni une version publiée ni un avis juridique.',
    localeNav: 'Langue de l’interface',
  }),
  common: Object.freeze({
    loading: 'Interrogation de ce serveur.',
    languageNames: Object.freeze({ fra: 'français', deu: 'allemand', eng: 'anglais' }),
    language: 'Langue',
    appliesFrom: 'S’applique à partir du',
    nextFrom: 'Version suivante à partir du',
    permalink: 'Lien permanent',
    noneHeld: 'aucune détenue',
    notHeldRow: '{item} : {reason}',
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

/** Every entry of a table as `[path, text]`, in the table's order. */
export function entriesOf(table, path = []) {
  return Object.entries(table).flatMap(([key, value]) => (typeof value === 'string' ? [[[...path, key].join('.'), value]] : entriesOf(value, [...path, key])));
}

/** The draft beside the English, as Markdown, for review. */
export function renderChromeDraft() {
  const french = new Map(entriesOf(LIVE_CHROME_FR_DRAFT));
  return [
    '# The live pages\' interface copy: English, and the French draft for review',
    '',
    'Decision 41: French interface copy ships only once reviewed. Printed by `node web/scripts/live-chrome-fr-draft.mjs`',
    'from the English table the pages render (`live-chrome.mjs`) and the draft beside it.',
    '',
    '| Entry | English (served) | French (draft) |',
    '|-------|------------------|----------------|',
    ...entriesOf(liveChrome('en')).map(([path, english]) => `| \`${path}\` | ${english} | ${french.get(path) ?? '(no draft)'} |`),
    '',
  ].join('\n');
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.stdout.write(renderChromeDraft());
}

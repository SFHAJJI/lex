namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// The French of every answer template, keyed by template id: the finals of the independent French review of 2026-10-28
/// (three rounds, C:\lex-v3\lanes\fr-review-assistant), applied byte for byte, and R50's French, J6's master. Kept apart
/// from the English so the reviewed bytes are one table a later review replaces whole.
/// </summary>
public static partial class V3AnswerTemplates
{
    private const string FrenchReviewer = "Claude (AI reviewer, independent of the drafter), under the owner's delegation; not R43's qualified person";

    // A property, not a field: the parts of a partial class initialise their fields in no stated order, and the set is built
    // from this table during the other part's initialisation.
    private static V3TemplateProvenance FrenchReviewed => new(null, AgentDraft, FrenchReviewer, "2026-10-28", false);

    private static Dictionary<string, (string Text, V3TemplateProvenance Basis)> FrenchTexts() => new(StringComparer.Ordinal)
    {
        ["action.candidate"] = ("{candidate}", FrenchReviewed),
        ["action.open_work"] = ("Ouvrir {work}", FrenchReviewed),
        ["action.read_article"] = ("Lire l’article {anchor} de {work} tel qu’il se lisait le {date}", FrenchReviewed),
        ["action.read_state"] = ("Lire la version de {work} applicable à partir du {date}", FrenchReviewed),
        ["action.read_state_eu"] = ("Lire le libellé de {work} daté du {date}", FrenchReviewed),
        ["boundary.line"] = ("Savoir si ce texte s’applique à votre situation, et comment, dépend de faits qui vous concernent\u00A0; Lex n’applique pas le droit à des cas individuels.", FrenchReviewed),
        ["boundary.no_adviser"] = ("Lex n’indique pas encore qui peut vous conseiller\u00A0: cette liste doit d’abord être vérifiée par une personne.", FrenchReviewed),
        ["boundary.whole"] = ("Je peux vous montrer exactement ce que dit le texte publié, à toute date, et comment il a changé, citations à l’appui. Je ne peux pas vous dire s’il s’applique à votre situation, ni comment\u00A0: cela dépend de faits qui vous concernent, et ce service n’applique pas le droit à des cas individuels. Voici le texte en cause tel qu’il se lisait le {date}, et voici qui peut vous conseiller.", FrenchReviewed),
        ["boundary.whole_no_adviser"] = ("Je peux vous montrer exactement ce que dit le texte publié, à toute date, et comment il a changé, citations à l’appui. Je ne peux pas vous dire s’il s’applique à votre situation, ni comment\u00A0: cela dépend de faits qui vous concernent, et ce service n’applique pas le droit à des cas individuels. Voici le texte en cause tel qu’il se lisait le {date}. Lex n’indique pas encore qui peut vous conseiller\u00A0: cette liste doit d’abord être vérifiée par une personne.", FrenchReviewed),
        ["boundary.whole_no_text"] = ("Je peux vous montrer exactement ce que dit le texte publié, à toute date, et comment il a changé, citations à l’appui. Je ne peux pas vous dire s’il s’applique à votre situation, ni comment\u00A0: cela dépend de faits qui vous concernent, et ce service n’applique pas le droit à des cas individuels. Lex n’indique pas encore qui peut vous conseiller\u00A0: cette liste doit d’abord être vérifiée par une personne.", FrenchReviewed),
        ["clarify.which_act"] = ("Vos mots figurent dans plus d’un acte que Lex détient. De quel acte s’agit-il\u00A0?", FrenchReviewed),
        ["clarify.which_article"] = ("Vos mots figurent dans plus d’un article de {work}. De quel article s’agit-il\u00A0?", FrenchReviewed),
        ["clarify.which_article_of_act"] = ("De quel article de {work} s’agit-il\u00A0?", FrenchReviewed),
        ["clarify.which_identifier"] = ("Ce que nomme votre question peut désigner plus d’un acte. Duquel s’agit-il\u00A0?", FrenchReviewed),
        ["clarify.which_state"] = ("Plus d’une version que Lex détient couvre le {date}. De laquelle s’agit-il\u00A0?", FrenchReviewed),
        ["clarify.which_wording_eu"] = ("Plus d’un libellé que Lex détient couvre le {date}. Duquel s’agit-il\u00A0?", FrenchReviewed),
        ["disclosure.authenticity_eu"] = ("Toutes les versions linguistiques officielles de l’acte tel que publié au Journal officiel font également foi. Ce texte consolidé, quelle que soit sa langue, est un outil de documentation sans effet juridique.", FrenchReviewed),
        ["disclosure.authenticity_eu_original"] = ("Toutes les versions linguistiques officielles de l’acte tel que publié au Journal officiel font également foi. Ceci est la copie que Lex donne du texte tel que publié, à titre d’information\u00A0; elle ne fait pas foi.", FrenchReviewed),
        ["disclosure.authenticity_lu"] = ("Ceci est la copie que Lex donne du texte, à titre d’information\u00A0; elle ne fait pas foi. Seuls font foi les textes publiés au Journal officiel (Mémorial A)\u00A0: l’acte tel que publié, {uri}.", FrenchReviewed),
        ["disclosure.corrigendum"] = ("Un rectificatif de cet acte existe\u00A0; sa date et ses langues figurent dans la fiche de l’acte.", FrenchReviewed),
        ["disclosure.derogation"] = ("Un autre acte a dérogé temporairement à cette disposition\u00A0; Lex ne détient pas encore les dates de l’éditeur pour cette dérogation.", FrenchReviewed),
        ["disclosure.no_legal_effect"] = ("Un texte consolidé est un outil de documentation sans effet juridique.", FrenchReviewed),
        ["disclosure.pre_application"] = ("À cette date, la date d’entrée en vigueur de l’acte était atteinte, mais pas encore celle de sa mise en application.", FrenchReviewed),
        ["disclosure.provisional"] = ("Cette version s’applique à partir du {date}, après le jour où l’index de Lex a été construit\u00A0; Lex n’utilise jamais de version ultérieure pour aujourd’hui.", FrenchReviewed),
        ["disclosure.title_distrust"] = ("Les intitulés sont indiqués tels que l’éditeur les a écrits\u00A0; Lex ne les vérifie pas par rapport aux dates de l’acte.", FrenchReviewed),
        ["disclosure.validity_conflict"] = ("L’éditeur date cet article du {date}, une date différente de la date propre de la version\u00A0; les deux dates sont indiquées.", FrenchReviewed),
        ["handoff.advice"] = ("Qui peut vous conseiller\u00A0: {name}, {uri}", FrenchReviewed),
        ["handoff.complaint"] = ("Où introduire une réclamation\u00A0: {name}, {uri}", FrenchReviewed),
        ["handoff.information"] = ("Information\u00A0: {name}, {uri}", FrenchReviewed),
        ["handoff.mediation"] = ("Médiation\u00A0: {name}, {uri}", FrenchReviewed),
        ["handoff.official_source"] = ("Source officielle\u00A0: {name}, {uri}", FrenchReviewed),
        ["law.content_hash"] = ("Empreinte du contenu\u00A0: {hash}", FrenchReviewed),
        ["law.dated_by_entry_into_force"] = ("Version datée de son entrée en vigueur le {date} (éditeur), et non d’une date d’applicabilité.", FrenchReviewed),
        ["law.eu_original_wording"] = ("Libellé tel que publié au Journal officiel, daté du {from}.", FrenchReviewed),
        ["law.interval_eu"] = ("Libellé consolidé du {from} au {to}.", FrenchReviewed),
        ["law.interval_eu_open"] = ("Libellé consolidé à partir du {from}.", FrenchReviewed),
        ["law.interval_lu"] = ("Applicable du {from} au {to} (éditeur).", FrenchReviewed),
        ["law.interval_lu_open"] = ("Applicable à partir du {from} (éditeur).", FrenchReviewed),
        ["law.official_source"] = ("Source officielle\u00A0: {uri}", FrenchReviewed),
        ["law.permalink"] = ("Permalien\u00A0: {uri}", FrenchReviewed),
        ["law.quote_heading"] = ("{work}, article {anchor}, tel qu’il se lisait le {date}", FrenchReviewed),
        ["law.quote_shortened"] = ("L’article se poursuit au-delà de cette citation\u00A0; lisez-le en entier à l’adresse {uri}.", FrenchReviewed),
        ["notice.assembled_by_model"] = ("Un modèle d’IA a assemblé cette réponse\u00A0: il a planifié les opérations de Lex à exécuter. Le code de Lex a vérifié ce plan, exécuté les opérations et écrit chaque phrase à partir de formules fixes\u00A0; les citations reproduisent le texte publié.", FrenchReviewed),
        ["notice.input"] = ("Ceci est un assistant automatisé qui montre des textes juridiques publiés. Il ne donne pas de conseil juridique et n’applique pas le droit à votre situation. N’écrivez ni noms, ni données de santé, ni autres données personnelles.", FrenchReviewed),
        ["notice.not_legal_advice"] = ("Lex montre le texte de la loi. Il ne dit pas ce qui s’applique à votre situation, et ce n’est pas un conseil juridique.", Master("R50 (J6)")),
        ["notice.preview"] = ("Phase d’essai\u00A0: l’assistant n’a pas encore été examiné par un avocat ni par des annotateurs indépendants.", FrenchReviewed),
        ["plan.date_today"] = ("Votre question ne donne pas de date\u00A0: Lex a lu le droit tel qu’il se lit aujourd’hui, le {date}.", FrenchReviewed),
        ["plan.follow_up.single_article"] = ("Comme cette étape désignait un seul article, Lex a effectué l’étape suivante\u00A0:", FrenchReviewed),
        ["plan.follow_up.single_work"] = ("Comme cette étape désignait un seul acte, Lex a effectué l’étape suivante\u00A0:", FrenchReviewed),
        ["plan.heading"] = ("Ce que Lex a fait de votre question", FrenchReviewed),
        ["plan.split"] = ("Lex a lu deux questions dans la vôtre\u00A0: «\u00A0{first}\u00A0» et «\u00A0{second}\u00A0».", FrenchReviewed),
        ["plan.step.article_history"] = ("Liste des libellés de l’article {anchor} de {identifier} que Lex détient.", FrenchReviewed),
        ["plan.step.as_of"] = ("Lecture de {identifier} dans son état du {date}.", FrenchReviewed),
        ["plan.step.changes_in_period"] = ("Liste de ce que l’éditeur a modifié entre le {date_from} et le {date_to}, tel que Lex le détient.", FrenchReviewed),
        ["plan.step.diff"] = ("Comparaison de {identifier} entre son état du {date_from} et son état du {date_to}.", FrenchReviewed),
        ["plan.step.dossier"] = ("Ouverture de la fiche de {identifier}.", FrenchReviewed),
        ["plan.step.evidence_bundle"] = ("Lecture du texte de {identifier} tel qu’il se lisait le {date}.", FrenchReviewed),
        ["plan.step.evidence_bundle_article"] = ("Lecture de l’article {anchor} de {identifier} tel qu’il se lisait le {date}.", FrenchReviewed),
        ["plan.step.resolve"] = ("Recherche de «\u00A0{identifier}\u00A0».", FrenchReviewed),
        ["plan.step.search"] = ("Recherche de vos mots dans le texte que Lex détient en {language}\u00A0: «\u00A0{query}\u00A0».", FrenchReviewed),
        ["plan.step.search_dated"] = ("Recherche de vos mots dans le texte que Lex détient en {language}, tel qu’il se lisait le {date}\u00A0: «\u00A0{query}\u00A0».", FrenchReviewed),
        ["plan.step.timeline"] = ("Liste des versions de {identifier} que Lex détient.", FrenchReviewed),
        ["plan.step.timeline_eu"] = ("Liste des libellés de {identifier} que Lex détient.", FrenchReviewed),
        ["point.no_hit"] = ("Lex n’a trouvé aucun article contenant vos mots «\u00A0{query}\u00A0» dans le texte qu’il détient. Cela indique ce que Lex détient, et non ce que contient le droit.", FrenchReviewed),
        ["point.not_held"] = ("Lex ne détient aucun texte qui y réponde. Les sources officielles ci-dessous détiennent le droit publié.", FrenchReviewed),
        ["presentation.assistant_unavailable"] = ("L’assistant est désactivé sur Lex\u00A0; la recherche, la lecture et les autres opérations fonctionnent.", FrenchReviewed),
        ["presentation.localization_unavailable"] = ("L’assistant ne répond qu’en français et en anglais.", FrenchReviewed),
        ["refusal.anchor_not_in_version"] = ("Lex ne détient aucun article {anchor} dans cette version. Les articles les plus proches qu’il y détient sont indiqués ci-dessous.", FrenchReviewed),
        ["refusal.identifier_unknown"] = ("Lex ne reconnaît pas «\u00A0{identifier}\u00A0» comme un acte qu’il détient. Cela indique ce que Lex détient, et non si l’acte existe.", FrenchReviewed),
        ["refusal.no_version_for_date"] = ("Lex ne détient aucune version de {work} applicable le {date}. L’absence de version détenue n’est pas un élément de preuve que le droit n’a pas changé.", FrenchReviewed),
        ["refusal.no_version_for_date_eu"] = ("Lex ne détient aucun libellé de {work} pour le {date}. L’absence de libellé détenu n’est pas un élément de preuve que le droit n’a pas changé.", FrenchReviewed),
        ["refusal.other"] = ("Lex n’a pas répondu à cette étape ({code})\u00A0; les détails ci-dessous disent pourquoi.", FrenchReviewed),
        ["refusal.out_of_corpus_scope"] = ("Cela ne fait pas partie du droit que Lex détient. La source officielle ci-dessous le détient.", FrenchReviewed),
        ["refusal.text_not_available"] = ("Lex détient la notice de cet acte, mais pas son texte. La source officielle ci-dessous le contient.", FrenchReviewed),
        ["refusal.text_withheld"] = ("Lex détient ce texte mais ne l’affiche pas, car sa licence n’a pas été validée. La source officielle ci-dessous l’affiche.", FrenchReviewed),
        ["verdict.answer"] = ("D’après le texte officiel", FrenchReviewed),
        ["verdict.clarify"] = ("Il manque une précision", FrenchReviewed),
        ["verdict.point"] = ("Non détenu par Lex\u00A0: source officielle", FrenchReviewed),
        ["verdict.refuse"] = ("Hors de ce que fait ce service", FrenchReviewed),
        ["verdict.split"] = ("Deux questions", FrenchReviewed),
    };
}

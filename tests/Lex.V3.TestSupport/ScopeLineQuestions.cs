namespace Lex.V3.TestSupport;

/// <summary>
/// The eighteen questions the pack puts to the scope line, as the reader typed them: number, language
/// and wording. <c>ScopeLineQuestionTests</c> owns what the rule requires of each and asserts that its
/// cases carry exactly these wordings; the ingest suite puts every one of them to the mounted
/// <c>ask</c> route, which is why the wordings live here and not in either suite alone.
/// </summary>
public static class ScopeLineQuestions
{
    public static readonly (int Number, string Language, string Question)[] Eighteen =
    [
        (1, "fr", "Que disait l'art. L. 121-6 le 15 mars 2021?"),
        (2, "en", "Can I be fired while on sick leave?"),
        (3, "de", "Kann ich in der Probezeit gekündigt werden, und wie lang darf sie sein?"),
        (4, "pt", "Quantos meses de caução pode pedir o senhorio?"),
        (5, "lb", "Wéi laang ass de Congé parental?"),
        (6, "en", "How many days can I telework from France before my taxes change?"),
        (7, "fr", "Combien de points pour un excès de vitesse?"),
        (8, "en", "Does Regulation 261/2004 cover my cancelled flight?"),
        (9, "en", "What did the AML law require in June 2020?"),
        (10, "en", "Was the GDPR applicable in May 2017?"),
        (11, "fr", "Le plafond de garantie locative de 2024 s'applique-t-il à mon bail de 2022?"),
        (12, "fr", "Quelles lois ont changé en 2015?"),
        (13, "en", "Which LU laws are in force today in financial regulation?"),
        (14, "fr", "Jusqu'à quelle semaine l'IVG est-elle légale?"),
        (15, "fr", "Quel est le salaire social minimum aujourd'hui?"),
        (16, "fr", "Peut-on licencier une salariée enceinte?"),
        (17, "en", "What does CSSF Circular 22/806 require?"),
        (18, "en", "Has the answer you gave me in March drifted?"),
    ];
}

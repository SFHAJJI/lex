using Lex.V3.Ingest.Names;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// R92(1)'s classes and detector (<see cref="PrivateIndividuals"/>) over synthetic text only: every name, date, place and
/// number below is invented for the test ("Exemple", "Fictif", "Testville"), so nothing here is a person's data. Each class
/// has its positives and the general acts beside it that must stay general; the detector has the particulars it must find
/// next to a person and the general provisions it must leave alone (a label with no value, a residence rule, a variable
/// written "M.", an office named after an honorific, a form's empty fields), which are the shapes the population mount's
/// served text showed it (STATUS-DATA.md, the measurement).
/// </summary>
[TestClass]
public sealed class V3NamesDetectorTests
{
    [TestMethod]
    public void ALuxembourgActGrantingNaturalisationIsItsClassWithOrWithoutANameInItsTitle()
    {
        foreach (var title in new[]
        {
            "Loi du 3 mars 1880 qui accorde la naturalisation au sieur Fictif, de Testville.",
            "Loi du 3 mars 1890, accordant la naturalisation à M. Jean Exemple, cultivateur à Testville.",
            "Loi du 3 mars 1975 conférant la naturalisation.",
            "Loi du 3 mars 1880 par laquelle la naturalisation est accordée au sieur Fictif.",
            "Loi du 3 mars 1880 qui accorde au sieur Fictif un nouveau délai de trois mois pour accepter sa naturalisation.",
            "Loi du 3 mars 1880 qui accorde la naturalistaion au sieur Fictif.",
            "Loi du 3 mars 1950 accordant la nationalité luxembourgeoise à Mme Anne Exemple.",
        })
        {
            Assert.AreEqual(PrivateIndividuals.LuxembourgNaturalisation, PrivateIndividuals.LuxembourgClass([title]), title);
        }

        foreach (var title in new[]
        {
            "Loi du 9 janvier 1878 sur les naturalisations.",
            "Loi du 23 avril 1934 sur l'indigénat luxembourgeois.",
            "Loi du 8 mars 2017 sur la nationalité luxembourgeoise.",
            "Loi du 7 juin 1989 relative à la transposition des noms et prénoms des personnes qui acquièrent ou recouvrent la nationalité luxembourgeoise.",
            "Loi du 1er décembre 1880 ayant pour objet de majorer les taxes prévues par la loi du 9 janvier 1878 sur les naturalisations.",
        })
        {
            Assert.AreEqual(PrivateIndividuals.LuxembourgGeneral, PrivateIndividuals.LuxembourgClass([title]), title);
        }
    }

    [TestMethod]
    public void TheOtherLuxembourgClassesNeedTheirTopicAndAPersonNamedInOneTitle()
    {
        var cases = new (string Title, string Class)[]
        {
            ("Loi du 3 mars 1880 conférant un titre de baron à M. Jean Exemple.", PrivateIndividuals.LuxembourgNobility),
            ("Loi du 3 mars 1880, accordant une pension supplémentaire au sieur Fictif, ex-chapelain de Testville.", PrivateIndividuals.LuxembourgGrantToNamedPerson),
            ("Loi du 3 mars 1880 accordant une dispense d'âge au sieur Fictif.", PrivateIndividuals.LuxembourgGrantToNamedPerson),
            ("Loi du 3 mars 1880 autorisant un échange de propriétés entre la commune de Testville et le sieur Fictif.", PrivateIndividuals.LuxembourgPropertyOfNamedPerson),
            ("Loi du 3 mars 1880 concernant la donation faite par les époux Fictif à la ville de Testville.", PrivateIndividuals.LuxembourgPropertyOfNamedPerson),
            ("Loi du 3 mars 1880 portant changement du nom de M. Jean Exemple.", PrivateIndividuals.LuxembourgCivilStatusOfNamedPerson),
            ("Loi du 3 mars 1880 appliquant en faveur de M. Jean Exemple l'exception prévue par l'article 1er.", PrivateIndividuals.LuxembourgTitleNamesPerson),
            // General acts of the same topics: no person named, so no class.
            ("Règlement grand-ducal du 3 mars 2020 portant adaptation des pensions minima.", PrivateIndividuals.LuxembourgGeneral),
            ("Loi du 3 mars 2020 relative à la cession d'un terrain domanial au fonds de compensation.", PrivateIndividuals.LuxembourgGeneral),
            ("Loi du 3 mars 2020 sur le changement du nom et des prénoms.", PrivateIndividuals.LuxembourgGeneral),
            ("Règlement grand-ducal du 3 mars 2020 relatif aux aides en faveur de la commercialisation de produits agricoles.", PrivateIndividuals.LuxembourgGeneral),
            ("Loi du 3 mars 2020 portant nomination de Monsieur le Ministre de la Justice.", PrivateIndividuals.LuxembourgGeneral),
        };
        foreach (var (title, expected) in cases)
        {
            Assert.AreEqual(expected, PrivateIndividuals.LuxembourgClass([title]), title);
        }

        Assert.AreEqual(PrivateIndividuals.LuxembourgGeneral, PrivateIndividuals.LuxembourgClass([]), "an act with no title held is general");
        Assert.AreEqual(PrivateIndividuals.LuxembourgNaturalisation,
            PrivateIndividuals.LuxembourgClass(["Loi du 3 mars 1975.", "Loi du 3 mars 1975 conférant la naturalisation."]),
            "any one of an act's titles gives it its class");
    }

    [TestMethod]
    [DataRow("Loi du 3 mars 1910 concernant l'acquisition par l'État d'un terrain sis à Testville de MM. Exemple et consorts.", PrivateIndividuals.LuxembourgPropertyOfNamedPerson)]
    [DataRow("Loi du 3 mars 1855 qui accorde une pension viagère à M. Exemple, ancien instituteur.", PrivateIndividuals.LuxembourgGrantToNamedPerson)]
    [DataRow("Arrêté grand-ducal du 3 mars 1900 accordant une pension à Mme Exemple.", PrivateIndividuals.LuxembourgGrantToNamedPerson)]
    [DataRow("Loi du 3 mars 1900 accordant une pension à Mme EXEMPLE.", PrivateIndividuals.LuxembourgGrantToNamedPerson)]
    [DataRow("Loi du 3 mars 1900 autorisant la vente d'un terrain aux héritiers Exemple et consorts.", PrivateIndividuals.LuxembourgPropertyOfNamedPerson)]
    [DataRow("Loi du 3 mars 1900 qui accorde une pension viagère à M. le Ministre d'État honoraire.", PrivateIndividuals.LuxembourgGeneral)]
    [DataRow("Loi du 3 mars 2020 fixant les pensions des fonctionnaires de l'État et des communes.", PrivateIndividuals.LuxembourgGeneral)]
    [DataRow("Règlement grand-ducal du 3 mars 2020 portant nomination de M. le Président du Conseil.", PrivateIndividuals.LuxembourgGeneral)]
    [DataRow("Loi du 3 mars 2020 relative à l'acquisition par l'État d'immeubles pour les besoins de l'Administration des Ponts.", PrivateIndividuals.LuxembourgGeneral)]
    public void ATitleNamesAPersonByAnHonorificAndOneNameOrEtConsorts(string title, string expected)
    {
        // The review of #1001, F2: a title names its people by their surname ("M. Exemple", "MM. Exemple et consorts").
        Assert.AreEqual(expected, PrivateIndividuals.LuxembourgClass([title]), title);
    }

    [TestMethod]
    public void TheDetectorFindsAPersonsParticularsNextToTheirNameOrNextToOneAnother()
    {
        var cases = new (string Text, string[] Codes)[]
        {
            ("La naturalisation est conférée à Monsieur Jean Exemple, né le 1er janvier 1950 à Testville, demeurant à Testville.",
                [PrivateIndividuals.BirthDateCode, PrivateIndividuals.BirthPlaceCode, PrivateIndividuals.DomicileCode, PrivateIndividuals.PersonNamedCode]),
            // A list with no honorific: two particulars of one person.
            ("1. Exemple, Jean, né le 01.01.1950 à Testville ;",
                [PrivateIndividuals.BirthDateCode, PrivateIndividuals.BirthPlaceCode]),
            // A sanctions annex's entry, in English.
            ("Name: Ivan FICTIF. Date of birth: 1.1.1960. Place of birth: Testville. Passport No: AB1234567.",
                [PrivateIndividuals.BirthDateCode, PrivateIndividuals.BirthPlaceCode, PrivateIndividuals.IdentityDocumentCode]),
            ("Mme Anne Exemple, demeurant à Testville, est autorisée à exercer.",
                [PrivateIndividuals.DomicileCode, PrivateIndividuals.PersonNamedCode]),
            ("Le sieur Fictif, né à Testville, est dispensé de l'astreinte.",
                [PrivateIndividuals.BirthPlaceCode, PrivateIndividuals.PersonNamedCode]),
            ("Mr John Fictive, born on 2 March 1961, national identification number: 1961030212345.",
                [PrivateIndividuals.BirthDateCode, PrivateIndividuals.NationalNumberCode, PrivateIndividuals.PersonNamedCode]),
        };
        foreach (var (text, codes) in cases)
        {
            CollectionAssert.AreEqual(codes, PrivateIndividuals.DetectText(text).ToArray(), text);
        }
    }

    [TestMethod]
    public void TheDetectorLeavesGeneralProvisionsAlone()
    {
        foreach (var text in new[]
        {
            // Labels with no value, as registers and forms name them.
            "Le registre contient les nom, prénoms, date et lieu de naissance de la personne concernée.",
            "Date de naissance :… Lieu de naissance : Catégorie de permis :…",
            "the controller shall record the name, date of birth and place of birth of the data subject",
            // A residence rule.
            "Toute personne résidant au Grand-Duché de Luxembourg qui est victime d'une infraction.",
            // A variable written M. (the effective maturity of a model), not a person.
            "the effective credit duration estimated by the internal model as M. Notwithstanding paragraph 1, for netting sets",
            // Offices after an honorific.
            "Monsieur le Ministre de la Justice et Madame la Présidente du Conseil d'État sont chargés de l'exécution.",
            "Mr President, the Council shall act.",
            // A naming rule's placeholders: a person named, and no particular.
            "Les noms doivent être libellés de la façon suivante : « Anne Exemple épouse Fictif ».",
            // A general rule with a date of birth and no person.
            "Les personnes nées le 1er janvier 1960 ou après ont droit à la pension.",
            "",
        })
        {
            Assert.IsEmpty(PrivateIndividuals.DetectText(text), text);
        }
    }

    [TestMethod]
    public void TwoParticularsTooFarApartAreNoFinding()
    {
        var gap = new string(' ', PrivateIndividuals.Window + 10);
        Assert.IsEmpty(PrivateIndividuals.DetectText("Monsieur Jean Exemple est nommé." + gap + "Toute personne née le 1er janvier 1950 est concernée."));
        Assert.IsNotEmpty(PrivateIndividuals.DetectText("Monsieur Jean Exemple, né le 1er janvier 1950."));
    }

    [TestMethod]
    public void TheSignalsSayWhatTheDetectorSawAndSetAside()
    {
        CollectionAssert.AreEqual(
            new[] { PrivateIndividuals.BirthLabelSignal },
            PrivateIndividuals.SignalsIn("les nom, prénoms, date et lieu de naissance").ToArray());
        CollectionAssert.AreEqual(
            new[] { PrivateIndividuals.BirthLabelSignal, PrivateIndividuals.BirthPlaceCode },
            PrivateIndividuals.SignalsIn("Date de naissance :… Lieu de naissance : Catégorie").ToArray());
        Assert.IsEmpty(PrivateIndividuals.SignalsIn("Le présent règlement entre en vigueur le jour de sa publication."));
    }

    [TestMethod]
    public void AnEuActFreezingTheFundsOfThePersonsItsAnnexListsIsARestrictiveMeasure()
    {
        Assert.IsTrue(PrivateIndividuals.FreezesListedPersons(
            "All funds and economic resources belonging to, owned, held or controlled by the natural or legal persons, entities or bodies listed in Annex I shall be frozen."));
        Assert.IsTrue(PrivateIndividuals.FreezesListedPersons(
            "Sont gelés tous les fonds et ressources économiques appartenant aux personnes physiques ou morales énumérées à l'annexe I."));
        // The Treaty's own provision on restrictive measures freezes nothing.
        Assert.IsFalse(PrivateIndividuals.FreezesListedPersons(
            "Where a decision provides for the interruption of economic and financial relations, the Council shall adopt the necessary measures, including restrictive measures against natural or legal persons."));
        Assert.IsFalse(PrivateIndividuals.FreezesListedPersons("Member States shall ensure that the accounts are frozen where the court so orders."));
    }

    [TestMethod]
    public void ALuxembourgSubjectBelongsToItsWorkIri()
    {
        Assert.AreEqual("http://data.legilux.public.lu/eli/etat/leg/loi/1880/03/03/n1",
            PrivateIndividuals.LuxembourgWorkIriOf("http://data.legilux.public.lu/eli/etat/leg/loi/1880/03/03/n1/jo/fr"));
        Assert.AreEqual("http://data.legilux.public.lu/eli/etat/leg/rgd/2020/03/03/a1",
            PrivateIndividuals.LuxembourgWorkIriOf("http://data.legilux.public.lu/eli/etat/leg/rgd/2020/03/03/a1/consolide/20240101/fr"));
        Assert.AreEqual("http://data.legilux.public.lu/eli/etat/leg/code/civil",
            PrivateIndividuals.LuxembourgWorkIriOf("http://data.legilux.public.lu/eli/etat/leg/code/civil/20160901/fr"));
        Assert.AreEqual("http://data.legilux.public.lu/eli/etat/leg/loi/1880/03/03/n1",
            PrivateIndividuals.LuxembourgWorkIriOf("http://data.legilux.public.lu/eli/etat/leg/loi/1880/03/03/n1"));
        Assert.IsNull(PrivateIndividuals.LuxembourgWorkIriOf("http://data.legilux.public.lu/eli/etat/leg/memorial/1880/a12"));
        Assert.IsNull(PrivateIndividuals.LuxembourgWorkIriOf("https://example.invalid/eli/etat/leg/loi/1880/03/03/n1"));
    }

    [TestMethod]
    public void TheClassesAndCodesAreOneClosedVocabulary()
    {
        CollectionAssert.AreEqual(PrivateIndividuals.Classes.Order(StringComparer.Ordinal).ToArray(), PrivateIndividuals.Classes.ToArray());
        CollectionAssert.AreEqual(PrivateIndividuals.ReasonCodes.Order(StringComparer.Ordinal).ToArray(), PrivateIndividuals.ReasonCodes.ToArray());
        Assert.IsTrue(PrivateIndividuals.NamingClasses.All(PrivateIndividuals.Classes.Contains));
        Assert.IsFalse(PrivateIndividuals.NamingClasses.Contains(PrivateIndividuals.LuxembourgGeneral));
        Assert.IsFalse(PrivateIndividuals.NamingClasses.Contains(PrivateIndividuals.EuropeGeneral));
    }
}

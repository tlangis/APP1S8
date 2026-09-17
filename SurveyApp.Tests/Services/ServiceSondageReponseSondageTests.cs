using Newtonsoft.Json;
using SurveyApp.Models;
using SurveyApp.Services;
using SurveyApp.Tests.Infrastructure;

namespace SurveyApp.Tests.Services;

/// <summary>
/// Couvre <c>ReponseSondage</c> : les cinq codes de retour (0, -1, -2, -3, -4), les deux
/// branches de chargement des fichiers JSON (vide / peuplé), et la persistance.
/// </summary>
public class ServiceSondageReponseSondageTests
{
    private static ServiceSondage ServiceAvecTroisSondages(DossierDonneesTemporaire donnees)
    {
        donnees.EcrireSondage(SondagesDeTest.TroisSondages);
        return new ServiceSondage();
    }

    // ---------------------------------------------------------------- cas nominal (code 0)

    [Fact]
    public void ReponseSondage_ReponseValide_RetourneZero()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var code = service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        Assert.Equal(0, code);
    }

    [Fact]
    public void ReponseSondage_ReponseValide_EnregistreLEmpreinteDuParticipant()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        var participants = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(
            donnees.LireParticipants());

        Assert.NotNull(participants);
        var empreinte = Empreinte.De("participant-a");
        Assert.True(participants.ContainsKey(empreinte));
        Assert.Equal(new[] { 1 }, participants[empreinte]);

        // La clé en clair ne doit jamais se retrouver dans le fichier des participants.
        Assert.DoesNotContain("participant-a", donnees.LireParticipants());
    }

    /// <summary>
    /// CONSTAT DE SÉCURITÉ (réf. REVISION-SECURITE.md) : la clé participant est hachée dans
    /// <c>participants.json</c> mais stockée EN CLAIR dans <c>reponsesRecues.json</c>, à côté
    /// des réponses. L'anonymat visé par le hachage est donc annulé par le second fichier.
    /// Ce test fige le comportement réel pour rendre la régression visible si on le corrige.
    /// </summary>
    [Fact]
    public void ReponseSondage_ReponseValide_StockeLaCleEnClairDansLesReponsesRecues()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        var contenu = donnees.LireReponsesRecues();
        Assert.Contains("participant-a", contenu);

        var reponses = JsonConvert.DeserializeObject<List<Reponse>>(contenu);
        Assert.NotNull(reponses);
        var reponse = Assert.Single(reponses);
        Assert.Equal("participant-a", reponse.cleParticipant);
        Assert.Equal("a", reponse.QR[1]);
        Assert.Equal("b", reponse.QR[2]);
    }

    [Fact]
    public void ReponseSondage_FichiersDejaPeuples_AjouteSansEcraser()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        // Branche « désérialisation non nulle » des deux fichiers : ils contiennent déjà du JSON.
        donnees.EcrireParticipants(JsonConvert.SerializeObject(
            new Dictionary<string, List<int>> { [Empreinte.De("participant-existant")] = new() { 1 } }));
        donnees.EcrireReponsesRecues(JsonConvert.SerializeObject(
            new List<Reponse> { ReponsesDeTest.Valide("participant-existant") }));

        var code = service.ReponseSondage(1, ReponsesDeTest.Valide("participant-b"));

        Assert.Equal(0, code);

        var participants = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(
            donnees.LireParticipants());
        Assert.Equal(2, participants!.Count);

        var reponses = JsonConvert.DeserializeObject<List<Reponse>>(donnees.LireReponsesRecues());
        Assert.Equal(2, reponses!.Count);
    }

    /// <summary>
    /// Branche « le participant existe déjà mais n'a pas répondu à CE sondage » : l'id est
    /// ajouté à sa liste existante au lieu de créer une nouvelle entrée.
    /// </summary>
    [Fact]
    public void ReponseSondage_ParticipantConnuAutreSondage_AjouteLIdALaListeExistante()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        Assert.Equal(0, service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a", idSondage: 1)));
        Assert.Equal(0, service.ReponseSondage(2, ReponsesDeTest.Valide("participant-a", idSondage: 2)));

        var participants = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(
            donnees.LireParticipants());

        var entree = Assert.Single(participants!);
        Assert.Equal(Empreinte.De("participant-a"), entree.Key);
        Assert.Equal(new[] { 1, 2 }, entree.Value);
    }

    // ------------------------------------------------------- sondage introuvable (code -4)

    [Fact]
    public void ReponseSondage_SondageInexistant_RetourneMoinsQuatre()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        Assert.Equal(-4, service.ReponseSondage(99, ReponsesDeTest.Valide("participant-a")));
    }

    [Fact]
    public void ReponseSondage_SondageInexistant_NEnregistreRien()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        service.ReponseSondage(99, ReponsesDeTest.Valide("participant-a"));

        Assert.Equal(string.Empty, donnees.LireParticipants());
        Assert.Equal(string.Empty, donnees.LireReponsesRecues());
    }

    // --------------------------------------------- numérotation des questions (code -3)

    [Fact]
    public void ReponseSondage_TropDeQuestions_RetourneMoinsTrois()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = ReponsesDeTest.Avec("participant-a", new Dictionary<int, string>
        {
            [1] = "a",
            [2] = "b",
            [3] = "a",   // le sondage 1 n'a que deux questions
        });

        Assert.Equal(-3, service.ReponseSondage(1, reponse));
    }

    [Fact]
    public void ReponseSondage_PasAssezDeQuestions_RetourneMoinsTrois()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = ReponsesDeTest.Avec("participant-a", new Dictionary<int, string> { [1] = "a" });

        Assert.Equal(-3, service.ReponseSondage(1, reponse));
    }

    [Fact]
    public void ReponseSondage_NumeroDeQuestionHorsSequence_RetourneMoinsTrois()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = ReponsesDeTest.Avec("participant-a", new Dictionary<int, string>
        {
            [1] = "a",
            [5] = "b",   // attendu : 2
        });

        Assert.Equal(-3, service.ReponseSondage(1, reponse));
    }

    /// <summary>
    /// BOGUE CONNU : la validation parcourt <c>QR.Keys</c> dans l'ordre d'INSERTION et exige
    /// 1..n. Une réponse par ailleurs valide, mais dont les clés arrivent dans le désordre, est
    /// rejetée — alors qu'un objet JSON n'a pas d'ordre significatif. Test figeant le réel.
    /// </summary>
    [Fact]
    public void ReponseSondage_QuestionsDansLeDesordre_RetourneMoinsTrois_BogueOrdreDInsertion()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var qr = new Dictionary<int, string>();
        qr[2] = "b";   // insérée en premier
        qr[1] = "a";

        Assert.Equal(-3, service.ReponseSondage(1, ReponsesDeTest.Avec("participant-a", qr)));
    }

    [Fact]
    public void ReponseSondage_QrVide_RetourneMoinsTrois()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = ReponsesDeTest.Avec("participant-a", new Dictionary<int, string>());

        // La boucle de numérotation ne s'exécute pas ; c'est la comparaison des effectifs
        // (0 contre 2 questions) qui rejette.
        Assert.Equal(-3, service.ReponseSondage(1, reponse));
    }

    // ------------------------------------------------------ choix invalide (code -1)

    [Fact]
    public void ReponseSondage_LettreDeChoixInconnue_RetourneMoinsUn()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = ReponsesDeTest.Avec("participant-a", new Dictionary<int, string>
        {
            [1] = "a",
            [2] = "z",   // « z » n'est pas un choix du sondage
        });

        Assert.Equal(-1, service.ReponseSondage(1, reponse));
    }

    [Fact]
    public void ReponseSondage_ChoixInvalide_NEnregistreRien()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = ReponsesDeTest.Avec("participant-a", new Dictionary<int, string>
        {
            [1] = "z",
            [2] = "z",
        });

        service.ReponseSondage(1, reponse);

        Assert.Equal(string.Empty, donnees.LireParticipants());
        Assert.Equal(string.Empty, donnees.LireReponsesRecues());
    }

    /// <summary>
    /// La comparaison des lettres est sensible à la casse : « A » n'est pas « a ».
    /// </summary>
    [Fact]
    public void ReponseSondage_LettreEnMajuscule_RetourneMoinsUn()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = ReponsesDeTest.Avec("participant-a", new Dictionary<int, string>
        {
            [1] = "A",
            [2] = "b",
        });

        Assert.Equal(-1, service.ReponseSondage(1, reponse));
    }

    // --------------------------------------------------------- déjà répondu (code -2)

    [Fact]
    public void ReponseSondage_MemeCleDeuxFoisMemeSondage_RetourneMoinsDeux()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        Assert.Equal(0, service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a")));
        Assert.Equal(-2, service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a")));
    }

    [Fact]
    public void ReponseSondage_DeuxiemeTentative_NAjoutePasDeReponse()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));
        service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        var reponses = JsonConvert.DeserializeObject<List<Reponse>>(donnees.LireReponsesRecues());
        Assert.Single(reponses!);
    }

    /// <summary>
    /// CONSTAT DE SÉCURITÉ (réf. REVISION-SECURITE.md, constat V-01) : l'unicité repose sur une
    /// clé CHOISIE PAR L'APPELANT dans le corps de la requête. Elle n'est ni émise ni vérifiée
    /// par le serveur, donc n'importe qui peut répondre autant de fois qu'il veut en changeant
    /// de chaîne. Le livrable 4 (authentification du participant) n'est pas atteint.
    /// Ce test DOCUMENTE la faille ; il devra être inversé quand une vraie authentification
    /// sera en place.
    /// </summary>
    [Fact]
    public void ReponseSondage_ClesInventees_PermettentDeRepondreIndefiniment_FailleV01()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        for (var tentative = 0; tentative < 5; tentative++)
        {
            var code = service.ReponseSondage(1, ReponsesDeTest.Valide($"cle-inventee-{tentative}"));
            Assert.Equal(0, code);
        }

        var reponses = JsonConvert.DeserializeObject<List<Reponse>>(donnees.LireReponsesRecues());
        Assert.Equal(5, reponses!.Count);
    }

    // ------------------------------------------------------------ ordre des vérifications

    /// <summary>
    /// L'anti-rejeu est évalué APRÈS la validation du contenu : une réponse invalide envoyée
    /// par un participant ayant déjà répondu donne -1, pas -2.
    /// </summary>
    [Fact]
    public void ReponseSondage_ParticipantConnuAvecReponseInvalide_RetourneMoinsUnAvantMoinsDeux()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        service.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        var invalide = ReponsesDeTest.Avec("participant-a", new Dictionary<int, string>
        {
            [1] = "a",
            [2] = "z",
        });

        Assert.Equal(-1, service.ReponseSondage(1, invalide));
    }

    // ------------------------------------------------------------------- entrées dégradées

    /// <summary>
    /// ROBUSTESSE : le hachage précède toute validation, donc une clé nulle lève avant même
    /// le contrôle d'existence du sondage. En production, la validation de modèle
    /// d'<c>[ApiController]</c> rejette le cas en 400 avant d'atteindre le service — la garde
    /// est accidentelle, pas explicite.
    /// </summary>
    [Fact]
    public void ReponseSondage_CleParticipantNulle_LeveArgumentNull()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = new Reponse
        {
            cleParticipant = null,
            QR = new Dictionary<int, string> { [1] = "a", [2] = "b" },
        };

        Assert.Throws<ArgumentNullException>(() => service.ReponseSondage(1, reponse));
    }

    /// <summary>ROBUSTESSE : un dictionnaire QR nul déréférence null.</summary>
    [Fact]
    public void ReponseSondage_QrNul_LeveNullReference()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = new Reponse { cleParticipant = "participant-a", QR = null };

        Assert.Throws<NullReferenceException>(() => service.ReponseSondage(1, reponse));
    }

    [Fact]
    public void ReponseSondage_CleParticipantVide_EstAcceptee()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        // La chaîne vide est hachable : aucune longueur minimale n'est imposée.
        Assert.Equal(0, service.ReponseSondage(1, ReponsesDeTest.Valide(string.Empty)));
        Assert.Equal(-2, service.ReponseSondage(1, ReponsesDeTest.Valide(string.Empty)));
    }

    /// <summary>
    /// L'<c>IdSondage</c> présent dans le CORPS est totalement ignoré : seul l'id de l'URL
    /// compte. Deux sources d'identité pour la même donnée, dont une morte.
    /// </summary>
    [Fact]
    public void ReponseSondage_IdSondageDuCorpsIncoherent_EstIgnore()
    {
        using var donnees = new DossierDonneesTemporaire();
        var service = ServiceAvecTroisSondages(donnees);

        var reponse = ReponsesDeTest.Valide("participant-a", idSondage: 999);

        Assert.Equal(0, service.ReponseSondage(1, reponse));

        var participants = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(
            donnees.LireParticipants());
        Assert.Equal(new[] { 1 }, participants![Empreinte.De("participant-a")]);
    }
}

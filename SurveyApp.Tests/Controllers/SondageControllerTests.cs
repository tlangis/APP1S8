using Microsoft.AspNetCore.Mvc;
using SurveyApp.Controllers;
using SurveyApp.Models;
using SurveyApp.Tests.Infrastructure;

namespace SurveyApp.Tests.Controllers;

/// <summary>
/// Couvre la traduction, par le contrôleur, des codes sentinelles du service vers des codes
/// HTTP. Le service est doublé : aucun accès disque ici.
/// </summary>
public class SondageControllerTests
{
    private static Sondage UnSondage() => new()
    {
        Id = 1,
        Nom = "Sondage 1",
        Questions = new List<Question>
        {
            new()
            {
                Id = 1,
                Titre = "Aimez-vous le café?",
                Choix = new List<Choix>
                {
                    new() { Lettre = "a", Valeur = "Oui" },
                    new() { Lettre = "b", Valeur = "Non" },
                },
            },
        },
    };

    // ------------------------------------------------------------------------ GET

    [Fact]
    public void GetSondage_ServiceRetourneUnSondage_RepondOkAvecLeSondage()
    {
        var sondage = UnSondage();
        var service = new FauxServiceSondage { SondageARetourner = sondage };
        var controleur = new SondageController(service);

        var resultat = controleur.GetSondage(1);

        var ok = Assert.IsType<OkObjectResult>(resultat.Result);
        Assert.Same(sondage, ok.Value);
    }

    [Fact]
    public void GetSondage_ServiceRetourneNull_RepondNotFound()
    {
        var service = new FauxServiceSondage { SondageARetourner = null };
        var controleur = new SondageController(service);

        var resultat = controleur.GetSondage(42);

        Assert.IsType<NotFoundResult>(resultat.Result);
    }

    [Fact]
    public void GetSondage_TransmetLIdAuService()
    {
        var service = new FauxServiceSondage { SondageARetourner = UnSondage() };
        var controleur = new SondageController(service);

        controleur.GetSondage(7);

        Assert.Equal(7, service.DernierIdSondageDemande);
    }

    // ----------------------------------------------------------------------- POST

    [Fact]
    public void ReponseSondage_CodeZero_RepondOk()
    {
        var service = new FauxServiceSondage { CodeARetourner = 0 };
        var controleur = new SondageController(service);

        var resultat = controleur.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        var ok = Assert.IsType<OkObjectResult>(resultat.Result);

        // Le corps renvoyé est le littéral 0, alors que l'action est déclarée
        // ActionResult<Reponse> : le contrat OpenAPI ne décrit pas ce que l'API renvoie.
        Assert.Equal(0, ok.Value);
    }

    [Fact]
    public void ReponseSondage_CodeMoinsUn_RepondBadRequest()
    {
        var service = new FauxServiceSondage { CodeARetourner = -1 };
        var controleur = new SondageController(service);

        var resultat = controleur.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        Assert.IsType<BadRequestResult>(resultat.Result);
    }

    [Fact]
    public void ReponseSondage_CodeMoinsDeux_RepondConflict()
    {
        var service = new FauxServiceSondage { CodeARetourner = -2 };
        var controleur = new SondageController(service);

        var resultat = controleur.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        Assert.IsType<ConflictResult>(resultat.Result);
    }

    [Theory]
    [InlineData(-3)]   // numérotation des questions invalide
    [InlineData(-4)]   // sondage inexistant
    public void ReponseSondage_CodesMoinsTroisEtMoinsQuatre_RepondentNotFound(int code)
    {
        var service = new FauxServiceSondage { CodeARetourner = code };
        var controleur = new SondageController(service);

        var resultat = controleur.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        // Les deux se confondent en 404 : le client ne peut pas distinguer « sondage absent »
        // d'« ensemble de questions invalide », ce qui rend le diagnostic impossible côté appelant.
        Assert.IsType<NotFoundResult>(resultat.Result);
    }

    /// <summary>
    /// Branche par défaut : tout code non prévu retombe sur 204 No Content. Elle est
    /// inatteignable avec l'implémentation actuelle du service, mais fait partie du contrôleur.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    public void ReponseSondage_CodeInattendu_RepondNoContent(int code)
    {
        var service = new FauxServiceSondage { CodeARetourner = code };
        var controleur = new SondageController(service);

        var resultat = controleur.ReponseSondage(1, ReponsesDeTest.Valide("participant-a"));

        Assert.IsType<NoContentResult>(resultat.Result);
    }

    [Fact]
    public void ReponseSondage_TransmetLIdEtLeCorpsAuService()
    {
        var service = new FauxServiceSondage { CodeARetourner = 0 };
        var controleur = new SondageController(service);
        var reponse = ReponsesDeTest.Valide("participant-a");

        controleur.ReponseSondage(3, reponse);

        Assert.Equal(3, service.DernierIdReponse);
        Assert.Same(reponse, service.DerniereReponseRecue);
    }
}

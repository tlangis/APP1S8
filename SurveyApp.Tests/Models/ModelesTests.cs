using SurveyApp.Models;

namespace SurveyApp.Tests.Models;

/// <summary>
/// Les modèles sont des classes mutables à propriétés automatiques. Ces tests couvrent leurs
/// accesseurs, qui comptent dans la couverture de l'assembly mesurée par coverlet.
/// </summary>
public class ModelesTests
{
    [Fact]
    public void Choix_ExposeLettreEtValeur()
    {
        var choix = new Choix { Lettre = "a", Valeur = "Oui" };

        Assert.Equal("a", choix.Lettre);
        Assert.Equal("Oui", choix.Valeur);
    }

    [Fact]
    public void Question_ExposeIdTitreEtChoix()
    {
        var choix = new List<Choix> { new() { Lettre = "a", Valeur = "Oui" } };
        var question = new Question { Id = 3, Titre = "Aimez-vous le café?", Choix = choix };

        Assert.Equal(3, question.Id);
        Assert.Equal("Aimez-vous le café?", question.Titre);
        Assert.Same(choix, question.Choix);
    }

    [Fact]
    public void Sondage_ExposeIdNomEtQuestions()
    {
        var questions = new List<Question> { new() { Id = 1, Titre = "Q?", Choix = new List<Choix>() } };
        var sondage = new Sondage { Id = 2, Nom = "Sondage 2", Questions = questions };

        Assert.Equal(2, sondage.Id);
        Assert.Equal("Sondage 2", sondage.Nom);
        Assert.Same(questions, sondage.Questions);
    }

    [Fact]
    public void Reponse_ExposeQrCleParticipantEtIdSondage()
    {
        var qr = new Dictionary<int, string> { [1] = "a" };
        var reponse = new Reponse { QR = qr, cleParticipant = "participant-a", IdSondage = 1 };

        Assert.Same(qr, reponse.QR);
        Assert.Equal("participant-a", reponse.cleParticipant);
        Assert.Equal(1, reponse.IdSondage);
    }
}

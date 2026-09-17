using SurveyApp.Models;

namespace SurveyApp.Tests.Infrastructure;

/// <summary>Construction de <see cref="Reponse"/> valides ou volontairement dégradées.</summary>
public static class ReponsesDeTest
{
    /// <summary>Réponse valide à un sondage de deux questions.</summary>
    public static Reponse Valide(string cleParticipant, int idSondage = 1) => new()
    {
        cleParticipant = cleParticipant,
        IdSondage = idSondage,
        QR = new Dictionary<int, string> { [1] = "a", [2] = "b" },
    };

    public static Reponse Avec(string cleParticipant, Dictionary<int, string> qr, int idSondage = 1) => new()
    {
        cleParticipant = cleParticipant,
        IdSondage = idSondage,
        QR = qr,
    };
}

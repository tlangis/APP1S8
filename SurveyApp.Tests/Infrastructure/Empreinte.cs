using System.Security.Cryptography;
using System.Text;

namespace SurveyApp.Tests.Infrastructure;

/// <summary>
/// Reproduit l'empreinte que <c>ServiceSondage</c> utilise comme clé dans
/// <c>participants.json</c> : SHA-256 de la clé participant, en hexadécimal majuscule.
/// Permet aux tests de préparer un fichier de participants déjà peuplé.
/// </summary>
public static class Empreinte
{
    public static string De(string cleParticipant) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cleParticipant)));
}

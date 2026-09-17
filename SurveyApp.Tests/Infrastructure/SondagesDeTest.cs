namespace SurveyApp.Tests.Infrastructure;

/// <summary>
/// Fabriques de contenus <c>sondage.txt</c> respectant le format maison attendu par
/// <c>ServiceSondage.LireFichier</c> : un en-tête « Sondage N: », puis des lignes
/// « N. Titre? a:Valeur, b:Valeur ».
/// </summary>
public static class SondagesDeTest
{
    /// <summary>Deux sondages de deux questions, chacune avec deux choix (a, b).</summary>
    public const string DeuxSondages = """
        Sondage 1:
        1. Aimez-vous le café? a:Oui, b:Non
        2. Buvez-vous du thé? a:Oui, b:Non

        Sondage 2:
        1. Lisez-vous le journal? a:Oui, b:Non
        2. Écoutez-vous la radio? a:Oui, b:Non
        """;

    /// <summary>
    /// Trois sondages : nécessaire pour atteindre les sondages 1 et 2 malgré le décalage
    /// d'indice de <c>GetSondage</c>, qui rend le dernier sondage du fichier inatteignable.
    /// </summary>
    public const string TroisSondages = """
        Sondage 1:
        1. Aimez-vous le café? a:Oui, b:Non
        2. Buvez-vous du thé? a:Oui, b:Non

        Sondage 2:
        1. Lisez-vous le journal? a:Oui, b:Non
        2. Écoutez-vous la radio? a:Oui, b:Non

        Sondage 3:
        1. Question de remplissage? a:Oui, b:Non
        """;

    /// <summary>Un seul sondage à une question, utile aux cas dégénérés.</summary>
    public const string SondageUnique = """
        Sondage 1:
        1. Aimez-vous le café? a:Oui, b:Non
        """;
}

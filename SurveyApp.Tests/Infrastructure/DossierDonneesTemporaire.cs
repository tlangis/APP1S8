using System.Text;

namespace SurveyApp.Tests.Infrastructure;

/// <summary>
/// Crée un bac à sable isolé sur disque, y place un dossier <c>Data</c>, et bascule le
/// répertoire courant du processus dessus pendant la durée du test.
///
/// C'est la seule façon de tester <see cref="SurveyApp.Services.ServiceSondage"/> sans le
/// modifier : le service code en dur des chemins relatifs et n'accepte aucune injection de
/// chemin. Le répertoire courant est restauré et le bac à sable supprimé par
/// <see cref="Dispose"/>.
/// </summary>
public sealed class DossierDonneesTemporaire : IDisposable
{
    private readonly string _repertoireOrigine;

    public DossierDonneesTemporaire()
    {
        _repertoireOrigine = Directory.GetCurrentDirectory();

        Racine = Path.Combine(Path.GetTempPath(), "SurveyAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Racine, "Data"));
        Directory.SetCurrentDirectory(Racine);

        // État de départ identique à celui que Program.cs impose au démarrage : fichiers vides.
        EcrireParticipants(string.Empty);
        EcrireReponsesRecues(string.Empty);
    }

    /// <summary>Chemin absolu du bac à sable.</summary>
    public string Racine { get; }

    public string CheminSondage => Path.Combine(Racine, "Data", "sondage.txt");

    public string CheminParticipants => Path.Combine(Racine, "Data", "participants.json");

    public string CheminReponsesRecues => Path.Combine(Racine, "Data", "reponsesRecues.json");

    public void EcrireSondage(string contenu) =>
        File.WriteAllText(CheminSondage, contenu, new UTF8Encoding(false));

    public void EcrireParticipants(string contenu) =>
        File.WriteAllText(CheminParticipants, contenu, new UTF8Encoding(false));

    public void EcrireReponsesRecues(string contenu) =>
        File.WriteAllText(CheminReponsesRecues, contenu, new UTF8Encoding(false));

    public string LireParticipants() => File.ReadAllText(CheminParticipants);

    public string LireReponsesRecues() => File.ReadAllText(CheminReponsesRecues);

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_repertoireOrigine);

        try
        {
            Directory.Delete(Racine, recursive: true);
        }
        catch (IOException)
        {
            // Un bac à sable temporaire résiduel n'est pas une raison de faire échouer un test.
        }
    }
}

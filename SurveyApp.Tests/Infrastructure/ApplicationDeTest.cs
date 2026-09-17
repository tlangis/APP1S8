using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SurveyApp.Tests.Infrastructure;

/// <summary>
/// Démarre l'application complète (Program.cs, intergiciels, contrôleurs) en mémoire.
///
/// Program.cs utilise des instructions de haut niveau : la classe <c>Program</c> engendrée par
/// le compilateur est <c>internal</c> et n'est donc pas nommable depuis ce projet de test. Plutôt
/// que de modifier le projet testé (ajout d'un <c>InternalsVisibleTo</c>), on construit
/// <see cref="WebApplicationFactory{TEntryPoint}"/> par réflexion sur le type récupéré dans
/// l'assembly de l'application.
///
/// Le répertoire courant est redirigé vers un bac à sable AVANT le démarrage de l'hôte, car
/// Program.cs tronque <c>Data/participants.json</c> et <c>Data/reponsesRecues.json</c> dès son
/// exécution : sans cela, un simple passage des tests effacerait les données du dépôt.
/// </summary>
public sealed class ApplicationDeTest : IDisposable
{
    public const string ClefApiDeAppsettings = "Equipe_Christine_Frechette_Coalition_Avenir_Quebec";

    private readonly DossierDonneesTemporaire _donnees;
    private readonly IDisposable _fabrique;
    private readonly string? _environnementOrigine;

    private ApplicationDeTest(string environnement, string contenuSondage)
    {
        // Le bac à sable doit exister avant que l'hôte ne démarre.
        _donnees = new DossierDonneesTemporaire();
        _donnees.EcrireSondage(contenuSondage);

        _environnementOrigine = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environnement);

        var typeProgram = typeof(SurveyApp.Services.ServiceSondage).Assembly.GetType("Program")
            ?? throw new InvalidOperationException(
                "Type « Program » introuvable dans l'assembly SurveyApp.");

        var typeFabrique = typeof(WebApplicationFactory<>).MakeGenericType(typeProgram);
        _fabrique = (IDisposable)Activator.CreateInstance(typeFabrique)!;

        var creerClient = typeFabrique.GetMethod(
            nameof(WebApplicationFactory<object>.CreateClient),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)!;

        Client = (HttpClient)creerClient.Invoke(_fabrique, null)!;
    }

    public HttpClient Client { get; }

    public DossierDonneesTemporaire Donnees => _donnees;

    public static ApplicationDeTest EnDeveloppement(string? contenuSondage = null) =>
        new("Development", contenuSondage ?? SondagesDeTest.TroisSondages);

    public static ApplicationDeTest EnProduction(string? contenuSondage = null) =>
        new("Production", contenuSondage ?? SondagesDeTest.TroisSondages);

    /// <summary>Client muni de la clé d'API attendue par l'intergiciel.</summary>
    public HttpClient ClientAuthentifie()
    {
        Client.DefaultRequestHeaders.Remove("X-Api-Key");
        Client.DefaultRequestHeaders.Add("X-Api-Key", ClefApiDeAppsettings);
        return Client;
    }

    public void Dispose()
    {
        Client.Dispose();
        _fabrique.Dispose();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _environnementOrigine);
        _donnees.Dispose();
    }
}

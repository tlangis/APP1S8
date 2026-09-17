using SurveyApp.Models;
using SurveyApp.Services;

namespace SurveyApp.Tests.Infrastructure;

/// <summary>
/// Double de test pour <see cref="IServiceSondage"/>. Les tests du contrôleur portent sur la
/// traduction code de retour → code HTTP ; ils n'ont donc pas à toucher au disque.
/// </summary>
public sealed class FauxServiceSondage : IServiceSondage
{
    public Sondage? SondageARetourner { get; set; }

    public int CodeARetourner { get; set; }

    public int? DernierIdSondageDemande { get; private set; }

    public int? DernierIdReponse { get; private set; }

    public Reponse? DerniereReponseRecue { get; private set; }

    public Sondage GetSondage(int id)
    {
        DernierIdSondageDemande = id;
        return SondageARetourner!;
    }

    public int ReponseSondage(int id, Reponse reponse)
    {
        DernierIdReponse = id;
        DerniereReponseRecue = reponse;
        return CodeARetourner;
    }
}

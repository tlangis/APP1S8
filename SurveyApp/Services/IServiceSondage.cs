using SurveyApp.Models;

namespace SurveyApp.Services
{
    public interface IServiceSondage
    {
        Sondage GetSondage(int id);

        int ReponseSondage(int id, Reponse reponse);
    }
}

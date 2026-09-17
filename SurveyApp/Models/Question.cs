namespace SurveyApp.Models
{
    public class Question
    {
        public int Id { get; set; }
        public string Titre { get; set; }
        public List<Choix> Choix { get; set; }
    }
}

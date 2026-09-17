namespace SurveyApp.Models
{
    public class Sondage
    {
        public int Id { get; set; }
        public string Nom { get; set; }
        public List<Question> Questions { get; set; }
    }
}

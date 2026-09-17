using Newtonsoft.Json;
using SurveyApp.Models;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SurveyApp.Services
{
    public class ServiceSondage : IServiceSondage
    {
        //private Dictionary<string, List<int>> participants; //string = hash, List<int> = liste de sondages id répondus
        public Sondage GetSondage(int id)
        {
            List<Sondage> liste = LireFichier("Data/sondage.txt");
            if (liste.Count > id && id > 0)
                return liste[id - 1] ;
            else
                return null;
        }

        public int ReponseSondage(int idSondage, Reponse reponse)
        {
            var dataParticipants = System.IO.File.ReadAllText("Data/participants.json");
            var participants = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(dataParticipants)
                ?? new Dictionary<string, List<int>>();
            var dataReponses = File.ReadAllText("Data/reponsesRecues.json");
            var reponseRecues = JsonConvert.DeserializeObject<List<Reponse>>(dataReponses) ?? new List<Reponse>();

            string cleHashed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reponse.cleParticipant)));


            //Valid
            List<String> reponsesPossibles = new List<String>();
            Sondage sondage = GetSondage(idSondage);
            if (sondage == null)
                return -4;

            int j = 1;
            foreach (int question in reponse.QR.Keys)
            {
                if(question != j)
                {
                    //Questions pas bonnes
                    return -3;
                }
                j++;
            }
            if (reponse.QR.Keys.Count != sondage.Questions.Count) 
                return -3;

            int i = 0;
            var resultat = new List<bool>();
            foreach (string choix in reponse.QR.Values)
            {
                foreach (Choix idk in sondage.Questions[i].Choix)
                {
                    if (choix == idk.Lettre)
                    {
                        resultat.Add(true);
                    }
                }
                i++;
            }
            if (resultat.Count != reponse.QR.Count)
            {
                //ResponseInvalide
                return -1;
            }

            if (participants.ContainsKey(cleHashed) && participants[cleHashed].Contains(idSondage))
            {
                //Déja répondu
                return -2;
            }

            if(participants.ContainsKey(cleHashed))
            {
                participants[cleHashed].Add(idSondage);
            }
            else
            {
                participants.Add(cleHashed, new List<int>());
                participants[cleHashed].Add(idSondage);
            }

            string jsonP = JsonConvert.SerializeObject(participants);
            File.WriteAllText("Data/participants.json", jsonP);

            reponseRecues.Add(reponse);

            string jsonR = JsonConvert.SerializeObject(reponseRecues);
            File.WriteAllText("Data/reponsesRecues.json", jsonR);

            return 0;
        }


        private List<Sondage> LireFichier(string chemin)
        {
            var sondages = new List<Sondage>();
            
            using (StreamReader lecteur = new StreamReader(chemin))
            {
                string ligne;
                while ((ligne = lecteur.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(ligne))
                        continue;

                    if (ligne.StartsWith("Sondage"))
                    {
                        Sondage sondage = new Sondage();
                        sondage.Nom = ligne.Split(":")[0];
                       
                        sondage.Id = int.Parse(sondage.Nom.Replace("Sondage ", ""));
                        sondage.Questions = new List<Question>();
                        sondages.Add(sondage);
                    }
                    else if (char.IsDigit(ligne[0]))
                    {
                        Question question = new Question();
                        int indexPoint = ligne.IndexOf('.');
                        string reste = ligne.Substring(indexPoint + 1).Trim();
                        question.Id = int.Parse(ligne.Substring(0, indexPoint));
                        var qr = reste.Split("?");
                        question.Titre = qr[0] + "?";
                        question.Choix = ListeReponses(qr[1]);
                        sondages.Last().Questions.Add(question);
                    }
                }
            }
            return sondages;
        }

        private List<Choix> ListeReponses(string ligne)
        {
            List<Choix> reponses = new List<Choix>();
            var stringReponses = ligne.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach(string stringReponse in stringReponses)
            {
                var reponseParties = stringReponse.Trim().Split(':');
                reponses.Add(new Choix
                {
                    Lettre = reponseParties[0].Trim(),
                    Valeur = reponseParties[1].Trim()
                });
            }
            return reponses;
        }
    }

    
}

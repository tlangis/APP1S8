using SurveyApp.Services;
using SurveyApp.Tests.Infrastructure;

namespace SurveyApp.Tests.Services;

/// <summary>
/// Couvre <c>GetSondage</c> et, indirectement, l'analyseur de fichier privé
/// <c>LireFichier</c> / <c>ListeReponses</c> — seul chemin d'accès public vers eux.
/// </summary>
public class ServiceSondageGetSondageTests
{
    [Fact]
    public void GetSondage_IdValide_RetourneLeSondageEtSesQuestions()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(SondagesDeTest.DeuxSondages);
        var service = new ServiceSondage();

        var sondage = service.GetSondage(1);

        Assert.NotNull(sondage);
        Assert.Equal(1, sondage.Id);
        Assert.Equal("Sondage 1", sondage.Nom);
        Assert.Equal(2, sondage.Questions.Count);
        Assert.Equal("Aimez-vous le café?", sondage.Questions[0].Titre);
        Assert.Equal(1, sondage.Questions[0].Id);
    }

    [Fact]
    public void GetSondage_IdValide_AnalyseLesChoixLettreEtValeur()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(SondagesDeTest.DeuxSondages);
        var service = new ServiceSondage();

        var choix = service.GetSondage(1).Questions[0].Choix;

        Assert.Equal(2, choix.Count);
        Assert.Equal("a", choix[0].Lettre);
        Assert.Equal("Oui", choix[0].Valeur);
        Assert.Equal("b", choix[1].Lettre);
        Assert.Equal("Non", choix[1].Valeur);
    }

    [Fact]
    public void GetSondage_SeparateurPointVirgule_EstAccepteCommeLaVirgule()
    {
        using var donnees = new DossierDonneesTemporaire();
        // Deux sondages sont nécessaires pour que le sondage 1 soit atteignable : voir
        // GetSondage_DernierSondageDuFichier_RetourneNull_BogueDecalageIndice.
        donnees.EcrireSondage(
            "Sondage 1:\r\n" +
            "1. Combien de temps? a:Moins de 10 minutes; b:Entre 10 et 30 minutes, c:Plus\r\n" +
            "2. Question de remplissage? a:Oui, b:Non\r\n" +
            "Sondage 2:\r\n" +
            "1. Autre? a:Oui, b:Non\r\n");
        var service = new ServiceSondage();

        var choix = service.GetSondage(1).Questions[0].Choix;

        Assert.Equal(3, choix.Count);
        Assert.Equal(new[] { "a", "b", "c" }, choix.Select(c => c.Lettre));
    }

    [Theory]
    [InlineData(0)]   // « id > 0 » est faux
    [InlineData(-1)]  // « id > 0 » est faux, valeur négative
    public void GetSondage_IdNonPositif_RetourneNull(int id)
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(SondagesDeTest.DeuxSondages);
        var service = new ServiceSondage();

        Assert.Null(service.GetSondage(id));
    }

    [Fact]
    public void GetSondage_IdSuperieurAuNombreDeSondages_RetourneNull()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(SondagesDeTest.DeuxSondages);
        var service = new ServiceSondage();

        Assert.Null(service.GetSondage(3));
    }

    /// <summary>
    /// BOGUE CONNU (réf. REVISION-SECURITE.md, constat A-02). La garde est
    /// <c>liste.Count &gt; id</c> au lieu de <c>liste.Count &gt;= id</c> : le dernier sondage du
    /// fichier est donc toujours inatteignable. Avec deux sondages en fichier, l'id 2 renvoie 404.
    /// Ce test fige le comportement RÉEL, pas le comportement souhaité ; il devra être inversé
    /// quand le décalage d'indice sera corrigé.
    /// </summary>
    [Fact]
    public void GetSondage_DernierSondageDuFichier_RetourneNull_BogueDecalageIndice()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(SondagesDeTest.DeuxSondages);
        var service = new ServiceSondage();

        Assert.NotNull(service.GetSondage(1));
        Assert.Null(service.GetSondage(2));   // Sondage 2 existe pourtant dans le fichier.
    }

    [Fact]
    public void GetSondage_AvecTroisSondages_RendLesDeuxPremiersAccessibles()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(SondagesDeTest.TroisSondages);
        var service = new ServiceSondage();

        Assert.Equal(1, service.GetSondage(1).Id);
        Assert.Equal(2, service.GetSondage(2).Id);
        Assert.Null(service.GetSondage(3));
    }

    [Fact]
    public void GetSondage_LignesVidesOuBlanches_SontIgnorees()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(
            "Sondage 1:\r\n\r\n   \r\n" +
            "1. Aimez-vous le café? a:Oui, b:Non\r\n\r\n" +
            "Sondage 2:\r\n" +
            "1. Autre? a:Oui\r\n");
        var service = new ServiceSondage();

        var sondage = service.GetSondage(1);

        Assert.Single(sondage.Questions);
    }

    /// <summary>
    /// Une ligne qui n'est ni un en-tête « Sondage » ni une question numérotée tombe dans la
    /// branche implicite de l'analyseur : elle est silencieusement ignorée.
    /// </summary>
    [Fact]
    public void GetSondage_LigneNiEnteteNiQuestion_EstIgnoreeSilencieusement()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(
            "Sondage 1:\r\n" +
            "# commentaire qui ne devrait pas exister dans le format\r\n" +
            "1. Aimez-vous le café? a:Oui, b:Non\r\n" +
            "Ceci est du texte libre\r\n" +
            "Sondage 2:\r\n" +
            "1. Autre? a:Oui, b:Non\r\n");
        var service = new ServiceSondage();

        var sondage = service.GetSondage(1);

        Assert.Single(sondage.Questions);
        Assert.Equal("Aimez-vous le café?", sondage.Questions[0].Titre);
    }

    [Fact]
    public void GetSondage_FichierVide_RetourneNull()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage(string.Empty);
        var service = new ServiceSondage();

        Assert.Null(service.GetSondage(1));
    }

    [Fact]
    public void GetSondage_FichierAbsent_LeveUneException()
    {
        using var donnees = new DossierDonneesTemporaire();
        File.Delete(donnees.CheminSondage);
        var service = new ServiceSondage();

        // L'analyseur ouvre le StreamReader sans garde : l'absence du fichier remonte brute
        // jusqu'à l'appelant, et se traduit par une 500 côté API.
        Assert.Throws<FileNotFoundException>(() => service.GetSondage(1));
    }

    /// <summary>
    /// ROBUSTESSE : une question avant tout en-tête fait appeler <c>sondages.Last()</c> sur une
    /// liste vide. Le fichier de données n'est pas validé, donc une corruption devient une 500.
    /// </summary>
    [Fact]
    public void GetSondage_QuestionAvantToutEntete_LeveInvalidOperation()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage("1. Question orpheline? a:Oui, b:Non\r\nSondage 1:\r\n");
        var service = new ServiceSondage();

        Assert.Throws<InvalidOperationException>(() => service.GetSondage(1));
    }

    /// <summary>
    /// ROBUSTESSE : un choix sans « : » fait sortir <c>ListeReponses</c> des bornes du tableau.
    /// </summary>
    [Fact]
    public void GetSondage_ChoixSansDeuxPoints_LeveIndexOutOfRange()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage("Sondage 1:\r\n1. Question mal formée? aOui, b:Non\r\n");
        var service = new ServiceSondage();

        Assert.Throws<IndexOutOfRangeException>(() => service.GetSondage(1));
    }

    /// <summary>
    /// ROBUSTESSE : un en-tête dont le numéro n'est pas un entier fait échouer <c>int.Parse</c>.
    /// </summary>
    [Fact]
    public void GetSondage_EnteteAvecNumeroNonEntier_LeveFormatException()
    {
        using var donnees = new DossierDonneesTemporaire();
        donnees.EcrireSondage("Sondage X:\r\n1. Question? a:Oui, b:Non\r\n");
        var service = new ServiceSondage();

        Assert.Throws<FormatException>(() => service.GetSondage(1));
    }
}

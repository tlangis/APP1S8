using System.Net;
using System.Net.Http.Json;
using SurveyApp.Models;
using SurveyApp.Tests.Infrastructure;

namespace SurveyApp.Tests.Integration;

/// <summary>
/// Tests de bout en bout sur l'application réelle : Program.cs, l'intergiciel de clé d'API, le
/// routage et la validation de modèle d'<c>[ApiController]</c>. Ce sont les seuls tests qui
/// couvrent la composition du pipeline, y compris la branche « environnement de développement ».
/// </summary>
public class PipelineApplicatifTests
{
    // ------------------------------------------------------------- environnement et Swagger

    /// <summary>
    /// CONSTAT DE SÉCURITÉ (réf. REVISION-SECURITE.md) : <c>UseSwagger()</c> /
    /// <c>UseSwaggerUI()</c> sont enregistrés AVANT <c>UseMiddleware&lt;ClefAPIAuthz&gt;()</c>.
    /// En développement, la documentation de l'API — donc la description de la surface
    /// d'attaque — est donc accessible SANS clé.
    /// </summary>
    [Fact]
    public async Task EnDeveloppement_DocumentSwagger_EstAccessibleSansClef()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var reponse = await application.Client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        var document = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("API sondage", document);
        Assert.Contains("/api/Sondage/{id}", document);
    }

    [Fact]
    public async Task EnProduction_DocumentSwagger_NEstPasExpose()
    {
        using var application = ApplicationDeTest.EnProduction();

        var reponse = await application.Client.GetAsync("/swagger/v1/swagger.json");

        // Hors développement, Swagger n'est pas branché : la requête atteint l'intergiciel de
        // clé, qui la refuse avant tout routage.
        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task EnDeveloppement_DefinitionDeSecurite_DeclareLaClefDApi()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var document = await application.Client.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("\"ApiKey\"", document);
        Assert.Contains("X-API-Key", document);
    }

    // ----------------------------------------------------------------- intergiciel de clé

    [Fact]
    public async Task SansClef_ToutAppelApi_EstRefuseEn401()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var reponse = await application.Client.GetAsync("/api/sondage/1");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal("Rentre ta clef??", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AvecMauvaiseClef_ToutAppelApi_EstRefuseEn403()
    {
        using var application = ApplicationDeTest.EnDeveloppement();
        application.Client.DefaultRequestHeaders.Add("X-Api-Key", "pas-la-bonne");

        var reponse = await application.Client.GetAsync("/api/sondage/1");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task AvecClefValide_RouteInexistante_Repond404()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var reponse = await application.ClientAuthentifie().GetAsync("/api/inexistant");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    // ------------------------------------------------------------------------- GET sondage

    [Fact]
    public async Task GetSondage_AvecClefValide_Repond200AvecLeSondage()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var sondage = await application.ClientAuthentifie().GetFromJsonAsync<Sondage>("/api/sondage/1");

        Assert.NotNull(sondage);
        Assert.Equal(1, sondage.Id);
        Assert.Equal(2, sondage.Questions.Count);
    }

    [Fact]
    public async Task GetSondage_IdInexistant_Repond404()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var reponse = await application.ClientAuthentifie().GetAsync("/api/sondage/99");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task GetSondage_IdNonEntier_Repond404ParContrainteDeRoute()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var reponse = await application.ClientAuthentifie().GetAsync("/api/sondage/abc");

        // La contrainte « {id:int} » empêche la route de correspondre ; le service n'est
        // jamais atteint.
        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    // ------------------------------------------------------------------------ POST réponse

    [Fact]
    public async Task PostReponse_Valide_Repond200()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var reponse = await application.ClientAuthentifie()
            .PostAsJsonAsync("/api/sondage/1/reponses", ReponsesDeTest.Valide("participant-e2e"));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("0", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostReponse_DeuxFoisLaMemeClef_Repond409()
    {
        using var application = ApplicationDeTest.EnDeveloppement();
        var client = application.ClientAuthentifie();
        var corps = ReponsesDeTest.Valide("participant-e2e");

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/sondage/1/reponses", corps)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/sondage/1/reponses", corps)).StatusCode);
    }

    [Fact]
    public async Task PostReponse_ChoixInvalide_Repond400()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var corps = ReponsesDeTest.Avec("participant-e2e", new Dictionary<int, string>
        {
            [1] = "a",
            [2] = "z",
        });

        var reponse = await application.ClientAuthentifie()
            .PostAsJsonAsync("/api/sondage/1/reponses", corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task PostReponse_SondageInexistant_Repond404()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var reponse = await application.ClientAuthentifie()
            .PostAsJsonAsync("/api/sondage/99/reponses", ReponsesDeTest.Valide("participant-e2e"));

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    /// <summary>
    /// Un corps vide est rejeté en 400 par la validation de modèle d'<c>[ApiController]</c> :
    /// les types référence non-nullables rendent <c>QR</c> et <c>cleParticipant</c>
    /// implicitement obligatoires. Le service n'est jamais atteint — la protection contre le
    /// déréférencement de null y est donc accidentelle, pas explicite.
    /// </summary>
    [Fact]
    public async Task PostReponse_CorpsVide_Repond400AvantDAtteindreLeService()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var reponse = await application.ClientAuthentifie()
            .PostAsJsonAsync("/api/sondage/1/reponses", new { });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);

        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("QR", corps);
        Assert.Contains("cleParticipant", corps);
    }

    [Fact]
    public async Task PostReponse_JsonMalforme_Repond400()
    {
        using var application = ApplicationDeTest.EnDeveloppement();

        var contenu = new StringContent("{ pas du json", System.Text.Encoding.UTF8, "application/json");
        var reponse = await application.ClientAuthentifie()
            .PostAsync("/api/sondage/1/reponses", contenu);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    /// <summary>
    /// CONSTAT DE SÉCURITÉ (réf. REVISION-SECURITE.md, constat V-01) : de bout en bout, rien
    /// n'empêche un même appelant de répondre autant de fois qu'il le souhaite en changeant la
    /// chaîne <c>cleParticipant</c> qu'il choisit lui-même. Le livrable 4 n'est pas atteint.
    /// </summary>
    [Fact]
    public async Task PostReponse_ClesInventees_ContournentLUnicite_FailleV01()
    {
        using var application = ApplicationDeTest.EnDeveloppement();
        var client = application.ClientAuthentifie();

        for (var tentative = 0; tentative < 5; tentative++)
        {
            var reponse = await client.PostAsJsonAsync(
                "/api/sondage/1/reponses",
                ReponsesDeTest.Valide($"identite-jetable-{tentative}"));

            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        }
    }
}

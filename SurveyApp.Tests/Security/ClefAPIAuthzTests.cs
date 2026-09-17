using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SurveyApp.Security;

namespace SurveyApp.Tests.Security;

/// <summary>
/// Couvre l'intergiciel de clé d'API (livrable 1) : en-tête absent, clé erronée, clé valide.
/// </summary>
public class ClefAPIAuthzTests
{
    private const string ClefValide = "clef-de-test";

    /// <summary>
    /// Construit un <see cref="HttpContext"/> autonome : corps de réponse capturable et
    /// <see cref="IConfiguration"/> résoluble, comme le fait le conteneur en production.
    /// </summary>
    private static DefaultHttpContext ContexteAvecConfiguration(params (string Cle, string Valeur)[] reglages)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(reglages.Select(r =>
                new KeyValuePair<string, string?>(r.Cle, r.Valeur)))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    private static string LireCorps(HttpContext contexte)
    {
        contexte.Response.Body.Position = 0;
        return new StreamReader(contexte.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    [Fact]
    public async Task InvokeAsync_SansEnteteDeClef_Repond401EtNAppellePasLaSuite()
    {
        var contexte = ContexteAvecConfiguration(("ApiKey", ClefValide));
        var suiteAppelee = false;
        var intergiciel = new ClefAPIAuthz(_ => { suiteAppelee = true; return Task.CompletedTask; });

        await intergiciel.InvokeAsync(contexte);

        Assert.Equal(StatusCodes.Status401Unauthorized, contexte.Response.StatusCode);
        Assert.False(suiteAppelee);
    }

    [Fact]
    public async Task InvokeAsync_ClefErronee_Repond403EtNAppellePasLaSuite()
    {
        var contexte = ContexteAvecConfiguration(("ApiKey", ClefValide));
        contexte.Request.Headers["X-Api-Key"] = "mauvaise-clef";
        var suiteAppelee = false;
        var intergiciel = new ClefAPIAuthz(_ => { suiteAppelee = true; return Task.CompletedTask; });

        await intergiciel.InvokeAsync(contexte);

        Assert.Equal(StatusCodes.Status403Forbidden, contexte.Response.StatusCode);
        Assert.False(suiteAppelee);
    }

    [Fact]
    public async Task InvokeAsync_ClefValide_AppelleLaSuiteSansToucherAuStatut()
    {
        var contexte = ContexteAvecConfiguration(("ApiKey", ClefValide));
        contexte.Request.Headers["X-Api-Key"] = ClefValide;
        var suiteAppelee = false;
        var intergiciel = new ClefAPIAuthz(_ => { suiteAppelee = true; return Task.CompletedTask; });

        await intergiciel.InvokeAsync(contexte);

        Assert.True(suiteAppelee);
        Assert.Equal(StatusCodes.Status200OK, contexte.Response.StatusCode);
        Assert.Equal(string.Empty, LireCorps(contexte));
    }

    /// <summary>
    /// Le nom d'en-tête est comparé sans tenir compte de la casse par la pile HTTP :
    /// <c>x-api-key</c> passe aussi bien que <c>X-Api-Key</c>. L'écart de casse entre
    /// l'intergiciel (<c>X-Api-Key</c>) et la définition Swagger (<c>X-API-Key</c>) est donc
    /// cosmétique.
    /// </summary>
    [Theory]
    [InlineData("X-Api-Key")]
    [InlineData("x-api-key")]
    [InlineData("X-API-KEY")]
    public async Task InvokeAsync_CasseDuNomDEntete_EstIndifferente(string nomEntete)
    {
        var contexte = ContexteAvecConfiguration(("ApiKey", ClefValide));
        contexte.Request.Headers[nomEntete] = ClefValide;
        var suiteAppelee = false;
        var intergiciel = new ClefAPIAuthz(_ => { suiteAppelee = true; return Task.CompletedTask; });

        await intergiciel.InvokeAsync(contexte);

        Assert.True(suiteAppelee);
    }

    /// <summary>
    /// La VALEUR, elle, est comparée avec <c>string.Equals</c> ordinal : sensible à la casse.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ClefAvecCasseDifferente_Repond403()
    {
        var contexte = ContexteAvecConfiguration(("ApiKey", ClefValide));
        contexte.Request.Headers["X-Api-Key"] = ClefValide.ToUpperInvariant();
        var intergiciel = new ClefAPIAuthz(_ => Task.CompletedTask);

        await intergiciel.InvokeAsync(contexte);

        Assert.Equal(StatusCodes.Status403Forbidden, contexte.Response.StatusCode);
    }

    /// <summary>
    /// En-tête présent mais vide : <c>TryGetValue</c> réussit, la comparaison échoue → 403.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_EnteteVide_Repond403PasUn401()
    {
        var contexte = ContexteAvecConfiguration(("ApiKey", ClefValide));
        contexte.Request.Headers["X-Api-Key"] = string.Empty;
        var intergiciel = new ClefAPIAuthz(_ => Task.CompletedTask);

        await intergiciel.InvokeAsync(contexte);

        Assert.Equal(StatusCodes.Status403Forbidden, contexte.Response.StatusCode);
    }

    /// <summary>
    /// En-tête dupliqué : la conversion implicite de <c>StringValues</c> vers <c>string</c>
    /// donne <c>null</c> pour une valeur multiple, donc la comparaison échoue. L'intergiciel
    /// échoue en mode fermé — par accident, mais dans le bon sens.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_EnteteDuplique_EchoueEnModeFerme()
    {
        var contexte = ContexteAvecConfiguration(("ApiKey", ClefValide));
        contexte.Request.Headers["X-Api-Key"] = new[] { ClefValide, ClefValide };
        var suiteAppelee = false;
        var intergiciel = new ClefAPIAuthz(_ => { suiteAppelee = true; return Task.CompletedTask; });

        await intergiciel.InvokeAsync(contexte);

        Assert.Equal(StatusCodes.Status403Forbidden, contexte.Response.StatusCode);
        Assert.False(suiteAppelee);
    }

    /// <summary>
    /// CONSTAT DE SÉCURITÉ (réf. REVISION-SECURITE.md) : l'absence d'en-tête (401) et la
    /// mauvaise clé (403) sont distinguables, et les corps diffèrent. Un attaquant apprend donc
    /// que son NOM d'en-tête est le bon avant même d'avoir deviné la clé. Une réponse unique
    /// (401 dans les deux cas, corps identique) ne fuiterait rien.
    /// Ce test fige le comportement réel.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ReponsesDErreur_SontDistinguables_FuiteDInformation()
    {
        var sansEntete = ContexteAvecConfiguration(("ApiKey", ClefValide));
        await new ClefAPIAuthz(_ => Task.CompletedTask).InvokeAsync(sansEntete);

        var mauvaiseClef = ContexteAvecConfiguration(("ApiKey", ClefValide));
        mauvaiseClef.Request.Headers["X-Api-Key"] = "mauvaise-clef";
        await new ClefAPIAuthz(_ => Task.CompletedTask).InvokeAsync(mauvaiseClef);

        Assert.NotEqual(sansEntete.Response.StatusCode, mauvaiseClef.Response.StatusCode);
        Assert.NotEqual(LireCorps(sansEntete), LireCorps(mauvaiseClef));
        Assert.Equal("Rentre ta clef??", LireCorps(sansEntete));
        Assert.Equal("Mauvaise clef Kessé tu fâ", LireCorps(mauvaiseClef));
    }

    /// <summary>
    /// CONSTAT DE SÉCURITÉ : si la clé n'est pas configurée, <c>clefAPI</c> est nul et
    /// l'intergiciel lève une <see cref="NullReferenceException"/>. Une configuration
    /// incomplète devient une 500 sur CHAQUE requête, au lieu d'un refus net au démarrage.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ClefNonConfiguree_LeveNullReference()
    {
        var contexte = ContexteAvecConfiguration();   // aucun réglage « ApiKey »
        contexte.Request.Headers["X-Api-Key"] = "peu importe";
        var intergiciel = new ClefAPIAuthz(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<NullReferenceException>(() => intergiciel.InvokeAsync(contexte));
    }

    [Fact]
    public async Task InvokeAsync_SansEntete_EcritLeMessageDansLeCorps()
    {
        var contexte = ContexteAvecConfiguration(("ApiKey", ClefValide));
        var intergiciel = new ClefAPIAuthz(_ => Task.CompletedTask);

        await intergiciel.InvokeAsync(contexte);

        Assert.Equal("Rentre ta clef??", LireCorps(contexte));
    }
}

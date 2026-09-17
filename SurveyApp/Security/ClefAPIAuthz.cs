namespace SurveyApp.Security
{
    public class ClefAPIAuthz
    {
        private readonly RequestDelegate _prochain;
        private const string ClefAPIEntete = "X-Api-Key";

        public ClefAPIAuthz(RequestDelegate prochain)
        {
            this._prochain = prochain;
        }

        public async Task InvokeAsync(HttpContext contexte)
        {
            if (!contexte.Request.Headers.TryGetValue(ClefAPIEntete, out var clefAPIExtraite))
            {
                contexte.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await contexte.Response.WriteAsync("Rentre ta clef??");
                return;
            }

            var configuration = contexte.RequestServices.GetRequiredService<IConfiguration>();
            var clefAPI = configuration.GetValue<string>("ApiKey");

            if (!clefAPI.Equals(clefAPIExtraite))
            {
                contexte.Response.StatusCode= StatusCodes.Status403Forbidden;
                await contexte.Response.WriteAsync("Mauvaise clef Kessé tu fâ");
                return;
            }

            await _prochain(contexte);
        }

    }
}

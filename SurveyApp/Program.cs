using SurveyApp.Services;
using Microsoft.OpenApi;
using SurveyApp.Security;

const string clefApiSchemaId = "ApiKey";

var builder = WebApplication.CreateBuilder(args);

File.WriteAllText("Data/participants.json", string.Empty);
File.WriteAllText("Data/reponsesRecues.json", string.Empty);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSingleton<IServiceSondage, ServiceSondage>();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "API sondage",
        Version = "v1",
        Description = "Api sondage pour app1 s8. CANB1801 et LANT1401"
    });

    c.AddSecurityDefinition(clefApiSchemaId, new OpenApiSecurityScheme
    {
        Description = "Entrez votre clé d'API dans le format : X-API-Key: votre_clé",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(clefApiSchemaId, document)] = new List<string>()
    });
});




var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("swagger/v1/swagger.json", "API v1");
        c.RoutePrefix = String.Empty;
    });
}

app.UseHttpsRedirection();
app.UseMiddleware<ClefAPIAuthz>();
app.UseAuthorization();
app.UseAuthentication();
app.MapControllers();


app.Run();




using Microsoft.AspNetCore.Mvc;
using WebApiDocumentator.Options;

var builder = WebApplication.CreateBuilder(args);
//// Configurar el logging para un nivel detallado
//builder.Logging.ClearProviders();
//builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Trace);
//builder.Logging.AddDebug().SetMinimumLevel(LogLevel.Trace);  // Asegúrate de que los logs lleguen al debug
//builder.Logging.AddEventSourceLogger(); // Esto también muestra detalles de eventos internos

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers()
    .AddApplicationPart(typeof(WebApiDocumentator.ApiDemo.TestController).Assembly); // Forzar descubrimiento del ensamblado
builder.Services.AddOpenApi();
builder.Services.AddMyApiDocs(options =>
{
    options.ApiName = "Test Api";
    options.Version = "v1";
    options.Description = "The best API in the world!";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if(app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();


var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/debug/routes", (IEnumerable<EndpointDataSource> endpointSources) =>
{
    var routes = endpointSources
        .SelectMany(source => source.Endpoints)
        .OfType<RouteEndpoint>()
        .Select(endpoint => new
        {
            Route = endpoint.RoutePattern.RawText,
            Methods = endpoint.Metadata.OfType<HttpMethodMetadata>().SelectMany(m => m.HttpMethods),
            DisplayName = endpoint.DisplayName
        });
    return Results.Ok(routes);
}).WithName("DebugRoutes");

app.MapGet("/weatherforecast", (IHttpClientFactory service) =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

/// <summary>
/// Crea un nuevo recurso con las opciones especificadas.
/// </summary>
/// <returns>El recurso creado.</returns>
app.MapPost("/algo/{id}", (int id, DocumentatorOptions data, IHttpClientFactory service, [FromQuery] string filter = "") =>
    {
        data.Version = $"{id}/{data.Version}";
        data.Description = $"[{filter}]: {data.Description}";
        return data;
    });

app.UseWebApiDocumentatorUi();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

using Microsoft.AspNetCore.Mvc;
using System.Reflection.Metadata.Ecma335;
using WebApiDocumentator.Metadata;
using WebApiDocumentator.Options;

var builder = WebApplication.CreateBuilder(args);
//// Configurar el logging para un nivel detallado
//builder.Logging.ClearProviders();
//builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Trace);
//builder.Logging.AddDebug().SetMinimumLevel(LogLevel.Trace);  // Asegúrate de que los logs lleguen al debug
//builder.Logging.AddEventSourceLogger(); // Esto también muestra detalles de eventos internos

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddMyApiDocs(options =>
{
    options.ApiName = "Test Api";
    options.Version = "v1";
    options.Description = "The best API in the world!";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};


app.MapGet("/weatherforecast", () =>
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
app.MapPost("/algo", (DocumentatorOptions data) => Results.Ok(data));

app.UseWebApiDocumentatorUi();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

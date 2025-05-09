using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WebApiDocumentator.Metadata;
using WebApiDocumentator.Models;
using WebApiDocumentator.Options;

namespace WebApiDocumentator.Areas.Docs.Pages;

internal class IndexModel : PageModel
{
    private readonly CompositeMetadataProvider _metadataProvider;
    private readonly DocumentatorOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IApiDescriptionGroupCollectionProvider _provider;

    public List<ApiGroupInfo> Groups { get; private set; } = new();
    public ApiEndpointInfo? SelectedEndpoint { get; private set; }

    public string Name => _options.ApiName;
    public string Version => _options.Version;
    public string Description => _options.Description;
    public string? TestResponse { get; private set; }
    [BindProperty]
    public EndpointTestInput TestInput { get; set; } = new();


    public IndexModel(CompositeMetadataProvider metadataProvider, 
        IHttpClientFactory httpClientFactory,
        IApiDescriptionGroupCollectionProvider provider,
        IOptions<DocumentatorOptions> options)
    {
        _metadataProvider = metadataProvider;
        _httpClient = httpClientFactory.CreateClient("WebApiDocumentator");
        _options = options.Value;
        _provider = provider;
    }

    public void OnGet([FromQuery] string? method, [FromQuery] string? route)
    {
        Groups = _metadataProvider.GetGroupedEndpoints();

        if(!string.IsNullOrEmpty(method) && !string.IsNullOrEmpty(route))
        {
            SelectedEndpoint = Groups
                .SelectMany(g => g.Endpoints)
                .FirstOrDefault(e => e.HttpMethod.Equals(method, StringComparison.OrdinalIgnoreCase)
                                  && e.Route.Equals(route, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Verificar que Method y Route estén presentes
        if(string.IsNullOrEmpty(TestInput.Method) || string.IsNullOrEmpty(TestInput.Route))
        {
            ModelState.AddModelError("", "El método y la ruta son necesarios.");
            return Page(); // Vuelve a cargar la página si falta algún parámetro importante
        }

        Groups = _metadataProvider.GetGroupedEndpoints();

        SelectedEndpoint = Groups
            .SelectMany(g => g.Endpoints)
            .FirstOrDefault(e => e.HttpMethod.Equals(TestInput.Method, StringComparison.OrdinalIgnoreCase)
                              && e.Route.Equals(TestInput.Route, StringComparison.OrdinalIgnoreCase));

        if(SelectedEndpoint == null)
            return Page();

        // Asignamos dinámicamente la base URL
        _httpClient.BaseAddress = new Uri($"{Request.Scheme}://{Request.Host}{Request.PathBase}");

        var request = new HttpRequestMessage(new HttpMethod(TestInput.Method), TestInput.Route);

        if(SelectedEndpoint.Parameters.Any(p => p.IsFromBody))
        {
            var bodyParams = TestInput.Parameters
                .Where(p => SelectedEndpoint.Parameters.Any(sp => sp.Name == p.Key && sp.IsFromBody))
                .ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

            var json = System.Text.Json.JsonSerializer.Serialize(bodyParams);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Formateamos la respuesta JSON con indentación
        try
        {
            var formattedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(responseContent), new JsonSerializerOptions { WriteIndented = true });
            TestResponse = formattedJson;
        }
        catch
        {
            TestResponse = responseContent; // Si no es JSON, lo mostramos tal cual
        }

        return Page();
    }

    public string GetApiVersion()
    {
        return $"v{_provider.ApiDescriptionGroups.Version + 1}";
    }

}

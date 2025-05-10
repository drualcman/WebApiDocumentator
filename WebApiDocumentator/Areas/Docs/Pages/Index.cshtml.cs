using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WebApiDocumentator.Metadata;
using WebApiDocumentator.Models;
using WebApiDocumentator.Options;
using System.Web;

namespace WebApiDocumentator.Areas.Docs.Pages;

internal class IndexModel : PageModel
{
    private readonly CompositeMetadataProvider _metadataProvider;
    private readonly DocumentatorOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IApiDescriptionGroupCollectionProvider _provider;

    public List<ApiGroupInfo> Groups { get; private set; } = new();
    public ApiEndpointInfo? SelectedEndpoint { get; private set; }
    public string? ExampleBodyJson { get; private set; }

    public string Name => _options.ApiName;
    public string Version => _options.Version;
    public string Description => _options.Description;
    public string? TestResponse { get; private set; }
    [BindProperty]
    public EndpointTestInput TestInput { get; set; } = new();

    public IndexModel(
        CompositeMetadataProvider metadataProvider,
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

            if(SelectedEndpoint != null)
            {
                ExampleBodyJson = GenerateExampleBodyJson(SelectedEndpoint);
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if(string.IsNullOrEmpty(TestInput.Method) || string.IsNullOrEmpty(TestInput.Route))
        {
            ModelState.AddModelError("", "El método y la ruta son necesarios.");
            return Page();
        }

        Groups = _metadataProvider.GetGroupedEndpoints();

        SelectedEndpoint = Groups
            .SelectMany(g => g.Endpoints)
            .FirstOrDefault(e => e.HttpMethod.Equals(TestInput.Method, StringComparison.OrdinalIgnoreCase)
                              && e.Route.Equals(TestInput.Route, StringComparison.OrdinalIgnoreCase));

        if(SelectedEndpoint == null)
        {
            ModelState.AddModelError("", "No se encontró el endpoint seleccionado.");
            return Page();
        }

        ExampleBodyJson = GenerateExampleBodyJson(SelectedEndpoint);

        // Validar parámetros requeridos
        foreach(var param in SelectedEndpoint.Parameters.Where(p => p.IsRequired))
        {
            if(!TestInput.Parameters.ContainsKey(param.Name) || string.IsNullOrEmpty(TestInput.Parameters[param.Name]))
            {
                ModelState.AddModelError($"TestInput.Parameters[{param.Name}]", $"El parámetro {param.Name} es requerido.");
            }
        }

        if(!ModelState.IsValid)
        {
            return Page();
        }

        // Construir la URL con parámetros de consulta
        var queryParams = SelectedEndpoint.Parameters
            .Where(p => !p.IsFromBody && TestInput.Parameters.ContainsKey(p.Name) && !string.IsNullOrEmpty(TestInput.Parameters[p.Name]))
            .Select(p => $"{HttpUtility.UrlEncode(p.Name)}={HttpUtility.UrlEncode(TestInput.Parameters[p.Name])}")
            .ToList();

        var requestUrl = TestInput.Route;
        if(queryParams.Any())
        {
            requestUrl += "?" + string.Join("&", queryParams);
        }

        _httpClient.BaseAddress = new Uri($"{Request.Scheme}://{Request.Host}{Request.PathBase}");

        var request = new HttpRequestMessage(new HttpMethod(TestInput.Method), requestUrl);

        // Manejar parámetros IsFromBody
        if(SelectedEndpoint.Parameters.Any(p => p.IsFromBody))
        {
            var bodyParam = SelectedEndpoint.Parameters.FirstOrDefault(p => p.IsFromBody);
            if(bodyParam != null && TestInput.Parameters.ContainsKey(bodyParam.Name))
            {
                var bodyValue = TestInput.Parameters[bodyParam.Name];
                try
                {
                    // Intentar parsear el JSON introducido por el usuario
                    var jsonObject = JsonSerializer.Deserialize<object>(bodyValue);
                    var json = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = true });
                    request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                }
                catch(JsonException)
                {
                    ModelState.AddModelError($"TestInput.Parameters[{bodyParam.Name}]", "El cuerpo debe ser un JSON válido.");
                    return Page();
                }
            }
            else
            {
                ModelState.AddModelError("", "Falta el cuerpo de la solicitud para el parámetro esperado.");
                return Page();
            }
        }

        try
        {
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            try
            {
                var formattedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(responseContent), new JsonSerializerOptions { WriteIndented = true });
                TestResponse = formattedJson;
            }
            catch
            {
                TestResponse = responseContent;
            }

            if(!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", $"Error en la solicitud: {response.StatusCode} - {responseContent}");
            }
        }
        catch(HttpRequestException ex)
        {
            ModelState.AddModelError("", $"Error al enviar la solicitud: {ex.Message}");
        }

        return Page();
    }

    public string GetApiVersion()
    {
        return $"v{_provider.ApiDescriptionGroups.Version + 1}";
    }

    private string? GenerateExampleBodyJson(ApiEndpointInfo endpoint)
    {
        var bodyParam = endpoint.Parameters.FirstOrDefault(p => p.IsFromBody);
        if(bodyParam?.Schema == null)
            return null;

        var example = GenerateExampleFromSchema(bodyParam.Schema);
        return JsonSerializer.Serialize(example, new JsonSerializerOptions { WriteIndented = true });
    }

    private object GenerateExampleFromSchema(Dictionary<string, object> schema)
    {
        if(schema.TryGetValue("type", out var type))
        {
            switch(type.ToString())
            {
                case "object":
                    var example = new Dictionary<string, object>();
                    if(schema.TryGetValue("properties", out var propertiesObj) && propertiesObj is Dictionary<string, object> properties)
                    {
                        foreach(var prop in properties)
                        {
                            if(prop.Value is Dictionary<string, object> propSchema)
                            {
                                example[prop.Key] = GenerateExampleFromSchema(propSchema);
                            }
                        }
                    }
                    return example;
                case "array":
                    if(schema.TryGetValue("items", out var itemsObj) && itemsObj is Dictionary<string, object> itemsSchema)
                    {
                        return new[] { GenerateExampleFromSchema(itemsSchema) };
                    }
                    return new object[0];
                case "string":
                    return "string"; // Estilo Swagger
                case "integer":
                    return "integer"; // Estilo Swagger
                case "number":
                    return "number"; // Estilo Swagger
                case "boolean":
                    return "boolean"; // Estilo Swagger
                default:
                    return null!;
            }
        }
        return null!;
    }

    public string? GenerateFlatSchema(Dictionary<string, object>? schema)
    {
        if(schema == null || !schema.TryGetValue("type", out var type) || type.ToString() != "object")
            return null;

        var flatSchema = new Dictionary<string, string>();
        if(schema.TryGetValue("properties", out var propertiesObj) && propertiesObj is Dictionary<string, object> properties)
        {
            foreach(var prop in properties)
            {
                if(prop.Value is Dictionary<string, object> propSchema && propSchema.TryGetValue("type", out var propType))
                {
                    flatSchema[prop.Key] = propType.ToString();
                }
            }
        }

        return JsonSerializer.Serialize(flatSchema, new JsonSerializerOptions { WriteIndented = true });
    }
}
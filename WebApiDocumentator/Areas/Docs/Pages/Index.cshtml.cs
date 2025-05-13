namespace WebApiDocumentator.Areas.Docs.Pages;

internal class IndexModel : PageModel
{
    private readonly CompositeMetadataProvider _metadataProvider;
    private readonly DocumentatorOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IApiDescriptionGroupCollectionProvider _provider;
    private readonly ILogger<IndexModel> _logger;

    public List<EndpointGroupNode> Groups { get; private set; } = new();
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
        IOptions<DocumentatorOptions> options,
        ILogger<IndexModel> logger)
    {
        _metadataProvider = metadataProvider;
        _httpClient = httpClientFactory.CreateClient("WebApiDocumentator");
        _options = options.Value;
        _provider = provider;
        _logger = logger;
    }

    public void OnGet([FromQuery] string? id)
    {
        Groups = _metadataProvider.GetGroupedEndpoints();

        try
        {
            // Cargar Authentication desde la sesión
            var authJson = HttpContext.Session.GetString("AuthenticationInput");
            if(!string.IsNullOrEmpty(authJson))
            {
                try
                {
                    TestInput.Authentication = JsonSerializer.Deserialize<AuthenticationInput>(authJson) ?? new AuthenticationInput();
                    _logger.LogDebug("Loaded Authentication from session: Type={AuthType}", TestInput.Authentication.Type);
                }
                catch(JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Authentication from session: {Error}", ex.Message);
                    TestInput.Authentication = new AuthenticationInput();
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning("Failed use session: {Error}", ex.Message);
            TestInput.Authentication = new AuthenticationInput();
        }

        if(!string.IsNullOrWhiteSpace(id))
        {
            _logger.LogInformation("Received query Id: {Id}", id);
            SelectedEndpoint = Groups
                .SelectMany(g => GetAllEndpoints(g))
                .FirstOrDefault(e => e.Id == id);

            if(SelectedEndpoint == null)
            {
                _logger.LogWarning("Endpoint with Id {Id} not found. Available endpoints: {Endpoints}",
                    id,
                    string.Join("; ", Groups.SelectMany(g => GetAllEndpoints(g))
                        .Select(e => $"Id={e.Id}, Method={e.HttpMethod}, Route={e.Route}")));
            }
            else
            {
                _logger.LogDebug("Found endpoint: Id={Id}, Method={Method}, Route={Route}",
                    SelectedEndpoint.Id, SelectedEndpoint.HttpMethod, SelectedEndpoint.Route);
                ExampleBodyJson = SelectedEndpoint.ExampleJson;
            }
        }
        else
        {
            _logger.LogInformation("No Id provided in query. Displaying default view.");
            _logger.LogDebug("Available endpoints: {Endpoints}",
                string.Join("; ", Groups.SelectMany(g => GetAllEndpoints(g))
                    .Select(e => $"Id={e.Id}, Method={e.HttpMethod}, Route={e.Route}")));
            ExampleBodyJson = JsonSerializer.Serialize(Groups, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if(string.IsNullOrEmpty(TestInput.Method) || string.IsNullOrEmpty(TestInput.Route))
        {
            ModelState.AddModelError("", "Method and route are required.");
            return Page();
        }

        Groups = _metadataProvider.GetGroupedEndpoints();

        SelectedEndpoint = Groups
            .SelectMany(g => GetAllEndpoints(g))
            .FirstOrDefault(e => e.Id == TestInput.Id ||
                                (e.HttpMethod.Equals(TestInput.Method, StringComparison.OrdinalIgnoreCase) &&
                                 e.Route.Equals(TestInput.Route, StringComparison.OrdinalIgnoreCase)));

        if(SelectedEndpoint == null)
        {
            ModelState.AddModelError("", "Selected endpoint not found.");
            return Page();
        }

        ExampleBodyJson = SelectedEndpoint.ExampleJson;

        // Validar parámetros requeridos
        foreach(var param in SelectedEndpoint.Parameters.Where(p => p.IsRequired))
        {
            if(!TestInput.Parameters.ContainsKey(param.Name) || string.IsNullOrEmpty(TestInput.Parameters[param.Name]))
            {
                ModelState.AddModelError($"TestInput.Parameters[{param.Name}]", $"Parameter {param.Name} is required.");
            }
        }

        // Validar autenticación
        if(TestInput.Authentication.Type == AuthenticationType.Bearer)
        {
            if(string.IsNullOrWhiteSpace(TestInput.Authentication.BearerToken))
            {
                ModelState.AddModelError("TestInput.BearerToken", "Bearer token is required.");
            }
        }
        else if(TestInput.Authentication.Type == AuthenticationType.Basic)
        {
            if(string.IsNullOrWhiteSpace(TestInput.Authentication.BasicUsername))
            {
                ModelState.AddModelError("TestInput.BasicUsername", "Username is required for Basic authentication.");
            }
            if(string.IsNullOrWhiteSpace(TestInput.Authentication.BasicPassword))
            {
                ModelState.AddModelError("TestInput.BasicPassword", "Password is required for Basic authentication.");
            }
        }
        else if(TestInput.Authentication.Type == AuthenticationType.ApiKey && string.IsNullOrEmpty(TestInput.Authentication.ApiKeyValue))
        {
            ModelState.AddModelError("TestInput.Authentication.ApiKeyValue", "API Key is required when API Key authentication is selected.");
        }

        var authJson = JsonSerializer.Serialize(TestInput.Authentication);
        HttpContext.Session.SetString("AuthenticationInput", authJson);

        if(!ModelState.IsValid)
        {
            _logger.LogWarning("Invalid ModelState. Errors: {Errors}",
                string.Join("; ", ModelState.SelectMany(e => e.Value.Errors.Select(err => $"{e.Key}: {err.ErrorMessage}"))));
            return Page();
        }

        var requestUrl = TestInput.Route;

        // Reemplazar parámetros de ruta
        foreach(var param in SelectedEndpoint.Parameters.Where(p => p.Source == "Path" && TestInput.Parameters.ContainsKey(p.Name)))
        {
            var paramValue = HttpUtility.UrlEncode(TestInput.Parameters[param.Name] ?? "");
            requestUrl = requestUrl.Replace($"{{{param.Name}}}", paramValue, StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("Replaced route parameter: {{{ParamName}}} -> {ParamValue}", param.Name, paramValue);
        }

        // Añadir parámetros de consulta
        var queryParams = SelectedEndpoint.Parameters
            .Where(p => p.Source == "Query" && TestInput.Parameters.ContainsKey(p.Name))
            .Select(p => $"{HttpUtility.UrlEncode(p.Name)}={HttpUtility.UrlEncode(TestInput.Parameters[p.Name] ?? "")}")
            .ToList();

        if(queryParams.Any())
        {
            requestUrl += "?" + string.Join("&", queryParams);
        }

        _logger.LogInformation("Request URL: {RequestUrl}", requestUrl);

        _httpClient.BaseAddress = new Uri($"{Request.Scheme}://{Request.Host}{Request.PathBase}");

        var request = new HttpRequestMessage(new HttpMethod(TestInput.Method), requestUrl);

        // Añadir encabezados de autenticación
        if(TestInput.Authentication.Type == AuthenticationType.Bearer)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestInput.Authentication.BearerToken);
            _logger.LogInformation("Added Bearer authentication header");
        }
        else if(TestInput.Authentication.Type == AuthenticationType.Basic)
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{TestInput.Authentication.BasicUsername}:{TestInput.Authentication.Type}"));
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            _logger.LogInformation("Added Basic authentication header");
        }
        else if(TestInput.Authentication.Type == AuthenticationType.ApiKey && string.IsNullOrEmpty(TestInput.Authentication.ApiKeyValue))
        {
            if(TestInput.Authentication.ApiKeyLocation == "Header")
            {
                var headerName = string.IsNullOrEmpty(TestInput.Authentication.ApiKeyName) ? "X-Api-Key" : TestInput.Authentication.ApiKeyName;
                _httpClient.DefaultRequestHeaders.Add(headerName, TestInput.Authentication.ApiKeyValue);
                _logger.LogInformation("Added API Key to header: {HeaderName}={HeaderValue}", headerName, TestInput.Authentication.ApiKeyValue);
            }
            else if(TestInput.Authentication.ApiKeyLocation == "Query")
            {
                var keyName = string.IsNullOrEmpty(TestInput.Authentication.ApiKeyName) ? "apiKey" : TestInput.Authentication.ApiKeyName;
                queryParams.Add($"{HttpUtility.UrlEncode(keyName)}={HttpUtility.UrlEncode(TestInput.Authentication.ApiKeyValue)}");
                _logger.LogInformation("Added API Key to query: {KeyName}={KeyValue}", keyName, TestInput.Authentication.ApiKeyValue);
            }
        }


        // Añadir cuerpo de la solicitud
        if(SelectedEndpoint.Parameters.Any(p => p.IsFromBody))
        {
            var bodyParam = SelectedEndpoint.Parameters.FirstOrDefault(p => p.IsFromBody);
            if(bodyParam != null && TestInput.Parameters.ContainsKey(bodyParam.Name))
            {
                var bodyValue = TestInput.Parameters[bodyParam.Name];
                try
                {
                    var jsonObject = JsonSerializer.Deserialize<object>(bodyValue);
                    var json = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = true });
                    request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                }
                catch(JsonException)
                {
                    ModelState.AddModelError($"TestInput.Parameters[{bodyParam.Name}]", "Body must be valid JSON.");
                    return Page();
                }
            }
            else
            {
                ModelState.AddModelError("", "Request body is missing for the expected parameter.");
                return Page();
            }
        }

        try
        {
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if(!string.IsNullOrWhiteSpace(responseContent))
            {
                try
                {
                    var formattedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(responseContent), new JsonSerializerOptions { WriteIndented = true });
                    TestResponse = formattedJson;
                }
                catch
                {
                    TestResponse = responseContent;
                }
            }

            if(!response.IsSuccessStatusCode)
            {
                if(!string.IsNullOrWhiteSpace(responseContent))
                {
                    try
                    {
                        var problemDetails = JsonSerializer.Deserialize<JsonDocument>(responseContent);
                        if(problemDetails != null && problemDetails.RootElement.TryGetProperty("errors", out var errors))
                        {
                            var errorList = errors.Deserialize<JsonObject>();
                            foreach(var error in errorList)
                            {
                                var paramName = error.Key;
                                var errorMessages = error.Value as JsonArray;
                                if(errorMessages != null && SelectedEndpoint.Parameters.Any(p => p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    foreach(var errorMessage in errorMessages)
                                    {
                                        if(errorMessage != null)
                                        {
                                            ModelState.AddModelError($"TestInput.Parameters[{paramName}]", errorMessage.ToString());
                                        }
                                    }
                                }
                                else
                                {
                                    ModelState.AddModelError("", $"Request error: {error.Key} - {error.Value}");
                                }
                            }
                        }
                        else
                        {
                            ModelState.AddModelError("", $"Request error: {response.StatusCode} - {responseContent}");
                        }
                    }
                    catch
                    {
                        ModelState.AddModelError("", $"Request error: {(int)response.StatusCode} - {response.ReasonPhrase}. {responseContent}".Trim());
                    }
                }
                else
                {
                    ModelState.AddModelError("", $"Request error: {(int)response.StatusCode} - {response.ReasonPhrase}");
                }
            }
        }
        catch(HttpRequestException ex)
        {
            ModelState.AddModelError("", $"Error sending request: {ex.Message}");
        }

        return Page();
    }

    public string GetApiVersion()
    {
        return $"v{_provider.ApiDescriptionGroups.Version + 1}";
    }

    public string? GenerateFlatSchema(Dictionary<string, object>? schema)
    {
        JsonSchemaGenerator generator = new JsonSchemaGenerator(new());
        return generator.GetExampleAsJsonString(schema);
    }

    private IEnumerable<ApiEndpointInfo> GetAllEndpoints(EndpointGroupNode node)
    {
        var endpoints = new List<ApiEndpointInfo>(node.Endpoints);
        foreach(var child in node.Children)
        {
            endpoints.AddRange(GetAllEndpoints(child));
        }
        return endpoints;
    }

    public IActionResult OnPostClearAuth()
    {
        HttpContext.Session.Remove("AuthenticationInput");
        _logger.LogDebug("Cleared Authentication from session");
        return new JsonResult(new { success = true });
    }
}
namespace WebApiDocumentator.Areas.WebApiDocumentator.Pages;

internal class IndexModel : PageModel
{
    private readonly CompositeMetadataProvider _metadataProvider;
    private readonly DocumentatorOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IApiDescriptionGroupCollectionProvider _provider;
    private readonly ILogger<IndexModel> _logger;

    public List<EndpointGroupNode> Groups { get; private set; } = new();
    public ApiEndpointInfo? SelectedEndpoint { get; private set; }

    public string Name => _options.ApiName;
    public string Version => _options.Version;
    public string Description => _options.Description;
    public string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
    public string DocsBaseUrl => string.IsNullOrWhiteSpace(_options.DocsBaseUrl) ? "WebApiDocumentator" : _options.DocsBaseUrl.TrimStart('/');
    public string? TestResponse { get; private set; }
    [BindProperty]
    public EndpointTestInput TestInput { get; set; } = new();
    public string? ExampleRequestUrl { get; private set; }
    public string? RequestBodyJson { get; private set; }
    public string? ExampleBodyJson { get; private set; }

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
                ExampleRequestUrl = GenerateExampleRequestUrl(SelectedEndpoint);
                // Generar el JSON del cuerpo de la solicitud
                RequestBodyJson = GenerateRequestBodyJson(SelectedEndpoint);
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
        ExampleRequestUrl = GenerateExampleRequestUrl(SelectedEndpoint);
        // Generar el JSON del cuerpo de la solicitud
        RequestBodyJson = GenerateRequestBodyJson(SelectedEndpoint);

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
            .Where(p => p.Source == "Query" && TestInput.Parameters.ContainsKey(p.Name) && !string.IsNullOrEmpty(TestInput.Parameters[p.Name]))
            .Select(p => $"{HttpUtility.UrlEncode(p.Name)}={HttpUtility.UrlEncode(TestInput.Parameters[p.Name])}")
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

    // Nuevo método para generar la URL de ejemplo con parámetros de ruta y consulta
    private string GenerateExampleRequestUrl(ApiEndpointInfo endpoint)
    {
        var generator = new JsonSchemaGenerator();
        var url = endpoint.Route;

        // Reemplazar parámetros de ruta
        foreach(var param in endpoint.Parameters.Where(p => p.Source == "Path"))
        {
            var exampleValue = GenerateParameterExample(param, generator);
            url = url.Replace($"{{{param.Name}}}", HttpUtility.UrlEncode(exampleValue), StringComparison.OrdinalIgnoreCase);
        }

        // Añadir parámetros de consulta
        var queryParams = endpoint.Parameters
            .Where(p => p.Source == "Query")
            .Select(p => $"{HttpUtility.UrlEncode(p.Name)}={HttpUtility.UrlEncode(GenerateParameterExample(p, generator))}")
            .ToList();

        if(queryParams.Any())
        {
            url += "?" + string.Join("&", queryParams);
        }

        return url;
    }

    // Método auxiliar para generar valores de ejemplo para parámetros
    private string GenerateParameterExample(ApiParameterInfo param, JsonSchemaGenerator generator)
    {
        if(param.Schema != null)
        {
            var example = generator.GetExampleAsJsonString(param.Schema);
            if(!string.IsNullOrEmpty(example))
            {
                return example.Trim('"');
            }
        }

        // Valores predeterminados si no hay esquema o ejemplo
        return param.Type switch
        {
            "string" => "example",
            "integer" => "123",
            "number" => "123.45",
            "boolean" => "true",
            _ => "example"
        };
    }


    // Método auxiliar para generar el JSON del cuerpo de la solicitud
    private string? GenerateRequestBodyJson(ApiEndpointInfo endpoint)
    {
        var bodyParam = endpoint.Parameters.FirstOrDefault(p => p.IsFromBody);
        if(bodyParam != null && bodyParam.Schema != null)
        {
            var generator = new JsonSchemaGenerator();
            return generator.GetExampleAsJsonString(bodyParam.Schema);
        }
        return null;
    }

    public string GetApiVersion()
    {
        return $"v{_provider.ApiDescriptionGroups.Version + 1}";
    }

    public string? GenerateFlatSchema(Dictionary<string, object>? schema)
    {
        JsonSchemaGenerator generator = new JsonSchemaGenerator();
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

    public string? HttpMethodFormatted => SelectedEndpoint != null
    ? char.ToUpper(SelectedEndpoint.HttpMethod[0]) + SelectedEndpoint.HttpMethod.Substring(1).ToLower()
    : "";

    public string Style
    {
        get
        {
            string stype = @"
    <style>
        :root {
            --primary-color: #4361ee;
            --primary-dark: #3a56d4;
            --secondary-color: #3f37c9;
            --success-color: #4cc9f0;
            --danger-color: #f72585;
            --warning-color: #f8961e;
            --info-color: #4895ef;
            --light-color: #f8f9fa;
            --dark-color: #212529;
            --gray-color: #6c757d;
            --light-gray: #e9ecef;
            --border-color: #dee2e6;
            --sidebar-width: 25%;
            --sidebar-bg: #2b2d42;
            --sidebar-text: #f8f9fa;
            --sidebar-hover: #3a3e5a;
            --sidebar-active: #ffffff;
            --content-bg: #ffffff;
            --example-bg: #f8f9fa;
            --code-bg: #1e1e1e;
            --code-text: #d4d4d4;
            --transition: all 0.3s ease;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
            font-family: 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            background-color: var(--content-bg);
            display: flex;
            min-height: 100vh;
            margin: 0;
        }

        .sidebar-toggle {
            position: fixed;
            right: 1rem;
            top: 1rem;
            z-index: 900;
            background: var(--primary-color);
            color: white;
            border: none;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            font-size: 1.2rem;
            cursor: pointer;
            box-shadow: 0 2px 5px rgba(0, 0, 0, 0.2);
            transform: rotate(90deg);
        }  

            .sidebar-toggle.active {
                transform: unset;
            }

        #sidebar {
            width: var(--sidebar-width);
            background-color: var(--sidebar-bg);
            color: var(--sidebar-text);
            height: 100vh;
            position: fixed;
            right: -100%;
            top: 0;
            overflow-y: auto;
            transition: var(--transition);
            z-index: 10;
            box-shadow: 2px 0 10px rgba(0, 0, 0, 0.1);  .
            scroll-behavior: smooth;
        }

            #sidebar.active {
                right: 0;
            }

        .sidebar-header {
            padding: 1.5rem;
            border-bottom: 1px solid rgba(255, 255, 255, 0.1);
        }

            .sidebar-header h3 {
                color: white;
                margin: 0;
                font-size: 1.2rem;
            }

        .sidebar-search {
            padding: 0.5rem 1rem;
            position: relative;
        }

            .sidebar-search input {
                width: 100%;
                padding: 0.5rem 1rem 0.5rem 2rem;
                border-radius: 4px;
                border: none;
                background-color: rgba(255, 255, 255, 0.1);
                color: white;
            }

                .sidebar-search input::placeholder {
                    color: rgba(255, 255, 255, 0.5);
                }

        .sidebar-title {
            padding: 1rem 1.5rem;
            font-size: 0.9rem;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: rgba(255, 255, 255, 0.7);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

            .sidebar-title .toggle-all {
                cursor: pointer;
                font-size: 0.8rem;
                color: var(--sidebar-text);
            }

        .endpoint-list {
            list-style: none;
            padding: 0;
            margin: 0;
        }         

            .endpoint-list .endpoint-list {
                margin-left: 1.5rem;
                border-left: 1px solid rgba(255, 255, 255, 0.1);
                padding-left: 0.5rem;
            }

        .endpoint-group {
            border-bottom: 1px solid rgba(255, 255, 255, 0.1);
            padding-bottom: 0.5rem;
        }

        .group-header {
            padding: 0.15rem 1rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
            cursor: pointer;
            transition: var(--transition);
            user-select: none;
        }

            .group-header:hover {
                background-color: var(--sidebar-hover);
            }

        .group-title {
            font-weight: 500;
            display: flex;
            align-items: center; 
            flex-grow: 1;
        }

            .group-title::before {
                content: ""📁"";
                margin-right: 0.5rem;
                font-size: 0.9em;
            }

        .group-toggle {
            transition: transform 0.2s ease; 
            margin-left: 0.5rem;
            flex-shrink: 0; 
        }

            .group-toggle.collapsed {
                transform: rotate(-90deg);
            }

        .group-items {
            list-style: none;
            padding: 0;
            margin: 0;
            max-height: 0;
            overflow: hidden;
            transition: max-height 0.3s ease;
        }

        .endpoint-item {                            
            padding: 0.15rem 0.5rem 0.15rem 1rem;  .
            overflow: hidden; 
        }

        a.endpoint-link {
            text-decoration: none;
            color: rgba(255, 255, 255, 0.8);
            display: flex;
            align-items: center;
            font-size: 0.9rem;
            transition: var(--transition);
            overflow: hidden; 
            padding: 0.25rem 0.5rem;
            border-radius: 4px; 
            margin: 0.1rem 0; 
            word-break: break-word;
            white-space: normal; 
        }

            a.endpoint-link:hover {
                color: white;
                background-color: rgba(255, 255, 255, 0.1);
            }

            a.endpoint-link.selected {   
                background-color: var(--sidebar-active);
                color: black;
                font-weight: 600;
                padding: 0.5rem;
                overflow: visible;
            }

        .endpoint-method {
            display: inline-block;
            padding: 0.2rem 0.5rem;
            border-radius: 3px;
            font-size: 0.7rem;
            font-weight: 600;
            margin-right: 0.5rem;
            text-transform: uppercase;
            min-width: 50px;
            text-align: center;  
            flex-shrink: 0;
        }

        .endpoint-route {
            flex-grow: 1;
            overflow: hidden;
            word-break: break-word;
        }

        .GET {
            background-color: var(--success-color);
            color: black;
        }

        .POST {
            background-color: var(--info-color);
            color: white;
        }

        .PUT {
            background-color: var(--warning-color);
            color: black;
        }

        .DELETE {
            background-color: var(--danger-color);
            color: white;
        }

        .PATCH {
            background-color: #9d4edd;
            color: white;
        }

        .HEAD {
            background-color: #7b2cbf;
            color: white;
        }

        #content {
            padding: 1rem;
            flex: 1; 
        }

 
        #models-example,        
        {
            padding: 2rem;
            background-color: var(--example-bg);
            border-left: 1px solid var(--border-color);
        } 

        #examples {  
            min-width: 30%; 
            max-width: 35%; 
            padding: 1rem;
            background-color: var(--example-bg);
            border-left: 1px solid var(--border-color);
            position: sticky;
            overflow: auto;
            top: 0;
            height: 100vh;
        }

        .card {
            background-color: white;
            border-radius: 8px;
            box-shadow: 0 2px 10px rgba(0, 0, 0, 0.05);
            padding: 1.5rem;
            margin-bottom: 1.5rem;
        }

        h1, h2, h3, h4, h5 {
            color: var(--dark-color);
            margin-bottom: 1rem;
        }

        h1 {
            font-size: 2rem;
        }

        h2 {
            font-size: 1.75rem;
        }

        h3 {
            font-size: 1.5rem;
        }

        h4 {
            font-size: 1.25rem;
        }

        h5 {
            font-size: 1rem;
        }

        p {
            margin-bottom: 1rem;
        }

       
        .endpoint-header {
            display: flex;
            align-items: center;
            gap: 1rem;
            margin-bottom: 1.5rem;
        }

        .endpoint-title {
            font-size: 1.75rem;
            margin: 0;
        }

        .endpoint-description {
            color: var(--gray-color);
            margin-bottom: 1.5rem;
        }

      
        .schema-table {
            width: 100%;
            border-collapse: collapse;
            margin: 1.5rem 0;
            background: white;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
        }

            .schema-table th, .schema-table td {
                padding: 0.75rem 1rem;
                text-align: left;
                border-bottom: 1px solid var(--border-color);
            }

            .schema-table th {
                background-color: var(--light-gray);
                font-weight: 600;
                color: var(--dark-color);
            }

            .schema-table tr:last-child td {
                border-bottom: none;
            }

        .source-path {
            color: var(--primary-color);
            font-weight: 600;
        }

    
        .json-viewer {
            background-color: var(--code-bg);
            color: var(--code-text);
            padding: 1rem;
            border-radius: 8px;
            font-family: ""Consolas"", ""Courier New"", monospace;
            font-size: 0.9rem;
            white-space: pre-wrap;
            word-wrap: break-word;
            overflow-y: auto;
            max-height: 400px;
            margin: 1rem 0;
        }

      
        .test-form {
            margin-top: 2rem;
        }

        .form-group {
            margin-bottom: 1.5rem;
        }

        label {
            display: block;
            margin-bottom: 0.5rem;
            font-weight: 500;
        }

        input[type=""text""],
        input[type=""password""],
        textarea,
        select {
            width: 100%;
            padding: 0.75rem;
            border: 1px solid var(--border-color);
            border-radius: 6px;
            font-family: inherit;
            font-size: 0.9rem;
            transition: border-color 0.2s;
        }

            input[type=""text""]:focus,
            input[type=""password""]:focus,
            textarea:focus,
            select:focus {
                outline: none;
                border-color: var(--primary-color);
                box-shadow: 0 0 0 3px rgba(67, 97, 238, 0.1);
            }

        textarea {
            min-height: 150px;
            resize: vertical;
        }

        .btn {
            display: inline-block;
            padding: 0.75rem 1.5rem;
            background-color: var(--primary-color);
            color: white;
            border: none;
            border-radius: 6px;
            font-size: 0.9rem;
            font-weight: 500;
            cursor: pointer;
            transition: var(--transition);
        }

            .btn:hover {
                background-color: var(--primary-dark);
            }

        .btn-secondary {
            background-color: var(--gray-color);
        }

            .btn-secondary:hover {
                background-color: #5a6268;
            }

        /* Auth section */
        .auth-section {
            margin-bottom: 1.5rem;
            padding: 1rem;
            background-color: var(--light-gray);
            border-radius: 8px;
        }

        .auth-fields {
            display: none;
            margin-top: 1rem;
            padding-top: 1rem;
            border-top: 1px solid var(--border-color);
        }

            .auth-fields.active {
                display: block;
            }

   
        .error-message {
            color: var(--danger-color);
            background-color: #f8d7da;
            padding: 0.75rem;
            border-radius: 6px;
            margin-bottom: 1rem;
            font-size: 0.9rem;
        }

        .field-error {
            color: var(--danger-color);
            font-size: 0.8rem;
            margin-top: 0.25rem;
        }
                
        .models-tabs,        
        .example-tabs {
            display: flex;
            border-bottom: 1px solid var(--border-color);
            margin-bottom: 1rem;
        }

        
        .models-tab,
        .example-tab {
            padding: 0.5rem 1rem;
            cursor: pointer;
            border-bottom: 2px solid transparent;
            transition: var(--transition);
        }
            .models-tab.active,
            .example-tab.active {
                border-bottom-color: var(--primary-color);
                color: var(--primary-color);
                font-weight: 500;
            }
        .models-tab-content,
        .example-tab-content {
            display: none;
        }
            .models-tab-content.active ,
            .example-tab-content.active {
                display: block;
            }


        @media (max-width: 1200px) {
            #examples {
                display: none;
            }
        }

        @media (max-width: 768px) {
            #content {
                min-width: 85%;
                margin-left: 0;
                padding: 1rem;
            }
        }   
    
        a.endpoint-link {
            font-size: 0.85rem;
        }

        .loading {
            display: none;
            text-align: center;
            padding: 1rem;
        }

        .loading-spinner {
            border: 3px solid rgba(0, 0, 0, 0.1);
            border-radius: 50%;
            border-top: 3px solid var(--primary-color);
            width: 20px;
            height: 20px;
            animation: spin 1s linear infinite;
            display: inline-block;
        }

        @@keyframes spin {
            0% {
                transform: rotate(0deg);
            }

            100% {
                transform: rotate(360deg);
            }
        }
        .example-header {
            display: flex;
            justify-content: flex-end;
            margin-bottom: 0.5rem;
        }

        .copy-btn {
            background-color: var(--primary-color);
            color: white;
            border: none;
            border-radius: 4px;
            padding: 0.25rem 0.5rem;
            font-size: 0.8rem;
            cursor: pointer;
            transition: var(--transition);
        }

        .copy-btn:hover {
            background-color: var(--primary-dark);
        }

        .copy-btn.copied {
            background-color: var(--success-color);
        }
        .doc-tabs {
            display: flex;
            border-bottom: 1px solid var(--border-color);
            margin-bottom: 1.5rem;
        }

        .doc-tab {
            padding: 0.75rem 1.5rem;
            cursor: pointer;
            border-bottom: 3px solid transparent;
            font-weight: 500;
            transition: var(--transition);
        }

        .doc-tab:hover {
            color: var(--primary-color);
        }

        .doc-tab.active {
            border-bottom-color: var(--primary-color);
            color: var(--primary-color);
        }

        .doc-tab-content {
            display: none;
        }

        .doc-tab-content.active {
            display: block;
        }
    </style>
";
            return stype;
        }
    }
}
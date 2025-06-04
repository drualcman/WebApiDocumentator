namespace WebApiDocumentator.Areas.WebApiDocumentator.Pages;

internal class IndexModel : PageModel
{
    private readonly EndpointService _endpointService;
    private readonly RequestProcessor _requestProcessor;
    private readonly ILogger<IndexModel> _logger;
    private readonly DocumentatorOptions _options;

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
    public string? ResponseCodeDescription { get; private set; }
    public string FormEnctype => _endpointService.GetFormEnctype(SelectedEndpoint);

    public IndexModel(
        EndpointService endpointService,
        RequestProcessor requestProcessor,
        IOptions<DocumentatorOptions> options,
        ILogger<IndexModel> logger)
    {
        _endpointService = endpointService;
        _requestProcessor = requestProcessor;
        _options = options.Value;
        _logger = logger;
    }

    public void OnGet([FromQuery] string? id)
    {
        Groups = _endpointService.GetGroupedEndpoints();
        TestInput.Authentication = _requestProcessor.LoadAuthenticationFromSession(HttpContext.Session);

        if(!string.IsNullOrWhiteSpace(id))
        {
            SelectedEndpoint = _endpointService.FindEndpointById(Groups, id);
            if(SelectedEndpoint != null)
            {
                ExampleBodyJson = SelectedEndpoint.ExampleJson;
                ExampleRequestUrl = _endpointService.GenerateExampleRequestUrl(SelectedEndpoint);
                RequestBodyJson = _endpointService.GenerateRequestBodyJson(SelectedEndpoint);
            }
        }
        else
        {
            ExampleBodyJson = JsonSerializer.Serialize(Groups, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Groups = _endpointService.GetGroupedEndpoints();
        SelectedEndpoint = _endpointService.FindEndpointById(Groups, TestInput.Id);

        if(SelectedEndpoint == null)
        {
            ModelState.AddModelError("", "Selected endpoint not found.");
            TestResponse = "Selected endpoint not found.";
            return Page();
        }

        ExampleBodyJson = SelectedEndpoint.ExampleJson;
        ExampleRequestUrl = _endpointService.GenerateExampleRequestUrl(SelectedEndpoint);
        RequestBodyJson = _endpointService.GenerateRequestBodyJson(SelectedEndpoint);

        await _requestProcessor.ValidateInput(SelectedEndpoint, TestInput, Request, ModelState);
        _requestProcessor.SaveAuthenticationToSession(HttpContext.Session, TestInput.Authentication);

        if(!ModelState.IsValid)
        {
            TestResponse = $"Invalid ModelState.";
            return Page();
        }

        var result = await _requestProcessor.ProcessRequestAsync(SelectedEndpoint, TestInput, Request, ModelState);
        TestResponse = result.ResponseContent;
        ResponseCodeDescription = result.ResponseCodeDescription;

        return Page();
    }

    public IActionResult OnPostClearAuth()
    {
        HttpContext.Session.Remove("AuthenticationInput");
        _logger.LogDebug("Cleared Authentication from session");
        return new JsonResult(new { success = true });
    }

    public string GetApiVersion() => _endpointService.GetApiVersion();

    public string? GenerateFlatSchema(Dictionary<string, object>? schema) => _endpointService.GenerateFlatSchema(schema);

    public IEnumerable<PropertyInfo> GetFormProperties(string typeName) => _endpointService.GetFormProperties(typeName);

    public Type? GetTypeFromName(string typeName) => _endpointService.GetTypeFromName(typeName);

    public string? HttpMethodFormatted => SelectedEndpoint != null
        ? char.ToUpper(SelectedEndpoint.HttpMethod[0]) + SelectedEndpoint.HttpMethod.Substring(1).ToLower()
        : "";

    public int CountLinesInSchema(Dictionary<string, object>? schema) => _endpointService.CountLinesInSchema(schema);
}
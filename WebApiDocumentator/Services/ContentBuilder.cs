namespace WebApiDocumentator.Services;

internal class ContentBuilder
{
    private readonly ILogger<ContentBuilder> _logger;
    private readonly JsonBodyBuilder _jsonBodyBuilder;
    private readonly FormContentBuilder _formContentBuilder;

    public ContentBuilder(
        ILogger<ContentBuilder> logger,
        FormContentBuilder formContentBuilder)
    {
        _logger = logger;
        _jsonBodyBuilder = new();
        _formContentBuilder = formContentBuilder;
    }

    public async Task<bool> PrepareRequestContent(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        HttpRequest httpRequest,
        ModelStateDictionary modelState,
        HttpRequestMessage request)
    {
        if(endpoint.Parameters.Any(p => p.IsFromBody))
        {
            _jsonBodyBuilder.PrepareJsonBodyContent(endpoint, testInput, modelState, request);
            return false;
        }
        else if(endpoint.Parameters.Any(p => p.Source == "Form"))
        {
            return await _formContentBuilder.PrepareFormContent(
                endpoint, testInput, httpRequest, modelState, request);
        }
        return false;
    }
}
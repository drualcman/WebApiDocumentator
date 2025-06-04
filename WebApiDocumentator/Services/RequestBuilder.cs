namespace WebApiDocumentator.Services;

internal class RequestBuilder
{
    private readonly ILogger<RequestBuilder> _logger;
    private readonly UrlBuilder _urlBuilder;
    private readonly ContentBuilder _contentBuilder;
    private HttpRequestMessage _request;

    public RequestBuilder(
        ILogger<RequestBuilder> logger,
        UrlBuilder urlBuilder,
        ContentBuilder contentBuilder)
    {
        _logger = logger;
        _urlBuilder = urlBuilder;
        _contentBuilder = contentBuilder;
        _request = new HttpRequestMessage();
    }

    public HttpRequestMessage Request => _request;

    public string BuildRequestUrl(ApiEndpointInfo endpoint, EndpointTestInput testInput)
    {
        return _urlBuilder.BuildRequestUrl(endpoint, testInput);
    }

    public async Task<bool> PrepareRequestContent(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        HttpRequest httpRequest,
        ModelStateDictionary modelState)
    {
        return await _contentBuilder.PrepareRequestContent(
            endpoint, testInput, httpRequest, modelState, _request);
    }
}
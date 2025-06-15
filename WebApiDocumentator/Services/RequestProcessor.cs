namespace WebApiDocumentator.Services;

internal class RequestProcessor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuthenticationHandler _authHandler;
    private readonly RequestValidator _validator;
    private readonly RequestBuilder _builder;
    private readonly ResponseProcessor _responseProcessor;

    public RequestProcessor(
        IHttpClientFactory httpClientFactory,
        AuthenticationHandler authHandler,
        RequestBuilder builder,
        ResponseProcessor responseProcessor)
    {
        _httpClientFactory = httpClientFactory;
        _authHandler = authHandler;
        _validator = new();
        _builder = builder;
        _responseProcessor = responseProcessor;
    }

    public AuthenticationInput LoadAuthenticationFromSession(ISession session)
        => _authHandler.LoadFromSession(session);

    public void SaveAuthenticationToSession(ISession session, AuthenticationInput authentication)
        => _authHandler.SaveToSession(session, authentication);

    public async Task ValidateInput(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        HttpRequest httpRequest,
        ModelStateDictionary modelState)
    {
        var requestUrl = _builder.BuildRequestUrl(endpoint, testInput);
        _builder.Request.Method = new HttpMethod(testInput.Method);
        _builder.Request.RequestUri = new Uri(requestUrl, UriKind.RelativeOrAbsolute);

        await _builder.PrepareRequestContent(endpoint, testInput, httpRequest, modelState);
        _validator.ValidateInput(endpoint, testInput, modelState);
    }

    public async Task<RequestProcessingResult> ProcessRequestAsync(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        HttpRequest httpRequest,
        ModelStateDictionary modelState)
    {
        var result = new RequestProcessingResult();
        using var httpClient = _httpClientFactory.CreateClient("WebApiDocumentator");
        httpClient.BaseAddress = new Uri($"{httpRequest.Scheme}://{httpRequest.Host}{httpRequest.PathBase}");

        _authHandler.PrepareAuthentication(httpClient, testInput.Authentication);

        try
        {
            using var response = await httpClient.SendAsync(_builder.Request);
            result.ResponseContent = response.ReasonPhrase;
            result.ResponseCodeDescription = $"[{(int)response.StatusCode}] {response.ReasonPhrase}".Trim();

            await _responseProcessor.FormatResponseContent(response, result);

            if(!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _responseProcessor.ProcessErrorResponse(responseContent, endpoint, modelState, result);
            }
        }
        catch(HttpRequestException ex)
        {
            result.ResponseContent = $"Error sending request: {ex.Message}";
            modelState.AddModelError("", $"Error sending request: {ex.Message}");
        }

        return result;
    }
}
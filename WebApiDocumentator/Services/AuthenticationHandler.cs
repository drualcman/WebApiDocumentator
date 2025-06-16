namespace WebApiDocumentator.Services;
internal class AuthenticationHandler
{
    private readonly ILogger<AuthenticationHandler> _logger;

    public AuthenticationHandler(ILogger<AuthenticationHandler> logger)
    {
        _logger = logger;
    }

    public AuthenticationInput LoadAuthenticationFromSession(ISession session)
    {
        try
        {
            var authJson = session.GetString("AuthenticationInput");
            if(!string.IsNullOrEmpty(authJson))
            {
                try
                {
                    return JsonSerializer.Deserialize<AuthenticationInput>(authJson) ?? new AuthenticationInput();
                }
                catch(JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Authentication from session: {Error}", ex.Message);
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning("Failed use session: {Error}", ex.Message);
        }
        return new AuthenticationInput();
    }

    public List<Header> LoadHeadersFromSession(ISession session)
    {
        try
        {
            var headers = session.GetString("CustomHeaders");
            if(!string.IsNullOrEmpty(headers))
            {
                try
                {
                    return JsonSerializer.Deserialize<List<Header>>(headers) ?? new List<Header>();
                }
                catch(JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Authentication from session: {Error}", ex.Message);
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning("Failed use session: {Error}", ex.Message);
        }
        return new List<Header>();
    }

    public void SaveToSession(ISession session, AuthenticationInput authentication)
    {
        var authJson = JsonSerializer.Serialize(authentication);
        session.SetString("AuthenticationInput", authJson);
    }

    public void SaveToSession(ISession session, List<Header> customHeaders)
    {
        var headers = JsonSerializer.Serialize(customHeaders);
        session.SetString("CustomHeaders", headers);
    }

    public void PrepareAuthentication(HttpClient httpClient, AuthenticationInput authentication)
    {
        if(authentication.Type == AuthenticationType.Bearer)
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authentication.BearerToken);
            _logger.LogInformation("Added Bearer authentication header");
        }
        else if(authentication.Type == AuthenticationType.Basic)
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(
                $"{authentication.BasicUsername}:{authentication.BasicPassword}"));
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            _logger.LogInformation("Added Basic authentication header");
        }
        else if(authentication.Type == AuthenticationType.ApiKey &&
                 !string.IsNullOrEmpty(authentication.ApiKeyValue) &&
                 authentication.ApiKeyLocation == "Header")
        {
            var headerName = string.IsNullOrEmpty(authentication.ApiKeyName) ? "X-Api-Key" : authentication.ApiKeyName;
            httpClient.DefaultRequestHeaders.Add(headerName, authentication.ApiKeyValue);
            _logger.LogInformation("Added API Key to header: {HeaderName}={HeaderValue}", headerName, authentication.ApiKeyValue);
        }
    }
}

namespace WebApiDocumentator.Services;

internal class UrlBuilder
{
    private readonly ILogger<UrlBuilder> _logger;

    public UrlBuilder(ILogger<UrlBuilder> logger)
    {
        _logger = logger;
    }

    public string BuildRequestUrl(ApiEndpointInfo endpoint, EndpointTestInput testInput)
    {
        var requestUrl = testInput.Route;

        requestUrl = ReplacePathParameters(endpoint, testInput, requestUrl);
        requestUrl = AddQueryParameters(endpoint, testInput, requestUrl);

        _logger.LogInformation("Request URL: {RequestUrl}", requestUrl);
        return requestUrl;
    }

    private string ReplacePathParameters(ApiEndpointInfo endpoint, EndpointTestInput testInput, string requestUrl)
    {
        foreach(var param in endpoint.Parameters.Where(p => p.Source == "Path" && testInput.Parameters.ContainsKey(p.Name)))
        {
            var paramValue = HttpUtility.UrlEncode(testInput.Parameters[param.Name] ?? "");

            // Patrón para encontrar {param.Name}, {param.Name:long}, {param.Name:...}
            var pattern = $"{{{param.Name}(:[^}}]*)?}}";

            // Reemplazar todas las ocurrencias que coincidan con el patrón
            requestUrl = Regex.Replace(
                requestUrl,
                pattern,
                paramValue,
                RegexOptions.IgnoreCase);
            _logger.LogInformation("Replaced route parameter: {{{ParamName}}} -> {ParamValue}", param.Name, paramValue);
        }
        return requestUrl;
    }

    private string AddQueryParameters(ApiEndpointInfo endpoint, EndpointTestInput testInput, string requestUrl)
    {
        var queryParams = BuildQueryParameters(endpoint, testInput);
        if(queryParams.Any())
        {
            requestUrl += "?" + string.Join("&", queryParams);
        }
        return requestUrl;
    }

    private List<string> BuildQueryParameters(ApiEndpointInfo endpoint, EndpointTestInput testInput)
    {
        var queryParams = new List<string>();

        foreach(var param in endpoint.Parameters.Where(p => p.Source == "Query"))
        {
            if(param.IsCollection)
            {
                AddCollectionQueryParameter(param, testInput, queryParams);
            }
            else
            {
                AddSingleQueryParameter(param, testInput, queryParams);
            }
        }

        AddApiKeyQueryParameter(testInput, queryParams);

        return queryParams;
    }

    private void AddCollectionQueryParameter(ApiParameterInfo param, EndpointTestInput testInput, List<string> queryParams)
    {
        if(testInput.Collections.TryGetValue(param.Name, out var collectionValues) && collectionValues != null)
        {
            foreach(var value in collectionValues)
            {
                if(!string.IsNullOrEmpty(value))
                {
                    queryParams.Add($"{HttpUtility.UrlEncode(param.Name)}={HttpUtility.UrlEncode(value)}");
                }
            }
        }
    }

    private void AddSingleQueryParameter(ApiParameterInfo param, EndpointTestInput testInput, List<string> queryParams)
    {
        if(testInput.Parameters.TryGetValue(param.Name, out var paramValue) && !string.IsNullOrEmpty(paramValue))
        {
            queryParams.Add($"{HttpUtility.UrlEncode(param.Name)}={HttpUtility.UrlEncode(paramValue)}");
        }
    }

    private void AddApiKeyQueryParameter(EndpointTestInput testInput, List<string> queryParams)
    {
        if(testInput.Authentication.Type == AuthenticationType.ApiKey &&
            !string.IsNullOrEmpty(testInput.Authentication.ApiKeyValue) &&
            testInput.Authentication.ApiKeyLocation == "Query")
        {
            var keyName = string.IsNullOrEmpty(testInput.Authentication.ApiKeyName) ? "apiKey" : testInput.Authentication.ApiKeyName;
            queryParams.Add($"{HttpUtility.UrlEncode(keyName)}={HttpUtility.UrlEncode(testInput.Authentication.ApiKeyValue)}");
            _logger.LogInformation("Added API Key to query: {KeyName}={KeyValue}", keyName, testInput.Authentication.ApiKeyValue);
        }
    }
}
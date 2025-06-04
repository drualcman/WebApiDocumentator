using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WebApiDocumentator.Services;

internal class RequestProcessor
{
    private readonly IHttpClientFactory HttpClientFactory;
    private readonly ILogger<RequestProcessor> logger;

    public RequestProcessor(IHttpClientFactory httpClientFactory, ILogger<RequestProcessor> logger1)
    {
        HttpClientFactory = httpClientFactory;
        logger = logger1;
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
                    logger.LogWarning("Failed to deserialize Authentication from session: {Error}", ex.Message);
                }
            }
        }
        catch(Exception ex)
        {
            logger.LogWarning("Failed use session: {Error}", ex.Message);
        }
        return new AuthenticationInput();
    }

    public void SaveAuthenticationToSession(ISession session, AuthenticationInput authentication)
    {
        var authJson = JsonSerializer.Serialize(authentication);
        session.SetString("AuthenticationInput", authJson);
    }

    HttpRequestMessage request = default;
    public async Task ValidateInput(ApiEndpointInfo endpoint, EndpointTestInput testInput,
        HttpRequest httpRequest, ModelStateDictionary modelState)
    {
        var requestUrl = BuildRequestUrl(endpoint, testInput);
        request = new HttpRequestMessage(new HttpMethod(testInput.Method), requestUrl);

        bool useMultipart = await PrepareRequestContent(endpoint, testInput, httpRequest, modelState);

        var filesRequired = endpoint.Parameters
            .Where(p => p.Source == "Form" &&
                   p.IsRequired == true &&
                   (p.Type.Equals(typeof(IFormFile).Name) || p.Type.Equals(typeof(IFormFileCollection).Name)));


        bool hasFilesRequired = filesRequired is not null && filesRequired.Any();
        if(hasFilesRequired && !useMultipart)
        {
            foreach(var file in filesRequired)
            {
                modelState.AddModelError($"TestInput.Parameters[{file.Name}]", $"Form parameter {file.Name} is required.");
            }
        }
        var requeredParameter = endpoint.Parameters
            .Where(p => p.IsRequired == true &&
                       p.Source != "Form" &&
                     !(p.Type.Equals(typeof(IFormFile).Name) || p.Type.Equals(typeof(IFormFileCollection).Name)));

        if(requeredParameter.Any())
        {
            foreach(var param in requeredParameter)
            {
                if(param.IsCollection)
                {
                    if(!testInput.Collections.ContainsKey(param.Name) ||
                       testInput.Collections[param.Name] == null ||
                       !testInput.Collections[param.Name].Any())
                    {
                        modelState.AddModelError($"TestInput.Collections[{param.Name}]", $"Parameter {param.Name} is required and must have at least one item.");
                    }
                    else
                    {
                        var collection = testInput.Collections[param.Name];
                        for(int i = 0; i < collection.Count; i++)
                        {
                            if(string.IsNullOrWhiteSpace(collection[i]))
                            {
                                modelState.AddModelError($"TestInput.Collections[{param.Name}][{i}]", $"Item {i + 1} of {param.Name} cannot be empty.");
                            }
                        }
                    }
                }
                else if(!testInput.Parameters.ContainsKey(param.Name) || string.IsNullOrEmpty(testInput.Parameters[param.Name]))
                {
                    modelState.AddModelError($"TestInput.Parameters[{param.Name}]", $"Parameter {param.Name} is required.");
                }
            }
        }

        if(testInput.Authentication.Type == AuthenticationType.Bearer && string.IsNullOrWhiteSpace(testInput.Authentication.BearerToken))
        {
            modelState.AddModelError("TestInput.BearerToken", "Bearer token is required.");
        }
        else if(testInput.Authentication.Type == AuthenticationType.Basic)
        {
            if(string.IsNullOrWhiteSpace(testInput.Authentication.BasicUsername))
                modelState.AddModelError("TestInput.BasicUsername", "Username is required for Basic authentication.");
            if(string.IsNullOrWhiteSpace(testInput.Authentication.BasicPassword))
                modelState.AddModelError("TestInput.BasicPassword", "Password is required for Basic authentication.");
        }
        else if(testInput.Authentication.Type == AuthenticationType.ApiKey && string.IsNullOrWhiteSpace(testInput.Authentication.ApiKeyValue))
        {
            modelState.AddModelError("TestInput.Authentication.ApiKeyValue", "API Key is required when API Key authentication is selected.");
        }
    }

    public async Task<RequestProcessingResult> ProcessRequestAsync(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        HttpRequest httpRequest,
        ModelStateDictionary modelState)
    {
        if(request != default)
        {
            var result = new RequestProcessingResult();
            using var httpClient = HttpClientFactory.CreateClient("WebApiDocumentator");
            httpClient.BaseAddress = new Uri($"{httpRequest.Scheme}://{httpRequest.Host}{httpRequest.PathBase}");

            PrepareAuthentication(httpClient, testInput.Authentication, logger);

            try
            {
                using var response = await httpClient.SendAsync(request);
                result.ResponseCodeDescription = $"[{(int)response.StatusCode}] {response.ReasonPhrase}".Trim();
                var responseContent = await response.Content.ReadAsStringAsync();

                if(!string.IsNullOrWhiteSpace(responseContent))
                {
                    try
                    {
                        var formattedJson = JsonSerializer.Serialize(
                            JsonSerializer.Deserialize<object>(responseContent),
                            new JsonSerializerOptions { WriteIndented = true });
                        result.ResponseContent = formattedJson;
                    }
                    catch
                    {
                        result.ResponseContent = responseContent;
                    }
                }

                if(!response.IsSuccessStatusCode)
                {
                    ProcessErrorResponse(responseContent, endpoint, modelState, result);
                }
            }
            catch(HttpRequestException ex)
            {
                result.ResponseContent = $"Error sending request: {ex.Message}";
                modelState.AddModelError("", $"Error sending request: {ex.Message}");
            }

            return result;
        }
        else
            throw new ArgumentNullException(nameof(request));
    }

    private string BuildRequestUrl(ApiEndpointInfo endpoint, EndpointTestInput testInput)
    {
        var requestUrl = testInput.Route;

        foreach(var param in endpoint.Parameters.Where(p => p.Source == "Path" && testInput.Parameters.ContainsKey(p.Name)))
        {
            var paramValue = HttpUtility.UrlEncode(testInput.Parameters[param.Name] ?? "");
            requestUrl = requestUrl.Replace($"{{{param.Name}}}", paramValue, StringComparison.OrdinalIgnoreCase);
            logger.LogInformation("Replaced route parameter: {{{ParamName}}} -> {ParamValue}", param.Name, paramValue);
        }

        var queryParams = new List<string>();
        foreach(var param in endpoint.Parameters.Where(p => p.Source == "Query"))
        {
            if(param.IsCollection)
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
            else if(testInput.Parameters.TryGetValue(param.Name, out var paramValue) && !string.IsNullOrEmpty(paramValue))
            {
                queryParams.Add($"{HttpUtility.UrlEncode(param.Name)}={HttpUtility.UrlEncode(paramValue)}");
            }
        }

        if(testInput.Authentication.Type == AuthenticationType.ApiKey &&
            !string.IsNullOrEmpty(testInput.Authentication.ApiKeyValue) &&
            testInput.Authentication.ApiKeyLocation == "Query")
        {
            var keyName = string.IsNullOrEmpty(testInput.Authentication.ApiKeyName) ? "apiKey" : testInput.Authentication.ApiKeyName;
            queryParams.Add($"{HttpUtility.UrlEncode(keyName)}={HttpUtility.UrlEncode(testInput.Authentication.ApiKeyValue)}");
            logger.LogInformation("Added API Key to query: {KeyName}={KeyValue}", keyName, testInput.Authentication.ApiKeyValue);
        }

        if(queryParams.Any())
        {
            requestUrl += "?" + string.Join("&", queryParams);
        }

        logger.LogInformation("Request URL: {RequestUrl}", requestUrl);
        return requestUrl;
    }

    private void PrepareAuthentication(HttpClient httpClient, AuthenticationInput authentication, ILogger logger)
    {
        if(authentication.Type == AuthenticationType.Bearer)
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authentication.BearerToken);
            logger.LogInformation("Added Bearer authentication header");
        }
        else if(authentication.Type == AuthenticationType.Basic)
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(
                $"{authentication.BasicUsername}:{authentication.BasicPassword}"));
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            logger.LogInformation("Added Basic authentication header");
        }
        else if(authentication.Type == AuthenticationType.ApiKey &&
                 !string.IsNullOrEmpty(authentication.ApiKeyValue) &&
                 authentication.ApiKeyLocation == "Header")
        {
            var headerName = string.IsNullOrEmpty(authentication.ApiKeyName) ? "X-Api-Key" : authentication.ApiKeyName;
            httpClient.DefaultRequestHeaders.Add(headerName, authentication.ApiKeyValue);
            logger.LogInformation("Added API Key to header: {HeaderName}={HeaderValue}", headerName, authentication.ApiKeyValue);
        }
    }

    private async Task<bool> PrepareRequestContent(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        HttpRequest httpRequest,
        ModelStateDictionary modelState)
    {
        bool useMultipart = false;
        if(endpoint.Parameters.Any(p => p.IsFromBody))
        {
            var bodyParam = endpoint.Parameters.First(p => p.IsFromBody);
            if(testInput.Parameters.TryGetValue(bodyParam.Name, out string? bodyValue))
            {
                if(bodyParam.IsRequired && string.IsNullOrWhiteSpace(bodyValue))
                {
                    modelState.AddModelError($"TestInput.Parameters[{bodyParam.Name}]", "Body must be valid JSON.");
                }
                else
                {
                    try
                    {
                        string json = "";
                        if(!string.IsNullOrEmpty(bodyValue))
                        {
                            var jsonObject = JsonSerializer.Deserialize<object>(bodyValue);
                            json = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = true });
                        }
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    }
                    catch(JsonException)
                    {
                        modelState.AddModelError($"TestInput.Parameters[{bodyParam.Name}]", "Body must be valid JSON.");
                    }
                }
            }
            else
            {
                modelState.AddModelError("", "Request body is missing for the expected parameter.");
            }
        }
        else if(endpoint.Parameters.Any(p => p.Source == "Form"))
        {
            useMultipart = await PrepareFormContent(endpoint, testInput, httpRequest, request, modelState, logger);
        }
        return useMultipart;
    }

    private async Task<bool> PrepareFormContent(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        HttpRequest httpRequest,
        HttpRequestMessage request,
        ModelStateDictionary modelState,
        ILogger logger)
    {
        IFormCollection form = await httpRequest.ReadFormAsync();
        testInput.Files = new Dictionary<string, IFormFile>();

        foreach(IFormFile file in form.Files)
        {
            string? extractedKey = ExtractKeyFromFieldName(file.Name);
            if(!string.IsNullOrWhiteSpace(extractedKey))
            {
                testInput.Files[extractedKey] = file;
                logger.LogInformation("Received file: {Key} ({Length} bytes)", extractedKey, file.Length);
            }
        }

        bool useMultipart = testInput.Files != null && testInput.Files.Count > 0;

        if(useMultipart)
        {
            var multipartContent = new MultipartFormDataContent();
            AddFormParametersToContent(testInput.Parameters, multipartContent, modelState);
            AddFilesToContent(testInput.Files, multipartContent);
            request.Content = multipartContent;
            logger.LogInformation("Added multipart/form-data content");
        }
        else
        {
            List<KeyValuePair<string, string>> formValues = new List<KeyValuePair<string, string>>();
            foreach(KeyValuePair<string, string?> parameter in testInput.Parameters)
            {
                if(!string.IsNullOrWhiteSpace(parameter.Value))
                {
                    formValues.Add(new KeyValuePair<string, string>(parameter.Key, parameter.Value));
                }
                else
                {
                    modelState.AddModelError($"TestInput.Parameters[{parameter.Key}]", $"Form parameter {parameter.Key} is required.");
                }
            }
            request.Content = new FormUrlEncodedContent(formValues);
            logger.LogInformation("Added application/x-www-form-urlencoded content");
        }
        return useMultipart;
    }

    private void AddFormParametersToContent(
        Dictionary<string, string?> parameters,
        MultipartFormDataContent content,
        ModelStateDictionary modelState)
    {
        foreach(var parameter in parameters)
        {
            if(!string.IsNullOrWhiteSpace(parameter.Value))
            {
                content.Add(new StringContent(parameter.Value), parameter.Key);
            }
            else
            {
                modelState.AddModelError($"TestInput.Parameters[{parameter.Key}]", $"Form parameter {parameter.Key} is required.");
            }
        }
    }

    private void AddFilesToContent(
        Dictionary<string, IFormFile>? files,
        MultipartFormDataContent content)
    {
        if(files == null)
            return;

        foreach(var filePair in files)
        {
            if(filePair.Value != null)
            {
                var fileContent = new StreamContent(filePair.Value.OpenReadStream());
                content.Add(fileContent, filePair.Key, filePair.Value.FileName);
            }
        }
    }

    private void ProcessErrorResponse(
        string responseContent,
        ApiEndpointInfo endpoint,
        ModelStateDictionary modelState,
        RequestProcessingResult result)
    {
        if(string.IsNullOrWhiteSpace(result.ResponseContent))
            result.ResponseContent = $"Request error: {result.ResponseCodeDescription}".Trim();

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
                        if(errorMessages != null && endpoint.Parameters.Any(p => p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase)))
                        {
                            foreach(var errorMessage in errorMessages)
                            {
                                if(errorMessage != null)
                                {
                                    modelState.AddModelError($"TestInput.Parameters[{paramName}]", errorMessage.ToString());
                                }
                            }
                        }
                        else
                        {
                            modelState.AddModelError("", $"Request error: {error.Key} - {error.Value}");
                        }
                    }
                }
            }
            catch
            {
                result.ResponseContent += $"\nRequest error: {result.ResponseCodeDescription}. {responseContent}".Trim();
            }
        }

        modelState.AddModelError("", result.ResponseContent);
    }

    private string? ExtractKeyFromFieldName(string fieldName)
    {
        int start = fieldName.IndexOf('[');
        int end = fieldName.IndexOf(']');
        if(start >= 0 && end > start)
        {
            return fieldName.Substring(start + 1, end - start - 1);
        }
        return null;
    }
}
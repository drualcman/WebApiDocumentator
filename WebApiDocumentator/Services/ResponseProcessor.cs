namespace WebApiDocumentator.Services;
internal class ResponseProcessor
{
    private readonly ILogger<ResponseProcessor> _logger;

    public ResponseProcessor(ILogger<ResponseProcessor> logger)
    {
        _logger = logger;
    }

    public void ProcessErrorResponse(
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
                    ProcessErrorDetails(errors, endpoint, modelState);
                }
            }
            catch
            {
                result.ResponseContent += $"\nRequest error: {result.ResponseCodeDescription}. {responseContent}".Trim();
            }
        }

        modelState.AddModelError("", result.ResponseContent);
    }

    private void ProcessErrorDetails(JsonElement errors, ApiEndpointInfo endpoint, ModelStateDictionary modelState)
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

    public async Task FormatResponseContent(HttpResponseMessage response, RequestProcessingResult result)
    {
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
    }
}

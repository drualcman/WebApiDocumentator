namespace WebApiDocumentator.Builders;

internal class JsonBodyBuilder
{
    public void PrepareJsonBodyContent(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        ModelStateDictionary modelState,
        HttpRequestMessage request)
    {
        var bodyParam = endpoint.Parameters.First(p => p.IsFromBody);
        if(testInput.Parameters.TryGetValue(bodyParam.Name, out string bodyValue))
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
}
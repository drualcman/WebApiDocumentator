namespace WebApiDocumentator.Services;
internal class RequestValidator
{
    public void ValidateInput(ApiEndpointInfo endpoint, EndpointTestInput testInput, ModelStateDictionary modelState)
    {
        ValidateRequiredParameters(endpoint, testInput, modelState);
        ValidateAuthentication(testInput, modelState);
        ValidateCustomHeaders(testInput, modelState);
    }

    private void ValidateRequiredParameters(ApiEndpointInfo endpoint, EndpointTestInput testInput, ModelStateDictionary modelState)
    {
        var filesRequired = endpoint.Parameters
            .Where(p => p.Source == "Form" &&
                   p.IsRequired == true &&
                   (p.Type.Equals(typeof(IFormFile).Name) || p.Type.Equals(typeof(IFormFileCollection).Name)));

        bool hasFilesRequired = filesRequired is not null && filesRequired.Any();
        if(hasFilesRequired && !(testInput.Files?.Any() ?? false))
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

        foreach(var param in requeredParameter)
        {
            if(param.IsCollection)
            {
                ValidateCollectionParameter(param, testInput, modelState);
            }
            else
            {
                ValidateSingleParameter(param, testInput, modelState);
            }
        }
    }

    private void ValidateCollectionParameter(ApiParameterInfo param, EndpointTestInput testInput, ModelStateDictionary modelState)
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

    private void ValidateSingleParameter(ApiParameterInfo param, EndpointTestInput testInput, ModelStateDictionary modelState)
    {
        if(!testInput.Parameters.ContainsKey(param.Name) || string.IsNullOrEmpty(testInput.Parameters[param.Name]))
        {
            modelState.AddModelError($"TestInput.Parameters[{param.Name}]", $"Parameter {param.Name} is required.");
        }
    }

    private void ValidateAuthentication(EndpointTestInput testInput, ModelStateDictionary modelState)
    {
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

    private void ValidateCustomHeaders(EndpointTestInput testInput, ModelStateDictionary modelState)
    {
        if(testInput.CustomHeaders is not null && testInput.CustomHeaders.Any())
        {
            foreach(var header in testInput.CustomHeaders)
            {
                if(string.IsNullOrWhiteSpace(header.Key))
                {
                    modelState.AddModelError("CustomHeaders", "Header name cannot be empty.");
                }
                if(string.IsNullOrWhiteSpace(header.Value?.ToString()))
                {
                    modelState.AddModelError("CustomHeaders", $"Value for header '{header.Key}' cannot be empty.");
                }
            }
        }
    }
}

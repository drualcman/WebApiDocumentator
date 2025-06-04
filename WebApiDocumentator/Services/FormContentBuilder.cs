namespace WebApiDocumentator.Services;

internal class FormContentBuilder
{
    private readonly ILogger<FormContentBuilder> _logger;

    public FormContentBuilder(ILogger<FormContentBuilder> logger)
    {
        _logger = logger;
    }

    public async Task<bool> PrepareFormContent(
        ApiEndpointInfo endpoint,
        EndpointTestInput testInput,
        HttpRequest httpRequest,
        ModelStateDictionary modelState,
        HttpRequestMessage request)
    {
        IFormCollection form = await httpRequest.ReadFormAsync();
        testInput.Files = new Dictionary<string, IFormFile>();

        ExtractFilesFromForm(form, testInput);

        bool useMultipart = testInput.Files != null && testInput.Files.Count > 0;

        if(useMultipart)
        {
            var multipartContent = new MultipartFormDataContent();
            AddFormParametersToContent(testInput.Parameters, multipartContent, modelState);
            AddFilesToContent(testInput.Files, multipartContent);
            request.Content = multipartContent;
            _logger.LogInformation("Added multipart/form-data content");
        }
        else
        {
            PrepareUrlEncodedContent(testInput.Parameters, modelState, request);
        }
        return useMultipart;
    }

    private void ExtractFilesFromForm(IFormCollection form, EndpointTestInput testInput)
    {
        foreach(IFormFile file in form.Files)
        {
            string extractedKey = ExtractKeyFromFieldName(file.Name);
            if(!string.IsNullOrWhiteSpace(extractedKey))
            {
                testInput.Files[extractedKey] = file;
                _logger.LogInformation("Received file: {Key} ({Length} bytes)", extractedKey, file.Length);
            }
        }
    }

    private void PrepareUrlEncodedContent(
        Dictionary<string, string> parameters,
        ModelStateDictionary modelState,
        HttpRequestMessage request)
    {
        List<KeyValuePair<string, string>> formValues = new List<KeyValuePair<string, string>>();
        foreach(KeyValuePair<string, string> parameter in parameters)
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
        _logger.LogInformation("Added application/x-www-form-urlencoded content");
    }

    private void AddFormParametersToContent(
        Dictionary<string, string> parameters,
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
        Dictionary<string, IFormFile> files,
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

    private string ExtractKeyFromFieldName(string fieldName)
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
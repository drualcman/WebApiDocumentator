namespace WebApiDocumentator.Services;
internal class EndpointService
{
    private readonly CompositeMetadataProvider MetadataProvider;
    private readonly JsonSchemaGenerator JsonSchemaGenerator;
    private readonly IApiDescriptionGroupCollectionProvider ApiDescriptionGroupCollectionProvider;

    public EndpointService(
        CompositeMetadataProvider metadataProvider,
        IApiDescriptionGroupCollectionProvider apiDescriptionGroupCollectionProvider)
    {
        MetadataProvider = metadataProvider;
        JsonSchemaGenerator = new JsonSchemaGenerator();
        ApiDescriptionGroupCollectionProvider = apiDescriptionGroupCollectionProvider;
    }

    public List<EndpointGroupNode> GetGroupedEndpoints() => MetadataProvider.GetGroupedEndpoints();

    public ApiEndpointInfo FindEndpointById(List<EndpointGroupNode> groups, string id)
    {
        if(string.IsNullOrWhiteSpace(id))
            return null;
        return groups.SelectMany(g => GetAllEndpoints(g)).FirstOrDefault(e => e.Id == id);
    }

    public string GenerateExampleRequestUrl(ApiEndpointInfo endpoint)
    {
        var url = endpoint.Route;

        foreach(var param in endpoint.Parameters.Where(p => p.Source == "Path"))
        {
            var exampleValue = GenerateParameterExample(param);
            url = url.Replace($"{{{param.Name}}}", HttpUtility.UrlEncode(exampleValue), StringComparison.OrdinalIgnoreCase);
        }

        var queryParams = new List<string>();
        foreach(var param in endpoint.Parameters.Where(p => p.Source == "Query"))
        {
            if(param.IsCollection)
            {
                var exampleValue1 = GenerateParameterExample(param);
                var exampleValue2 = GenerateParameterExample(param);
                queryParams.Add($"{HttpUtility.UrlEncode(param.Name)}={HttpUtility.UrlEncode(exampleValue1)}");
                queryParams.Add($"{HttpUtility.UrlEncode(param.Name)}={HttpUtility.UrlEncode(exampleValue2)}");
            }
            else
            {
                var exampleValue = GenerateParameterExample(param);
                queryParams.Add($"{HttpUtility.UrlEncode(param.Name)}={HttpUtility.UrlEncode(exampleValue)}");
            }
        }

        if(queryParams.Any())
        {
            url += "?" + string.Join("&", queryParams);
        }

        return url;
    }

    public string GenerateRequestBodyJson(ApiEndpointInfo endpoint)
    {
        var bodyParam = endpoint.Parameters.FirstOrDefault(p => p.IsFromBody);
        return bodyParam?.Schema != null ? JsonSchemaGenerator.GetExampleAsJsonString(bodyParam.Schema) : null;
    }

    public string GetFormEnctype(ApiEndpointInfo endpoint)
    {
        if(endpoint == null)
            return "application/x-www-form-urlencoded";

        foreach(var param in endpoint.Parameters.Where(p => p.Source == "Form"))
        {
            Type type = GetTypeFromName(param.Type);
            if(type is not null && type == typeof(byte[]) || type?.Name == "IFormFile")
            {
                return "multipart/form-data";
            }

            if(type != null)
            {
                foreach(var prop in GetFormProperties(param.Type))
                {
                    if(prop.PropertyType == typeof(byte[]) || prop.PropertyType.Name == "IFormFile")
                    {
                        return "multipart/form-data";
                    }
                }
            }
        }

        return "application/x-www-form-urlencoded";
    }

    public int CountLinesInSchema(Dictionary<string, object> schema)
    {
        int result = 5;
        if(schema != null)
        {
            var json = JsonSchemaGenerator.GetExampleAsJsonString(schema);
            if(!string.IsNullOrWhiteSpace(json))
            {
                result = json.Split('\n').Length;
            }
        }
        return result;
    }

    public IEnumerable<ApiEndpointInfo> GetAllEndpoints(EndpointGroupNode node)
    {
        var endpoints = new List<ApiEndpointInfo>(node.Endpoints);
        foreach(var child in node.Children)
        {
            endpoints.AddRange(GetAllEndpoints(child));
        }
        return endpoints;
    }

    public string GetApiVersion() => $"v{ApiDescriptionGroupCollectionProvider.ApiDescriptionGroups.Version + 1}";

    public string GenerateFlatSchema(Dictionary<string, object> schema) => JsonSchemaGenerator.GetExampleAsJsonString(schema);

    public Type GetTypeFromName(string typeName)
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == typeName);
    }

    public IEnumerable<PropertyInfo> GetFormProperties(string typeName)
    {
        Type type = GetTypeFromName(typeName);
        return type?.GetProperties(BindingFlags.Public | BindingFlags.Instance) ?? Enumerable.Empty<PropertyInfo>();
    }

    private string GenerateParameterExample(ApiParameterInfo param)
    {
        var typeToUse = param.IsCollection ? param.CollectionElementType : param.Type;

        if(param.Schema != null)
        {
            var example = JsonSchemaGenerator.GetExampleAsJsonString(param.Schema);
            if(!string.IsNullOrEmpty(example))
            {
                return example.Trim('"');
            }
        }

        return typeToUse switch
        {
            "string" => "example",
            "int" or "integer" => "123",
            "float" or "double" or "number" => "123.45",
            "bool" or "boolean" => "true",
            _ => "example"
        };
    }
}
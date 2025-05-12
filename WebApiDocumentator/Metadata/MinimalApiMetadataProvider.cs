namespace WebApiDocumentator.Metadata;

internal class MinimalApiMetadataProvider : IMetadataProvider
{
    private readonly EndpointDataSource _endpointDataSource;
    private readonly Dictionary<string, string> _xmlDocs;
    private readonly ParameterDescriptionBuilder _descriptionBuilder;
    private readonly JsonSchemaGenerator _schemaGenerator;

    public MinimalApiMetadataProvider(EndpointDataSource endpointDataSource, ILogger<MinimalApiMetadataProvider> logger)
    {
        _endpointDataSource = endpointDataSource;
        var loader = new XmlDocumentationLoader(logger);
        _xmlDocs = loader.LoadXmlDocumentation(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());
        _descriptionBuilder = new ParameterDescriptionBuilder(_xmlDocs, logger);
        _schemaGenerator = new JsonSchemaGenerator(_xmlDocs);
    }

    public List<ApiEndpointInfo> GetEndpoints()
    {
        var endpoints = new List<ApiEndpointInfo>();
        var excludedRoutes = new[] { "/openapi" };

        var endpointToTrace = _endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e =>
                !e.Metadata.OfType<CompiledPageActionDescriptor>().Any() &&
                !e.Metadata.OfType<ControllerActionDescriptor>().Any() &&
                !excludedRoutes.Any(excluded => e.RoutePattern.RawText?.StartsWith(excluded, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        foreach(var endpoint in endpointToTrace)
        {
            var httpMethods = endpoint.Metadata
                .OfType<HttpMethodMetadata>()
                .FirstOrDefault()?.HttpMethods;

            if(httpMethods != null && httpMethods.Count != 0)
            {
                var methodInfo = endpoint.Metadata
                    .OfType<MethodInfo>()
                    .FirstOrDefault();

                if(methodInfo != null)
                {
                    var routeParameters = endpoint.RoutePattern.Parameters
                        .Select(p => p.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var (parameters, description) = _descriptionBuilder.BuildParameters(
                        methodInfo,
                        routeParameters,
                        p => !IsHttpContextOrService(p.ParameterType),
                        endpoint.Metadata);

                    var returnType = TypeNameHelper.GetFriendlyTypeName(methodInfo.ReturnType);
                    var returnSchema = _schemaGenerator.GenerateJsonSchema(methodInfo.ReturnType, new HashSet<Type>());
                    var exampleJson = _schemaGenerator.GetExampleAsJsonString(returnSchema);

                    if(typeof(IResult).IsAssignableFrom(methodInfo.ReturnType))
                    {
                        var resultType = InferResultType(methodInfo, endpoint.Metadata);
                        if(resultType != null)
                        {
                            returnType = TypeNameHelper.GetFriendlyTypeName(resultType);
                            returnSchema = _schemaGenerator.GenerateJsonSchema(resultType, new HashSet<Type>());
                            exampleJson = _schemaGenerator.GetExampleAsJsonString(returnSchema);
                        }
                    }

                    // Clean up summary to avoid compiler-generated names
                    var summary = XmlDocumentationHelper.GetXmlSummary(_xmlDocs, methodInfo)?.Trim().TrimEnd('.');
                    if(string.IsNullOrWhiteSpace(summary) || methodInfo.Name.Contains("<") || methodInfo.Name.Contains("b__"))
                    {
                        summary = $"{httpMethods[0]} {endpoint.RoutePattern.RawText}";
                    }

                    // Remove parameter details from description to avoid redundancy
                    var cleanDescription = description;
                    if(!string.IsNullOrWhiteSpace(description))
                    {
                        var paramIndex = description.IndexOf("\nParameters:\n", StringComparison.Ordinal);
                        if(paramIndex >= 0)
                        {
                            cleanDescription = description.Substring(0, paramIndex).Trim();
                        }
                    }

                    var endpointInfo = new ApiEndpointInfo
                    {
                        Id = EndpointHelper.GenerateEndpointId(httpMethods[0], endpoint.RoutePattern.RawText?.ToLowerInvariant() ?? ""), // Assign unique identifier
                        Route = endpoint.RoutePattern.RawText?.ToLowerInvariant() ?? "",
                        HttpMethod = httpMethods[0],
                        Summary = summary,
                        Description = cleanDescription,
                        ReturnType = returnType,
                        Parameters = parameters,
                        ReturnSchema = returnSchema,
                        ExampleJson = exampleJson
                    };

                    endpoints.Add(endpointInfo);
                }
            }
        }

        return endpoints;
    }

    private bool IsHttpContextOrService(Type type)
    {
        return type == typeof(HttpContext) ||
               type == typeof(CancellationToken) ||
               type == typeof(IFormFile) ||
               typeof(IHttpContextAccessor).IsAssignableFrom(type);
    }

    private Type? InferResultType(MethodInfo methodInfo, EndpointMetadataCollection metadata)
    {
        var producesMetadata = metadata
            .OfType<IProducesResponseTypeMetadata>()
            .FirstOrDefault();

        if(producesMetadata != null && producesMetadata.Type != null)
        {
            return producesMetadata.Type;
        }

        if(methodInfo.ReturnType == typeof(IResult))
            return null;

        return methodInfo.ReturnType;
    }
}
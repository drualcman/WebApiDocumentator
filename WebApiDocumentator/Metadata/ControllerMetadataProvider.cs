namespace WebApiDocumentator.Metadata;

internal class ControllerMetadataProvider : IMetadataProvider
{
    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _xmlDocs;
    private readonly ILogger<ControllerMetadataProvider> _logger;
    private readonly ParameterDescriptionBuilder _descriptionBuilder;
    private readonly JsonSchemaGenerator _schemaGenerator;

    public ControllerMetadataProvider(ILogger<ControllerMetadataProvider> logger)
    {
        _assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var loader = new XmlDocumentationLoader(logger);
        var assemblies = new[] { _assembly }
            .Concat(_assembly.GetReferencedAssemblies()
                .Select(asm => Assembly.Load(asm)));
        _xmlDocs = loader.LoadXmlDocumentation(assemblies);
        _descriptionBuilder = new ParameterDescriptionBuilder(_xmlDocs, logger);
        _schemaGenerator = new JsonSchemaGenerator(_xmlDocs);
        _logger = logger;
    }

    public List<ApiEndpointInfo> GetEndpoints()
    {
        var result = new List<ApiEndpointInfo>();
        var excludedRoutes = new[] { "/get-metadata", "/openapi" };
        var processedControllers = new HashSet<Type>();
        var processedMethods = new HashSet<string>();

        var controllerTypes = _assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Distinct()
            .OrderBy(t => t.FullName)
            .ToList();

        foreach(var controllerType in controllerTypes)
        {
            if(!processedControllers.Contains(controllerType))
            {
                processedControllers.Add(controllerType);

                var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
                var controllerName = controllerType.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
                    ? controllerType.Name.Substring(0, controllerType.Name.Length - "Controller".Length).ToLowerInvariant()
                    : controllerType.Name.ToLowerInvariant();
                var routePrefix = routeAttr?.Template?.Replace("[controller]", controllerName).ToLowerInvariant() ?? controllerName;

                var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .OrderBy(m => m.Name)
                    .ToList();

                foreach(var method in methods)
                {
                    var paramTypes = string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName ?? "Unknown"));
                    var methodKey = $"{controllerType.FullName}.{method.Name}({paramTypes})";

                    if(!processedMethods.Contains(methodKey))
                    {
                        var httpAttrs = method.GetCustomAttributes()
                            .OfType<HttpMethodAttribute>()
                            .ToList();

                        if(httpAttrs.Any())
                        {
                            var httpAttr = httpAttrs.First();
                            var httpMethod = httpAttr.HttpMethods.FirstOrDefault()?.ToUpper() ?? "UNKNOWN";
                            var methodRoute = GetMethodRoute(method).ToLowerInvariant();
                            var fullRoute = CombineRoute(routePrefix, methodRoute).ToLowerInvariant();

                            if(!excludedRoutes.Any(excluded => fullRoute.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
                            {
                                var routeParameters = GetRouteParameters(fullRoute);

                                var (parameters, description) = _descriptionBuilder.BuildParameters(method, routeParameters);

                                // Log if description or summary is missing
                                if(string.IsNullOrWhiteSpace(description))
                                {
                                    description = $"Handles {httpMethod} requests for {fullRoute}";
                                    _logger.LogWarning("No XML summary found for method {MethodKey}. Using fallback: {Fallback}", methodKey, description);
                                }

                                // Ensure parameter descriptions are set
                                var methodXmlKey = XmlDocumentationHelper.GetXmlMemberName(method);
                                foreach(var param in parameters)
                                {
                                    if(string.IsNullOrWhiteSpace(param.Description))
                                    {
                                        var paramXml = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name) ?? "Route parameter";
                                        param.Description = paramXml.Trim().TrimEnd('.');
                                        if(param.Description == "Route parameter")
                                        {
                                            _logger.LogWarning("No XML <param> documentation found for parameter {ParamName} in method {MethodKey}", param.Name, methodKey);
                                        }
                                    }
                                }
                                var schema = _schemaGenerator.GenerateJsonSchema(method.ReturnType, new HashSet<Type>());
                                var endpoint = new ApiEndpointInfo
                                {
                                    HttpMethod = httpMethod,
                                    Route = fullRoute,
                                    Summary = XmlDocumentationHelper.GetXmlSummary(_xmlDocs, method)?.Trim().TrimEnd('.') ?? method.Name,
                                    Description = description,
                                    ReturnType = TypeNameHelper.GetFriendlyTypeName(method.ReturnType),
                                    ReturnSchema = schema,
                                    Parameters = parameters,
                                    ExampleJson = _schemaGenerator.GetExampleAsJsonString(schema)
                                };

                                result.Add(endpoint);
                                processedMethods.Add(methodKey);
                            }
                        }
                    }
                }
            }
        }

        var filteredResult = result
            .GroupBy(e => (e.Route, e.HttpMethod))
            .Select(g =>
            {
                var endpoints = g.ToList();
                return endpoints
                    .OrderByDescending(e => e.Parameters.Count)
                    .ThenByDescending(e => e.ReturnType != "Unknown")
                    .ThenByDescending(e => e.Summary != null && !e.Summary.Contains(" ("))
                    .First();
            })
            .Where(e => e.ReturnType != "Unknown" || e.Parameters.Any())
            .ToList();

        return filteredResult;
    }

    private HashSet<string> GetRouteParameters(string route)
    {
        var routeParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(route, @"{([^}]+)}");
        foreach(Match match in matches)
        {
            var paramName = match.Groups[1].Value.Split(':').First();
            routeParameters.Add(paramName);
        }
        return routeParameters;
    }

    private string CombineRoute(string prefix, string route)
    {
        prefix = prefix.TrimEnd('/');
        route = route.TrimStart('/');
        return string.IsNullOrEmpty(route) ? prefix : $"{prefix}/{route}";
    }

    private string GetMethodRoute(MethodInfo method)
    {
        var routeAttr = method.GetCustomAttribute<RouteAttribute>();
        if(routeAttr != null)
            return routeAttr.Template;

        var httpAttr = method.GetCustomAttribute<HttpMethodAttribute>();
        return httpAttr?.Template ?? "";
    }
}
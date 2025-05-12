namespace WebApiDocumentator.Metadata;

internal class ControllerMetadataProvider : IMetadataProvider
{
    private readonly Assembly[] _assemblies;
    private readonly Dictionary<string, string> _xmlDocs;
    private readonly ILogger<ControllerMetadataProvider> _logger;
    private readonly ParameterDescriptionBuilder _descriptionBuilder;
    private readonly JsonSchemaGenerator _schemaGenerator;

    public ControllerMetadataProvider(ILogger<ControllerMetadataProvider> logger)
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        _assemblies = new[] { entryAssembly }
            .Concat(entryAssembly.GetReferencedAssemblies()
                .Select(asm => Assembly.Load(asm))
                .Where(asm => asm != null))
            .Distinct()
            .ToArray();
        var loader = new XmlDocumentationLoader(logger);
        _xmlDocs = loader.LoadXmlDocumentation(_assemblies);
        _descriptionBuilder = new ParameterDescriptionBuilder(_xmlDocs, logger);
        _schemaGenerator = new JsonSchemaGenerator(_xmlDocs);
        _logger = logger;
    }

    public List<ApiEndpointInfo> GetEndpoints()
    {
        var result = new List<ApiEndpointInfo>();
        var excludedRoutes = new[] { "/openapi" };
        var processedControllers = new HashSet<Type>();
        var processedMethods = new HashSet<string>();

        var controllerTypes = _assemblies
            .SelectMany(asm => asm.GetTypes())
            .Where(t => t.GetCustomAttribute<ApiControllerAttribute>() != null && !t.IsAbstract && !t.IsInterface)
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
                    .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
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

                        foreach(var httpAttr in httpAttrs)
                        {
                            var httpMethod = httpAttr.HttpMethods.FirstOrDefault()?.ToUpper() ?? "UNKNOWN";
                            var methodRoute = httpAttr.Template?.ToLowerInvariant() ?? GetMethodRoute(method).ToLowerInvariant();

                            // Usar la ruta absoluta si está definida, sin combinar con el prefijo
                            var fullRoute = methodRoute.StartsWith(".well-known/") || methodRoute.StartsWith("/")
                                ? methodRoute.TrimStart('/')
                                : CombineRoute(routePrefix, methodRoute).ToLowerInvariant();

                            if(!excludedRoutes.Any(excluded => fullRoute.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
                            {
                                var routeParameters = GetRouteParameters(fullRoute);

                                var (parameters, description) = _descriptionBuilder.BuildParameters(method, routeParameters);

                                // Log si falta descripción o resumen
                                if(string.IsNullOrWhiteSpace(description))
                                {
                                    description = $"Handles {httpMethod} requests for {fullRoute}";
                                    _logger.LogWarning("No XML summary found for method {MethodKey}. Using fallback: {Fallback}", methodKey, description);
                                }

                                // Asegurar descripciones de parámetros
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

                                // Determinar el tipo de retorno
                                Type returnType = method.ReturnType;
                                var producesResponse = method.GetCustomAttributes()
                                    .OfType<ProducesResponseTypeAttribute>()
                                    .FirstOrDefault(attr => attr.StatusCode == 200);
                                if(producesResponse != null && producesResponse.Type != null)
                                {
                                    returnType = producesResponse.Type;
                                }
                                else if(returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                                {
                                    returnType = returnType.GetGenericArguments()[0];
                                }
                                else if(returnType == typeof(Task))
                                {
                                    returnType = typeof(void);
                                }

                                var schema = _schemaGenerator.GenerateJsonSchema(returnType, new HashSet<Type>());
                                var endpoint = new ApiEndpointInfo
                                {
                                    Id = EndpointHelper.GenerateEndpointId(httpMethod, fullRoute),
                                    HttpMethod = httpMethod,
                                    Route = fullRoute,
                                    Summary = XmlDocumentationHelper.GetXmlSummary(_xmlDocs, method)?.Trim().TrimEnd('.') ?? method.Name,
                                    Description = description,
                                    ReturnType = TypeNameHelper.GetFriendlyTypeName(returnType),
                                    ReturnSchema = schema,
                                    Parameters = parameters,
                                    ExampleJson = _schemaGenerator.GetExampleAsJsonString(schema)
                                };

                                result.Add(endpoint);
                                _logger.LogInformation("Added endpoint: Id={Id}, Method={Method}, Route={Route}", endpoint.Id, httpMethod, fullRoute);
                            }
                        }
                        processedMethods.Add(methodKey);
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

        var httpAttrs = method.GetCustomAttributes()
            .OfType<HttpMethodAttribute>()
            .ToList();

        if(httpAttrs.Any())
        {
            if(httpAttrs.Count > 1)
            {
                _logger.LogWarning("Multiple HttpMethodAttribute found on method {MethodName}. Using the first one for fallback. Attributes: {Attributes}",
                    method.Name,
                    string.Join(", ", httpAttrs.Select(a => $"{a.GetType().Name}(Template={a.Template})")));
            }
            return httpAttrs.First().Template ?? "";
        }

        return "";
    }
}
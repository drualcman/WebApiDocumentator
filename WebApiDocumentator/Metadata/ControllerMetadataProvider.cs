using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WebApiDocumentator.Metadata;

internal class ControllerMetadataProvider : IMetadataProvider
{
    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _xmlDocs;
    private readonly ILogger<ControllerMetadataProvider> _logger;

    public ControllerMetadataProvider(ILogger<ControllerMetadataProvider> logger)
    {
        _assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        _xmlDocs = LoadXmlDocumentation();
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
                                // Get route parameters
                                var routeParameters = GetRouteParameters(fullRoute);

                                // Get method description
                                var methodSummary = GetXmlSummary(method) ?? method.Name;
                                methodSummary = methodSummary.Trim().TrimEnd('.');

                                // Build description with parameter information
                                var methodXmlKey = GetXmlMemberName(method);
                                var parameterDescriptions = new List<string>();
                                foreach(var param in method.GetParameters())
                                {
                                    var paramDescription = GetXmlParamSummary(methodXmlKey, param.Name) ?? "Route parameter";
                                    paramDescription = paramDescription.Trim().TrimEnd('.');
                                    var paramType = GetFriendlyTypeName(param.ParameterType);
                                    var paramSource = GetParameterSource(param, routeParameters);
                                    var paramInfo = $"{param.Name} ({paramType}, {paramSource}): {paramDescription}";
                                    parameterDescriptions.Add(paramInfo);
                                }

                                // Build description with new lines
                                var description = methodSummary;
                                if(parameterDescriptions.Any())
                                {
                                    description += "\nParameters:\n" + string.Join("\n", parameterDescriptions);
                                }

                                var endpoint = new ApiEndpointInfo
                                {
                                    HttpMethod = httpMethod,
                                    Route = fullRoute,
                                    Summary = methodSummary,
                                    Description = description,
                                    ReturnType = GetFriendlyTypeName(method.ReturnType),
                                    ReturnSchema = GenerateJsonSchema(method.ReturnType, new HashSet<Type>()),
                                    Parameters = GetParameters(method, routeParameters)
                                };

                                result.Add(endpoint);
                                processedMethods.Add(methodKey);
                            }
                        }
                    }
                }
            }
        }

        // Filter duplicates and invalid endpoints
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
            .Where(e => e.ReturnType != "Unknown" || e.Parameters.Any() || e.Route == "api/simple")
            .ToList();

        return filteredResult;
    }

    private HashSet<string> GetRouteParameters(string route)
    {
        var routeParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(route, @"{([^}]+)}");
        foreach(Match match in matches)
        {
            var paramName = match.Groups[1].Value.Split(':').First(); // Ignore constraints like {name:string}
            routeParameters.Add(paramName);
        }
        return routeParameters;
    }

    private string GetParameterSource(ParameterInfo parameter, HashSet<string> routeParameters)
    {
        if(routeParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
            return "Path";
        if(parameter.GetCustomAttribute<FromQueryAttribute>() != null)
            return "Query";
        if(parameter.GetCustomAttribute<FromBodyAttribute>() != null)
            return "Body";
        return "Unknown";
    }

    private List<ApiParameterInfo> GetParameters(MethodInfo method, HashSet<string> routeParameters)
    {
        var parameters = new List<ApiParameterInfo>();
        var methodXmlKey = GetXmlMemberName(method);

        foreach(var param in method.GetParameters())
        {
            var fromQueryAttr = param.GetCustomAttribute<FromQueryAttribute>();
            if(fromQueryAttr != null && !param.ParameterType.IsPrimitive && param.ParameterType != typeof(string))
            {
                var paramDescription = GetXmlParamSummary(methodXmlKey, param.Name);

                foreach(var prop in param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var propDescription = GetXmlSummary(prop);

                    var description = !string.IsNullOrEmpty(propDescription) ? propDescription : paramDescription;

                    parameters.Add(new ApiParameterInfo
                    {
                        Name = prop.Name,
                        Type = GetFriendlyTypeName(prop.PropertyType),
                        IsFromBody = false,
                        Source = "Query",
                        IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null,
                        Description = description,
                        Schema = GenerateJsonSchema(prop.PropertyType, new HashSet<Type>())
                    });
                }
            }
            else
            {
                var paramDescription = GetXmlParamSummary(methodXmlKey, param.Name) ?? "Route parameter";

                parameters.Add(new ApiParameterInfo
                {
                    Name = param.Name ?? "unnamed",
                    Type = GetFriendlyTypeName(param.ParameterType),
                    IsFromBody = param.GetCustomAttribute<FromBodyAttribute>() != null,
                    Source = GetParameterSource(param, routeParameters),
                    IsRequired = param.GetCustomAttribute<RequiredAttribute>() != null || !param.IsOptional,
                    Description = paramDescription,
                    Schema = GenerateJsonSchema(param.ParameterType, new HashSet<Type>())
                });
            }
        }

        return parameters;
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

    private Dictionary<string, string> LoadXmlDocumentation()
    {
        var result = new Dictionary<string, string>();
        var assemblies = new[] { _assembly }
            .Concat(_assembly.GetReferencedAssemblies()
                .Select(asm => Assembly.Load(asm)))
            .Distinct()
            .ToList();

        foreach(var assembly in assemblies)
        {
            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");

            if(File.Exists(xmlFile))
            {
                try
                {
                    var doc = XDocument.Load(xmlFile);

                    foreach(var member in doc.Descendants("member"))
                    {
                        var nameAttr = member.Attribute("name")?.Value;
                        if(!string.IsNullOrWhiteSpace(nameAttr))
                        {
                            var summary = member.Element("summary")?.Value?.Trim();
                            if(!string.IsNullOrWhiteSpace(summary))
                            {
                                result[nameAttr] = summary;
                            }

                            if(nameAttr.StartsWith("M:"))
                            {
                                foreach(var param in member.Elements("param"))
                                {
                                    var paramName = param.Attribute("name")?.Value;
                                    var paramSummary = param.Value?.Trim();
                                    if(!string.IsNullOrWhiteSpace(paramName) && !string.IsNullOrWhiteSpace(paramSummary))
                                    {
                                        var paramKey = $"{nameAttr}#{paramName}";
                                        result[paramKey] = paramSummary;
                                    }
                                }
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, $"Reading XML {xmlFile}");
                }
            }
        }

        return result;
    }

    private string? GetXmlSummary(MemberInfo? member)
    {
        if(member == null)
            return null;

        var memberId = GetXmlMemberName(member);
        var summary = _xmlDocs.TryGetValue(memberId, out var value) ? value : null;
        return summary;
    }

    private string? GetXmlParamSummary(string methodXmlKey, string paramName)
    {
        var paramKey = $"{methodXmlKey}#{paramName}";
        var summary = _xmlDocs.TryGetValue(paramKey, out var value) ? value : null;
        return summary;
    }

    private static string GetXmlMemberName(MemberInfo member)
    {
        if(member is Type type)
            return "T:" + type.FullName;

        if(member is MethodInfo method)
        {
            var paramTypes = method.GetParameters()
                .Select(p => p.ParameterType.FullName ?? "Unknown")
                .ToArray();

            var methodName = $"{method.DeclaringType?.FullName}.{method.Name}";
            if(paramTypes.Length > 0)
                methodName += $"({string.Join(",", paramTypes)})";

            return "M:" + methodName;
        }

        if(member is PropertyInfo property)
        {
            var declaringTypeName = property.DeclaringType?.FullName?.Replace("+", ".") ?? "Unknown";
            return $"P:{declaringTypeName}.{property.Name}";
        }

        return member.Name;
    }

    private string GetFriendlyTypeName(Type type)
    {
        if(type == null)
            return "Unknown";

        if(type.IsGenericType)
        {
            var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
            return $"{type.Name.Split('`')[0]}<{genericArgs}>";
        }

        return type.Name;
    }

    private Dictionary<string, object>? GenerateJsonSchema(Type? type, HashSet<Type>? processedTypes = null)
    {
        if(type == null)
            return null;

        processedTypes ??= new HashSet<Type>();

        if(type.IsPrimitive || type == typeof(string))
        {
            return new Dictionary<string, object>
            {
                ["type"] = GetJsonType(type)
            };
        }

        if(processedTypes.Contains(type))
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["$ref"] = $"#/components/schemas/{type.Name}"
            };
        }

        processedTypes.Add(type);

        var schema = new Dictionary<string, object>();

        if(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            schema["type"] = "array";
            schema["items"] = GenerateJsonSchema(type.GetGenericArguments()[0], processedTypes) ?? new Dictionary<string, object>();
            processedTypes.Remove(type);
            return schema;
        }

        schema["type"] = "object";
        schema["properties"] = new Dictionary<string, object>();
        var requiredProperties = new List<string>();

        foreach(var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propSchema = GenerateJsonSchema(prop.PropertyType, processedTypes);
            if(propSchema != null)
            {
                var propDict = new Dictionary<string, object>(propSchema);
                var description = GetXmlSummary(prop);
                if(!string.IsNullOrEmpty(description))
                {
                    propDict["description"] = description;
                }
                ((Dictionary<string, object>)schema["properties"])[prop.Name] = propDict;

                if(prop.GetCustomAttribute<RequiredAttribute>() != null)
                {
                    requiredProperties.Add(prop.Name);
                }
            }
        }

        if(requiredProperties.Any())
        {
            schema["required"] = requiredProperties;
        }

        processedTypes.Remove(type);

        return schema;
    }

    private string GetJsonType(Type type)
    {
        if(type == typeof(int) || type == typeof(long))
            return "integer";
        if(type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            return "number";
        if(type == typeof(bool))
            return "boolean";
        if(type == typeof(string))
            return "string";
        return "object";
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Xml.Linq;

namespace WebApiDocumentator.Metadata;

internal class ControllerMetadataProvider : IMetadataProvider
{
    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _xmlDocs;

    public ControllerMetadataProvider()
    {
        _assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        _xmlDocs = LoadXmlDocumentation();
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

        Console.WriteLine($"Total de controladores detectados: {controllerTypes.Count}");
        foreach(var controllerType in controllerTypes)
        {
            Console.WriteLine($"Controlador detectado: {controllerType.FullName}");
            if(processedControllers.Contains(controllerType))
            {
                Console.WriteLine($"Controlador duplicado ignorado: {controllerType.FullName}");
                continue;
            }

            processedControllers.Add(controllerType);

            var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
            var controllerName = controllerType.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
                ? controllerType.Name.Substring(0, controllerType.Name.Length - "Controller".Length).ToLowerInvariant()
                : controllerType.Name.ToLowerInvariant();
            var routePrefix = routeAttr?.Template?.Replace("[controller]", controllerName).ToLowerInvariant() ?? controllerName;

            var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .OrderBy(m => m.Name)
                .ToList();

            Console.WriteLine($"Métodos en {controllerType.FullName}: {methods.Count}");
            foreach(var method in methods)
            {
                var paramTypes = string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName ?? "Unknown"));
                var methodKey = $"{controllerType.FullName}.{method.Name}({paramTypes})";
                Console.WriteLine($"Método detectado: {methodKey}");

                if(processedMethods.Contains(methodKey))
                {
                    Console.WriteLine($"Método duplicado ignorado: {methodKey}");
                    continue;
                }

                var httpAttrs = method.GetCustomAttributes()
                    .OfType<HttpMethodAttribute>()
                    .ToList();

                if(!httpAttrs.Any())
                {
                    Console.WriteLine($"Método {methodKey} sin atributos HTTP, ignorado");
                    continue;
                }

                Console.WriteLine($"Procesando método: {methodKey}, Atributos HTTP: {httpAttrs.Count} ({string.Join(", ", httpAttrs.Select(a => a.GetType().Name))})");

                var httpAttr = httpAttrs.First();
                var httpMethod = httpAttr.HttpMethods.FirstOrDefault()?.ToUpper() ?? "UNKNOWN";
                var methodRoute = GetMethodRoute(method).ToLowerInvariant();
                var fullRoute = CombineRoute(routePrefix, methodRoute).ToLowerInvariant();

                if(excludedRoutes.Any(excluded => fullRoute.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"Ruta excluida: {fullRoute}");
                    continue;
                }

                var endpoint = new ApiEndpointInfo
                {
                    HttpMethod = httpMethod,
                    Route = fullRoute,
                    Summary = GetXmlSummary(method) ?? method.Name,
                    Description = GetXmlSummary(method) ?? method.Name,
                    ReturnType = GetFriendlyTypeName(method.ReturnType),
                    ReturnSchema = GenerateJsonSchema(method.ReturnType, new HashSet<Type>()),
                    Parameters = GetParameters(method)
                };

                Console.WriteLine($"Endpoint generado: {httpMethod} {fullRoute}, Parámetros: {endpoint.Parameters.Count}, ReturnType: {endpoint.ReturnType}");
                result.Add(endpoint);
                processedMethods.Add(methodKey);
            }
        }

        // Filtrar duplicados y endpoints inválidos
        var filteredResult = result
            .GroupBy(e => (e.Route, e.HttpMethod))
            .Select(g =>
            {
                var endpoints = g.ToList();
                if(endpoints.Count > 1)
                {
                    Console.WriteLine($"Duplicados detectados para {g.Key.Route} ({g.Key.HttpMethod}): {endpoints.Count} endpoints");
                    foreach(var ep in endpoints)
                    {
                        Console.WriteLine($" - {ep.HttpMethod} {ep.Route}, Parámetros: {ep.Parameters.Count}, ReturnType: {ep.ReturnType}");
                    }
                }
                return endpoints
                    .OrderByDescending(e => e.Parameters.Count)
                    .ThenByDescending(e => e.ReturnType != "Unknown")
                    .ThenByDescending(e => e.Summary != null && !e.Summary.Contains(" ("))
                    .First();
            })
            .Where(e => e.ReturnType != "Unknown" || e.Parameters.Any() || e.Route == "api/simple") // Permitir api/simple sin parámetros
            .ToList();

        Console.WriteLine($"Endpoints generados: {filteredResult.Count}");
        foreach(var endpoint in filteredResult)
        {
            Console.WriteLine($"Endpoint final: {endpoint.HttpMethod} {endpoint.Route}, Parámetros: {endpoint.Parameters.Count}, ReturnType: {endpoint.ReturnType}, Summary: {endpoint.Summary}");
        }

        return filteredResult;
    }

    private List<ApiParameterInfo> GetParameters(MethodInfo method)
    {
        var parameters = new List<ApiParameterInfo>();

        foreach(var param in method.GetParameters())
        {
            var fromQueryAttr = param.GetCustomAttribute<FromQueryAttribute>();
            if(fromQueryAttr != null && !param.ParameterType.IsPrimitive && param.ParameterType != typeof(string))
            {
                foreach(var prop in param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    parameters.Add(new ApiParameterInfo
                    {
                        Name = prop.Name,
                        Type = GetFriendlyTypeName(prop.PropertyType),
                        IsFromBody = false,
                        IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null || !IsNullable(prop.PropertyType),
                        Description = GetXmlSummary(prop),
                        Schema = GenerateJsonSchema(prop.PropertyType, new HashSet<Type>())
                    });
                }
            }
            else
            {
                parameters.Add(new ApiParameterInfo
                {
                    Name = param.Name ?? "unnamed",
                    Type = GetFriendlyTypeName(param.ParameterType),
                    IsFromBody = param.GetCustomAttribute<FromBodyAttribute>() != null,
                    IsRequired = param.GetCustomAttribute<RequiredAttribute>() != null || !param.IsOptional,
                    Description = GetXmlSummary(param),
                    Schema = GenerateJsonSchema(param.ParameterType, new HashSet<Type>())
                });
            }
        }

        return parameters;
    }

    private bool IsNullable(Type type)
    {
        return Nullable.GetUnderlyingType(type) != null || type.IsClass || type.IsInterface;
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
        var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{_assembly.GetName().Name}.xml");
        if(!File.Exists(xmlFile))
            return result;

        var doc = XDocument.Load(xmlFile);
        foreach(var member in doc.Descendants("member"))
        {
            var nameAttr = member.Attribute("name")?.Value;
            var summary = member.Element("summary")?.Value?.Trim();
            if(!string.IsNullOrWhiteSpace(nameAttr) && summary != null)
                result[nameAttr] = summary;
        }

        return result;
    }

    private string? GetXmlSummary(MemberInfo? member)
    {
        if(member == null)
            return null;

        var memberId = GetXmlMemberName(member);
        return _xmlDocs.TryGetValue(memberId, out var summary) ? summary : null;
    }

    private string? GetXmlSummary(ParameterInfo? parameter)
    {
        if(parameter == null)
            return null;

        var memberId = GetXmlMemberName(parameter);
        return _xmlDocs.TryGetValue(memberId, out var summary) ? summary : null;
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
            return "P:" + $"{property.DeclaringType?.FullName}.{property.Name}";
        }

        return member.Name;
    }

    private static string GetXmlMemberName(ParameterInfo parameter)
    {
        var method = parameter.Member as MethodInfo;
        if(method != null)
        {
            var paramTypes = method.GetParameters()
                .Select(p => p.ParameterType.FullName ?? "Unknown")
                .ToArray();
            var methodName = $"{method.DeclaringType?.FullName}.{method.Name}";
            if(paramTypes.Length > 0)
                methodName += $"({string.Join(",", paramTypes)})";

            return $"P:{methodName}#{parameter.Name}";
        }

        return parameter.Name;
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
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

        foreach(var controllerType in controllerTypes)
        {
            if(processedControllers.Contains(controllerType))
            {
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

            foreach(var method in methods)
            {
                var paramTypes = string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName ?? "Unknown"));
                var methodKey = $"{controllerType.FullName}.{method.Name}({paramTypes})";

                if(processedMethods.Contains(methodKey))
                {
                    continue;
                }

                var httpAttrs = method.GetCustomAttributes()
                    .OfType<HttpMethodAttribute>()
                    .ToList();

                if(!httpAttrs.Any())
                {
                    continue;
                }

                var httpAttr = httpAttrs.First();
                var httpMethod = httpAttr.HttpMethods.FirstOrDefault()?.ToUpper() ?? "UNKNOWN";
                var methodRoute = GetMethodRoute(method).ToLowerInvariant();
                var fullRoute = CombineRoute(routePrefix, methodRoute).ToLowerInvariant();

                if(excludedRoutes.Any(excluded => fullRoute.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
                {
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

    private List<ApiParameterInfo> GetParameters(MethodInfo method)
    {
        var parameters = new List<ApiParameterInfo>();
        var methodXmlKey = GetXmlMemberName(method);

        foreach(var param in method.GetParameters())
        {
            var fromQueryAttr = param.GetCustomAttribute<FromQueryAttribute>();
            if(fromQueryAttr != null && !param.ParameterType.IsPrimitive && param.ParameterType != typeof(string))
            {
                // Obtener la descripción del parámetro del método
                var paramDescription = GetXmlParamSummary(methodXmlKey, param.Name);
                Console.WriteLine($"Parámetro: {param.Name}, Descripción del parámetro: {paramDescription}");

                foreach(var prop in param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var propDescription = GetXmlSummary(prop);
                    Console.WriteLine($"Propiedad: {prop.Name}, Clave XML: P:{prop.DeclaringType?.FullName}.{prop.Name}, Descripción: {propDescription}");

                    // Usar la descripción de la propiedad si existe; de lo contrario, usar la descripción del parámetro
                    var description = !string.IsNullOrEmpty(propDescription) ? propDescription : paramDescription;

                    parameters.Add(new ApiParameterInfo
                    {
                        Name = prop.Name,
                        Type = GetFriendlyTypeName(prop.PropertyType),
                        IsFromBody = false,
                        IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null,
                        Description = description,
                        Schema = GenerateJsonSchema(prop.PropertyType, new HashSet<Type>())
                    });
                }
            }
            else
            {
                var paramDescription = GetXmlParamSummary(methodXmlKey, param.Name);
                Console.WriteLine($"Parámetro: {param.Name}, Descripción: {paramDescription}");

                parameters.Add(new ApiParameterInfo
                {
                    Name = param.Name ?? "unnamed",
                    Type = GetFriendlyTypeName(param.ParameterType),
                    IsFromBody = param.GetCustomAttribute<FromBodyAttribute>() != null,
                    IsRequired = param.GetCustomAttribute<RequiredAttribute>() != null || !param.IsOptional,
                    Description = paramDescription,
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
        var assemblies = new[] { _assembly }
            .Concat(_assembly.GetReferencedAssemblies()
                .Select(asm => Assembly.Load(asm)))
            .Distinct()
            .ToList();

        foreach(var assembly in assemblies)
        {
            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");
            Console.WriteLine($"Intentando cargar archivo XML: {xmlFile}");

            if(!File.Exists(xmlFile))
            {
                Console.WriteLine($"Archivo XML no encontrado: {xmlFile}");
                continue;
            }

            try
            {
                var doc = XDocument.Load(xmlFile);
                Console.WriteLine($"Archivo XML cargado: {xmlFile}");

                foreach(var member in doc.Descendants("member"))
                {
                    var nameAttr = member.Attribute("name")?.Value;
                    if(string.IsNullOrWhiteSpace(nameAttr))
                        continue;

                    // Procesar <summary> para métodos y propiedades
                    var summary = member.Element("summary")?.Value?.Trim();
                    if(!string.IsNullOrWhiteSpace(summary))
                    {
                        result[nameAttr] = summary;
                        Console.WriteLine($"Cargada entrada: {nameAttr}: {summary}");
                    }

                    // Procesar <param> para parámetros de métodos
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
                                Console.WriteLine($"Cargada entrada: {paramKey}: {paramSummary}");
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Error al cargar archivo XML {xmlFile}: {ex.Message}");
            }
        }

        Console.WriteLine($"Entradas XML totales cargadas: {result.Count}");
        foreach(var entry in result)
        {
            Console.WriteLine($" - {entry.Key}: {entry.Value}");
        }

        return result;
    }

    private string? GetXmlSummary(MemberInfo? member)
    {
        if(member == null)
            return null;

        var memberId = GetXmlMemberName(member);
        Console.WriteLine($"Buscando descripción para: {memberId}");
        var summary = _xmlDocs.TryGetValue(memberId, out var value) ? value : null;
        Console.WriteLine($"Resultado: {summary}");
        return summary;
    }

    private string? GetXmlParamSummary(string methodXmlKey, string paramName)
    {
        var paramKey = $"{methodXmlKey}#{paramName}";
        Console.WriteLine($"Buscando descripción para parámetro: {paramKey}");
        var summary = _xmlDocs.TryGetValue(paramKey, out var value) ? value : null;
        Console.WriteLine($"Resultado: {summary}");
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Xml.Linq;

namespace WebApiDocumentator.Metadata;

internal class MinimalApiMetadataProvider : IMetadataProvider
{
    private readonly EndpointDataSource _endpointDataSource;
    private readonly Dictionary<string, string> _xmlDocs;

    public MinimalApiMetadataProvider(EndpointDataSource endpointDataSource)
    {
        _endpointDataSource = endpointDataSource;
        _xmlDocs = LoadXmlDocumentation();
    }

    private string GetParameterSource(ParameterInfo parameter, HashSet<string> routeParameters, EndpointMetadataCollection metadata)
    {
        if(routeParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
            return "Path";
        if(parameter.GetCustomAttribute<FromQueryAttribute>() != null)
            return "Query";
        if(parameter.GetCustomAttribute<FromBodyAttribute>() != null ||
            metadata.OfType<IAcceptsMetadata>()
                .Any(m => m.RequestType == parameter.ParameterType && m.ContentTypes.Contains("application/json")))
            return "Body";
        return "Unknown";
    }

    // Fragmento de GetEndpoints
    public List<ApiEndpointInfo> GetEndpoints()
    {
        var endpoints = new List<ApiEndpointInfo>();
        var excludedRoutes = new[] { "/get-metadata", "/openapi" };

        var endpointToTrace = _endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e =>
                !e.Metadata.OfType<CompiledPageActionDescriptor>().Any() &&
                !e.Metadata.OfType<ControllerActionDescriptor>().Any() &&
                !excludedRoutes.Any(excluded => e.RoutePattern.RawText?.StartsWith(excluded, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        Console.WriteLine($"Total de endpoints Minimal API detectados: {endpointToTrace.Count}");
        foreach(var endpoint in endpointToTrace)
        {
            var httpMethods = endpoint.Metadata
                .OfType<HttpMethodMetadata>()
                .FirstOrDefault()?.HttpMethods;

            if(httpMethods == null || httpMethods.Count == 0)
            {
                Console.WriteLine($"Endpoint ignorado (sin métodos HTTP): {endpoint.RoutePattern.RawText}");
                continue;
            }

            var methodInfo = endpoint.Metadata
                .OfType<MethodInfo>()
                .FirstOrDefault();

            var routeParameters = endpoint.RoutePattern.Parameters
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var parameters = new List<ApiParameterInfo>();
            string returnType = "Unknown";
            Dictionary<string, object>? returnSchema = null;

            Console.WriteLine($"Procesando endpoint: {httpMethods[0]} {endpoint.RoutePattern.RawText}, DisplayName: {endpoint.DisplayName}");

            string? methodSummary = null;
            string methodXmlKey = null;

            if(methodInfo != null)
            {
                methodSummary = GetXmlSummary(methodInfo) ?? endpoint.DisplayName;
                methodSummary = methodSummary?.Trim().TrimEnd('.');
                methodXmlKey = GetXmlMemberName(methodInfo);

                var parameterDescriptions = new List<string>();
                parameters = methodInfo.GetParameters()
                    .Where(p => !IsHttpContextOrService(p.ParameterType))
                    .Select(p =>
                    {
                        var paramDescription = GetXmlParamSummary(methodXmlKey, p.Name);
                        if(!string.IsNullOrEmpty(paramDescription))
                        {
                            paramDescription = paramDescription.Trim().TrimEnd('.');
                            var paramType = GetFriendlyTypeName(p.ParameterType);
                            var paramSource = GetParameterSource(p, routeParameters, endpoint.Metadata);
                            var paramInfo = $"- {p.Name} ({paramType}, {paramSource}): {paramDescription}";
                            parameterDescriptions.Add(paramInfo);
                        }

                        return new ApiParameterInfo
                        {
                            Name = p.Name ?? "unnamed",
                            Type = GetFriendlyTypeName(p.ParameterType),
                            IsFromBody = p.GetCustomAttribute<FromBodyAttribute>() != null ||
                                         endpoint.Metadata.OfType<IAcceptsMetadata>()
                                             .Any(m => m.RequestType == p.ParameterType && m.ContentTypes.Contains("application/json")),
                            Source = GetParameterSource(p, routeParameters, endpoint.Metadata),
                            IsRequired = p.GetCustomAttribute<RequiredAttribute>() != null || !p.IsOptional,
                            Description = paramDescription,
                            Schema = GenerateJsonSchema(p.ParameterType, new HashSet<Type>())
                        };
                    })
                    .ToList();

                // Construir la descripción del endpoint
                var description = methodSummary;
                if(parameterDescriptions.Any())
                {
                    description += "\nParámetros:\n" + string.Join("\n", parameterDescriptions);
                }

                returnType = GetFriendlyTypeName(methodInfo.ReturnType);
                returnSchema = GenerateJsonSchema(methodInfo.ReturnType, new HashSet<Type>());

                if(typeof(IResult).IsAssignableFrom(methodInfo.ReturnType))
                {
                    var resultType = InferResultType(methodInfo, endpoint.Metadata);
                    if(resultType != null)
                    {
                        returnType = GetFriendlyTypeName(resultType);
                        returnSchema = GenerateJsonSchema(resultType, new HashSet<Type>());
                    }
                }

                var endpointInfo = new ApiEndpointInfo
                {
                    Route = endpoint.RoutePattern.RawText?.ToLowerInvariant() ?? "",
                    HttpMethod = httpMethods[0],
                    Summary = methodSummary,
                    Description = description,
                    ReturnType = returnType,
                    Parameters = parameters,
                    ReturnSchema = returnSchema
                };

                Console.WriteLine($"Endpoint generado: {endpointInfo.HttpMethod} {endpointInfo.Route}, Parámetros: {endpointInfo.Parameters.Count}, ReturnType: {endpointInfo.ReturnType}");
                endpoints.Add(endpointInfo);
            }
            // ... (resto del método sin cambios)
        }

        Console.WriteLine($"Total de endpoints Minimal API generados: {endpoints.Count}");
        return endpoints;
    }
    private string? GetMinimalApiSummary(RouteEndpoint endpoint)
    {
        return endpoint.DisplayName;
    }

    private bool IsHttpContextOrService(Type type)
    {
        return type == typeof(HttpContext) ||
               type == typeof(CancellationToken) ||
               type == typeof(IFormFile) ||
               typeof(IHttpContextAccessor).IsAssignableFrom(type);
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

    private Dictionary<string, string> LoadXmlDocumentation()
    {
        var result = new Dictionary<string, string>();
        var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetEntryAssembly()?.GetName().Name}.xml");
        if(!File.Exists(xmlFile))
        {
            Console.WriteLine($"Archivo XML no encontrado: {xmlFile}");
            return result;
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

                // Procesar <summary> para métodos
                var summary = member.Element("summary")?.Value?.Trim();
                if(!string.IsNullOrWhiteSpace(summary))
                {
                    result[nameAttr] = summary;
                    Console.WriteLine($"Cargada entrada: {nameAttr}: {summary}");
                }

                // Procesar <param> para parámetros
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

        Console.WriteLine($"Entradas XML totales cargadas: {result.Count}");
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

    private string? GetXmlParamSummary(string methodXmlKey, string? paramName)
    {
        if(string.IsNullOrWhiteSpace(methodXmlKey) || string.IsNullOrWhiteSpace(paramName))
            return null;

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
            return "P:" + $"{property.DeclaringType?.FullName}.{property.Name}";
        }

        return member.Name;
    }
}
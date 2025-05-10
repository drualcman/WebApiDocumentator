using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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

    public List<ApiEndpointInfo> GetEndpoints()
    {
        var endpoints = new List<ApiEndpointInfo>();

        var excludedRoutes = new[] { "/get-metadata", "/openapi" };

        var endpointToTrace = _endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e =>
                !e.Metadata.OfType<CompiledPageActionDescriptor>().Any() &&
                !excludedRoutes.Any(excluded => e.RoutePattern.RawText?.StartsWith(excluded, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        foreach(var endpoint in endpointToTrace)
        {
            var httpMethods = endpoint.Metadata
                .OfType<HttpMethodMetadata>()
                .FirstOrDefault()?.HttpMethods;

            if(httpMethods == null || httpMethods.Count == 0)
                continue;

            var methodInfo = endpoint.Metadata
                .OfType<MethodInfo>()
                .FirstOrDefault();

            var parameters = new List<ApiParameterInfo>();
            string returnType = "Unknown";
            Dictionary<string, object>? returnSchema = null;

            if(methodInfo != null)
            {
                parameters = methodInfo.GetParameters()
                    .Where(p => !IsHttpContextOrService(p.ParameterType))
                    .Select(p => new ApiParameterInfo
                    {
                        Name = p.Name ?? "unnamed",
                        Type = GetFriendlyTypeName(p.ParameterType),
                        IsFromBody = p.GetCustomAttribute<FromBodyAttribute>() != null ||
                                     endpoint.Metadata
                                         .OfType<AcceptsMetadata>()
                                         .Any(m => m.RequestType == p.ParameterType && m.ContentTypes.Contains("application/json")),
                        IsRequired = p.GetCustomAttribute<RequiredAttribute>() != null || !p.IsOptional,
                        Description = GetXmlSummary(p),
                        Schema = GenerateJsonSchema(p.ParameterType, new HashSet<Type>())
                    })
                    .ToList();

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
            }
            else
            {
                var acceptsMetadata = endpoint.Metadata
                    .OfType<AcceptsMetadata>()
                    .FirstOrDefault();

                if(acceptsMetadata != null)
                {
                    parameters.Add(new ApiParameterInfo
                    {
                        Name = "body",
                        Type = GetFriendlyTypeName(acceptsMetadata.RequestType),
                        IsFromBody = acceptsMetadata.ContentTypes.Contains("application/json"),
                        IsRequired = true,
                        Description = $"Body parameter of type {GetFriendlyTypeName(acceptsMetadata.RequestType)}",
                        Schema = GenerateJsonSchema(acceptsMetadata.RequestType, new HashSet<Type>())
                    });
                }
            }

            var endpointInfo = new ApiEndpointInfo
            {
                Route = endpoint.RoutePattern.RawText ?? "",
                HttpMethod = httpMethods[0],
                Summary = GetMinimalApiSummary(endpoint),
                Description = GetXmlSummary(methodInfo),
                ReturnType = returnType,
                Parameters = parameters,
                ReturnSchema = returnSchema
            };

            endpoints.Add(endpointInfo);
        }

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

        // Manejar tipos primitivos primero
        if(type.IsPrimitive || type == typeof(string))
        {
            return new Dictionary<string, object>
            {
                ["type"] = GetJsonType(type)
            };
        }

        // Verificar referencias circulares solo para tipos no primitivos
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

        // Manejar listas
        if(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            schema["type"] = "array";
            schema["items"] = GenerateJsonSchema(type.GetGenericArguments()[0], processedTypes) ?? new Dictionary<string, object>();
            processedTypes.Remove(type);
            return schema;
        }

        // Manejar objetos
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
                .Select(p => p.ParameterType.FullName)
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
                .Select(p => p.ParameterType.FullName)
                .ToArray();
            var methodName = $"{method.DeclaringType?.FullName}.{method.Name}";
            if(paramTypes.Length > 0)
                methodName += $"({string.Join(",", paramTypes)})";

            var paramIndex = Array.IndexOf(method.GetParameters(), parameter);
            return $"P:{methodName}#{parameter.Name}";
        }

        return parameter.Name;
    }
}
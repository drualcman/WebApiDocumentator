using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace WebApiDocumentator.Metadata;

internal class MinimalApiMetadataProvider : IMetadataProvider
{
    private readonly EndpointDataSource _endpointDataSource;

    public MinimalApiMetadataProvider(EndpointDataSource endpointDataSource)
    {
        _endpointDataSource = endpointDataSource;
    }

    public List<ApiEndpointInfo> GetEndpoints()
    {
        var endpoints = new List<ApiEndpointInfo>();

        // Definir las rutas excluidas (excluyendo /docs, ya que se manejará como página)
        var excludedRoutes = new[] { "/get-metadata", "/openapi" };

        // Filtrar endpoints para excluir Razor Pages y rutas específicas
        var endpointToTrace = _endpointDataSource.Endpoints
                    .OfType<RouteEndpoint>()
                    .Where(e =>
                        // Excluir endpoints de Razor Pages
                        !e.Metadata.OfType<CompiledPageActionDescriptor>().Any() &&
                        // Excluir rutas específicas
                        !excludedRoutes.Any(excluded => e.RoutePattern.RawText?.StartsWith(excluded, StringComparison.OrdinalIgnoreCase) == true))
                    .ToList();

        foreach(var endpoint in endpointToTrace)
        {
            var httpMethods = endpoint.Metadata
                .OfType<HttpMethodMetadata>()
                .FirstOrDefault()?.HttpMethods;

            if(httpMethods == null || httpMethods.Count == 0)
                continue;

            // Obtener MethodInfo del manejador
            var methodInfo = endpoint.Metadata
                .OfType<MethodInfo>()
                .FirstOrDefault();

            var parameters = new List<ApiParameterInfo>();
            string returnType = "Unknown";
            Dictionary<string, object>? returnSchema = null;

            if(methodInfo != null)
            {
                // Obtener parámetros del método
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
                        Schema = GenerateJsonSchema(p.ParameterType, new HashSet<Type>())
                    })
                    .ToList();

                // Obtener tipo de retorno
                returnType = GetFriendlyTypeName(methodInfo.ReturnType);
                returnSchema = GenerateJsonSchema(methodInfo.ReturnType, new HashSet<Type>());

                // Si el retorno es IResult, intentar inferir el tipo real
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
                // Usar AcceptsMetadata para parámetros si MethodInfo no está disponible
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
                        Schema = GenerateJsonSchema(acceptsMetadata.RequestType, new HashSet<Type>())
                    });
                }
            }

            var endpointInfo = new ApiEndpointInfo
            {
                Route = endpoint.RoutePattern.RawText ?? "",
                HttpMethod = httpMethods[0],
                Summary = GetMinimalApiSummary(endpoint),
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

        if(type.IsPrimitive || type == typeof(string))
        {
            schema["type"] = GetJsonType(type);
            return schema;
        }

        if(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            schema["type"] = "array";
            schema["items"] = GenerateJsonSchema(type.GetGenericArguments()[0], processedTypes) ?? new Dictionary<string, object>();
            return schema;
        }

        schema["type"] = "object";
        schema["properties"] = new Dictionary<string, object>();

        foreach(var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propSchema = GenerateJsonSchema(prop.PropertyType, processedTypes);
            if(propSchema != null)
            {
                ((Dictionary<string, object>)schema["properties"])[prop.Name] = propSchema;
            }
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
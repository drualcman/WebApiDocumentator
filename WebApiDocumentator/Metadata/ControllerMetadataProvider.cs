using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
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

        var controllerTypes = _assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        foreach(var controllerType in controllerTypes)
        {
            var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
            var routePrefix = routeAttr?.Template ?? "[controller]";

            foreach(var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var httpAttr = method.GetCustomAttributes()
                    .FirstOrDefault(attr => attr is IHttpMethodMetadata) as Attribute;

                if(httpAttr == null)
                    continue;

                var httpMethod = httpAttr.GetType().Name.Replace("Attribute", "").ToUpper();
                var methodRoute = GetMethodRoute(method);
                var fullRoute = CombineRoute(routePrefix, methodRoute);

                if(excludedRoutes.Any(excluded => fullRoute.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var endpoint = new ApiEndpointInfo
                {
                    HttpMethod = httpMethod,
                    Route = fullRoute,
                    Summary = GetXmlSummary(method),
                    Description = GetXmlSummary(method),
                    ReturnType = GetFriendlyTypeName(method.ReturnType),
                    ReturnSchema = GenerateJsonSchema(method.ReturnType, new HashSet<Type>()),
                    Parameters = method.GetParameters().Select(p => new ApiParameterInfo
                    {
                        Name = p.Name ?? "unnamed",
                        Type = GetFriendlyTypeName(p.ParameterType),
                        IsFromBody = p.GetCustomAttribute<FromBodyAttribute>() != null,
                        IsRequired = p.GetCustomAttribute<RequiredAttribute>() != null || !p.IsOptional,
                        Description = GetXmlSummary(p),
                        Schema = GenerateJsonSchema(p.ParameterType, new HashSet<Type>())
                    }).ToList()
                };

                result.Add(endpoint);
            }
        }

        return result;
    }

    private string CombineRoute(string prefix, string route)
    {
        return $"{prefix.TrimEnd('/')}/{route.TrimStart('/')}";
    }

    private string GetMethodRoute(MethodInfo method)
    {
        var routeAttr = method.GetCustomAttribute<RouteAttribute>();
        if(routeAttr != null)
            return routeAttr.Template;

        var httpAttr = method.GetCustomAttributes().FirstOrDefault(a => a is HttpMethodAttribute) as HttpMethodAttribute;
        return httpAttr?.Template ?? method.Name;
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
}
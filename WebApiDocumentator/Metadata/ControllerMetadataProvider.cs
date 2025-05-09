using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using System.Xml.Linq;
using WebApiDocumentator.Metadata;

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

        var excludedRoutes = new[] { "/get-metadata", "/docs", "/openapi" };

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

                var endpoint = new ApiEndpointInfo
                {
                    HttpMethod = httpMethod,
                    Route = CombineRoute(routePrefix, GetMethodRoute(method)),
                    Summary = GetXmlSummary(method),
                    ReturnType = method.ReturnType.Name,
                    Parameters = method.GetParameters().Select(p => new ApiParameterInfo
                    {
                        Name = p.Name!,
                        Type = p.ParameterType.Name,
                        IsFromBody = p.GetCustomAttribute<FromBodyAttribute>() != null
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

    private string? GetXmlSummary(MemberInfo member)
    {
        var memberId = GetXmlMemberName(member);
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

        return member.Name;
    }
}

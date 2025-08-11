using Microsoft.Extensions.DependencyInjection;

namespace WebApiDocumentator.Handlers;

public class ParameterSourceResolver : IParameterSourceResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ParameterSourceResolver> _logger;

    public ParameterSourceResolver(IServiceProvider serviceProvider, ILogger<ParameterSourceResolver> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetParameterSource(ParameterInfo parameter, HashSet<string> routeParameters, EndpointMetadataCollection metadata)
    {
        var paramType = parameter.ParameterType;
        var paramName = parameter.Name ?? "unnamed";
        var method = parameter.Member as MethodInfo;

        // Atributos explícitos
        if(routeParameters.Contains(paramName, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Path (route parameter)", paramName, method?.Name);
            return "Path";
        }
        if(parameter.GetCustomAttribute<FromQueryAttribute>() != null)
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Query ([FromQuery])", paramName, method?.Name);
            return "Query";
        }
        if(parameter.GetCustomAttribute<FromFormAttribute>() != null)
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Form ([FromForm])", paramName, method?.Name);
            return "Form";
        }
        if(metadata?.OfType<IAcceptsMetadata>()
            .Any(m => m.RequestType == paramType &&
                 m.ContentTypes.Any(ct => ct.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) ||
                                          ct.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))) ?? false)
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Form (metadata content-type)", paramName, method?.Name);
            return "Form";
        }
        if(parameter.GetCustomAttribute<FromBodyAttribute>() != null ||
            (metadata?.OfType<IAcceptsMetadata>()
                .Any(m => m.RequestType == paramType && m.ContentTypes.Contains("application/json")) ?? false))
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Body ([FromBody] or metadata)", paramName, method?.Name);
            return "Body";
        }
        if(parameter.GetCustomAttribute<FromServicesAttribute>() != null)
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Service ([FromServices])", paramName, method?.Name);
            return "Service";
        }

        // Verificar si es un servicio registrado
        if(IsServiceType(paramType))
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Service (registered in DI)", paramName, method?.Name);
            return "Service";
        }

        // Convenciones de ASP.NET Core
        if(IsBodyMethod(method))
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Body (non-service in POST/PUT/PATCH)", paramName, method?.Name);
            return "Body";
        }
        if(IsQueryMethod(method))
        {
            _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Query (non-service in GET/HEAD)", paramName, method?.Name);
            return "Query";
        }

        // Por defecto: Body (para evitar errores en métodos desconocidos)
        _logger.LogDebug("Parameter {ParamName} in {MethodName}: Source=Body (default)", paramName, method?.Name);
        return "Body";
    }

    private bool IsServiceType(Type type)
    {
        try
        {
            // Crear un ámbito para manejar servicios Scoped
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetService(type);
            if(service != null)
                return true;

            // Verificar interfaces implementadas
            var interfaces = type.GetInterfaces();
            foreach(var iface in interfaces)
            {
                service = scope.ServiceProvider.GetService(iface);
                if(service != null)
                    return true;
            }

            return false;
        }
        catch(Exception ex)
        {
            _logger.LogWarning("Failed to resolve service {TypeName}: {Error}", type.FullName, ex.Message);
            return false;
        }
    }

    private bool IsBodyMethod(MethodInfo method)
    {
        return method?.GetCustomAttributes()
            .OfType<HttpMethodAttribute>()
            .Any(attr => new[] { "POST", "PUT", "PATCH" }.Contains(attr.HttpMethods.FirstOrDefault(), StringComparer.OrdinalIgnoreCase)) ?? false;
    }

    private bool IsQueryMethod(MethodInfo method)
    {
        return method?.GetCustomAttributes()
            .OfType<HttpMethodAttribute>()
            .Any(attr => new[] { "GET", "HEAD" }.Contains(attr.HttpMethods.FirstOrDefault(), StringComparer.OrdinalIgnoreCase)) ?? false;
    }
}
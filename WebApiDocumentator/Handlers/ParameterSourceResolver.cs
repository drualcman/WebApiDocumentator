namespace WebApiDocumentator.Handlers;
internal static class ParameterSourceResolver
{
    public static string GetParameterSource(ParameterInfo parameter, HashSet<string> routeParameters)
    {
        if(routeParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
            return "Path";
        if(parameter.GetCustomAttribute<FromQueryAttribute>() != null)
            return "Query";
        if(parameter.GetCustomAttribute<FromBodyAttribute>() != null)
            return "Body";
        return "Service";
    }

    public static string GetParameterSource(ParameterInfo parameter, HashSet<string> routeParameters, EndpointMetadataCollection metadata)
    {
        if(routeParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
            return "Path";
        if(parameter.GetCustomAttribute<FromQueryAttribute>() != null)
            return "Query";
        if(parameter.GetCustomAttribute<FromBodyAttribute>() != null ||
            metadata.OfType<IAcceptsMetadata>()
                .Any(m => m.RequestType == parameter.ParameterType && m.ContentTypes.Contains("application/json")))
            return "Body";
        return "Service";
    }
}

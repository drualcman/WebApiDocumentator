namespace WebApiDocumentator.Handlers;
internal interface IParameterSourceResolver
{
    string GetParameterSource(ParameterInfo parameter, HashSet<string> routeParameters, EndpointMetadataCollection metadata);
}

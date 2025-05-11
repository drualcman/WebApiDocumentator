namespace WebApiDocumentator.Builders;
internal class ParameterDescriptionBuilder
{
    private readonly Dictionary<string, string> _xmlDocs;

    public ParameterDescriptionBuilder(Dictionary<string, string> xmlDocs)
    {
        _xmlDocs = xmlDocs;
    }

    public (List<ApiParameterInfo> Parameters, string Description) BuildParameters(
        MethodInfo method,
        HashSet<string> routeParameters,
        Func<ParameterInfo, bool>? parameterFilter = null,
        EndpointMetadataCollection? metadata = null)
    {
        var parameters = new List<ApiParameterInfo>();
        var parameterDescriptions = new List<string>();
        var methodXmlKey = XmlDocumentationHelper.GetXmlMemberName(method);

        foreach(var param in method.GetParameters())
        {
            if(parameterFilter == null || parameterFilter(param))
            {
                var paramDescription = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name) ?? "Route parameter";
                paramDescription = paramDescription.Trim().TrimEnd('.');

                var paramType = TypeNameHelper.GetFriendlyTypeName(param.ParameterType);
                var paramSource = metadata != null
                    ? ParameterSourceResolver.GetParameterSource(param, routeParameters, metadata)
                    : ParameterSourceResolver.GetParameterSource(param, routeParameters);

                var paramInfo = $"{param.Name} ({paramType}, {paramSource}): {paramDescription}";
                parameterDescriptions.Add(paramInfo);

                parameters.Add(new ApiParameterInfo
                {
                    Name = param.Name ?? "unnamed",
                    Type = paramType,
                    IsFromBody = param.GetCustomAttribute<FromBodyAttribute>() != null ||
                                 (metadata?.OfType<IAcceptsMetadata>()
                                     .Any(m => m.RequestType == param.ParameterType && m.ContentTypes.Contains("application/json")) ?? false),
                    Source = paramSource,
                    IsRequired = param.GetCustomAttribute<RequiredAttribute>() != null || !param.IsOptional,
                    Description = paramDescription,
                    Schema = new JsonSchemaGenerator(_xmlDocs).GenerateJsonSchema(param.ParameterType, new HashSet<Type>())
                });
            }
        }

        var description = XmlDocumentationHelper.GetXmlSummary(_xmlDocs, method) ?? method.Name;
        description = description.Trim().TrimEnd('.');
        if(parameterDescriptions.Any())
        {
            description += "\nParameters:\n" + string.Join("\n", parameterDescriptions);
        }

        return (parameters, description);
    }
}

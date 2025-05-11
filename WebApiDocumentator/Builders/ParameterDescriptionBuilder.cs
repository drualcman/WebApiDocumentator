using System.Text;

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
        var methodXmlKey = XmlDocumentationHelper.GetXmlMemberName(method);

        // Build parameter descriptions
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

                var fromQueryAttr = param.GetCustomAttribute<FromQueryAttribute>();
                if(fromQueryAttr != null && !param.ParameterType.IsPrimitive && param.ParameterType != typeof(string))
                {
                    foreach(var prop in param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var propDescription = XmlDocumentationHelper.GetXmlSummary(_xmlDocs, prop) ?? paramDescription;
                        propDescription = propDescription.Trim().TrimEnd('.');
                        var propType = TypeNameHelper.GetFriendlyTypeName(prop.PropertyType);

                        parameters.Add(new ApiParameterInfo
                        {
                            Name = prop.Name,
                            Type = propType,
                            IsFromBody = false,
                            Source = "Query",
                            IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null,
                            Description = propDescription,
                            Schema = new JsonSchemaGenerator(_xmlDocs).GenerateJsonSchema(prop.PropertyType, new HashSet<Type>())
                        });
                    }
                }
                else
                {
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
        }

        // Build description with all XML tags
        var descriptionBuilder = new StringBuilder();
        // Add parameter descriptions
        foreach(var param in method.GetParameters())
        {
            var paramDescription = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name);
            if(!string.IsNullOrWhiteSpace(paramDescription))
            {
                var paramType = TypeNameHelper.GetFriendlyTypeName(param.ParameterType);
                descriptionBuilder.AppendLine($"{param.Name} ({paramType}): {paramDescription.Trim().TrimEnd('.')}");
            }
        }

        // Add returns description
        var returns = XmlDocumentationHelper.GetXmlReturns(_xmlDocs, method);
        if(!string.IsNullOrWhiteSpace(returns))
        {
            descriptionBuilder.AppendLine($"Returns: {returns.Trim().TrimEnd('.')}");
        }

        // Add other XML tags (e.g., <remarks>) if needed
        var remarks = XmlDocumentationHelper.GetXmlRemarks(_xmlDocs, method);
        if(!string.IsNullOrWhiteSpace(remarks))
        {
            descriptionBuilder.AppendLine($"Remarks: {remarks.Trim().TrimEnd('.')}");
        }

        var description = descriptionBuilder.ToString().TrimEnd('\n', '\r');
        if(string.IsNullOrWhiteSpace(description))
        {
            description = method.Name;
        }

        return (parameters, description);
    }
}
namespace WebApiDocumentator.Builders;

internal class ParameterDescriptionBuilder
{
    private readonly Dictionary<string, string> _xmlDocs;
    private readonly ILogger _logger;

    public ParameterDescriptionBuilder(Dictionary<string, string> xmlDocs, ILogger logger)
    {
        _xmlDocs = xmlDocs;
        _logger = logger;
    }

    public (List<ApiParameterInfo> Parameters, string Description) BuildParameters(
        MethodInfo method,
        HashSet<string> routeParameters,
        Func<ParameterInfo, bool>? parameterFilter = null,
        EndpointMetadataCollection? metadata = null)
    {
        var parameters = new List<ApiParameterInfo>();
        var methodXmlKey = XmlDocumentationHelper.GetXmlMemberName(method);

        // Build description with all XML tags
        var descriptionBuilder = new StringBuilder();
        // Build parameter descriptions
        foreach(var param in method.GetParameters())
        {
            if(parameterFilter == null || parameterFilter(param))
            {
                // Skip invalid or compiler-generated parameter names
                if(IsInvalidParameterName(param.Name))
                {
                    _logger.LogWarning("Skipping invalid parameter name '{ParamName}' for method {MethodKey}", param.Name, methodXmlKey);
                    continue;
                }

                var paramDescription = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name) ?? "Route parameter";
                paramDescription = paramDescription.Trim().TrimEnd('.');
                var paramType = TypeNameHelper.GetFriendlyTypeName(param.ParameterType);
                var paramSource = metadata != null
                    ? ParameterSourceResolver.GetParameterSource(param, routeParameters, metadata)
                    : ParameterSourceResolver.GetParameterSource(param, routeParameters);

                var fromQueryAttr = param.GetCustomAttribute<FromQueryAttribute>();
                if(!paramSource.Equals("Service"))
                {
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
                            Name = param.Name ?? "unnamed_parameter",
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
                else
                {
                    descriptionBuilder.AppendLine($"Service: {TypeNameHelper.GetFriendlyTypeName(param.ParameterType)}");
                }
            }
        }

        // Add parameter descriptions
        foreach(var param in method.GetParameters())
        {
            // Skip invalid parameter names in description
            if(!IsInvalidParameterName(param.Name))
            {
                var paramDescription = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name);
                if(!string.IsNullOrWhiteSpace(paramDescription))
                {
                    var paramType = TypeNameHelper.GetFriendlyTypeName(param.ParameterType);
                    descriptionBuilder.AppendLine($"{param.Name} ({paramType}): {paramDescription.Trim().TrimEnd('.')}");
                }
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
        if(string.IsNullOrWhiteSpace(description) && !IsInvalidParameterName(method.Name))
        {
            description = method.Name;
        }

        return (parameters, description);
    }

    private bool IsInvalidParameterName(string? name)
    {
        if(string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        // Check for common compiler-generated name patterns
        return name.Contains("<") || name.Contains(">") || name.Contains("$") || name.StartsWith("b__");
    }
}
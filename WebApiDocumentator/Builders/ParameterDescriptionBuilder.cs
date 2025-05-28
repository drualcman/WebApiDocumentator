namespace WebApiDocumentator.Builders;

internal class ParameterDescriptionBuilder
{
    private readonly Dictionary<string, string> _xmlDocs;
    private readonly ILogger _logger;
    private readonly IParameterSourceResolver _parameterSourceResolver;

    public ParameterDescriptionBuilder(
        Dictionary<string, string> xmlDocs,
        ILogger logger,
        IParameterSourceResolver parameterSourceResolver)
    {
        _xmlDocs = xmlDocs ?? throw new ArgumentNullException(nameof(xmlDocs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parameterSourceResolver = parameterSourceResolver ?? throw new ArgumentNullException(nameof(parameterSourceResolver));
    }

    public (List<ApiParameterInfo> Parameters, string Description) BuildParameters(
        MethodInfo method,
        HashSet<string> routeParameters,
        Func<ParameterInfo, bool>? parameterFilter = null,
        EndpointMetadataCollection? metadata = null)
    {
        var parameters = new List<ApiParameterInfo>();
        var methodXmlKey = XmlDocumentationHelper.GetXmlMemberName(method);
        var descriptionBuilder = new StringBuilder();
        var validParameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach(var param in method.GetParameters())
        {
            if(parameterFilter == null || parameterFilter(param))
            {
                if(IsInvalidParameterName(param.Name))
                {
                    _logger.LogWarning("Skipping invalid parameter name '{ParamName}' for method {MethodKey}", param.Name, methodXmlKey);
                    continue;
                }

                var paramDescription = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name) ?? "Parameter";
                paramDescription = paramDescription.Trim().TrimEnd('.');
                var paramType = TypeNameHelper.GetFriendlyTypeName(param.ParameterType);
                var paramSource = _parameterSourceResolver.GetParameterSource(param, routeParameters, metadata);

                var fromQueryAttr = param.GetCustomAttribute<FromQueryAttribute>();
                if(!paramSource.Equals("Service", StringComparison.OrdinalIgnoreCase))
                {
                    if(fromQueryAttr != null && !param.ParameterType.IsPrimitive && param.ParameterType != typeof(string))
                    {
                        foreach(var prop in param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            var propDescription = XmlDocumentationHelper.GetXmlSummary(_xmlDocs, prop) ?? paramDescription;
                            propDescription = propDescription.Trim().TrimEnd('.');
                            var propType = TypeNameHelper.GetFriendlyTypeName(prop.PropertyType);

                            var paramModel = new ApiParameterInfo
                            {
                                Name = prop.Name,
                                Type = propType,
                                IsFromBody = false,
                                Source = "Query",
                                IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null,
                                Description = propDescription,
                                Schema = new JsonSchemaGenerator(_xmlDocs).GenerateJsonSchema(prop.PropertyType, new HashSet<Type>())
                            };
                            parameters.Add(paramModel);
                            validParameterNames.Add(prop.Name);
                        }
                    }
                    else
                    {
                        var isFromBody = paramSource.Equals("Body", StringComparison.OrdinalIgnoreCase);
                        var paramModel = new ApiParameterInfo
                        {
                            Name = param.Name ?? "unnamed_parameter",
                            Type = paramType,
                            IsFromBody = isFromBody,
                            Source = paramSource,
                            IsRequired = param.GetCustomAttribute<RequiredAttribute>() != null || !param.IsOptional,
                            Description = paramDescription,
                            Schema = isFromBody ? new JsonSchemaGenerator(_xmlDocs).GenerateJsonSchema(param.ParameterType, new HashSet<Type>()) : null
                        };
                        parameters.Add(paramModel);
                        validParameterNames.Add(param.Name);
                    }
                }
                else
                {
                    _logger.LogDebug("Excluded service parameter: {ParamName} ({ParamType}) for method {MethodName}",
                        param.Name, paramType, method.Name);
                    paramDescription = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name);
                    descriptionBuilder.AppendLine($"[{paramType}] {param.Name}: {paramDescription?.Trim().TrimEnd('.')}".Trim().TrimEnd(':'));
                }
            }
        }

        //// Generar descripción solo para parámetros válidos
        //foreach(var param in method.GetParameters())
        //{
        //    if(!IsInvalidParameterName(param.Name) && validParameterNames.Contains(param.Name))
        //    {
        //        var paramDescription = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name);
        //        if(!string.IsNullOrWhiteSpace(paramDescription))
        //        {
        //            var paramType = TypeNameHelper.GetFriendlyTypeName(param.ParameterType);
        //            descriptionBuilder.AppendLine($"{param.Name} ({paramType}): {paramDescription.Trim().TrimEnd('.')}");
        //        }
        //    }
        //}

        var returns = XmlDocumentationHelper.GetXmlReturns(_xmlDocs, method);
        if(!string.IsNullOrWhiteSpace(returns))
        {
            descriptionBuilder.AppendLine($"Returns: {returns.Trim().TrimEnd('.')}");
        }

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

        _logger.LogInformation("Built parameters for method {MethodName}: {Parameters}",
            method.Name,
            string.Join(", ", parameters.Select(p => $"{p.Name} ({p.Source})")));
        return (parameters, description);
    }

    private bool IsInvalidParameterName(string? name)
    {
        if(string.IsNullOrWhiteSpace(name))
            return true;
        return name.Contains("<") || name.Contains(">") || name.Contains("$") || name.StartsWith("b__");
    }
}
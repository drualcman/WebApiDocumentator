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
        Func<ParameterInfo, bool> parameterFilter = null,
        EndpointMetadataCollection metadata = null)
    {
        List<ApiParameterInfo> parameters = new List<ApiParameterInfo>();

        string methodXmlKey = XmlDocumentationHelper.GetXmlMemberName(method);
        StringBuilder descriptionBuilder = new StringBuilder();
        HashSet<string> validParameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach(ParameterInfo param in method.GetParameters())
        {
            if(parameterFilter == null || parameterFilter(param))
            {
                if(IsInvalidParameterName(param.Name))
                {
                    _logger.LogWarning("Skipping invalid parameter name '{ParamName}' for method {MethodKey}", param.Name, methodXmlKey);
                    continue;
                }

                string paramDescription = XmlDocumentationHelper.GetXmlParamSummary(_xmlDocs, methodXmlKey, param.Name) ?? "Parameter";
                paramDescription = paramDescription.Trim().TrimEnd('.');
                string paramType = TypeNameHelper.GetFriendlyTypeName(param.ParameterType);
                string paramSource = _parameterSourceResolver.GetParameterSource(param, routeParameters, metadata);

                FromQueryAttribute fromQueryAttr = param.GetCustomAttribute<FromQueryAttribute>();

                bool isPrimitive = IsPrimitiveOrFrameworkType(param.ParameterType);

                if(!paramSource.Equals("Service", StringComparison.OrdinalIgnoreCase))
                {
                    bool isOptional = IsOptionalParam(param);
                    if(fromQueryAttr != null && !isPrimitive)
                    {
                        // Handle collections or complex object
                        Type elementType = null;
                        bool isCollection = false;

                        if(param.ParameterType.IsArray)
                        {
                            isCollection = true;
                            elementType = param.ParameterType.GetElementType();
                        }
                        else
                        {
                            var enumerableInterface = param.ParameterType.GetInterfaces()
                                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

                            if(enumerableInterface != null)
                            {
                                isCollection = true;
                                elementType = enumerableInterface.GetGenericArguments()[0];
                            }
                        }

                        if(isCollection)
                        {
                            // Collection of primitives or complex
                            string collectionDescription = $"{paramDescription} (collection of {TypeNameHelper.GetFriendlyTypeName(elementType)})";

                            ApiParameterInfo paramModel = new ApiParameterInfo
                            {
                                Name = param.Name ?? "unnamed_parameter",
                                Type = TypeNameHelper.GetFriendlyTypeName(param.ParameterType),
                                IsFromBody = false,
                                Source = "Query",
                                IsRequired = !isOptional,
                                Description = collectionDescription,
                                IsCollection = true,
                                CollectionElementType = TypeNameHelper.GetFriendlyTypeName(elementType),
                                Schema = new JsonSchemaGenerator(_xmlDocs).GenerateJsonSchema(param.ParameterType, new HashSet<Type>())
                            };
                            parameters.Add(paramModel);
                            validParameterNames.Add(param.Name);
                        }
                        else
                        {
                            // If it's a complex object, we must break down its properties
                            foreach(PropertyInfo prop in param.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                            {
                                if(IsPrimitiveOrFrameworkType(prop.PropertyType))
                                {
                                    // It's a primitive
                                    string propDescription = XmlDocumentationHelper.GetXmlSummary(_xmlDocs, prop) ?? paramDescription;
                                    propDescription = propDescription.Trim().TrimEnd('.');
                                    string propType = TypeNameHelper.GetFriendlyTypeName(prop.PropertyType);

                                    ApiParameterInfo propModel = new ApiParameterInfo
                                    {
                                        Name = prop.Name,
                                        Type = propType,
                                        IsFromBody = false,
                                        Source = "Query",
                                        IsRequired = !isOptional,
                                        Description = propDescription,
                                        Schema = new JsonSchemaGenerator(_xmlDocs).GenerateJsonSchema(prop.PropertyType, new HashSet<Type>())
                                    };
                                    parameters.Add(propModel);
                                    validParameterNames.Add(prop.Name);
                                }
                            }
                        }
                    }
                    else
                    {
                        bool isFromBody = paramSource.Equals("Body", StringComparison.OrdinalIgnoreCase);
                        ApiParameterInfo paramModel = new ApiParameterInfo
                        {
                            Name = param.Name ?? "unnamed_parameter",
                            Type = paramType,
                            IsFromBody = isFromBody,
                            Source = paramSource,
                            IsRequired = !isOptional,
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

        string returns = XmlDocumentationHelper.GetXmlReturns(_xmlDocs, method);
        if(!string.IsNullOrWhiteSpace(returns))
        {
            descriptionBuilder.AppendLine($"Returns: {returns.Trim().TrimEnd('.')}");

        }

        string remarks = XmlDocumentationHelper.GetXmlRemarks(_xmlDocs, method);
        if(!string.IsNullOrWhiteSpace(remarks))
        {
            descriptionBuilder.AppendLine($"Remarks: {remarks.Trim().TrimEnd('.')}");

        }

        string description = descriptionBuilder.ToString().TrimEnd('\n', '\r');
        if(string.IsNullOrWhiteSpace(description) && !IsInvalidParameterName(method.Name))
        {
            description = method.Name;
        }

        _logger.LogInformation("Built parameters for method {MethodName}: {Parameters}",
            method.Name,
            string.Join(", ", parameters.Select(p => $"{p.Name} ({p.Source})")));
        return (parameters, description);
    }

    private bool IsPrimitiveOrFrameworkType(Type type)
    {
        if(type == null)
        {
            return false;
        }

        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive ||
               underlying == typeof(string) ||
               underlying == typeof(decimal) ||
               underlying == typeof(DateTime) ||
               underlying == typeof(TimeSpan) ||
               underlying == typeof(Guid) ||
               underlying.IsEnum;
    }

    private bool IsInvalidParameterName(string name)
    {
        if(string.IsNullOrWhiteSpace(name))
            return true;
        return name.Contains("<") ||
               name.Contains(">") ||
               name.Contains("$") ||
               name.StartsWith("b__");

    }

    private bool IsOptionalParam(ParameterInfo param)
    {
        // If parameter is nullable or not marked with [Required] then it's optional
        return param.IsOptional ||
               Nullable.GetUnderlyingType(param.ParameterType) != null &&
               param.GetCustomAttribute<RequiredAttribute>() == null;
    }

}
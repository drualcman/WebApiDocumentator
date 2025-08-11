namespace WebApiDocumentator.Handlers;

internal class JsonSchemaGenerator
{
    private readonly Dictionary<string, string> _xmlDocs;

    public JsonSchemaGenerator() : this(new Dictionary<string, string>()) { }

    public JsonSchemaGenerator(Dictionary<string, string> xmlDocs)
    {
        _xmlDocs = xmlDocs;
    }

    public string GetExampleAsJsonString(Dictionary<string, object> schema)
    {
        string result = "";
        if(schema != null && schema.ContainsKey("example"))
        {
            var example = schema["example"];
            result = SerializeToJson(example, true).Trim('"');
        }
        return result;
    }

    public Dictionary<string, object> GenerateJsonSchema(Type type, HashSet<Type> processedTypes = null, bool includeExample = true)
    {
        if(type != null)
        {
            processedTypes ??= new HashSet<Type>();
            if(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var innerType = type.GetGenericArguments()[0];
                return GenerateJsonSchema(innerType, processedTypes, includeExample);
            }
            if(type != typeof(Task))
            {
                var schema = new Dictionary<string, object>();
                if(type.IsPrimitive || type == typeof(string))
                {
                    schema["type"] = GetJsonType(type);
                    if(includeExample)
                        schema["example"] = GetExampleValue(type);
                    return schema;
                }
                if(processedTypes.Contains(type))
                {
                    schema["type"] = "object";
                    schema[" Shots"] = $"#/components/schemas/{type.Name}";
                    return schema;
                }
                processedTypes.Add(type);
                if(type.IsArray)
                    return GenerateCollectionSchema(type, type.GetElementType(), processedTypes, includeExample);
                if(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                    return GenerateCollectionSchema(type, type.GetGenericArguments()[0], processedTypes, includeExample);
                schema["type"] = "object";
                schema["properties"] = new Dictionary<string, object>();
                var requiredProperties = new List<string>();
                foreach(var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var propSchema = GenerateJsonSchema(prop.PropertyType, processedTypes, includeExample);
                    if(propSchema != null)
                    {
                        var propDict = new Dictionary<string, object>(propSchema);
                        var description = XmlDocumentationHelper.GetXmlSummary(_xmlDocs, prop);
                        if(!string.IsNullOrEmpty(description))
                            propDict["description"] = description;
                        ((Dictionary<string, object>)schema["properties"])[ToCamelCase(prop.Name)] = propDict;

                        if(prop.GetCustomAttribute<RequiredAttribute>() != null)
                            requiredProperties.Add(ToCamelCase(prop.Name));
                    }
                }
                if(requiredProperties.Any())
                    schema["required"] = requiredProperties;
                if(includeExample)
                {
                    object exampleInstance = GenerateExampleForType(type, 1);
                    if(exampleInstance != null && exampleInstance is not JsonElement)
                    {
                        try
                        {
                            JsonElement jsonElement = JsonSerializer.Deserialize<JsonElement>(SerializeToJson(exampleInstance));
                            schema["example"] = jsonElement;
                        }
                        catch
                        {
                            schema["example"] = null;
                        }
                    }
                }
                processedTypes.Remove(type);
                return schema;
            }
        }
        return null;
    }

    private Dictionary<string, object> GenerateCollectionSchema(Type type, Type elementType, HashSet<Type> processedTypes, bool includeExample)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "array",
            ["items"] = GenerateJsonSchema(elementType, processedTypes, includeExample) ?? new Dictionary<string, object>()
        };

        if(includeExample)
            schema["example"] = new[] { GenerateExampleForType(elementType, 1) };
        processedTypes.Remove(type);
        return schema;
    }

    private object GetExampleValue(Type type, bool useRepresentativeValues = false)
    {
        Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        if(underlyingType == typeof(string))
            return useRepresentativeValues ? "string" : "";
        if(underlyingType == typeof(int))
            return useRepresentativeValues ? 123 : 0;
        if(underlyingType == typeof(long))
            return useRepresentativeValues ? 123456L : 0L;
        if(underlyingType == typeof(double))
            return useRepresentativeValues ? 123.45 : 0.0;
        if(underlyingType == typeof(float))
            return useRepresentativeValues ? 123.45f : 0.0f;
        if(underlyingType == typeof(decimal))
            return useRepresentativeValues ? 123.45m : 0.0m;
        if(underlyingType == typeof(bool))
            return useRepresentativeValues ? true : false;
        if(underlyingType == typeof(DateTime))
            return useRepresentativeValues ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") : DateTime.UtcNow;
        if(underlyingType == typeof(DateOnly))
            return useRepresentativeValues ? DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd") : DateOnly.FromDateTime(DateTime.Now);
        if(underlyingType == typeof(Guid))
            return useRepresentativeValues ? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        if(underlyingType.IsEnum)
            return Enum.GetValues(underlyingType).GetValue(0)?.ToString() ?? "UNKNOWN";
        return null;
    }

    private object GenerateExampleForType(Type type, int depth = 0)
    {
        const int MAX_DEPTH = 4;
        if(depth <= MAX_DEPTH)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            if(IsPrimitiveOrBasic(underlyingType))
                return GetExampleValue(underlyingType, true);
            if(underlyingType.IsArray)
                return GenerateCollectionExample(underlyingType, underlyingType.GetElementType(), depth);
            if(underlyingType.IsGenericType)
            {
                var genericTypeDef = underlyingType.GetGenericTypeDefinition();
                if(genericTypeDef == typeof(IEnumerable<>) ||
                    genericTypeDef == typeof(IList<>) ||
                    genericTypeDef == typeof(List<>))
                {
                    return GenerateCollectionExample(underlyingType, underlyingType.GetGenericArguments()[0], depth);
                }
            }
            var example = new Dictionary<string, object>();
            foreach(var prop in underlyingType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if(prop.CanRead)
                {
                    try
                    {
                        var propValue = GenerateExampleForType(prop.PropertyType, depth + 1);
                        example[GetPropertyName(prop)] = propValue ?? null;
                    }
                    catch
                    {
                        example[GetPropertyName(prop)] = null;
                    }
                }
            }
            return example.Count == 0 ? new object() : example;
        }

        return "max-depth";
    }

    private bool IsPrimitiveOrBasic(Type underlyingType) => underlyingType.IsPrimitive || underlyingType == typeof(string) || underlyingType == typeof(DateTime) ||
                underlyingType == typeof(DateOnly) || underlyingType == typeof(Guid) || underlyingType.IsEnum;
    private object GenerateCollectionExample(Type type, Type elementType, int depth) => new[] { GenerateExampleForType(elementType, depth + 1) };

    private string SerializeToJson(object value, bool writeIndented = false) => JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = writeIndented,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    private string GetPropertyName(PropertyInfo prop)
    {
        var jsonProperty = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
        if(jsonProperty != null)
        {
            return jsonProperty.Name;
        }
        return ToCamelCase(prop.Name);
    }

    private string ToCamelCase(string input)
    {
        if(string.IsNullOrEmpty(input) || char.IsLower(input[0]))
            return input;
        return char.ToLowerInvariant(input[0]) + input.Substring(1);
    }

    private static string GetJsonType(Type type)
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
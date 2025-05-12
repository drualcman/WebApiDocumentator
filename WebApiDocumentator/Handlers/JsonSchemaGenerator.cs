namespace WebApiDocumentator.Handlers;

internal class JsonSchemaGenerator
{
    private readonly Dictionary<string, string> _xmlDocs;

    public JsonSchemaGenerator(Dictionary<string, string> xmlDocs)
    {
        _xmlDocs = xmlDocs;
    }

    public string GetExampleAsJsonString(Dictionary<string, object>? schema)
    {
        if(schema == null || !schema.ContainsKey("example"))
            return ""; // O algún valor por defecto

        // Extraer el ejemplo del esquema
        var example = schema["example"];

        // Serializar a JSON con formato bonito
        return JsonSerializer.Serialize(example, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }).Trim('"');
    }

    public Dictionary<string, object>? GenerateJsonSchema(Type? type, HashSet<Type>? processedTypes = null, bool includeExample = true)
    {
        if(type == null)
            return null;

        processedTypes ??= new HashSet<Type>();

        // Manejar Task<T> y Task
        if(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var innerType = type.GetGenericArguments()[0];
            return GenerateJsonSchema(innerType, processedTypes, includeExample);
        }
        if(type == typeof(Task))
        {
            // Task sin valor de retorno (void)
            return null;
        }

        var schema = new Dictionary<string, object>();

        // Manejar tipos básicos
        if(type.IsPrimitive || type == typeof(string))
        {
            schema["type"] = GetJsonType(type);
            if(includeExample)
            {
                schema["example"] = GetExampleValue(type);
            }
            return schema;
        }

        if(processedTypes.Contains(type))
        {
            schema["type"] = "object";
            schema["$ref"] = $"#/components/schemas/{type.Name}";
            return schema;
        }

        processedTypes.Add(type);

        // Manejar arrays
        if(type.IsArray)
        {
            schema["type"] = "array";
            var elementType = type.GetElementType();
            schema["items"] = GenerateJsonSchema(elementType, processedTypes, includeExample) ?? new Dictionary<string, object>();

            if(includeExample)
            {
                schema["example"] = new[] { GenerateExampleForType(elementType, processedTypes) };
            }

            processedTypes.Remove(type);
            return schema;
        }

        // Manejar List<>
        if(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            schema["type"] = "array";
            var elementType = type.GetGenericArguments()[0];
            schema["items"] = GenerateJsonSchema(elementType, processedTypes, includeExample) ?? new Dictionary<string, object>();

            if(includeExample)
            {
                schema["example"] = new[] { GenerateExampleForType(elementType, processedTypes) };
            }

            processedTypes.Remove(type);
            return schema;
        }

        // Manejar objetos complejos
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
                {
                    propDict["description"] = description;
                }
                ((Dictionary<string, object>)schema["properties"])[ToCamelCase(prop.Name)] = propDict;

                if(prop.GetCustomAttribute<RequiredAttribute>() != null)
                {
                    requiredProperties.Add(ToCamelCase(prop.Name));
                }
            }
        }

        if(requiredProperties.Any())
        {
            schema["required"] = requiredProperties;
        }

        if(includeExample)
        {
            schema["example"] = GenerateExampleForType(type, processedTypes);
        }

        processedTypes.Remove(type);

        return schema;
    }

    private object GetExampleValue(Type type)
    {
        if(type == typeof(string))
            return "string";
        if(type == typeof(int))
            return 0;
        if(type == typeof(long))
            return 0L;
        if(type == typeof(double))
            return 0.0;
        if(type == typeof(float))
            return 0.0f;
        if(type == typeof(decimal))
            return 0.0m;
        if(type == typeof(bool))
            return false;
        if(type == typeof(DateOnly) || type == typeof(DateTime))
            return DateTime.Now.ToString("yyyy-MM-dd");
        return null;
    }

    private Dictionary<string, object> GenerateExampleForType(Type type, HashSet<Type> processedTypes)
    {
        if(!type.IsPrimitive && type != typeof(string))
        {
            var example = new Dictionary<string, object>();

            foreach(var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = prop.PropertyType;
                var propName = ToCamelCase(prop.Name);

                if(propType.IsPrimitive || propType == typeof(string))
                {
                    example[propName] = GetExampleValue(propType);
                }
                else if(propType == typeof(DateOnly) || propType == typeof(DateTime))
                {
                    example[propName] = DateTime.Now.ToString("yyyy-MM-dd");
                }
                else if(propType.IsClass)
                {
                    // Para propiedades complejas, generar un ejemplo simple
                    var complexExample = new Dictionary<string, object>();
                    foreach(var subProp in propType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        complexExample[ToCamelCase(subProp.Name)] = GetExampleValue(subProp.PropertyType);
                    }
                    example[propName] = complexExample;
                }
            }

            return example;
        }

        return null;
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
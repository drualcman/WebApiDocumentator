namespace WebApiDocumentator.Metadata;

internal class ApiParameterInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool IsFromBody { get; set; }
    public string Source { get; set; } = "Unknown"; // Nueva: Path, Query, Body, Unknown
    public bool IsRequired { get; set; } // Nuevo: indica si el parámetro es obligatorio
    public string? Description { get; set; } // Nuevo: descripción del parámetro
    public Dictionary<string, object>? Schema { get; set; }
    public bool IsValueParameter => !(Source.Equals("Unknown") || Source.Equals("Service"));
    public bool IsCollection { get; set; }
    public string? CollectionElementType { get; set; }
}


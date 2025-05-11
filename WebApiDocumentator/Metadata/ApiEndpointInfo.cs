namespace WebApiDocumentator.Metadata;

internal class ApiEndpointInfo
{
    public string HttpMethod { get; set; }
    public string Route { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; } // Nuevo: descripción detallada del endpoint
    public List<ApiParameterInfo> Parameters { get; set; } = new();
    public string? ReturnType { get; set; }
    public Dictionary<string, object>? ReturnSchema { get; set; }
    public string ExampleJson { get; set; }
}


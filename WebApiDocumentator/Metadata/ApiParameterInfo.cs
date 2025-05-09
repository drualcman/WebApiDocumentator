namespace WebApiDocumentator.Metadata;

internal class ApiParameterInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool IsFromBody { get; set; }
    public Dictionary<string, object>? Schema { get; set; }
}


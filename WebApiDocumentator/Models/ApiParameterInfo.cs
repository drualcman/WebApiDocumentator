namespace WebApiDocumentator.Models;

internal class ApiParameterInfo
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool IsFromBody { get; set; }
    public string Source { get; set; } = "Unknown";
    public bool IsRequired { get; set; }
    public string Description { get; set; }
    public Dictionary<string, object> Schema { get; set; }
    public bool IsValueParameter => !(Source.Equals("Unknown") || Source.Equals("Service"));
    public bool IsCollection { get; set; }
    public string CollectionElementType { get; set; }
}


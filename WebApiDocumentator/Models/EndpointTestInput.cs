namespace WebApiDocumentator.Models;

internal class EndpointTestInput
{
    public string Id { get; set; }
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}


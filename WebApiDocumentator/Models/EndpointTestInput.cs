namespace WebApiDocumentator.Models;

internal class EndpointTestInput
{
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
}


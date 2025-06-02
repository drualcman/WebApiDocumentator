namespace WebApiDocumentator.Models;

internal class EndpointTestInput
{
    public string Id { get; set; }
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new();
    public Dictionary<string, IFormFile> Files { get; set; } = new();
    public AuthenticationInput Authentication { get; set; } = new();
    public Dictionary<string, List<string>> Collections { get; set; } = new Dictionary<string, List<string>>();
}


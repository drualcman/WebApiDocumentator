namespace WebApiDocumentator.Models;
internal class EndpointGroupNode
{
    public string Name { get; set; } = string.Empty;
    public List<ApiEndpointInfo> Endpoints { get; set; } = new List<ApiEndpointInfo>();
    public List<EndpointGroupNode> Children { get; set; } = new List<EndpointGroupNode>();
}

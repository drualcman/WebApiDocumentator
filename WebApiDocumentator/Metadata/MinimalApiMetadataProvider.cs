using Microsoft.AspNetCore.Routing;
using System.Text.RegularExpressions;

namespace WebApiDocumentator.Metadata;

internal class MinimalApiMetadataProvider : IMetadataProvider
{
    private readonly EndpointDataSource _endpointDataSource;

    public MinimalApiMetadataProvider(EndpointDataSource endpointDataSource)
    {
        _endpointDataSource = endpointDataSource;
    }

    public List<ApiEndpointInfo> GetEndpoints()
    {
        var endpoints = new List<ApiEndpointInfo>();

        foreach(var endpoint in _endpointDataSource.Endpoints)
        {
            if(endpoint is RouteEndpoint routeEndpoint)
            {
                var httpMethods = routeEndpoint.Metadata
                    .OfType<HttpMethodMetadata>()
                    .FirstOrDefault()?.HttpMethods;

                if(httpMethods == null || httpMethods.Count == 0)
                    continue;

                var displayName = routeEndpoint.DisplayName ?? "MinimalApiEndpoint";
                var routePattern = routeEndpoint.RoutePattern.RawText ?? "";

                var endpointInfo = new ApiEndpointInfo
                {
                    Route = routePattern,
                    HttpMethod = httpMethods[0], // normalmente uno solo (GET, POST, etc.)
                    Summary = GetMinimalApiSummary(routeEndpoint),
                    ReturnType = "Unknown (lambda)",
                    Parameters = new List<ApiParameterInfo>() // No hay forma fácil de inferirlos
                };

                endpoints.Add(endpointInfo);
            }
        }

        return endpoints;
    }

    private string? GetMinimalApiSummary(RouteEndpoint endpoint)
    {
        // Aquí puedes intentar extraer comentarios si están en anotaciones personalizadas
        return endpoint.DisplayName;
    }
}

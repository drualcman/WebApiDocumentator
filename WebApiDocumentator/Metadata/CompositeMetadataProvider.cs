namespace WebApiDocumentator.Metadata;
internal class CompositeMetadataProvider
{
    private readonly IEnumerable<IMetadataProvider> _providers;

    public CompositeMetadataProvider(IEnumerable<IMetadataProvider> providers)
    {
        _providers = providers;
    }

    public List<ApiGroupInfo> GetGroupedEndpoints()
    {
        var allEndpoints = _providers
                    .SelectMany(p => p.GetEndpoints())
                    .ToList();


        var grouped = allEndpoints
            .GroupBy(e => GetRouteGroupKey(e.Route))
            .Select(g => new ApiGroupInfo
            {
                PathPrefix = g.Key,
                Endpoints = g.ToList()
            })
            .OrderBy(g => g.PathPrefix)
            .ToList();

        return grouped;
    }

    private string GetRouteGroupKey(string route)
    {
        // Ruta base: user/management -> user
        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].ToLower() : "root";
    }
}


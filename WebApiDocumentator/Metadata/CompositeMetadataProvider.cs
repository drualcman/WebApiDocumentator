namespace WebApiDocumentator.Metadata;

internal class CompositeMetadataProvider
{
    private readonly IEnumerable<IMetadataProvider> _providers;
    private readonly ILogger<CompositeMetadataProvider> _logger;

    public CompositeMetadataProvider(
        IEnumerable<IMetadataProvider> providers,
        ILogger<CompositeMetadataProvider> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public List<ApiEndpointInfo> GetEndpoints()
    {
        var endpoints = _providers
            .SelectMany(p => p.GetEndpoints())
            .OrderBy(e => e.Route)
            .ThenBy(e => e.HttpMethod)
            .ToList();
        return endpoints;
    }

    public List<EndpointGroupNode> GetGroupedEndpoints()
    {
        var allEndpoints = GetEndpoints();
        var rootNodes = new List<EndpointGroupNode>();

        // Agrupar endpoints por el primer segmento o "root" para rutas sin prefijo
        var groupedByPrefix = allEndpoints
            .GroupBy(e => GetRouteGroupKey(e.Route))
            .OrderBy(g => g.Key == "root" ? "" : g.Key); // Priorizar "root"

        foreach(var group in groupedByPrefix)
        {
            var prefix = group.Key;
            var groupNode = new EndpointGroupNode { Name = prefix == "root" ? "/" : prefix };

            // Procesar los endpoints del grupo
            var endpointsByPath = group
                .GroupBy(e => GetFullPathWithoutPrefix(e.Route, prefix == "root" ? "" : prefix))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(e => e.HttpMethod).ToList());

            // Agrupar por el siguiente segmento significativo
            var subGroups = endpointsByPath
                .GroupBy(kvp => GetNextSegment(kvp.Key))
                .ToDictionary(
                    g => g.Key,
                    g => g.SelectMany(kvp => kvp.Value).ToList());

            foreach(var subGroup in subGroups.OrderBy(g => g.Key))
            {
                var subPrefix = subGroup.Key;
                if(string.IsNullOrEmpty(subPrefix))
                {
                    // Endpoint directo, ej: ping
                    groupNode.Endpoints.AddRange(subGroup.Value);
                    foreach(var endpoint in subGroup.Value)
                    {
                        _logger.LogInformation("Added direct endpoint to node {NodeName}: {Method} {Route}", prefix, endpoint.HttpMethod, endpoint.Route);
                    }
                    continue;
                }

                var subNode = new EndpointGroupNode { Name = subPrefix };

                // Procesar los endpoints del subgrupo
                var subEndpointsByPath = subGroup.Value
                    .GroupBy(e => GetFullPathWithoutPrefix(e.Route, $"{prefix}/{subPrefix}".TrimStart('/')))
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(e => e.HttpMethod).ToList());

                foreach(var pathGroup in subEndpointsByPath.OrderBy(g => g.Key))
                {
                    var path = pathGroup.Key;
                    var endpoints = pathGroup.Value;

                    if(string.IsNullOrEmpty(path))
                    {
                        // Endpoint directo, ej: .well-known/assetlinks.json
                        subNode.Endpoints.AddRange(endpoints);
                        foreach(var endpoint in endpoints)
                        {
                            _logger.LogInformation("Added endpoint to subnode {NodeName}: {Method} {Route}", subPrefix, endpoint.HttpMethod, endpoint.Route);
                        }
                        continue;
                    }

                    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var currentNode = subNode;

                    // Navegar hasta el penúltimo segmento
                    for(int i = 0; i < segments.Length - 1; i++)
                    {
                        var segment = CleanSegment(segments[i]);
                        var childNode = currentNode.Children.FirstOrDefault(c => c.Name == segment);
                        if(childNode == null)
                        {
                            childNode = new EndpointGroupNode { Name = segment };
                            currentNode.Children.Add(childNode);
                            _logger.LogInformation("Created child node: {ParentNode}/{NodeName}", currentNode.Name, segment);
                        }
                        currentNode = childNode;
                    }

                    // Último segmento
                    var lastSegment = CleanSegment(segments.Last());
                    var sameLastSegmentEndpoints = subEndpointsByPath
                        .Where(kvp => kvp.Key.EndsWith($"/{lastSegment}", StringComparison.OrdinalIgnoreCase) ||
                                      kvp.Key.Equals(lastSegment, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(kvp => kvp.Value)
                        .ToList();

                    if(sameLastSegmentEndpoints.Count > 1)
                    {
                        // Más de un endpoint con el mismo último segmento, crear subnodo
                        var lastNode = currentNode.Children.FirstOrDefault(c => c.Name == lastSegment);
                        if(lastNode == null)
                        {
                            lastNode = new EndpointGroupNode { Name = lastSegment };
                            currentNode.Children.Add(lastNode);
                            _logger.LogInformation("Created last node: {ParentNode}/{NodeName}", currentNode.Name, lastSegment);
                        }
                        lastNode.Endpoints.AddRange(endpoints);
                        foreach(var endpoint in endpoints)
                        {
                            _logger.LogInformation("Added endpoint to last node {NodeName}: {Method} {Route}", lastSegment, endpoint.HttpMethod, endpoint.Route);
                        }
                    }
                    else
                    {
                        // Solo un endpoint, añadir directamente
                        currentNode.Endpoints.AddRange(endpoints);
                        foreach(var endpoint in endpoints)
                        {
                            _logger.LogInformation("Added endpoint to node {NodeName}: {Method} {Route}", currentNode.Name, endpoint.HttpMethod, endpoint.Route);
                        }
                    }
                }

                // Colapsar subNode si tiene un solo endpoint y no tiene hijos
                if(subNode.Endpoints.Count == 1 && subNode.Children.Count == 0)
                {
                    groupNode.Endpoints.AddRange(subNode.Endpoints);
                    _logger.LogInformation("Collapsed single-endpoint node {NodeName} into {ParentNode}", subNode.Name, groupNode.Name);
                }
                else
                {
                    groupNode.Children.Add(subNode);
                }
            }

            rootNodes.Add(groupNode);
        }

        // Ordenar nodos y endpoints
        SortNodes(rootNodes);
        return rootNodes;
    }

    private string GetRouteGroupKey(string route)
    {
        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length == 0)
            return "root";
        return parts[0].ToLower();
    }

    private string GetNextSegment(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? CleanSegment(parts[0]) : "";
    }

    private string GetFullPathWithoutPrefix(string route, string prefix)
    {
        var normalizedRoute = route.Trim('/').ToLower();
        var normalizedPrefix = prefix.ToLower();
        if(string.IsNullOrEmpty(normalizedPrefix) || !normalizedRoute.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedRoute;
        }
        var remaining = normalizedRoute.Substring(normalizedPrefix.Length).Trim('/');
        return remaining;
    }

    private string CleanSegment(string segment)
    {
        // Limpiar parámetros, ej: {id:int} -> id
        if(segment.StartsWith("{") && segment.EndsWith("}"))
        {
            var paramName = segment.Trim('{', '}');
            return paramName.Split(':')[0];
        }
        return segment;
    }

    private void SortNodes(List<EndpointGroupNode> nodes)
    {
        foreach(var node in nodes)
        {
            node.Endpoints = node.Endpoints
                .OrderBy(e => e.Route)
                .ThenBy(e => e.HttpMethod)
                .ToList();
            SortNodes(node.Children);
        }
        nodes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebApiDocumentator.Metadata;

internal class ApiEndpointInfo
{
    public string HttpMethod { get; set; }
    public string Route { get; set; }
    public string? Summary { get; set; }

    public List<ApiParameterInfo> Parameters { get; set; } = new();
    public string? ReturnType { get; set; }
}


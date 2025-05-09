using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebApiDocumentator.Metadata;
internal class ApiGroupInfo
{
    public string PathPrefix { get; set; } = "";
    public string? Summary { get; set; }

    public List<ApiEndpointInfo> Endpoints { get; set; } = new();
}

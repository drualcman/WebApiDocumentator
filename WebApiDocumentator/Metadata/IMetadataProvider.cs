using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebApiDocumentator.Metadata;
internal interface IMetadataProvider
{
    List<ApiEndpointInfo> GetEndpoints();
}


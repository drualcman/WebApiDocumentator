namespace WebApiDocumentator.Metadata;
internal interface IMetadataProvider
{
    List<ApiEndpointInfo> GetEndpoints();
}


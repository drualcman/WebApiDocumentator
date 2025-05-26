namespace WebApiDocumentator.Options;

public class DocumentatorOptions
{
    public static string SectionKey = nameof(DocumentatorOptions);
    public string ApiName { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string DocsBaseUrl { get; set; } = "/api/docs";
}

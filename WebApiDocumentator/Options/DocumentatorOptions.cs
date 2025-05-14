namespace WebApiDocumentator.Options;


/// <summary>
/// Optiones
/// </summary>
public class DocumentatorOptions
{
    /// <summary>
    /// Nombre corte de la api
    /// </summary>
    [Required]
    public string ApiName { get; set; }
    /// <summary>
    /// Version de la api
    /// </summary>     
    [Required]
    public string Version { get; set; }
    /// <summary>
    /// Descripcino larga de la api
    /// </summary>   
    [Required]
    public string Description { get; set; }
    public string DocsBaseUrl { get; set; } = "/Docs";
}

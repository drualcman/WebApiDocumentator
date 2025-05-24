namespace WebApiDocumentator.Models;
internal class AuthenticationInput
{
    public AuthenticationType Type { get; set; } = AuthenticationType.None; // None, Bearer, ApiKey
    public string? BearerToken { get; set; } = string.Empty;
    public string? ApiKeyValue { get; set; } = string.Empty;
    public string? ApiKeyName { get; set; } = "X-Api-Key"; // Defaults to "X-Api-Key" for Header
    public string? ApiKeyLocation { get; set; } = "Header"; // Header or Query  
    public string? BasicUsername { get; set; } = string.Empty;
    public string? BasicPassword { get; set; } = string.Empty;
}

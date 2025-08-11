namespace WebApiDocumentator.Models;
internal class AuthenticationInput
{
    public AuthenticationType Type { get; set; } = AuthenticationType.None;
    public string BearerToken { get; set; } = string.Empty;
    public string ApiKeyValue { get; set; } = string.Empty;
    public string ApiKeyName { get; set; } = "X-Api-Key";
    public string ApiKeyLocation { get; set; } = "Header";
    public string BasicUsername { get; set; } = string.Empty;
    public string BasicPassword { get; set; } = string.Empty;
}

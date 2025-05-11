namespace WebApiDocumentator.Helpers;
internal static class EndpointHelper
{
    public static string GenerateEndpointId(string httpMethod, string route)
    {
        var input = $"{httpMethod}:{route}".ToLowerInvariant();
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes).TrimEnd('=').Replace('/', '_').Replace('+', '-');
    }
}

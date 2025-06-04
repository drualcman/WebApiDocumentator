namespace WebApiDocumentator.Helpers;
internal static class TypeNameHelper
{
    public static string GetFriendlyTypeName(Type type)
    {
        if(type == null)
            return "Unknown";
        if(type.IsGenericType)
        {
            var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
            return $"{type.Name.Split('`')[0]}<{genericArgs}>";
        }
        return type.Name;
    }
}

namespace WebApiDocumentator.Helpers;
internal static class XmlDocumentationHelper
{
    public static string? GetXmlSummary(Dictionary<string, string> xmlDocs, MemberInfo? member)
    {
        if(member == null || (!xmlDocs?.Any() ?? true))
            return null;

        var memberId = GetXmlMemberName(member);
        return xmlDocs.TryGetValue(memberId, out var value) ? value : null;
    }

    public static string? GetXmlParamSummary(Dictionary<string, string> xmlDocs, string methodXmlKey, string? paramName)
    {
        if(string.IsNullOrWhiteSpace(methodXmlKey) || string.IsNullOrWhiteSpace(paramName) || (!xmlDocs?.Any() ?? true))
            return null;

        var paramKey = $"{methodXmlKey}#{paramName}";
        return xmlDocs.TryGetValue(paramKey, out var value) ? value : null;
    }

    public static string GetXmlMemberName(MemberInfo member)
    {
        if(member is Type type)
            return "T:" + type.FullName;

        if(member is MethodInfo method)
        {
            var paramTypes = method.GetParameters()
                .Select(p => p.ParameterType.FullName ?? "Unknown")
                .ToArray();

            var methodName = $"{method.DeclaringType?.FullName}.{method.Name}";
            if(paramTypes.Length > 0)
                methodName += $"({string.Join(",", paramTypes)})";

            return "M:" + methodName;
        }

        if(member is PropertyInfo property)
        {
            var declaringTypeName = property.DeclaringType?.FullName?.Replace("+", ".") ?? "Unknown";
            return $"P:{declaringTypeName}.{property.Name}";
        }

        return member.Name;
    }

    public static string? GetXmlReturns(Dictionary<string, string> xmlDocs, MethodInfo method)
    {
        if(!xmlDocs?.Any() ?? true)
            return null;
        var key = GetXmlMemberName(method);
        return xmlDocs.TryGetValue($"{key}#returns", out var value) ? value : null;
    }

    public static string? GetXmlRemarks(Dictionary<string, string> xmlDocs, MethodInfo method)
    {
        if(!xmlDocs?.Any() ?? true)
            return null;
        var key = GetXmlMemberName(method);
        return xmlDocs.TryGetValue($"{key}#remarks", out var value) ? value : null;
    }
}

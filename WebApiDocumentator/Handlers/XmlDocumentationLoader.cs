namespace WebApiDocumentator.Handlers;
internal class XmlDocumentationLoader
{
    private readonly ILogger? _logger;

    public XmlDocumentationLoader(ILogger? logger = null)
    {
        _logger = logger;
    }

    public Dictionary<string, string> LoadXmlDocumentation(Assembly assembly)
    {
        return LoadXmlDocumentation(new[] { assembly });
    }

    public Dictionary<string, string> LoadXmlDocumentation(IEnumerable<Assembly> assemblies)
    {
        var result = new Dictionary<string, string>();

        foreach(var assembly in assemblies.Distinct())
        {
            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");
            if(File.Exists(xmlFile))
            {
                try
                {
                    var doc = XDocument.Load(xmlFile);
                    foreach(var member in doc.Descendants("member"))
                    {
                        var nameAttr = member.Attribute("name")?.Value;
                        if(!string.IsNullOrWhiteSpace(nameAttr))
                        {
                            var summary = member.Element("summary")?.Value?.Trim();
                            if(!string.IsNullOrWhiteSpace(summary))
                            {
                                result[nameAttr] = summary;
                            }

                            if(nameAttr.StartsWith("M:"))
                            {
                                foreach(var param in member.Elements("param"))
                                {
                                    var paramName = param.Attribute("name")?.Value;
                                    var paramSummary = param.Value?.Trim();
                                    if(!string.IsNullOrWhiteSpace(paramName) && !string.IsNullOrWhiteSpace(paramSummary))
                                    {
                                        result[$"{nameAttr}#{paramName}"] = paramSummary;
                                    }
                                }
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    _logger?.LogError(ex, $"Error reading XML documentation from {xmlFile}");
                }
            }
        }

        return result;
    }
}

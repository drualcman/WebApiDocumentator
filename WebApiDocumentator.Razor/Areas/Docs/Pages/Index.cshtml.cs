using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using WebApiDocumentator.Metadata;

namespace WebApiDocumentator.Razor.Areas.Docs.Pages;

public class IndexModel : PageModel
{
    private readonly CompositeMetadataProvider _metadataProvider;

    public List<ApiGroupInfo> Groups { get; private set; }

    public IndexModel(CompositeMetadataProvider metadataProvider)
    {
        _metadataProvider = metadataProvider;
    }

    public void OnGet()
    {
        Groups = _metadataProvider.GetGroupedEndpoints();
    }
}


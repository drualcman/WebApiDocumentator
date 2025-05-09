using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System.Reflection;
using WebApiDocumentator.Metadata;
using WebApiDocumentator.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMyApiDocs(this IServiceCollection services, 
        Action<DocumentatorOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IMetadataProvider, MinimalApiMetadataProvider>();
        services.AddSingleton<IMetadataProvider, ControllerMetadataProvider>(); 
        services.AddSingleton<CompositeMetadataProvider>();
        services.AddRazorPages();
        services.AddHttpClient("WebApiDocumentator");

        return services;
    }

    public static WebApplication UseWebApiDocumentatorUi(this WebApplication app)
    {
        app.MapRazorPages();
        app.MapGet("/get-metadata", (CompositeMetadataProvider provider) =>
        {
            var metadata = provider.GetGroupedEndpoints();
            return Results.Ok(metadata);
        })
        .WithTags("WebApiDocumentator")
        .ExcludeFromDescription();
        return app;
    }
}

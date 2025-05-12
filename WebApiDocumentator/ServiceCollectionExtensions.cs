using Microsoft.AspNetCore.Builder;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMyApiDocs(this IServiceCollection services,
        Action<DocumentatorOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IParameterSourceResolver, ParameterSourceResolver>();
        services.AddSingleton<IMetadataProvider, MinimalApiMetadataProvider>();
        services.AddSingleton<IMetadataProvider, ControllerMetadataProvider>();
        services.AddSingleton<CompositeMetadataProvider>();
        services.AddRazorPages();
        services.AddHttpClient("WebApiDocumentator");
        services.AddLogging();

        return services;
    }

    public static IApplicationBuilder UseWebApiDocumentatorUi(this IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();
        });

        return app;
    }

    public static WebApplication UseWebApiDocumentatorUi(this WebApplication app)
    {
        app.MapRazorPages();
        return app;
    }
}

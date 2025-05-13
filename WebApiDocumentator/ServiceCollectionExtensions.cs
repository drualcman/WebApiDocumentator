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
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30); // Duración de la sesión
            options.Cookie.HttpOnly = true; // Seguridad: evita acceso desde JavaScript
            options.Cookie.IsEssential = true; // Necesario para GDPR
        });

        return services;
    }

    public static IApplicationBuilder UseWebApiDocumentatorUi(this IApplicationBuilder app)
    {
        app.UseSession();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();
        });

        return app;
    }

    public static WebApplication UseWebApiDocumentatorUi(this WebApplication app)
    {
        app.UseSession();
        app.MapRazorPages();
        return app;
    }
}

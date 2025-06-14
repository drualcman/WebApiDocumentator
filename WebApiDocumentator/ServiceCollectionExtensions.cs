﻿namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebApiDocumentator(this IServiceCollection services,
        Action<DocumentatorOptions> configure)
    {
        DocumentatorOptions customOptions = new();
        configure(customOptions);
        services.Configure(configure);
        services.AddScoped<IParameterSourceResolver, ParameterSourceResolver>();
        services.AddScoped<IMetadataProvider, MinimalApiMetadataProvider>();
        services.AddScoped<IMetadataProvider, ControllerMetadataProvider>();
        services.AddScoped<CompositeMetadataProvider>();
        services.AddScoped<EndpointService>();
        services.AddScoped<AuthenticationHandler>();
        services.AddScoped<UrlBuilder>();
        services.AddScoped<FormContentBuilder>();
        services.AddScoped<ContentBuilder>();
        services.AddScoped<RequestBuilder>();
        services.AddScoped<ResponseProcessor>();
        services.AddScoped<RequestProcessor>();
        services.AddRazorPages(options =>
        {
            if(!string.IsNullOrWhiteSpace(customOptions.DocsBaseUrl))
            {
                options.Conventions.AddAreaPageRoute(
                    areaName: "WebApiDocumentator",
                    pageName: "/Index",
                    route: NormalizePath(customOptions.DocsBaseUrl)
                );
            }
        });
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

    public static IApplicationBuilder UseWebApiDocumentator(this IApplicationBuilder app)
    {
        app.UseSession();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();
        });
        return app;
    }

    public static WebApplication UseWebApiDocumentator(this WebApplication app)
    {
        app.UseSession();
        app.MapRazorPages();
        return app;
    }

    private static string NormalizePath(string path)
    {
        if(string.IsNullOrEmpty(path))
            return "/";
        path = path.Trim();
        if(!path.StartsWith("/"))
            path = "/" + path;
        return path.TrimEnd('/');
    }
}

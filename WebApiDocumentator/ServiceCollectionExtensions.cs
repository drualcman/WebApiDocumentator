using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Session;
using WebApiDocumentator.Internals;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures the necessary services for WebApiDocumentator, including support for sessions, Razor Pages,
    /// and other required services. This method must be called in ConfigureServices (Startup.cs)
    /// or in the service configuration in Program.cs (before building the application).
    /// It does not duplicate services if they are already registered (such as AddSession, AddRazorPages, or AddLogging).
    /// </summary>
    /// <param name="services">The service container.</param>
    /// <param name="configure">Action to configure WebApiDocumentator options.</param>
    /// <returns>The modified service container.</returns>
    /// <example>
    /// builder.Services.AddWebApiDocumentator(options => { options.DocsBaseUrl = "/api-docs"; });
    /// </example>
    public static IServiceCollection AddWebApiDocumentator(this IServiceCollection services,
        Action<DocumentatorOptions> configure)
    {
        DocumentatorOptions customOptions = new();
        configure(customOptions);
        services.Configure(configure);
        if(!services.Any(s => s.ServiceType == typeof(IApiDescriptionGroupCollectionProvider)))
        {
            services.AddEndpointsApiExplorer();
        }
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
        if(!services.Any(s => s.ServiceType == typeof(IRazorViewEngine)))
        {
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
            services.AddSingleton<IRazorPagesRegistrationMarker, Markers>();
        }
        else
        {
            services.Configure<RazorPagesOptions>(options =>
            {
                options.Conventions.AddAreaPageRoute(
                    areaName: "WebApiDocumentator",
                    pageName: "/Index",
                    route: NormalizePath(customOptions.DocsBaseUrl)
                );
            });
        }
        services.AddHttpClient("WebApiDocumentator");
        if(!services.Any(s => s.ServiceType == typeof(ILoggerFactory)))
        {
            services.AddLogging();
        }
        if(!services.Any(s => s.ServiceType == typeof(ISessionStore)))
        {
            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });
            services.AddSingleton<ISessionRegistrationMarker, Markers>();
        }
        return services;
    }

    /// <summary>
    /// Adds the session middleware for WebApiDocumentator if it was registered by AddWebApiDocumentator.
    /// This method is optional if the user has already configured sessions manually with UseSession().
    /// It must be placed after UseExceptionHandler/UseStaticFiles and before UseRouting.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The modified application builder.</returns>
    /// <example>
    /// ...
    /// app.UseWebApiDocumentatorSessions();
    /// app.UseRouting();
    /// ...
    /// </example>
    public static IApplicationBuilder UseWebApiDocumentatorSessions(this IApplicationBuilder app)
    {
        var serviceProvider = app.ApplicationServices;
        var sessionMarker = serviceProvider.GetService<ISessionRegistrationMarker>();
        if(sessionMarker != null)
        {
            app.UseSession();
        }
        return app;
    }

    /// <summary>
    /// Adds the Razor Pages mapping for WebApiDocumentator if it was registered by AddWebApiDocumentator.
    /// This method is mandatory to ensure the documentator routes are available.
    /// It must be placed after UseRouting and before other endpoints.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The modified application builder.</returns>
    /// <example>
    /// ...
    /// app.UseRouting();   
    /// app.MapRazorPages();
    /// app.UseWebApiDocumentator();
    /// ...
    /// </example>
    public static IApplicationBuilder MapWebApiDocumentator(this IApplicationBuilder app)
    {
        var serviceProvider = app.ApplicationServices;
        var razorMarker = serviceProvider.GetService<IRazorPagesRegistrationMarker>();
        if(razorMarker != null)
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
            });
        }
        return app;
    }
    public static IApplicationBuilder UseWebApiDocumentator(this IApplicationBuilder app)
    {
        app.MapWebApiDocumentator();
        return app;
    }

    /// <summary>
    /// Adds the session middleware for WebApiDocumentator if it was registered by AddWebApiDocumentator.
    /// This method is optional if the user has already configured sessions manually with UseSession().
    /// It must be placed after UseExceptionHandler/UseStaticFiles and before UseRouting.
    /// </summary>
    /// <param name="app">The WebApplication application.</param>
    /// <returns>The modified application.</returns>
    /// <example>
    /// ...
    /// app.UseWebApiDocumentatorSessions();
    /// app.UseRouting();
    /// ...
    /// </example>
    public static WebApplication UseWebApiDocumentatorSessions(this WebApplication app)
    {
        var sessionMarker = app.Services.GetService<ISessionRegistrationMarker>();
        if(sessionMarker != null)
        {
            app.UseSession();
        }
        return app;
    }

    /// <summary>
    /// Adds the Razor Pages mapping for WebApiDocumentator if it was registered by AddWebApiDocumentator.
    /// This method is mandatory to ensure the documentator routes are available.
    /// It must be placed after UseRouting and before other endpoints.
    /// </summary>
    /// <param name="app">The WebApplication application.</param>
    /// <returns>The modified application.</returns>
    /// <example>
    /// ...
    /// app.UseRouting();        
    /// app.MapRazorPages();
    /// app.MapWebApiDocumentator();
    /// ...
    /// </example>
    public static WebApplication MapWebApiDocumentator(this WebApplication app)
    {
        var razorMarker = app.Services.GetService<IRazorPagesRegistrationMarker>();
        if(razorMarker != null)
        {
            app.MapRazorPages();
        }
        return app;
    }
    public static WebApplication UseWebApiDocumentator(this WebApplication app)
    {
        app.MapWebApiDocumentator();
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
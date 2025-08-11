[![Nuget](https://img.shields.io/nuget/v/WebApiDocumentator?style=for-the-badge)](https://www.nuget.org/packages/WebApiDocumentator)
[![Nuget](https://img.shields.io/nuget/dt/WebApiDocumentator?style=for-the-badge)](https://www.nuget.org/packages/WebApiDocumentator)


# WebApiDocumentator
`WebApiDocumentator` is a quick and easy way to create an interface to document a WebAPI built in .NET Core. It creates a user-friendly interface and has options for endpoint testing.

## Features

- Automatic documentation using XML metadata from C# code
- Show endpoints tree estructure
- HTML interface
- Test endpoints

## Installation

1. Install the nuget package via the package manager:

    ```
    dotnet add package WebApiDocumentator
    ```

2. Or by using the NuGet CLI:

    ```
    nuget install WebApiDocumentator
    ```

## Quick Start

### Step 1: Add WebApiDocumentator to Your API

In your `Program.cs`, you will need to add the middleware to your service collection and configure the options.

#### 1.1 Configure Options

You can customize the url for the page and add basic data via `DocumentatorOptions`.

In appsettings json using `IOptions<DocumentatorOptions>` file like:
```json
  "DocumentatorOptions": {
    "ApiName": "Your api name",
    "Version": "Your version, it's a string",
    "Description": "Full descripcion about your API",
    "DocsBaseUrl": "documentation path, defatul it's [api root]/WebApiDocumentator",
    "EnableTesting": true,
    "ShopOpenApiLink": false
  }
```
Then in the definition of the API:
```csharp
builder.Services.AddWebApiDocumentator(
    options => builder.Configuration.GetSection(SmartCacheOptions.SectionKey).Bind(options)
    );
```
Or directly like:
```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddWebApiDocumentator(options =>
    {
        options.ApiName = "Test Api";
        options.Version = "v1";
        options.Description = "The best API in the world!";
        options.DocsBaseUrl = "docs/api";
        options.EnableTesting = true;
        options.ShopOpenApiLink = false;
    });
}

//minimal api with default options
builder.Services.AddWebApiDocumentator();
```
#### 1.2 Add the user interface

In your `Configure` method, add the middleware to the pipeline using `UseWebApiDocumentator()`:

```csharp
public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // Add other middlewares like authentication, etc.    
    app.UseWebApiDocumentatorSessions();    // Optional if you already use app.UseSession()
    app.UseRouting();
    app.MapRazorPages();                    // If you are using razor pages should be before UseWebApiDocumentator()
    app.MapWebApiDocumentator();            // Required for WebApiDocumentator routes
}

//minimal apivar 
app = builder.Build();
...
app.UseWebApiDocumentatorSessions();    // Optional if you already use app.UseSession()
app.UseRouting();
app.MapRazorPages();                    // If you are using razor pages should be before UseWebApiDocumentator()
app.MapWebApiDocumentator();            // Required for WebApiDocumentator routes
...

app.Run();
```

### Step 2: Interface HTML

To access to the interface you can use the default url

```[your api url]/WebApiDocumentator```

Or if you personalize the url then use your own url

```[your api url]/api/docs```

*Remind:* Always you can use a defatult WebApiDocumentator page.

#### 2.1 Home page

- Show the name, version and description from your options.
- Show the schema of your API
- Right side bar with to search and select endpoint

#### 2.2 Selected endpoint

- Show documentation information
- Show params type and from where
- Show testing tab

## Models
If you use Options Pathern then this is the class should me match in the json configuration file. And this is the default values.
```csharp
public class DocumentatorOptions
{
    public static string SectionKey = nameof(DocumentatorOptions);
    public string ApiName { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string DocsBaseUrl { get; set; } = "/api/docs";
    public bool EnableTesting { get; set; } = true;
    public bool ShopOpenApiLink { get; set; }
}
```
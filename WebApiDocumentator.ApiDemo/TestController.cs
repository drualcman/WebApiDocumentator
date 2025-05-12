using Microsoft.AspNetCore.Mvc;
using WebApiDocumentator.ApiDemo.Services;
using WebApiDocumentator.Options;

namespace WebApiDocumentator.ApiDemo;
[Route("api/Test")]
[ApiController]
public class TestController : ControllerBase
{
    /// <summary>
    /// Obtiene las opciones de configuración.
    /// </summary>
    /// <param name="options">Opciones de configuración pasadas como parámetros de consulta.</param>
    /// <returns>Las opciones recibidas.</returns>
    [HttpGet("{name}")]
    public DocumentatorOptions GetOptions(string name, [FromQuery] DocumentatorOptions options, IHttpClientFactory client, SomeServicio servicioInterno)
    {
        options.ApiName = $"{name}: {options.ApiName}";
        return options;
    }
    /// <summary>
    /// Obtiene las opciones de configuración. Este lo muestra como POST
    /// </summary>
    /// <param name="options">Opciones de configuración pasadas como parámetros de consulta.</param>
    /// <returns>Las opciones recibidas.</returns>
    [HttpPost("otro/{name}")]
    public DocumentatorOptions PostOptions(string name, DocumentatorOptions options, IHttpClientFactory client, SomeServicio servicioInterno)
    {
        options.ApiName = $"{name}: {options.ApiName}";
        return options;
    }
}

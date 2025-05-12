using Microsoft.AspNetCore.Mvc;
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
    [HttpGet("otro/{name}")]
    public DocumentatorOptions GetOptions(string name, [FromQuery] DocumentatorOptions options)
    {
        options.ApiName = $"{name}: {options.ApiName}";
        return options;
    }
}

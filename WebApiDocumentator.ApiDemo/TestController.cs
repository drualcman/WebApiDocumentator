﻿using Microsoft.AspNetCore.Mvc;
using WebApiDocumentator.ApiDemo.Services;
using WebApiDocumentator.Options;

namespace WebApiDocumentator.ApiDemo;
[Route("api/[controller]")]
[ApiController]
public class TestController : ControllerBase
{
    /// <summary>
    ///  Obtiene las opciones de configuración.
    /// </summary>
    /// <param name="id">la opcion que deas cambiar</param>
    /// <param name="options">Opciones de configuración pasadas como parámetros de consulta.</param>
    /// <param name="client">Servicio api externo</param>
    /// <param name="servicioInterno">otro servicio de base de datos externo</param>
    /// <returns>Las opciones recibidas.</returns>
    [HttpGet("{id:int}")]
    public WeatherForecast[] GetOptions(int id, [FromForm] DocumentatorOptions options, IHttpClientFactory client, SomeServicio servicioInterno)
    {
        options.ApiName = $"{id}: {options.ApiName}";
        return [];
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

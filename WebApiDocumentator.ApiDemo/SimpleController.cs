using Microsoft.AspNetCore.Mvc;
using WebApiDocumentator.ApiDemo.Models;

namespace WebApiDocumentator.ApiDemo;

[Route("api/[controller]")]
[ApiController]
public class SimpleController : ControllerBase
{
    [HttpGet("{name}")]
    public ModelOne Get(string name) => new ModelOne { Name = $"Hello {name}!", OtherModel = new ModelTwo { Id = 1, Someelse = $"Adios {name}!" } };
}

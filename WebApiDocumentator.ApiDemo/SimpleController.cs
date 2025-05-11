using Microsoft.AspNetCore.Mvc;

namespace WebApiDocumentator.ApiDemo;

[Route("api/[controller]")]
[ApiController]
public class SimpleController : ControllerBase
{
    [HttpGet("{name}")]
    public IActionResult Get(string name) => Ok($"Hello {name}!");
}

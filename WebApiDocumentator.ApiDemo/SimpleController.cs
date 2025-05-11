using Microsoft.AspNetCore.Mvc;

namespace WebApiDocumentator.ApiDemo;

[Route("api/[controller]")]
[ApiController]
public class SimpleController : ControllerBase
{
    [HttpGet("{name}")]
    public string Get(string name) => $"Hello {name}!";
}

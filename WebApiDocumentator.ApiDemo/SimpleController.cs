using Microsoft.AspNetCore.Mvc;

namespace WebApiDocumentator.ApiDemo;

[Route("api/[controller]")]
[ApiController]
public class SimpleController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("Hello");
}

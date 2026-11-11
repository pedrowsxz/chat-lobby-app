using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lobby.Web.Controllers;

// Anonymous so that a 404 for a signed-out visitor is a 404 page, not a redirect to login.
[AllowAnonymous]
[IgnoreAntiforgeryToken] // a failed POST is re-executed here as a POST; showing the page must not fail again
[Route("erro")]
public class ErrorController : Controller
{
    [Route("{code:int}")]
    public IActionResult Status(int code)
    {
        ViewBag.Code = code;
        return View("Status");
    }

    [Route("")]
    public IActionResult Error()
    {
        ViewBag.Code = 500;
        return View("Status");
    }
}

using Lobby.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lobby.Web.Controllers;

public class AvatarsController(AvatarStorage avatars) : Controller
{
    [HttpGet("/avatars/{fileName}")]
    public IActionResult Get(string fileName)
    {
        // GetPath rejects any name that is not "{32 hex}.{jpg|png|webp}", so "../" can't get through.
        var path = avatars.GetPath(fileName);
        if (path is null) return NotFound();

        // nosniff: the browser must trust our Content-Type and never guess "this looks like HTML".
        Response.Headers.XContentTypeOptions = "nosniff";
        // A new upload always gets a new file name, so a file never changes and can be cached for long.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return PhysicalFile(path, AvatarStorage.ContentTypeFor(fileName));
    }
}

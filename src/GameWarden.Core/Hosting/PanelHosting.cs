using GameWarden.Core.Api;
using Microsoft.Extensions.FileProviders;

namespace GameWarden.Core.Hosting;

/// <summary>
/// Serves the built panel from <c>wwwroot</c> next to Core (docs/11 §12): hashed assets cached for a year, everything
/// else revalidated, and client routes answered with <c>index.html</c>. Development runs the panel on Vite instead, so
/// a Core without <c>wwwroot</c> serves the API only.
/// </summary>
public static class PanelHosting
{
    public static WebApplication UsePanelFiles(this WebApplication app)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(root))
        {
            return app;
        }

        var files = new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
                context.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
        };
        app.UseStaticFiles(files);
        return app;
    }

    /// <summary>Client routes (<c>/servers/valheim-main</c>) get the panel; unknown API paths stay a 404 problem.</summary>
    public static void MapPanelFallback(this WebApplication app)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!File.Exists(Path.Combine(root, "index.html")))
        {
            return;
        }

        var index = new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
        };
        app.MapFallback("/api/{**path}", () => ApiProblems.Create(StatusCodes.Status404NotFound, "not_found", "No such API endpoint."))
            .ExcludeFromDescription();
        app.MapFallback("/hubs/{**path}", () => Results.NotFound()).ExcludeFromDescription();
        app.MapFallbackToFile("index.html", index);
    }
}

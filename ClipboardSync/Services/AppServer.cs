using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PhotinoNetUtils.Hosting;
using PhotinoNetUtils.Utils;
using Serilog;

namespace ClipboardSync.Services;

/// <summary>
///     The one local server this window talks to: the built interface in a release build, and the
///     image route in both builds. A debug build loads the interface from Vite, so the host is still
///     needed there for the pictures.
/// </summary>
internal static class AppServer
{
    /// <summary>The prefix for this run's data routes, or empty before the server started.</summary>
    public static string DataUrl { get; private set; } = "";

    public static StaticServer Start()
    {
        var server = StaticServer.Start(
            Constants.DataDirectory,
            typeof(AppServer).Assembly,
            "Resources/wwwroot",
            MapData,
#if DEBUG
            // The page comes from Vite in this build, so a fetch from it is cross-origin; an <img>
            // needs no CORS, but a route read through fetch would.
            ["http://127.0.0.1:3300", "http://localhost:3300"]
#else
            []
#endif
        );

        DataUrl = server.DataUrl;
        Log.Information("Data plane at {Url}", DataUrl);

        return server;
    }

    private static void MapData(WebApplication app, string token)
    {
        // A clipboard screenshot is megabytes of PNG; as a response it keeps the browser's own cache
        // and connection pool, instead of a base64 string through the bridge.
        app.MapGet($"/data/{token}/image/{{id}}", async (string id) =>
        {
            var record = await ClipboardStore.Instance.FindAsync(id);

            if (record is not { Kind: "image", DataFile: { Length: > 0 } stored })
                return Results.NotFound();

            // The stored path is relative to the app's data directory, not to the process.
            var file = Path.Combine(Constants.DataDirectory, stored);
            if (!File.Exists(file))
                return Results.NotFound();

            return Results.File(file, "image/png", enableRangeProcessing: false,
                lastModified: File.GetLastWriteTimeUtc(file));
        });
    }
}

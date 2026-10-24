namespace Prodimt.Pedidos.Api;

public static class PilotWebHosting
{
    public static void UsePilotStaticFiles(this WebApplication app)
    {
        // Static delivery is public; only compiled assets belong in wwwroot.
        // Reserved API/health paths must never be satisfied by a SPA or static file.
        app.UseWhen(context => !IsReserved(context.Request.Path), branch => branch.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                if (context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                    context.Context.Response.Headers.CacheControl = "no-store";
            }
        }));
    }

    public static void MapPilotWeb(this WebApplication app)
    {
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            var isUi = path == "/" || path == "/cliente" || path.StartsWithSegments("/admin");
            var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            var file = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
            if (!isUi || !isRead || IsReserved(path) || !file.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength = file.Length;
            context.Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsGet(context.Request.Method))
                await context.Response.SendFileAsync(file, context.RequestAborted);
        });
    }

    private static bool IsReserved(PathString path) => path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
}

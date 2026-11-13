using ClipboardSync.Messaging;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>Opens external links in the system browser.</summary>
[Singleton]
internal partial class LinkService
{
    [NativeMessage("system.openUrl")]
    private static NativeResponse Open(NativeContext context)
    {
        var url = context.Payload<OpenUrlRequest>()?.Url;

        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return context.Fail("system.openUrl.result", Localization.Get("error.open_url_failed"));

        var started = Try.Run(() => SystemLauncher.Open(uri.AbsoluteUri));

        if (started.Exception is not { } exception)
            return context.Reply("system.openUrl.result", new OkPayload());

        Constants.Logger.Warning(exception, "Could not open {Url}", uri);
        return context.Fail("system.openUrl.result", Localization.Get("error.open_url_failed"));
    }
}

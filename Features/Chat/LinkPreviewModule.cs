using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;

namespace VibeTrade.Backend.Features.Chat;

public static class LinkPreviewModule
{
    public static WebApplication MapLinkPreviewEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/link-preview", GetLinkPreviewAsync).WithTags("Utils");
        return app;
    }

    private static async Task<IResult> GetLinkPreviewAsync(
        string? url,
        IHttpClientFactory httpFactory,
        CancellationToken cancellationToken)
    {
        if (!TryValidateUrl(url, out var absolute))
            return Results.BadRequest();

        var client = httpFactory.CreateClient("linkPreview");
        using var req = new HttpRequestMessage(HttpMethod.Get, absolute);
        using var res = await client.SendAsync(
                req,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return Results.Ok(new LinkPreviewResponse(
                absolute.ToString(),
                null,
                null,
                null));
        }

        var html = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var title = MetaContent(html, "og:title")
            ?? MetaContent(html, "twitter:title");
        var desc = MetaContent(html, "og:description")
            ?? MetaContent(html, "description");
        var image = MetaContent(html, "og:image");
        return Results.Ok(new LinkPreviewResponse(absolute.ToString(), title, desc, image));
    }

    private static bool TryValidateUrl(string? url, [NotNullWhen(true)] out Uri? absolute)
    {
        absolute = null;
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u))
            return false;
        if (u.Scheme is not ("http" or "https"))
            return false;
        var h = (u.Host ?? "").ToLowerInvariant();
        if (h is "localhost" or "127.0.0.1" or "::1")
            return false;
        if (h.EndsWith(".local", StringComparison.Ordinal))
            return false;
        absolute = u;
        return true;
    }

    private static string? MetaContent(string html, string propertyOrName)
    {
        var esc = Regex.Escape(propertyOrName);
        var m = Regex.Match(
            html,
            $"(?is)<meta[^>]+property=[\"']{esc}[\"'][^>]+content=[\"']([^\"']+)[\"']");
        if (m.Success)
            return WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
        m = Regex.Match(
            html,
            $"(?is)<meta[^>]+name=[\"']{esc}[\"'][^>]+content=[\"']([^\"']+)[\"']");
        if (m.Success)
            return WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
        return null;
    }
}

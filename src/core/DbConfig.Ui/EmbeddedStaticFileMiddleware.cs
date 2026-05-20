using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace DbConfig.Ui;

/// <summary>
/// Renders the embedded <c>index.html</c> with the API prefix meta tag injected and
/// relative asset paths rewritten to absolute paths rooted at the UI prefix. Also
/// exposes the underlying <see cref="EmbeddedFileProvider"/> so the route group can
/// hand it to ASP.NET's <c>StaticFileMiddleware</c> for serving the rest of the
/// embedded UI bundle (assets, fonts, favicon).
/// </summary>
internal sealed class EmbeddedStaticFileMiddleware
{
    private const string EmbeddedNamespace = "DbConfig.Ui.dist";
    private readonly string _indexHtml;

    internal EmbeddedStaticFileMiddleware(string uiPrefix, string apiPrefix, bool hasBuiltInLogin)
    {
        FileProvider = new EmbeddedFileProvider(
            typeof(EmbeddedStaticFileMiddleware).GetTypeInfo().Assembly,
            EmbeddedNamespace);

        _indexHtml = BuildIndexHtml(uiPrefix, apiPrefix, hasBuiltInLogin);
    }

    internal EmbeddedFileProvider FileProvider { get; }

    internal async Task ServeIndexAsync(HttpContext context)
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html;charset=utf-8";
        await context.Response.WriteAsync(_indexHtml, Encoding.UTF8, context.RequestAborted);
    }

    private string BuildIndexHtml(string uiPrefix, string apiPrefix, bool hasBuiltInLogin)
    {
        var fileInfo = FileProvider.GetFileInfo("index.html");
        if (!fileInfo.Exists)
        {
            return string.Empty;
        }

        using var stream = fileInfo.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = reader.ReadToEnd();

        // Vite emits relative paths ("./assets/foo.js"). When the UI is mounted at a
        // sub-path like "/admin/dbconfig" and served WITHOUT a trailing slash, the browser
        // resolves "./assets/foo.js" against "/admin/dbconfig" → "/admin/assets/foo.js" (404).
        // Rewrite to absolute paths rooted at the UI prefix so assets resolve regardless of
        // trailing-slash. Mirrors sister project Warp's WarpUIMiddleware.
        var normalizedPrefix = uiPrefix.TrimEnd('/');
        html = html.Replace("src=\"./", $"src=\"{normalizedPrefix}/", StringComparison.Ordinal);
        html = html.Replace("href=\"./", $"href=\"{normalizedPrefix}/", StringComparison.Ordinal);

        var metaTag = $"<meta name=\"db-config-api-prefix\" content=\"{apiPrefix}\" />";
        var hasLoginLiteral = hasBuiltInLogin ? "true" : "false";
        var scriptBlock = $"<script>window.dbConfig = {{ apiPrefix: \"{apiPrefix}\", hasBuiltInLogin: {hasLoginLiteral} }};</script>";
        var headEndIndex = html.IndexOf("</head>", StringComparison.Ordinal);
        if (headEndIndex >= 0)
        {
            html = html.Insert(headEndIndex, metaTag + scriptBlock);
        }

        return html;
    }
}

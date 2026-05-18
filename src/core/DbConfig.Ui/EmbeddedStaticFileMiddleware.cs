using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace DbConfig.Ui;

/// <summary>
/// Serves files embedded in the <c>DbConfig.Ui</c> assembly under the <c>dist/</c> namespace.
/// Injects the API prefix meta tag into <c>index.html</c> at startup.
/// </summary>
internal sealed class EmbeddedStaticFileMiddleware
{
    private const string EmbeddedNamespace = "DbConfig.Ui.dist";
    private readonly EmbeddedFileProvider _fileProvider;
    private readonly FileExtensionContentTypeProvider _contentTypeProvider;
    private readonly string _indexHtml;

    internal EmbeddedStaticFileMiddleware(string apiPrefix)
    {
        _fileProvider = new EmbeddedFileProvider(
            typeof(EmbeddedStaticFileMiddleware).GetTypeInfo().Assembly,
            EmbeddedNamespace);

        _contentTypeProvider = BuildContentTypeProvider();
        _indexHtml = BuildIndexHtml(apiPrefix);
    }

    internal async Task ServeIndexAsync(HttpContext context)
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html;charset=utf-8";
        await context.Response.WriteAsync(_indexHtml, Encoding.UTF8, context.RequestAborted);
    }

    internal async Task ServeAssetAsync(HttpContext context, string relativePath)
    {
        var fileInfo = _fileProvider.GetFileInfo(relativePath);
        if (!fileInfo.Exists)
        {
            context.Response.StatusCode = 404;
            return;
        }

        if (!_contentTypeProvider.TryGetContentType(relativePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        context.Response.StatusCode = 200;
        context.Response.ContentType = contentType;

        await using var stream = fileInfo.CreateReadStream();
        await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private string BuildIndexHtml(string apiPrefix)
    {
        var fileInfo = _fileProvider.GetFileInfo("index.html");
        if (!fileInfo.Exists)
        {
            return string.Empty;
        }

        using var stream = fileInfo.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = reader.ReadToEnd();

        var metaTag = $"<meta name=\"db-config-api-prefix\" content=\"{apiPrefix}\" />";
        var headEndIndex = html.IndexOf("</head>", StringComparison.Ordinal);
        if (headEndIndex >= 0)
        {
            html = html.Insert(headEndIndex, metaTag);
        }

        return html;
    }

    private static FileExtensionContentTypeProvider BuildContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".js"] = "application/javascript";
        provider.Mappings[".css"] = "text/css";
        provider.Mappings[".html"] = "text/html";
        provider.Mappings[".woff"] = "font/woff";
        provider.Mappings[".woff2"] = "font/woff2";
        provider.Mappings[".svg"] = "image/svg+xml";
        return provider;
    }
}

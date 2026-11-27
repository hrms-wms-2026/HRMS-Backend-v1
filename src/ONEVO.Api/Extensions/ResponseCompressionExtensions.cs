using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace ONEVO.Api.Extensions;

/// <summary>
/// Improvements task 10 (DEC-069): the API compresses its own JSON, because neither UAT host
/// (App Service, IIS with ARR) compresses dynamic responses. Brotli first, then Gzip, at the
/// fastest level, which keeps most of the size win at a small CPU cost. Responses that carry a
/// secret in the body opt out with <see cref="Filters.NoResponseCompressionAttribute"/> (BREACH).
/// </summary>
public static class ResponseCompressionExtensions
{
    internal static readonly string[] CompressedMimeTypes = ["application/json", "application/problem+json"];

    public static IServiceCollection AddApiResponseCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = CompressedMimeTypes;
        });
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        return services;
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Diagnostics;

public static class DiagnosticServer
{
    public static WebApplication Build(string[] args, DiagnosticServerOptions? settings = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = settings ?? builder.Configuration.GetSection("Diagnostics").Get<DiagnosticServerOptions>() ?? new DiagnosticServerOptions();
        options.Validate();
        var adminToken = File.ReadAllText(options.AdminTokenFile).Trim();
        if (adminToken.Length < 32) throw new InvalidOperationException("관리자 토큰 파일에 충분한 길이의 무작위 비밀값이 필요합니다.");
        var adminHash = SHA256.HashData(Encoding.UTF8.GetBytes(adminToken));
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = DiagnosticArchive.MaximumArchiveBytes);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<DiagnosticStore>();
        builder.Services.AddHostedService<DiagnosticRetention>();
        builder.Services.Configure<ForwardedHeadersOptions>(forward =>
        {
            forward.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forward.ForwardLimit = 1;
            forward.KnownIPNetworks.Clear();
            forward.KnownProxies.Clear();
            foreach (var proxy in options.TrustedProxies)
            {
                var address = System.Net.IPAddress.Parse(proxy);
                forward.KnownProxies.Add(address);
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    forward.KnownProxies.Add(address.MapToIPv6());
            }
        });
        builder.Services.AddRateLimiter(limits =>
        {
            limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limits.AddFixedWindowLimiter("upload", limiter =>
            {
                limiter.PermitLimit = options.UploadsPerMinute;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
            limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Method == "POST" ? RateLimitPartition.GetConcurrencyLimiter("uploads", _ => new ConcurrencyLimiterOptions
                { PermitLimit = options.ConcurrentUploads, QueueLimit = 0 }) : RateLimitPartition.GetNoLimiter("read"));
        });
        var app = builder.Build();
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (context.Request.Path != "/health" && !context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (context.Request.Path.StartsWithSegments("/admin"))
            {
                var authorization = context.Request.Headers.Authorization.ToString();
                if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) ||
                    !CryptographicOperations.FixedTimeEquals(adminHash, SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]))))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }
            await next(context);
        });
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Text("ok"));
        app.MapPost("/api/v1/reports", Receive).RequireRateLimiting("upload");
        app.MapGet("/admin/reports", async (int? offset, DiagnosticStore store, CancellationToken cancellation) =>
            offset is < 0 ? Results.BadRequest() : Results.Ok(await store.ListAsync(offset ?? 0, cancellation)));
        app.MapGet("/admin/reports/{id}", async (string id, DiagnosticStore store, CancellationToken cancellation) =>
        {
            if (!Guid.TryParseExact(id, "N", out var reportId) || reportId == Guid.Empty) return Results.BadRequest();
            var bytes = await store.ReadAsync(reportId, cancellation);
            return bytes is null ? Results.NotFound() : Results.File(bytes, "application/zip", id + ".zip");
        });
        app.MapDelete("/admin/reports/{id}", async (string id, DiagnosticStore store, CancellationToken cancellation) =>
        {
            if (!Guid.TryParseExact(id, "N", out var reportId) || reportId == Guid.Empty) return Results.BadRequest();
            return await store.DeleteAsync(reportId, cancellation) ? Results.NoContent() : Results.NotFound();
        });
        return app;
    }

    private static async Task<IResult> Receive(HttpContext context, DiagnosticStore store, DiagnosticServerOptions options)
    {
        var request = context.Request;
        if (request.ContentType != "application/zip" ||
            !Guid.TryParseExact(request.Headers["X-SephPlanner-Report-Id"], "N", out var reportId) || reportId == Guid.Empty)
            return Results.BadRequest();
        var hash = request.Headers["X-SephPlanner-SHA256"].ToString();
        if (hash.Length != 64 || hash.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return Results.BadRequest();
        if (request.ContentLength > DiagnosticArchive.MaximumArchiveBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.UploadTimeoutSeconds));
        try
        {
            var saved = await store.SaveAsync(reportId, hash, request.Body, timeout.Token);
            return saved switch
            {
                DiagnosticSaveResult.Created => Results.Text(reportId.ToString("N"), "text/plain", statusCode: StatusCodes.Status201Created),
                DiagnosticSaveResult.Duplicate => Results.Text(reportId.ToString("N"), "text/plain"),
                DiagnosticSaveResult.Conflict => Results.Conflict(),
                DiagnosticSaveResult.Full => Results.StatusCode(StatusCodes.Status507InsufficientStorage),
                _ => throw new InvalidOperationException("처리하지 못한 진단 저장 결과입니다."),
            };
        }
        catch (InvalidDataException) { return Results.BadRequest(); }
        catch (JsonException) { return Results.BadRequest(); }
        catch (DecoderFallbackException) { return Results.BadRequest(); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { return Results.StatusCode(StatusCodes.Status408RequestTimeout); }
    }
}

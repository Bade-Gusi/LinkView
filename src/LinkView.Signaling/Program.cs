using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using LinkView.Protocol;
using LinkView.Signaling;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<SignalingOptions>()
    .Bind(builder.Configuration.GetSection(SignalingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<SignalingServer>();
builder.Services.AddHostedService<SessionCleanupService>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    KeepAliveTimeout = TimeSpan.FromSeconds(15)
});

var startedAt = Stopwatch.GetTimestamp();
var server = app.Services.GetRequiredService<SignalingServer>();
var options = app.Services.GetRequiredService<IOptions<SignalingOptions>>().Value;
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

app.MapGet("/", () => Results.Json(new
{
    name = "LinkView Signaling",
    protocol = ProtocolConstants.Version,
    note = "该服务只转发配对与连接协商消息，不转发或存储媒体内容。"
}));

app.MapGet("/healthz", () =>
{
    var uptime = Stopwatch.GetElapsedTime(startedAt);
    return Results.Json(new
    {
        status = "ok",
        uptimeSeconds = (int)uptime.TotalSeconds,
        connections = server.ConnectionCount,
        sessions = server.SessionCount
    });
});

app.Use(async (context, next) =>
{
    if (!context.Request.Path.Equals("/ws", StringComparison.OrdinalIgnoreCase))
    {
        await next(context);
        return;
    }

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket upgrade required.");
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var logger = loggerFactory.CreateLogger("LinkView.Signaling.Connection");
    var connection = new ClientConnection(socket, context.Connection.RemoteIpAddress, options, logger);

    if (!server.TryRegister(connection, out var reason))
    {
        await connection.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None);
        await connection.DisposeAsync();
        return;
    }

    var buffer = ArrayPool<byte>.Shared.Rent(8192);
    using var messageBuffer = new MemoryStream(8192);
    try
    {
        while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            messageBuffer.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }

                    return;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await connection.SendErrorAsync(SignalErrorCode.InvalidMessage, "仅支持文本 JSON 信令消息", context.RequestAborted);
                    await connection.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "binary messages unsupported", CancellationToken.None);
                    return;
                }

                messageBuffer.Write(buffer, 0, result.Count);
                if (messageBuffer.Length > ProtocolConstants.MaxMessageBytes)
                {
                    await connection.SendErrorAsync(SignalErrorCode.PayloadTooLarge, "消息超过大小限制", context.RequestAborted);
                    await connection.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too large", CancellationToken.None);
                    return;
                }
            } while (!result.EndOfMessage);

            if (messageBuffer.Length == 0)
            {
                continue;
            }

            if (!SignalCodec.TryParse(messageBuffer.GetBuffer().AsSpan(0, (int)messageBuffer.Length), out var envelope, out var parseError))
            {
                logger.LogDebug("signaling_parse_failed {ConnectionId} {Error}", connection.Id, parseError);
                await connection.SendErrorAsync(SignalErrorCode.InvalidMessage, "消息格式不合法", context.RequestAborted);
                continue;
            }

            await server.HandleMessageAsync(connection, envelope!, context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
        // 客户端或服务器正常取消。
    }
    catch (WebSocketException ex)
    {
        logger.LogDebug("signaling_socket_closed {ConnectionId} {Error}", connection.Id, ex.Message);
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
        await server.HandleDisconnectAsync(connection);
        await connection.DisposeAsync();
    }
});

app.Run();

public partial class Program;

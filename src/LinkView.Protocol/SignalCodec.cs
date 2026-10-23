using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinkView.Protocol;

/// <summary>
/// 信令 JSON 编解码。所有跨进程消息必须通过这里，避免各处使用不同的序列化设置。
/// </summary>
public static class SignalCodec
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static SignalEnvelope Create<T>(SignalMessageType type, T payload, string? sessionId = null)
    {
        var element = JsonSerializer.SerializeToElement(payload, JsonOptions);
        return new SignalEnvelope
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            SessionId = sessionId,
            Payload = element
        };
    }

    public static string Serialize(SignalEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, JsonOptions);

    public static byte[] SerializeToUtf8(SignalEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

    /// <summary>
    /// 反序列化信封。返回 false 表示消息格式非法；不会向调用方抛出 JSON 异常。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out SignalEnvelope? envelope, out string? error)
    {
        envelope = null;
        error = null;
        try
        {
            envelope = JsonSerializer.Deserialize<SignalEnvelope>(utf8, JsonOptions);
            if (envelope is null)
            {
                error = "empty envelope";
                return false;
            }

            if (envelope.V != ProtocolConstants.Version)
            {
                error = $"unsupported protocol version {envelope.V}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(envelope.Id) || envelope.Id.Length > 64)
            {
                error = "invalid message id";
                return false;
            }

            if (!Enum.IsDefined(envelope.Type))
            {
                error = "unknown message type";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static T? GetPayload<T>(SignalEnvelope envelope)
    {
        try
        {
            return envelope.Payload.ValueKind == JsonValueKind.Undefined
                ? default
                : envelope.Payload.Deserialize<T>(JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

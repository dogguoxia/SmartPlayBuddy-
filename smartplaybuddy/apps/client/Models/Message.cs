using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartPlayBuddy.Client.Models;

public class Message
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("binary")]
    public bool Binary { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static Message FromJson(string json)
    {
        var raw = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        var msg = new Message
        {
            Type = raw.GetProperty("type").GetString() ?? string.Empty,
            Action = raw.GetProperty("action").GetString() ?? string.Empty,
            From = raw.TryGetProperty("from", out var fromProp) ? fromProp.GetString() : null,
            To = raw.TryGetProperty("to", out var toProp) ? toProp.GetString() : null,
            RequestId = raw.TryGetProperty("requestId", out var reqProp) ? reqProp.GetString() : null,
            Timestamp = raw.TryGetProperty("timestamp", out var tsProp) && tsProp.ValueKind == JsonValueKind.Number
                ? tsProp.GetInt64()
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Binary = raw.TryGetProperty("binary", out var binProp) && binProp.ValueKind == JsonValueKind.True,
        };

        if (raw.TryGetProperty("data", out var dataProp) && dataProp.ValueKind != JsonValueKind.Null)
        {
            if (dataProp.ValueKind == JsonValueKind.String)
            {
                var bytes = Convert.FromBase64String(dataProp.GetString() ?? string.Empty);
                var decoded = Encoding.UTF8.GetString(bytes);
                try
                {
                    msg.Data = JsonSerializer.Deserialize<object>(decoded, JsonOptions);
                }
                catch
                {
                    msg.Data = decoded;
                }
            }
            else
            {
                msg.Data = dataProp;
            }
        }

        return msg;
    }

    public string ToJson()
    {
        var dict = new Dictionary<string, object?>
        {
            ["type"] = Type,
            ["action"] = Action,
            ["timestamp"] = Timestamp,
        };

        if (!string.IsNullOrEmpty(From)) dict["from"] = From;
        if (!string.IsNullOrEmpty(To)) dict["to"] = To;
        if (!string.IsNullOrEmpty(RequestId)) dict["requestId"] = RequestId;
        if (Binary) dict["binary"] = true;

        if (Data is not null)
        {
            var raw = Data is string s ? Encoding.UTF8.GetBytes(s) : JsonSerializer.SerializeToUtf8Bytes(Data, JsonOptions);
            dict["data"] = Convert.ToBase64String(raw);
        }

        return JsonSerializer.Serialize(dict, JsonOptions);
    }
}

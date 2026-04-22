using System.Text.Json;
using System.Text.Json.Nodes;
using VibeTrade.Backend.Data;

namespace VibeTrade.Backend.Features.Chat;

/// <summary>
/// Alinea <see cref="ChatMessageDto"/> con el shape de <c>Message</c> del market store (front).
/// </summary>
public static class ChatMarketMessageJsonMapper
{
    public static JsonObject ToMarketMessage(ChatMessageDto m, string viewerUserId)
    {
        var from = m.SenderUserId == viewerUserId ? "me" : "other";
        var at = m.CreatedAtUtc.ToUnixTimeMilliseconds();
        var read = from == "me" ? m.Status == ChatMessageStatus.Read : true;
        var statusStr = ChatStatusToApiString(m.Status);
        var p = m.Payload;

        if (p.ValueKind != JsonValueKind.Object)
            return TextFallback(m.Id, from, at, read, statusStr, "");

        if (!p.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return TextFallback(m.Id, from, at, read, statusStr, "");

        return typeEl.GetString() switch
        {
            "text" => MapText(m.Id, from, at, read, statusStr, p),
            "audio" => MapAudio(m.Id, from, at, read, p),
            "image" => MapImage(m.Id, from, at, read, p),
            "doc" => MapDoc(m.Id, from, at, read, p),
            "docs" => MapDocs(m.Id, from, at, read, p),
            "agreement" => MapAgreement(m.Id, from, at, read, statusStr, p),
            "system_text" => MapSystemText(m.Id, at, read, p),
            _ => TextFallback(
                m.Id,
                from,
                at,
                read,
                statusStr,
                p.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : ""),
        };
    }

    private static string ChatStatusToApiString(ChatMessageStatus s) => s switch
    {
        ChatMessageStatus.Pending => "pending",
        ChatMessageStatus.Sent => "sent",
        ChatMessageStatus.Delivered => "delivered",
        ChatMessageStatus.Read => "read",
        ChatMessageStatus.Error => "error",
        _ => "sent",
    };

    private static JsonObject MapText(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        JsonElement p)
    {
        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "text",
            ["text"] = p.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
            ["at"] = at,
            ["read"] = read,
            ["chatStatus"] = chatStatus,
        };
        if (p.TryGetProperty("offerQaId", out var oq) && oq.ValueKind == JsonValueKind.String)
        {
            var oqs = oq.GetString();
            if (!string.IsNullOrEmpty(oqs))
                obj["offerQaId"] = oqs;
        }
        AppendReplyQuotes(obj, p);
        return obj;
    }

    private static JsonObject MapAudio(string id, string from, long at, bool read, JsonElement p)
    {
        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "audio",
            ["url"] = p.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
            ["seconds"] = p.TryGetProperty("seconds", out var s) && s.TryGetInt32(out var sec) ? sec : 1,
            ["at"] = at,
            ["read"] = read,
        };
        AppendReplyQuotes(obj, p);
        return obj;
    }

    private static JsonObject MapImage(string id, string from, long at, bool read, JsonElement p)
    {
        var images = new JsonArray();
        if (p.TryGetProperty("images", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("url", out var u))
                    continue;
                images.Add(new JsonObject { ["url"] = u.GetString() ?? "" });
            }
        }
        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "image",
            ["images"] = images,
            ["at"] = at,
            ["read"] = read,
        };
        if (p.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String)
        {
            var cs = cap.GetString();
            if (!string.IsNullOrEmpty(cs))
                obj["caption"] = cs;
        }
        if (p.TryGetProperty("embeddedAudio", out var ea)
            && ea.ValueKind == JsonValueKind.Object
            && ea.TryGetProperty("url", out var eau)
            && ea.TryGetProperty("seconds", out var eas)
            && eas.TryGetInt32(out var easc))
        {
            obj["embeddedAudio"] = new JsonObject
            {
                ["url"] = eau.GetString() ?? "",
                ["seconds"] = easc,
            };
        }
        AppendReplyQuotes(obj, p);
        return obj;
    }

    private static JsonObject MapDoc(string id, string from, long at, bool read, JsonElement p)
    {
        var kind = p.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
            ? k.GetString() ?? "other"
            : "other";
        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "doc",
            ["name"] = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            ["size"] = p.TryGetProperty("size", out var sz) ? sz.GetString() ?? "" : "",
            ["kind"] = kind,
            ["at"] = at,
            ["read"] = read,
        };
        if (p.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
        {
            var us = u.GetString();
            if (!string.IsNullOrEmpty(us))
                obj["url"] = us;
        }
        if (p.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String)
        {
            var cs = cap.GetString();
            if (!string.IsNullOrEmpty(cs))
                obj["caption"] = cs;
        }
        AppendReplyQuotes(obj, p);
        return obj;
    }

    private static JsonObject MapDocs(string id, string from, long at, bool read, JsonElement p)
    {
        var docs = new JsonArray();
        if (p.TryGetProperty("documents", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;
                var kind = el.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                    ? k.GetString() ?? "other"
                    : "other";
                var one = new JsonObject
                {
                    ["name"] = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    ["size"] = el.TryGetProperty("size", out var sz) ? sz.GetString() ?? "" : "",
                    ["kind"] = kind,
                };
                if (el.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                {
                    var us = u.GetString();
                    if (!string.IsNullOrEmpty(us))
                        one["url"] = us;
                }
                docs.Add(one);
            }
        }
        var obj = new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "docs",
            ["documents"] = docs,
            ["at"] = at,
            ["read"] = read,
        };
        if (p.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String)
        {
            var cs = cap.GetString();
            if (!string.IsNullOrEmpty(cs))
                obj["caption"] = cs;
        }
        if (p.TryGetProperty("embeddedAudio", out var ea)
            && ea.ValueKind == JsonValueKind.Object
            && ea.TryGetProperty("url", out var eau)
            && ea.TryGetProperty("seconds", out var eas)
            && eas.TryGetInt32(out var easc))
        {
            obj["embeddedAudio"] = new JsonObject
            {
                ["url"] = eau.GetString() ?? "",
                ["seconds"] = easc,
            };
        }
        AppendReplyQuotes(obj, p);
        return obj;
    }

    private static void AppendReplyQuotes(JsonObject obj, JsonElement p)
    {
        if (!p.TryGetProperty("replyQuotes", out var rq) || rq.ValueKind != JsonValueKind.Array)
            return;
        var outArr = new JsonArray();
        foreach (var el in rq.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            var mid = el.TryGetProperty("messageId", out var mi) ? mi.GetString() : null;
            var author = el.TryGetProperty("author", out var au) ? au.GetString() : null;
            var preview = el.TryGetProperty("preview", out var pr) ? pr.GetString() : null;
            if (string.IsNullOrEmpty(mid) || author is null || preview is null)
                continue;
            outArr.Add(new JsonObject
            {
                ["id"] = mid,
                ["author"] = author,
                ["preview"] = preview,
            });
        }
        if (outArr.Count > 0)
            obj["replyQuotes"] = outArr;
    }

    private static JsonObject MapAgreement(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        JsonElement p)
    {
        var aid = p.TryGetProperty("agreementId", out var a) ? a.GetString() ?? "" : "";
        var title = p.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        return new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "agreement",
            ["agreementId"] = aid,
            ["title"] = title,
            ["at"] = at,
            ["read"] = read,
            ["chatStatus"] = chatStatus,
        };
    }

    private static JsonObject MapSystemText(string id, long at, bool read, JsonElement p)
    {
        var text = p.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return new JsonObject
        {
            ["id"] = id,
            ["from"] = "system",
            ["type"] = "text",
            ["text"] = text,
            ["at"] = at,
            ["read"] = read,
        };
    }

    private static JsonObject TextFallback(
        string id,
        string from,
        long at,
        bool read,
        string chatStatus,
        string text)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["from"] = from,
            ["type"] = "text",
            ["text"] = text,
            ["at"] = at,
            ["read"] = read,
            ["chatStatus"] = chatStatus,
        };
    }
}

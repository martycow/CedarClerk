using System.Text;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

public static class CedarToTelegramHtmlRenderer
{
    public static string Render(string cedarJson)
    {
        var root = JsonNode.Parse(cedarJson) ?? throw new ArgumentException("Invalid cedar JSON");
        var doc = root["doc"] ?? root;
        var sb = new StringBuilder();
        RenderNodes(doc["content"]?.AsArray(), sb);
        return sb.ToString();
    }

    private static void RenderNodes(JsonArray? nodes, StringBuilder sb)
    {
        if (nodes is null) return;
        foreach (var n in nodes) RenderNode(n!, sb);
    }

    private static void RenderNode(JsonNode node, StringBuilder sb)
    {
        switch ((string?)node["type"])
        {
            case "paragraph":
                sb.Append("<p>");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append("</p>");
                break;

            case "heading":
                var level = (int?)node["attrs"]?["level"] ?? 1;
                level = Math.Clamp(level, 1, 6);
                sb.Append($"<h{level}>");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append($"</h{level}>");
                break;

            case "bulletList":
                sb.Append("<ul>");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append("</ul>");
                break;

            case "orderedList":
                sb.Append("<ol>");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append("</ol>");
                break;

            case "listItem":
                sb.Append("<li>");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append("</li>");
                break;

            case "codeBlock":
                var lang = (string?)node["attrs"]?["language"];
                sb.Append(lang is null ? "<pre>" : $"<pre><code class=\"language-{lang}\">");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append(lang is null ? "</pre>" : "</code></pre>");
                break;

            case "blockquote":
                sb.Append("<blockquote>");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append("</blockquote>");
                break;

            case "horizontalRule":
                sb.Append("<hr>");
                break;

            case "hardBreak":
                sb.Append("<br>");
                break;

            case "text":
                RenderText(node, sb);
                break;

            default:
                RenderNodes(node["content"]?.AsArray(), sb);
                break;
        }
    }

    private static void RenderText(JsonNode node, StringBuilder sb)
    {
        var text = Escape((string?)node["text"] ?? "");
        var open = new StringBuilder();
        var close = new StringBuilder();

        if (node["marks"]?.AsArray() is { } marks)
        {
            foreach (var m in marks)
            {
                switch ((string?)m!["type"])
                {
                    case "bold":
                        open.Append("<b>");
                        close.Insert(0, "</b>");
                        break;
                    case "italic":
                        open.Append("<i>");
                        close.Insert(0, "</i>");
                        break;
                    case "underline":
                        open.Append("<u>");
                        close.Insert(0, "</u>");
                        break;
                    case "strike":
                        open.Append("<s>");
                        close.Insert(0, "</s>");
                        break;
                    case "code":
                        open.Append("<code>");
                        close.Insert(0, "</code>");
                        break;
                    case "link":
                        var href = Escape((string?)m["attrs"]?["href"] ?? "");
                        open.Append($"<a href=\"{href}\">");
                        close.Insert(0, "</a>");
                        break;
                }
            }
        }

        sb.Append(open).Append(text).Append(close);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
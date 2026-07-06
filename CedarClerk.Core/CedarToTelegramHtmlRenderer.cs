using System.Text;
using System.Text.Json.Nodes;

namespace CedarClerk.Core;

public static class CedarToTelegramHtmlRenderer
{
    public static string Render(string cedarJson)
    {
        var root = JsonNode.Parse(cedarJson) ?? throw new ArgumentException("Invalid JSON", nameof(cedarJson));
        var doc = root["doc"] ?? root;
        var sb = new StringBuilder();
        
        RenderNodes(doc["content"]?.AsArray(), sb);
        return sb.ToString().TrimEnd('\n');
    }
    
    private static void RenderNodes(JsonArray? nodes, StringBuilder sb)
    {
        if (nodes is null) 
            return;
        
        foreach (var n in nodes) 
            RenderNode(n!, sb);
    }

    private static void RenderNode(JsonNode node, StringBuilder sb)
    {
        var nodeType = (string?)node["type"];
        switch (nodeType)
        {
            case "paragraph":
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append("\n\n");
                break;

            case "heading":
                sb.Append("<b>");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append("</b>\n\n");
                break;

            case "codeBlock":
                var lang = (string?)node["attrs"]?["language"];
                sb.Append(lang is null ? "<pre>" : $"<pre><code class=\"language-{lang}\">");
                RenderNodes(node["content"]?.AsArray(), sb);
                sb.Append(lang is null ? "</pre>\n\n" : "</code></pre>\n\n");
                break;

            case "blockquote":
                var inner = new StringBuilder();
                RenderNodes(node["content"]?.AsArray(), inner);
                sb.Append("<blockquote>").Append(inner.ToString().TrimEnd('\n')).Append("</blockquote>\n\n");
                break;

            case "hardBreak":
                sb.Append('\n');
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
    
    private static string Escape(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
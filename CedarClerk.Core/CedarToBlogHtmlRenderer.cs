using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CedarClerk.Core;

// Renders a Cedar document (TipTap JSON) to a public-facing HTML fragment for the blog.
// Unlike CedarToTelegramHtmlRenderer, output goes straight to browsers, so attribute values
// are escaped for quotes too (not just text content).
public static class CedarToBlogHtmlRenderer
{
    private sealed class RenderContext
    {
        public required string MediaBaseUrl;
        public required string Lang;
        public List<string> Footnotes { get; } = [];
        // Pre-computed once per Render() call (see HeadingOutline) — see the comment on
        // CedarToTelegramBlocksRenderer.RenderContext for why this needs to happen up front.
        public IReadOnlyList<HeadingEntry> Outline { get; init; } = [];
        public int HeadingIndex;
    }

    // "en"/"ru" only, matching CedarClerk.Localization.Languages — Core stays free of a project
    // reference to Localization, so the caller (BlogEndpoints) passes the plain language code.
    public static string Render(string cedarJson, string mediaBaseUrl, string lang = "ru")
    {
        var root = JsonNode.Parse(cedarJson) ?? throw new ArgumentException("Invalid cedar JSON");
        var doc = root["doc"] ?? root;
        var sb = new StringBuilder();
        var ctx = new RenderContext { MediaBaseUrl = mediaBaseUrl, Lang = lang, Outline = HeadingOutline.Extract(doc) };
        RenderNodes(doc["content"]?.AsArray(), sb, ctx);
        AppendFootnotes(sb, ctx);
        return sb.ToString();
    }

    private static void RenderTableOfContents(StringBuilder sb, RenderContext ctx)
    {
        var entries = ctx.Outline.Where(h => h.Text.Length > 0).ToList();
        if (entries.Count == 0)
            return;

        var contentsLabel = ctx.Lang == "en" ? "Contents" : "Оглавление";
        sb.Append($"<nav class=\"toc\"><div class=\"toc-title\">{contentsLabel}</div><ul>");
        foreach (var h in entries)
            sb.Append($"<li class=\"toc-lvl-{h.Level}\"><a href=\"#{EscapeAttr(h.Slug)}\">")
              .Append(Escape(h.Text)).Append("</a></li>");
        sb.Append("</ul></nav>");
    }

    private static void AppendFootnotes(StringBuilder sb, RenderContext ctx)
    {
        if (ctx.Footnotes.Count == 0)
            return;

        sb.Append("<section class=\"footnotes\"><hr><ol>");
        for (var i = 0; i < ctx.Footnotes.Count; i++)
            sb.Append($"<li id=\"fn-{i + 1}\">").Append(Escape(ctx.Footnotes[i])).Append("</li>");
        sb.Append("</ol></section>");
    }

    private static void RenderNodes(JsonArray? nodes, StringBuilder sb, RenderContext ctx)
    {
        if (nodes is null)
            return;

        foreach (var n in nodes)
            RenderNode(n!, sb, ctx);
    }

    private static void RenderNode(JsonNode node, StringBuilder sb, RenderContext ctx)
    {
        switch ((string?)node["type"])
        {
            case "paragraph":
                sb.Append("<p>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</p>");
                break;

            case "heading":
                var level = Math.Clamp((int?)node["attrs"]?["level"] ?? 1, 1, 6);
                var headingSlug = ctx.Outline[ctx.HeadingIndex++].Slug;
                sb.Append($"<h{level} id=\"{EscapeAttr(headingSlug)}\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append($"</h{level}>");
                break;

            case "tableOfContents":
                RenderTableOfContents(sb, ctx);
                break;

            case "bulletList":
                sb.Append("<ul>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</ul>");
                break;

            case "orderedList":
                sb.Append("<ol>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</ol>");
                break;

            case "listItem":
                sb.Append("<li>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</li>");
                break;

            case "codeBlock":
                var lang = (string?)node["attrs"]?["language"];
                sb.Append(lang is null ? "<pre><code>" : $"<pre><code class=\"language-{EscapeAttr(lang)}\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</code></pre>");
                break;

            case "blockquote":
                sb.Append("<blockquote>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
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

            case "image":
                var src = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                AppendMedia(sb, "img", src, (string?)node["attrs"]?["caption"], isVoid: true);
                break;

            case "video":
                var videoSrc = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                // GIFs are stored as "video" nodes (Telegram sends them as an animation, not a
                // static photo), but no browser can decode a GIF inside a <video> tag — render
                // those as <img> instead so they actually play on the blog.
                if (IsGifSrc(videoSrc))
                    AppendMedia(sb, "img", videoSrc, (string?)node["attrs"]?["caption"], isVoid: true);
                else
                    AppendMedia(sb, "video", videoSrc, (string?)node["attrs"]?["caption"], isVoid: false);
                break;

            case "audio":
                var audioSrc = ResolveUrl((string?)node["attrs"]?["src"], ctx.MediaBaseUrl);
                // The clip name (I16) is primarily a Telegram concept, but a bare <audio> player
                // on the blog is just as anonymous — so it's shown here too when set.
                var audioName = ((string?)node["attrs"]?["title"])?.Trim();
                if (!string.IsNullOrEmpty(audioName))
                    sb.Append("<div class=\"audio-title\">").Append(Escape(audioName)).Append("</div>");
                AppendMedia(sb, "audio", audioSrc, (string?)node["attrs"]?["caption"], isVoid: false);
                break;

            case "youtube":
                var videoId = (string?)node["attrs"]?["videoId"];
                if (string.IsNullOrEmpty(videoId))
                    break;
                var embedSrc = $"https://www.youtube-nocookie.com/embed/{Uri.EscapeDataString(videoId)}";
                var embedHtml = $"<div class=\"youtube-embed\"><iframe src=\"{EscapeAttr(embedSrc)}\" loading=\"lazy\" allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share\" allowfullscreen></iframe></div>";
                var youtubeCaption = (string?)node["attrs"]?["caption"];
                if (string.IsNullOrEmpty(youtubeCaption))
                    sb.Append(embedHtml);
                else
                    sb.Append("<figure>").Append(embedHtml)
                      .Append("<figcaption>").Append(Escape(youtubeCaption)).Append("</figcaption></figure>");
                break;

            case "carousel":
                RenderCarousel(node["attrs"]?["images"]?.AsArray(), sb, ctx);
                break;

            case "collage":
                sb.Append("<div class=\"collage\">");
                if (node["attrs"]?["images"]?.AsArray() is { } collageImages)
                    foreach (var img in collageImages)
                        sb.Append($"<img loading=\"lazy\" src=\"{EscapeAttr(ResolveUrl((string?)img, ctx.MediaBaseUrl))}\">");
                sb.Append("</div>");
                break;

            case "table":
                sb.Append("<table>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</table>");
                break;

            case "tableRow":
                sb.Append("<tr>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</tr>");
                break;

            case "tableHeader":
            case "tableCell":
                var tag = (string?)node["type"] == "tableHeader" ? "th" : "td";
                var span = "";
                if ((int?)node["attrs"]?["colspan"] is { } colspan && colspan > 1)
                    span += $" colspan=\"{colspan}\"";
                if ((int?)node["attrs"]?["rowspan"] is { } rowspan && rowspan > 1)
                    span += $" rowspan=\"{rowspan}\"";
                sb.Append($"<{tag}{span}>");
                RenderCellContent(node["content"]?.AsArray(), sb, ctx);
                sb.Append($"</{tag}>");
                break;

            case "taskList":
                sb.Append("<ul class=\"task-list\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</ul>");
                break;

            case "taskItem":
                var isChecked = (bool?)node["attrs"]?["checked"] ?? false;
                sb.Append(isChecked ? "<li><input type=\"checkbox\" disabled checked> " : "<li><input type=\"checkbox\" disabled> ");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</li>");
                break;

            case "blockMath":
                sb.Append($"<div class=\"math-tex\" data-display=\"true\">{Escape((string?)node["attrs"]?["latex"] ?? "")}</div>");
                break;

            case "inlineMath":
                sb.Append($"<span class=\"math-tex\" data-display=\"false\">{Escape((string?)node["attrs"]?["latex"] ?? "")}</span>");
                break;

            case "toggle":
                var summary = Escape((string?)node["attrs"]?["summary"] ?? "Details");
                sb.Append($"<details><summary>{summary}</summary>");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append("</details>");
                break;

            case "datetime":
                var unix = (long?)node["attrs"]?["unix"] ?? 0;
                var format = (string?)node["attrs"]?["format"] ?? "wDT";
                var dt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
                sb.Append($"<time datetime=\"{dt:yyyy-MM-ddTHH:mm:ssZ}\">{Escape(FormatDateTime(dt, format))}</time>");
                break;

            case "footnote":
                ctx.Footnotes.Add((string?)node["attrs"]?["text"] ?? "");
                sb.Append($"<sup><a href=\"#fn-{ctx.Footnotes.Count}\">[{ctx.Footnotes.Count}]</a></sup>");
                break;

            // Intentionally not handled by CedarToTelegramHtmlRenderer/CedarToTelegramMarkdownRenderer —
            // both fall through to their own "unknown type" default (render children only), which is
            // exactly the desired behavior there: Telegram has no concept of anchored reactions/comments.
            case "annotation":
                var aid = (string?)node["attrs"]?["id"] ?? "";
                sb.Append($"<div class=\"annotation\" data-annotation-id=\"{EscapeAttr(aid)}\">");
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                sb.Append(AnnotationControlsHtml(ctx.Lang));
                sb.Append("</div>");
                break;

            default:
                RenderNodes(node["content"]?.AsArray(), sb, ctx);
                break;
        }
    }

    // Zero-count placeholder markup for the like/dislike/comment controls on an anchored region —
    // hydrated client-side (see BlogEndpoints' page script). Also reused as-is by BlogEndpoints for
    // the whole-article reaction/comment block, wrapped in the same ".annotation" div with an empty id.
    // Comments are shown by default (no expand/collapse) — client script paginates the list.
    //
    // `ownerName`/`publishedAt` are only passed by the whole-article call site (BlogEndpoints,
    // Phase 8 Step 7) — omitted (null) for every per-fragment inline annotation instance, since
    // repeating "post published: ..." inside every in-text comment popup would be noise, and the
    // owner-name data attribute only needs to exist once per page for the hydration script to read.
    public static string AnnotationControlsHtml(string lang = "ru", string? ownerName = null, DateTime? publishedAt = null)
    {
        var comments = lang == "en" ? "Comments" : "Комментарии";
        var showMore = lang == "en" ? "Show more comments" : "Показать больше комментариев";
        var namePlaceholder = lang == "en" ? "Name (optional)" : "Имя (необязательно)";
        var commentPlaceholder = lang == "en" ? "Add a comment…" : "Добавить комментарий…";
        var send = lang == "en" ? "Send" : "Отправить";
        var replyingTo = lang == "en" ? "Replying to" : "Ответ";
        var cancelReply = lang == "en" ? "Cancel" : "Отмена";

        var ownerAttr = string.IsNullOrEmpty(ownerName) ? "" : $" data-owner-name=\"{EscapeAttr(ownerName)}\"";
        var publishedLine = publishedAt is { } p
            ? $"<div class=\"comment-published-line\">{(lang == "en" ? "Post published" : "Пост опубликован")}: {p.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}</div>"
            : "";

        return $"""
            <div class="annotation-controls">
            <button type="button" class="react-btn" data-kind="like">&#128077; <span class="count" data-kind-count="like">0</span></button>
            <button type="button" class="react-btn" data-kind="dislike">&#128078; <span class="count" data-kind-count="dislike">0</span></button>
            <span class="comment-count-label">&#128172; <span class="comment-count">0</span></span>
            </div>
            <div class="comment-box"{ownerAttr}>
            <div class="comment-box-label">{comments}</div>
            {publishedLine}
            <div class="comment-list"></div>
            <button type="button" class="comment-load-more" hidden>{showMore}</button>
            <div class="comment-reply-indicator" hidden>{replyingTo} <span class="reply-target-name"></span> <button type="button" class="cancel-reply">{cancelReply}</button></div>
            <form class="comment-form">
            <input type="hidden" class="comment-parent-id" value="">
            <textarea class="comment-text" placeholder="{commentPlaceholder}" maxlength="2000" required></textarea>
            <div class="comment-form-row">
            <input type="text" class="comment-author" placeholder="{namePlaceholder}" maxlength="60">
            <button type="submit">{send}</button>
            </div>
            </form>
            </div>
            """;
    }

    // Registration form shown instead of the post body to an uninvited visitor of a private
    // post (B3). Hydrated by the page script in BlogEndpoints' ShellTemplate, same as the
    // comment form above. Every author-authored string (intro, question labels, choice options)
    // is escaped here — it's the one place visitor-facing HTML is built from owner input.
    public static string RegistrationFormHtml(RegistrationFormDefinition form, string title, string lang = "ru")
    {
        var heading = lang == "en" ? "This post is private" : "Это приватный пост";
        var blurb = lang == "en"
            ? "Fill in the form below to get access."
            : "Заполните форму ниже, чтобы получить доступ.";
        var submit = lang == "en" ? "Get access" : "Получить доступ";
        var namePh = lang == "en" ? "First and last name" : "Имя и фамилия";
        var nickPh = lang == "en" ? "Nickname" : "Никнейм";
        var emailPh = lang == "en" ? "Email" : "Почта";
        var socialPh = lang == "en" ? "A social link" : "Ссылка на соцсеть";
        var choosePh = lang == "en" ? "Choose…" : "Выберите…";

        var sb = new StringBuilder();
        sb.Append("<div class=\"reg-gate\"><div class=\"reg-card\">");
        sb.Append("<h1 class=\"reg-title\">").Append(Escape(title)).Append("</h1>");
        sb.Append("<div class=\"reg-lock\">&#128274; ").Append(heading).Append("</div>");
        sb.Append("<p class=\"reg-blurb\">").Append(blurb).Append("</p>");

        if (!string.IsNullOrWhiteSpace(form.Intro))
            sb.Append("<p class=\"reg-intro\">").Append(Escape(form.Intro!)).Append("</p>");

        sb.Append("<form class=\"reg-form\">");
        if (form.RequireName)
            sb.Append($"<input type=\"text\" class=\"reg-input\" data-field=\"name\" placeholder=\"{namePh}\" maxlength=\"200\" required>");
        if (form.RequireNickname)
            sb.Append($"<input type=\"text\" class=\"reg-input\" data-field=\"nickname\" placeholder=\"{nickPh}\" maxlength=\"200\" required>");
        if (form.RequireEmail)
            sb.Append($"<input type=\"email\" class=\"reg-input\" data-field=\"email\" placeholder=\"{emailPh}\" maxlength=\"200\" required>");
        if (form.RequireSocial)
            sb.Append($"<input type=\"text\" class=\"reg-input\" data-field=\"social\" placeholder=\"{socialPh}\" maxlength=\"200\" required>");

        foreach (var q in form.Questions)
        {
            var req = q.Required ? " required" : "";
            sb.Append("<label class=\"reg-question\"><span class=\"reg-question-label\">")
              .Append(Escape(q.Label)).Append(q.Required ? " *" : "").Append("</span>");

            if (q.Type == RegistrationQuestionType.Choice)
            {
                sb.Append($"<select class=\"reg-input\" data-question=\"{EscapeAttr(q.Id)}\"{req}>");
                sb.Append($"<option value=\"\">{choosePh}</option>");
                foreach (var o in q.Options)
                    sb.Append($"<option value=\"{EscapeAttr(o)}\">").Append(Escape(o)).Append("</option>");
                sb.Append("</select>");
            }
            else if (q.Type == RegistrationQuestionType.Multi)
            {
                // Checkboxes, not a multi-<select>: the latter needs ctrl-click to pick more than
                // one, which nobody discovers on a public page. Collected by the page script into
                // a JSON array (MultiAnswer, Core) — hence data-question-multi rather than
                // data-question, which the script reads one value at a time.
                sb.Append($"<span class=\"reg-multi\" data-question-multi=\"{EscapeAttr(q.Id)}\">");
                foreach (var o in q.Options)
                {
                    sb.Append("<label class=\"reg-multi-option\"><input type=\"checkbox\" value=\"")
                      .Append(EscapeAttr(o)).Append("\">").Append(Escape(o)).Append("</label>");
                }
                sb.Append("</span>");
            }
            else
            {
                sb.Append($"<input type=\"text\" class=\"reg-input\" data-question=\"{EscapeAttr(q.Id)}\" maxlength=\"200\"{req}>");
            }
            sb.Append("</label>");
        }

        sb.Append($"<button type=\"submit\" class=\"reg-submit\">{submit}</button>");
        sb.Append("<p class=\"reg-error\" hidden></p>");
        sb.Append("</form></div></div>");
        return sb.ToString();
    }

    private static string FormatDateTime(DateTime dt, string format)
    {
        var parts = new List<string>();
        if (format.Contains('w')) parts.Add(dt.ToString("ddd", CultureInfo.InvariantCulture));
        if (format.Contains('D')) parts.Add(dt.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
        if (format.Contains('T')) parts.Add(dt.ToString("HH:mm", CultureInfo.InvariantCulture));
        return parts.Count > 0 ? string.Join(' ', parts) : dt.ToString("g", CultureInfo.InvariantCulture);
    }

    private static bool IsGifSrc(string src) =>
        Regex.IsMatch(src, @"\.gif(?:[?#]|$)", RegexOptions.IgnoreCase);

    private static void AppendMedia(StringBuilder sb, string tag, string src, string? caption, bool isVoid)
    {
        var mediaHtml = isVoid
            ? $"<{tag} loading=\"lazy\" src=\"{EscapeAttr(src)}\">"
            : $"<{tag} controls src=\"{EscapeAttr(src)}\"></{tag}>";
        if (string.IsNullOrEmpty(caption))
        {
            sb.Append(mediaHtml);
        }
        else
        {
            sb.Append("<figure>").Append(mediaHtml)
              .Append("<figcaption>").Append(Escape(caption)).Append("</figcaption></figure>");
        }
    }

    // Interactive slideshow (prev/next + dots) — behavior wired up client-side by a small
    // script in the page shell (BlogEndpoints.PageShell) querying for the .carousel class.
    private static void RenderCarousel(JsonArray? images, StringBuilder sb, RenderContext ctx)
    {
        var urls = (images ?? []).Select(img => ResolveUrl((string?)img, ctx.MediaBaseUrl)).ToList();
        if (urls.Count == 0)
            return;

        sb.Append("<div class=\"carousel\">");
        sb.Append("<div class=\"carousel-viewport\">");
        foreach (var url in urls)
            sb.Append($"<img loading=\"lazy\" src=\"{EscapeAttr(url)}\">");
        sb.Append("</div>");

        if (urls.Count > 1)
        {
            sb.Append("<button type=\"button\" class=\"carousel-prev\" aria-label=\"Previous\">&#8249;</button>");
            sb.Append("<button type=\"button\" class=\"carousel-next\" aria-label=\"Next\">&#8250;</button>");
            sb.Append("<div class=\"carousel-dots\">");
            for (var i = 0; i < urls.Count; i++)
                sb.Append($"<button type=\"button\" class=\"carousel-dot\" aria-label=\"Slide {i + 1}\"></button>");
            sb.Append("</div>");
        }

        sb.Append("</div>");
    }

    private static string ResolveUrl(string? src, string mediaBaseUrl)
    {
        src ??= "";
        if (src.StartsWith('/'))
            src = mediaBaseUrl.TrimEnd('/') + src;
        return src;
    }

    private static void RenderCellContent(JsonArray? nodes, StringBuilder sb, RenderContext ctx)
    {
        if (nodes is null)
            return;

        foreach (var n in nodes)
        {
            if ((string?)n!["type"] == "paragraph")
                RenderNodes(n["content"]?.AsArray(), sb, ctx);
            else
                RenderNode(n, sb, ctx);
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
                        open.Append("<strong>");
                        close.Insert(0, "</strong>");
                        break;
                    case "italic":
                        open.Append("<em>");
                        close.Insert(0, "</em>");
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
                        var href = EscapeAttr((string?)m["attrs"]?["href"] ?? "");
                        open.Append($"<a href=\"{href}\" rel=\"noopener noreferrer\">");
                        close.Insert(0, "</a>");
                        break;
                    case "spoiler":
                        open.Append("<span class=\"spoiler\">");
                        close.Insert(0, "</span>");
                        break;
                }
            }
        }

        sb.Append(open).Append(text).Append(close);
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string EscapeAttr(string s) => Escape(s).Replace("\"", "&quot;");
}

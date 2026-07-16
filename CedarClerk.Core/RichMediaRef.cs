namespace CedarClerk.Core;

public enum RichMediaKind
{
    Photo,
    Video,
    Audio
}

/// <summary>
/// A media item referenced from rendered Rich Message text via a tg://{kind}?id={Id} link
/// (Bot API 10.2 — InputRichMessageMedia). Renderers never embed a direct media URL in the
/// text anymore; callers (e.g. PostEndpoints) attach these to InputRichMessage.Media.
/// No Caption field: verified 16.07.2026 against @testingandfun that InputMediaPhoto/Video/Audio.Caption
/// is silently ignored for media referenced this way (it only applies to a standalone Blocks photo/
/// video/audio block) — renderers emit the caption as plain flowing text right after the reference.
/// </summary>
public sealed record RichMediaRef(string Id, RichMediaKind Kind, string Url);

public sealed record RichRenderResult(string Text, IReadOnlyList<RichMediaRef> Media);

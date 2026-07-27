import { Node, mergeAttributes } from '@tiptap/core';

// Covers watch?v=, youtu.be/, shorts/, embed/, live/ — with or without extra query params
// (playlist, timestamp, ?si= share token) or a youtube-nocookie.com host.
const YOUTUBE_ID_PATTERN = /(?:youtube(?:-nocookie)?\.com\/(?:watch\?(?:.*&)?v=|shorts\/|embed\/|live\/)|youtu\.be\/)([\w-]{11})/;

export function extractYouTubeId(url: string): string | null {
    const match = url.trim().match(YOUTUBE_ID_PATTERN);
    return match ? match[1] : null;
}

export const YoutubeNode = Node.create({
    name: 'youtube',
    group: 'block',
    atom: true,
    draggable: true,

    addAttributes() {
        return {
            videoId: { default: null },
            caption: { default: null },
        };
    },

    parseHTML() {
        return [{ tag: 'div[data-youtube-id]' }];
    },

    renderHTML({ HTMLAttributes, node }) {
        return ['div', mergeAttributes(HTMLAttributes, { 'data-youtube-id': node.attrs['videoId'] })];
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            const wrapper = document.createElement('div');
            wrapper.className = 'media-with-caption youtube-node';

            const thumb = document.createElement('img');
            thumb.className = 'youtube-thumb';
            thumb.src = `https://img.youtube.com/vi/${node.attrs['videoId'] ?? ''}/hqdefault.jpg`;
            thumb.alt = 'YouTube video';
            wrapper.appendChild(thumb);

            const playOverlay = document.createElement('div');
            playOverlay.className = 'youtube-play-overlay';
            playOverlay.innerHTML = '<svg viewBox="0 0 68 48" width="44" height="31"><path d="M66.52 7.74c-.78-2.93-2.49-5.41-5.42-6.19C55.79.13 34 0 34 0S12.21.13 6.9 1.55c-2.93.78-4.63 3.26-5.42 6.19C.06 13.05 0 24 0 24s.06 10.95 1.48 16.26c.78 2.93 2.49 5.41 5.42 6.19C12.21 47.87 34 48 34 48s21.79-.13 27.1-1.55c2.93-.78 4.64-3.26 5.42-6.19C67.94 34.95 68 24 68 24s-.06-10.95-1.48-16.26z" fill="#f00"/><path d="M45 24 27 14v20" fill="#fff"/></svg>';
            wrapper.appendChild(playOverlay);

            const captionInput = document.createElement('input');
            captionInput.type = 'text';
            captionInput.className = 'media-caption-input';
            // B18: this doubles as the link text Telegram shows (the renderer falls back to
            // "Watch on YouTube" only when it's empty), so the placeholder says so rather than
            // adding a second field that would mean the same thing.
            captionInput.placeholder = 'Caption / Telegram link text…';
            captionInput.value = (node.attrs['caption'] as string) ?? '';
            captionInput.addEventListener('input', () => {
                if (typeof getPos !== 'function') return;
                const pos = getPos();
                if (pos === undefined) return;
                editor.view.dispatch(
                    editor.view.state.tr.setNodeMarkup(pos, undefined, { ...node.attrs, caption: captionInput.value })
                );
            });
            wrapper.appendChild(captionInput);

            return {
                dom: wrapper,
                update: updatedNode => {
                    if (updatedNode.type.name !== 'youtube') return false;
                    node = updatedNode;
                    thumb.src = `https://img.youtube.com/vi/${node.attrs['videoId'] ?? ''}/hqdefault.jpg`;
                    if (document.activeElement !== captionInput) {
                        captionInput.value = (node.attrs['caption'] as string) ?? '';
                    }
                    return true;
                },
                // See image-node.ts: without this, clicking/typing/backspacing in the caption
                // input gets intercepted as node selection/deletion instead of text editing.
                stopEvent: event => captionInput.contains(event.target as globalThis.Node),
            };
        };
    },
});

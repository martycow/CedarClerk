import { Node, mergeAttributes } from '@tiptap/core';

// GIFs are stored as "video" nodes (Telegram needs them sent as an animation, not a static
// photo), but a <video src="…gif"> never plays in the browser — no browser video decoder
// understands the GIF container. Preview those as a plain <img> instead, which autoplays GIFs
// natively; the node type/attrs stay "video" either way, so the Telegram export is unaffected.
function isGifSrc(src: string): boolean {
    return /\.gif(?:[?#]|$)/i.test(src);
}

export const VideoNode = Node.create({
    name: 'video',
    group: 'block',
    atom: true,
    draggable: true,

    addAttributes() {
        return {
            src: { default: null },
            caption: { default: null },
        };
    },

    parseHTML() {
        return [{ tag: 'video[src]' }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['video', mergeAttributes(HTMLAttributes, { controls: 'true' })];
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            const wrapper = document.createElement('div');
            wrapper.className = 'media-with-caption';

            const src = (node.attrs['src'] as string) ?? '';
            const gif = isGifSrc(src);
            const media: HTMLVideoElement | HTMLImageElement = gif
                ? document.createElement('img')
                : document.createElement('video');
            if (!gif) (media as HTMLVideoElement).controls = true;
            media.src = src;
            wrapper.appendChild(media);

            const captionInput = document.createElement('input');
            captionInput.type = 'text';
            captionInput.className = 'media-caption-input';
            captionInput.placeholder = 'Add a caption…';
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
                    if (updatedNode.type.name !== 'video') return false;
                    const newSrc = (updatedNode.attrs['src'] as string) ?? '';
                    // Media kind (img vs video) changed — bail out so Tiptap recreates the view.
                    if (isGifSrc(newSrc) !== gif) return false;
                    node = updatedNode;
                    media.src = newSrc;
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

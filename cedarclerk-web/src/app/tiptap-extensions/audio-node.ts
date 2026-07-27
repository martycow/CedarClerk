import { Node, mergeAttributes } from '@tiptap/core';

export const AudioNode = Node.create({
    name: 'audio',
    group: 'block',
    atom: true,
    draggable: true,

    addAttributes() {
        return {
            src: { default: null },
            caption: { default: null },
            // I16: the clip name Telegram shows. Empty falls back to the generated
            // asset_<guid>.mp3 filename, which is what it used to always show.
            title: { default: null },
        };
    },

    parseHTML() {
        return [{ tag: 'audio[src]' }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['audio', mergeAttributes(HTMLAttributes, { controls: 'true' })];
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            const wrapper = document.createElement('div');
            wrapper.className = 'media-with-caption';

            const audio = document.createElement('audio');
            audio.src = (node.attrs['src'] as string) ?? '';
            audio.controls = true;
            wrapper.appendChild(audio);

            // Sits above the caption: the title names the file in Telegram's player, the caption
            // is body text under it — two different things that both looked like "the label".
            const titleInput = document.createElement('input');
            titleInput.type = 'text';
            titleInput.className = 'media-caption-input media-title-input';
            titleInput.placeholder = 'Clip name (shown in Telegram)…';
            titleInput.value = (node.attrs['title'] as string) ?? '';
            titleInput.addEventListener('input', () => {
                if (typeof getPos !== 'function') return;
                const pos = getPos();
                if (pos === undefined) return;
                editor.view.dispatch(
                    editor.view.state.tr.setNodeMarkup(pos, undefined, { ...node.attrs, title: titleInput.value })
                );
            });
            wrapper.appendChild(titleInput);

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
                    if (updatedNode.type.name !== 'audio') return false;
                    node = updatedNode;
                    audio.src = (node.attrs['src'] as string) ?? '';
                    if (document.activeElement !== captionInput) {
                        captionInput.value = (node.attrs['caption'] as string) ?? '';
                    }
                    if (document.activeElement !== titleInput) {
                        titleInput.value = (node.attrs['title'] as string) ?? '';
                    }
                    return true;
                },
                // See image-node.ts: without this, clicking/typing/backspacing in either input
                // gets intercepted as node selection/deletion instead of text editing.
                stopEvent: event => captionInput.contains(event.target as globalThis.Node)
                    || titleInput.contains(event.target as globalThis.Node),
            };
        };
    },
});

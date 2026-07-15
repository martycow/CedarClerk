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
                    return true;
                },
                // See image-node.ts: without this, clicking/typing/backspacing in the caption
                // input gets intercepted as node selection/deletion instead of text editing.
                stopEvent: event => captionInput.contains(event.target as globalThis.Node),
            };
        };
    },
});

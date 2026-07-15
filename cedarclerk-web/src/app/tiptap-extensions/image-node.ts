import { Image } from '@tiptap/extension-image';

export const ImageNode = Image.extend({
    addAttributes() {
        return {
            ...this.parent?.(),
            caption: { default: null },
        };
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            const wrapper = document.createElement('div');
            wrapper.className = 'media-with-caption';

            const img = document.createElement('img');
            img.src = (node.attrs['src'] as string) ?? '';
            if (node.attrs['alt']) img.alt = node.attrs['alt'] as string;
            wrapper.appendChild(img);

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
                    if (updatedNode.type.name !== 'image') return false;
                    node = updatedNode;
                    img.src = (node.attrs['src'] as string) ?? '';
                    if (document.activeElement !== captionInput) {
                        captionInput.value = (node.attrs['caption'] as string) ?? '';
                    }
                    return true;
                },
                // Without this, ProseMirror treats clicks/keydowns inside the caption input as
                // events on the (atom) image node itself: clicking it turned into a NodeSelection
                // instead of placing a caret, and Backspace while editing the caption deleted the
                // whole image because the node — not the input's text — was "selected".
                stopEvent: event => captionInput.contains(event.target as Node),
            };
        };
    },
});

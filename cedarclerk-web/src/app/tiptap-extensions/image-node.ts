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
            };
        };
    },
});

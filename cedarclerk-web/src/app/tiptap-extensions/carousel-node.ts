import { Node, mergeAttributes } from '@tiptap/core';

export const CarouselNode = Node.create({
    name: 'carousel',
    group: 'block',
    atom: true,
    draggable: true,

    addAttributes() {
        return {
            images: {
                default: [] as string[],
                parseHTML: element =>
                    Array.from(element.querySelectorAll('img')).map(img => img.getAttribute('src') ?? ''),
                renderHTML: () => ({}),
            },
        };
    },

    parseHTML() {
        return [{ tag: 'div[data-type="carousel"]' }];
    },

    renderHTML({ node, HTMLAttributes }) {
        const images = (node.attrs['images'] as string[]) ?? [];
        return [
            'div',
            mergeAttributes(HTMLAttributes, { 'data-type': 'carousel', class: 'carousel-preview' }),
            ...images.map(src => ['img', { src }] as const),
        ];
    },

    addNodeView() {
        return ({ node, getPos, editor }) => {
            const container = document.createElement('div');
            container.className = 'carousel-editor';

            const render = () => {
                container.innerHTML = '';
                const images = (node.attrs['images'] as string[]) ?? [];
                images.forEach((src, index) => {
                    const item = document.createElement('div');
                    item.className = 'carousel-editor-item';

                    const img = document.createElement('img');
                    img.src = src;
                    item.appendChild(img);

                    const remove = document.createElement('button');
                    remove.textContent = '×';
                    remove.type = 'button';
                    remove.className = 'carousel-editor-remove';
                    remove.addEventListener('click', () => {
                        if (typeof getPos !== 'function') return;
                        const pos = getPos();
                        if (pos === undefined) return;
                        const next = images.filter((_, i) => i !== index);
                        editor.chain().focus().setNodeSelection(pos).updateAttributes('carousel', { images: next }).run();
                    });
                    item.appendChild(remove);

                    container.appendChild(item);
                });
            };

            render();

            return {
                dom: container,
                update: updatedNode => {
                    if (updatedNode.type.name !== 'carousel') return false;
                    node = updatedNode;
                    render();
                    return true;
                },
            };
        };
    },
});

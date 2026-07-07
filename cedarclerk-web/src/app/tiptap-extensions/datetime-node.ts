import { Node, mergeAttributes } from '@tiptap/core';

// Telegram live-renders the actual display client-side from unix+format at delivery time —
// this is only an editor-side preview, not what recipients will see.
function formatPreview(unix: number, format: string): string {
    const date = new Date(unix * 1000);
    const parts: string[] = [];
    if (format.includes('w')) parts.push(date.toLocaleDateString(undefined, { weekday: 'short' }));
    if (format.includes('D')) parts.push(date.toLocaleDateString());
    if (format.includes('T')) parts.push(date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }));
    return parts.join(' ') || date.toLocaleString();
}

export const DateTimeNode = Node.create({
    name: 'datetime',
    group: 'inline',
    inline: true,
    atom: true,

    addAttributes() {
        return {
            unix: { default: 0 },
            format: { default: 'wDT' },
        };
    },

    parseHTML() {
        return [{ tag: 'span[data-type="datetime"]' }];
    },

    renderHTML({ node, HTMLAttributes }) {
        const unix = (node.attrs['unix'] as number) ?? 0;
        const format = (node.attrs['format'] as string) ?? 'wDT';
        return ['span', mergeAttributes(HTMLAttributes, { 'data-type': 'datetime', class: 'datetime-pill' }), formatPreview(unix, format)];
    },

    addNodeView() {
        return ({ node }) => {
            const span = document.createElement('span');
            span.className = 'datetime-pill';

            const render = () => {
                const unix = (node.attrs['unix'] as number) ?? 0;
                const format = (node.attrs['format'] as string) ?? 'wDT';
                span.textContent = formatPreview(unix, format);
            };
            render();

            return {
                dom: span,
                update: updatedNode => {
                    if (updatedNode.type.name !== 'datetime') return false;
                    node = updatedNode;
                    render();
                    return true;
                },
            };
        };
    },
});

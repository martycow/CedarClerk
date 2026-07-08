import { Node, mergeAttributes } from '@tiptap/core';

function footnoteNumber(doc: any, pos: number): number {
    let count = 0;
    doc.nodesBetween(0, pos, (node: any) => {
        if (node.type.name === 'footnote') count++;
    });
    return count + 1;
}

export const FootnoteNode = Node.create({
    name: 'footnote',
    group: 'inline',
    inline: true,
    atom: true,

    addAttributes() {
        return {
            text: { default: '' },
        };
    },

    parseHTML() {
        return [{ tag: 'sup[data-type="footnote"]' }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['sup', mergeAttributes(HTMLAttributes, { 'data-type': 'footnote' }), '*'];
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            const sup = document.createElement('sup');
            sup.className = 'footnote-badge';

            const render = () => {
                const pos = typeof getPos === 'function' ? getPos() : undefined;
                const num = pos !== undefined ? footnoteNumber(editor.state.doc, pos) : 1;
                sup.textContent = `[${num}]`;
                sup.title = (node.attrs['text'] as string) ?? '';
            };
            render();

            return {
                dom: sup,
                update: updatedNode => {
                    if (updatedNode.type.name !== 'footnote') return false;
                    node = updatedNode;
                    render();
                    return true;
                },
            };
        };
    },
});

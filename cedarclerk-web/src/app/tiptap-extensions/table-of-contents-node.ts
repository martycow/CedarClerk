import { Node } from '@tiptap/core';

// Live preview only — the real, published TOC is auto-generated server-side from the document's
// headings at render/export time (see CedarClerk.Core.HeadingOutline), not authored here. This
// node view just mirrors that so editing feels WYSIWYG.
function collectHeadings(doc: any): { level: number; text: string }[] {
    const headings: { level: number; text: string }[] = [];
    doc.descendants((node: any) => {
        if (node.type.name === 'heading') {
            headings.push({ level: (node.attrs['level'] as number) ?? 1, text: node.textContent });
        }
    });
    return headings;
}

export const TableOfContentsNode = Node.create({
    name: 'tableOfContents',
    group: 'block',
    atom: true,

    parseHTML() {
        return [{ tag: 'div[data-type="toc"]' }];
    },

    renderHTML() {
        return ['div', { 'data-type': 'toc' }];
    },

    addNodeView() {
        return ({ editor }) => {
            const wrap = document.createElement('div');
            wrap.className = 'toc-block';

            const title = document.createElement('div');
            title.className = 'toc-block-title';
            title.textContent = 'Table of Contents';
            wrap.appendChild(title);

            const list = document.createElement('div');
            list.className = 'toc-block-list';
            wrap.appendChild(list);

            const render = () => {
                const headings = collectHeadings(editor.state.doc);
                list.innerHTML = '';
                if (headings.length === 0) {
                    const empty = document.createElement('div');
                    empty.className = 'toc-block-empty';
                    empty.textContent = 'No headings yet — add some and they’ll show up here.';
                    list.appendChild(empty);
                    return;
                }
                headings.forEach(h => {
                    const item = document.createElement('div');
                    item.className = 'toc-block-item toc-lvl-' + h.level;
                    item.textContent = h.text || '(untitled heading)';
                    list.appendChild(item);
                });
            };
            render();

            // A heading changing anywhere in the doc doesn't touch this node itself, so TipTap's
            // per-node `update` lifecycle won't re-fire — listen at the editor level instead.
            editor.on('transaction', render);

            return {
                dom: wrap,
                update: updatedNode => updatedNode.type.name === 'tableOfContents',
                destroy: () => editor.off('transaction', render),
                ignoreMutation: () => true,
            };
        };
    },
});

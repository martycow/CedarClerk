import { Node, mergeAttributes } from '@tiptap/core';

export const ToggleNode = Node.create({
    name: 'toggle',
    group: 'block',
    content: 'block+',
    defining: true,

    addAttributes() {
        return {
            summary: { default: 'Details' },
        };
    },

    parseHTML() {
        return [{ tag: 'details' }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['details', mergeAttributes(HTMLAttributes, { open: 'true' }), 0];
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            const details = document.createElement('details');
            details.open = true;
            details.className = 'toggle-block';

            const summaryEl = document.createElement('summary');
            const input = document.createElement('input');
            input.type = 'text';
            input.className = 'toggle-summary-input';
            input.value = (node.attrs['summary'] as string) ?? 'Details';
            input.addEventListener('input', () => {
                if (typeof getPos !== 'function') return;
                const pos = getPos();
                if (pos === undefined) return;
                editor.view.dispatch(
                    editor.view.state.tr.setNodeMarkup(pos, undefined, { ...node.attrs, summary: input.value })
                );
            });
            summaryEl.appendChild(input);
            details.appendChild(summaryEl);

            const content = document.createElement('div');
            content.className = 'toggle-content';
            details.appendChild(content);

            return {
                dom: details,
                contentDOM: content,
                update: updatedNode => {
                    if (updatedNode.type.name !== 'toggle') return false;
                    node = updatedNode;
                    if (document.activeElement !== input) {
                        input.value = (node.attrs['summary'] as string) ?? '';
                    }
                    return true;
                },
            };
        };
    },
});

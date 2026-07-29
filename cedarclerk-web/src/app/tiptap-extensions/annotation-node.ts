import { Node, mergeAttributes } from '@tiptap/core';
import { liftTarget } from '@tiptap/pm/transform';

// Wraps a selection of block content so the blog can attach anchored likes/comments to it.
// Telegram renderers ignore this node type entirely (fall through to rendering children only).
export const AnnotationNode = Node.create({
    name: 'annotation',
    group: 'block',
    content: 'block+',
    defining: true,

    addAttributes() {
        return {
            id: { default: null },
        };
    },

    parseHTML() {
        return [{ tag: 'div[data-type="annotation"]' }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'annotation', class: 'annotation-block' }), 0];
    },

    // Unwrap-only delete (Marty, 28.07.2026): the block had no interactive control at all — the
    // 💬 marker is decorative and pointer-events:none by design (I4). A corner × removes just the
    // wrapper, keeping its content in the document, since deleting the content along with the
    // wrapper was never the ask — same "resolve inside the node, lift its blockRange" idiom
    // ProseMirror itself uses for a generic unwrap, done directly here rather than through
    // TipTap's chain `lift` command so it works regardless of the current selection.
    addNodeView() {
        return ({ editor, getPos }) => {
            const dom = document.createElement('div');
            dom.className = 'annotation-block';
            dom.dataset['type'] = 'annotation';

            const removeBtn = document.createElement('button');
            removeBtn.className = 'annotation-remove';
            removeBtn.type = 'button';
            removeBtn.title = 'Remove reaction block (keeps the content)';
            removeBtn.textContent = '×';
            removeBtn.addEventListener('click', () => {
                if (typeof getPos !== 'function') return;
                const pos = getPos();
                if (pos === undefined) return;

                const { state, dispatch } = editor.view;
                const $inside = state.doc.resolve(pos + 1);
                const range = $inside.blockRange();
                const target = range ? liftTarget(range) : null;
                if (range && target != null) {
                    dispatch(state.tr.lift(range, target));
                }
            });
            dom.appendChild(removeBtn);

            const content = document.createElement('div');
            content.className = 'annotation-content';
            dom.appendChild(content);

            return { dom, contentDOM: content };
        };
    },
});

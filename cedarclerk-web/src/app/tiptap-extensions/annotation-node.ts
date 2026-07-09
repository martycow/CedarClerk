import { Node, mergeAttributes } from '@tiptap/core';

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
});

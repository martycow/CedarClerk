import { Mark, mergeAttributes } from '@tiptap/core';

export const SpoilerMark = Mark.create({
    name: 'spoiler',

    parseHTML() {
        return [{ tag: 'tg-spoiler' }, { tag: 'span.tg-spoiler' }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['tg-spoiler', mergeAttributes(HTMLAttributes), 0];
    },
});

import { Extension } from '@tiptap/core';

// Wave 2 item 17 — a blockquote can be marked expandable: Telegram renders it as a natively
// collapsible quote (Bot API 10.3, InputRichBlockExpandableBlockQuotation), the blog renders it
// as a plain blockquote. Added as a global attribute rather than a re-declared Blockquote node so
// StarterKit's own blockquote (and its input rules) stay untouched; absent/false means a plain
// quote, which is exactly how the renderers read it.
export const ExpandableBlockquote = Extension.create({
    name: 'expandableBlockquote',

    addGlobalAttributes() {
        return [{
            types: ['blockquote'],
            attributes: {
                expandable: {
                    default: false,
                    keepOnSplit: false,
                    parseHTML: (element: HTMLElement) => element.getAttribute('data-expandable') === 'true',
                    renderHTML: (attributes: Record<string, unknown>) =>
                        attributes['expandable'] ? { 'data-expandable': 'true' } : {},
                },
            },
        }];
    },
});

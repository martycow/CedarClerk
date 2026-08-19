import { Node, mergeAttributes } from '@tiptap/core';

// ADR-128 — an inline atom {draftId, label}: linked by id so renaming the target never breaks
// the link; label is the title as it was at insert time. The chip cannot navigate itself (a node
// view has no router), so a click dispatches this event and the editor page opens the target.
export const WIKILINK_OPEN_EVENT = 'cedar:wikilink-open';

export const WikiLinkNode = Node.create({
    name: 'wikilink',
    group: 'inline',
    inline: true,
    atom: true,

    addAttributes() {
        return {
            draftId: { default: '' },
            label: { default: '' },
        };
    },

    parseHTML() {
        return [{
            tag: 'span[data-type="wikilink"]',
            getAttrs: el => ({
                draftId: (el as HTMLElement).getAttribute('data-draft-id') ?? '',
                label: (el as HTMLElement).textContent ?? '',
            }),
        }];
    },

    renderHTML({ node, HTMLAttributes }) {
        return ['span',
            mergeAttributes(HTMLAttributes, { 'data-type': 'wikilink', 'data-draft-id': node.attrs['draftId'] as string }),
            (node.attrs['label'] as string) || '…'];
    },

    addNodeView() {
        return ({ node, editor }) => {
            const chip = document.createElement('span');
            chip.className = 'wikilink-chip';
            const render = () => {
                chip.textContent = (node.attrs['label'] as string) || '…';
            };
            render();
            chip.addEventListener('click', ev => {
                ev.preventDefault();
                const draftId = node.attrs['draftId'] as string;
                if (draftId) {
                    editor.view.dom.dispatchEvent(new CustomEvent(WIKILINK_OPEN_EVENT, { bubbles: true, detail: { draftId } }));
                }
            });
            return {
                dom: chip,
                update: updated => {
                    if (updated.type.name !== 'wikilink') return false;
                    node = updated;
                    render();
                    return true;
                },
            };
        };
    },
});

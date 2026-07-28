import { Node, mergeAttributes } from '@tiptap/core';

// NF5 — polls, blog-only per Marty's decision ("опросы только на сайте"): the Telegram renderers
// never get an explicit case for this node type (see docs/DECISIONS.md), so it simply never
// appears on that surface, the same way `annotation` never carries its reaction/comment chrome
// there. Question and options live in the node's own attrs rather than as child content — there's
// nothing here that's itself rich text, and a flat string array is the simplest thing that can
// grow or shrink as options are added/removed.
const MAX_OPTIONS = 10;
const MIN_OPTIONS = 2;

function randomPollId(): string {
    return 'poll-' + Math.random().toString(36).slice(2, 10);
}

export const PollNode = Node.create({
    name: 'poll',
    group: 'block',
    atom: true,
    selectable: true,

    addAttributes() {
        return {
            id: { default: null },
            question: { default: '' },
            options: { default: ['', ''] },
        };
    },

    parseHTML() {
        return [{ tag: 'div[data-type="poll"]' }];
    },

    renderHTML({ HTMLAttributes }) {
        return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'poll' })];
    },

    addNodeView() {
        return ({ node, editor, getPos }) => {
            const wrap = document.createElement('div');
            wrap.className = 'poll-editor';

            function patch(attrs: Record<string, unknown>) {
                if (typeof getPos !== 'function') return;
                const pos = getPos();
                if (pos === undefined) return;
                editor.view.dispatch(editor.view.state.tr.setNodeMarkup(pos, undefined, { ...node.attrs, ...attrs }));
            }

            // Assigned lazily on first mount rather than at insert time — keeps the insert command
            // itself a one-liner, same as every other attrs-only block (datetime, toggle's summary).
            if (!node.attrs['id']) patch({ id: randomPollId() });

            const qInput = document.createElement('input');
            qInput.type = 'text';
            qInput.className = 'poll-editor-question';
            qInput.placeholder = 'Question…';
            qInput.value = (node.attrs['question'] as string) ?? '';
            qInput.addEventListener('input', () => patch({ question: qInput.value }));
            wrap.appendChild(qInput);

            const optsWrap = document.createElement('div');
            optsWrap.className = 'poll-editor-options';
            wrap.appendChild(optsWrap);

            function renderOptions() {
                const options = (node.attrs['options'] as string[]) ?? [];
                optsWrap.innerHTML = '';
                options.forEach((opt, i) => {
                    const row = document.createElement('div');
                    row.className = 'poll-editor-option-row';

                    const input = document.createElement('input');
                    input.type = 'text';
                    input.value = opt;
                    input.placeholder = `Option ${i + 1}`;
                    input.addEventListener('input', () => {
                        const next = [...((node.attrs['options'] as string[]) ?? [])];
                        next[i] = input.value;
                        patch({ options: next });
                    });
                    row.appendChild(input);

                    if (options.length > MIN_OPTIONS) {
                        const removeBtn = document.createElement('button');
                        removeBtn.type = 'button';
                        removeBtn.className = 'poll-editor-remove';
                        removeBtn.textContent = '×';
                        removeBtn.addEventListener('click', () => {
                            patch({ options: options.filter((_, idx) => idx !== i) });
                        });
                        row.appendChild(removeBtn);
                    }
                    optsWrap.appendChild(row);
                });

                if (options.length < MAX_OPTIONS) {
                    const addBtn = document.createElement('button');
                    addBtn.type = 'button';
                    addBtn.className = 'poll-editor-add';
                    addBtn.textContent = '+ Option';
                    addBtn.addEventListener('click', () => patch({ options: [...options, ''] }));
                    optsWrap.appendChild(addBtn);
                }
            }
            renderOptions();

            return {
                dom: wrap,
                update: updatedNode => {
                    if (updatedNode.type.name !== 'poll') return false;
                    node = updatedNode;
                    if (document.activeElement !== qInput) qInput.value = (node.attrs['question'] as string) ?? '';
                    renderOptions();
                    return true;
                },
            };
        };
    },
});

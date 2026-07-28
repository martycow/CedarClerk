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

            function buildOptions(options: string[]) {
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
                        // Reads node.attrs live, not the `options` this closure was built with —
                        // buildOptions only re-runs on a count change, so by the time this is
                        // clicked, edits typed since the last rebuild would otherwise be silently
                        // discarded (the exact bug this line fixes: +Option/remove reverting text
                        // typed into the other fields back to whatever they were at last rebuild).
                        removeBtn.addEventListener('click', () => {
                            const current = (node.attrs['options'] as string[]) ?? [];
                            patch({ options: current.filter((_, idx) => idx !== i) });
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
                    addBtn.addEventListener('click', () => {
                        const current = (node.attrs['options'] as string[]) ?? [];
                        patch({ options: [...current, ''] });
                    });
                    optsWrap.appendChild(addBtn);
                }
            }

            // Called on every keystroke (via patch() -> node update), so a full rebuild here — as
            // opposed to the initial build above — would tear down and recreate the very <input>
            // the user is mid-word in, losing focus after the first character (the second bug this
            // node shipped with: the question field worked because it's a single persistent input
            // guarded by the activeElement check, but this list was unconditionally rebuilt every
            // time). Only rebuild when the option COUNT changed (add/remove); otherwise sync values
            // into the existing inputs in place, skipping whichever one currently has focus.
            function renderOptions() {
                const options = (node.attrs['options'] as string[]) ?? [];
                const rows = optsWrap.querySelectorAll<HTMLInputElement>('.poll-editor-option-row input');
                if (rows.length !== options.length) {
                    buildOptions(options);
                    return;
                }
                rows.forEach((input, i) => {
                    if (document.activeElement !== input) input.value = options[i] ?? '';
                });
            }
            buildOptions(node.attrs['options'] as string[] ?? []);

            return {
                dom: wrap,
                // Without this, clicking into one of this node's own <input>s first creates a
                // ProseMirror NodeSelection around the whole atom (this node has no contentDOM,
                // so PM doesn't know the inputs are foreign interactive elements) — the next
                // keystroke then replaces the entire poll instead of typing into the field. This
                // node has no editable ProseMirror content at all (question/options live in
                // attrs), so it's safe to tell PM to ignore every event and mutation inside it.
                stopEvent: () => true,
                ignoreMutation: () => true,
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

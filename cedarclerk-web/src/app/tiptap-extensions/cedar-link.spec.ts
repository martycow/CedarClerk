import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { CedarLink } from './cedar-link';

function editorWith(content: object): Editor {
    return new Editor({
        element: document.createElement('div'),
        extensions: [StarterKit.configure({ link: false }), CedarLink],
        content,
    });
}

const paragraph = (...nodes: object[]) => ({ type: 'doc', content: [{ type: 'paragraph', content: nodes }] });
const linked = (text: string) => ({ type: 'text', text, marks: [{ type: 'link', attrs: { href: 'https://example.com' } }] });

// #2 — text typed after a linked word must not stay linked.
describe('link mark', () => {
    it('is not inclusive, so typing after a link leaves the link', () => {
        const editor = editorWith(paragraph({ type: 'text', text: 'see ' }, linked('docs')));
        editor.commands.focus('end');
        editor.commands.insertContent(' and more');

        const runs: { text: string; link: boolean }[] = [];
        editor.state.doc.descendants(node => {
            if (node.isText) runs.push({ text: node.text!, link: node.marks.some(m => m.type.name === 'link') });
        });
        expect(runs).toEqual([
            { text: 'see ', link: false },
            { text: 'docs', link: true },
            { text: ' and more', link: false },
        ]);
        editor.destroy();
    });

    it('still links the selected text', () => {
        const editor = editorWith(paragraph({ type: 'text', text: 'hello' }));
        editor.commands.selectAll();
        editor.commands.setLink({ href: 'https://example.com' });
        expect(editor.getJSON().content![0].content![0].marks![0].type).toBe('link');
        editor.destroy();
    });
});

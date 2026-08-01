import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';

/**
 * T-100 — "Ctrl+B doesn't always fire".
 *
 * It fires exactly as often as the keyboard is in a Latin layout. ProseMirror matches shortcuts by
 * `event.key`, and on a Cyrillic layout Ctrl+B arrives as `Ctrl+и` — a combination nothing is bound
 * to. Nothing is broken and nothing is intermittent: the editor simply never saw the shortcut. For
 * someone who writes in Russian that is most of the time, which is what "не всегда" was describing.
 *
 * This binds the **physical key** (`event.code`, which is layout-independent) for the handful of
 * shortcuts that matter while typing. Latin layouts keep working through ProseMirror's own keymap
 * and never reach this plugin, because it only acts when `event.key` is not the expected letter.
 */

/** Physical key → what it does. `code` values are the US-layout names of the physical keys. */
const BINDINGS: Record<string, { latin: string; run: (chain: any) => unknown; shift?: boolean }> = {
    KeyB: { latin: 'b', run: c => c.toggleBold().run() },
    KeyI: { latin: 'i', run: c => c.toggleItalic().run() },
    KeyU: { latin: 'u', run: c => c.toggleUnderline().run() },
    KeyZ: { latin: 'z', run: c => c.undo().run() },
    KeyY: { latin: 'y', run: c => c.redo().run() },
};

export const LayoutShortcuts = Extension.create({
    name: 'layoutShortcuts',

    addProseMirrorPlugins() {
        const editor = this.editor;

        return [
            new Plugin({
                key: new PluginKey('layoutShortcuts'),
                props: {
                    handleKeyDown(_view, event) {
                        if (!(event.ctrlKey || event.metaKey) || event.altKey) return false;

                        const binding = BINDINGS[event.code];
                        if (!binding) return false;

                        // A Latin layout already produced the right `key`, so ProseMirror's own
                        // keymap has handled it — acting here too would toggle bold twice.
                        if (event.key.toLowerCase() === binding.latin) return false;

                        // Shift+Ctrl+Z is redo on every layout; without this it would undo.
                        if (event.code === 'KeyZ' && event.shiftKey) {
                            editor.chain().focus().redo().run();
                            event.preventDefault();
                            return true;
                        }
                        if (event.shiftKey) return false;

                        binding.run(editor.chain().focus());
                        event.preventDefault();
                        return true;
                    },
                },
            }),
        ];
    },
});

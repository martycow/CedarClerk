// The tool strip's catalogue: which buttons exist and in what order they are drawn. Nothing here
// is a preference any more — ADR-150 deleted the stored layout, its four presets and the per-button
// visibility list, and the strip decides its own shape by measurement (core/toolbar-fit.ts).

export type ToolbarButtonId =
    | 'bold' | 'italic' | 'underline' | 'strike' | 'spoiler' | 'align'
    | 'link' | 'emoji' | 'datetime' | 'footnote'
    | 'bulletList' | 'orderedList' | 'taskList' | 'indent' | 'outdent'
    | 'inlineCode' | 'codeBlock'
    | 'image' | 'video' | 'gif' | 'audio' | 'carousel' | 'collage' | 'library'
    | 'table' | 'formula' | 'blockquote' | 'toggle' | 'toc' | 'divider' | 'annotation' | 'poll';

export interface ToolbarGroupDef {
    id: string;
    label: string;
    buttons: { id: ToolbarButtonId; label: string }[];
}

// The block-type dropdown and undo/redo are deliberately absent: they are pinned to the head of
// the first row, outside the group system, so the fit can never wrap them away from the caret.
export const TOOLBAR_GROUPS: ToolbarGroupDef[] = [
    {
        id: 'text', label: 'Text', buttons: [
            { id: 'bold', label: 'Bold' }, { id: 'italic', label: 'Italic' }, { id: 'underline', label: 'Underline' },
            { id: 'strike', label: 'Strikethrough' }, { id: 'spoiler', label: 'Spoiler' },
            { id: 'inlineCode', label: 'Inline code' },
        ],
    },
    {
        id: 'insert', label: 'Insert', buttons: [
            { id: 'link', label: 'Link / YouTube / email / phone / mention' }, { id: 'emoji', label: 'Emoji' },
            { id: 'datetime', label: 'Date/time' }, { id: 'footnote', label: 'Footnote' },
        ],
    },
    {
        id: 'lists', label: 'Lists', buttons: [
            { id: 'bulletList', label: 'Bullet list' }, { id: 'orderedList', label: 'Numbered list' },
            { id: 'taskList', label: 'Task list' }, { id: 'indent', label: 'Indent' }, { id: 'outdent', label: 'Outdent' },
        ],
    },
    {
        id: 'media', label: 'Media', buttons: [
            { id: 'image', label: 'Image' }, { id: 'video', label: 'Video' }, { id: 'gif', label: 'GIF' },
            { id: 'audio', label: 'Audio' }, { id: 'carousel', label: 'Carousel' }, { id: 'collage', label: 'Collage' },
            { id: 'library', label: 'Media library' },
        ],
    },
    {
        id: 'blocks', label: 'Blocks', buttons: [
            { id: 'table', label: 'Table' }, { id: 'blockquote', label: 'Blockquote' }, { id: 'codeBlock', label: 'Code block' },
            { id: 'divider', label: 'Divider' },
        ],
    },
    // The commands a devlog reaches for least often sit behind one menu, so the strip's first
    // row is the twenty buttons a session actually uses. Nothing here is a second copy of a
    // dialog's or the context menu's command — each entry is the only trigger it has.
    {
        id: 'more', label: 'More', buttons: [
            { id: 'align', label: 'Text alignment (blog only)' }, { id: 'formula', label: 'Formula' },
            { id: 'toggle', label: 'Toggle block' }, { id: 'toc', label: 'Table of contents' },
            { id: 'annotation', label: 'Annotation' }, { id: 'poll', label: 'Poll' },
        ],
    },
];

/** The order the strip renders groups in. */
export const STRIP_GROUP_IDS: readonly string[] = TOOLBAR_GROUPS.map(g => g.id);

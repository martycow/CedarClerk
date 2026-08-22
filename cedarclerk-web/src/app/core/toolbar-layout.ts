// The tool strip's catalogue: which buttons exist and in what order they are drawn. Nothing here
// is a preference any more — ADR-150 deleted the stored layout, its four presets and the per-button
// visibility list, and the strip decides its own shape by measurement (core/toolbar-fit.ts).

export type ToolbarButtonId =
    | 'bold' | 'italic' | 'underline' | 'strike' | 'spoiler' | 'align'
    | 'link' | 'emoji' | 'datetime' | 'footnote'
    | 'bulletList' | 'orderedList' | 'taskList' | 'indent' | 'outdent'
    | 'inlineCode' | 'codeBlock'
    | 'image' | 'video' | 'gif' | 'audio' | 'carousel' | 'collage' | 'library'
    | 'table' | 'formula' | 'blockquote' | 'toggle' | 'toc' | 'divider' | 'annotation' | 'poll'
    | 'aiActions';

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
            { id: 'align', label: 'Text alignment (blog only)' },
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
    { id: 'code', label: 'Code', buttons: [{ id: 'inlineCode', label: 'Inline code' }, { id: 'codeBlock', label: 'Code block' }] },
    {
        id: 'media', label: 'Media', buttons: [
            { id: 'image', label: 'Image' }, { id: 'video', label: 'Video' }, { id: 'gif', label: 'GIF' },
            { id: 'audio', label: 'Audio' }, { id: 'carousel', label: 'Carousel' }, { id: 'collage', label: 'Collage' },
            { id: 'library', label: 'Media library' },
        ],
    },
    {
        id: 'blocks', label: 'Blocks', buttons: [
            { id: 'table', label: 'Table' }, { id: 'formula', label: 'Formula' }, { id: 'blockquote', label: 'Blockquote' },
            { id: 'toggle', label: 'Toggle block' }, { id: 'toc', label: 'Table of contents' },
            { id: 'divider', label: 'Divider' },
        ],
    },
    // Reader-engagement tools — split out from "Blocks" on Marty's request: these two are about
    // getting feedback from a reader (comment anchors, a vote), not authoring content, so they
    // don't belong grouped with tables/formulas/dividers.
    {
        id: 'feedback', label: 'Feedback', buttons: [
            { id: 'annotation', label: 'Annotation' }, { id: 'poll', label: 'Poll' },
        ],
    },
    { id: 'ai', label: 'AI', buttons: [{ id: 'aiActions', label: 'Fix errors / Schizo-izer' }] },
];

/**
 * The order the strip renders groups in. `ai` is not among them: it is pinned to the tail of the
 * first row beside the view controls and never wraps, so it is not a group the fit can move.
 */
export const STRIP_GROUP_IDS: readonly string[] = TOOLBAR_GROUPS.filter(g => g.id !== 'ai').map(g => g.id);

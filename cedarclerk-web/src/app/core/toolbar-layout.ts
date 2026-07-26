// Toolbar customization (ADR-035, Settings → Toolbar). Scoped down from the original mockup's
// per-button drag-and-drop: buttons are individually hide/show-able (checkbox catalog), but the
// row1/row2 zone assignment happens at the GROUP level — dragging 30+ individual buttons between
// two rows adds a lot of engineering surface for a personal-blog tool with one active user, while
// group-level placement still delivers the core "customizable toolbar" value.

export type ToolbarButtonId =
    | 'bold' | 'italic' | 'underline' | 'strike' | 'spoiler'
    | 'link' | 'emoji' | 'datetime' | 'footnote'
    | 'bulletList' | 'orderedList' | 'taskList' | 'indent' | 'outdent'
    | 'inlineCode' | 'codeBlock'
    | 'image' | 'video' | 'gif' | 'audio' | 'carousel' | 'collage' | 'youtube'
    | 'table' | 'formula' | 'blockquote' | 'toggle' | 'toc' | 'divider' | 'annotation'
    | 'aiActions';

export interface ToolbarGroupDef {
    id: string;
    label: string;
    buttons: { id: ToolbarButtonId; label: string }[];
}

// Order here is the Standard preset's row1-then-row2 reading order; Block-type dropdown and
// undo/redo are permanently pinned to row 1 (not part of any group, not hideable) since a
// document editor without them isn't meaningfully "minimal", it's broken.
export const TOOLBAR_GROUPS: ToolbarGroupDef[] = [
    {
        id: 'text', label: 'Text', buttons: [
            { id: 'bold', label: 'Bold' }, { id: 'italic', label: 'Italic' }, { id: 'underline', label: 'Underline' },
            { id: 'strike', label: 'Strikethrough' }, { id: 'spoiler', label: 'Spoiler' },
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
            { id: 'youtube', label: 'YouTube' },
        ],
    },
    {
        id: 'blocks', label: 'Blocks', buttons: [
            { id: 'table', label: 'Table' }, { id: 'formula', label: 'Formula' }, { id: 'blockquote', label: 'Blockquote' },
            { id: 'toggle', label: 'Toggle block' }, { id: 'toc', label: 'Table of contents' },
            { id: 'divider', label: 'Divider' }, { id: 'annotation', label: 'Annotation' },
        ],
    },
    { id: 'ai', label: 'AI', buttons: [{ id: 'aiActions', label: 'Fix errors / Schizo-izer' }] },
];

export const ALL_BUTTON_IDS: ToolbarButtonId[] = TOOLBAR_GROUPS.flatMap(g => g.buttons.map(b => b.id));

export type ToolbarPreset = 'minimal' | 'standard' | 'everything' | 'custom';

export interface ToolbarLayout {
    preset: ToolbarPreset;
    row2Groups: string[]; // group ids placed in row 2; anything else renders in row 1
    hiddenButtons: ToolbarButtonId[];
}

export const STANDARD_ROW2_GROUPS = ['code', 'media', 'blocks'];

const MINIMAL_HIDDEN: ToolbarButtonId[] = [
    'underline', 'strike', 'spoiler', 'emoji', 'datetime', 'footnote',
    'orderedList', 'taskList', 'indent', 'outdent', 'inlineCode', 'codeBlock',
    'video', 'gif', 'audio', 'carousel', 'collage', 'youtube',
    'table', 'formula', 'toggle', 'toc', 'divider', 'annotation',
];

export const DEFAULT_TOOLBAR_LAYOUT: ToolbarLayout = {
    preset: 'standard',
    row2Groups: STANDARD_ROW2_GROUPS,
    hiddenButtons: [],
};

export function presetLayout(preset: ToolbarPreset): ToolbarLayout {
    switch (preset) {
        case 'minimal':
            return { preset, row2Groups: [], hiddenButtons: MINIMAL_HIDDEN };
        case 'everything':
            return { preset, row2Groups: STANDARD_ROW2_GROUPS, hiddenButtons: [] };
        case 'standard':
            return { preset, row2Groups: STANDARD_ROW2_GROUPS, hiddenButtons: [] };
        case 'custom':
            return { preset: 'custom', row2Groups: STANDARD_ROW2_GROUPS, hiddenButtons: [] };
    }
}

export function parseToolbarLayout(json: string | null): ToolbarLayout {
    if (!json) return DEFAULT_TOOLBAR_LAYOUT;
    try {
        const parsed = JSON.parse(json);
        return {
            preset: parsed.preset ?? 'custom',
            row2Groups: Array.isArray(parsed.row2Groups) ? parsed.row2Groups : STANDARD_ROW2_GROUPS,
            hiddenButtons: Array.isArray(parsed.hiddenButtons) ? parsed.hiddenButtons : [],
        };
    } catch {
        return DEFAULT_TOOLBAR_LAYOUT;
    }
}

// Toolbar customization (ADR-035, Settings → Toolbar). Scoped down from the original mockup's
// per-button drag-and-drop: buttons are individually hide/show-able (checkbox catalog), but the
// row1/row2 zone assignment happens at the GROUP level — dragging 30+ individual buttons between
// two rows adds a lot of engineering surface for a personal-blog tool with one active user, while
// group-level placement still delivers the core "customizable toolbar" value.

export type ToolbarButtonId =
    | 'bold' | 'italic' | 'underline' | 'strike' | 'spoiler' | 'align'
    | 'link' | 'emoji' | 'datetime' | 'footnote'
    | 'bulletList' | 'orderedList' | 'taskList' | 'indent' | 'outdent'
    | 'inlineCode' | 'codeBlock'
    | 'image' | 'video' | 'gif' | 'audio' | 'carousel' | 'collage' | 'youtube'
    | 'table' | 'formula' | 'blockquote' | 'toggle' | 'toc' | 'divider' | 'annotation' | 'poll'
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
            { id: 'youtube', label: 'YouTube' },
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

export const ALL_BUTTON_IDS: ToolbarButtonId[] = TOOLBAR_GROUPS.flatMap(g => g.buttons.map(b => b.id));

export type ToolbarPreset = 'minimal' | 'standard' | 'everything' | 'custom';

export interface ToolbarLayout {
    preset: ToolbarPreset;
    // Both rows are ORDERED lists, not just membership: reordering within a row used to be
    // draggable in the UI but had nowhere to be stored and nothing reading it, so it silently
    // did nothing. row1Groups is optional in stored JSON — see parseToolbarLayout.
    row1Groups?: string[];
    row2Groups: string[];
    hiddenButtons: ToolbarButtonId[];
}

export const STANDARD_ROW2_GROUPS = ['code', 'media', 'blocks', 'feedback'];
export const STANDARD_ROW1_GROUPS = ['text', 'insert', 'lists'];

// 'ai' is pinned to row 1 outside the group system (never movable), so it is not orderable.
export const MOVABLE_GROUP_IDS = TOOLBAR_GROUPS.filter(g => g.id !== 'ai').map(g => g.id);

const MINIMAL_HIDDEN: ToolbarButtonId[] = [
    'underline', 'strike', 'spoiler', 'align', 'emoji', 'datetime', 'footnote',
    'orderedList', 'taskList', 'indent', 'outdent', 'inlineCode', 'codeBlock',
    'video', 'gif', 'audio', 'carousel', 'collage', 'youtube',
    'table', 'formula', 'toggle', 'toc', 'divider', 'annotation', 'poll',
];

export const DEFAULT_TOOLBAR_LAYOUT: ToolbarLayout = {
    preset: 'standard',
    row1Groups: STANDARD_ROW1_GROUPS,
    row2Groups: STANDARD_ROW2_GROUPS,
    hiddenButtons: [],
};

export function presetLayout(preset: ToolbarPreset): ToolbarLayout {
    switch (preset) {
        case 'minimal':
            return { preset, row1Groups: MOVABLE_GROUP_IDS, row2Groups: [], hiddenButtons: MINIMAL_HIDDEN };
        case 'everything':
        case 'standard':
        case 'custom':
            return {
                preset: preset === 'custom' ? 'custom' : preset,
                row1Groups: STANDARD_ROW1_GROUPS,
                row2Groups: STANDARD_ROW2_GROUPS,
                hiddenButtons: [],
            };
    }
}

// Normalizes any stored blob into two ordered, disjoint, complete lists. Stored layouts predate
// row1Groups, so it is derived when absent; and a group added to TOOLBAR_GROUPS after a layout was
// saved would otherwise vanish from the toolbar entirely, so anything unaccounted for lands in
// row 1 in canonical order.
export function normalizeRows(row1: unknown, row2: unknown): { row1Groups: string[]; row2Groups: string[] } {
    const known = (ids: unknown): string[] =>
        Array.isArray(ids) ? ids.filter((id): id is string => MOVABLE_GROUP_IDS.includes(id as string)) : [];

    const r2 = [...new Set(known(row2))];
    const r1 = [...new Set(known(row1))].filter(id => !r2.includes(id));
    const missing = MOVABLE_GROUP_IDS.filter(id => !r1.includes(id) && !r2.includes(id));
    return { row1Groups: [...r1, ...missing], row2Groups: r2 };
}

export function parseToolbarLayout(json: string | null): ToolbarLayout {
    if (!json) return DEFAULT_TOOLBAR_LAYOUT;
    try {
        const parsed = JSON.parse(json);
        const rows = normalizeRows(parsed.row1Groups, parsed.row2Groups ?? STANDARD_ROW2_GROUPS);
        return {
            preset: parsed.preset ?? 'custom',
            ...rows,
            hiddenButtons: Array.isArray(parsed.hiddenButtons) ? parsed.hiddenButtons : [],
        };
    } catch {
        return DEFAULT_TOOLBAR_LAYOUT;
    }
}

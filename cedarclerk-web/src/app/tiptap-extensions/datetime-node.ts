import { Node, mergeAttributes } from '@tiptap/core';
import { displayTimeZone, formatInZone, zoneAbbreviation } from '../core/display-time';

// Telegram live-renders the actual display client-side from unix+format at delivery time —
// this is only an editor-side preview, not what recipients will see. The blog, which does render
// it, prints the display zone (ADR-115), so the preview follows that rather than the machine's.
function formatPreview(unix: number, format: string): string {
    const date = new Date(unix * 1000);
    const parts: string[] = [];
    if (format.includes('w')) parts.push(WEEKDAYS_SHORT[weekdayInZone(date)]);
    if (format.includes('D')) parts.push(formatInZone(date, 'd MMM yyyy'));
    if (format.includes('T')) parts.push(`${formatInZone(date, 'HH:mm')} ${zoneAbbreviation(date)}`);
    return parts.join(' ') || formatInZone(date);
}

const WEEKDAYS_SHORT = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

function weekdayInZone(date: Date): number {
    const named = new Intl.DateTimeFormat('en-US', { timeZone: displayTimeZone(), weekday: 'short' }).format(date);
    const index = WEEKDAYS_SHORT.indexOf(named);
    return index < 0 ? date.getDay() : index;
}

export const DateTimeNode = Node.create({
    name: 'datetime',
    group: 'inline',
    inline: true,
    atom: true,

    addAttributes() {
        return {
            unix: { default: 0 },
            format: { default: 'wDT' },
        };
    },

    parseHTML() {
        return [{ tag: 'span[data-type="datetime"]' }];
    },

    renderHTML({ node, HTMLAttributes }) {
        const unix = (node.attrs['unix'] as number) ?? 0;
        const format = (node.attrs['format'] as string) ?? 'wDT';
        return ['span', mergeAttributes(HTMLAttributes, { 'data-type': 'datetime', class: 'datetime-pill' }), formatPreview(unix, format)];
    },

    addNodeView() {
        return ({ node }) => {
            const span = document.createElement('span');
            span.className = 'datetime-pill';

            const render = () => {
                const unix = (node.attrs['unix'] as number) ?? 0;
                const format = (node.attrs['format'] as string) ?? 'wDT';
                span.textContent = formatPreview(unix, format);
            };
            render();

            return {
                dom: span,
                update: updatedNode => {
                    if (updatedNode.type.name !== 'datetime') return false;
                    node = updatedNode;
                    render();
                    return true;
                },
            };
        };
    },
});

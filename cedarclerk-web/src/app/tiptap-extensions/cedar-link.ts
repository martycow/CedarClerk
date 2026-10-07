import Link from '@tiptap/extension-link';

/**
 * #2 — "the link kept going as I typed".
 *
 * TipTap's Link mark is `inclusive` whenever autolink is on (its default), so text typed at the
 * right edge of a link joins the link. A writer who links the last word of a sentence and keeps
 * typing expects the link to end where they stopped linking.
 *
 * `inclusive` is a schema method, not an option, which is why this is an `extend` rather than a
 * `configure`. Autolink stays on (decided 07.10.2026): typing at the very end of an auto-linked
 * URL now leaves the new characters outside the link, which is the rarer case and the right one.
 */
export const CedarLink = Link.extend({
    inclusive() {
        return false;
    },
});

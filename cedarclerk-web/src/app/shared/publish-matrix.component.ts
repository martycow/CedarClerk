import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DOCUMENT_KINDS, DocumentKind, DocumentKindCounts } from '../core/document-kinds';
import { Dict } from '@localization/en';
import { LocaleService } from '../core/i18n/locale.service';
import { PublishCapabilities } from '../core/publish.service';
import { BrandIconComponent, BrandIconName } from './brand-icon.component';
import { IconComponent } from './icon.component';

export type MatrixVerdict = 'yes' | 'partial' | 'no';

export interface MatrixCell {
    verdict: MatrixVerdict;
    /** The short reason under a partial or a no — «up to 4», «as text». Empty for a plain yes. */
    note: string;
}

export interface MatrixColumn {
    id: string;
    name: string;
    brand: BrandIconName | null;
    /** Absent while the network has no connected account: the column is drawn muted. */
    connected: boolean;
}

/** The blog takes the whole document: every kind, no cap. */
const BLOG: PublishCapabilities = {
    network: 'blog', maxCharacters: null, maxMediaItems: Number.MAX_SAFE_INTEGER, maxImageBytes: null,
    supportsVideo: true, supportsAudio: true, supportsRichText: true, supportsHeadings: true, supportsLists: true,
    supportsTables: true, supportsCodeBlocks: true, supportsMath: true, supportsLinkPreview: true, supportsAltText: true,
    supportsThreads: false, postsHavePublicUrls: true,
};

// What goes where: one row per kind of content, one column per destination, each cell the
// network's own answer read off its capabilities — the same record the publish pre-flight reads,
// so the matrix cannot promise what the target drops. Rows the document actually holds are lit.
@Component({
    selector: 'app-publish-matrix',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent, BrandIconComponent],
    host: { 'data-surface': 'paper' },
    template: `
        <div class="pm-scroll">
            <table class="pm" [attr.aria-label]="t().matrix.title">
                <thead>
                    <tr>
                        <th scope="col" class="pm-kind-head">{{ t().matrix.content }}</th>
                        @for (col of columns(); track col.id) {
                            <th scope="col" class="pm-col" [class.is-off]="!col.connected">
                                @if (col.brand) { <app-brand-icon [name]="col.brand" [size]="13" /> }
                                @else { <app-icon name="globe" size="xs" /> }
                                <span>{{ col.name }}</span>
                            </th>
                        }
                    </tr>
                </thead>
                <tbody>
                    @for (kind of kinds; track kind) {
                        <tr [class.is-present]="present()[kind] > 0">
                            <th scope="row" class="pm-kind">
                                <span>{{ t().matrix.kinds[kind] }}</span>
                                @if (present()[kind] > 0) { <span class="pm-count">{{ present()[kind] }}</span> }
                            </th>
                            @for (col of columns(); track col.id) {
                                @let cell = cellOf(kind, col.id);
                                <td class="pm-cell" [attr.data-verdict]="cell.verdict" [class.is-off]="!col.connected"
                                    [attr.title]="cell.note || null">
                                    <span class="pm-mark" aria-hidden="true">
                                        @switch (cell.verdict) {
                                            @case ('yes') { <app-icon name="check" size="xs" /> }
                                            @case ('partial') { <span class="pm-half"></span> }
                                            @default { <app-icon name="x" size="xs" /> }
                                        }
                                    </span>
                                    <span class="visually-hidden">{{ t().matrix.verdict[cell.verdict] }}</span>
                                    @if (cell.note) { <span class="pm-note">{{ cell.note }}</span> }
                                </td>
                            }
                        </tr>
                    }
                </tbody>
            </table>
        </div>
        <p class="pm-legend">
            <span><i class="pm-key" data-verdict="yes"></i>{{ t().matrix.verdict.yes }}</span>
            <span><i class="pm-key" data-verdict="partial"></i>{{ t().matrix.verdict.partial }}</span>
            <span><i class="pm-key" data-verdict="no"></i>{{ t().matrix.verdict.no }}</span>
            <span><i class="pm-key is-present"></i>{{ t().matrix.inDocument }}</span>
        </p>
    `,
    styles: [`
        :host { display: block; font-family: var(--font-sans); color: var(--text); }

        .pm-scroll { overflow-x: auto; }

        .pm {
            width: 100%;
            border-collapse: collapse;
            font-size: var(--fs-13);
        }

        .pm th, .pm td {
            padding: 6px 8px;
            border-bottom: 1px solid var(--paper-edge);
            text-align: left;
            vertical-align: middle;
            white-space: nowrap;
        }

        .pm thead th { font-size: var(--fs-11); font-weight: 700; letter-spacing: .06em; text-transform: uppercase; color: var(--t3); }
        .pm-col { text-align: center; }
        .pm-col span, .pm-col app-icon, .pm-col app-brand-icon { vertical-align: middle; }
        .pm-col app-brand-icon, .pm-col app-icon { margin-right: 4px; }
        .pm-col.is-off, .pm-cell.is-off { opacity: .45; }

        .pm-kind { font-weight: 600; color: var(--t2); }
        tr.is-present .pm-kind { color: var(--text); }
        tr.is-present .pm-kind::before {
            content: '';
            display: inline-block;
            width: 6px;
            height: 6px;
            margin-right: 8px;
            border-radius: 50%;
            background: var(--accent);
            vertical-align: middle;
        }

        .pm-count { margin-left: 6px; font-size: var(--fs-11); font-weight: 600; color: var(--t3); font-variant-numeric: tabular-nums; }

        .pm-cell { text-align: center; }

        .pm-mark {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 20px;
            height: 20px;
            border-radius: 50%;
            vertical-align: middle;
        }

        .pm-cell[data-verdict="yes"] .pm-mark { background: var(--ok-soft); color: var(--ok); }
        .pm-cell[data-verdict="partial"] .pm-mark { background: var(--warn-soft); color: var(--warn); }
        .pm-cell[data-verdict="no"] .pm-mark { background: var(--alt); color: var(--t3); }

        .pm-half { width: 8px; height: 8px; border-radius: 50%; background: linear-gradient(90deg, currentColor 50%, transparent 50%); border: 1.5px solid currentColor; box-sizing: border-box; }

        .pm-note { display: block; margin-top: 2px; font-size: var(--fs-11); color: var(--t3); }

        .pm-legend { display: flex; flex-wrap: wrap; gap: var(--space-4); margin: var(--space-3) 0 0; font-size: var(--fs-12); color: var(--t3); }
        .pm-legend span { display: inline-flex; align-items: center; gap: 6px; }
        .pm-key { display: inline-block; width: 10px; height: 10px; border-radius: 50%; }
        .pm-key[data-verdict="yes"] { background: var(--ok); }
        .pm-key[data-verdict="partial"] { background: var(--warn); }
        .pm-key[data-verdict="no"] { background: var(--t3); }
        .pm-key.is-present { background: var(--accent); }

        .visually-hidden { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; border: 0; overflow: hidden; white-space: nowrap; clip-path: inset(50%); }
    `],
})
export class PublishMatrixComponent {
    /** The networks as `/api/publish/networks` answers them, connected or not. */
    readonly capabilities = input<readonly PublishCapabilities[]>([]);
    readonly connected = input<readonly string[]>([]);
    /** What the open document holds; rows above zero are lit. */
    readonly present = input<DocumentKindCounts>(zeroCounts());
    /** Whether the blog column is drawn first. */
    readonly withBlog = input(true);

    protected readonly t = inject(LocaleService).t;
    protected readonly kinds = DOCUMENT_KINDS;

    private readonly order = ['telegram', 'x', 'bluesky', 'discord'];

    protected readonly columns = computed<MatrixColumn[]>(() => {
        const words = this.t().matrix;
        const cols: MatrixColumn[] = this.withBlog() ? [{ id: 'blog', name: words.blog, brand: null, connected: true }] : [];
        const sorted = [...this.capabilities()].sort((a, b) => this.order.indexOf(a.network) - this.order.indexOf(b.network));
        for (const cap of sorted) {
            cols.push({ id: cap.network, name: NAMES[cap.network] ?? cap.network, brand: BRANDS[cap.network] ?? null, connected: this.connected().includes(cap.network) });
        }
        return cols;
    });

    cellOf(kind: DocumentKind, network: string): MatrixCell {
        const cap = network === 'blog' ? BLOG : this.capabilities().find(c => c.network === network);
        return matrixCell(kind, cap, this.t().matrix.notes);
    }
}

export type MatrixWords = Dict['matrix']['notes'];

/** The network's own answer for one kind of content — the same record the pre-flight reads. */
export function matrixCell(kind: DocumentKind, cap: PublishCapabilities | undefined, words: MatrixWords): MatrixCell {
    if (!cap) return { verdict: 'no', note: '' };
    const yes: MatrixCell = { verdict: 'yes', note: '' };
    const no: MatrixCell = { verdict: 'no', note: '' };
    const asText: MatrixCell = { verdict: 'partial', note: words.asText };
    switch (kind) {
        case 'text':
            if (cap.maxCharacters === null) return yes;
            return cap.supportsThreads
                ? { verdict: 'partial', note: words.teaserOrThread(cap.maxCharacters) }
                : { verdict: 'partial', note: words.teaser(cap.maxCharacters) };
        case 'headings': return cap.supportsHeadings ? yes : asText;
        case 'lists': return cap.supportsLists ? yes : asText;
        case 'links': return cap.supportsRichText ? yes : cap.supportsLinkPreview ? { verdict: 'partial', note: words.blogLinkOnly } : no;
        case 'images':
            if (cap.maxMediaItems <= 0) return { verdict: 'no', note: cap.supportsLinkPreview ? words.linkCard : '' };
            if (cap.maxMediaItems >= 1000) return yes;
            return { verdict: 'partial', note: cap.supportsThreads ? words.upToFirst(cap.maxMediaItems) : words.upTo(cap.maxMediaItems) };
        case 'video': return cap.supportsVideo ? yes : no;
        case 'audio': return cap.supportsAudio ? yes : no;
        case 'tables': return cap.supportsTables ? yes : cap.supportsRichText ? asText : no;
        case 'code': return cap.supportsCodeBlocks ? yes : asText;
        case 'math': return cap.supportsMath ? yes : cap.supportsRichText ? asText : no;
        case 'quotes': return cap.supportsRichText ? yes : asText;
    }
}

const NAMES: Record<string, string> = { telegram: 'Telegram', x: 'X', bluesky: 'Bluesky', discord: 'Discord' };
const BRANDS: Record<string, BrandIconName> = { telegram: 'telegram', x: 'twitter', bluesky: 'bluesky', discord: 'discord' };

function zeroCounts(): DocumentKindCounts {
    return Object.fromEntries(DOCUMENT_KINDS.map(k => [k, 0])) as DocumentKindCounts;
}

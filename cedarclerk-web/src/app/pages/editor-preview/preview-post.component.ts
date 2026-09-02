import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleService } from '../../core/i18n/locale.service';
import { MicroNetwork, MicroPreview, MicroPreviewPost } from '../../core/preview.service';
import { BrandIconComponent, BrandIconName } from '../../shared/brand-icon.component';

export type MicroMode = 'single' | 'thread';

// The short-post card draws the DTO and nothing else, like the phone: the announcement as one
// post, or the thread as the parts the splitter left, each replying to the one before. The link
// card is drawn only where the network unfurls a bare link (X, Discord) — Bluesky shows the text.
@Component({
    selector: 'app-preview-post',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [BrandIconComponent],
    host: { 'data-surface': 'paper' },
    template: `
        <div class="feed" [class.is-wide]="wide()" [attr.data-network]="network()">
            @for (post of posts(); track post.index; let last = $last) {
                <article class="post" [class.is-thread]="posts().length > 1" [attr.aria-label]="postName(post)">
                    <span class="post-rail" aria-hidden="true">
                        <span class="post-avatar">{{ initial() }}</span>
                        @if (!last) { <span class="post-line"></span> }
                    </span>
                    <div class="post-body">
                        <header class="post-head">
                            <strong class="post-name">{{ accountName() || t().editor.previewTab.post.noAccount }}</strong>
                            <span class="post-now">· {{ t().editor.previewTab.post.now }}</span>
                            <app-brand-icon class="post-brand" [name]="brand()" [size]="14" />
                        </header>
                        <p class="post-text">{{ bodyOf(post) }}@if (post.linkUrl) {<a class="post-link">{{ shortLink(post.linkUrl) }}</a>}</p>
                        @if (post.imageUrls.length) {
                            <div class="post-images" [class.is-grid]="post.imageUrls.length > 1">
                                @for (url of post.imageUrls; track url) {
                                    <span class="post-image"><img [src]="url" alt="" loading="lazy"></span>
                                }
                            </div>
                        }
                        @if (post.linkUrl && unfurls() && !post.imageUrls.length) {
                            <div class="post-card">
                                <span class="post-card-host">{{ hostOf(post.linkUrl) }}</span>
                                <span class="post-card-title">{{ linkTitle() || post.linkUrl }}</span>
                            </div>
                        }
                        <footer class="post-meta">
                            <span [class.is-over]="post.length > max()">{{ post.length.toLocaleString() }} / {{ max().toLocaleString() }}</span>
                            @if (posts().length > 1) { <span>· {{ t().editor.previewTab.post.part(post.index + 1, posts().length) }}</span> }
                        </footer>
                    </div>
                </article>
            } @empty {
                <p class="post-empty">{{ t().editor.previewTab.post.empty }}</p>
            }
        </div>
    `,
    styles: [`
        :host {
            display: flex;
            justify-content: center;
            padding: var(--space-5) 0;
        }

        .feed {
            display: flex;
            flex-direction: column;
            width: 390px;
            max-width: 100%;
            box-sizing: border-box;
            border: 8px solid var(--text);
            border-radius: 36px;
            background: var(--surface);
            padding: 14px 0;
            font-family: var(--font-sans);
            color: var(--text);
        }

        .feed.is-wide {
            width: 100%;
            max-width: 600px;
            border-width: 1px;
            border-color: var(--border);
            border-radius: var(--radius-md);
        }

        .post {
            display: flex;
            gap: 10px;
            padding: 10px 14px;
        }

        .post + .post:not(.is-thread) { border-top: 1px solid var(--border); }

        .post-rail { display: flex; flex-direction: column; align-items: center; flex: none; width: 36px; }

        .post-avatar {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 36px;
            height: 36px;
            border-radius: 50%;
            background: var(--accent);
            color: var(--text-on-pine);
            font-weight: 700;
        }

        .post-line { flex: 1; width: 2px; margin-top: 4px; background: var(--border); }

        .post-body { display: flex; flex: 1; flex-direction: column; gap: 8px; min-width: 0; font-size: var(--fs-14); line-height: 1.4; }

        .post-head { display: flex; align-items: center; gap: 4px; min-width: 0; }
        .post-name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .post-now { color: var(--t3); font-size: var(--fs-13); white-space: nowrap; }
        .post-brand { margin-left: auto; color: var(--t3); }

        .post-text { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
        .post-link { color: var(--accent); text-decoration: none; }

        .post-images { display: flex; gap: 4px; overflow: hidden; border-radius: 12px; border: 1px solid var(--border); }
        .post-images.is-grid { flex-wrap: wrap; }
        .post-image { display: block; flex: 1 1 100%; max-height: 240px; background: var(--sheet); }
        .is-grid .post-image { flex: 1 1 45%; min-height: 100px; max-height: 140px; }
        .post-image img { display: block; width: 100%; height: 100%; object-fit: cover; }

        .post-card {
            display: flex;
            flex-direction: column;
            gap: 2px;
            padding: 10px 12px;
            border: 1px solid var(--border);
            border-radius: 12px;
            background: var(--sheet);
        }

        .post-card-host { font-size: var(--fs-12); color: var(--t3); }
        .post-card-title { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

        .post-meta { display: flex; gap: 4px; font-size: var(--fs-11); color: var(--t3); }
        .post-meta .is-over { color: var(--danger); font-weight: 600; }

        .post-empty { margin: auto; padding: 24px; font-size: var(--fs-13); color: var(--t3); text-align: center; }
    `],
})
export class PreviewPostComponent {
    readonly preview = input<MicroPreview | null>(null);
    readonly mode = input<MicroMode>('single');
    readonly accountName = input('');
    /** The document's title, for the link card the network would build from the blog page. */
    readonly linkTitle = input('');
    /** Desktop mode draws the same feed without the phone shell. */
    readonly wide = input(false);

    protected readonly t = inject(LocaleService).t;

    protected readonly network = computed<MicroNetwork | null>(() => this.preview()?.network ?? null);
    protected readonly max = computed(() => this.preview()?.maxLength ?? 0);
    protected readonly initial = computed(() => (this.accountName() || '?').trim().replace(/^@/, '').charAt(0).toUpperCase());
    protected readonly unfurls = computed(() => this.network() !== 'bluesky');

    protected readonly posts = computed<MicroPreviewPost[]>(() => {
        const p = this.preview();
        if (!p) return [];
        if (this.mode() === 'thread' && p.supportsThreads) return p.thread;
        return p.single.text.trim() ? [p.single] : [];
    });

    protected readonly brand = computed<BrandIconName>(() => {
        const network = this.network();
        return network === 'x' || network === null ? 'twitter' : network;
    });

    bodyOf(post: MicroPreviewPost): string {
        if (!post.linkUrl || !post.text.endsWith(post.linkUrl)) return post.text;
        return post.text.slice(0, post.text.length - post.linkUrl.length);
    }

    shortLink(url: string): string {
        return url.replace(/^https?:\/\//, '');
    }

    hostOf(url: string): string {
        try {
            return new URL(url).host;
        } catch {
            return url;
        }
    }

    postName(post: MicroPreviewPost): string {
        return this.t().editor.previewTab.post.part(post.index + 1, this.posts().length);
    }
}

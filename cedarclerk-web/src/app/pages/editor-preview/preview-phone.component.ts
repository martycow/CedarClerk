import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleService } from '../../core/i18n/locale.service';
import {
    MEDIA_KINDS, PreviewTheme, TelegramPreview, TelegramPreviewBlock, TelegramPreviewMessage,
} from '../../core/preview.service';
import { IconComponent } from '../../shared/icon.component';
import { IconName } from '../../shared/icon-data.generated';

const MAX_TILES = 4;

function blockLength(block: TelegramPreviewBlock): number {
    return block.text.length + (block.caption?.length ?? 0);
}

// The phone draws the DTO and nothing else (CONTRACT §E8): one bubble per message, blocks in the
// order the server left them, media over the relative /media URLs, the CTA row under the last
// bubble. No client-side split — the count is the server's. ADR-313: the colours are Telegram's own
// (light and night), scoped to the phone and driven by `theme`, not the app's paper tokens.
@Component({
    selector: 'app-preview-phone',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent, NgTemplateOutlet],
    host: { 'data-surface': 'paper', '[attr.data-tg-theme]': 'theme()' },
    template: `
        <div class="phone" [class.is-wide]="wide()">
            <div class="phone-head">
                <span class="phone-avatar" aria-hidden="true">{{ initial() }}</span>
                <span class="phone-who">
                    <span class="phone-title">{{ channelTitle() || t().editor.previewTab.phone.noChannel }}</span>
                    <span class="phone-sub">{{ channelHandle() ? '@' + channelHandle() : t().editor.previewTab.phone.channel }}</span>
                </span>
            </div>
            <div class="phone-feed">
                <span class="phone-day">{{ t().editor.previewTab.phone.today }}</span>
                @for (m of messages(); track m.index; let last = $last) {
                    <article class="bubble" [attr.aria-label]="bubbleName(m)">
                        @for (b of m.blocks; track $index) {
                            @if (b.kind === 'list' && b.items?.length) {
                                <div class="bubble-block" data-kind="list">
                                    @if (isOrdered(b)) {
                                        <ol class="bubble-list"><ng-container *ngTemplateOutlet="listItems; context: { $implicit: b.items }" /></ol>
                                    } @else {
                                        <ul class="bubble-list"><ng-container *ngTemplateOutlet="listItems; context: { $implicit: b.items }" /></ul>
                                    }
                                </div>
                            } @else if (isMedia(b)) {
                                <figure class="bubble-media" [class.is-group]="b.urls.length > 1" [attr.data-kind]="b.kind">
                                    @for (url of shown(b); track $index; let tail = $last) {
                                        <span class="bubble-tile">
                                            @if (b.kind === 'photo' || b.kind === 'slideshow' || b.kind === 'collage') {
                                                <img [src]="url" alt="" loading="lazy">
                                            } @else {
                                                <app-icon [name]="mediaIcon(b)" size="md" />
                                            }
                                            @if (tail && hidden(b) > 0) {
                                                <span class="bubble-more">{{ t().editor.previewTab.phone.moreImages(hidden(b)) }}</span>
                                            }
                                        </span>
                                    }
                                    @if (b.kind !== 'photo') {
                                        <figcaption class="bubble-media-kind">{{ mediaWord(b) }}</figcaption>
                                    }
                                    @if (b.caption) { <figcaption class="bubble-caption">{{ b.caption }}</figcaption> }
                                </figure>
                            } @else {
                                <p class="bubble-block" [attr.data-kind]="b.kind"
                                   [class.is-heading]="b.kind === 'heading'" [class.is-quote]="b.kind === 'quote'"
                                   [class.is-code]="b.kind === 'code'" [class.is-footer]="b.kind === 'footer'">{{ blockText(b) }}</p>
                            }
                            @if ($index === foldAfterBlock(m)) {
                                <div class="bubble-fold" role="separator">{{ t().editor.previewTab.phone.showMore }}</div>
                            }
                        }
                        @if (last && buttons().length) {
                            <div class="bubble-buttons">
                                @for (btn of buttons(); track $index) {
                                    <span class="bubble-button">{{ btn.text }}</span>
                                }
                            </div>
                        }
                        <span class="bubble-meta">{{ m.characters.toLocaleString() }} / {{ max().toLocaleString() }}{{ foldNote(m) }} · {{ cutWord(m) }}</span>
                    </article>
                } @empty {
                    <p class="phone-empty">{{ t().editor.previewTab.phone.empty }}</p>
                }
            </div>
        </div>
        <ng-template #listItems let-items>
            @for (item of items; track $index) {
                <li class="bubble-li" [class.has-check]="item.hasCheckbox">
                    @if (item.hasCheckbox) {
                        <span class="bubble-check" [class.is-on]="item.isChecked" role="checkbox" aria-disabled="true"
                              [attr.aria-checked]="item.isChecked"></span>
                    } @else if (item.order !== null) {
                        <span class="bubble-marker">{{ item.order }}.</span>
                    } @else {
                        <span class="bubble-marker" aria-hidden="true">•</span>
                    }
                    <span class="bubble-li-text">{{ item.text }}</span>
                </li>
            }
        </ng-template>
    `,
    styles: [`
        /* ADR-313 — Telegram's own colours, for the phone only. Light and night are both
           Telegram's, so a post can be judged on the background readers will actually see. */
        :host {
            --tg-bg: #dfe7ec;
            --tg-head: #ffffff;
            --tg-bubble: #ffffff;
            --tg-text: #11171c;
            --tg-text-2: #4a5b68;
            --tg-meta: #5f7283;
            --tg-border: #c9d4dc;
            --tg-accent: #2481cc;
            --tg-button-bg: #dbe9f5;
            --tg-tile: #e9eff3;
            --tg-fold: #2481cc;
            display: flex;
            justify-content: center;
            padding: var(--space-5) 0;
        }

        :host([data-tg-theme="dark"]) {
            --tg-bg: #0e1621;
            --tg-head: #17212b;
            --tg-bubble: #182533;
            --tg-text: #f5f5f5;
            --tg-text-2: #b6c3cf;
            --tg-meta: #8ea1b2;
            --tg-border: #25323f;
            --tg-accent: #6ab3f3;
            --tg-button-bg: #1f3347;
            --tg-tile: #101b27;
            --tg-fold: #6ab3f3;
        }

        .phone {
            display: flex;
            flex-direction: column;
            width: 390px;
            max-width: 100%;
            min-height: 560px;
            box-sizing: border-box;
            overflow: hidden;
            border: 8px solid var(--text);
            border-radius: 36px;
            background: var(--tg-bg);
            font-family: var(--font-sans);
            color: var(--tg-text);
        }

        .phone.is-wide { width: 100%; border-width: 1px; border-color: var(--border); border-radius: var(--radius-md); }

        .phone-head {
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 14px 14px 10px;
            border-bottom: 1px solid var(--tg-border);
            background: var(--tg-head);
        }

        .phone-avatar {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 34px;
            height: 34px;
            border-radius: 50%;
            background: var(--tg-accent);
            color: #ffffff;
            font-weight: 700;
        }

        .phone-who { display: flex; flex-direction: column; min-width: 0; }
        .phone-title { font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .phone-sub { font-size: var(--fs-12); color: var(--tg-meta); }

        .phone-feed {
            display: flex;
            flex: 1;
            flex-direction: column;
            gap: 10px;
            padding: 12px;
        }

        .phone-day {
            align-self: center;
            padding: 2px 8px;
            border-radius: 10px;
            background: var(--tg-head);
            font-size: var(--fs-11);
            color: var(--tg-meta);
        }

        .bubble {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
            padding: 10px 12px;
            border-radius: 12px 12px 12px 4px;
            background: var(--tg-bubble);
            box-shadow: 0 1px 1px rgb(0 0 0 / .12);
            font-size: var(--fs-14);
            line-height: 1.45;
        }

        .bubble-block { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
        .bubble-block.is-heading { font-weight: 700; }
        .bubble-block.is-quote { padding-left: 10px; border-left: 2px solid var(--tg-accent); color: var(--tg-text-2); }
        .bubble-block.is-code { font-family: var(--font-mono); font-size: var(--fs-12); }
        .bubble-block.is-footer { color: var(--tg-accent); font-size: var(--fs-13); }

        /* Lists — Telegram draws real markers; a list that reads as plain lines hides a mistake. */
        .bubble-block[data-kind="list"] { white-space: normal; }
        .bubble-list { display: flex; flex-direction: column; gap: 2px; margin: 0; padding: 0; list-style: none; }
        .bubble-li { display: flex; gap: 8px; align-items: baseline; }
        .bubble-marker { flex: none; min-width: 1.4em; text-align: right; color: var(--tg-text-2); }
        .bubble-li-text { min-width: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
        .bubble-li.has-check { align-items: flex-start; }
        .bubble-check {
            position: relative;
            flex: none;
            width: 16px;
            height: 16px;
            margin-top: 3px;
            box-sizing: border-box;
            border: 2px solid var(--tg-meta);
            border-radius: 4px;
        }
        .bubble-check.is-on { border-color: var(--tg-accent); background: var(--tg-accent); }
        .bubble-check.is-on::after {
            content: '';
            position: absolute;
            left: 3px;
            top: 0;
            width: 4px;
            height: 8px;
            border: solid #ffffff;
            border-width: 0 2px 2px 0;
            transform: rotate(45deg);
        }

        /* Media — a single picture keeps its own shape (a portrait stays a portrait, up to a cap);
           a group is a grid of square cells, which is what a collage is. */
        .bubble-media { display: flex; flex-wrap: wrap; gap: 4px; margin: 0; }
        .bubble-media.is-group .bubble-tile { flex: 1 1 calc(50% - 2px); aspect-ratio: 1; min-height: 84px; }
        .bubble-media.is-group .bubble-tile img { height: 100%; object-fit: cover; }

        .bubble-tile {
            position: relative;
            display: flex;
            align-items: center;
            justify-content: center;
            flex: 1 1 100%;
            min-height: 96px;
            overflow: hidden;
            border-radius: 8px;
            background: var(--tg-tile);
            color: var(--tg-meta);
        }

        .bubble-tile img { display: block; width: 100%; height: auto; max-height: 420px; object-fit: contain; }

        .bubble-more {
            position: absolute;
            inset: 0;
            display: flex;
            align-items: center;
            justify-content: center;
            background: rgb(0 0 0 / .5);
            color: #ffffff;
            font-size: var(--fs-18);
            font-weight: 700;
        }

        .bubble-media-kind { flex: 1 1 100%; font-size: var(--fs-11); color: var(--tg-meta); text-transform: uppercase; letter-spacing: .06em; }
        .bubble-caption { flex: 1 1 100%; font-size: var(--fs-12); color: var(--tg-text-2); }

        /* Where the client folds the post behind "Show more" (ADR-086) — inside one message. */
        .bubble-fold {
            padding-top: var(--space-2);
            border-top: 1px dashed var(--tg-fold);
            color: var(--tg-fold);
            font-size: var(--fs-13);
        }

        .bubble-buttons { display: flex; flex-direction: column; gap: 4px; }

        .bubble-button {
            padding: 8px;
            border-radius: 6px;
            background: var(--tg-button-bg);
            color: var(--tg-accent);
            font-weight: 600;
            text-align: center;
        }

        .bubble-meta { display: flex; gap: 4px; align-self: flex-end; font-size: var(--fs-11); color: var(--tg-meta); }

        .phone-empty { margin: auto 0; font-size: var(--fs-13); color: var(--tg-meta); text-align: center; }
    `],
})
export class PreviewPhoneComponent {
    readonly preview = input<TelegramPreview | null>(null);
    readonly channelTitle = input('');
    readonly channelHandle = input<string | null>(null);
    /** Desktop mode draws the same feed without the phone shell. */
    readonly wide = input(false);
    /** Telegram's own light or night palette (ADR-313), not the app's theme. */
    readonly theme = input<PreviewTheme>('light');

    protected readonly t = inject(LocaleService).t;

    protected readonly messages = computed(() => this.preview()?.messages ?? []);
    protected readonly buttons = computed(() => this.preview()?.buttons ?? []);
    protected readonly max = computed(() => this.preview()?.maxCharactersPerMessage ?? 0);
    protected readonly fold = computed(() => this.preview()?.foldAfterCharacters ?? 0);
    protected readonly initial = computed(() => (this.channelTitle() || '?').trim().charAt(0).toUpperCase());

    /** A slideshow or collage shows four tiles; the rest are counted on the last one. */
    shown(block: TelegramPreviewBlock): string[] {
        return block.urls.slice(0, MAX_TILES);
    }

    hidden(block: TelegramPreviewBlock): number {
        return Math.max(0, block.urls.length - MAX_TILES);
    }

    isOrdered(block: TelegramPreviewBlock): boolean {
        return !!block.items?.length && block.items[0].order !== null;
    }

    /**
     * The index of the block the "Show more" marker is drawn after: the first block whose running
     * text passes the fold, provided something follows it. Block-granular on purpose — the client
     * folds mid-paragraph, which a flat DTO cannot show; the meta line says it in numbers too.
     */
    foldAfterBlock(message: TelegramPreviewMessage): number {
        const fold = this.fold();
        if (!fold || message.characters <= fold) return -1;
        let run = 0;
        for (let i = 0; i < message.blocks.length - 1; i++) {
            run += blockLength(message.blocks[i]);
            if (run >= fold) return i;
        }
        return -1;
    }

    foldNote(message: TelegramPreviewMessage): string {
        const fold = this.fold();
        return fold && message.characters > fold ? ` · ${this.t().editor.previewTab.phone.foldsAfter(fold)}` : '';
    }

    isMedia(block: TelegramPreviewBlock): boolean {
        return MEDIA_KINDS.has(block.kind);
    }

    mediaIcon(block: TelegramPreviewBlock): IconName {
        switch (block.kind) {
            case 'video': return 'video-camera';
            case 'audio': return 'waveform';
            default: return 'image';
        }
    }

    mediaWord(block: TelegramPreviewBlock): string {
        const words = this.t().editor.previewTab.phone;
        switch (block.kind) {
            case 'video': return words.video;
            case 'audio': return words.audio;
            case 'slideshow':
            case 'collage': return words.gallery(block.urls.length);
            default: return words.photo;
        }
    }

    blockText(block: TelegramPreviewBlock): string {
        if (block.kind === 'divider') return '———';
        return block.text;
    }

    cutWord(message: TelegramPreviewMessage): string {
        return this.t().editor.previewTab.phone.cut[message.cutReason];
    }

    bubbleName(message: TelegramPreviewMessage): string {
        return this.t().editor.previewTab.phone.message(message.index + 1, this.messages().length);
    }
}

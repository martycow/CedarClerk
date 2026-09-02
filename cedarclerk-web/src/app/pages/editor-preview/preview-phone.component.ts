import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleService } from '../../core/i18n/locale.service';
import { MEDIA_KINDS, TelegramPreview, TelegramPreviewBlock, TelegramPreviewMessage } from '../../core/preview.service';
import { IconComponent } from '../../shared/icon.component';
import { IconName } from '../../shared/icon-data.generated';

// The phone draws the DTO and nothing else (CONTRACT §E8): one bubble per message, blocks in the
// order the splitter left them, media as placeholders over the relative /media URLs, the CTA row
// under the last bubble. No client-side split — the count is the server's.
@Component({
    selector: 'app-preview-phone',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent],
    host: { 'data-surface': 'paper' },
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
                            @if (isMedia(b)) {
                                <figure class="bubble-media" [class.is-group]="b.urls.length > 1">
                                    @for (url of b.urls.slice(0, 4); track $index) {
                                        <span class="bubble-tile">
                                            @if (b.kind === 'photo' || b.kind === 'slideshow' || b.kind === 'collage') {
                                                <img [src]="url" alt="" loading="lazy">
                                            } @else {
                                                <app-icon [name]="mediaIcon(b)" size="md" />
                                            }
                                        </span>
                                    }
                                    <figcaption class="bubble-media-kind">{{ mediaWord(b) }}</figcaption>
                                    @if (b.caption) { <figcaption class="bubble-caption">{{ b.caption }}</figcaption> }
                                </figure>
                            } @else {
                                <p class="bubble-block" [attr.data-kind]="b.kind"
                                   [class.is-heading]="b.kind === 'heading'" [class.is-quote]="b.kind === 'quote'"
                                   [class.is-code]="b.kind === 'code'" [class.is-footer]="b.kind === 'footer'">{{ blockText(b) }}</p>
                            }
                        }
                        @if (last && buttons().length) {
                            <div class="bubble-buttons">
                                @for (btn of buttons(); track $index) {
                                    <span class="bubble-button">{{ btn.text }}</span>
                                }
                            </div>
                        }
                        <span class="bubble-meta">{{ m.characters.toLocaleString() }} / {{ max().toLocaleString() }} · {{ cutWord(m) }}</span>
                    </article>
                } @empty {
                    <p class="phone-empty">{{ t().editor.previewTab.phone.empty }}</p>
                }
            </div>
        </div>
    `,
    styles: [`
        :host {
            display: flex;
            justify-content: center;
            padding: var(--space-5) 0;
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
            background: var(--surface);
            font-family: var(--font-sans);
            color: var(--text);
        }

        .phone.is-wide { width: 100%; border-width: 1px; border-color: var(--border); border-radius: var(--radius-md); }

        .phone-head {
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 14px 14px 10px;
            border-bottom: 1px solid var(--border);
            background: var(--sheet);
        }

        .phone-avatar {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 34px;
            height: 34px;
            border-radius: 50%;
            background: var(--accent);
            color: var(--text-on-pine);
            font-weight: 700;
        }

        .phone-who { display: flex; flex-direction: column; min-width: 0; }
        .phone-title { font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .phone-sub { font-size: var(--fs-12); color: var(--t3); }

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
            background: var(--sheet);
            font-size: var(--fs-11);
            color: var(--t3);
        }

        .bubble {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
            padding: 10px 12px;
            border-radius: 12px 12px 12px 4px;
            background: var(--sheet);
            box-shadow: var(--shadow-paper-sm);
            font-size: var(--fs-14);
            line-height: 1.45;
        }

        .bubble-block { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
        .bubble-block.is-heading { font-weight: 700; }
        .bubble-block.is-quote { padding-left: 10px; border-left: 2px solid var(--accent); color: var(--t2); }
        .bubble-block.is-code { font-family: var(--font-mono); font-size: var(--fs-12); }
        .bubble-block.is-footer { color: var(--accent); font-size: var(--fs-13); }

        .bubble-media { display: flex; flex-wrap: wrap; gap: 4px; margin: 0; }
        .bubble-media.is-group .bubble-tile { flex: 1 1 45%; min-height: 84px; }

        .bubble-tile {
            display: flex;
            align-items: center;
            justify-content: center;
            flex: 1 1 100%;
            min-height: 120px;
            max-height: 220px;
            overflow: hidden;
            border-radius: 8px;
            background: var(--surface);
            color: var(--t3);
        }

        .bubble-tile img { display: block; width: 100%; height: 100%; object-fit: cover; }

        .bubble-media-kind { flex: 1 1 100%; font-size: var(--fs-11); color: var(--t3); text-transform: uppercase; letter-spacing: .06em; }
        .bubble-caption { flex: 1 1 100%; font-size: var(--fs-12); color: var(--t2); }

        .bubble-buttons { display: flex; flex-direction: column; gap: 4px; }

        .bubble-button {
            padding: 8px;
            border-radius: 6px;
            background: var(--ok-soft);
            color: var(--ok);
            font-weight: 600;
            text-align: center;
        }

        .bubble-meta { display: flex; gap: 4px; align-self: flex-end; font-size: var(--fs-11); color: var(--t3); }

        .phone-empty { margin: auto 0; font-size: var(--fs-13); color: var(--t3); text-align: center; }
    `],
})
export class PreviewPhoneComponent {
    readonly preview = input<TelegramPreview | null>(null);
    readonly channelTitle = input('');
    readonly channelHandle = input<string | null>(null);
    /** Desktop mode draws the same feed without the phone shell. */
    readonly wide = input(false);

    protected readonly t = inject(LocaleService).t;

    protected readonly messages = computed(() => this.preview()?.messages ?? []);
    protected readonly buttons = computed(() => this.preview()?.buttons ?? []);
    protected readonly max = computed(() => this.preview()?.maxCharactersPerMessage ?? 0);
    protected readonly initial = computed(() => (this.channelTitle() || '?').trim().charAt(0).toUpperCase());

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

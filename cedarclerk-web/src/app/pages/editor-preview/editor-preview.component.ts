import {
    ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, effect, inject, input, output, signal,
    untracked, viewChild,
} from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Channel } from '../../core/channels.service';
import { ScheduledInfo } from '../../core/drafts.service';
import { LocaleService } from '../../core/i18n/locale.service';
import { endonymOf } from '../../core/languages';
import { PostsService, PreflightLanguage } from '../../core/posts.service';
import { PreviewService, PreviewTheme, TelegramPreview } from '../../core/preview.service';
import { PublishAccount, PublishService } from '../../core/publish.service';
import { ThemeService } from '../../core/theme.service';
import { EmptyStateComponent } from '../../shell/empty-state.component';
import { IconComponent } from '../../shared/icon.component';
import { PreviewCheck, PreviewChecksComponent } from './preview-checks.component';
import { DestinationRow, PreviewDestination, PreviewDestinationsComponent } from './preview-destinations.component';
import { PreviewPhoneComponent } from './preview-phone.component';

/** What the editor already holds about the open draft — the tab fetches nothing it can be told. */
export interface PreviewDraftFacts {
    id: string;
    title: string;
    typeName: string;
    isWorkingMaterial: boolean;
    primaryLanguage: string;
    /** Translations that exist, primary excluded. */
    languages: string[];
    staleLanguages: string[];
    coverImagePath: string | null;
    blog: { slug: string; isPublished: boolean } | null;
    isPrivate: boolean;
    scheduled: ScheduledInfo | null;
}

interface PublishIssue { code: string; blocking: boolean; actual: number; limit: number; }

type Device = 'desktop' | 'mobile';

const MOBILE_WIDTH_PX = 390;

// The Preview tab (ADR-239 clause 9): destinations with their readiness on the left, the render
// in the middle, the checks for the selected destination on the right. Readiness and checks are
// composed here from endpoints that already exist (CONTRACT §D4); the render is the server's.
@Component({
    selector: 'app-editor-preview',
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [IconComponent, EmptyStateComponent, PreviewDestinationsComponent, PreviewPhoneComponent, PreviewChecksComponent],
    host: { 'data-surface': 'paper' },
    template: `
        <app-preview-destinations [rows]="rows()" [selected]="destination()" (pick)="destination.set($event)" />

        <section class="card ep-render">
            <div class="ep-toolbar">
                <div class="seg" role="group" [attr.aria-label]="t().editor.previewTab.deviceLabel">
                    <button type="button" [class.is-on]="device() === 'desktop'" [attr.aria-pressed]="device() === 'desktop'" (click)="device.set('desktop')">
                        <app-icon name="desktop" size="xs" />{{ t().editor.previewTab.desktop }}
                    </button>
                    <button type="button" [class.is-on]="device() === 'mobile'" [attr.aria-pressed]="device() === 'mobile'" (click)="device.set('mobile')">
                        <app-icon name="device-mobile" size="xs" />{{ t().editor.previewTab.mobile }}
                    </button>
                </div>
                <span class="ep-spacer"></span>
                <span class="ep-width" role="status">{{ t().editor.previewTab.width(paneWidth()) }}</span>
                <span class="ep-spacer"></span>
                @if (languages().length > 1) {
                    <div class="seg" role="group" [attr.aria-label]="t().editor.previewTab.languageLabel">
                        @for (code of languages(); track code) {
                            <button type="button" [class.is-on]="lang() === code" [attr.aria-pressed]="lang() === code"
                                    [attr.title]="endonym(code)" (click)="lang.set(code)">{{ code.toUpperCase() }}</button>
                        }
                    </div>
                }
                <button type="button" class="ep-icon-btn" (click)="toggleTheme()"
                        [attr.title]="t().editor.previewTab.toggleTheme" [attr.aria-label]="t().editor.previewTab.toggleTheme">
                    <app-icon [name]="theme() === 'dark' ? 'sun' : 'moon'" size="sm" />
                </button>
            </div>

            <div class="ep-pane" #pane [class.is-mobile]="device() === 'mobile'">
                @switch (destination()) {
                    @case ('blog') {
                        @if (renderError()) {
                            <app-empty-state icon="warning" [title]="t().editor.previewTab.failed">
                                <button type="button" class="btn sm" (click)="reload()">{{ t().editor.previewTab.retry }}</button>
                            </app-empty-state>
                        } @else {
                            <iframe class="ep-frame" sandbox="" [srcdoc]="blogHtml()" [title]="t().editor.previewTab.blogFrameTitle"></iframe>
                        }
                    }
                    @case ('telegram') {
                        @if (renderError()) {
                            <app-empty-state icon="warning" [title]="t().editor.previewTab.failed">
                                <button type="button" class="btn sm" (click)="reload()">{{ t().editor.previewTab.retry }}</button>
                            </app-empty-state>
                        } @else {
                            <app-preview-phone [preview]="telegram()" [channelTitle]="channelTitle()"
                                               [channelHandle]="channelHandle()" [wide]="device() === 'desktop'" />
                        }
                    }
                    @default {
                        <app-empty-state icon="eye" [title]="t().editor.previewTab.notBuilt" [text]="t().editor.previewTab.notBuiltText" />
                    }
                }
                @if (renderLoading()) {
                    <div class="ep-loading" role="status"><app-icon name="circle-notch" size="sm" />{{ t().editor.previewTab.loading }}</div>
                }
            </div>
        </section>

        <app-preview-checks [title]="t().editor.previewTab.checks.title(destinationName())" [checks]="checks()"
                            [loading]="checksLoading()" (details)="details.emit()" />
    `,
    styles: [`
        :host {
            display: flex;
            flex: 1;
            gap: var(--space-4);
            min-height: 0;
            font-family: var(--font-sans);
        }

        .ep-render {
            display: flex;
            flex: 1;
            flex-direction: column;
            min-width: 0;
            min-height: 0;
            overflow: hidden;
        }

        .ep-toolbar {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            flex: none;
            padding: var(--space-3) var(--space-4);
            border-bottom: 1px solid var(--paper-edge);
        }

        .ep-spacer { flex: 1; }

        .ep-width { font-size: var(--fs-13); color: var(--t3); font-variant-numeric: tabular-nums; }

        .ep-icon-btn {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: var(--hit-target);
            min-height: var(--hit-target);
            border: none;
            border-radius: var(--radius-sm);
            background: none;
            color: var(--t2);
            cursor: pointer;
        }

        .ep-icon-btn:hover { background: var(--hover); color: var(--text); }

        .ep-pane {
            position: relative;
            display: flex;
            flex: 1;
            flex-direction: column;
            min-height: 0;
            overflow: auto;
            background: var(--surface);
        }

        .ep-pane.is-mobile { align-items: center; }

        .ep-frame {
            display: block;
            flex: 1;
            width: 100%;
            min-height: 0;
            border: 0;
            background: var(--sheet);
        }

        .ep-pane.is-mobile .ep-frame {
            width: 390px;
            max-width: 100%;
            margin: var(--space-5) 0;
            flex: none;
            height: 720px;
            border: 8px solid var(--text);
            border-radius: 36px;
        }

        .ep-pane app-preview-phone { flex: none; }
        .ep-pane:not(.is-mobile) app-preview-phone { padding: var(--space-5) var(--space-6); }

        .ep-loading {
            position: absolute;
            top: var(--space-3);
            left: 50%;
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--border);
            border-radius: var(--radius-stamp);
            background: var(--sheet);
            font-size: var(--fs-13);
            color: var(--t2);
            transform: translateX(-50%);
        }

        app-empty-state { margin: var(--space-6); }
    `],
})
export class EditorPreviewComponent implements OnDestroy {
    readonly facts = input.required<PreviewDraftFacts>();
    readonly channels = input<readonly Channel[]>([]);
    /** The language the editor was on when the tab opened; the tab's own switch takes over after. */
    readonly initialLang = input('');
    /** Bumped by the editor after a save, so the render follows the stored document. */
    readonly savedVersion = input(0);
    /** "View all details" — opens the export window's checks. */
    readonly details = output<void>();

    protected readonly t = inject(LocaleService).t;
    private readonly previewApi = inject(PreviewService);
    private readonly posts = inject(PostsService);
    private readonly publishApi = inject(PublishService);
    private readonly sanitizer = inject(DomSanitizer);

    readonly destination = signal<PreviewDestination>('blog');
    readonly device = signal<Device>('desktop');
    readonly lang = signal('');
    readonly theme = signal<PreviewTheme>(inject(ThemeService).theme() === 'dark' ? 'dark' : 'light');

    readonly telegram = signal<TelegramPreview | null>(null);
    readonly blogHtml = signal<SafeHtml | null>(null);
    readonly renderLoading = signal(false);
    readonly renderError = signal(false);

    readonly checksLoading = signal(false);
    private readonly issues = signal<PublishIssue[]>([]);
    private readonly preflight = signal<PreflightLanguage | null>(null);
    private readonly accounts = signal<Partial<Record<'x' | 'bluesky', PublishAccount | null>>>({});
    private readonly partCounts = signal<Partial<Record<'x' | 'bluesky', number>>>({});
    private readonly measured = signal(0);
    private readonly reloadTick = signal(0);

    private readonly pane = viewChild<ElementRef<HTMLElement>>('pane');
    private paneObserver?: ResizeObserver;
    private renderRequest = 0;
    private checksRequest = 0;

    /** The id alone, so a re-derived `facts` object after a save does not read as a new draft. */
    private readonly draftId = computed(() => this.facts().id);

    readonly languages = computed(() => [this.facts().primaryLanguage, ...this.facts().languages]);

    readonly paneWidth = computed(() => this.device() === 'mobile' ? MOBILE_WIDTH_PX : this.measured());

    readonly channelTitle = computed(() => this.channels()[0]?.title ?? '');
    readonly channelHandle = computed(() => this.channels()[0]?.username ?? null);

    readonly destinationName = computed(() => this.rows().find(r => r.id === this.destination())?.name ?? '');

    readonly rows = computed<DestinationRow[]>(() => {
        const words = this.t().editor.previewTab;
        const notConnected = this.t().editor.exportModal.notConnected;
        const f = this.facts();
        const tg = this.telegram();
        const blog: DestinationRow = f.isWorkingMaterial
            ? { id: 'blog', name: words.blog, readiness: 'warn', detail: words.blogWorkingMaterial }
            : {
                id: 'blog', name: words.blog, readiness: 'ready',
                detail: `${f.typeName} · ${f.isPrivate ? this.t().editor.state.private : this.t().editor.state.public}`,
            };
        const telegram: DestinationRow = !this.channels().length
            ? { id: 'telegram', name: 'Telegram', readiness: 'off', detail: notConnected }
            : f.isWorkingMaterial
                ? { id: 'telegram', name: 'Telegram', readiness: 'warn', detail: words.blogWorkingMaterial }
                : { id: 'telegram', name: 'Telegram', readiness: 'ready', detail: tg ? words.telegramMessages(tg.messageCount) : this.channelTitle() };
        const micro = (id: 'x' | 'bluesky', name: string): DestinationRow => {
            const account = this.accounts()[id];
            if (!account) return { id, name, readiness: 'off', detail: notConnected };
            if (account.lastError) return { id, name, readiness: 'warn', detail: account.lastError };
            const parts = this.partCounts()[id];
            return { id, name, readiness: 'ready', detail: parts ? words.parts(parts) : account.displayName };
        };
        return [blog, telegram, micro('x', 'X'), micro('bluesky', 'Bluesky')];
    });

    readonly checks = computed<PreviewCheck[]>(() => {
        switch (this.destination()) {
            case 'blog': return this.blogChecks();
            case 'telegram': return this.telegramChecks();
            default: return [];
        }
    });

    constructor() {
        // A new draft, or a draft the tab was opened on, resets the language to the editor's own.
        effect(() => {
            this.draftId();
            const initial = this.initialLang();
            untracked(() => this.lang.set(this.languages().includes(initial) ? initial : this.facts().primaryLanguage));
        });

        effect(() => {
            const id = this.draftId();
            this.savedVersion();
            untracked(() => void this.loadAccounts(id));
        });

        effect(() => {
            const id = this.draftId();
            const lang = this.lang();
            const theme = this.theme();
            const destination = this.destination();
            this.savedVersion();
            this.reloadTick();
            if (!lang) return;
            untracked(() => {
                void this.loadRender(id, lang, theme, destination);
                void this.loadChecks(id, lang, destination);
            });
        });

        effect(() => {
            const el = this.pane()?.nativeElement;
            this.paneObserver?.disconnect();
            if (!el || typeof ResizeObserver === 'undefined') return;
            this.paneObserver = new ResizeObserver(() => this.measured.set(Math.round(el.clientWidth)));
            this.paneObserver.observe(el);
            this.measured.set(Math.round(el.clientWidth));
        });
    }

    ngOnDestroy() {
        this.paneObserver?.disconnect();
    }

    endonym(code: string): string {
        return endonymOf(code);
    }

    toggleTheme() {
        this.theme.update(v => v === 'dark' ? 'light' : 'dark');
    }

    reload() {
        this.reloadTick.update(v => v + 1);
    }

    private async loadAccounts(id: string) {
        try {
            const networks = await this.publishApi.networks();
            const x = networks.find(n => n.network === 'x')?.accounts[0] ?? null;
            const bluesky = networks.find(n => n.network === 'bluesky')?.accounts[0] ?? null;
            this.accounts.set({ x, bluesky });
            // T-367 — the rows carry the part counts while the middle pane says the render is coming.
            for (const network of ['x', 'bluesky'] as const) {
                if (!(network === 'x' ? x : bluesky)) continue;
                this.publishApi.threadPreview(id, network)
                    .then(res => this.partCounts.update(m => ({ ...m, [network]: res.parts.length })))
                    .catch(() => { /* the row falls back to the account name */ });
            }
        } catch {
            this.accounts.set({});
        }
    }

    private async loadRender(id: string, lang: string, theme: PreviewTheme, destination: PreviewDestination) {
        if (destination !== 'blog' && destination !== 'telegram') { this.renderLoading.set(false); return; }
        const request = ++this.renderRequest;
        this.renderLoading.set(true);
        this.renderError.set(false);
        try {
            if (destination === 'blog') {
                const html = await this.previewApi.blogHtml(id, lang, theme);
                if (request !== this.renderRequest) return;
                // Nothing in the page can run: the frame is sandboxed with no permissions, so the
                // sanitizer's bypass hands over markup, not trust.
                this.blogHtml.set(this.sanitizer.bypassSecurityTrustHtml(html));
            } else {
                const preview = await this.previewApi.telegram(id, lang);
                if (request !== this.renderRequest) return;
                this.telegram.set(preview);
            }
        } catch {
            if (request !== this.renderRequest) return;
            this.renderError.set(true);
        } finally {
            if (request === this.renderRequest) this.renderLoading.set(false);
        }
    }

    private async loadChecks(id: string, lang: string, destination: PreviewDestination) {
        if (destination !== 'blog' && destination !== 'telegram') { this.checksLoading.set(false); return; }
        const request = ++this.checksRequest;
        this.checksLoading.set(true);
        // Both are best-effort by contract: a check that cannot run says nothing rather than warns.
        const [issues, preflight] = await Promise.all([
            destination === 'telegram'
                ? this.posts.validate(id, 'telegram', lang).then(r => r.issues).catch(() => [] as PublishIssue[])
                : Promise.resolve([] as PublishIssue[]),
            this.posts.preflight(id, [lang]).then(r => r.perLanguage?.[0] ?? null).catch(() => null),
        ]);
        if (request !== this.checksRequest) return;
        this.issues.set(issues);
        this.preflight.set(preflight);
        this.checksLoading.set(false);
    }

    private otherLanguageRows(prefix: (code: string) => string): PreviewCheck[] {
        const words = this.t().editor.previewTab.checks;
        const f = this.facts();
        return f.languages.filter(code => code !== this.lang()).map(code => {
            const stale = f.staleLanguages.includes(code);
            return {
                id: `lang-${code}`, label: prefix(code.toUpperCase()),
                detail: stale ? words.stale : words.inSync, tone: stale ? 'warn' : 'ok',
            };
        });
    }

    private linkRow(): PreviewCheck {
        const words = this.t().editor.previewTab.checks;
        const pre = this.preflight();
        if (pre?.emptyVersion) return { id: 'links', label: words.links, detail: words.emptyVersion, tone: 'warn' };
        const dead = pre?.deadLinks.length ?? 0;
        return dead
            ? { id: 'links', label: words.links, detail: words.linksWarn(dead), tone: 'warn' }
            : { id: 'links', label: words.links, detail: pre ? words.ok : words.notChecked, tone: pre ? 'ok' : 'muted' };
    }

    private blogChecks(): PreviewCheck[] {
        const words = this.t().editor.previewTab.checks;
        const f = this.facts();
        const rows: PreviewCheck[] = [];
        if (f.isWorkingMaterial) rows.push({ id: 'type', label: words.type, detail: words.workingMaterial(f.typeName), tone: 'warn' });
        rows.push(f.title.trim()
            ? { id: 'title', label: words.docTitle, detail: words.ok, tone: 'ok' }
            : { id: 'title', label: words.docTitle, detail: words.noTitle, tone: 'warn' });
        rows.push(f.coverImagePath
            ? { id: 'hero', label: words.hero, detail: words.heroFound(fileName(f.coverImagePath)), tone: 'ok' }
            : { id: 'hero', label: words.hero, detail: words.heroMissing, tone: 'warn' });
        rows.push(f.blog?.slug
            ? { id: 'slug', label: words.slug, detail: f.blog.slug, tone: 'ok' }
            : { id: 'slug', label: words.slug, detail: words.slugMissing, tone: 'muted' });
        rows.push({
            id: 'visibility', label: words.visibility,
            detail: f.isPrivate ? this.t().editor.state.private : this.t().editor.state.public,
            tone: f.isPrivate ? 'muted' : 'ok',
        });
        const others = this.otherLanguageRows(code => words.otherLanguage(code));
        rows.push({
            id: 'language', label: words.language,
            detail: others.length ? `${this.endonym(this.lang())} (${this.lang().toUpperCase()})` : words.singleVersion(this.lang().toUpperCase()),
            tone: 'ok',
        }, ...others);
        rows.push(this.linkRow());
        return rows;
    }

    private telegramChecks(): PreviewCheck[] {
        const words = this.t().editor.previewTab.checks;
        const f = this.facts();
        const tg = this.telegram();
        const channel = this.channels()[0];
        const rows: PreviewCheck[] = [];
        if (f.isWorkingMaterial) rows.push({ id: 'type', label: words.type, detail: words.workingMaterial(f.typeName), tone: 'warn' });
        rows.push(channel
            ? { id: 'channel', label: words.channel, detail: words.channelOk(channel.username ? '@' + channel.username : channel.title), tone: 'ok' }
            : { id: 'channel', label: words.channel, detail: words.channelMissing, tone: 'warn' });
        if (tg) {
            const over = this.issues().some(i => i.code === 'too-long' && i.blocking);
            rows.push({
                id: 'length', label: words.length,
                detail: words.lengthDetail(tg.messageCount, tg.characters, tg.maxCharactersPerMessage),
                tone: over ? 'warn' : 'ok',
            });
            const media = tg.messages.reduce((n, m) => n + m.mediaCount, 0);
            const mediaOver = this.issues().some(i => i.code === 'too-many-media' && i.blocking);
            rows.push(media
                ? { id: 'media', label: words.media, detail: words.mediaDetail(media, tg.maxMediaPerMessage), tone: mediaOver ? 'warn' : 'ok' }
                : { id: 'media', label: words.media, detail: words.mediaNone, tone: 'muted' });
            rows.push(tg.buttons.length
                ? { id: 'cta', label: words.cta, detail: words.ctaDetail(tg.buttons.length), tone: 'ok' }
                : { id: 'cta', label: words.cta, detail: words.ctaNone, tone: 'muted' });
        } else {
            rows.push({ id: 'length', label: words.length, detail: words.notChecked, tone: 'muted' });
        }
        for (const issue of this.issues()) {
            if (issue.code === 'too-long' || issue.code === 'too-many-media') continue;
            rows.push({ id: `issue-${issue.code}`, label: words.compatibility, detail: this.issueText(issue), tone: 'warn' });
        }
        rows.push(f.scheduled
            ? { id: 'schedule', label: words.schedule, detail: this.when(f.scheduled.scheduledAtUtc), tone: 'ok' }
            : { id: 'schedule', label: words.schedule, detail: words.scheduleNone, tone: 'muted' });
        rows.push(...this.otherLanguageRows(code => words.otherCopy(code)));
        rows.push(this.linkRow());
        return rows;
    }

    private issueText(issue: PublishIssue): string {
        const texts = this.t().editor.exportModal.issues as Record<string, unknown>;
        const entry = texts[issue.code];
        if (typeof entry === 'function') {
            return (entry as (a: string, b: string) => string)(issue.actual.toLocaleString(), issue.limit.toLocaleString());
        }
        return typeof entry === 'string' ? entry : issue.code;
    }

    private when(iso: string): string {
        const date = new Date(iso);
        if (!Number.isFinite(date.getTime())) return iso;
        return date.toLocaleString(undefined, { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
    }
}

function fileName(path: string): string {
    return path.split('/').pop() || path;
}

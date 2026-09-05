import { NO_ERRORS_SCHEMA, Type, signal, type WritableSignal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EditorComponent } from './editor.component';
import { AuthService } from '../core/auth.service';
import { AssetsService } from '../core/assets.service';
import { BillingService } from '../core/billing.service';
import { ChannelsService } from '../core/channels.service';
import { CommentsService } from '../core/comments.service';
import { CurrentProjectService } from '../core/current-project.service';
import { DraftsService } from '../core/drafts.service';
import { FormPresetsService } from '../core/form-presets.service';
import { GlossaryService } from '../core/glossary.service';
import { en } from '../core/i18n/en';
import { LocaleService } from '../core/i18n/locale.service';
import { LinksService } from '../core/links.service';
import { PostsService } from '../core/posts.service';
import { PresetsService } from '../core/presets.service';
import { PreviewService } from '../core/preview.service';
import { ProjectsService } from '../core/projects.service';
import { PublishService } from '../core/publish.service';
import { TagUsageService } from '../core/tag-usage.service';
import { AppearanceService, DEFAULT_APPEARANCE } from '../core/appearance.service';
import { ZonedDatePipe } from '../shared/zoned-date.pipe';

describe('editor UI contract', () => {
    let fixture: ComponentFixture<EditorComponent>;
    let host: HTMLElement;
    let currentProjectId: WritableSignal<string>;

    beforeEach(() => {
        vi.spyOn(EditorComponent.prototype, 'ngAfterViewInit').mockResolvedValue();

        const emptyApi = {};
        const routeParams = convertToParamMap({});
        currentProjectId = signal('');
        TestBed.configureTestingModule({
            imports: [EditorComponent],
            providers: [
                { provide: AuthService, useValue: { indieDev: () => false, hasAiPlan: () => false, blogUrl: () => null } },
                { provide: AppearanceService, useValue: { prefs: signal(DEFAULT_APPEARANCE) } },
                { provide: LocaleService, useValue: { t: signal(en) } },
                { provide: ActivatedRoute, useValue: { queryParamMap: of(routeParams), snapshot: { queryParamMap: routeParams } } },
                { provide: Router, useValue: { navigate: vi.fn() } },
                { provide: CurrentProjectService, useValue: { id: currentProjectId, name: signal('') } },
                ...([
                    DraftsService, FormPresetsService, PresetsService, CommentsService, AssetsService,
                    TagUsageService, PreviewService, PublishService, BillingService, LinksService,
                    GlossaryService, PostsService, ChannelsService, ProjectsService,
                ] as Type<unknown>[]).map(token => ({ provide: token, useValue: emptyApi })),
            ],
        });
        TestBed.overrideComponent(EditorComponent, {
            set: {
                imports: [FormsModule, NgTemplateOutlet, ZonedDatePipe],
                schemas: [NO_ERRORS_SCHEMA],
            },
        });

        fixture = TestBed.createComponent(EditorComponent);
        fixture.detectChanges();
        host = fixture.nativeElement as HTMLElement;
    });

    afterEach(() => {
        fixture.destroy();
        vi.restoreAllMocks();
    });

    it('exposes formatting, alignment and block selection through aria-pressed', () => {
        const textButtons = host.querySelectorAll('[data-tb-group="text"] button');
        expect(textButtons).toHaveLength(6);
        expect([...textButtons].every(button => button.hasAttribute('aria-pressed'))).toBe(true);

        const alignmentButtons = host.querySelectorAll('.tb-more-menu button[aria-pressed]');
        expect(alignmentButtons).toHaveLength(4);

        const blockTypeButtons = host.querySelectorAll('.tb-lead .block-menu button');
        expect(blockTypeButtons).toHaveLength(7);
        expect([...blockTypeButtons].every(button => button.hasAttribute('aria-pressed'))).toBe(true);

        for (const group of ['lists', 'blocks']) {
            const statefulButtons = host.querySelectorAll(`[data-tb-group="${group}"] button[aria-pressed]`);
            expect(statefulButtons.length).toBeGreaterThan(0);
        }
    });

    it('keeps Undo, Redo and the inspector toggle in the fitted Write strip', () => {
        const strip = host.querySelector('.strip')!;
        expect(strip.querySelector('app-icon[name="arrow-u-up-left"]')).toBeTruthy();
        expect(strip.querySelector('app-icon[name="arrow-u-up-right"]')).toBeTruthy();

        const paneToggle = strip.querySelector('button[aria-controls="editor-inspector"]')!;
        expect(paneToggle.getAttribute('aria-label')).toBe('Details');
        expect(paneToggle.getAttribute('aria-pressed')).toBe('false');
        expect(host.querySelector('[title-actions]')).toBeNull();
    });

    it('puts History in the Write footer instead of an overflow menu', () => {
        const history = [...host.querySelectorAll('.frame-back button')]
            .find(button => button.textContent?.trim() === 'History');
        expect(history).toBeTruthy();
        expect(host.querySelector('button[aria-label="More actions"]')).toBeNull();
    });

    it('names the project before its kind and document count in the frame kicker', () => {
        currentProjectId.set('p1');
        fixture.componentInstance.projectSummaries.set([{
            id: 'p1', name: 'Cedar Quest', description: '', projectType: 'blog', coverUrl: null,
            createdAt: '2026-09-01T00:00:00Z', archivedAt: null, documentCount: 23,
            openTaskCount: 0, assetCount: 0, buildCount: 0, latestBuildVersion: null, lastPublishedAt: null, lastActivityAt: '2026-09-01T00:00:00Z',
        }]);

        expect(fixture.componentInstance.frameKicker())
            .toBe(`Cedar Quest · ${en.projects.projectTypes.blog.name} · ${en.editor.frame.documents(23)}`);
    });

    it('lets narrow footer buttons stack and wrap their translated labels', () => {
        const css = (EditorComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('')
            .replace(/\[_ng(?:content|host)-%COMP%\]/g, '');
        expect(css).toMatch(/@media\s*\(max-width:\s*759px\)[\s\S]*?\.frame-back,\s*\.frame-next\s*\{[^}]*flex-direction:\s*column;[^}]*width:\s*100%;/);
        expect(css).toMatch(/\.frame-back\s+\.btn,\s*\.frame-next\s+\.btn\s*\{[^}]*width:\s*100%;[^}]*white-space:\s*normal;/);
    });

    it('keeps the publish workspace columns shrink-safe and stacks them before they clip', () => {
        const css = (EditorComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('')
            .replace(/\[_ng(?:content|host)-%COMP%\]/g, '');
        expect(css).toMatch(/\.pub-columns\s*\{[^}]*display:\s*grid;[^}]*grid-template-columns:\s*minmax\(var\(--pane-list-min\),\s*var\(--pane-list-max\)\)\s*minmax\(0,\s*1fr\)\s*minmax\(var\(--pane-inspector-min\),\s*var\(--pane-inspector-max\)\);/s);
        expect(css).toMatch(/\.pub-columns\s*>\s*\*\s*\{\s*min-width:\s*0;\s*\}/);
        expect(css).toMatch(/@media\s*\(max-width:\s*1320px\)\s*\{[\s\S]*?\.pub-columns\s*\{\s*grid-template-columns:\s*minmax\(0,\s*1fr\);\s*\}/);
    });

    it('includes the sheet gutters in its available worktop width', () => {
        const css = (EditorComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('')
            .replace(/\[_ng(?:content|host)-%COMP%\]/g, '');
        expect(css).toMatch(/\.sheet-column\s*\{[^}]*width:\s*100%;[^}]*box-sizing:\s*border-box;/s);
    });

    it('lets the shared main-and-inspector workspace own the editor columns', () => {
        const css = (EditorComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles.join('')
            .replace(/\[_ng(?:content|host)-%COMP%\]/g, '');
        expect(css).toMatch(/\.main:not\(\.split-workspace\)\s*\{\s*grid-template-columns:\s*minmax\(0,\s*1fr\);\s*\}/);
        expect(css).not.toMatch(/\.main\s*\{[^}]*grid-template-columns:/s);
    });
});

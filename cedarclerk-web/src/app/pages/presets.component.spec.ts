import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PresetsComponent } from './presets.component';
import { Preset, PresetsService } from '../core/presets.service';
import { LocaleService } from '../core/i18n/locale.service';
import { en } from '../core/i18n/en';

class PresetsStub {
    readonly data: Record<'document' | 'project' | 'export', Preset[]> = {
        document: [], project: [], export: [],
    };

    list(kind: 'document' | 'project' | 'export') { return Promise.resolve(this.data[kind]); }
    create() { return Promise.resolve({}); }
    update() { return Promise.resolve({}); }
    remove() { return Promise.resolve(); }
}

describe('preset manager layout and choices', () => {
    let fixture: ComponentFixture<PresetsComponent>;
    const page = () => fixture.componentInstance;
    const el = () => fixture.nativeElement as HTMLElement;

    async function settle() {
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
    }

    beforeEach(async () => {
        TestBed.configureTestingModule({
            providers: [
                { provide: PresetsService, useClass: PresetsStub },
                { provide: LocaleService, useValue: { t: () => en } },
            ],
        });
        fixture = TestBed.createComponent(PresetsComponent);
        await settle();
    });

    const buttonsNamed = (name: string) => [...el().querySelectorAll('app-button')]
        .filter(button => button.textContent?.trim() === name);

    it('uses the form measure and offers one creation action for each state', () => {
        expect(el().querySelector('.page')?.getAttribute('data-layout')).toBe('form');
        expect(el().querySelectorAll('app-empty-state')).toHaveLength(1);
        expect(buttonsNamed(en.presets.newPreset)).toHaveLength(1);

        page().startNew();
        fixture.detectChanges();
        expect(el().querySelector('.pr-form')).not.toBeNull();
        expect(buttonsNamed(en.presets.newPreset)).toHaveLength(0);

        page().cancel();
        page().presets.set([{
            id: 'p1', kind: 'document', name: 'Postmortem', sortOrder: 0,
            configJson: JSON.stringify({ baseType: 'post', icon: 'newspaper', headings: [] }),
        }]);
        fixture.detectChanges();
        expect(buttonsNamed(en.presets.newPreset)).toHaveLength(1);
        page().startNew();
        fixture.detectChanges();
        expect(buttonsNamed(en.presets.newPreset)).toHaveLength(0);
    });

    it('exposes document selection to assistive technology and marks it beyond colour', () => {
        page().startNew();
        fixture.detectChanges();

        const choices = [...el().querySelectorAll<HTMLButtonElement>('.pr-type-btn')];
        expect(choices[0].getAttribute('aria-pressed')).toBe('true');
        expect(choices[0].querySelector('app-icon.pr-choice-check')).not.toBeNull();
        expect(choices[1].getAttribute('aria-pressed')).toBe('false');
        expect(choices[1].querySelector('app-icon.pr-choice-check')).toBeNull();

        const save = [...el().querySelectorAll<HTMLButtonElement>('app-button button')]
            .find(button => button.textContent?.trim() === en.common.save)!;
        expect(save.disabled).toBe(true);
        page().form.update(form => ({ ...form, name: 'A useful start' }));
        fixture.detectChanges();
        expect(save.disabled).toBe(false);
    });

    it('lets the nearest surface set every choice hit target', () => {
        page().startNew();
        fixture.detectChanges();

        const styles = [...document.querySelectorAll('style')]
            .map(style => style.textContent ?? '')
            .filter(css => css.includes('.pr-type-btn'))
            .join('\n');
        expect(styles).toMatch(/\.pr-type-btn[^{}]*\{[^}]*min-height:\s*var\(--hit-surface,\s*var\(--hit-target\)\)/s);
    });

    it('names the project-derived starter instead of hiding it behind an opaque default', () => {
        page().kind.set('project');
        page().startNew();
        fixture.detectChanges();

        expect(el().textContent).toContain(en.presets.project.documentTypeDefault(en.projects.docTypes.design.name));
        page().pickProjectType('product');
        fixture.detectChanges();
        expect(el().textContent).toContain(en.presets.project.documentTypeDefault(en.projects.docTypes.changelog.name));
    });

    it('shows language codes with endonyms and marks selected languages', () => {
        page().kind.set('export');
        page().startNew();
        fixture.detectChanges();

        const ukrainian = [...el().querySelectorAll<HTMLButtonElement>('.pr-type-btn')]
            .find(button => button.textContent?.includes('UK · Українська'))!;
        expect(ukrainian.getAttribute('aria-pressed')).toBe('false');
        ukrainian.click();
        fixture.detectChanges();
        expect(ukrainian.getAttribute('aria-pressed')).toBe('true');
        expect(ukrainian.querySelector('app-icon.pr-choice-check')).not.toBeNull();
    });
});

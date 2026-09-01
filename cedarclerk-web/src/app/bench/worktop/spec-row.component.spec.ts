import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { StampBadgeComponent } from '../display/stamp-badge.component';
import { SpecRowComponent, SpecScope } from './spec-row.component';

@Component({
    imports: [SpecRowComponent, StampBadgeComponent],
    template: `
        <app-spec-row label="Тип" value="игнорируется">
            <app-stamp-badge tone="ink" [rotate]="-1">пост</app-stamp-badge>
        </app-spec-row>
    `,
})
class ProjectedHost {}

// The inspector shows the selected object, or the document when nothing is selected — never both
// at once. A sheet built from these rows carries its subject on every line, which is what makes
// the rule assertable at all.
@Component({
    imports: [SpecRowComponent],
    template: `
        <app-spec-row label="Файл" value="fog_pass_02.png" scope="selection" field />
        <app-spec-row label="Разрешение" value="1920 × 1080" scope="selection" />
        <app-spec-row label="Описание" value="не заполнено" [scope]="strayScope()" field warn />
    `,
})
class InspectorHost {
    readonly strayScope = signal<SpecScope>('selection');
}

/** Every rule the runner let into the document that mentions a marker, as plain text. */
function styleText(marker: string): string {
    const inline = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '');
    const adopted = Array.from(document.adoptedStyleSheets ?? []).map(
        s => Array.from(s.cssRules).map(r => r.cssText).join('\n'));
    const hits = [...inline, ...adopted].filter(t => t.includes(marker));
    expect(hits.length, `no stylesheet carrying "${marker}" reached the document`).toBeGreaterThan(0);
    return hits.join('\n');
}

function selectorsDeclaring(css: string, property: RegExp): string[] {
    return [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
        .filter(m => property.test(m[2]))
        .map(m => m[1].trim());
}

describe('SpecRowComponent', () => {
    function create() {
        const fixture = TestBed.createComponent(SpecRowComponent);
        fixture.componentRef.setInput('label', 'Файл');
        fixture.detectChanges();
        return fixture;
    }

    it('is chrome — the inspector lives in a dock, not on the sheet', () => {
        expect(create().nativeElement.getAttribute('data-surface')).toBe('chrome');
    });

    it('rules a label against a mono value', () => {
        const fixture = create();
        fixture.componentRef.setInput('value', 'fog_pass_02.png');
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        expect(el.querySelector('.label')!.textContent!.trim()).toBe('Файл');
        expect(el.querySelector('.text')!.textContent!.trim()).toBe('fog_pass_02.png');
    });

    it('carries the field and warn variants as host classes so the two can combine', () => {
        const fixture = create();
        const el = fixture.nativeElement as HTMLElement;
        expect(el.classList.contains('field')).toBe(false);
        expect(el.classList.contains('warn')).toBe(false);

        fixture.componentRef.setInput('field', true);
        fixture.componentRef.setInput('warn', true);
        fixture.detectChanges();
        expect(el.classList.contains('field')).toBe(true);
        expect(el.classList.contains('warn')).toBe(true);
    });

    // The defect ADR-238 clause 1 names: drawn on the ink, the box appeared on exactly the rows
    // that could not be typed in and vanished on the rows that could, because a row hosting a
    // control has no ink to draw it on. Drawn on the value slot, the projected control *is* the
    // field. The runner resolves neither var() nor inheritance, so the rule is read, not measured.
    it('draws the field box around the value slot, never around the ink inside it', () => {
        const fixture = create();
        fixture.componentRef.setInput('field', true);
        fixture.detectChanges();

        const value = (fixture.nativeElement as HTMLElement).querySelector('.value')!;
        const scope = value.getAttributeNames().find(n => n.startsWith('_ngcontent-'))!
            .replace('_ngcontent-', '');
        const own = (property: RegExp) =>
            selectorsDeclaring(styleText(scope), property).filter(s => s.includes(scope));

        const boxed = own(/box-shadow\s*:\s*var\(--shadow-field-inset\)/);
        expect(boxed.length, 'no field box rule found — the marker or the class names moved')
            .toBeGreaterThan(0);
        for (const selector of boxed) {
            expect(selector.includes('.value'), `${selector} boxes something other than the value`).toBe(true);
            expect(/\.text\b/.test(selector), `${selector} still boxes the ink`).toBe(false);
        }

        // The warning is drawn on that same edge, so the two variants stay one box.
        for (const selector of own(/border\s*:\s*1px dashed var\(--danger\)/)) {
            expect(/\.text\b/.test(selector), `${selector} draws the warn edge on the ink`).toBe(false);
        }
    });

    it('leaves the label column to the type until a consumer names a width', () => {
        const fixture = create();
        const el = fixture.nativeElement as HTMLElement;
        expect(el.style.getPropertyValue('--spec-label-w')).toBe('');

        fixture.componentRef.setInput('labelWidth', 'var(--bench-tool-w)');
        fixture.detectChanges();
        expect(el.style.getPropertyValue('--spec-label-w')).toBe('var(--bench-tool-w)');
    });

    it('gives the value slot to projected content, and drops the text it would have drawn', () => {
        const fixture = TestBed.createComponent(ProjectedHost);
        fixture.detectChanges();
        const row = fixture.nativeElement.querySelector('app-spec-row') as HTMLElement;
        expect(row.querySelector('app-stamp-badge')).not.toBeNull();
        expect(row.querySelector('.text')).toBeNull();
        expect(row.textContent).not.toContain('игнорируется');
    });

    it('defaults to the document — the subject the inspector falls back to', () => {
        expect(create().nativeElement.getAttribute('data-scope')).toBe('document');
    });

    it('makes "one subject at a time" a thing that can be checked', () => {
        const fixture = TestBed.createComponent(InspectorHost);
        fixture.detectChanges();
        const scopes = () => new Set(
            [...fixture.nativeElement.querySelectorAll('app-spec-row')]
                .map((r: Element) => r.getAttribute('data-scope')));

        expect(scopes()).toEqual(new Set(['selection']));

        // The failure the rule exists to stop: the toolbar claims a selection while one row still
        // describes the document. The sheet now says so out loud.
        fixture.componentInstance.strayScope.set('document');
        fixture.detectChanges();
        expect(scopes().size).toBe(2);
    });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DialoguesService } from '../core/dialogues.service';
import { en } from '../core/i18n/en';
import { ProjectDialogueComponent } from './project-dialogue.component';

class FakeDialogues {
    async save(_id: string, input: { graphJson?: string }) {
        return {
            id: 'd1', name: 'Opening', projectId: 'p1',
            graphJson: input.graphJson ?? '[]',
            createdAt: '2026-08-01T00:00:00Z', updatedAt: '2026-08-01T00:00:00Z',
        };
    }
}

describe('project dialogue', () => {
    let fixture: ComponentFixture<ProjectDialogueComponent>;

    beforeEach(() => {
        localStorage.setItem('cedar-ui-lang', 'en');
        TestBed.configureTestingModule({
            providers: [
                provideRouter([]),
                { provide: DialoguesService, useClass: FakeDialogues },
                {
                    provide: ActivatedRoute,
                    useValue: { paramMap: of(convertToParamMap({})) },
                },
            ],
        });
        fixture = TestBed.createComponent(ProjectDialogueComponent);
        fixture.componentInstance.loading.set(false);
        fixture.detectChanges();
    });

    afterEach(() => localStorage.removeItem('cedar-ui-lang'));

    it('uses the operational pane contract and shows one visible node-title label', () => {
        const component = fixture.componentInstance;
        component.nodes.set([{
            id: 'n1', title: 'Start', x: 80, y: 80,
            body: 'Marty: Hello #line:abc12345',
        }]);
        component.selectedId.set('n1');
        fixture.detectChanges();

        const root = fixture.nativeElement as HTMLElement;
        expect(root.querySelector('.page')?.getAttribute('data-layout')).toBe('operational');
        const nodePanel = root.querySelector(`section[aria-label="${en.projects.dialogues.nodeTitle}"]`)!;
        const titleLabels = [...nodePanel.querySelectorAll('label')]
            .filter(label => label.textContent?.trim() === en.projects.dialogues.nodeTitle);
        expect(titleLabels.length).toBe(1);
        expect(nodePanel.querySelector('#dialogue-node-body')?.getAttribute('aria-describedby'))
            .toContain('dialogue-line-id-warning');
        expect(nodePanel.querySelector('.line-id-warning')?.textContent)
            .toContain('#line:');
    });

    it('offers only supported languages and serializes a canonical multi-selection', () => {
        const root = fixture.nativeElement as HTMLElement;
        const options = [...root.querySelectorAll('.language-option')] as HTMLLabelElement[];
        expect(options.map(option => option.querySelector('.language-code')?.textContent?.trim()))
            .toEqual(['RU', 'EN', 'DE', 'FR', 'ES', 'JA', 'UK', 'BE', 'KA']);

        for (const code of ['JA', 'EN']) {
            const option = options.find(item => item.querySelector('.language-code')?.textContent?.trim() === code)!;
            const input = option.querySelector('input')!;
            input.checked = true;
            input.dispatchEvent(new Event('change'));
        }
        fixture.detectChanges();

        expect(fixture.componentInstance.sheetLanguageQuery()).toBe('en,ja');
        expect(options.find(item => item.textContent?.includes('EN'))?.querySelector('input')?.checked).toBe(true);
        expect(options.find(item => item.textContent?.includes('JA'))?.querySelector('input')?.checked).toBe(true);
    });
});

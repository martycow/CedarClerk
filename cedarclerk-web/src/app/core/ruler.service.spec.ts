import { TestBed } from '@angular/core/testing';
import { RulerService } from './ruler.service';

describe('RulerService', () => {
    function svc() {
        const ruler = TestBed.inject(RulerService);
        ruler.clear();
        return ruler;
    }

    it('reads as blank until a page measures something', () => {
        const ruler = svc();
        expect(ruler.label()).toBe('');
        expect(ruler.left()).toEqual([]);
        expect(ruler.right()).toEqual([]);
    });

    it('replaces the whole rule rather than adding to it', () => {
        const ruler = svc();
        ruler.publish({ label: 'Draft', left: [{ text: '3 blocks' }], right: [{ text: '412 words' }] });
        // A page that publishes only its right end is stating that its left end is empty, not
        // inheriting the last page's — the rule belongs to whatever screen is open.
        ruler.publish({ right: [{ text: 'Synced' }] });
        expect(ruler.label()).toBe('');
        expect(ruler.left()).toEqual([]);
        expect(ruler.right()).toEqual([{ text: 'Synced' }]);
    });

    it('empties on clear, which is what a page leaving the screen owes the next one', () => {
        const ruler = svc();
        ruler.publish({ label: 'Draft', right: [{ text: 'Synced' }] });
        ruler.clear();
        expect(ruler.label()).toBe('');
        expect(ruler.right()).toEqual([]);
    });
});

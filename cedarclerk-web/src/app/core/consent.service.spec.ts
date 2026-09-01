import { TestBed } from '@angular/core/testing';
import { ConsentService } from './consent.service';

// ADR-236 clause 7. The cookie is the whole contract between the SPA and the server-rendered
// landing, so what is asserted here is the spelling and the values — a mismatch would let one
// surface track while the other still asked.
describe('ConsentService', () => {
    function clearCookie() {
        document.cookie = 'cedar_consent=; path=/; max-age=0';
    }

    beforeEach(() => {
        clearCookie();
        TestBed.resetTestingModule();
    });

    afterEach(clearCookie);

    it('starts unasked when no cookie has been written', () => {
        expect(TestBed.inject(ConsentService).state()).toBe('unasked');
    });

    it('reads an existing answer rather than asking again', () => {
        document.cookie = 'cedar_consent=granted; path=/';
        expect(TestBed.inject(ConsentService).state()).toBe('granted');
    });

    it('writes the exact value the landing script looks for', () => {
        TestBed.inject(ConsentService).grant();
        expect(document.cookie).toContain('cedar_consent=granted');
    });

    it('records a refusal rather than leaving it unasked', () => {
        // The difference that matters: an unrecorded "no" brings the banner back on every visit,
        // which reads as the product ignoring the answer.
        const consent = TestBed.inject(ConsentService);
        consent.deny();

        expect(consent.state()).toBe('denied');
        expect(document.cookie).toContain('cedar_consent=denied');
    });

    it('treats an unrecognised cookie value as unasked', () => {
        document.cookie = 'cedar_consent=maybe; path=/';
        expect(TestBed.inject(ConsentService).state()).toBe('unasked');
    });
});

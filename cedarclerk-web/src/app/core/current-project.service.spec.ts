import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { CurrentProjectService } from './current-project.service';

const KEY = 'cedar-project';

describe('CurrentProjectService', () => {
    function make() {
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        return TestBed.inject(CurrentProjectService);
    }

    beforeEach(() => {
        TestBed.resetTestingModule();
        localStorage.removeItem(KEY);
    });

    it('starts empty and remembers what it is told', () => {
        const svc = make();
        expect(svc.id()).toBe('');
        svc.remember('p1', 'Cedar Quest');
        expect(svc.id()).toBe('p1');
        expect(svc.name()).toBe('Cedar Quest');
    });

    // The reason it is persisted at all: a reload must not lose which project you are working on.
    it('survives a reload', () => {
        make().remember('p1', 'Cedar Quest');
        TestBed.resetTestingModule();
        expect(make().id()).toBe('p1');
    });

    it('ignores an empty id rather than storing a blank', () => {
        const svc = make();
        svc.remember('', 'nothing');
        expect(svc.id()).toBe('');
        expect(localStorage.getItem(KEY)).toBeNull();
    });

    // A remembered id that outlived its project would become a Board hook pointing at a 404.
    it('forgets a project the account no longer has, and refreshes a renamed one', () => {
        const svc = make();
        svc.remember('p1', 'Old name');
        svc.reconcile([{ id: 'p1', name: 'New name' }]);
        expect(svc.name()).toBe('New name');

        svc.reconcile([{ id: 'p2', name: 'Something else' }]);
        expect(svc.id()).toBe('');
        expect(localStorage.getItem(KEY)).toBeNull();
    });

    // A remembered project belongs to the account that opened it.
    it('drops the memory when the session ends', () => {
        const svc = make();
        const auth = TestBed.inject(AuthService);
        auth.userEmail.set('marty@mooexe.dev');
        TestBed.tick();
        svc.remember('p1', 'Cedar Quest');

        auth.userEmail.set(null);
        TestBed.tick();
        expect(svc.id()).toBe('');
    });

    it('survives a corrupt stored value', () => {
        localStorage.setItem(KEY, '{not json');
        expect(make().id()).toBe('');
    });
});

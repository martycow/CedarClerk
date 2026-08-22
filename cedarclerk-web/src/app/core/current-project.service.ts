import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { AuthService } from './auth.service';

const KEY = 'cedar-project';

// ADR-186 — which project is open is a property of the session, not of the row on screen. The URL
// holds it on two routes out of eleven and throws it away on the rest, which is why the rail tile
// said "All Projects" nearly everywhere and the Board hook never opened a board.
//
// One writer: the shell, from the resolved route. Everything else reads.
@Injectable({ providedIn: 'root' })
export class CurrentProjectService {
    private readonly auth = inject(AuthService);

    private readonly state = signal<{ id: string; name: string }>(read());

    readonly id = computed(() => this.state().id);
    readonly name = computed(() => this.state().name);

    constructor() {
        // A remembered project belongs to the account that opened it, so the end of a session is
        // the end of the memory. Written here rather than in AuthService.logout() because a session
        // can also end by the refresh call failing, which never passes through logout at all.
        effect(() => {
            // untracked: forget() reads the state it clears, and a plain read here would make this
            // effect depend on its own writes — the shell's writer then feeds it in a loop.
            if (!this.auth.userEmail()) untracked(() => this.forget());
        });
    }

    remember(id: string, name: string): void {
        if (!id) return;
        if (this.state().id === id && this.state().name === name) return;
        this.state.set({ id, name });
        try { localStorage.setItem(KEY, JSON.stringify({ id, name })); } catch { /* private mode */ }
    }

    /** The project is gone — a deletion, or an account that no longer has it. */
    forget(): void {
        if (!this.state().id && !this.state().name) return;
        this.state.set({ id: '', name: '' });
        try { localStorage.removeItem(KEY); } catch { /* private mode */ }
    }

    /**
     * Drops the memory when the id is not among the projects the account actually has. Called with
     * the freshly fetched list — a remembered id that outlived its project would otherwise become
     * a Board hook pointing at a 404.
     */
    reconcile(projects: readonly { id: string; name: string }[]): void {
        const id = this.state().id;
        if (!id) return;
        const found = projects.find(p => p.id === id);
        if (!found) this.forget();
        else this.remember(found.id, found.name);
    }
}

function read(): { id: string; name: string } {
    try {
        const raw = localStorage.getItem(KEY);
        if (!raw) return { id: '', name: '' };
        const parsed = JSON.parse(raw) as { id?: unknown; name?: unknown };
        return typeof parsed?.id === 'string' && typeof parsed?.name === 'string'
            ? { id: parsed.id, name: parsed.name }
            : { id: '', name: '' };
    } catch {
        return { id: '', name: '' };
    }
}

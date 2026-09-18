import { Injectable, computed, signal } from '@angular/core';

export type ShellOverlay = 'feedback' | 'search' | 'debug' | 'commands';

interface LayerRecord {
    id: number;
    owner: ShellOverlay | null;
}

interface InertRecord {
    count: number;
    wasInert: boolean;
    hadAttribute: boolean;
}

export interface OverlayLayerLease {
    isTop(): boolean;
    release(): void;
}

type PeerDismiss = (activeElement: HTMLElement | null) => HTMLElement | null;

const FOCUSABLE = [
    'button:not([disabled]):not([tabindex="-1"])',
    '[href]:not([tabindex="-1"])',
    'input:not([disabled]):not([tabindex="-1"])',
    'select:not([disabled]):not([tabindex="-1"])',
    'textarea:not([disabled]):not([tabindex="-1"])',
    '[tabindex]:not([tabindex="-1"])',
].join(', ');

// One shell, one transient layer (ADR-246). Components still own their content and close
// behaviour; this service only makes their visibility mutually exclusive.
@Injectable({ providedIn: 'root' })
export class OverlayCoordinatorService {
    private readonly current = signal<ShellOverlay | null>(null);
    private readonly layers = signal<readonly LayerRecord[]>([]);
    private readonly inertRefs = new Map<HTMLElement, InertRecord>();
    private readonly peerDismissers = new Set<PeerDismiss>();
    private nextLayerId = 0;
    private shellReturnFocus: HTMLElement | null = null;

    readonly active = this.current.asReadonly();
    readonly modalOpen = computed(() => this.layers().some(layer => layer.owner === null));

    open(overlay: ShellOverlay): boolean {
        if (this.modalOpen()) return false;
        const activeElement = this.activeElement();
        const peerReturnFocus = this.dismissPeers(activeElement);
        if (this.current() === null) {
            this.shellReturnFocus = peerReturnFocus ?? activeElement;
        }
        this.current.set(overlay);
        return true;
    }

    close(overlay: ShellOverlay): void {
        if (this.current() !== overlay) return;
        this.current.set(null);
        this.restoreShellFocusWhenIdle();
    }

    toggle(overlay: ShellOverlay): boolean {
        if (this.current() === overlay) {
            this.close(overlay);
            return true;
        }
        return this.open(overlay);
    }

    registerLayer(host: HTMLElement, owner: ShellOverlay | null = null): OverlayLayerLease {
        const activeElement = this.activeElement();
        const returnFocus = this.dismissPeers(activeElement) ?? activeElement;
        if (owner === null && this.current() !== null) this.current.set(null);

        const id = ++this.nextLayerId;
        const releaseInert = this.inertOutside(host);
        this.layers.update(layers => [...layers, { id, owner }]);
        let released = false;

        return {
            isTop: () => this.layers().at(-1)?.id === id,
            release: () => {
                if (released) return;
                released = true;
                const wasTop = this.layers().at(-1)?.id === id;
                this.layers.update(layers => layers.filter(layer => layer.id !== id));
                releaseInert();
                if (owner === null && wasTop) {
                    this.restoreElement(returnFocus, this.layers().at(-1)?.id ?? null);
                    this.restoreShellFocusWhenIdle();
                }
                else if (owner !== null) this.restoreShellFocusWhenIdle();
            },
        };
    }

    registerDismissablePeer(dismiss: PeerDismiss): () => void {
        this.peerDismissers.add(dismiss);
        return () => this.peerDismissers.delete(dismiss);
    }

    dismissPopovers(): void {
        this.dismissPeers(this.activeElement());
    }

    focusFirst(container: HTMLElement, preferred?: HTMLElement | null): void {
        (preferred ?? this.focusableWithin(container)[0] ?? container).focus();
    }

    trapTab(container: HTMLElement, event: KeyboardEvent): void {
        const focusable = this.focusableWithin(container);
        if (focusable.length === 0) {
            event.preventDefault();
            container.focus();
            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = document.activeElement;
        if (!container.contains(active)) {
            event.preventDefault();
            (event.shiftKey ? last : first).focus();
        } else if (event.shiftKey && active === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && active === last) {
            event.preventDefault();
            first.focus();
        }
    }

    private focusableWithin(container: HTMLElement): HTMLElement[] {
        return [...container.querySelectorAll<HTMLElement>(FOCUSABLE)]
            .filter(element => !element.hidden && element.getAttribute('aria-hidden') !== 'true');
    }

    private activeElement(): HTMLElement | null {
        return document.activeElement instanceof HTMLElement ? document.activeElement : null;
    }

    private dismissPeers(activeElement: HTMLElement | null): HTMLElement | null {
        let returnFocus: HTMLElement | null = null;
        for (const dismiss of this.peerDismissers) {
            const candidate = dismiss(activeElement);
            returnFocus ??= candidate;
        }
        return returnFocus;
    }

    private restoreShellFocusWhenIdle(): void {
        const target = this.shellReturnFocus;
        if (!target) return;
        queueMicrotask(() => {
            if (this.current() !== null || this.layers().length > 0 || this.shellReturnFocus !== target) return;
            this.shellReturnFocus = null;
            if (target.isConnected) target.focus();
        });
    }

    private restoreElement(target: HTMLElement | null, expectedTopLayerId: number | null): void {
        if (!target) return;
        queueMicrotask(() => {
            if (this.current() !== null || (this.layers().at(-1)?.id ?? null) !== expectedTopLayerId) return;
            if (target.isConnected) target.focus();
        });
    }

    private inertOutside(host: HTMLElement): () => void {
        const acquired = new Set<HTMLElement>();
        let branch: HTMLElement | null = host;

        while (branch && branch !== document.body) {
            const parent: HTMLElement | null = branch.parentElement;
            if (!parent) break;
            for (const sibling of parent.children) {
                if (sibling === branch || !(sibling instanceof HTMLElement)
                    || sibling.matches('script, style, link')) continue;
                acquired.add(sibling);
            }
            branch = parent;
        }

        for (const element of acquired) {
            const record = this.inertRefs.get(element);
            if (record) {
                record.count++;
            } else {
                const hadAttribute = element.hasAttribute('inert');
                this.inertRefs.set(element, {
                    count: 1,
                    wasInert: element.inert === true || hadAttribute,
                    hadAttribute,
                });
                element.inert = true;
                element.setAttribute('inert', '');
            }
        }

        return () => {
            for (const element of acquired) {
                const record = this.inertRefs.get(element);
                if (!record) continue;
                record.count--;
                if (record.count > 0) continue;
                element.inert = record.wasInert;
                if (record.hadAttribute) element.setAttribute('inert', '');
                else element.removeAttribute('inert');
                this.inertRefs.delete(element);
            }
        };
    }
}

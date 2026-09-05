import { ApplicationRef, EnvironmentInjector, Injectable, OnDestroy, createComponent, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { NavigationStart, Router } from '@angular/router';
import { LocaleService } from './i18n/locale.service';
import { ConfirmationDialogComponent } from '../shared/confirmation-dialog.component';

export interface ConfirmationOptions {
    message: string;
    title?: string;
    confirmLabel?: string;
}

@Injectable({ providedIn: 'root' })
export class ConfirmationService implements OnDestroy {
    private readonly app = inject(ApplicationRef);
    private readonly environmentInjector = inject(EnvironmentInjector);
    private readonly document = inject(DOCUMENT);
    private readonly t = inject(LocaleService).t;
    private finish?: (accepted: boolean) => void;
    private readonly navigation = inject(Router).events.subscribe(event => {
        if (event instanceof NavigationStart) this.finish?.(false);
    });

    confirm(options: ConfirmationOptions | string): Promise<boolean> {
        if (this.finish) return Promise.resolve(false);
        const settings = typeof options === 'string' ? { message: options } : options;
        const host = this.document.createElement('app-confirmation-dialog');
        // An editing modal may already make app-root inert; the confirmation must be its peer.
        this.document.body.appendChild(host);
        const component = createComponent(ConfirmationDialogComponent, {
            environmentInjector: this.environmentInjector, hostElement: host,
        });
        component.setInput('title', settings.title ?? this.t().common.confirmAction);
        component.setInput('message', settings.message);
        component.setInput('confirmLabel', settings.confirmLabel ?? this.t().common.delete);
        return new Promise(resolve => {
            this.finish = accepted => {
                this.finish = undefined;
                this.app.detachView(component.hostView);
                component.destroy();
                host.remove();
                resolve(accepted);
            };
            component.instance.answered.subscribe(accepted => this.finish?.(accepted));
            this.app.attachView(component.hostView);
            component.changeDetectorRef.detectChanges();
        });
    }

    ngOnDestroy(): void {
        this.navigation.unsubscribe();
        this.finish?.(false);
    }
}

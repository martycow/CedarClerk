import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { LocaleService } from '../core/i18n/locale.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import { LangSwitchComponent } from '../shared/lang-switch.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { IconComponent } from '../shared/icon.component';
import { InputComponent } from '../bench/forms/input.component';
import { PaperCardComponent } from '../bench/display/paper-card.component';

// T-328 — the mandatory first stop after registration: who the author is, where they live on the
// web, and (T-329) the blog address they already own — shown, never assigned here. The guard in
// auth.guard.ts routes every account without a display name to this door.
@Component({
    selector: 'app-onboarding',
    imports: [
        CedarLogoComponent, LangSwitchComponent,
        ButtonComponent, InputComponent, PaperCardComponent, IconComponent,
    ],
    templateUrl: 'onboarding.component.html',
    styleUrls: ['onboarding.component.css'],
})
export class OnboardingComponent {
    auth = inject(AuthService);
    theme = inject(ThemeService);
    private router = inject(Router);
    private route = inject(ActivatedRoute);
    t = inject(LocaleService).t;

    displayName = '';
    profileUrl = '';
    location = '';
    busy = signal(false);
    error = signal('');

    async submit() {
        const name = this.displayName.trim();
        if (!name) {
            this.error.set(this.t().onboarding.nameRequired);
            return;
        }
        this.busy.set(true);
        this.error.set('');
        try {
            // The same whole-profile POST Settings sends: a fresh account's other fields are
            // empty, and passing the current values keeps an old account's slots intact.
            await this.auth.saveProfile({
                authorDisplayName: name,
                profileUrl: this.profileUrl.trim(),
                profileLocation: this.location.trim(),
                headerSlot1Type: this.auth.headerSlot1Type(),
                headerSlot2Type: this.auth.headerSlot2Type(),
                headerSlot3Type: this.auth.headerSlot3Type(),
            });
            const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
            await this.router.navigateByUrl(returnUrl && returnUrl !== '/onboarding' ? returnUrl : '/');
        } catch {
            this.error.set(this.t().onboarding.failed);
        } finally {
            this.busy.set(false);
        }
    }
}

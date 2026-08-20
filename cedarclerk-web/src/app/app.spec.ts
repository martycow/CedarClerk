import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  // The root draws one thing now. Everything that used to hang off it — the console, the rule —
  // belongs to the shell route, and bench-shell.component.spec.ts is where those are asserted.
  it('renders nothing but the outlet', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
    expect(compiled.querySelector('app-debug-console')).toBeFalsy();
    expect(compiled.querySelector('app-ruler-bar')).toBeFalsy();
  });
});

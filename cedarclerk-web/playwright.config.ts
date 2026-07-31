import { defineConfig, devices } from '@playwright/test';

// Phase 10 smoke suite (ADR-070). Deliberately small and serial: it exists to say "this broke"
// during the Phase 11 redesign, not to be an exhaustive functional suite.
//
// The .NET server is NOT started here — `Scripts/e2e.ps1` owns it, because the run needs a fresh
// scratch database, a seeded account, and a restart in between (the admin bootstrap only grants
// rights at startup, so the account has to exist before the second start). Playwright starts only
// the Angular dev server, which proxies /api and /media to :8080 the same way `ng serve` does in
// ordinary development.
export default defineConfig({
    testDir: './e2e',
    // One worker on purpose: every test shares one server and one SQLite file, and a parallel run
    // would be testing the test harness rather than the app.
    fullyParallel: false,
    workers: 1,
    forbidOnly: !!process.env.CI,
    retries: 0,
    timeout: 45_000,
    expect: { timeout: 10_000 },
    reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
    use: {
        baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:4200',
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure',
        video: 'off',
    },
    projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
    webServer: {
        command: 'npm run start -- --port 4200',
        url: 'http://localhost:4200',
        reuseExistingServer: true,
        timeout: 180_000,
        stdout: 'ignore',
        stderr: 'pipe',
    },
});

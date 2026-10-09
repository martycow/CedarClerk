// Cedar Clerk desktop shell (ADR-104, reshaped by ADR-117). Full rationale: docs/tech/DESKTOP.md.
//
// What this is: a window onto cedarclerk.app, plus a local process that can read this machine's
// disk. There is exactly one database and it is in the cloud — the shell keeps no data of its own.
//
// Why it exists at all: the asset index describes a folder on this machine (ADR-107), and only a
// process running here can see one. Everything else the desktop gains is a side effect.
//
// What changed with ADR-117: the sidecar used to BE a Cedar Clerk, with its own SQLite, its own
// accounts and its own copy of everything. That is what made "the same email is a different account"
// true, which is the complaint this shell was rebuilt to answer. Now it is an agent: it walks folders
// and renders thumbnails, and the page uploads what it finds.
const electron = require('electron');

// Electron's binary is also a Node runtime, and ELECTRON_RUN_AS_NODE switches it over — a variable
// several editors set in their integrated terminals. When it is on, `require('electron')` hands
// back the path to the binary instead of the API, every name below is undefined, and the first one
// used throws a TypeError that says nothing about why. `npm start` goes through start.js, which
// strips the variable; this is for anyone running `electron .` by hand.
if (typeof electron === 'string') {
    console.error([
        'Cedar Clerk: Electron is running as plain Node, so its API is unavailable.',
        'ELECTRON_RUN_AS_NODE is set in this environment (some editors set it in their terminals).',
        'Use `npm start`, which clears it, or unset the variable and try again.',
    ].join('\n'));
    process.exit(1);
}

const { app, BrowserWindow, dialog, ipcMain, shell } = electron;
const { autoUpdater } = require('electron-updater');
const { spawn, spawnSync } = require('node:child_process');
const crypto = require('node:crypto');
const net = require('node:net');
const os = require('node:os');
const path = require('node:path');
const fs = require('node:fs');

const SHELL_VERSION = require('./package.json').version;
const HEALTH_TIMEOUT_MS = 60_000;
const HEALTH_POLL_MS = 250;
// A session here is hours, not days, so this is mostly about the copy somebody leaves open all
// week. The check at launch is the one that matters.
const UPDATE_CHECK_INTERVAL_MS = 4 * 60 * 60 * 1000;

// The one installation this shell is a window onto. A constant rather than configuration: it decides
// where the data is, where identity comes from AND which origin gets the filesystem bridge, and a
// value that decides all three has no business being editable by whatever last wrote a config file.
const UPSTREAM = 'https://cedarclerk.app';
// Never "/". The landing page answers that path for anyone without a session cookie, and a desktop app
// opening on a pricing table was the first thing Marty saw on the first real launch (10.08.2026).
// Any other path falls through to the SPA, whose own guard sends a signed-out visitor to /login.
const START_PATH = '/projects';

// ADR-327 — how a session made in the system browser comes back to this window.
const SIGN_IN_SCHEME = 'cedarclerk';
const SIGN_IN_PROVIDERS = new Set(['google', 'discord']);
const SIGN_IN_WINDOW_MS = 10 * 60 * 1000;

let agentProcess = null;
let agentOrigin = null;
let agentToken = null;
let mainWindow = null;
let pendingSignIn = null;

// Windows and Linux deliver a cedarclerk:// link by starting a second copy with it in argv; the lock
// is what routes that to this one. Packaged builds only: a development run shares the installed
// app's lock name, and `npm start` exiting silently beside it reads as a broken checkout.
const isPrimaryInstance = !app.isPackaged || app.requestSingleInstanceLock();

/** Ask the OS for a port nobody is using. Never 8080: a local dev server or the tunnel-fixed
 *  production port would collide, and a collision here reads as "the app won't start". */
function findFreePort() {
    return new Promise((resolve, reject) => {
        const probe = net.createServer();
        probe.unref();
        probe.on('error', reject);
        probe.listen(0, '127.0.0.1', () => {
            const { port } = probe.address();
            probe.close(() => resolve(port));
        });
    });
}

/**
 * Where this shell keeps its own few files. No longer a database: since ADR-117 the only things here
 * are machine.json and update.log.
 *
 * A cedar.db from before ADR-117 may still be sitting in this folder. Nothing opens it any more, and
 * nothing deletes it either — it is Marty's data, and removing it would be a decision this shell has
 * no business making quietly.
 */
function dataDirectory() {
    const dir = path.join(app.getPath('appData'), 'CedarClerk');
    fs.mkdirSync(dir, { recursive: true });
    return dir;
}

/**
 * This machine's identity, generated once and kept (ADR-117).
 *
 * It is what lets every asset screen tell a file from a fingerprint of a file: the cloud records which
 * machine indexed a folder, and a client compares that against this id. A browser has no id at all,
 * so it always gets the fingerprint answer — which is the truth, not a fallback.
 *
 * The hostname is refreshed on every launch because a renamed machine should say its new name; the id
 * never changes, because a renamed machine is still the machine holding the files.
 */
function machineIdentity() {
    const file = path.join(dataDirectory(), 'machine.json');
    let id = null;
    try {
        id = JSON.parse(fs.readFileSync(file, 'utf8')).id ?? null;
    } catch {
        // No file yet, or one written by a older/broken run. Either way a fresh id is correct.
    }
    if (typeof id !== 'string' || id.length === 0) id = crypto.randomUUID();

    const identity = { id, name: os.hostname() };
    try {
        fs.writeFileSync(file, JSON.stringify(identity, null, 2));
    } catch {
        // An id that cannot be persisted still works for this session; it just means the next launch
        // looks like a different machine, which shows up as a re-index rather than as data loss.
    }
    return identity;
}

const machine = { id: null, name: null };

/**
 * Folders the human has picked on this machine, remembered across launches.
 *
 * The grant mechanism (ADR-117) exists so a compromised renderer cannot name an arbitrary path — disk
 * access has to begin with a gesture. But grants held only for one launch would mean re-picking the
 * folder after every restart just to re-scan, which is friction with no safety in it: the gesture was
 * made, it just happened last week.
 *
 * So the list is persisted **here**, next to machine.json, and never taken from the server. That
 * distinction is the whole security argument: re-granting what the *cloud* recorded would let a
 * compromised page write any path into the project's root and then ask for it back, and the grant
 * would guard nothing. Re-granting what this file remembers cannot be influenced from the page at all.
 *
 * The residual cost, stated rather than hidden: a folder picked once stays readable to this app until
 * the file is edited. That is the same bargain a browser strikes with persisted directory permissions.
 */
function grantedRootsFile() {
    return path.join(dataDirectory(), 'granted-folders.json');
}

function loadGrantedRoots() {
    try {
        const roots = JSON.parse(fs.readFileSync(grantedRootsFile(), 'utf8'));
        return Array.isArray(roots) ? roots.filter(r => typeof r === 'string') : [];
    } catch {
        return [];
    }
}

function rememberGrantedRoot(root) {
    const roots = loadGrantedRoots();
    if (roots.includes(root)) return;
    try {
        fs.writeFileSync(grantedRootsFile(), JSON.stringify([root, ...roots].slice(0, 50), null, 2));
    } catch {
        // A grant that cannot be persisted still works for this session — the next launch just asks
        // for the folder again, which is a nuisance rather than a failure.
    }
}

/** Hands the remembered folders to a freshly started agent. */
async function restoreGrants() {
    for (const root of loadGrantedRoots()) {
        // A folder that has since been deleted or is on an unplugged drive is skipped quietly: the
        // agent rejects it, and the asset screen already knows how to say "not found at path".
        try {
            await agentFetch('/agent/grant', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ root }),
            });
        } catch {
            // An agent that cannot be reached at all is reported by startAgent, not here.
        }
    }
}

function agentExecutable() {
    // Packaged: resources/server/. Development: ./server/, produced by Scripts/build.ps1.
    const base = app.isPackaged
        ? path.join(process.resourcesPath, 'server')
        : path.join(__dirname, 'server');
    const exe = path.join(base, process.platform === 'win32' ? 'CedarClerk.Server.exe' : 'CedarClerk.Server');
    return fs.existsSync(exe) ? exe : null;
}

/** Every call to the agent carries the launch token. Without it the agent answers 401 — see
 *  AgentEndpoints for why an unauthenticated loopback file reader is not an option. */
function agentFetch(route, init = {}) {
    return fetch(`${agentOrigin}${route}`, {
        ...init,
        headers: { ...(init.headers ?? {}), Authorization: `Bearer ${agentToken}` },
    });
}

async function waitForAgent(origin, deadline) {
    while (Date.now() < deadline) {
        try {
            const response = await fetch(`${origin}/agent/health`, {
                headers: { Authorization: `Bearer ${agentToken}` },
                signal: AbortSignal.timeout(2000),
            });
            if (response.ok) return await response.json();
        } catch {
            // Not up yet. A refused connection during startup is expected, not an error.
        }
        await new Promise(r => setTimeout(r, HEALTH_POLL_MS));
    }
    return null;
}

async function startAgent() {
    const exe = agentExecutable();
    if (!exe) {
        throw new Error(
            'The agent executable is missing. Run Scripts/build.ps1 to publish it into CedarClerk.Desktop/server/.');
    }

    const port = await findFreePort();
    const origin = `http://127.0.0.1:${port}`;
    // 32 random bytes, new every launch, never written to disk. The agent demands it on every request;
    // this is what keeps a service that lists folders from being readable by every other process on
    // the machine — and by any page in any browser, since a browser can reach 127.0.0.1 too.
    agentToken = crypto.randomBytes(32).toString('base64url');

    agentProcess = spawn(exe, [], {
        cwd: path.dirname(exe),
        env: {
            ...process.env,
            // ADR-117 — the whole reason this process is small. With this set, Program.cs builds no
            // database, no Identity, no bot, no SPA: only /agent/*.
            Cedar__Agent__Enabled: 'true',
            Cedar__Agent__Token: agentToken,
            // Consts.General.UrlsCfg — double underscore is how .NET reads a nested config key.
            Cedar__Urls: origin,
            // No bot token, ever. Telegram allows exactly one process to long-poll a token, so a
            // desktop bot would knock production's bot off the air (.claude/rules/telegram-bot.md).
            // An agent never starts the bot at all; this is belt and braces against an inherited
            // environment variable, and the 409 it prevents is worth two lines.
            Cedar__BotToken: '',
            ASPNETCORE_ENVIRONMENT: 'Agent',
        },
        stdio: ['ignore', 'pipe', 'pipe'],
    });

    agentProcess.stdout.on('data', d => process.stdout.write(`[agent] ${d}`));
    agentProcess.stderr.on('data', d => process.stderr.write(`[agent] ${d}`));
    agentProcess.on('exit', code => {
        agentProcess = null;
        // The window keeps working without the agent — it is a browser onto the cloud — but folder
        // indexing stops, so say so rather than let that one feature fail silently.
        if (mainWindow && !mainWindow.isDestroyed()) {
            dialog.showErrorBox('Cedar Clerk',
                `The local file agent stopped unexpectedly (exit code ${code}).\n\n` +
                'Cedar Clerk still works, but folders cannot be indexed until you restart the app.');
        }
    });

    const health = await waitForAgent(origin, Date.now() + HEALTH_TIMEOUT_MS);
    if (!health) throw new Error('The local file agent did not start in time.');

    // The agent and this shell ship inside one installer, so a version mismatch here means a broken
    // build rather than a stale deploy — worth a dialog. **The cloud's version is deliberately not
    // checked**: the site deploys on its own schedule and the installer only with `deploy -Desktop`,
    // so those two diverge legitimately (ADR-116), and a dialog about it would fire after every
    // ordinary deploy.
    if (health.version !== SHELL_VERSION) {
        dialog.showErrorBox(
            'Cedar Clerk',
            `Version mismatch: this shell is ${SHELL_VERSION}, the local agent is ${health.version}.\n\n` +
            'Re-run Scripts/build.ps1 so both come from the same build.');
    }

    agentOrigin = origin;
    return origin;
}

function stopAgent() {
    if (!agentProcess) return;
    const child = agentProcess;
    agentProcess = null;

    // Detach the "it died on its own" reporter BEFORE killing it. `taskkill /f` makes the child exit
    // with code 1, and a shutdown we asked for must never be announced as a failure.
    //
    // This was invisible until updates existed: every other stop happens after the window is gone,
    // and the handler checks for that. Installing an update stops the agent while the window is still
    // open, so the modal error box appeared in the middle of quitting — which blocks the main process
    // and breaks the quit sequence. Reported by Marty on the first real update (11.08.2026): "exit
    // code 1", while the installer itself ran fine.
    child.removeAllListeners('exit');

    // A .NET host does not always die with its parent. An orphan no longer holds a database lock
    // (there is no database since ADR-117), but it does hold a port and a live token that grants
    // read access to a granted folder — which is a better reason to be thorough, not a worse one.
    //
    // Synchronous on purpose, for two reasons that only look like one. `process.on('exit')` runs no
    // asynchronous work at all, so a fire-and-forget kill there might never happen; and an update
    // installing on quit (ADR-116) overwrites this very executable, which a still-running process
    // holds locked. Both want the kill finished before this function returns.
    if (process.platform === 'win32') {
        spawnSync('taskkill', ['/pid', String(child.pid), '/f', '/t'], { stdio: 'ignore' });
    } else {
        child.kill('SIGTERM');
    }
}

/**
 * An update installs by quitting, so anything that goes wrong takes the console with it — and a
 * packaged app has no console to begin with. The line lands in the data directory instead, which is
 * the one place that survives both the crash and the reinstall.
 */
function logUpdate(message) {
    const line = `${new Date().toISOString()} ${message instanceof Error ? message.stack : message}`;
    console.log(`[update] ${line}`);
    try {
        fs.appendFileSync(path.join(dataDirectory(), 'update.log'), `${line}\n`);
    } catch {
        // A log that cannot be written must not be the reason an update fails.
    }
}

/**
 * Self-update (ADR-116). `electron-updater` reads https://cedarclerk.app/downloads/latest*.yml,
 * compares versions, downloads the installer named there and verifies its sha512 — the whole
 * protocol is that file, and the server side of it is plain static hosting.
 *
 * Failure here is silent by design: no network, no manifest published yet, a server that is down —
 * none of them stop the app from working on the version already installed, and a dialog about it
 * would interrupt someone who is writing.
 */
function startUpdateChecks() {
    // An unpackaged run has no app-update.yml beside it, and checkForUpdates() throws rather than
    // shrugging — so `npm start` during development would open on an error box.
    if (!app.isPackaged) return;

    autoUpdater.logger = { info: logUpdate, warn: logUpdate, error: logUpdate, debug: () => { } };
    autoUpdater.autoDownload = true;
    // Closing the window is how this app normally ends, so it is also the least intrusive moment to
    // install: no prompt, no progress bar, the next launch is simply the new version.
    autoUpdater.autoInstallOnAppQuit = true;

    autoUpdater.on('error', err => logUpdate(err?.message ?? err));
    autoUpdater.on('update-available', info => logUpdate(`update available: ${info.version}`));
    autoUpdater.on('update-not-available', () => logUpdate(`no update; running ${SHELL_VERSION}`));

    autoUpdater.on('update-downloaded', async info => {
        logUpdate(`downloaded ${info.version}`);
        // English, like every other dialog this shell shows. The app's own interface is translated;
        // the shell around it is not, and half-translating it would read as a bug rather than care.
        const message = {
            type: 'question',
            buttons: ['Restart now', 'Later'],
            defaultId: 0,
            cancelId: 1,
            title: 'Cedar Clerk',
            message: `Version ${info.version} is ready to install.`,
            detail: `This copy is ${SHELL_VERSION}. Restarting applies the update — choosing Later ` +
                'installs it when you close the app.',
        };
        const alive = mainWindow && !mainWindow.isDestroyed();
        const { response } = alive
            ? await dialog.showMessageBox(mainWindow, message)
            : await dialog.showMessageBox(message);
        if (response !== 0) {
            logUpdate('deferred to next quit');
            return;
        }

        // NSIS replaces resources/server/CedarClerk.Server.exe, which is running right now as our
        // child process. stopAgent() is synchronous, so by the time quitAndInstall() hands over,
        // nothing is holding the file the installer is about to overwrite.
        logUpdate('stopping the agent, then handing over to the installer');
        stopAgent();
        autoUpdater.quitAndInstall();
    });

    const check = () => autoUpdater.checkForUpdates().catch(() => { });
    void check();
    setInterval(check, UPDATE_CHECK_INTERVAL_MS).unref();
}

function createWindow() {
    mainWindow = new BrowserWindow({
        width: 1440,
        height: 900,
        minWidth: 900,
        minHeight: 600,
        show: false,
        title: 'Cedar Clerk',
        // The renderer loads a remote origin now, plus TipTap and whatever the author pasted into a
        // document. It gets the handful of functions preload exposes and nothing else — and preload
        // itself refuses to expose them unless the page really is the upstream (ADR-117, Decision 6).
        webPreferences: {
            preload: path.join(__dirname, 'preload.js'),
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: true,
            // Explicit rather than relied upon: this window loads a remote page and the same-origin
            // policy is what keeps it from reading others.
            webSecurity: true,
        },
    });

    mainWindow.once('ready-to-show', () => mainWindow.show());
    // External links open in the real browser; a Telegram or blog link should not replace the app.
    mainWindow.webContents.setWindowOpenHandler(({ url }) => {
        void shell.openExternal(url);
        return { action: 'deny' };
    });

    // The bridge is granted per-origin by preload, but navigation is blocked here as well, and the two
    // are not redundant: this one keeps the window from *becoming* a general-purpose browser that
    // happens to sit inside an app with disk access. Anything off-origin goes to the real browser,
    // where it belongs.
    mainWindow.webContents.on('will-navigate', (event, url) => {
        if (isUpstream(url)) return;
        event.preventDefault();
        void shell.openExternal(url);
    });

    void mainWindow.loadURL(`${UPSTREAM}${START_PATH}`);
}

/** Same-origin paths only: the page supplies this, and it ends up in a navigation. */
function safePath(value) {
    return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') ? value : START_PATH;
}

function showWindow() {
    if (!mainWindow || mainWindow.isDestroyed()) return;
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.show();
    mainWindow.focus();
}

/**
 * The second half of a provider sign-in (ADR-327): the browser hands back a one-time code, and this
 * window trades it, with the verifier only this process holds, for a session cookie in its own jar.
 *
 * A navigation rather than a fetch, because the cookie has to land in the window's session. The
 * X-Cedar-Desktop header is what the server takes as proof a web page did not post this.
 */
function finishSignIn(link) {
    let code = null;
    try {
        const url = new URL(link);
        if (url.protocol === `${SIGN_IN_SCHEME}:` && url.hostname === 'auth') code = url.searchParams.get('code');
    } catch {
        // Not a link at all; nothing to do.
    }

    const pending = pendingSignIn;
    if (!code || !pending || Date.now() > pending.expires) return;
    if (!mainWindow || mainWindow.isDestroyed()) return;
    pendingSignIn = null;

    const body = new URLSearchParams({ code, verifier: pending.verifier, returnUrl: pending.returnUrl });
    void mainWindow.loadURL(`${UPSTREAM}/api/auth/desktop/redeem`, {
        postData: [{ type: 'rawData', bytes: Buffer.from(body.toString()) }],
        extraHeaders: 'Content-Type: application/x-www-form-urlencoded\nX-Cedar-Desktop: 1',
    });
    showWindow();
}

function isUpstream(url) {
    try {
        return new URL(url).origin === new URL(UPSTREAM).origin;
    } catch {
        return false;
    }
}

/**
 * Every bridge call passes through here first.
 *
 * A renderer loading a remote origin is the boundary ADR-117 moved, so the check that the caller
 * really is that origin lives in the main process too — preload's gate can only be as trustworthy as
 * the page it runs in, and this one cannot be reasoned about from inside the page at all.
 */
function fromUpstream(event) {
    return isUpstream(event.senderFrame?.url ?? '');
}

function guard(channel, handler) {
    ipcMain.handle(channel, async (event, ...args) => {
        if (!fromUpstream(event)) throw new Error('Cedar Clerk: this page may not use the desktop bridge.');
        return handler(...args);
    });
}

// ---------------------------------------------------------------------------------------------
// The filesystem bridge. None of it writes, deletes or launches anything (ADR-117): the worst a
// compromised page can do with all of it is learn what is in a folder its human chose.
// `shell.openPath` is deliberately absent — that one turns reading into execution, and Marty chose to
// do without it rather than gain a double-click that opens Blender.
// ---------------------------------------------------------------------------------------------

guard('cedar:machine', () => machine);

// The one capability a browser cannot give (ADR-107): choosing a folder on this machine. The grant
// that follows is what lets the agent read it — so disk access always begins with a human gesture,
// not with a path the page thought up.
guard('cedar:pick-folder', async () => {
    const result = await dialog.showOpenDialog(mainWindow, {
        properties: ['openDirectory'],
        title: 'Choose a folder to index',
    });
    if (result.canceled) return null;

    const root = result.filePaths[0];
    const granted = await agentFetch('/agent/grant', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ root }),
    });
    if (!granted.ok) throw new Error('The local file agent would not accept that folder.');
    rememberGrantedRoot(root);
    return root;
});

guard('cedar:scan', async (root) => {
    const response = await agentFetch('/agent/scan', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ root }),
    });
    if (!response.ok) throw new Error(await agentError(response, 'The scan could not be started.'));
    return await response.json();
});

guard('cedar:scan-progress', async (scanId) => {
    const response = await agentFetch(`/agent/scan/${scanId}`);
    return response.ok ? await response.json() : null;
});

guard('cedar:scan-files', async (scanId, skip, take) => {
    const response = await agentFetch(`/agent/scan/${scanId}/files?skip=${skip | 0}&take=${take | 0}`);
    return response.ok ? await response.json() : null;
});

guard('cedar:scan-cancel', async (scanId) => {
    const response = await agentFetch(`/agent/scan/${scanId}`, { method: 'DELETE' });
    return response.ok;
});

guard('cedar:stat', async (root, relativePath) => {
    const response = await agentFetch('/agent/stat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ root, relativePath }),
    });
    return response.ok ? await response.json() : null;
});

// Returns base64 rather than a Buffer: what crosses the IPC boundary should be plainly inert data, and
// a string is harder to mistake for something the page can execute. The page turns it back into bytes
// for the upload, and a 360px JPEG is a few tens of kilobytes either way.
guard('cedar:thumb', async (fullPath) => {
    const response = await agentFetch('/agent/thumb', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: fullPath }),
    });
    if (!response.ok) return null;
    const buffer = Buffer.from(await response.arrayBuffer());
    return buffer.toString('base64');
});

// Highlights the file in Explorer/Finder. It opens no file and runs nothing — the distinction that
// made this acceptable while `openPath` was not.
guard('cedar:reveal', async (target) => {
    if (typeof target === 'string' && target.length > 0) shell.showItemInFolder(target);
});

// Starts a provider sign-in in the system browser (ADR-327). The page names a provider and a return
// path and nothing else: the address opened is built here and is always the upstream's own.
guard('cedar:sign-in', (provider, returnUrl) => {
    if (!SIGN_IN_PROVIDERS.has(provider)) throw new Error('Cedar Clerk: unknown sign-in provider.');

    const verifier = crypto.randomBytes(32).toString('base64url');
    const challenge = crypto.createHash('sha256').update(verifier).digest('base64url');
    pendingSignIn = { verifier, returnUrl: safePath(returnUrl), expires: Date.now() + SIGN_IN_WINDOW_MS };

    const handoff = `/auth/desktop?challenge=${challenge}`;
    void shell.openExternal(`${UPSTREAM}/api/auth/external/${provider}?returnUrl=${encodeURIComponent(handoff)}`);
});

async function agentError(response, fallback) {
    try {
        return (await response.json()).error ?? fallback;
    } catch {
        return fallback;
    }
}

if (!isPrimaryInstance) app.quit();

app.on('second-instance', (_event, argv) => {
    const link = argv.find(arg => arg.startsWith(`${SIGN_IN_SCHEME}://`));
    if (link) finishSignIn(link);
    else showWindow();
});

// macOS delivers the link to the running app as an event instead of a second launch.
app.on('open-url', (event, url) => {
    event.preventDefault();
    finishSignIn(url);
});

app.whenReady().then(async () => {
    if (!isPrimaryInstance) return;
    try {
        // Windows has no install-time registration for the scheme. Packaged only: an unpackaged run
        // would register the bare Electron binary as the handler.
        if (app.isPackaged) app.setAsDefaultProtocolClient(SIGN_IN_SCHEME);

        const identity = machineIdentity();
        machine.id = identity.id;
        machine.name = identity.name;

        await startAgent();
        // Before the window: a re-scan must not race a grant that has not landed yet.
        await restoreGrants();
        createWindow();
        startUpdateChecks();
    } catch (e) {
        dialog.showErrorBox('Cedar Clerk', String(e.message ?? e));
        app.quit();
    }
});

app.on('window-all-closed', () => {
    // On macOS the Dock keeps the app alive after its window closes. Keep the agent alongside it,
    // so reopening the window does not leave the filesystem bridge pointing at a stopped process.
    if (process.platform !== 'darwin') app.quit();
});

app.on('before-quit', stopAgent);
// A crash of the shell itself must not leave the agent behind either.
process.on('exit', stopAgent);

app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
});

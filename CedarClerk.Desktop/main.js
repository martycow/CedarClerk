// Cedar Clerk desktop shell (T-121, ADR-104/105/107). Full rationale: docs/DESKTOP.md.
//
// What this is: a window that starts the ordinary CedarClerk.Server as a local child process and
// opens the ordinary Angular SPA against it. Neither the server nor the frontend is forked — the
// whole shell is this file, a preload script and a builder config.
//
// Why it exists at all: the asset index reads a folder on this machine (ADR-107), and only a
// process running here can do that. Everything else the desktop gains is a side effect.
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
const net = require('node:net');
const path = require('node:path');
const fs = require('node:fs');

const SHELL_VERSION = require('./package.json').version;
const HEALTH_TIMEOUT_MS = 60_000;
const HEALTH_POLL_MS = 250;
// A session here is hours, not days, so this is mostly about the copy somebody leaves open all
// week. The check at launch is the one that matters.
const UPDATE_CHECK_INTERVAL_MS = 4 * 60 * 60 * 1000;

let serverProcess = null;
let serverOrigin = null;
let mainWindow = null;

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

/** Where cedar.db and media/ live. The SAME variable the Pi's systemd drop-in sets, which is why
 *  the server needs no desktop-specific code at all (docs/DESKTOP.md, "Data"). */
function dataDirectory() {
    const dir = path.join(app.getPath('appData'), 'CedarClerk');
    fs.mkdirSync(dir, { recursive: true });
    return dir;
}

function serverExecutable() {
    // Packaged: resources/server/. Development: ./server/, produced by Scripts/build.ps1.
    const base = app.isPackaged
        ? path.join(process.resourcesPath, 'server')
        : path.join(__dirname, 'server');
    const exe = path.join(base, process.platform === 'win32' ? 'CedarClerk.Server.exe' : 'CedarClerk.Server');
    return fs.existsSync(exe) ? exe : null;
}

async function waitForHealth(origin, deadline) {
    while (Date.now() < deadline) {
        try {
            const response = await fetch(`${origin}/api/health`, { signal: AbortSignal.timeout(2000) });
            if (response.ok) return await response.json();
        } catch {
            // Not up yet. A refused connection during startup is expected, not an error.
        }
        await new Promise(r => setTimeout(r, HEALTH_POLL_MS));
    }
    return null;
}

async function startServer() {
    const exe = serverExecutable();
    if (!exe) {
        throw new Error(
            'The server executable is missing. Run Scripts/build.ps1 to publish it into CedarClerk.Desktop/server/.');
    }

    const port = await findFreePort();
    const origin = `http://127.0.0.1:${port}`;

    serverProcess = spawn(exe, [], {
        cwd: path.dirname(exe),
        env: {
            ...process.env,
            CEDAR_DATA_DIR: dataDirectory(),
            // Consts.General.UrlsCfg — double underscore is how .NET reads a nested config key.
            Cedar__Urls: origin,
            // No bot token, ever. Telegram allows exactly one process to long-poll a token, so a
            // desktop bot would knock the Pi's bot off the air (.claude/rules/telegram-bot.md).
            // The server already disables the bot when the token is absent, so this is belt and
            // braces against an inherited environment variable.
            Cedar__BotToken: '',
            // T-122 — the asset index makes the server walk the server's disk. Here that is the
            // whole point and the machine is the author's own; on the Pi, which serves every
            // account from one process, it would let any tenant enumerate its filesystem. So the
            // capability is off by default everywhere and turned on only right here.
            Cedar__AssetIndex__Enabled: 'true',
            // ADR-108 — who you are is the Pi's answer, so one address cannot mean two different
            // people. Accounts are created there too, which is why the open-registration flag is
            // deliberately NOT set here any more.
            Cedar__Auth__Upstream: 'https://cedarclerk.mooexe.dev',
            // Presentation only: skips the marketing landing page, which has no audience here.
            Cedar__Desktop: 'true',
            ASPNETCORE_ENVIRONMENT: 'Desktop',
        },
        stdio: ['ignore', 'pipe', 'pipe'],
    });

    serverProcess.stdout.on('data', d => process.stdout.write(`[server] ${d}`));
    serverProcess.stderr.on('data', d => process.stderr.write(`[server] ${d}`));
    serverProcess.on('exit', code => {
        serverProcess = null;
        // A server that dies while the window is open leaves a shell that can do nothing, so say
        // so rather than let every click fail silently.
        if (mainWindow && !mainWindow.isDestroyed()) {
            dialog.showErrorBox('Cedar Clerk', `The local server stopped unexpectedly (exit code ${code}).`);
        }
    });

    const health = await waitForHealth(origin, Date.now() + HEALTH_TIMEOUT_MS);
    if (!health) throw new Error('The local server did not become healthy in time.');

    // The health endpoint reports the version it was built from, so a shell sitting next to a
    // server from another build is caught here rather than as confusing behaviour later.
    if (health.version !== SHELL_VERSION) {
        dialog.showErrorBox(
            'Cedar Clerk',
            `Version mismatch: this shell is ${SHELL_VERSION}, the server is ${health.version}.\n\n` +
            'Re-run Scripts/build.ps1 so both come from the same build.');
    }

    serverOrigin = origin;
    return origin;
}

function stopServer() {
    if (!serverProcess) return;
    const child = serverProcess;
    serverProcess = null;

    // Detach the "it died on its own" reporter BEFORE killing it. `taskkill /f` makes the child
    // exit with code 1, and a shutdown we asked for must never be announced as a failure.
    //
    // This was invisible until updates existed: every other stop happens after the window is gone,
    // and the handler checks for that. Installing an update stops the server while the window is
    // still open, so the modal error box appeared in the middle of quitting — which blocks the main
    // process and breaks the quit sequence. Reported by Marty on the first real update
    // (11.08.2026): "exit code 1", while the installer itself ran fine.
    child.removeAllListeners('exit');

    // A .NET host does not always die with its parent, and an orphan holds cedar.db's WAL lock —
    // the next launch would then find a database it cannot open.
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
 * packaged app has no console to begin with. The line lands in the data directory instead, beside
 * cedar.db, which is the one place that survives both the crash and the reinstall.
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
 * Self-update (ADR-116). `electron-updater` reads https://cedarclerk.mooexe.dev/downloads/latest.yml,
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
        // child process. stopServer() is synchronous, so by the time quitAndInstall() hands over,
        // nothing is holding the file the installer is about to overwrite.
        logUpdate('stopping the server, then handing over to the installer');
        stopServer();
        autoUpdater.quitAndInstall();
    });

    const check = () => autoUpdater.checkForUpdates().catch(() => { });
    void check();
    setInterval(check, UPDATE_CHECK_INTERVAL_MS).unref();
}

function createWindow(origin) {
    mainWindow = new BrowserWindow({
        width: 1440,
        height: 900,
        minWidth: 900,
        minHeight: 600,
        show: false,
        title: 'Cedar Clerk',
        // The renderer loads TipTap and whatever the author pasted into a document, so it gets the
        // two or three functions preload exposes and nothing else.
        webPreferences: {
            preload: path.join(__dirname, 'preload.js'),
            contextIsolation: true,
            nodeIntegration: false,
            sandbox: true,
        },
    });

    mainWindow.once('ready-to-show', () => mainWindow.show());
    // External links open in the real browser; a Telegram or blog link should not replace the app.
    mainWindow.webContents.setWindowOpenHandler(({ url }) => {
        void shell.openExternal(url);
        return { action: 'deny' };
    });

    void mainWindow.loadURL(origin);
}

// The one capability a browser cannot give (ADR-107): choosing a folder on this machine so the
// server can index the paths inside it. Nothing is uploaded — see docs/DESKTOP.md.
ipcMain.handle('cedar:pick-folder', async () => {
    const result = await dialog.showOpenDialog(mainWindow, {
        properties: ['openDirectory'],
        title: 'Choose a folder to index',
    });
    return result.canceled ? null : result.filePaths[0];
});

ipcMain.handle('cedar:reveal', async (_event, target) => {
    if (typeof target === 'string' && target.length > 0) shell.showItemInFolder(target);
});

app.whenReady().then(async () => {
    try {
        const origin = await startServer();
        createWindow(origin);
        startUpdateChecks();
    } catch (e) {
        dialog.showErrorBox('Cedar Clerk', String(e.message ?? e));
        app.quit();
    }
});

app.on('window-all-closed', () => {
    stopServer();
    if (process.platform !== 'darwin') app.quit();
});

app.on('before-quit', stopServer);
// A crash of the shell itself must not leave the server behind either.
process.on('exit', stopServer);

app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0 && serverOrigin) createWindow(serverOrigin);
});

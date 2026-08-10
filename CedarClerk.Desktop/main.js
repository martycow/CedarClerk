// Cedar Clerk desktop shell (T-121, ADR-104/105/107). Full rationale: docs/DESKTOP.md.
//
// What this is: a window that starts the ordinary CedarClerk.Server as a local child process and
// opens the ordinary Angular SPA against it. Neither the server nor the frontend is forked — the
// whole shell is this file, a preload script and a builder config.
//
// Why it exists at all: the asset index reads a folder on this machine (ADR-107), and only a
// process running here can do that. Everything else the desktop gains is a side effect.
const { app, BrowserWindow, dialog, ipcMain, shell } = require('electron');
const { spawn } = require('node:child_process');
const net = require('node:net');
const path = require('node:path');
const fs = require('node:fs');

const SHELL_VERSION = require('./package.json').version;
const HEALTH_TIMEOUT_MS = 60_000;
const HEALTH_POLL_MS = 250;

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
            // No invite code here, and none to type: the gate protects a shared server from
            // strangers, and this one listens on 127.0.0.1 for one person. Without this a fresh
            // install cannot create its first account — there is nowhere to get a code from.
            Cedar__Registration__Open: 'true',
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
    // A .NET host does not always die with its parent, and an orphan holds cedar.db's WAL lock —
    // the next launch would then find a database it cannot open.
    if (process.platform === 'win32') {
        spawn('taskkill', ['/pid', String(child.pid), '/f', '/t'], { stdio: 'ignore' });
    } else {
        child.kill('SIGTERM');
    }
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

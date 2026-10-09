// The only bridge between the page and this machine (ADR-104, tightened by ADR-117).
//
// ## Why this file now has a gate in it
//
// It used to be unconditional, and that was fine: the window loaded 127.0.0.1, so "the page" and "our
// server" were the same thing. Since ADR-117 the window loads cedarclerk.app — a remote origin —
// and the SPA it serves renders TipTap documents and pasted HTML. So the bridge is exposed only when
// the page really is the upstream, and the main process checks the same thing again on every call
// (see `guard` in main.js). Two checks rather than one because this one runs *inside* the renderer and
// therefore cannot be the last word about it.
//
// Nothing here writes, deletes or runs a file. `shell.openPath` is deliberately not exposed:
// reading a folder is what the feature needs, and executing a file is what an attacker needs.
const { contextBridge, ipcRenderer } = require('electron');

// Repeated from main.js rather than shared, and that is a considered choice. A sandboxed preload can
// only require a documented subset of modules, and `process.argv` is not a surface worth betting a
// security gate on — so the alternatives were an uncertain mechanism or a duplicated constant. The
// duplicate is one string, sitting next to the file it is duplicated from, and if the two ever
// disagree the bridge simply does not appear: it fails closed, which is the direction a mistake here
// should fail. The authoritative check is `guard()` in main.js, which cannot be reached from the page.
const UPSTREAM_ORIGIN = 'https://cedarclerk.app';

function isUpstream() {
    try {
        return new URL(UPSTREAM_ORIGIN).origin === window.location.origin;
    } catch {
        return false;
    }
}

if (isUpstream()) {
    contextBridge.exposeInMainWorld('cedarDesktop', {
        /** True only inside the desktop shell — the web build has no `window.cedarDesktop` at all. */
        isDesktop: true,

        /**
         * This machine's id and name. The id is what lets a screen tell a real file from a fingerprint
         * of one: the cloud remembers which machine indexed a folder, and this is what it is compared
         * against. A browser has no bridge, so it never has an id, so it always shows fingerprints.
         */
        machine: () => ipcRenderer.invoke('cedar:machine'),

        /**
         * Opens the OS folder picker and grants the chosen folder to the local agent. Returns the
         * path, or null if the user cancelled. This is the only way a folder becomes readable — the
         * page cannot name one itself.
         */
        pickFolder: () => ipcRenderer.invoke('cedar:pick-folder'),

        /** Starts a walk of a granted folder. Returns { scanId, total, status, ... }. */
        scan: (root) => ipcRenderer.invoke('cedar:scan', root),

        /** Progress of a running walk: counted total, described so far, unreadable folders. */
        scanProgress: (scanId) => ipcRenderer.invoke('cedar:scan-progress', scanId),

        /** A page of described files. Readable while the walk is still running, on purpose: the first
         *  five hundred files upload while the walk is still deep in the tree. */
        scanFiles: (scanId, skip, take) => ipcRenderer.invoke('cedar:scan-files', scanId, skip, take),

        scanCancel: (scanId) => ipcRenderer.invoke('cedar:scan-cancel', scanId),

        /** Re-stats one file, for the "Re-index file" button. */
        stat: (root, relativePath) => ipcRenderer.invoke('cedar:stat', root, relativePath),

        /** Renders one thumbnail, as base64 JPEG, or null when this file cannot have one. */
        thumb: (fullPath) => ipcRenderer.invoke('cedar:thumb', fullPath),

        /** Shows a file in Explorer/Finder. Highlights it; does not open it. */
        reveal: (targetPath) => ipcRenderer.invoke('cedar:reveal', targetPath),

        /**
         * Starts a Google or Discord sign-in in the system browser (ADR-327). The provider's pages
         * cannot run in this window, and the address opened is built in main, not taken from here.
         */
        signIn: (provider, returnUrl) => ipcRenderer.invoke('cedar:sign-in', provider, returnUrl),
    });
}

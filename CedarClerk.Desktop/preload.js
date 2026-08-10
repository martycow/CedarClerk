// The only bridge between the page and this machine (ADR-104). Deliberately tiny: the renderer
// loads TipTap and arbitrary pasted content, so it gets these two functions and nothing else —
// no `require`, no filesystem, no Node globals (contextIsolation + sandbox are on in main.js).
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('cedarDesktop', {
    /** True only inside the desktop shell — the web build has no `window.cedarDesktop` at all. */
    isDesktop: true,

    /** Opens the OS folder picker. Returns the chosen path, or null if the user cancelled. */
    pickFolder: () => ipcRenderer.invoke('cedar:pick-folder'),

    /** Shows a file in Explorer/Finder — the "Reveal in file manager" action on an asset. */
    reveal: (targetPath) => ipcRenderer.invoke('cedar:reveal', targetPath),
});

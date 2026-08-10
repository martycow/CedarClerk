// Launcher for `npm start` (10.08.2026 — Marty's first launch failed on this).
//
// Electron's binary doubles as a plain Node runtime, and the environment variable
// ELECTRON_RUN_AS_NODE is what switches it over. Several editors — VS Code among them — set that
// variable in their integrated terminals for their own tooling, and it is inherited by anything
// started from there. `electron .` then runs main.js under Node instead of as an Electron app:
// `require('electron')` returns the *path to the binary* rather than the API object, every
// destructured name is undefined, and the first one used throws
//
//     TypeError: Cannot read properties of undefined (reading 'handle')
//
// which says nothing about the actual cause. This strips the variable and starts Electron properly,
// so where the command is typed stops mattering.
const { spawn } = require('node:child_process');
const electron = require('electron');

if (typeof electron !== 'string') {
    // Running under Electron itself would mean this file was used as the app entry point, which it
    // is not — main.js is. Bail rather than start something confusing.
    console.error('start.js is a launcher and must be run with node, not electron.');
    process.exit(1);
}

const env = { ...process.env };
delete env.ELECTRON_RUN_AS_NODE;

const child = spawn(electron, ['.', ...process.argv.slice(2)], {
    cwd: __dirname,
    env,
    stdio: 'inherit',
});

child.on('close', code => process.exit(code ?? 0));

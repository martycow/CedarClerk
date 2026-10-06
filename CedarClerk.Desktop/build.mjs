import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const desktop = dirname(fileURLToPath(import.meta.url));
const root = resolve(desktop, '..');
const platform = process.platform;
const arch = process.arch;
const platforms = { win32: 'win', darwin: 'mac', linux: 'linux' };
const runtimePrefixes = { win32: 'win', darwin: 'osx', linux: 'linux' };

if (!(platform in platforms) || !['x64', 'arm64'].includes(arch)) {
    throw new Error(`Unsupported desktop build host: ${platform}/${arch}`);
}
if (platform === 'win32' && arch !== 'x64') {
    throw new Error('The Windows NSIS target is currently x64 only.');
}

const platformName = platforms[platform];
const rid = `${runtimePrefixes[platform]}-${arch}`;
// A local build must not silently pick an unrelated Apple Development identity from Keychain.
// Distribution signing is an explicit release action with a Developer ID certificate.
if (platform === 'darwin' && process.env.CEDAR_DESKTOP_SIGN !== '1') {
    process.env.CSC_IDENTITY_AUTO_DISCOVERY = 'false';
}
if (platform === 'darwin' && process.env.CEDAR_DESKTOP_SIGN === '1'
    && !process.env.CSC_NAME && !process.env.CSC_LINK) {
    throw new Error('Set CSC_NAME to a Developer ID Application identity or provide CSC_LINK for a signed Mac build.');
}
const versionSource = readFileSync(join(root, 'CedarClerk.Core', 'Consts.cs'), 'utf8');
const version = versionSource.match(/CurrentVersion\s*=\s*"(\d+\.\d+\.\d+)"/)?.[1];
if (!version) throw new Error('Could not read Consts.CurrentVersion.');

// The packaged shell and the agent report one version. Keep npm's lockfile in step as well,
// otherwise `npm ci` rejects the next checkout after a release version bump.
for (const name of ['package.json', 'package-lock.json']) {
    const file = join(desktop, name);
    const value = JSON.parse(readFileSync(file, 'utf8'));
    if (value.version === version && (!value.packages?.[''] || value.packages[''].version === version)) continue;
    value.version = version;
    if (value.packages?.['']) value.packages[''].version = version;
    writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
    console.log(`Updated ${name} to ${version}`);
}

function run(program, args, cwd) {
    console.log(`> ${program} ${args.join(' ')}`);
    const result = spawnSync(program, args, { cwd, stdio: 'inherit', env: process.env });
    if (result.error) throw result.error;
    if (result.status !== 0) process.exit(result.status ?? 1);
}

run('dotnet', [
    'publish', join(root, 'CedarClerk.Server', 'CedarClerk.Server.csproj'),
    '-c', 'Release', '-r', rid, '--self-contained', 'true',
    '-o', join(desktop, 'server'),
], root);

const builderArgs = [
    join(desktop, 'node_modules', 'electron-builder', 'cli.js'),
    `--${platformName}`, `--${arch}`, '--publish', 'never',
];
if (platform === 'darwin' && process.env.CEDAR_DESKTOP_SIGN === '1') {
    builderArgs.push('-c.mac.forceCodeSigning=true', '-c.mac.notarize=true');
}
run(process.execPath, builderArgs, desktop);

console.log(`Desktop ${version} for ${platform}/${arch}: ${join(desktop, 'dist')}`);

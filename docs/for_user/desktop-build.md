# Build Cedar Clerk for Windows, macOS and Linux

The desktop app is an Electron window onto `https://cedarclerk.app`, plus a local .NET agent for reading folders selected in the asset manager. It uses the cloud account and needs a network connection. Build it on the operating system where it will run; the build packages that system's agent executable.

## Prerequisites

- A checkout of this repository and an internet connection for the production site and first-time package downloads.
- .NET 10 SDK, Node.js 26 and npm 11 on the build machine.
- On macOS, Xcode Command Line Tools for packaging. The Apple Silicon build was verified on macOS 27.0.1. An Intel Mac uses the same command and produces an `x64` build.
- On Windows, use an x64 machine and PowerShell. The current NSIS target is x64 only.
- On Linux, use an x64 or arm64 machine. The output is an AppImage.

The repository version in `CedarClerk.Core/Consts.cs` becomes the desktop version. The build command updates the two desktop npm metadata files when it changes; review and commit those updates with a release.

## Build

From the repository root, in a terminal or PowerShell:

```text
cd CedarClerk.Desktop
npm ci
npm run build:desktop
```

The command publishes `CedarClerk.Server` as a self-contained agent for the host runtime and then runs electron-builder. It does not build Angular: the desktop window loads the deployed site. The published agent is staged in `CedarClerk.Desktop/server/`; generated packages are in `CedarClerk.Desktop/dist/`. Both directories are ignored by Git.

| Build host | Agent runtime | Output in `CedarClerk.Desktop/dist/` |
|---|---|---|
| Windows x64 | `win-x64` | `CedarClerk-Setup-<version>.exe` and `latest.yml` |
| macOS Apple Silicon | `osx-arm64` | `CedarClerk-<version>-mac-arm64.dmg`, matching ZIP, and `latest-mac.yml` |
| macOS Intel | `osx-x64` | `CedarClerk-<version>-mac-x64.dmg`, matching ZIP, and `latest-mac.yml` |
| Linux x64 / arm64 | `linux-x64` / `linux-arm64` | `CedarClerk-<version>-linux-<arch>.AppImage` and `latest-linux.yml` |

`npm start` starts an unpackaged shell only after an agent has been published into `CedarClerk.Desktop/server/`. Use `npm run build:desktop` for a complete installable artifact.

## Check the result

1. Open the built application. On macOS, `open "dist/mac-arm64/Cedar Clerk.app"` opens the unpacked build on Apple Silicon; use `dist/mac/Cedar Clerk.app` if electron-builder names the Intel folder without an architecture suffix. On Windows, install the NSIS `.exe`; on Linux, make the AppImage executable and launch it.
2. Confirm that the Cedar Clerk login screen appears and that the local `CedarClerk.Server` agent process starts. Sign in with an existing account.
3. In an asset screen, choose a test folder and verify that indexing and file reveal work. Close and reopen the window on macOS, then confirm indexing still works. Quit the app and confirm the agent exits.
4. For a release, repeat the install and sign-in test on a clean machine without a development checkout.

The macOS build command skips automatic certificate discovery by default, so a local package is unsigned. That package is for local testing. To distribute a macOS build, install a **Developer ID Application** certificate, set `CSC_NAME` to that identity (or provide `CSC_LINK` for its exported certificate), provide Apple notarization credentials outside the repository, and run `CEDAR_DESKTOP_SIGN=1 npm run build:desktop`. The build then requires signing and notarization. An Apple Development certificate is not a substitute for Developer ID. Verify the resulting app with `codesign --verify --deep --strict` and `spctl --assess --type exec` before giving out the DMG.

Building does not publish downloads or updates. The `latest*.yml` files and their versioned packages must be uploaded to the download directory deliberately, with each platform manifest written last. The existing `/downloads/latest` convenience URL reads the Windows `latest.yml`; it is not a macOS or Linux download link. The `/download` page lists a platform as soon as its manifest and the installer it names are both in the download directory, and links it through `/downloads/latest/mac` or `/downloads/latest/linux`. The public landing still advertises the Windows installer until a signed, tested macOS release is published.

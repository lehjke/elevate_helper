# Releasing

1. Update the app version in `ElevateHelperWinUI.csproj` and `Package.appxmanifest`.
2. Add release notes to `docs/releases/<tag>.md`.
3. Commit the release-prep changes.
4. Create a tag matching the exact project version, `v<major>.<minor>.<patch>`, for example `v2.1.8`.
5. Push the branch and the tag to GitHub.

The `release.yml` workflow builds:

- a self-contained WinUI x64 zip archive
- an Inno Setup installer `.exe`

Both assets are published to GitHub Releases.

## Windows UI release gate

Before publishing a release, validate the native WinUI build on Windows 10 and Windows 11 x64:

- build and launch the unpackaged self-contained app;
- verify single-instance reactivation and shutdown with active jobs and unsaved editor changes;
- test the main page and editor at 100%, 125%, 150%, and 200% DPI;
- test 100%, 150%, and 225% text scale without clipped controls;
- test Light, Dark, and all four Windows contrast themes;
- turn off Windows animation effects and verify that nonessential motion is suppressed while state changes remain clear;
- complete the main workflows with keyboard only and with Narrator;
- inspect accessible names, selected states, focus order, live status announcements, and 40 effective-pixel hit areas;
- verify ELVX overwrite confirmation, atomic save, package assets, Excel COM cleanup, and installer upgrade.

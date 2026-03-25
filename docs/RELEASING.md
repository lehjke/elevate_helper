# Releasing

1. Update the app version in `ElevateHelperWinUI.csproj` and `Package.appxmanifest`.
2. Add release notes to `docs/releases/<tag>.md`.
3. Commit the release-prep changes.
4. Create a tag in the `v<major>.<minor>` format, for example `v1.17`.
5. Push the branch and the tag to GitHub.

The `release.yml` workflow builds a self-contained WinUI x64 archive and publishes it to GitHub Releases.

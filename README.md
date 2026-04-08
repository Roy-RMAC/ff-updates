# FabFlow Releases

This repository hosts public releases of **FabFlow**, a productivity add-in for Autodesk Inventor.

It contains only:

- `version.json` — the update manifest read by the in-add-in auto-updater. It points at the latest release asset and is updated automatically by CI when a new version is published.
- GitHub Releases — each tagged release has a `FabFlowSetup.exe` attached. This is the single installer that customers download (it bundles the .NET 8 runtime check and the MSI).

## Where do I download FabFlow?

Visit **[fabflow.com.au](https://fabflow.com.au)**, or grab the latest release directly from the [Releases page](https://github.com/Roy-RMAC/ff-updates/releases).

Supported Inventor versions: **2025, 2026, 2027**.

## Source code

FabFlow's source is **not public**. It lives in a separate private repository, and releases are published here automatically by the build pipeline.

## Support

Email **support@fabflow.com.au** for bug reports, license questions, or general support.

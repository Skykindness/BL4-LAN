# Build

Windows with .NET Framework4.x compiler: run `build.ps1` to compile the launcher.

For the full package, use PowerShell7: `./Tools/package-release.ps1 -SDKArchive C:/path/to/oak2-sdk-v0.3.zip`. The SDK archive must match the pinned SHA256 in the script. Without that argument the official archive is downloaded and checked. No game files or saves are required.

The bundled source manifest pins release inputs. The GitHub workflow is manual and does not publish releases. Upload the prepared assets yourself.

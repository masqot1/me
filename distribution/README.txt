TrueWebsiteCloner 1.0.0 — Windows x64 Distribution
=================================================

Requirements
------------
- Windows x64
- Google Chrome
- .NET 10 runtimes:
  Microsoft.NETCore.App 10.x
  Microsoft.WindowsDesktop.App 10.x
  Microsoft.AspNetCore.App 10.x

Quick start
-----------
1. Extract this ZIP to a normal local folder. Do not run directly from inside the ZIP.
2. Run 01_INSTALL.bat. Administrator rights are not required; Native Messaging is registered under HKCU.
3. Open chrome://extensions, enable Developer mode and Load unpacked from the chrome-extension folder.
4. Confirm extension ID: ggcmdgdiopplpbcfinamhjdkbhiknfbk
5. Run 02_RUN_TRUEWEBSITECLONER.bat.

Uninstall
---------
Run 03_UNINSTALL.bat. It removes the Native Messaging registration and local distribution install record only. It does not delete captured projects or project workspaces.

Integrity
---------
- distribution-manifest.json contains SHA-256 and byte length for every payload file.
- The ZIP has a sibling .sha256 file containing the SHA-256 of the complete distribution archive.

Security scope
--------------
Use TrueWebsiteCloner only with sites, applications and test environments you own or are authorized to capture. The V1.0 gates use the project-owned deterministic Test Lab for automated capture/replay tests.

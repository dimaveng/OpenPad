# Updates from GitHub Releases

OpenPad checks the public `dimaveng/OpenPad` latest stable release on startup. Disable this in the sidebar; **Updates** also provides a manual check. Offline startup remains quiet. Draft/prerelease releases and downgrades are ignored. Checking does not download the application package.

**Download and install** downloads the correct Windows x64 ZIP and `SHA256SUMS.txt` from the same tagged release, verifies length and SHA-256, then asks before closing OpenPad. The library must save successfully and no OPL operation may be active. A separate Windows PowerShell helper waits up to 60 seconds for this process to exit, refuses updates to a running destination, replaces files with rollback, and reopens the app. It never replaces `Data` or recreates Start menu shortcuts during an update. Other OpenPad copies in different folders do not block the update.

Installed copies use `OpenPad-update-win-x64.zip`; portable copies use `OpenPad-portable-win-x64.zip`. The latter is identified by `portable.marker`. A mode mismatch is rejected. Failed downloads/cancellations remove temporary partial files. The helper's temporary folder contains `update.log` and backup files for troubleshooting failed file replacement; successful download folders are left in Windows Temp after application restart.

SHA-256 detects damaged/inconsistent downloads; it is not an independent publisher signature. The trust source is the GitHub repository and its HTTPS release assets. API/asset hosts and repository paths are fixed in code. No GitHub login/token is needed.

CI publishes the package files and checksums together. Existing releases before this feature may have no update ZIP; they are not retroactively modified. The first upgrade from 0.6.0 still requires downloading a new installer manually because that version has no updater.

Updater failures use an English diagnostic dialog/log after OpenPad closes. The application update interface supports Ukrainian, English and Russian. Runtime/driver installation is handled by the normal installer, not by an in-app update of an already working copy.

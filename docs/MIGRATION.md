# Installation and saved-data migration

From **1.1.0-dev.20260929.2**, the installer defaults to `C:\Program Files\ElkaSoft\EchoPad`. It keeps EchoPad's existing installer identity so Windows recognizes the upgrade, but selects the new default folder instead of carrying forward the old one. The destination can still be changed in the installer.

The app saves under `%LOCALAPPDATA%\ElkaSoft\EchoPad` for the Windows user running it. It does not need administrator rights to save. The installer does not copy one administrator's profile into another user's account.

| Contents | New location |
| --- | --- |
| Global settings and current pad assignments | `echopad.settings.json` |
| All profiles, names and active profile | `profiles.json` |
| Imported stopped/playing PNGs | `PadImages` |
| Captured audio, including future recordings | `Captures` |
| Media referenced directly beside very old executables | `LegacyMedia` |
| One-time migration record | `migration-elka-v1.json` |
| Verified installer downloads | `Updates` |

All paths in the table are relative to the new user-data folder. The ZIP build uses the same folder; it is not an isolated portable profile.

## First launch after upgrading

Close the older EchoPad instance before upgrading. The installer asks Windows to close the previous executable when it can identify it. Migration happens when the new application first launches, before settings or profiles are loaded.

EchoPad searches the previous `%LOCALAPPDATA%\Echopad` folder first, then the previous install locations retained by the installer or Windows uninstall registration, the current executable folder, and the old Program Files `EchoPad` folders. It recognizes `echopad.settings.json` and the older `settings.json` filename, plus `profiles.json`. Each existing destination document takes precedence; documents are not merged between profiles from different installations.

Owned `PadImages` and `Captures` folders are copied, including the old Documents `Echopad\Captures` folder. References inside imported settings and profiles are updated to the copied files. If different media files have the same destination name, both are retained and the imported reference uses a name with a content hash. Originals are never deleted or rewritten.

External audio files, library folders and configured drop folders remain where they are. The default drop folder stays at Documents `Echopad\Drop`, preserving integrations that write files there.

The completion record prevents later launches from restoring old settings or media you have deliberately removed. Old binaries and original data are left in place; use the new shortcut for future runs. The old and new builds subsequently save independently and do not synchronize changes.

## If migration cannot finish

An unreadable selected JSON file or a failed media copy stops startup with the affected path rather than silently starting with empty settings. Fix the reported file/access problem and launch again. Files are copied through temporary files and completed individually; retry preserves already completed files. The completion record is written only after the full migration succeeds.

Legacy media directory links are not followed recursively. If one is reported, copy its actual contents into a normal legacy directory and retry. The original data remains available throughout.

## Backups and installer maintenance

Back up the complete new user-data folder and any externally referenced audio. Keep the original folders until you have checked your profiles, recordings and PNGs in the new application. Moving a backup between Windows accounts may require reselecting files because media paths are absolute.

The installer uses [UsePreviousAppDir=no](https://jrsoftware.org/ishelp/topic_setup_usepreviousappdir.htm) and records the prior installation before its registration changes. [SaveStringsToUTF8File](https://jrsoftware.org/ishelp/topic_isxfunc_savestringstoutf8file.htm) preserves Unicode custom paths in `legacy-install-paths.txt` beside the new executable. User-data migration is implemented by the application; the installer does not modify per-user saves.

The in-app downloader accepts only the version-matched EchoPad setup executable from this repository, with the size and `sha256` digest supplied by the [GitHub release-assets API](https://docs.github.com/en/rest/releases/assets?apiVersion=2026-03-10). Verification checks downloaded bytes against that metadata; it does not represent a code-signing certificate. These development builds remain unsigned. A canceled download removes its partial file; a verified installer is retained in `Updates` if Windows cancels its launch.

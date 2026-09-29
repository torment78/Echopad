# Installer artwork

The installer follows the ElkaSoft family layout used by Throttle, VBAN Stream and VBAN Plug: Inno Setup's `modern dark polar includetitlebar` theme at 120% size, portrait artwork on the welcome/finish pages and a small ElkaSoft brand logo alongside the product explanation.

- `../graphics/echopad-vertical.png`: the approved 941 × 1672 EchoPad portrait, fitted proportionally without cropping or stretching.
- `../graphics/references/echopad-icon.png`: the existing EchoPad application icon in the small wizard header.
- `Assets/ElkaSoft.png`: unchanged ElkaSoft brand image reused from VBAN Stream's installer assets.
- `Assets/Ecopadc.ico`: existing installer file icon.

The previous generic sidebar/header images are no longer selected by the installer. Original application and installer ICO files remain unchanged. No new AI artwork was generated for this installer; it uses the approved [EchoPad graphics](../graphics/README.md).

For layout inspection without installing EchoPad, compile with Inno Setup 6.6 or newer:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' /DBrandingPreview installer/Echopad.iss
```

This emits `EchoPad-Installer-Preview.exe` under `artifacts/release`. It omits application files, shortcuts and launch actions, requires no elevation and blocks installation. Normal release builds omit `BrandingPreview` and produce the complete installer.

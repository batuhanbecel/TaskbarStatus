# TaskbarStatus

A tiny, text-only system monitor that sits on the Windows 11 taskbar.

![TaskbarStatus on the taskbar](docs/screenshot.png)

- **CPU** usage and temperature (via the [PawnIO](https://pawnio.eu) driver)
- **RAM** usage (% or GB)
- **GPU** usage and temperature (any vendor)
- **FPS** of the game / app in the foreground
- **Network** download / upload speed

Same font and weight as the taskbar clock, in one line or two. Sits next to the clock or at the left edge of the taskbar.
No window, no tray icon, ~80 KB.

[Türkçe açıklama aşağıda ↓](#türkçe)

## Install

```
winget install batuhanbecel.TaskbarStatus
```

Or download `TaskbarStatus-Setup-x.y.z.exe` from [Releases](../../releases).
The installer also installs the **PawnIO** driver (required, used for the CPU temperature) and sets TaskbarStatus up
to start with administrator rights at sign-in, without a UAC prompt.

A portable `TaskbarStatus.exe` is attached to each release too; it reads CPU temperature only when PawnIO is installed
and the app runs as administrator.

## Usage

| Action | What it does |
| --- | --- |
| **Right-click** | Menu: values to show, appearance, position, update rate, start with Windows, updates, … |
| **Drag** | Fine-tune the position along the taskbar (remembered) |
| **Hover** | Tooltip with details (GPU name, RAM in GB, FPS process, …) |
| **Double-click** | Open Task Manager |

TaskbarStatus checks GitHub Releases for new versions (twice a day, can be turned off). When one is available a small
dot appears after the text, and *Install update* in the menu downloads and runs the new installer.

Settings live in `HKCU\Software\TaskbarStatus`.

## How the values are measured

| Value | Source | Notes |
| --- | --- | --- |
| CPU % | `GetSystemTimes` | |
| CPU °C | PawnIO: AMD Zen `THM_TCON_CUR_TMP` (SMN), Intel `IA32_PACKAGE_THERM_STATUS` | Needs admin. Falls back to HWiNFO shared memory, LibreHardwareMonitor WMI, ACPI thermal zone |
| RAM | `GlobalMemoryStatusEx` | |
| GPU % | `GPU Engine` performance counters | Same method as Task Manager (busiest engine) |
| GPU °C | `D3DKMTQueryAdapterInfo` (adapter perf data) | Same source as Task Manager; WDDM 2.4+ driver |
| FPS | ETW, `Microsoft-Windows-DXGI` Present events | Needs admin. DirectX 10/11/12 and titles that present through DXGI |

## Building

No .NET SDK needed; the C# compiler that ships with Windows is used.

```bat
build.bat            :: TaskbarStatus.exe (downloads the PawnIO modules on first run)
build-installer.bat  :: dist\TaskbarStatus-Setup-x.y.z.exe (needs Inno Setup 6)
```

The version comes from `src/AssemblyInfo.cs`. Pushing a tag like `v1.2.0` makes GitHub Actions build, sign (if
configured), publish the release and submit the package to winget (if configured).

### Repository secrets

| Secret | Used for |
| --- | --- |
| `SIGN_CERT_PFX_BASE64`, `SIGN_CERT_PASSWORD` | Code signing (base64 of a `.pfx` code-signing certificate). Without them builds are unsigned |
| `WINGET_TOKEN` | GitHub token (classic, `public_repo`) used by `wingetcreate` to open the PR in `microsoft/winget-pkgs` |

## Credits

- [PawnIO](https://pawnio.eu) by namazso: driver and modules (LGPL-2.1), bundled unmodified.
- Temperature register details follow [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).

## License

[MIT](LICENSE)

---

## Türkçe

Windows 11 görev çubuğunda duran, sadece yazıdan oluşan küçük bir sistem izleyici: CPU kullanımı ve sıcaklığı, RAM,
GPU kullanımı ve sıcaklığı, öndeki oyunun FPS değeri ve ağ hızı.

**Kurulum:** `winget install batuhanbecel.TaskbarStatus` ya da [Releases](../../releases) sayfasından
`TaskbarStatus-Setup-x.y.z.exe`. Kurulum, CPU sıcaklığı için gereken **PawnIO** sürücüsünü de kurar ve uygulamayı
oturum açılınca UAC sormadan yönetici olarak başlatacak şekilde ayarlar.

**Kullanım:** Sağ tıkla menüyü aç (gösterilecek değerler, görünüm, konum: saatin yanı / görev çubuğunun solu,
güncelleme sıklığı, Windows ile başlat, güncellemeler). Sürükleyerek ince ayar yap, üzerine gelince detayları gör,
çift tıklayınca Görev Yöneticisi açılır.

**Güncellemeler:** Yeni sürüm çıkınca yazının sonunda küçük bir nokta belirir; menüdeki *Güncellemeyi yükle* yeni
kurulumu indirip çalıştırır.

**Derleme:** `build.bat` (exe) ve `build-installer.bat` (kurulum, Inno Setup 6 gerekir).

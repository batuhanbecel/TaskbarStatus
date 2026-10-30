# TaskbarStatus

A compact system monitor with optional monochrome glyphs that lives inside the Windows 11 taskbar.

![TaskbarStatus on the taskbar](docs/screenshot.png)

*Earlier appearance shown above; glyphs can now be enabled from the Appearance menu.*

- **CPU** usage and temperature (via the [PawnIO](https://pawnio.eu) driver)
- **GPU** usage and temperature (any vendor)
- **RAM** usage (% or GB)
- **FPS** of the game / app in the foreground
- **Network** download / upload speed

Same font and weight as the taskbar clock, in one line or two. The widget is part of the taskbar itself, so it stays
sharp when the Start menu or other shell flyouts open. No separate app window or tray icon.

[Türkçe açıklama aşağıda ↓](#türkçe)

## What's new in 1.2.6

- Display order: **CPU → GPU → RAM → FPS → network**.
- Optional glyphs appear **before the label**, followed by the value. CPU/GPU temperature readings have their own thermometer glyph.
- Equal spacing between groups, based on the current text width, in both one-line and two-line layouts.
- Snipping Tool capture selection preserves the widget's existing visibility and placement.
- An opt-in diagnostic mode helps distinguish the local executable from an older installed copy.

## Install

```
winget install batuhanbecel.TaskbarStatus
```

Or download `TaskbarStatus-Setup-x.y.z.exe` from [Releases](../../releases).
The installer also installs the **PawnIO** driver (required, used for the CPU temperature) and sets TaskbarStatus up
to start with administrator rights at sign-in, without a UAC prompt.

A portable `TaskbarStatus.exe` is attached to each release too. PawnIO CPU temperature readings need the driver and
administrator rights; FPS readings also need administrator rights. If an elevated startup task is already installed,
a normal launch can redirect to that task's installed copy. Use `--dev` or `--diagnose` when testing a local build.

## Usage

| Action | What it does |
| --- | --- |
| **Right-click** | Windows 11 style menu: values to show, appearance, position, update rate, language, start with Windows, updates |
| **Drag** | Fine-tune the position along the taskbar (remembered) |
| **Hover** | Tooltip with details (GPU name, RAM in GB, FPS process, …) |
| **Double-click** | Open Task Manager |

**Menu highlights**

- *Show*: CPU usage, CPU temperature, RAM, GPU usage, GPU temperature, FPS, network speed (each on/off)
- *Appearance*: two lines like the clock, bold text, labels, highlight high values, RAM in GB, text size
- *Appearance → Show glyphs*: monochrome symbols for every reading, including separate temperature symbols.
  Enabled by default, with **glyph → label → value** ordering. Symbols and labels can be toggled independently.
  Groups have equal 12 px gaps at 100% scaling
  (6 px between readings within a CPU/GPU group); positions follow the current text width.
- *Position*: next to the clock or at the left edge of the taskbar
- *Language / Dil*: Windows language, Türkçe, English
- *Updates*: check automatically, install automatically in the background, check now

Settings live in `HKCU\Software\TaskbarStatus`.

## Updates

TaskbarStatus checks GitHub Releases shortly after it starts and then every 6 hours. A new version is downloaded in the
background, verified against the SHA-256 digest GitHub publishes for the release asset, and installed silently; the app
restarts itself a few seconds later. Installs wait while a fullscreen app (a game) is in front.

Turn this off under *Updates → Install automatically in the background*; a found update then appears at the top of the
menu instead (and as a small dot after the text).

## How the values are measured

| Value | Source | Notes |
| --- | --- | --- |
| CPU % | `GetSystemTimes` | |
| CPU °C | PawnIO: AMD Zen `THM_TCON_CUR_TMP` (SMN), Intel `IA32_PACKAGE_THERM_STATUS` | Needs admin. Falls back to HWiNFO shared memory, LibreHardwareMonitor WMI, ACPI thermal zone |
| RAM | `GlobalMemoryStatusEx` | |
| GPU % | `GPU Engine` performance counters | Same method as Task Manager (busiest engine) |
| GPU °C | `D3DKMTQueryAdapterInfo` (adapter perf data) | Same source as Task Manager; WDDM 2.4+ driver |
| FPS | ETW, `Microsoft-Windows-DxgKrnl` Present events (graphics kernel) | Needs admin. Any API: DirectX 9–12, OpenGL, Vulkan |

## How it sits in the taskbar

The widget is a layered child window of `Shell_TrayWnd`, drawn with GDI ClearType text turned into per-pixel alpha.
Being a child of the taskbar keeps it above the taskbar's background and out of the blur Windows draws when Start opens.
If *Show over fullscreen apps* is on, it detaches into a topmost overlay while a fullscreen app is in front (the taskbar
hides itself then). When Explorer restarts, the widget goes away with the old taskbar and the app starts itself again.

The fullscreen overlay is kept inside the monitor even when the hidden taskbar moves off-screen or a game changes
the resolution. Automatic update installation waits until fullscreen mode ends, including when the overlay is enabled.
Use borderless fullscreen for games: an ordinary desktop overlay is not guaranteed to appear over exclusive fullscreen.
While Snipping Tool's capture selection is active, the widget preserves its existing visibility and position;
it does not raise itself above the selection UI or run a pending installer.

## Building

No .NET SDK needed; the C# compiler that ships with Windows is used.

```bat
build.bat            :: TaskbarStatus.exe (downloads the PawnIO modules on first run)
build-installer.bat  :: dist\TaskbarStatus-Setup-x.y.z.exe (needs Inno Setup 6)
tests\run.bat        :: 13 overlay layout and screen capture policy regression checks
```

`TaskbarStatus.exe --dev` runs a second copy next to the installed one, with its own settings
(`HKCU\Software\TaskbarStatus.Dev`), no elevated-task hop and no update checks.

For visibility troubleshooting, `TaskbarStatus.exe --diagnose` runs that exact executable
with the usual settings, bypasses the installed task and disables automatic update checks.
It logs foreground process/class and widget visibility/position changes to `diagnostic.log`
next to the executable. Close the existing copy first. Normal launches do not write this log.
This flag does not elevate the process: run as administrator if testing PawnIO CPU temperatures or FPS.
Manual UI checks are documented in [tests/fullscreen-manual.md](tests/fullscreen-manual.md).

The version comes from `src/AssemblyInfo.cs`. Pushing a tag like `v1.2.3` makes GitHub Actions build, sign (if
configured), publish the release and submit the package to winget (if configured). Installed copies pick the release up
on their own within about 6 hours.

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

Windows 11 görev çubuğunun içinde duran, isteğe bağlı tek renkli simgeleri olan küçük bir sistem izleyici:
CPU kullanımı ve sıcaklığı, GPU kullanımı ve sıcaklığı, RAM, öndeki oyunun FPS değeri ve ağ hızı.
Görev çubuğunun bir parçası olduğu için Başlat
menüsü açıldığında da net kalır.

**1.2.6 yenilikleri:** Sıralama **CPU → GPU → RAM → FPS → ağ**. Simgeler etiketin önünde yer alır:
**simge → etiket → değer**. CPU/GPU sıcaklıklarının ayrıca termometre simgesi vardır.
**Sağ tık → Görünüm → Simgeleri göster** ile açılıp kapatılır; etiket ayarı bağımsızdır.
Gruplar arasındaki boşluk %100 ölçeklemede 12 px, CPU/GPU grubunun içindeki değerler arasında 6 px'tir.
Boşluklar güncel metin genişliğine göre hesaplanır; basamak sayısı değiştiğinde sonraki grupların konumu da değişebilir.

**Kurulum:** `winget install batuhanbecel.TaskbarStatus` ya da [Releases](../../releases) sayfasından
`TaskbarStatus-Setup-x.y.z.exe`. Kurulum, CPU sıcaklığı için gereken **PawnIO** sürücüsünü de kurar ve uygulamayı
oturum açılınca UAC sormadan yönetici olarak başlatacak şekilde ayarlar.

**Kullanım:** Sağ tıkla Windows 11 tarzı menüyü aç: gösterilecek değerler, görünüm (iki satır, kalın yazı, yazı
boyutu…), konum (saatin yanı / görev çubuğunun solu), güncelleme sıklığı, dil (Windows dili / Türkçe / English),
Windows ile başlat, güncellemeler. Sürükleyerek ince ayar yap, üzerine gelince detayları gör, çift tıklayınca Görev
Yöneticisi açılır.

**Ekran alıntısı ve oyunlar:** Ekran Alıntısı'nın seçim katmanı açıkken göstergenin mevcut görünürlüğü ve konumu
korunur. *Tam ekranda da göster* seçeneği oyun üstünde gösterimi açar. Özel tam ekran modunda masaüstü
göstergeleri görünmeyebilir; bu durumda oyunu kenarlıksız tam ekran modunda kullanın.

**CPU sıcaklığı görünmüyorsa:** PawnIO'nun kurulu olduğundan ve uygulamanın yönetici olarak çalıştığından emin olun.
Tanılama modu kendiliğinden yönetici yetkisi almaz. Yerel EXE'nin normal açılışı, kayıtlı başlangıç görevi varsa
kurulu eski kopyaya yönlenebilir; yerel derlemeyi doğrudan denemek için `--diagnose` kullanın.

**Güncellemeler:** Uygulama açıldıktan kısa süre sonra ve her 6 saatte bir GitHub'a bakar. Yeni sürümü arka planda
indirir, GitHub'ın yayınladığı SHA-256 ile doğrular, sessizce kurar ve birkaç saniye içinde kendini yeniden başlatır.
Tam ekran bir oyun açıksa kurulum, oyundan çıkana kadar bekler. *Güncellemeler → Arka planda otomatik yükle* ile
kapatılabilir.

**Derleme:** `build.bat` (exe) ve `build-installer.bat` (kurulum, Inno Setup 6 gerekir). Kurulu sürümün yanında test
için `TaskbarStatus.exe --dev` kullanılabilir. `tests\run.bat` 13 otomatik kontrolü çalıştırır.
`TaskbarStatus.exe --diagnose`, mevcut kopya kapatıldıktan sonra normal ayarlarla doğrudan açılır;
otomatik güncelleme denetimini kapatır ve pencere durumu değişikliklerini EXE yanındaki `diagnostic.log` dosyasına yazar.

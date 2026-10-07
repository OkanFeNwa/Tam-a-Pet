# Tam-a-Pet

Tamagotchi-style virtual pets that live on your Windows desktop. It starts with cats: they walk, sleep, play with a ball, eat from a bowl, get stroked, and groom or fight each other. More kinds of pets are planned. It is a native program (.NET 8, WinForms) built to be as light as possible: at rest it uses a few MB of memory and almost no CPU.

*Italiano: pet virtuali in stile tamagotchi sul desktop di Windows. Vedi il [riassunto in italiano](#italiano) in fondo.*

[![Watch the video](docs/img/brag.jpg)](docs/brag.mp4)

![Settings: the cats](docs/img/cats.png)

> **Status: 0.0.1 (pre-release).** Work in progress: more features and bug fixes are coming before the first real release.

## Download

From the [Releases](../../releases/latest) page:

| File | What it is |
|---|---|
| **TamAPet-Setup.exe** | Installer: installs for your user (no administrator rights), creates the Start menu entry, the desktop shortcut (optional), start with Windows (optional) and the uninstaller in *Installed apps*. |
| **TamAPet-Portable.exe** | Portable version: a single file, runs from anywhere, installs nothing. |

Both already include everything (even .NET): nothing else to install. Requirements: Windows 10/11, 64-bit.

> Windows SmartScreen may warn that the app is "not recognised", because the executable is not digitally signed: *More info → Run anyway*. SHA-256 checksums are in `SHA256SUMS.txt` in the release.

## What it does

- **Multiple cats** (up to 8), each with its own name, character (balanced, playful, lazy, greedy, chatty, attention-seeking), fur and eye colours, patterns (tabby, patched, calico) and stats: hunger, happiness, energy.
- **Objects**: bowl, bed and ball, each with its own colour; the cats use them when they need to. They can be shared between all the cats.
- **Interactions**: click and stroke (hold and move slowly), a fast mouse near the cat makes it jump, too many clicks make it angry. Between themselves the cats groom or fight.
- **Real sounds** (meows, purring, chewing, ball) with a mixer: master volume, per cat and per kind of sound.
- **Settings** from the notification-area icon: always on top, start with Windows, language (English / Italian), updates.
- **Updates**: at start it quietly checks GitHub for a newer version and tells you; it downloads and installs **only when you press the button** in *Settings → General → About*. The automatic check can be turned off.

English is the default language; you can switch to Italian in *Settings → General → Language*.

Your data (settings, stats, object positions) lives in `%APPDATA%\Tam-a-Pet\settings.ini`.

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd native
dotnet build -c Release          # to try it: native\bin\Release\net8.0-windows\TamAPet.exe
.\publish.ps1                    # the two executables to distribute, in native\dist
```

To make a release: raise `<Version>` in `native/TamAPet.csproj`, update `CHANGELOG.md`, then create and push the tag (`git tag v0.0.1 && git push origin v0.0.1`): the `.github/workflows/release.yml` workflow builds and publishes the release with installer, portable version and checksums.

The installer is the app itself: an executable named `TamAPet-Setup*.exe` shows the install wizard (see `native/Installer.cs`); the installed app removes itself with `--uninstall`.

The root folder also holds the first Electron prototype (`main.js`, `index.html`, ...), kept only for reference: the real app is in `native/`.

## Credits

Sounds from [OpenGameArt.org](https://opengameart.org) (CC0): see [CREDITS.md](CREDITS.md). Code licence: [MIT](LICENSE).

## Italiano

Tam-a-Pet mette dei pet virtuali sul desktop di Windows, in stile tamagotchi. Si parte dai gatti: camminano, dormono, giocano con la pallina, mangiano dalla ciotola, si fanno accarezzare e litigano o si fanno le coccole tra loro. Ogni gatto ha nome, carattere, colori, varianti, bisogni, suoni e oggetti propri, con un mixer per i volumi.

**Scarica** da [Releases](../../releases/latest): `TamAPet-Setup.exe` (installer per il tuo utente, senza permessi di amministratore) oppure `TamAPet-Portable.exe` (un solo file, non installa nulla). Entrambi includono .NET; Windows 10/11 a 64 bit. L'eseguibile non è firmato, quindi SmartScreen può avvisare (*Ulteriori informazioni → Esegui comunque*); i checksum sono in `SHA256SUMS.txt`.

**Aggiornamenti:** all'avvio controlla in silenzio su GitHub e te lo dice; scarica e installa solo quando premi il pulsante in *Impostazioni → Generale → Informazioni*. La lingua predefinita è l'inglese; si cambia in *Impostazioni → Generale → Lingua*.

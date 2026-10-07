# Tam-a-Pet

Un gatto (o più) in stile tamagotchi che vive sul desktop di Windows: cammina, dorme, gioca con la pallina, mangia dalla ciotola, si fa accarezzare e litiga o si fa le coccole con gli altri gatti. È un programma nativo (.NET 8, WinForms) pensato per essere il più leggero possibile: a riposo usa pochi MB di memoria e quasi nessuna CPU.

*A tamagotchi-style cat (or several) living on your Windows desktop. Native .NET 8 app, very light on memory and CPU. See the [English summary](#english) below.*

![Impostazioni: i gatti](docs/img/cats.png)

## Scarica

Dalla pagina [Releases](../../releases/latest):

| File | Cos'è |
|---|---|
| **TamAPet-Setup.exe** | Installer: installa per il tuo utente (nessun permesso di amministratore), crea la voce nel menu Start, il collegamento sul desktop (a scelta), l'avvio con Windows (a scelta) e il disinstallatore in *App installate*. |
| **TamAPet-Portable.exe** | Versione portatile: un solo file, si avvia dove vuoi, non installa nulla. |

Entrambi contengono già tutto (anche .NET): non serve installare altro. Requisiti: Windows 10/11 a 64 bit.

> Windows SmartScreen può avvisare che l'app "non è riconosciuta", perché l'eseguibile non è firmato digitalmente: *Ulteriori informazioni → Esegui comunque*. I checksum SHA-256 sono nel file `SHA256SUMS.txt` della release.

## Cosa fa

- **Più gatti** (fino a 8), ognuno con nome, carattere (equilibrato, giocherellone, pigro, goloso, chiacchierone, cerca attenzioni), colori di pelo e occhi, varianti (tigrato, a chiazze, calico) e statistiche proprie: fame, felicità, energia.
- **Oggetti**: ciotola, cuccia e pallina, ognuno con il proprio colore; i gatti li usano quando ne hanno bisogno. Si possono condividere tra tutti i gatti.
- **Interazioni**: clic e coccole (tieni premuto e muovi piano), il mouse veloce vicino al gatto lo fa saltare, troppi clic lo fanno arrabbiare. Tra loro i gatti si leccano o litigano.
- **Suoni** veri (miagolii, fusa, masticare, pallina) con un mixer: volume generale, per gatto e per tipo di suono.
- **Impostazioni** dall'icona nell'area di notifica: sempre in primo piano, avvio con Windows, lingua (italiano / inglese), aggiornamenti.
- **Aggiornamenti**: all'avvio controlla in silenzio se c'è una versione più nuova su GitHub e te lo dice; scarica e installa **solo quando premi il pulsante** in *Impostazioni → Generale → Informazioni*. Si può disattivare il controllo automatico.

I dati (impostazioni, statistiche, posizione degli oggetti) stanno in `%APPDATA%\Tam-a-Pet\settings.ini`.

## Compilare dal sorgente

Serve l'[SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd native
dotnet build -c Release          # per provarla: native\bin\Release\net8.0-windows\TamAPet.exe
.\publish.ps1                    # i due eseguibili da distribuire in native\dist
```

Per fare una release: alza `<Version>` in `native/TamAPet.csproj`, aggiorna `CHANGELOG.md`, poi crea e pusha il tag (`git tag v0.1.0 && git push origin v0.1.0`): il workflow `.github/workflows/release.yml` compila e pubblica la release con installer, versione portatile e checksum.

L'installer è l'app stessa: un eseguibile chiamato `TamAPet-Setup*.exe` mostra la procedura di installazione (vedi `native/Installer.cs`); l'app installata con `--uninstall` si rimuove.

La cartella principale contiene anche il primo prototipo in Electron (`main.js`, `index.html`, ...), conservato solo come riferimento: l'app vera è in `native/`.

## Crediti

Suoni da [OpenGameArt.org](https://opengameart.org) (CC0): vedi [CREDITS.md](CREDITS.md). Licenza del codice: [MIT](LICENSE).

## English

Tam-a-Pet puts one or more cats on your Windows desktop. They wander, sleep, play with a ball, eat from a bowl, get stroked, and groom or fight each other. Each cat has its own name, character, colours, patterns (tabby, patched, calico), needs, sounds and objects; there is a mixer for the volumes.

**Download** from [Releases](../../releases/latest): `TamAPet-Setup.exe` (per-user installer, no admin rights) or `TamAPet-Portable.exe` (single file, nothing installed). Both include .NET; Windows 10/11 x64. The exe is unsigned, so SmartScreen may warn you (*More info → Run anyway*); checksums are in `SHA256SUMS.txt`.

**Updates:** the app quietly checks GitHub at start and tells you when a newer release exists; it downloads and installs only when you press the button in *Settings → General → About* (the automatic check can be turned off).

**Build:** `cd native && dotnet build -c Release`; `./publish.ps1` makes both executables. Releases are built by GitHub Actions when a `vX.Y.Z` tag is pushed.

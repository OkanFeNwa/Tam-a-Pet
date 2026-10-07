const { app, BrowserWindow, ipcMain, Tray, Menu, screen } = require('electron');
const path = require('path');
const fs = require('fs');
const strings = require('./i18n');

// Cat settings, edited from the settings window and saved in the user data folder
const defaults = { nearRange: 150, jumpSpeed: 800, jumpCooldown: 5, wander: 0.08, walkSpeed: 2, cycle: 1328, lang: 'it' };
const settingsFile = () => path.join(app.getPath('userData'), 'settings.json');
let settings = { ...defaults };
let settingsWindow = null;

let petWindow = null;
let tray = null;
let alwaysOnTop = true;

// Software rendering: drops the GPU process (~1 process, tens of MB); the sprite is tiny.
app.disableHardwareAcceleration();
// Cap the V8 heap of the renderer.
app.commandLine.appendSwitch('js-flags', '--max-old-space-size=32');

// Only one instance (a second launch would spawn another set of processes)
if (!app.requestSingleInstanceLock()) app.quit();

app.whenReady().then(() => {
  try { settings = { ...defaults, ...JSON.parse(fs.readFileSync(settingsFile(), 'utf8')) }; } catch {}
  createPetWindow();
  createTray();
});

// Closing the window quits the app (no zombie processes). Use tray "Hide Pet" to keep it in background.
app.on('window-all-closed', () => app.quit());

function createPetWindow() {
  petWindow = new BrowserWindow({
    width: 256,
    height: 256,
    transparent: true,
    frame: false,
    alwaysOnTop: true,
    skipTaskbar: false,
    resizable: false,
    icon: path.join(__dirname, 'icon.ico'),
    webPreferences: {
      nodeIntegration: true,
      contextIsolation: false,
      backgroundThrottling: false,
    },
  });

  petWindow.setIgnoreMouseEvents(true, { forward: true });
  petWindow.loadFile('index.html');
  petWindow.webContents.setBackgroundThrottling(false);

  // The window ignores the mouse, so feed the cursor position to the renderer (only when it moves)
  let last = '';
  const poll = setInterval(() => {
    if (!petWindow || !petWindow.isVisible()) return;
    const p = screen.getCursorScreenPoint();
    const key = `${p.x},${p.y}`;
    if (key === last) return;
    last = key;
    petWindow.webContents.send('cursor', p.x, p.y);
  }, 50);

  petWindow.on('closed', () => {
    clearInterval(poll);
    petWindow = null;
  });
}

function openSettings() {
  if (settingsWindow) return settingsWindow.focus();
  settingsWindow = new BrowserWindow({
    width: 620,
    height: 520,
    resizable: false,
    autoHideMenuBar: true,
    icon: path.join(__dirname, 'icon.ico'),
    webPreferences: { nodeIntegration: true, contextIsolation: false }
  });
  settingsWindow.loadFile('settings.html');
  settingsWindow.on('closed', () => { settingsWindow = null; });
}

function buildMenu() {
  const t = (strings[settings.lang] || strings.it).tray;
  return Menu.buildFromTemplate([
    {
      label: t.alwaysOnTop,
      type: 'checkbox',
      checked: alwaysOnTop,
      click: () => {
        alwaysOnTop = !alwaysOnTop;
        petWindow.setAlwaysOnTop(alwaysOnTop);
      }
    },
    { label: t.settings, click: openSettings },
    { type: 'separator' },
    { label: t.showPet, click: () => { if (petWindow) petWindow.show(); } },
    { label: t.hidePet, click: () => { if (petWindow) petWindow.hide(); } },
    { type: 'separator' },
    { label: t.exit, click: () => app.quit() }
  ]);
}

function createTray() {
  tray = new Tray(path.join(__dirname, 'icon.ico'));
  tray.setContextMenu(buildMenu());
  tray.setToolTip((strings[settings.lang] || strings.it).tray.tooltip);
}

// setBounds (not setPosition) so DPI scaling can't resize the window while it moves
ipcMain.on('move', (event, x, y) => {
  if (petWindow) petWindow.setBounds({ x, y, width: 256, height: 256 });
});

ipcMain.on('set-clickable-region', (event, region) => {
  if (region) {
    petWindow.setIgnoreMouseEvents(false);
  } else {
    petWindow.setIgnoreMouseEvents(true, { forward: true });
  }
});

ipcMain.on('get-workarea', (event, x, y) => { event.returnValue = screen.getDisplayNearestPoint({ x, y }).workArea; });

ipcMain.on('get-settings', (event) => { event.returnValue = settings; });

ipcMain.on('set-settings', (event, s) => {
  const langChanged = s.lang !== settings.lang;
  settings = { ...defaults, ...s };
  if (langChanged && tray) tray.setContextMenu(buildMenu());
  fs.writeFile(settingsFile(), JSON.stringify(settings), () => {});
  if (petWindow) petWindow.webContents.send('settings', settings);
});

ipcMain.handle('get-defaults', () => defaults);

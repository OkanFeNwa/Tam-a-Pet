const { ipcRenderer } = require('electron');

// The window is a fixed 256x256 box that follows the cat around the screen:
// the cat is always drawn at the same spot inside it, and "moving" = moving the window.
const WIN = 256;
const CAT_X = 64, CAT_Y = 20;                  // sprite offset in the window (left / bottom)
const CAT_CX = CAT_X + 64, CAT_CY = WIN - CAT_Y - 64;  // cat center inside the window

const pet = {
  hunger: 100,
  happiness: 100,
  energy: 100,
  state: 'idle',
  target: null,          // {x, y}: window position to walk to
  facingRight: true
};
const win = { x: window.screenX, y: window.screenY };  // window position on screen

const petEl = document.getElementById('pet');
let frame = 0;

// User settings (edited in the settings window, see settings.html): nearRange, jumpSpeed,
// jumpCooldown, wander, walkSpeed, cycle
let cfg = ipcRenderer.sendSync('get-settings');
ipcRenderer.on('settings', (e, s) => { cfg = s; });

const SPAM_CLICKS = 5;      // clicks within 2s = too many

// Sprite rows (0-based) and frame counts
const animations = {
  idle:    { row: 0, frames: 4 },
  near:    { row: 1, frames: 4 },  // idle while the mouse is close
  lick:    { row: 2, frames: 4 },
  walk:    { row: 4, frames: 8 },
  run:     { row: 5, frames: 8 },
  sit:     { row: 6, frames: 1 },
  sleep:   { row: 6, frames: 4 },
  play:    { row: 7, frames: 6 },  // click: plays with the mouse
  pounce:  { row: 8, frames: 7, once: true },  // fast mouse nearby: jumps after it (single jump)
  frenzy:  { row: 9, frames: 8 }   // too many clicks
};

let currentAnim = 'idle';
let lockUntil = 0;        // while Date.now() < lockUntil the state machine leaves the state alone
let mouseNear = false;
let clicks = [];
let cursor = { x: 0, y: 0 };  // screen coords
let lastCursorAt = Date.now();
let nextJumpAt = 0;

function setState(state, ms = 0) {
  pet.state = state;
  lockUntil = Date.now() + ms;
}

// Window positions that keep the whole cat inside the work area of the display near (x, y)
function bounds(x, y) {
  // the screen module only exists in the main process
  const wa = ipcRenderer.sendSync('get-workarea', Math.round(x), Math.round(y));
  return {
    minX: wa.x - CAT_X, maxX: wa.x + wa.width - CAT_X - 128,
    minY: wa.y - (WIN - CAT_Y - 128), maxY: wa.y + wa.height - (WIN - CAT_Y)
  };
}

function clampTarget(x, y) {
  const b = bounds(x + CAT_CX, y + CAT_CY);
  return { x: Math.max(b.minX, Math.min(b.maxX, x)), y: Math.max(b.minY, Math.min(b.maxY, y)) };
}

function randomTarget() {
  const b = bounds(win.x + CAT_CX, win.y + CAT_CY);
  let t;
  for (let i = 0; i < 10; i++) {
    t = { x: b.minX + Math.random() * (b.maxX - b.minX), y: b.minY + Math.random() * (b.maxY - b.minY) };
    if (Math.hypot(t.x - win.x, t.y - win.y) > 150) break;  // go somewhere actually far
  }
  return t;
}

function updateNear() {
  const dist = Math.hypot(cursor.x - win.x - CAT_CX, cursor.y - win.y - CAT_CY);
  mouseNear = dist < cfg.nearRange;
}

// Cursor position comes from the main process, since the window ignores the mouse outside the sprite.
ipcRenderer.on('cursor', (e, cx, cy) => {
  const now = Date.now();
  const speed = Math.hypot(cx - cursor.x, cy - cursor.y) / Math.max(1, now - lastCursorAt) * 1000;  // px/s
  lastCursorAt = now;
  cursor = { x: cx, y: cy };
  updateNear();
  // Sprite faces right; flip it toward the mouse when standing still
  if (mouseNear && !pet.target && pet.state !== 'sleep') pet.facingRight = cx > win.x + CAT_CX;

  // Fast mouse movement near the cat: jump to where the mouse is, then wait for the cooldown
  if (mouseNear && speed >= cfg.jumpSpeed && now >= nextJumpAt && now >= lockUntil && pet.state !== 'sleep') {
    pet.target = clampTarget(cx - CAT_CX, cy - CAT_CY);
    frame = 0;
    setState('pounce', cfg.cycle + 100);
    nextJumpAt = now + cfg.jumpCooldown * 1000;
  }
});

// Click handler on sprite only
petEl.addEventListener('mouseenter', () => {
  ipcRenderer.send('set-clickable-region', true);
});

petEl.addEventListener('mouseleave', () => {
  ipcRenderer.send('set-clickable-region', false);
});

petEl.addEventListener('click', () => {
  pet.hunger = Math.min(100, pet.hunger + 10);
  pet.happiness = Math.min(100, pet.happiness + 15);

  const now = Date.now();
  clicks = clicks.filter(t => now - t < 2000);
  clicks.push(now);
  pet.target = null;
  if (clicks.length >= SPAM_CLICKS) {
    setState('frenzy', 2500);
  } else {
    setState('play', cfg.cycle);
  }
});

// Right-click: put the cat to sleep / wake it up
petEl.addEventListener('contextmenu', (e) => {
  e.preventDefault();
  pet.target = null;
  setState(pet.state === 'sleep' ? 'idle' : 'sleep');
});

// Animation loop: every animation takes the same time per cycle (like the 8-frame ones),
// so frame duration = cfg.cycle / frames
function animate() {
  updateNear();  // the cat may have walked toward/away from a still mouse
  const name = pet.state === 'idle' && mouseNear ? 'near' : pet.state;
  if (currentAnim !== name) {
    frame = 0;
    currentAnim = name;
  }

  const anim = animations[name];

  // Sprite sheet: 256x320px (8 cols x 10 rows of 32x32px sprites), shown at 4x
  const x = (frame % anim.frames) * -128;
  const y = anim.row * -128;

  petEl.style.transform = pet.facingRight ? 'scaleX(1)' : 'scaleX(-1)';
  petEl.style.backgroundPosition = `${x}px ${y}px`;

  frame = anim.once ? Math.min(frame + 1, anim.frames - 1) : (frame + 1) % anim.frames;
  setTimeout(animate, cfg.cycle / anim.frames);
}
animate();

// Movement loop: moves the window toward the target (runs only while walking)
setInterval(() => {
  if (!pet.target) return;
  const speed = cfg.walkSpeed * (pet.state === 'walk' ? 1 : pet.state === 'pounce' ? 3 : 2);
  const dx = pet.target.x - win.x, dy = pet.target.y - win.y;
  const dist = Math.hypot(dx, dy);

  if (dist < speed) {
    win.x = pet.target.x;
    win.y = pet.target.y;
    pet.target = null;
    if (pet.state === 'walk' || pet.state === 'run') pet.state = 'idle';
  } else {
    win.x += dx / dist * speed;
    win.y += dy / dist * speed;
    if (Math.abs(dx) > 1) pet.facingRight = dx > 0;
  }
  ipcRenderer.send('move', Math.round(win.x), Math.round(win.y));
}, 16);

// Pet behavior and stats decay
let idleTimer = 0;

setInterval(() => {
  pet.hunger = Math.max(0, pet.hunger - 0.5);
  pet.happiness = Math.max(0, pet.happiness - 0.3);
  pet.energy = Math.max(0, pet.energy - 0.2);

  if (pet.state === 'sleep') {
    // Recover energy while asleep, wake up when rested
    pet.energy = Math.min(100, pet.energy + 1.5);
    pet.target = null;
    idleTimer = 0;
    if (pet.energy >= 100) pet.state = 'idle';
  } else if (Date.now() < lockUntil) {
    // click / pounce / lick animation playing
  } else if (pet.energy < 20) {
    setState('sleep');
    pet.target = null;
  } else if (pet.hunger < 30) {
    pet.state = 'sit';
    pet.target = null;
    idleTimer = 0;
  } else if (pet.target) {
    // walking/running - controlled by movement loop
  } else {
    if (pet.state === 'sit' || pet.state === 'pounce' || pet.state === 'lick') pet.state = 'idle';
    idleTimer++;
    const rand = Math.random();

    if (rand < cfg.wander) {
      pet.target = randomTarget();
      pet.state = Math.random() < 0.3 && pet.energy > 50 ? 'run' : 'walk';
      idleTimer = 0;
    } else if (idleTimer > 5 && rand < 0.1) {
      setState('lick', 2500);
      idleTimer = 0;
    } else {
      pet.state = 'idle';
    }
  }
}, 1000);

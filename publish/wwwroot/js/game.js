// Star Siege - renderer, input e audio. La logica vive in C# (GameEngine).
const W = 672, H = 768, S = 3;

// ---------------------------------------------------------------- Sprite (pixel art originale)
const SPRITES = {
    // Alieno "Drone" (riga alta) - 2 frame
    a0: [
        "....XXXX....",
        "...XXXXXX...",
        "..XX.XX.XX..",
        "..XXXXXXXX..",
        "....X..X....",
        "...X.XX.X...",
        "..X......X..",
        "...X....X...",
    ],
    a1: [
        "....XXXX....",
        "...XXXXXX...",
        "..XX.XX.XX..",
        "..XXXXXXXX..",
        "....X..X....",
        "...X.XX.X...",
        "....X..X....",
        "..XX....XX..",
    ],
    // Alieno "Granchio-stella" (righe centrali)
    b0: [
        "X....XX....X",
        ".X..XXXX..X.",
        "..XXX..XXX..",
        ".XX.XXXX.XX.",
        "XXXXXXXXXXXX",
        "..XX....XX..",
        ".X..X..X..X.",
        "X..........X",
    ],
    b1: [
        "..X..XX..X..",
        "..X.XXXX.X..",
        "..XXX..XXX..",
        ".XX.XXXX.XX.",
        "XXXXXXXXXXXX",
        "X.XX....XX.X",
        "..X.X..X.X..",
        ".X........X.",
    ],
    // Alieno "Medusa" (righe basse)
    c0: [
        "...XXXXXX...",
        ".XXXXXXXXXX.",
        "XXX..XX..XXX",
        "XXXXXXXXXXXX",
        ".X.X.XX.X.X.",
        ".X.X....X.X.",
        "X..X....X..X",
        "...X....X...",
    ],
    c1: [
        "...XXXXXX...",
        ".XXXXXXXXXX.",
        "XXX..XX..XXX",
        "XXXXXXXXXXXX",
        ".X.X.XX.X.X.",
        "..X.X..X.X..",
        "..X.X..X.X..",
        ".X...XX...X.",
    ],
    player: [
        "......X......",
        ".....XXX.....",
        ".....XXX.....",
        "..X.XXXXX.X..",
        ".XXXXXXXXXXX.",
        "XXXXX.X.XXXXX",
        "XXXXXXXXXXXXX",
        "XX.XX...XX.XX",
    ],
    ufo: [
        "......XXXX......",
        "...XXXXXXXXXX...",
        "..XXXXXXXXXXXX..",
        ".XX.XX.XX.XX.XX.",
        "XXXXXXXXXXXXXXXX",
        "..XXX..XX..XXX..",
        "...X........X...",
    ],
    bomb0: ["X..", ".X.", "..X", ".X.", "X..", ".X.", "..X"],
    bomb1: ["..X", ".X.", "X..", ".X.", "..X", ".X.", "X.."],
    bomb2: [".X.", ".X.", "XXX", ".X.", ".X.", "XXX", ".X."],
    bomb3: [".X.", "XXX", ".X.", ".X.", "XXX", ".X.", ".X."],
    boomAlien: [
        "X...X..X...X",
        ".X...XX...X.",
        "..X......X..",
        "XX........XX",
        "..X......X..",
        ".X...XX...X.",
        "X...X..X...X",
        "............",
    ],
    boomShot: ["X..X..", ".XXXX.", "XXXXXX", ".XXXX.", "X..X.."],
    boomPlayer0: [
        "...X...X.....",
        "X....X...X..X",
        "..X.XXXX.....",
        ".X..XXXXX.X..",
        "...XXXXXXX...",
        "..XXXXXXXXX..",
        ".XXXXXXXXXXX.",
        "XXXXXXXXXXXXX",
    ],
    boomPlayer1: [
        "X.....X....X.",
        "...X.....X...",
        "X..X.XX.X...X",
        "....XXXX.X...",
        ".X.XXXXXXX..X",
        "..XXXXXXXXX..",
        ".XXXXXXXXXXX.",
        "XXXXXXXXXXXXX",
    ],
};

const COLORS = {
    a: "#ff4fd8", b: "#4fd8ff", c: "#7dff6a",
    player: "#ffe066", ufo: "#ff5a5a", bomb: "#ffffff",
    shot: "#ffffff", shield: "#3ad17c", boom: "#ffb84f",
};

const cache = {};
function sprite(name, color) {
    const key = name + color;
    if (cache[key]) return cache[key];
    const rows = SPRITES[name];
    const c = document.createElement("canvas");
    c.width = rows[0].length * S; c.height = rows.length * S;
    const g = c.getContext("2d");
    g.fillStyle = color;
    rows.forEach((row, y) => [...row].forEach((ch, x) => { if (ch === "X") g.fillRect(x * S, y * S, S, S); }));
    return (cache[key] = c);
}

// ---------------------------------------------------------------- Stelle di sfondo
const stars = Array.from({ length: 90 }, () => ({
    x: Math.random() * W, y: Math.random() * H, s: Math.random() * 1.6 + 0.4, v: Math.random() * 12 + 4,
}));

// ---------------------------------------------------------------- Audio (WebAudio sintetizzato)
let actx = null, muted = false, stepNote = 0;
function audio() {
    if (!actx) actx = new (window.AudioContext || window.webkitAudioContext)();
    if (actx.state === "suspended") actx.resume();
    return actx;
}
function tone(freq, dur, type = "square", vol = 0.08, slideTo = null) {
    if (muted || !actx) return;
    const t = actx.currentTime, o = actx.createOscillator(), g = actx.createGain();
    o.type = type; o.frequency.setValueAtTime(freq, t);
    if (slideTo) o.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
    g.gain.setValueAtTime(vol, t); g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    o.connect(g).connect(actx.destination); o.start(t); o.stop(t + dur);
}
function noise(dur, vol = 0.15) {
    if (muted || !actx) return;
    const len = actx.sampleRate * dur, buf = actx.createBuffer(1, len, actx.sampleRate), d = buf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
    const src = actx.createBufferSource(), g = actx.createGain();
    src.buffer = buf; g.gain.value = vol; src.connect(g).connect(actx.destination); src.start();
}
const STEP_NOTES = [98, 87, 78, 73];
function playSound(id) {
    switch (id) {
        case 1: tone(880, 0.12, "square", 0.05, 220); break;            // sparo
        case 2: noise(0.15, 0.12); tone(300, 0.15, "sawtooth", 0.04, 60); break; // alieno colpito
        case 3: noise(0.8, 0.25); tone(200, 0.8, "sawtooth", 0.06, 30); break;   // giocatore colpito
        case 4: tone(STEP_NOTES[stepNote++ % 4], 0.09, "triangle", 0.12); break; // passo formazione
        case 5: tone(600 + Math.random() * 200, 0.15, "sine", 0.03); break;      // ufo
        case 6: [523, 659, 784, 1046].forEach((f, i) => setTimeout(() => tone(f, 0.1, "square", 0.05), i * 70)); break;
        case 7: [440, 660, 880].forEach((f, i) => setTimeout(() => tone(f, 0.15, "triangle", 0.08), i * 120)); break;
        case 8: [392, 523, 659, 784].forEach((f, i) => setTimeout(() => tone(f, 0.18, "square", 0.05), i * 110)); break;
    }
}

// ---------------------------------------------------------------- Input
let keys = 0;
const KEYMAP = { ArrowLeft: 1, KeyA: 1, ArrowRight: 2, KeyD: 2, Space: 4, ArrowUp: 4, KeyW: 4 };
const touchBits = { left: 1, right: 2, fire: 4 };

function onKeyDown(e) {
    const b = KEYMAP[e.code];
    if (b) { keys |= b; e.preventDefault(); audio(); }
    if (e.code === "Enter" && dotnet) { audio(); dotnet.invokeMethodAsync("OnEnter"); }
    if (e.code === "KeyM") muted = !muted;
}
function onKeyUp(e) { const b = KEYMAP[e.code]; if (b) { keys &= ~b; e.preventDefault(); } }

function bindTouch(root) {
    root.querySelectorAll("[data-touch]").forEach(btn => {
        const bit = touchBits[btn.dataset.touch];
        const down = e => { e.preventDefault(); keys |= bit; audio(); btn.classList.add("pressed"); };
        const up = e => { e.preventDefault(); keys &= ~bit; btn.classList.remove("pressed"); };
        btn.addEventListener("pointerdown", down);
        btn.addEventListener("pointerup", up);
        btn.addEventListener("pointercancel", up);
        btn.addEventListener("pointerleave", up);
    });
}

// ---------------------------------------------------------------- Loop
let dotnet = null, ctx = null, last = 0, running = false;

export function start(canvas, dotnetRef, touchRoot) {
    dotnet = dotnetRef;
    ctx = canvas.getContext("2d");
    canvas.width = W; canvas.height = H;
    ctx.imageSmoothingEnabled = false;
    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("keyup", onKeyUp);
    window.addEventListener("blur", () => (keys = 0));
    if (touchRoot) bindTouch(touchRoot);
    running = true;
    last = performance.now();
    requestAnimationFrame(frame);
}

export function stop() {
    running = false;
    window.removeEventListener("keydown", onKeyDown);
    window.removeEventListener("keyup", onKeyUp);
}

export function unlockAudio() { audio(); }
export function toggleMute() { muted = !muted; return muted; }

export function loadHiScore() { try { return parseInt(localStorage.getItem("starsiege.hi") || "0", 10) || 0; } catch { return 0; } }
export function saveHiScore(v) { try { localStorage.setItem("starsiege.hi", String(v)); } catch { } }

function frame(now) {
    if (!running) return;
    const dt = Math.min(now - last, 50);
    last = now;
    const data = dotnet.invokeMethod("Tick", dt, keys); // chiamata sincrona a .NET WASM
    draw(data, dt / 1000);
    requestAnimationFrame(frame);
}

// ---------------------------------------------------------------- Disegno
function draw(d, dt) {
    ctx.fillStyle = "#05030f";
    ctx.fillRect(0, 0, W, H);

    for (const s of stars) {
        s.y += s.v * dt; if (s.y > H) { s.y = 0; s.x = Math.random() * W; }
        ctx.fillStyle = `rgba(200,210,255,${0.3 + s.s / 3})`;
        ctx.fillRect(s.x, s.y, s.s, s.s);
    }

    const [score, hi, lives, level, state] = d;

    // linea del suolo
    ctx.fillStyle = COLORS.shield;
    ctx.fillRect(0, H - 50, W, 2);

    for (let i = 5; i < d.length; i += 5) {
        const k = d[i], x = d[i + 1], y = d[i + 2], a = d[i + 3], b = d[i + 4];
        switch (k) {
            case 1: ctx.drawImage(sprite("player", COLORS.player), x, y); break;
            case 2: {
                const t = ["a", "b", "c"][a];
                ctx.drawImage(sprite(t + b, COLORS[t]), x, y); break;
            }
            case 3: ctx.drawImage(sprite("ufo", COLORS.ufo), x, y); break;
            case 4: ctx.fillStyle = COLORS.shot; ctx.fillRect(x, y, S, 4 * S); break;
            case 5: ctx.drawImage(sprite("bomb" + (a * 2 + b), COLORS.bomb), x, y); break;
            case 6:
                if (a === 0) ctx.drawImage(sprite("boomAlien", COLORS.boom), x, y);
                else if (a === 1) ctx.drawImage(sprite("boomShot", COLORS.boom), x, y);
                else if (a === 2) ctx.drawImage(sprite("boomPlayer" + (Math.floor(b * 10) & 1), COLORS.player), x, y);
                else if (a === 3) ctx.drawImage(sprite("boomAlien", COLORS.ufo), x + 6, y);
                break;
            case 7: ctx.fillStyle = COLORS.shield; ctx.fillRect(x, y, a, S); break;
            case 8:
                ctx.fillStyle = COLORS.ufo; ctx.font = "bold 18px 'Press Start 2P', monospace";
                ctx.fillText(String(a), x, y + 16); break;
            case 9: playSound(a); break;
        }
    }

    // HUD
    ctx.font = "16px 'Press Start 2P', monospace";
    ctx.fillStyle = "#ffffff";
    ctx.fillText("SCORE", 20, 28);
    ctx.fillText("HI-SCORE", W / 2 - 64, 28);
    ctx.fillText("LEVEL", W - 120, 28);
    ctx.fillStyle = COLORS.player;
    ctx.fillText(String(score).padStart(5, "0"), 20, 52);
    ctx.fillText(String(hi).padStart(5, "0"), W / 2 - 40, 52);
    ctx.fillText(String(level).padStart(2, "0"), W - 120, 52);

    // vite
    ctx.fillStyle = "#ffffff";
    ctx.fillText(String(Math.max(0, lives)), 20, H - 18);
    const ps = sprite("player", COLORS.player);
    for (let i = 0; i < Math.min(lives - 1, 6); i++) ctx.drawImage(ps, 50 + i * 50, H - 40);
    if (muted) { ctx.fillStyle = "#888"; ctx.fillText("MUTE", W - 84, H - 18); }
}

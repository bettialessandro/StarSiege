namespace StarSiege.Game;

public enum GameState { Title, Playing, LevelClear, PlayerDown, GameOver }

/// <summary>
/// Tutta la logica del gioco gira in C# (WebAssembly).
/// Ad ogni frame JS chiama Tick() e riceve un buffer di comandi di disegno.
/// </summary>
public sealed class GameEngine
{
    // Dimensioni logiche del canvas e scala dei "pixel" degli sprite
    public const int W = 672, H = 768, S = 3;

    // Bit dell'input inviati da JS
    public const int KeyLeft = 1, KeyRight = 2, KeyFire = 4;

    // Tipi di record nel buffer (5 float ciascuno: kind, x, y, a, b)
    const int RPlayer = 1, RAlien = 2, RUfo = 3, RShot = 4, RBomb = 5, RBoom = 6, RShield = 7, RText = 8, RSound = 9;

    // Suoni
    public const int SndShoot = 1, SndAlienDie = 2, SndPlayerDie = 3, SndStep = 4, SndUfo = 5, SndUfoHit = 6, SndExtraLife = 7, SndLevel = 8;

    const int Cols = 11, Rows = 5;
    const int AlienW = 12 * S, AlienH = 8 * S, ColGap = 16 * S, RowGap = 15 * S;
    const int PlayerW = 13 * S, PlayerH = 8 * S, PlayerY = H - 90;
    const int ShieldCols = 22, ShieldRows = 16, ShieldY = H - 200;
    const int UfoW = 16 * S, UfoH = 7 * S, UfoY = 70;

    readonly Random _rng = new();
    readonly List<float> _buf = new(4096);

    public GameState State { get; private set; } = GameState.Title;
    public int Score { get; private set; }
    public int HiScore { get; set; }
    public int Lives { get; private set; }
    public int Level { get; private set; }
    public event Action? StateChanged;

    // Giocatore
    float _px;
    bool _shotActive; float _sx, _sy;
    bool _fireHeld;
    float _stateTimer;
    bool _extraLifeGiven;

    // Formazione
    readonly bool[,] _alive = new bool[Rows, Cols];
    int _aliveCount;
    float _fx, _fy; int _dir = 1;
    float _stepTimer; int _frame; int _stepSound;

    // Bombe aliene
    readonly List<Bomb> _bombs = new();
    float _bombTimer;

    // UFO
    bool _ufoActive; float _ufoX; int _ufoDir; float _ufoTimer; float _ufoSoundTimer;

    // Scudi
    readonly bool[][,] _shields = new bool[4][,];
    readonly float[] _shieldX = new float[4];

    readonly List<Boom> _booms = new();
    readonly List<FloatText> _texts = new();
    readonly List<int> _sounds = new();

    record struct Bomb(float X, float Y, float Speed, int Type);
    record struct Boom(float X, float Y, int Kind, float Ttl);
    record struct FloatText(float X, float Y, int Value, float Ttl);

    // ---------------------------------------------------------------- API

    public void NewGame()
    {
        Score = 0; Lives = 3; Level = 1; _extraLifeGiven = false;
        BuildShields();
        StartLevel();
        SetState(GameState.Playing);
    }

    public float[] Tick(float dtMs, int keys)
    {
        float dt = Math.Clamp(dtMs / 1000f, 0f, 0.05f);
        switch (State)
        {
            case GameState.Playing: UpdatePlaying(dt, keys); break;
            case GameState.LevelClear:
                UpdateEffects(dt);
                if ((_stateTimer -= dt) <= 0) { Level++; StartLevel(); BuildShields(); SetState(GameState.Playing); }
                break;
            case GameState.PlayerDown:
                UpdateEffects(dt);
                if ((_stateTimer -= dt) <= 0)
                {
                    if (Lives <= 0) GameOver();
                    else { _px = W / 2f - PlayerW / 2f; _bombs.Clear(); SetState(GameState.Playing); }
                }
                break;
            case GameState.Title:
            case GameState.GameOver:
                UpdateEffects(dt);
                AnimateAttract(dt);
                break;
        }
        return BuildFrame();
    }

    // ---------------------------------------------------------------- Setup

    void StartLevel()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++) _alive[r, c] = true;
        _aliveCount = Rows * Cols;
        _fx = (W - (Cols - 1) * ColGap - AlienW) / 2f;
        _fy = 130 + Math.Min(Level - 1, 6) * 18;   // ogni livello parte più in basso
        _dir = 1; _stepTimer = 0; _frame = 0;
        _px = W / 2f - PlayerW / 2f;
        _shotActive = false;
        _bombs.Clear(); _booms.Clear(); _texts.Clear();
        _ufoActive = false; _ufoTimer = 12 + (float)_rng.NextDouble() * 10;
        _bombTimer = 1.5f;
    }

    void BuildShields()
    {
        // Forma di bunker originale: arco con apertura in basso
        float spacing = W / 4f;
        for (int i = 0; i < 4; i++)
        {
            var g = new bool[ShieldRows, ShieldCols];
            for (int y = 0; y < ShieldRows; y++)
                for (int x = 0; x < ShieldCols; x++)
                {
                    bool on = true;
                    // smussa gli angoli superiori
                    if (y < 4 && (x < 4 - y || x > ShieldCols - 1 - (4 - y))) on = false;
                    // apertura ad arco in basso
                    float cx = (ShieldCols - 1) / 2f, dx = Math.Abs(x - cx);
                    if (y >= 10 && dx < 5 - (15 - y) * 0.6f + 1.5f) on = false;
                    g[y, x] = on;
                }
            _shields[i] = g;
            _shieldX[i] = spacing * i + (spacing - ShieldCols * S) / 2f;
        }
    }

    void SetState(GameState s) { State = s; StateChanged?.Invoke(); }

    void GameOver()
    {
        if (Score > HiScore) HiScore = Score;
        SetState(GameState.GameOver);
    }

    // ---------------------------------------------------------------- Update

    void UpdatePlaying(float dt, int keys)
    {
        // --- giocatore
        float speed = 260;
        if ((keys & KeyLeft) != 0) _px -= speed * dt;
        if ((keys & KeyRight) != 0) _px += speed * dt;
        _px = Math.Clamp(_px, 16, W - 16 - PlayerW);

        bool fire = (keys & KeyFire) != 0;
        if (fire && !_fireHeld && !_shotActive)
        {
            _shotActive = true; _sx = _px + PlayerW / 2f - S / 2f; _sy = PlayerY - 4 * S;
            _sounds.Add(SndShoot);
        }
        _fireHeld = fire;

        if (_shotActive)
        {
            _sy -= 720 * dt;
            if (_sy < 50) { _shotActive = false; AddBoom(_sx - 9, 50, 1); }
            else CheckPlayerShot();
        }

        UpdateFormation(dt);
        UpdateBombs(dt);
        UpdateUfo(dt);
        UpdateEffects(dt);

        if (Score >= 1500 && !_extraLifeGiven) { _extraLifeGiven = true; Lives++; _sounds.Add(SndExtraLife); }

        if (_aliveCount == 0 && State == GameState.Playing)
        {
            _stateTimer = 2.2f; _shotActive = false; _bombs.Clear(); _ufoActive = false;
            _sounds.Add(SndLevel);
            SetState(GameState.LevelClear);
        }
    }

    void UpdateFormation(float dt)
    {
        // Più alieni muoiono, più la formazione accelera
        float interval = 0.02f + 0.75f * (_aliveCount / (float)(Rows * Cols));
        interval *= MathF.Max(0.55f, 1f - (Level - 1) * 0.07f);
        _stepTimer += dt;
        if (_stepTimer < interval) return;
        _stepTimer = 0;
        _frame ^= 1;
        _sounds.Add(SndStep); _stepSound = (_stepSound + 1) % 4;

        GetFormationBounds(out float minX, out float maxX, out _, out float maxY);
        float step = 3 * S;
        if ((_dir > 0 && maxX + step > W - 12) || (_dir < 0 && minX - step < 12))
        {
            _fy += 8 * S; _dir = -_dir;
        }
        else _fx += step * _dir;

        GetFormationBounds(out _, out _, out _, out maxY);
        // Gli alieni cancellano gli scudi che attraversano
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_alive[r, c]) EraseShields(AlienX(c), AlienY(r), AlienW, AlienH);

        if (maxY >= PlayerY)
        {
            Lives = 0; AddBoom(_px, PlayerY, 2); _sounds.Add(SndPlayerDie);
            _stateTimer = 1.5f; SetState(GameState.PlayerDown);
        }
    }

    void GetFormationBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = float.MaxValue; maxX = float.MinValue; minY = float.MaxValue; maxY = float.MinValue;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_alive[r, c])
                {
                    minX = Math.Min(minX, AlienX(c)); maxX = Math.Max(maxX, AlienX(c) + AlienW);
                    minY = Math.Min(minY, AlienY(r)); maxY = Math.Max(maxY, AlienY(r) + AlienH);
                }
    }

    float AlienX(int c) => _fx + c * ColGap;
    float AlienY(int r) => _fy + r * RowGap;
    static int AlienType(int r) => r == 0 ? 0 : r < 3 ? 1 : 2;
    static int AlienPoints(int r) => r == 0 ? 30 : r < 3 ? 20 : 10;

    void CheckPlayerShot()
    {
        const float sw = S, sh = 4 * S;
        // alieni
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                if (!_alive[r, c]) continue;
                float ax = AlienX(c), ay = AlienY(r);
                if (Overlap(_sx, _sy, sw, sh, ax, ay, AlienW, AlienH))
                {
                    _alive[r, c] = false; _aliveCount--;
                    _shotActive = false;
                    int pts = AlienPoints(r);
                    Score += pts;
                    AddBoom(ax, ay, 0);
                    _sounds.Add(SndAlienDie);
                    return;
                }
            }
        // ufo
        if (_ufoActive && Overlap(_sx, _sy, sw, sh, _ufoX, UfoY, UfoW, UfoH))
        {
            _ufoActive = false; _shotActive = false;
            int[] vals = { 50, 100, 150, 300 };
            int v = vals[_rng.Next(vals.Length)];
            Score += v;
            _texts.Add(new FloatText(_ufoX + 4, UfoY + 4, v, 1.5f));
            AddBoom(_ufoX, UfoY, 3);
            _sounds.Add(SndUfoHit);
            _ufoTimer = 18 + (float)_rng.NextDouble() * 12;
            return;
        }
        // bombe (un colpo può distruggere una bomba)
        for (int i = 0; i < _bombs.Count; i++)
        {
            var b = _bombs[i];
            if (Overlap(_sx - 3, _sy, sw + 6, sh, b.X, b.Y, S * 3, S * 7))
            {
                _bombs.RemoveAt(i); _shotActive = false;
                AddBoom(_sx - 9, _sy, 1);
                return;
            }
        }
        // scudi
        if (HitShield(_sx, _sy, sw, sh)) { _shotActive = false; }
    }

    void UpdateBombs(float dt)
    {
        _bombTimer -= dt;
        int maxBombs = Math.Min(2 + Level, 7);
        if (_bombTimer <= 0 && _bombs.Count < maxBombs && _aliveCount > 0)
        {
            _bombTimer = MathF.Max(0.25f, 1.1f - Level * 0.08f) * (0.5f + (float)_rng.NextDouble());
            // sceglie una colonna: spesso quella sopra il giocatore
            int col;
            if (_rng.NextDouble() < 0.4)
                col = (int)Math.Round((_px + PlayerW / 2f - _fx - AlienW / 2f) / ColGap);
            else col = _rng.Next(Cols);
            col = Math.Clamp(col, 0, Cols - 1);
            for (int tries = 0; tries < Cols; tries++)
            {
                int c = (col + tries) % Cols;
                for (int r = Rows - 1; r >= 0; r--)
                    if (_alive[r, c])
                    {
                        float spd = 200 + Level * 15 + _rng.Next(80);
                        _bombs.Add(new Bomb(AlienX(c) + AlienW / 2f - S, AlienY(r) + AlienH, spd, _rng.Next(2)));
                        tries = Cols; break;
                    }
            }
        }

        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            b = b with { Y = b.Y + b.Speed * dt };
            _bombs[i] = b;
            float bw = 3 * S, bh = 7 * S;
            if (b.Y > H - 50) { _bombs.RemoveAt(i); AddBoom(b.X - 6, H - 60, 1); continue; }
            if (HitShield(b.X, b.Y, bw, bh)) { _bombs.RemoveAt(i); continue; }
            if (Overlap(b.X, b.Y, bw, bh, _px + S, PlayerY + 2 * S, PlayerW - 2 * S, PlayerH - 2 * S))
            {
                _bombs.RemoveAt(i);
                Lives--;
                AddBoom(_px, PlayerY, 2);
                _sounds.Add(SndPlayerDie);
                _shotActive = false;
                _stateTimer = 1.6f;
                SetState(GameState.PlayerDown);
                return;
            }
        }
    }

    void UpdateUfo(float dt)
    {
        if (!_ufoActive)
        {
            _ufoTimer -= dt;
            if (_ufoTimer <= 0 && _aliveCount > 6)
            {
                _ufoActive = true;
                _ufoDir = _rng.Next(2) == 0 ? 1 : -1;
                _ufoX = _ufoDir > 0 ? -UfoW : W;
            }
            return;
        }
        _ufoX += _ufoDir * 140 * dt;
        if ((_ufoSoundTimer -= dt) <= 0) { _ufoSoundTimer = 0.18f; _sounds.Add(SndUfo); }
        if (_ufoX < -UfoW - 10 || _ufoX > W + 10)
        {
            _ufoActive = false; _ufoTimer = 18 + (float)_rng.NextDouble() * 12;
        }
    }

    void UpdateEffects(float dt)
    {
        for (int i = _booms.Count - 1; i >= 0; i--)
        {
            var b = _booms[i] with { Ttl = _booms[i].Ttl - dt };
            if (b.Ttl <= 0) _booms.RemoveAt(i); else _booms[i] = b;
        }
        for (int i = _texts.Count - 1; i >= 0; i--)
        {
            var t = _texts[i] with { Ttl = _texts[i].Ttl - dt, Y = _texts[i].Y - 15 * dt };
            if (t.Ttl <= 0) _texts.RemoveAt(i); else _texts[i] = t;
        }
    }

    float _attractTimer;
    void AnimateAttract(float dt)
    {
        _attractTimer += dt;
        if (_attractTimer > 0.5f) { _attractTimer = 0; _frame ^= 1; }
    }

    void AddBoom(float x, float y, int kind) =>
        _booms.Add(new Boom(x, y, kind, kind == 2 ? 1.2f : 0.35f));

    static bool Overlap(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh) =>
        ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

    // ---------------------------------------------------------------- Scudi

    bool HitShield(float x, float y, float w, float h)
    {
        if (y + h < ShieldY || y > ShieldY + ShieldRows * S) return false;
        for (int i = 0; i < 4; i++)
        {
            float sx = _shieldX[i];
            if (!Overlap(x, y, w, h, sx, ShieldY, ShieldCols * S, ShieldRows * S)) continue;
            var g = _shields[i];
            int c0 = Math.Max(0, (int)((x - sx) / S)), c1 = Math.Min(ShieldCols - 1, (int)((x + w - sx) / S));
            int r0 = Math.Max(0, (int)((y - ShieldY) / S)), r1 = Math.Min(ShieldRows - 1, (int)((y + h - ShieldY) / S));
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    if (g[r, c])
                    {
                        Crater(g, r, c);
                        return true;
                    }
        }
        return false;
    }

    void Crater(bool[,] g, int r, int c)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int rr = r + dy, cc = c + dx;
                if (rr < 0 || cc < 0 || rr >= ShieldRows || cc >= ShieldCols) continue;
                int d = Math.Abs(dx) + Math.Abs(dy);
                if (d <= 1 || (d <= 3 && _rng.NextDouble() < 0.5)) g[rr, cc] = false;
            }
    }

    void EraseShields(float x, float y, float w, float h)
    {
        if (y + h < ShieldY || y > ShieldY + ShieldRows * S) return;
        for (int i = 0; i < 4; i++)
        {
            float sx = _shieldX[i];
            if (!Overlap(x, y, w, h, sx, ShieldY, ShieldCols * S, ShieldRows * S)) continue;
            var g = _shields[i];
            for (int r = 0; r < ShieldRows; r++)
                for (int c = 0; c < ShieldCols; c++)
                    if (Overlap(x, y, w, h, sx + c * S, ShieldY + r * S, S, S)) g[r, c] = false;
        }
    }

    // ---------------------------------------------------------------- Render buffer

    void Rec(int kind, float x, float y, float a = 0, float b = 0)
    {
        _buf.Add(kind); _buf.Add(x); _buf.Add(y); _buf.Add(a); _buf.Add(b);
    }

    float[] BuildFrame()
    {
        _buf.Clear();
        // header: score, hi, lives, level, state
        _buf.Add(Score); _buf.Add(Math.Max(HiScore, Score)); _buf.Add(Lives); _buf.Add(Level); _buf.Add((int)State);

        bool inGame = State is GameState.Playing or GameState.LevelClear or GameState.PlayerDown;

        if (inGame)
        {
            // scudi come "run" orizzontali di celle accese
            for (int i = 0; i < 4; i++)
            {
                var g = _shields[i]; if (g == null) continue;
                for (int r = 0; r < ShieldRows; r++)
                {
                    int c = 0;
                    while (c < ShieldCols)
                    {
                        if (!g[r, c]) { c++; continue; }
                        int start = c;
                        while (c < ShieldCols && g[r, c]) c++;
                        Rec(RShield, _shieldX[i] + start * S, ShieldY + r * S, (c - start) * S);
                    }
                }
            }

            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (_alive[r, c]) Rec(RAlien, AlienX(c), AlienY(r), AlienType(r), _frame);

            if (_ufoActive) Rec(RUfo, _ufoX, UfoY);
            if (State != GameState.PlayerDown) Rec(RPlayer, _px, PlayerY);
            if (_shotActive) Rec(RShot, _sx, _sy);
            foreach (var b in _bombs) Rec(RBomb, b.X, b.Y, b.Type, ((int)(b.Y / 18)) & 1);
        }

        foreach (var b in _booms) Rec(RBoom, b.X, b.Y, b.Kind, b.Ttl);
        foreach (var t in _texts) Rec(RText, t.X, t.Y, t.Value);

        foreach (var s in _sounds) Rec(RSound, 0, 0, s);
        _sounds.Clear();

        return _buf.ToArray();
    }
}

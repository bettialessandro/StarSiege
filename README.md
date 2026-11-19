# Star Siege

Sparatutto arcade a schermata fissa scritto in **Blazor WebAssembly (.NET 10)**.

- Logica di gioco in C# (`src/StarSiege/Game/GameEngine.cs`), eseguita nel browser via WebAssembly
- Rendering canvas, input e audio sintetizzato in `wwwroot/js/game.js`
- Controlli: ← → / A D per muoversi, SPAZIO per sparare, M per l'audio, INVIO per iniziare. Su mobile ci sono i pulsanti touch.
- `/?autostart` fa partire subito la partita

## Avvio in locale

```bash
dotnet run --project src/StarSiege
```

## Pubblicazione su GitHub Pages

1. Fai il push del repository su GitHub (branch `main`)
2. In **Settings → Pages → Source** seleziona **GitHub Actions**
3. Il workflow `.github/workflows/deploy-pages.yml` compila, imposta il `<base href>` con il nome del repository e pubblica su `https://<utente>.github.io/<repo>/`

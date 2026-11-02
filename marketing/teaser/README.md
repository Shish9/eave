# Eave teaser — "Everything, at a glance"

A 25-second motion teaser. Everything is code, so it is easy to tweak and re-render.

| File | What it is |
|---|---|
| `teaser.html` | The animation. Every frame is `render(t)`. Open it in a browser and click to play it with sound. |
| `render.mjs` | Exports `eave-teaser.mp4` (1920×1080, 30 fps) with Playwright + ffmpeg. |
| `audio.py` | Builds `assets/teaser-audio.wav`: ambience, UI sounds, a soft music bed and the voice-over, ducked under the voice. |
| `assets/vo/l1–l5.wav` | Voice-over lines (synthetic placeholder voice). Replace them with a real recording and re-run `audio.py`. |
| `assets/wordmark.png` | The wordmark cut from the logo, used on the end card. |

## Use your own Windows desktop

1. On your PC, close anything private, then press **Win + PrtScn**. The screenshot is saved to
   `Pictures\Screenshots`. A 4K screen works best because the camera zooms in about 2×.
2. Save it here as `assets/desktop.png`, or pass it on the command line:

```bash
node render.mjs --desktop path/to/screenshot.png
```

The drawn desktop (wallpaper, windows, taskbar) is then replaced by your screenshot. The toasts, the Eave
island, the zoom and the end card stay the same. Leave the top-center of the screen clear; that is where the
island sits.

## Render

```bash
python3 audio.py
```

```bash
node render.mjs --fps 30
```

Options: `--fps 60`, `--scale 2` (renders at 3840×2160), `--out file.mp4`. To check one moment:
open `teaser.html?t=14.9`.

## Timeline

| Time | Beat | Voice-over |
|---|---|---|
| 0–4 s | Desktop wakes, three toasts pop in and pause mid-motion, the camera glides to the notch | — |
| 4–7 s | Eave opens ("Good morning"), the desktop dims and softens | — |
| 7–12 s | Music: art, track, moving progress, controls, waveform on the beat (96 BPM) | "Your music." |
| 12–16 s | Claude: "Your summary is ready", typed preview with glow, tap to open, folds back | "A quick update from Claude." |
| 16–20 s | Weather and forecast, then a calendar reminder and a finished download stack in | "The weather before you head out." / "Eave brings the little things…" |
| 20–25 s | Cards tuck away, the notch closes, the desktop returns, end card | "One glance, then back to what you were doing." |

# Eave teaser — "Everything, at a glance"

A 52-second motion teaser, timed word by word to the voice-over, in 16:9 (1920×1080) and 9:16 (1080×1920,
for Reels / TikTok / Shorts). Everything is code, so it is easy to tweak and re-render. Made by CodebyAmed.

| File | What it is |
|---|---|
| `teaser.html` | The animation. Every frame is `render(t)`. Open it in a browser and click to play it with sound. |
| `render.mjs` | Exports `eave-teaser.mp4` (1920×1080) or, with `--format vertical`, `eave-teaser-vertical.mp4` (1080×1920), 30 fps, with Playwright + ffmpeg. |
| `audio.py` | Builds `assets/teaser-audio.wav`: ambience, UI sounds, a soft music bed and the voice-over, ducked under the voice. |
| `assets/vo/voiceover.wav` | The ElevenLabs voice-over, one take. The picture is cued to its words; a new take needs the same pacing or new cue times. |
| `assets/mark.png`, `assets/wordmark.png` | The logo mark and wordmark cut from the original logo, used on the end card. |

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
node render.mjs
```

```bash
node render.mjs --format vertical
```

Options: `--fps 60`, `--scale 2` (renders at 3840×2160), `--out file.mp4`. To check one moment:
open `teaser.html?t=14.9` (add `&format=vertical` for the 9:16 cut).

## Timeline

| Time | Picture | Voice-over |
|---|---|---|
| 0–7 s | Desktop wakes; toasts pop on each word, then everything pauses mid-motion | "Your desktop is busy. Messages, downloads, reminders, all popping up wherever they like." |
| 7–12 s | Camera glides to the notch, the dot pulses, Eave opens and the desktop softens | "Meet Eave, a calm little notch right at the top of Windows." |
| 12–18 s | Music card; album art and controls light up as they're named; waveform on the beat | "Your music is right there. The song, the album art, and controls when you need them." |
| 18–26 s | Claude: "Your summary is ready" types in, the cursor taps on "open it", folds back on "get back to it" | "A quick update from Claude. Your summary is ready. Peek at it, open it if you want, and get back to it." |
| 26–30 s | Weather, then the hourly forecast on "the hours ahead" | "The weather before you head out, with the hours ahead at a glance." |
| 30–38 s | Calendar reminder, then the finished download stack in | "Your next meeting, and the download that just finished. Stacked neatly and out of the way." |
| 38–43 s | Cards tuck into "All at a glance"; music, Claude and weather icons light up in turn | "Eave brings the little things worth seeing together, right at the top of Windows." |
| 43–46 s | The notch closes, the camera pulls back, the desktop returns | "One glance, then back to what you were doing." |
| 46–52 s | End card: logo, "Everything you need, right up top.", "Made by CodebyAmed" | "Eave. Everything you need, right up top." |

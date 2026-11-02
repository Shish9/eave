// Renders teaser.html to MP4, frame by frame, then muxes the audio track if it exists.
//   node render.mjs [--format vertical] [--fps 30] [--out eave-teaser.mp4] [--desktop path/to/screenshot.png] [--scale 1|2]
import { createRequire } from 'node:module';
const { chromium } = createRequire(import.meta.url)('playwright'); // npm i playwright, or a global install
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (k, d) => { const i = process.argv.indexOf('--' + k); return i > 0 ? process.argv[i + 1] : d; };
const fps = +arg('fps', 30), scale = +arg('scale', 1);
const vertical = arg('format', 'landscape') === 'vertical';   // --format vertical → 1080x1920 for Reels/TikTok/Shorts
const [W, H] = vertical ? [1080, 1920] : [1920, 1080];
const out = path.resolve(arg('out', path.join(here, vertical ? 'eave-teaser-vertical.mp4' : 'eave-teaser.mp4')));
const desktop = arg('desktop');
const audio = path.join(here, 'assets', 'teaser-audio.wav');

const url = new URL(pathToFileURL(path.join(here, 'teaser.html')));
url.searchParams.set('render', '1');
if (vertical) url.searchParams.set('format', 'vertical');
if (desktop) url.searchParams.set('desktop', pathToFileURL(path.resolve(desktop)).href);

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: scale });
await page.goto(url.href);
await page.evaluate(() => window.teaserReady);
const duration = await page.evaluate(() => window.DURATION);
const frames = Math.round(duration * fps);

const ffArgs = ['-y', '-f', 'image2pipe', '-framerate', String(fps), '-c:v', 'mjpeg', '-i', '-'];
if (existsSync(audio)) ffArgs.push('-i', audio, '-c:a', 'aac', '-b:a', '192k', '-shortest');
ffArgs.push('-c:v', 'libx264', '-preset', 'slow', '-crf', '16', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', out);
const ff = spawn('ffmpeg', ffArgs, { stdio: ['pipe', 'ignore', 'inherit'] });

for (let i = 0; i < frames; i++) {
  await page.evaluate(t => window.seek(t), i / fps);
  const buf = await page.screenshot({ type: 'jpeg', quality: 95 });
  if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once('drain', r));
  if (i % fps === 0) process.stdout.write(`\r${(i / fps).toFixed(0)}s / ${duration}s`);
}
ff.stdin.end();
await new Promise(r => ff.on('close', r));
await browser.close();
console.log(`\nwrote ${out}`);

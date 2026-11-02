import { createRequire } from 'node:module';
const { chromium } = createRequire(import.meta.url)('playwright'); // npm i playwright, or a global install
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const [outDir, ...times] = process.argv.slice(2);
const V = process.env.FORMAT === 'vertical';
const b = await chromium.launch(); const p = await b.newPage({ viewport: V ? { width: 1080, height: 1920 } : { width: 1920, height: 1080 } });
const u = new URL(pathToFileURL(path.join(here, 'teaser.html'))); u.searchParams.set('render', '1'); if (V) u.searchParams.set('format', 'vertical');
await p.goto(u.href); await p.evaluate(() => window.teaserReady);
for (const t of times) { await p.evaluate(t => window.seek(t), +t); await p.screenshot({ path: path.join(outDir, `t${t}.jpg`), type: 'jpeg', quality: 80 }); }
await b.close();

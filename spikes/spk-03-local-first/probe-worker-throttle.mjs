// Sonda: o throttling de CPU do CDP pega nos workers? Tenta aplicar também na sessão de cada worker.
import fs from 'node:fs';
import { startServer, tempProfile, launch, BASE } from './lib/harness.mjs';
import { throttle } from './lib/harness.mjs';

const server = await startServer();
const dir = tempProfile('wthr');
const ctx = await launch(dir);
const page = await ctx.newPage();
await page.goto(`${BASE}/bench.html`);
await page.waitForSelector('body[data-ready="1"]');
console.log('1x', JSON.stringify(await page.evaluate(() => window.calib())));
await throttle(ctx, page, 4, { workers: false });
console.log('4x só página', JSON.stringify(await page.evaluate(() => window.calib())));
await throttle(ctx, page, 4, { workers: true });
console.log('4x página + workers', JSON.stringify(await page.evaluate(() => window.calib())));
await ctx.close();
fs.rmSync(dir, { recursive: true, force: true });
server.kill();

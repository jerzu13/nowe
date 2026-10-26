// Test only: opens a generated track map (mapa_*.html) in headless Chromium and fails on any script error.
// Usage: node check_map.js <mapa.html> [--app]   (--app: the in-game app panel must be there)   (needs Playwright; in cloud sessions it is preinstalled globally)
const path = require('path');
let pw;
try { pw = require('playwright'); } catch (e) { pw = require(path.join(require('child_process').execSync('npm root -g').toString().trim(), 'playwright')); }
(async () => {
  const b = await pw.chromium.launch();
  const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
  const errs = [];
  p.on('pageerror', e => errs.push(e.message));
  p.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
  await p.goto('file://' + path.resolve(process.argv[2]));
  await p.waitForTimeout(800);
  const laps = await p.$eval('#lap', s => s.options.length);
  const charts = await p.$$eval('#charts .chart', c => c.length);
  // the in-game app replayed on the lap: counter values, verdicts after the brake presses, the play button
  const app = await p.evaluate(() => {
    if (!D.app) return null;
    const s = curApp(), i = s.views.findIndex(o => o.dist != null && o.dist > 20);
    showHover(i);
    return { shown: !$('appCard').hidden, counts: s.views.filter(o => o.dist != null).length, presses: s.ev.filter(e => e.kind === 'press').length, box: $('apN').textContent };
  });
  if (app) { await p.click('#apPlay'); await p.waitForTimeout(600); app.playing = await p.evaluate(() => pinned > 0); }
  await b.close();
  console.log('map: ' + laps + ' laps, ' + charts + ' charts, in-game app: ' + JSON.stringify(app) + ', errors: ' + JSON.stringify(errs));
  const appBad = process.argv[3] === '--app' && !(app && app.shown && app.counts > 20 && app.presses > 0 && /\d+ m/.test(app.box) && app.playing);
  if (appBad) console.log('in-game app panel missing or empty');
  process.exit(errs.length || !laps || appBad ? 1 : 0);
})();

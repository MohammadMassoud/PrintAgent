// One unconfigured console agent, two independent applications, simulated TCP output only.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { spawn, spawnSync } = require('node:child_process');
const { once } = require('node:events');
const { randomUUID } = require('node:crypto');
const root = path.resolve(__dirname, '..');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || path.join(root, 'artifacts/browser-tools/node_modules/playwright'));
const configuration = process.env.PRINTAGENT_TEST_CONFIGURATION || 'Debug';
const work = path.join(root, 'artifacts', 'multi-application-' + randomUUID());
fs.mkdirSync(work, { recursive: true });
const processes = [], logs = [], captures = [];
const hosts = [];
let browser, receiver, agent;
async function port() {
    const s = net.createServer().listen(0, '127.0.0.1'); await once(s, 'listening');
    const p = s.address().port; await new Promise(resolve => s.close(resolve)); return p;
}
async function wait(fn, seconds = 35) {
    const until = Date.now() + seconds * 1000;
    while (Date.now() < until) { try { if (await fn()) return; } catch {} await new Promise(r => setTimeout(r, 100)); }
    throw new Error('Timed out waiting for application or agent');
}
function start(args, label, env = {}) {
    const log = fs.openSync(path.join(work, label + '.log'), 'w'); logs.push(log);
    const p = spawn('dotnet', args, { cwd: root, windowsHide: true, env: { ...process.env, ...env }, stdio: ['ignore', log, log] });
    processes.push(p); return p;
}
function stop(p) {
    if (p?.exitCode === null) spawnSync('taskkill', ['/PID', String(p.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
}
(async () => {
    const bridgePort = await port();
    const bridge = `http://127.0.0.1:${bridgePort}`;
    receiver = net.createServer(socket => {
        const chunks = []; socket.on('data', b => chunks.push(b));
        socket.on('end', () => captures.push(Buffer.concat(chunks).toString('utf8')));
    }).listen(0, '127.0.0.1');
    await once(receiver, 'listening');
    const origins = [`http://127.0.0.1:${await port()}`, `http://127.0.0.1:${await port()}`];
    const startAgent = label => start([path.join(root, `src/SinergiaAI.PrintAgent.Agent/bin/${configuration}/net10.0-windows/win-x64/SinergiaAI.PrintAgent.Agent.dll`)], label, {
        SINERGIAPRINT_Agent__ApiBaseUrl: '', SINERGIAPRINT_Agent__InstallationId: '00000000-0000-0000-0000-000000000000',
        SINERGIAPRINT_Agent__EnableLegacyApi: 'false', SINERGIAPRINT_Agent__OnlyNetworkPrinters: 'true',
        SINERGIAPRINT_Agent__AllowedOrigins__0: origins[0], SINERGIAPRINT_Agent__AllowedOrigins__1: origins[1],
        SINERGIAPRINT_Agent__Token: '', SINERGIAPRINT_Agent__DataPath: path.join(work, 'machine'),
        SINERGIAPRINT_Agent__LocalBridgePort: String(bridgePort),
        SINERGIAPRINT_Agent__NetworkPrinters__0__Name: 'Simulated TCP printer',
        SINERGIAPRINT_Agent__NetworkPrinters__0__Address: `tcp://127.0.0.1:${receiver.address().port}`
    });
    agent = startAgent('agent');
    for (const [i, origin] of origins.entries()) {
        hosts.push(start(['run', '--project', 'samples/PrintAgent.Demo', '-c', configuration, '--no-build', '--no-launch-profile', '--',
            '--urls', origin, '--Demo:DataDirectory', path.join(work, 'app-' + i), '--Demo:BridgePort', String(bridgePort)], 'app-' + i,
            { ASPNETCORE_ENVIRONMENT: 'Development' }));
        await wait(async () => (await fetch(origin + '/health')).ok);
    }
    await wait(async () => (await fetch(bridge + '/info', { headers: { Origin: origins[0] } })).ok);
    assert.equal((await fetch(bridge + '/info')).status, 403);
    assert.equal((await fetch(bridge + '/info', { headers: { Origin: 'http://untrusted.example' } })).status, 403);
    assert.equal((await fetch(bridge + '/connect', { method: 'POST', headers: { Origin: 'https://another.example', 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiBaseUrl: origins[0] + '/printing/', challenge: 'A'.repeat(64) }) })).status, 404);
    const info = await (await fetch(bridge + '/info', { headers: { Origin: 'https://another.example' } })).json();
    assert.equal(info.multiApplication, true);
    browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
    const contexts = [await browser.newContext(), await browser.newContext()];
    const pages = await Promise.all(contexts.map(c => c.newPage()));
    const errors = []; pages.forEach(p => p.on('pageerror', e => errors.push(e.message)));
    for (const [i, page] of pages.entries()) {
        await page.goto(origins[i]);
        await page.getByRole('status').filter({ hasText: 'Agent connected' }).waitFor({ timeout: 35000 });
        await page.getByLabel('Printer', { exact: true }).selectOption({ label: 'Simulated TCP printer' });
        await page.getByLabel('RAW commands').fill(`^XA^FDApplication ${i}^FS^XZ`);
        await page.getByRole('button', { name: 'Queue request', exact: true }).click();
        await wait(() => captures.includes(`^XA^FDApplication ${i}^FS^XZ`));
    }
    assert.equal(fs.existsSync(path.join(work,'machine/applications.json')),false,'No remote application profiles needed');
    assert.equal(fs.existsSync(path.join(work,'machine/local-queue.db')),true,'Queue persists on workstation');
    console.log('PASS one local agent serves two approved websites with exact RAW delivery and no backend enrollment');
    const fresh = await browser.newContext(); const freshPage = await fresh.newPage();
    await freshPage.goto(origins[0] + '/demo/sign-in?user=another-demo-user');
    await freshPage.getByRole('status').filter({ hasText: 'Agent connected' }).waitFor({ timeout: 35000 });
    assert.ok(fs.existsSync(path.join(work,'machine/local-queue.db')));
    console.log('PASS another application user in a fresh browser profile recognizes the existing machine connection with machine-wide approval and no enrollment or installer');
    stop(agent); await once(agent, 'exit').catch(() => {});
    agent = startAgent('agent-restarted');
    await wait(async () => (await fetch(bridge + '/info', { headers: { Origin: origins[0] } })).ok);
    for (const [i, page] of pages.entries()) {
        await page.reload();
        await page.getByRole('status').filter({ hasText: 'Agent connected' }).waitFor({ timeout: 35000 });
        await page.getByRole('button', { name: 'Retry current request', exact: true }).click();
    }
    await new Promise(r => setTimeout(r, 2500));
    assert.equal(captures.length, 2, 'Restart and retry must not print duplicate copies');
    assert.ok(fs.existsSync(path.join(work,'machine/local-queue.db')));
    await pages[0].getByRole('button', { name: 'New request', exact: true }).click();
    await pages[0].getByLabel('RAW commands').fill('after restart');
    await pages[0].getByRole('button', { name: 'Queue request', exact: true }).click();
    await wait(() => captures.includes('after restart'));
    await wait(async () => (await pages[0].locator('tbody .badge').allTextContents()).every(x => x === 'Sent'));
    stop(hosts[1]);
    await pages[0].getByRole('button', { name: 'New request', exact: true }).click();
    await pages[0].getByLabel('RAW commands').fill('application B is offline');
    await pages[0].getByRole('button', { name: 'Queue request', exact: true }).click();
    await wait(() => captures.includes('application B is offline'));
    console.log('PASS an unavailable application does not interrupt printing from the other application');
    assert.deepEqual(errors, []);
    await pages[0].screenshot({ path: path.join(work, 'application-a.png'), fullPage: true });
    console.log('PASS service restart restores the machine queue and original idempotency, and accepts new work');
    console.log('All multi-application checks passed. Artifacts: ' + work);
})().catch(e => { console.error(e); process.exitCode = 1; }).finally(async () => {
    if (browser) await browser.close();
    for (const p of processes.reverse()) stop(p);
    if (receiver) await new Promise(resolve => receiver.close(resolve));
    logs.forEach(fd => fs.closeSync(fd));
});

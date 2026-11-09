// Standalone public sample: temporary console agent + TCP receiver, no installed service or physical output.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { spawn, spawnSync } = require('node:child_process');
const { once } = require('node:events');
const { randomUUID } = require('node:crypto');
const root = path.resolve(__dirname, '..');
const configuration = process.env.PRINTAGENT_TEST_CONFIGURATION || 'Debug';
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || path.join(root, 'artifacts/browser-tools/node_modules/playwright'));
const work = path.join(root, 'artifacts', 'demo-browser-' + randomUUID());
fs.mkdirSync(work, { recursive: true });
let host, browser, page;
const log = fs.openSync(path.join(work, 'host.log'), 'w');
async function port() {
    const server = net.createServer().listen(0, '127.0.0.1'); await once(server, 'listening');
    const result = server.address().port; await new Promise(resolve => server.close(resolve)); return result;
}
async function wait(fn, seconds = 30) {
    const deadline = Date.now() + seconds * 1000;
    while (Date.now() < deadline) {
        try { if (await fn()) return; } catch {}
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error('Timed out');
}
(async () => {
    const base = `http://127.0.0.1:${await port()}/`;
    host = spawn('dotnet', ['run', '--project', 'samples/PrintAgent.Demo', '-c', configuration, '--no-build', '--no-launch-profile', '--', '--simulate',
        '--urls', base, '--Demo:DataDirectory', path.join(work, 'data'), '--Demo:BridgePort', String(await port())],
        { cwd: root, windowsHide: true, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' }, stdio: ['ignore', log, log] });
    await wait(async () => (await fetch(base + 'health')).ok);
    browser = await chromium.launch({ headless: true, ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}) });
    page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(base);
    await page.getByText('Agent connected', { exact: true }).waitFor({ timeout: 60000 });
    await page.getByLabel('Printer', { exact: true }).selectOption({ label: 'Simulated TCP printer' });
    await page.getByLabel('Quantity', { exact: true }).fill('3'); await page.getByLabel('Quantity', { exact: true }).press('Tab');
    await page.getByRole('button', { name: 'Queue request', exact: true }).click();
    await wait(async () => (await page.locator('table tbody tr').filter({ hasText: 'Sent' }).count()) === 3);
    await page.getByText('3 capture(s) · latest 20', { exact: true }).waitFor();
    const ids = await page.locator('table tbody tr code').allTextContents();
    await page.reload(); await page.getByText('Agent connected', { exact: true }).waitFor();
    await page.getByRole('button', { name: 'Retry current request', exact: true }).click();
    await page.waitForTimeout(3500);
    assert.deepEqual(await page.locator('table tbody tr code').allTextContents(), ids);
    assert.equal(await page.getByText('3 capture(s) · latest 20', { exact: true }).count(), 1);
    assert.equal(await page.locator('details pre').count(), 3);
    assert.ok((await page.locator('details pre').first().textContent()).includes('^XA'));
    console.log('PASS source-clone demo authenticates locally, discovers its temporary agent, prints exact ZPL to TCP and preserves request keys across reload/retry');
    await page.screenshot({ path: path.join(work, 'desktop.png'), fullPage: true });
    await page.getByRole('button', { name: 'New request', exact: true }).click();
    await page.getByLabel('Example', { exact: true }).selectOption('ESCPOS');
    await page.getByLabel('Quantity', { exact: true }).fill('1'); await page.getByLabel('Quantity', { exact: true }).press('Tab');
    await page.getByRole('button', { name: 'Queue request', exact: true }).click();
    await page.getByText('4 capture(s) · latest 20', { exact: true }).waitFor();
    assert.ok((await page.locator('details pre').first().textContent()).includes('RAW receipt test'));
    await page.setViewportSize({ width: 390, height: 844 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
    await page.screenshot({ path: path.join(work, 'mobile.png'), fullPage: true });
    assert.deepEqual(errors, []);
    console.log('PASS independent ESC/POS request, responsive layout and no browser exceptions');
    console.log('All demo checks passed. Artifacts: ' + work);
})().catch(async error => {
    if (page) { await page.screenshot({ path: path.join(work, 'failure.png'), fullPage: true }).catch(() => {}); fs.writeFileSync(path.join(work, 'failure.html'), await page.content().catch(() => '')); }
    console.error(error); console.error('Artifacts: ' + work); process.exitCode = 1;
}).finally(async () => {
    if (browser) await browser.close();
    if (host && host.exitCode === null) {
        if (process.platform === 'win32') spawnSync('taskkill', ['/PID', String(host.pid), '/T', '/F'], { windowsHide: true });
        else host.kill();
    }
    fs.closeSync(log);
});

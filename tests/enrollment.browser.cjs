// Windows integration: real Razor page + automatic installer + real agent + simulated TCP printer.
// Never installs a service or executes the elevated generic installer.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const net = require('node:net');
const {spawn, spawnSync} = require('node:child_process');
const {once} = require('node:events');
const root = path.resolve(__dirname, '..');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || path.join(root, 'artifacts/browser-tools/node_modules/playwright'));
const fixture = path.join(root, 'tests/ComercialFixture');
const work = path.join(root, 'artifacts', 'enrollment-' + crypto.randomUUID());
fs.mkdirSync(work, {recursive:true});
const processes = [], logs = [];
let browser, receiver, page;
async function freePort() {const s=net.createServer();s.listen(0,'127.0.0.1');await once(s,'listening');const port=s.address().port;await new Promise(r=>s.close(r));return port;}
async function wait(fn, timeout=30000) {const until=Date.now()+timeout;let last;while(Date.now()<until){try{const v=await fn();if(v)return v;}catch(e){last=e;}await new Promise(r=>setTimeout(r,150));}throw last||new Error('Timed out');}
function start(args, env, name, cwd=fixture) {const fd=fs.openSync(path.join(work,name),'w');logs.push(fd);const child=spawn('dotnet',args,{cwd,env:{...process.env,...env},stdio:['ignore',fd,fd],windowsHide:true});processes.push(child);return child;}
async function stop(child) {if(child.exitCode!==null)return;const end=once(child,'exit');child.kill();await end;}
function readContext(file) {
 const bytes=fs.readFileSync(file),marker=Buffer.from('SINERGIAPRINT-CONTEXT-V1');
 assert.ok(bytes.subarray(-marker.length).equals(marker));
 const length=bytes.readInt32LE(bytes.length-marker.length-4);
 return {bytes,context:JSON.parse(bytes.subarray(bytes.length-marker.length-4-length,bytes.length-marker.length-4).toString('utf8'))};
}
function resolveOnly(file, destination, expected=0) {
 const result=spawnSync(file,['--resolve-only',destination],{windowsHide:true,timeout:30000});
 if(result.status!==expected){const detail=fs.existsSync(destination+'.error')?fs.readFileSync(destination+'.error','utf8'):String(result.error||'');throw new Error('Bootstrap diagnostic failed: '+detail);}
 return expected===0?JSON.parse(fs.readFileSync(destination,'utf8')):null;
}
(async()=>{
 const port=await freePort(),base=`http://127.0.0.1:${port}/`;
 start([path.join(fixture,'bin/Debug/net10.0/PrintFixture.dll'),'--urls',base],{ASPNETCORE_ENVIRONMENT:'Development',PRINT_FIXTURE_DATA:path.join(work,'state')},'host.log');
 await wait(async()=>(await fetch(base+'health')).ok);
 const chrome=process.env.CHROME_PATH || path.join(process.env.LOCALAPPDATA,'ms-playwright/chromium-1208/chrome-win64/chrome.exe');
 browser=await chromium.launch({headless:true,executablePath:chrome});
 page=await browser.newPage({viewport:{width:1440,height:1100}});
 const exceptions=[];page.on('pageerror',e=>exceptions.push(e.message));
 await page.goto(base+'testes/agente-impressao');
 const downloadEvent=page.waitForEvent('download');
 await page.getByRole('button',{name:'Instalar agente automaticamente',exact:true}).click();
 const first=await downloadEvent,file=path.join(work,'automatic.exe');await first.saveAs(file);
 const firstContext=readContext(file);
 assert.equal(firstContext.bytes.subarray(0,2).toString(),'MZ');
 assert.equal(firstContext.context.apiBaseUrl,base+'sinergiaprint/');
 assert.ok(Date.parse(firstContext.context.expiresAt)-Date.now()>29*60*1000);
 assert.equal(await page.getByLabel('Token do agente',{exact:true}).isVisible(),false);
 console.log('PASS One click downloads a contextual EXE; manual fields are hidden');
 await page.getByText('Configuração manual (avançado)',{exact:true}).click();
 const iid=await page.getByLabel('GUID da instalação',{exact:true}).inputValue();
 const token=await page.getByLabel('Token do agente',{exact:true}).inputValue();
 assert.ok(!firstContext.bytes.includes(Buffer.from(token)));
 const config=resolveOnly(file,path.join(work,'resolved.private.json'));
 assert.equal(config.Agent.InstallationId,iid);assert.ok(config.Agent.Token===token);
 assert.equal(config.Agent.ApiBaseUrl,base+'sinergiaprint/');
 console.log('PASS Downloaded Windows EXE automatically resolves URL, GUID and token');
 const genericInstaller=path.join(work,'generic-installer.exe');
 const asset=spawnSync(file,['--download-only',genericInstaller],{windowsHide:true,timeout:180000});
 if(asset.status!==0)throw new Error('Public installer download failed: '+(fs.existsSync(genericInstaller+'.error')?fs.readFileSync(genericInstaller+'.error','utf8'):String(asset.error||'')));
 const expectedHash=fs.readFileSync(path.join(root,'src/SinergiaPrint.Bootstrap/InstallerRelease.cs'),'utf8').match(/Sha256 = "([A-F0-9]{64})"/)[1];
 assert.equal(crypto.createHash('sha256').update(fs.readFileSync(genericInstaller)).digest('hex').toUpperCase(),expectedHash);
 console.log('PASS Windows bootstrap downloads and verifies the exact public installer without installing it');
 const redeem=code=>fetch(base+'sinergiaprint/enrollment/redeem',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({code})});
 assert.equal((await redeem(firstContext.context.code)).status,400);
 assert.equal((await redeem('bad-code')).status,400);
 console.log('PASS Used and invalid enrollment codes are rejected');
 const uri=base+'sinergiaprint/installers/'+iid;
 assert.equal((await page.request.get(uri,{headers:{'X-Fixture-User':'other-user'}})).status(),404);
 assert.equal((await page.request.get(uri,{headers:{'X-Fixture-User':'anonymous'}})).status(),401);
 console.log('PASS Installer download requires login and ownership of the installation');
 async function getInstaller(name) {const response=await page.request.get(uri);assert.equal(response.status(),200);assert.equal(response.headers()['cache-control'],'no-store');const p=path.join(work,name+'.exe');fs.writeFileSync(p,await response.body());return {file:p,...readContext(p)};}
 const concurrent=await getInstaller('concurrent');
 const results=await Promise.all(Array.from({length:8},()=>redeem(concurrent.context.code)));
 assert.equal(results.filter(r=>r.status===200).length,1);assert.equal(results.filter(r=>r.status===400).length,7);
 assert.equal(results.find(r=>r.status===200).headers.get('cache-control'),'no-store');
 console.log('PASS Concurrent redemption succeeds once and configuration is not cacheable');
 const expired=await getInstaller('expired'),hash=crypto.createHash('sha256').update(expired.context.code).digest('hex').toUpperCase();
 const update=spawnSync('python',['-c',"import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute(\"UPDATE enrollments SET expires_at='2000-01-01T00:00:00.0000000+00:00' WHERE code_hash=?\",(sys.argv[2],)); c.commit()",path.join(work,'state/printing.db'),hash],{windowsHide:true});assert.equal(update.status,0);
 assert.equal((await redeem(expired.context.code)).status,400);
 console.log('PASS Expired enrollment codes cannot retrieve credentials');
 const tampered=await getInstaller('tampered'),changed=Buffer.from(tampered.bytes);changed[changed.length-1]^=1;fs.writeFileSync(tampered.file,changed);
 resolveOnly(tampered.file,path.join(work,'invalid.private.json'),1);
 console.log('PASS Downloaded EXE rejects a missing or damaged context footer');
 const linked=fs.readFileSync(path.join(work,'state/links',iid.replaceAll('-','')+'.protected'),'utf8');assert.ok(!linked.includes(token));
 const storage=await page.evaluate(()=>Object.values(localStorage));assert.ok(storage.every(v=>!v.includes(token)));
 console.log('PASS Permanent token is protected on server and absent from the EXE and browser storage');
 const received=[];receiver=net.createServer(socket=>{const chunks=[];socket.on('data',b=>chunks.push(b));socket.on('end',()=>received.push(Buffer.concat(chunks)));});receiver.listen(0,'127.0.0.1');await once(receiver,'listening');
 const agent=start([path.join(root,'src/SinergiaPrint.Agent/bin/Debug/net10.0-windows/win-x64/SinergiaPrint.Agent.dll')],{
  SINERGIAPRINT_Agent__ApiBaseUrl:config.Agent.ApiBaseUrl,SINERGIAPRINT_Agent__InstallationId:iid,SINERGIAPRINT_Agent__Token:config.Agent.Token,
  SINERGIAPRINT_Agent__DataPath:path.join(work,'agent'),SINERGIAPRINT_Agent__PollSeconds:'1',
  SINERGIAPRINT_Agent__NetworkPrinters__0__Name:'Zebra simulada TCP',SINERGIAPRINT_Agent__NetworkPrinters__0__Address:`tcp://127.0.0.1:${receiver.address().port}`},'agent.log');
 await page.getByText('Configuração manual (avançado)',{exact:true}).click();
 await page.locator('.connection-badge').filter({hasText:'Agente conectado'}).waitFor({timeout:30000});
 await page.getByText('Zebra simulada TCP',{exact:true}).click();
 let row=page.getByRole('row').filter({hasText:'1001'});
 await row.getByRole('button',{name:'Imprimir etiqueta',exact:true}).click();
 await row.getByText('Enviado à impressora',{exact:true}).waitFor({timeout:30000});
 await wait(()=>received.length===1);assert.ok(received[0].toString('ascii').includes('PEDIDO 1001'));
 console.log('PASS Automatically resolved configuration connects the real agent and prints ZPL over TCP');
 await page.reload();row=page.getByRole('row').filter({hasText:'1001'});
 await row.getByText('Enviado à impressora',{exact:true}).waitFor({timeout:20000});assert.equal(received.length,1);
 console.log('PASS Reload restores connection and print result without reprinting');
 await page.screenshot({path:path.join(work,'desktop.png'),fullPage:true});
 await new Promise(r=>receiver.close(r));receiver=null;
 await page.getByText('Zebra simulada TCP',{exact:true}).click();
 await row.getByRole('button',{name:'Preparar outra etiqueta',exact:true}).click();
 await row.getByRole('button',{name:'Imprimir etiqueta',exact:true}).click();
 await row.getByText(/^Falha:/).waitFor({timeout:30000});
 console.log('PASS Printer failure appears in the page');
 await page.setViewportSize({width:390,height:844});await page.screenshot({path:path.join(work,'mobile.png'),fullPage:true});
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));assert.deepEqual(exceptions,[]);
 console.log('PASS Responsive automatic-install page without browser exceptions');
 await stop(agent);
 const hostLog=fs.readFileSync(path.join(work,'host.log'),'utf8');assert.ok(!hostLog.includes(firstContext.context.code)&&!hostLog.includes(token));
 console.log('PASS Enrollment code and permanent token are absent from request logs');
 console.log('All automatic enrollment checks passed. Artifacts: '+work);
})().catch(async e=>{if(page){console.error('UI errors:',await page.locator('.notice.error').allTextContents());await page.screenshot({path:path.join(work,'failure.png'),fullPage:true});}console.error(e);process.exitCode=1;}).finally(async()=>{if(browser)await browser.close();if(receiver)receiver.close();for(const p of processes)await stop(p);for(const fd of logs)fs.closeSync(fd);});

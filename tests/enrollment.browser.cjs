// Real Comercial page + shared Common service + public NuGet + temporary agent/TCP printer.
// Does not install a Windows service or send bytes to a physical printer.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const crypto = require('node:crypto');
const {spawn, spawnSync} = require('node:child_process');
const {once} = require('node:events');
const root = path.resolve(__dirname, '..');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || path.join(root,'artifacts/browser-tools/node_modules/playwright'));
const fixture = path.join(root,'tests/ComercialFixture');
const work = path.join(root,'artifacts','shared-printing-'+crypto.randomUUID());
fs.mkdirSync(work,{recursive:true});
const processes=[], handles=[], received=[];
let browser, receiver, page;
function start(args,env,name,cwd=fixture){const fd=fs.openSync(path.join(work,name),'w');handles.push(fd);const child=spawn('dotnet',args,{cwd,env:{...process.env,...env},stdio:['ignore',fd,fd],windowsHide:true});processes.push(child);return child;}
async function wait(fn,timeout=60000){const until=Date.now()+timeout;let last;while(Date.now()<until){try{const result=await fn();if(result)return result;}catch(e){last=e;}await new Promise(r=>setTimeout(r,200));}throw last||new Error('Timed out');}
async function port(){const s=net.createServer();s.listen(0,'127.0.0.1');await once(s,'listening');const p=s.address().port;await new Promise(r=>s.close(r));return p;}
function context(file){const bytes=fs.readFileSync(file),marker=Buffer.from('SINERGIAPRINT-CONTEXT-V1');assert.ok(bytes.subarray(-marker.length).equals(marker));const n=bytes.readInt32LE(bytes.length-marker.length-4);return {bytes,...JSON.parse(bytes.subarray(bytes.length-marker.length-4-n,bytes.length-marker.length-4).toString('utf8'))};}
function sql(statement){const result=spawnSync('python',['-c','import sqlite3,sys,json; c=sqlite3.connect(sys.argv[1]); print(json.dumps(c.execute(sys.argv[2]).fetchall()))',path.join(work,'state/printing.db'),statement],{encoding:'utf8',windowsHide:true});assert.equal(result.status,0,result.stderr);return JSON.parse(result.stdout);}
function pass(message){console.log('PASS '+message);}
(async()=>{
 const base=`http://127.0.0.1:${await port()}/`, bridgePort=await port();
 start([path.join(fixture,'bin/Debug/net10.0/PrintFixture.dll'),'--urls',base],{ASPNETCORE_ENVIRONMENT:'Development',PRINT_FIXTURE_DATA:path.join(work,'state'),PRINT_BRIDGE_PORT:String(bridgePort)},'host.log');
 await wait(async()=>(await fetch(base+'health')).ok);
 browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_PATH||path.join(process.env.LOCALAPPDATA,'ms-playwright/chromium-1208/chrome-win64/chrome.exe')});
 page=await browser.newPage({viewport:{width:1440,height:1100}});const exceptions=[];
 page.on('pageerror',e=>exceptions.push(e.message));
 await page.goto(base+'testes/agente-impressao');
 await page.getByText('Não foi possível reconhecer o agente',{exact:true}).waitFor();
 assert.equal(await page.getByText('Atualização do agente disponível',{exact:true}).count(),0);
 assert.equal(await page.getByLabel('Token do agente',{exact:true}).count(),0);
 const download=page.waitForEvent('download');
 await page.getByRole('button',{name:'Instalar agente',exact:true}).click();
 const file=path.join(work,'automatic.exe');await (await download).saveAs(file);
 const ctx=context(file);assert.equal(ctx.apiBaseUrl,base+'sinergiaprint/');
 pass('Simple disconnected state and prominent contextual installer download; no manual credentials in page');
 const configFile=path.join(work,'resolved.private.json');
 const resolve=spawnSync(file,['--resolve-only',configFile],{windowsHide:true,timeout:30000});assert.equal(resolve.status,0,'Bootstrap redemption failed');
 const config=JSON.parse(fs.readFileSync(configFile,'utf8')).Agent, iid=config.InstallationId;
 assert.equal(config.ApiBaseUrl,base+'sinergiaprint/');assert.ok(!ctx.bytes.includes(Buffer.from(config.Token)));
 assert.equal((await fetch(base+'sinergiaprint/enrollment/redeem',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({code:ctx.code})})).status,400);
 const proof=await page.evaluate(()=>Object.entries(localStorage).find(([key])=>key.endsWith('machine.v1'))[1]);
 assert.equal((await page.request.get(base+'sinergiaprint/installers/'+iid,{headers:{'X-Fixture-User':'other-user'}})).status(),404);
 assert.equal((await page.request.get(base+'sinergiaprint/installers/'+iid,{headers:{'X-Fixture-User':'other-user','X-PrintAgent-Machine':proof}})).status(),200);
 assert.equal((await page.request.get(base+'sinergiaprint/installers/'+iid,{headers:{'X-Fixture-User':'anonymous'}})).status(),401);
 pass('Shared installer resolves credentials, enforces owner/login and rejects reused enrollment');
 const getInstaller=async()=>{const response=await page.request.get(base+'sinergiaprint/installers/'+iid,{headers:{'X-PrintAgent-Machine':proof}});assert.equal(response.status(),200);assert.equal(response.headers()['cache-control'],'no-store');return response.body();};
 const protectedResponse=await getInstaller();assert.ok(!protectedResponse.includes(Buffer.from(config.Token)));
 if(!process.env.PRINT_TEST_SKIP_PUBLIC_DOWNLOAD){
 const installer=path.join(work,'pinned-setup.exe');
 const verify=spawnSync(file,['--download-only',installer],{windowsHide:true,timeout:180000});assert.equal(verify.status,0);
 const expected=fs.readFileSync(path.join(root,'src/SinergiaAI.PrintAgent.Bootstrap/InstallerRelease.cs'),'utf8').match(/Sha256 = "([A-F0-9]{64})"/)[1];
 assert.equal(crypto.createHash('sha256').update(fs.readFileSync(installer)).digest('hex').toUpperCase(),expected);
 pass('Shared endpoint creates uncached downloads; bootstrap verifies the exact public installer without installing it');
 }
 receiver=net.createServer(socket=>{const chunks=[];socket.on('data',b=>chunks.push(b));socket.on('end',()=>received.push(Buffer.concat(chunks)));});
 receiver.listen(0,'127.0.0.1');await once(receiver,'listening');
 start([path.join(root,'src/SinergiaAI.PrintAgent.Agent/bin/Debug/net10.0-windows/win-x64/SinergiaAI.PrintAgent.Agent.dll')],{
  SINERGIAPRINT_Agent__ApiBaseUrl:config.ApiBaseUrl,SINERGIAPRINT_Agent__InstallationId:iid,SINERGIAPRINT_Agent__Token:config.Token,
  SINERGIAPRINT_Agent__LocalBridgePort:String(bridgePort),SINERGIAPRINT_Agent__DataPath:path.join(work,'agent'),SINERGIAPRINT_Agent__PollSeconds:'1',
  SINERGIAPRINT_Agent__NetworkPrinters__0__Name:'Impressora TCP simulada',SINERGIAPRINT_Agent__NetworkPrinters__0__Address:`tcp://127.0.0.1:${receiver.address().port}`},'agent.log');
 await page.getByText('Agente conectado',{exact:true}).waitFor({timeout:60000});
 assert.equal(await page.getByText('Impressora TCP simulada',{exact:true}).count(),0);
 await page.getByRole('button',{name:/Selecionar impressora/}).click();
 await page.getByText('Impressora TCP simulada',{exact:true}).click();
 await page.getByRole('button',{name:/Impressora TCP simulada/}).waitFor();
 pass('Connected state and collapsed printer selector backed by ListView');
 assert.equal((await fetch(`http://127.0.0.1:${bridgePort}/info`,{headers:{Origin:'https://unrelated.example'}})).status,403);
 assert.equal((await fetch(`http://127.0.0.1:${bridgePort}/connect`,{method:'POST',headers:{Origin:base.slice(0,-1),'Content-Type':'application/json'},body:JSON.stringify({apiBaseUrl:base+'wrong/',challenge:'A'.repeat(64)})})).status,409);
 const otherContext=await browser.newContext({viewport:{width:1440,height:1100}});
 await otherContext.addCookies([{name:'FixtureUser',value:'warehouse-user-b',url:base}]);
 const otherPage=await otherContext.newPage();await otherPage.goto(base+'testes/agente-impressao');
 await otherPage.getByText('Agente conectado',{exact:true}).waitFor({timeout:60000});
 const otherProof=await otherPage.evaluate(()=>Object.entries(localStorage).find(([key])=>key.endsWith('machine.v1'))[1]);
 const otherConnection=await otherPage.request.get(base+'fixture/connection/'+iid,{headers:{'X-PrintAgent-Machine':otherProof}});
 assert.equal(otherConnection.status(),200);assert.equal((await otherConnection.json()).instalacao.id,iid);
 assert.equal(sql('select count(*) from installations')[0][0],1);
 await otherContext.addCookies([{name:'FixtureUser',value:'warehouse-user-c',url:base}]);
 await otherPage.reload();await otherPage.getByText('Agente conectado',{exact:true}).waitFor({timeout:60000});
 assert.equal(await otherPage.getByRole('button',{name:'Instalar agente',exact:true}).count(),0);
 assert.equal(sql('select count(*) from installations')[0][0],1);
 pass('Independent browser profile and changed application login automatically discover the same Windows installation; wrong origins/API rejected');
 // Exercise observable discovery failures and version comparison through the real shared C# service.
 const infoUrl=`http://127.0.0.1:${bridgePort}/info`;
 let reportedVersion='0.2.9', infoMode='normal';
 await otherPage.route(infoUrl,async route=>{
  if(infoMode==='unavailable')return route.abort('connectionrefused');
  return route.fulfill({status:infoMode==='denied'?403:200,contentType:'application/json',
   headers:{'Access-Control-Allow-Origin':base.slice(0,-1)},
   body:JSON.stringify({product:'SinergiaAI.PrintAgent',protocol:infoMode==='incompatible'?99:1,version:reportedVersion})});
 });
 const recognize=()=>otherPage.getByRole('button',{name:'Reconhecer computador',exact:true}).click();
 await recognize();await otherPage.getByRole('button',{name:'Atualizar agente',exact:true}).waitFor();
 await otherPage.getByText('Versão instalada: 0.2.9',{exact:false}).waitFor();
 assert.equal(sql('select count(*) from installations')[0][0],1);
 reportedVersion='0.3.0';await recognize();
 await wait(async()=>(await otherPage.getByRole('button',{name:'Atualizar agente',exact:true}).count())===0);
 reportedVersion='0.4.0';await recognize();await otherPage.getByText('Versão instalada: 0.4.0',{exact:false}).waitFor();
 assert.equal(await otherPage.getByRole('button',{name:'Atualizar agente',exact:true}).count(),0);
 reportedVersion='invalid';await recognize();await otherPage.getByText('Versão instalada: invalid',{exact:false}).waitFor();
 assert.equal(await otherPage.getByRole('button',{name:'Atualizar agente',exact:true}).count(),0);
 infoMode='denied';await recognize();await otherPage.getByText('Conexão local não autorizada',{exact:true}).waitFor();
 assert.equal(await otherPage.getByRole('button',{name:'Atualizar agente',exact:true}).count(),0);
 infoMode='incompatible';reportedVersion='0.3.0';await recognize();await otherPage.getByText('Serviço local incompatível',{exact:true}).waitFor();
 infoMode='unavailable';await recognize();await otherPage.getByText('Não foi possível reconhecer o agente',{exact:true}).waitFor();
 assert.equal(await otherPage.getByRole('button',{name:'Atualizar agente',exact:true}).count(),0);
 await otherPage.unroute(infoUrl);await recognize();await otherPage.getByText('Agente conectado',{exact:true}).waitFor();
 pass('Shared connection service distinguishes unavailable/denied/incompatible; update requires a real older Windows version, never equal/newer/invalid or network failure');
 await page.screenshot({path:path.join(work,'desktop-ready.png'),fullPage:true});
 async function fillLabels(quantity,description='Jogo de cama'){
  await page.getByLabel('Código',{exact:true}).fill('316414');await page.getByLabel('Código',{exact:true}).press('Tab');
  await page.getByLabel('Descrição',{exact:true}).fill(description);await page.getByLabel('Descrição',{exact:true}).press('Tab');
  const quantityInput=page.getByRole('spinbutton',{name:'Quantidade',exact:true});
  await quantityInput.click();await quantityInput.press('ControlOrMeta+A');
  await quantityInput.pressSequentially(String(quantity));await quantityInput.press('Tab');
  await page.getByRole('button',{name:`Gerar etiquetas (${quantity})`,exact:true}).waitFor();
  await page.getByRole('button',{name:`Gerar etiquetas (${quantity})`,exact:true}).click();
  await page.getByText(new RegExp(`^${quantity} etiqueta\\(s\\) ·`)).waitFor();
 }
 await fillLabels(1000,'Jogo de cama ^XZ~JA');
 await page.getByText(/1000 etiqueta\(s\)/).waitFor();
 await page.getByRole('checkbox',{name:'Selecionar todas as etiquetas',exact:true}).check();
 await page.getByText(/1000 selecionada\(s\)/).waitFor();
 pass('Generates 1000 rows from code, description and quantity; header checkbox selects beyond the first page');
 await page.getByRole('button',{name:'Imprimir selecionadas',exact:true}).click();
 await page.getByText('1000 etiqueta(s) na fila. Acompanhe o resultado abaixo.',{exact:true}).waitFor({timeout:60000});
 assert.equal(sql('select count(*) from jobs')[0][0],1000);
 await page.reload();
 await page.getByText('Agente conectado',{exact:true}).waitFor();
 await wait(()=>received.length===1000,120000);
 await wait(()=>sql('select count(*) from jobs where status=2')[0][0]===1000,120000);
 for(let i=0;i<1000;i++){
  const zpl=received[i].toString('ascii');assert.equal(zpl.split('^XA').length-1,1);assert.equal(zpl.split('^XZ').length-1,1);
  assert.ok(zpl.includes(`Etiqueta ${i+1}/1000`),`Sequence ${i+1}`);assert.ok(zpl.includes('^BCN,100,Y,N,N^FH_^FD_33_31_36_34_31_34'));
  assert.ok(zpl.includes('_5E_58_5A_7E_4A_41'),'Description command escaping');
 }
 await page.getByText(/1000 enviada\(s\)/).waitFor({timeout:60000});
 assert.equal(sql('select count(*) from jobs')[0][0],1000);
 pass('1000 separate ZPL queue jobs print in sequence with code barcode, escaped descriptions and no duplicates after reload');
 await page.getByRole('button',{name:/Selecionar impressora/}).click();await page.getByText('Impressora TCP simulada',{exact:true}).click();
 await wait(()=>page.getByRole('button',{name:/^Gerar etiquetas/}).isEnabled());
 await page.getByText(/ESC\/POS · Térmica/).click();
 await page.getByText('ESC/POS · Térmica ✓',{exact:true}).waitFor();
 await fillLabels(3,'Jogo de cama');
 await page.getByRole('gridcell',{name:'ESC/POS',exact:true}).first().waitFor();
 await page.getByRole('checkbox',{name:'Selecionar etiqueta 1',exact:true}).check();
 await page.getByRole('button',{name:'Imprimir selecionadas',exact:true}).click();
 await wait(()=>received.length===1001);await page.getByText(/1 enviada\(s\)/).waitFor({timeout:60000});
 assert.equal(sql('select count(*) from jobs')[0][0],1001);
 const esc=received[1000];assert.equal(esc[0],0x1B);assert.equal(esc[1],0x40);
 assert.ok(esc.includes(Buffer.from('Etiqueta 1/3\n316414\nJogo de cama\n')));
 assert.ok(esc.includes(Buffer.concat([Buffer.from([0x1D,0x6B,73,8]),Buffer.from('{B316414')])));
 pass('ESC/POS format generates different content; one row checkbox prints only its document with CODE128');
 await page.getByRole('checkbox',{name:'Selecionar todas as etiquetas',exact:true}).check();
 await page.getByRole('button',{name:'Imprimir selecionadas',exact:true}).click();
 await wait(()=>received.length===1003);await page.getByText(/3 enviada\(s\)/).waitFor({timeout:60000});
 assert.ok(received[1001].includes(Buffer.from('Etiqueta 2/3')));assert.ok(received[1002].includes(Buffer.from('Etiqueta 3/3')));
 assert.equal(sql('select count(*) from jobs')[0][0],1003);
 pass('Select all after partial selection queues only the remaining two rows, preserving sequence');
 await new Promise(r=>receiver.close(r));receiver=null;
 await fillLabels(1);await page.getByRole('checkbox',{name:'Selecionar todas as etiquetas',exact:true}).check();
 await page.getByRole('button',{name:'Imprimir selecionadas',exact:true}).click();
 await page.getByText(/Falha:/).first().waitFor({timeout:60000});
 pass('Printer connection failure is reported in the grid');
 const documents=[{impressoraId:sql('select printer_id from jobs limit 1')[0][0],conteudo:Buffer.from('raw-one').toString('base64'),chaveIdempotencia:crypto.randomUUID()},
                  {impressoraId:sql('select printer_id from jobs limit 1')[0][0],conteudo:Buffer.from('raw-two').toString('base64'),chaveIdempotencia:crypto.randomUUID()}];
 const send=()=>page.request.post(base+'fixture/send/'+iid,{data:documents,headers:{'X-PrintAgent-Machine':proof}});
 const first=await otherPage.request.post(base+'fixture/send/'+iid,{data:documents,headers:{'X-PrintAgent-Machine':otherProof}});assert.equal(first.status(),200);const original=await first.json();
 const second=await send();assert.equal(second.status(),200);assert.deepEqual((await second.json()).map(x=>x.id),original.map(x=>x.id));
 assert.equal((await page.request.post(base+'fixture/send/'+iid,{data:documents,headers:{'X-Fixture-User':'other-user'}})).status(),401);
 assert.equal(sql('select count(*) from jobs')[0][0],1006);
 pass('Different authenticated users on the same machine share its queue and retries; requests without a machine proof are blocked');
 const storage=await page.evaluate(()=>JSON.stringify(localStorage));assert.ok(!storage.includes(config.Token));
 const keys=await page.request.get(base+'fixture/connection/'+iid,{headers:{'X-PrintAgent-Machine':proof}});assert.equal(keys.status(),200);assert.ok(!Object.hasOwn(await keys.json(),'token'));
 assert.ok(!fs.readFileSync(path.join(work,'host.log'),'utf8').includes(config.Token));
 pass('Shared connection DTO, localStorage and server request logs do not expose permanent token');
 await page.screenshot({path:path.join(work,'desktop.png'),fullPage:true});
 await page.setViewportSize({width:390,height:844});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false);
 await page.screenshot({path:path.join(work,'mobile.png'),fullPage:true});
 assert.deepEqual(exceptions,[]);
 pass('Desktop/mobile layouts have no horizontal overflow or browser exceptions');
 await page.setViewportSize({width:1440,height:1100});
 await wait(()=>page.getByRole('button',{name:'Remover vínculo',exact:true}).isEnabled());
 await page.getByRole('button',{name:'Remover vínculo',exact:true}).click();
 await page.getByRole('button',{name:'Cancelar',exact:true}).click();
 assert.equal((await page.request.get(base+'fixture/connection/'+iid,{headers:{'X-PrintAgent-Machine':proof}})).status(),200);
 await page.getByRole('button',{name:'Remover vínculo',exact:true}).click();
 await page.getByRole('button',{name:'Confirmar remoção',exact:true}).click();
 await page.getByText('Vínculo removido deste navegador',{exact:true}).waitFor();
 assert.equal(await page.getByText('Não foi possível reconhecer o agente',{exact:true}).count(),0);
 assert.equal(await page.getByRole('button',{name:'Instalar agente',exact:true}).count(),0);
 assert.equal((await page.request.get(base+'fixture/connection/'+iid,{headers:{'X-PrintAgent-Machine':proof}})).status(),404);
 assert.equal((await page.request.get(base+'sinergiaprint/installers/'+iid)).status(),404);
 assert.equal(await page.getByRole('link',{name:'Abrir aplicativos do Windows'}).getAttribute('href'),'ms-settings:appsfeatures');
 assert.equal(sql('select count(*) from jobs')[0][0],1006);
 await page.getByRole('button',{name:'Reconhecer computador',exact:true}).click();
 await page.getByText('Agente conectado',{exact:true}).waitFor({timeout:60000});
 assert.equal(sql('select count(*) from installations')[0][0],1);
 await page.reload();await page.getByText('Agente conectado',{exact:true}).waitFor({timeout:60000});
 await otherContext.close();
 pass('Forget-browser confirmation supports cancel, revokes its proof and retains queue; installed service is discovered again on reload');
 console.log('All shared printing checks passed. Artifacts: '+work);
})().catch(async error=>{if(page){await page.screenshot({path:path.join(work,'failure.png'),fullPage:true}).catch(()=>{});fs.writeFileSync(path.join(work,'failure.html'),await page.content().catch(()=>''));}console.error(error);console.error('Artifacts: '+work);process.exitCode=1;}).finally(async()=>{
 if(browser)await browser.close();
 for(const child of processes.reverse()){if(child.exitCode===null){child.kill();await Promise.race([once(child,'exit'),new Promise(r=>setTimeout(r,5000))]);}}
 if(receiver)receiver.close();for(const fd of handles)fs.closeSync(fd);
});

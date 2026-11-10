// Verify actual demo error panels without modifying permissions, removing DLLs or starting agents.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { spawn, spawnSync } = require('node:child_process');
const { once } = require('node:events');
const { randomUUID } = require('node:crypto');
const root = path.resolve(__dirname,'..');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || path.join(root,'artifacts/browser-tools/node_modules/playwright'));
const configuration = process.env.PRINTAGENT_TEST_CONFIGURATION || 'Debug';
const work = path.join(root,'artifacts','errors-browser-'+randomUUID());
fs.mkdirSync(work,{recursive:true});
async function port() { const s=net.createServer().listen(0,'127.0.0.1'); await once(s,'listening'); const p=s.address().port; await new Promise(r=>s.close(r)); return p; }
async function ready(base) { for(let i=0;i<150;i++){try{if((await fetch(base+'health')).ok)return;}catch{} await new Promise(r=>setTimeout(r,100));}throw new Error('Host startup timeout'); }
(async()=>{
 const browser=await chromium.launch({headless:true,...(process.env.CHROME_PATH?{executablePath:process.env.CHROME_PATH}:{})});
 try{
  for(const [kind,code,expected] of [['storage','STORAGE_ACCESS_DENIED','access storage'],['sqlite','SQLITE_DEPENDENCY_MISSING','SQLite dependency'],['unknown','BACKEND_INITIALIZATION_FAILED','could not be initialized']]){
   const base=`http://127.0.0.1:${await port()}/`, logPath=path.join(work,kind+'.log'), log=fs.openSync(logPath,'w');
   const host=spawn('dotnet',['run','--project','samples/PrintAgent.Demo','-c',configuration,'--no-build','--no-launch-profile','--','--simulate',`--Demo:ErrorExample=${kind}`,'--urls',base,'--Demo:DataDirectory',path.join(work,kind)],{cwd:root,windowsHide:true,env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development'},stdio:['ignore',log,log]});
   const page=await browser.newPage({viewport:{width:1280,height:1000}});
   const errors=[];page.on('pageerror',e=>errors.push(e.message));
   try{
    await ready(base);await page.goto(base);
    await page.getByRole('status').filter({hasText:'Printing backend unavailable'}).waitFor({timeout:20000});
    assert.match(await page.locator('.connection .detail').innerText(),new RegExp(expected));
    await page.locator('.error-help summary').click();
    assert.match(await page.locator('.error-help').innerText(),new RegExp(code));
    assert.ok(await page.locator('.error-help li').count()>=2);
    const reference=await page.locator('.error-help code').innerText();
    assert.match(reference,/^[a-f0-9]{12}$/);
    assert.ok(fs.readFileSync(logPath,'utf8').includes(reference),'Displayed reference must exist in the host log');
    assert.ok(!await page.getByRole('button',{name:'Download installer',exact:true}).count());
    assert.ok(!(await page.locator('main').innerText()).includes('Simulated unclassified startup failure'),'No raw exception in UI');
    await page.waitForTimeout(3500);
    assert.equal((fs.readFileSync(logPath,'utf8').match(new RegExp('PrintAgent failure '+code,'g'))||[]).length,1,'No automatic retry loop for configuration failures');
    if(kind==='storage') await page.screenshot({path:path.join(work,'administrator-steps.png'),fullPage:true});
    await page.setViewportSize({width:390,height:844});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Mobile error guidance must not overflow');
    assert.deepEqual(errors,[]);
    console.log('PASS',kind,'safe error, numbered steps, correlated reference, no installer/retry loop, responsive panel');
   }finally{
    await page.close();if(host.exitCode===null)spawnSync('taskkill',['/PID',String(host.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});fs.closeSync(log);
   }
  }
 }finally{await browser.close();}
 console.log('All error-panel browser checks passed. Artifacts: '+work);
})().catch(e=>{console.error(e);process.exitCode=1;});

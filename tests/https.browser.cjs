// HTTPS Blazor host to loopback WebSocket, in an isolated browser and temporary agent.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),net=require('node:net');
const {spawn,spawnSync}=require('node:child_process');const {once}=require('node:events');
const {chromium}=require('../artifacts/browser-tools/node_modules/playwright');
const work=path.resolve('artifacts/https-'+require('node:crypto').randomUUID());fs.mkdirSync(work,{recursive:true});
async function port(){const s=net.createServer().listen(0,'127.0.0.1');await once(s,'listening');const p=s.address().port;await new Promise(r=>s.close(r));return p;}
(async()=>{
 const http=`http://localhost:${await port()}`,https=`https://localhost:${await port()}`;
 const log=fs.openSync(path.join(work,'host.log'),'w');
 const host=spawn('dotnet',['run','--project','samples/PrintAgent.Demo','--no-build','--no-launch-profile','--','--simulate','--urls',http+';'+https,'--Demo:DataDirectory',path.join(work,'data'),'--Demo:BridgePort',String(await port())],{windowsHide:true,env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development'},stdio:['ignore',log,log]});
 let browser;
 try{
  for(let i=0;i<200;i++){try{if((await fetch(http+'/health')).ok)break;}catch{}await new Promise(r=>setTimeout(r,100));}
  browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_PATH});
  // The isolated browser accepts the sample host's development certificate only.
  const page=await browser.newPage({ignoreHTTPSErrors:true});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(https);await page.getByText('Agent connected',{exact:true}).waitFor({timeout:30000});
  await page.getByLabel('Printer',{exact:true}).selectOption({label:'Simulated TCP printer'});
  await page.getByRole('button',{name:'Queue request',exact:true}).click();
  await page.getByText('1 capture(s) · latest 20',{exact:true}).waitFor({timeout:15000});
  assert.deepEqual(errors,[]);await page.screenshot({path:path.join(work,'https-connected.png'),fullPage:true});
  console.log('PASS HTTPS Blazor page connects directly to loopback WebSocket and prints to a simulated TCP sink. No API certificate used by the service.');
 }finally{if(browser)await browser.close();if(host.exitCode===null)spawnSync('taskkill',['/PID',String(host.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});fs.closeSync(log);}
 console.log('Artifacts:',work);
})().catch(e=>{console.error(e);process.exitCode=1;});

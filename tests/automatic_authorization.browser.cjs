// Automatic StartAsync flow with a temporary service and simulated local desktop decisions.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),net=require('node:net');
const {spawn,spawnSync}=require('node:child_process');const {once}=require('node:events');
const {chromium}=require('../artifacts/browser-tools/node_modules/playwright');
const work=path.resolve('artifacts/automatic-authorization-'+require('node:crypto').randomUUID());fs.mkdirSync(work,{recursive:true});
async function port(){const s=net.createServer().listen(0,'127.0.0.1');await once(s,'listening');const p=s.address().port;await new Promise(r=>s.close(r));return p;}
async function wait(fn){for(let i=0;i<180;i++){try{const v=await fn();if(v)return v;}catch{}await new Promise(r=>setTimeout(r,150));}throw new Error('Timed out');}
function management(port,request){
 const json=JSON.stringify(request).replaceAll("'","''");
 const script=`$p=[System.IO.Pipes.NamedPipeClientStream]::new('.','SinergiaAI.PrintAgent.Approval.${port}',[System.IO.Pipes.PipeDirection]::InOut);$p.Connect(3000);$w=[System.IO.StreamWriter]::new($p);$w.AutoFlush=$true;$w.WriteLine('${json}');$r=[System.IO.StreamReader]::new($p);Write-Output $r.ReadLine();$p.Dispose()`;
 const r=spawnSync('powershell',['-NoProfile','-NonInteractive','-Command',script],{windowsHide:true,encoding:'utf8',timeout:6000});if(r.status!==0)throw new Error(r.stderr);return r.stdout.trim();
}
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_PATH});
 try{for(const approve of [true,false]){
  const base=`http://127.0.0.1:${await port()}/`,bridge=await port();const log=fs.openSync(path.join(work,approve+'.log'),'w');
  const host=spawn('dotnet',['run','--project','samples/PrintAgent.Demo','--no-build','--no-launch-profile','--','--simulate','--Demo:ErrorExample=authorization','--Demo:AutomaticAuthorization=true','--urls',base,'--Demo:BridgePort',String(bridge),'--Demo:DataDirectory',path.join(work,String(approve))],{windowsHide:true,env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development'},stdio:['ignore',log,log]});
  const page=await browser.newPage();
  try{
   await wait(async()=> (await fetch(base+'health')).ok);await wait(async()=> (await fetch(`http://127.0.0.1:${bridge}/info`,{headers:{Origin:base.slice(0,-1)}})).ok);await page.goto(base);
   const request=await wait(()=>JSON.parse(management(bridge,{Operation:'poll'})));
   assert.equal(request.Origin,base.slice(0,-1));
   assert.equal(management(bridge,{Origin:request.Origin,Approve:approve,RequestId:request.Id}),'OK');
   if(approve){await page.getByText('Agent connected',{exact:true}).waitFor({timeout:15000});assert.equal(await page.getByLabel('Printer',{exact:true}).locator('option').count(),2);}
   else{await page.getByText('Agent not connected',{exact:true}).waitFor({timeout:15000});assert.match(await page.locator('.error-help summary').innerText(),/LOCAL_ORIGIN_DENIED/);}
   await page.screenshot({path:path.join(work,approve?'approved.png':'denied.png'),fullPage:true});
   console.log('PASS StartAsync requests native authorization and handles '+(approve?'approval without a refresh/button':'denial without granting access'));
  }finally{await page.close();if(host.exitCode===null)spawnSync('taskkill',['/PID',String(host.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});fs.closeSync(log);}
 }}finally{await browser.close();}
 console.log('Artifacts:',work);
})().catch(e=>{console.error(e);process.exitCode=1;});

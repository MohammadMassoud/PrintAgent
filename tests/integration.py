"""Real API + Windows agent + TCP receiver. No physical printer or installed service."""
import base64, concurrent.futures, json, os, socket, sqlite3, subprocess, threading, time, uuid
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

ROOT=Path(__file__).resolve().parents[1]
WORK=ROOT/'artifacts'/('test-'+uuid.uuid4().hex)
WORK.mkdir(parents=True)
KEY=uuid.uuid4().hex+uuid.uuid4().hex
processes=[]
handles=[]
def port():
    with socket.socket() as s: s.bind(('127.0.0.1',0));return s.getsockname()[1]
PORT=port();BASE=f'http://127.0.0.1:{PORT}/'
DB=WORK/'printing.db'
def start_api():
    env=os.environ.copy();env['SINERGIAPRINT_Agent__EnableLegacyApi']='true';env.update({'ASPNETCORE_URLS':BASE,'Printing__ApiKey':KEY,'Printing__DatabasePath':str(DB)})
    return start(['dotnet',str(ROOT/'src/SinergiaAI.PrintAgent.Api/bin/Debug/net10.0/SinergiaAI.PrintAgent.Api.dll')],env,'api')
def start(args,env,name):
    log=open(WORK/(name+'-'+uuid.uuid4().hex[:6]+'.log'),'w');handles.append(log)
    proc=subprocess.Popen(args,cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT);processes.append(proc);return proc

def call(path,body=None,token=None,auth=True):
    headers={'Content-Type':'application/json'}
    if auth: headers['X-Api-Key']=KEY
    if token: headers['X-Agent-Token']=token
    request=Request(BASE+path,data=json.dumps(body).encode() if body is not None else None,headers=headers)
    try:
        with urlopen(request,timeout=5) as response:
            data=response.read();return response.status,json.loads(data) if data else None
    except HTTPError as e:
        data=e.read();return e.code,json.loads(data) if data else None

def wait(fn,seconds=20):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        try:
            value=fn()
            if value:return value
        except (URLError,ConnectionError):pass
        time.sleep(.15)
    raise AssertionError('Timed out')

def stop(proc):
    proc.terminate()
    try:proc.wait(timeout=10)
    except subprocess.TimeoutExpired:proc.kill();proc.wait()

def expect(condition,label):
    assert condition,label
    print('PASS',label,flush=True)

def job(installation,printer,payload=b'^XA^FO20,20^FDTEST^FS^XZ',key=None):
    request={'installationId':installation,'printerId':printer,'contentBase64':base64.b64encode(payload).decode(),'idempotencyKey':key or uuid.uuid4().hex}
    status,data=call('api/jobs',request);assert status==202,(status,data)
    return data,request

try:
    api=start_api();wait(lambda:call('health',auth=False)[0]==200)
    expect(call('api/installations',auth=False)[0]==401,'API requires backend key')
    _,credentials=call('api/installations',{'name':'Integration TCP'})
    iid=credentials['installationId'];token=credentials['agentToken'];agent=f'agent/{iid}/'
    expect(call(agent+'claim',{},token='invalid',auth=False)[0]==401,'Agent requires installation token')
    _,other=call('api/installations',{'name':'Other computer'})
    expect(call(agent+'claim',{},token=other['agentToken'],auth=False)[0]==401,'Installation isolation')
    received=[];ready=threading.Event();TCP=port()
    def receiver():
        with socket.socket() as server:
            server.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);server.bind(('127.0.0.1',TCP));server.listen();server.settimeout(20);ready.set()
            conn,_=server.accept()
            with conn:
                chunks=[]
                while True:
                    data=conn.recv(65536)
                    if not data:break
                    chunks.append(data)
                received.append(b''.join(chunks))
    thread=threading.Thread(target=receiver,daemon=True);thread.start();ready.wait(3)
    env=os.environ.copy();env['SINERGIAPRINT_Agent__EnableLegacyApi']='true';env.update({'SINERGIAPRINT_Agent__ApiBaseUrl':BASE,'SINERGIAPRINT_Agent__InstallationId':iid,'SINERGIAPRINT_Agent__Token':token,'SINERGIAPRINT_Agent__DataPath':str(WORK/'agent'),'SINERGIAPRINT_Agent__PollSeconds':'1','SINERGIAPRINT_Agent__LocalBridgePort':'0','SINERGIAPRINT_Agent__NetworkPrinters__0__Name':'TCP Test','SINERGIAPRINT_Agent__NetworkPrinters__0__Address':f'tcp://127.0.0.1:{TCP}'})
    def start_agent():return start(['dotnet',str(ROOT/'src/SinergiaAI.PrintAgent.Agent/bin/Debug/net10.0-windows/win-x64/SinergiaAI.PrintAgent.Agent.dll')],env,'agent')
    worker=start_agent()
    printers=wait(lambda:call(f'api/installations/{iid}/printers')[1])
    printer=next(p['id'] for p in printers if p['name']=='TCP Test')
    expect(True,'Agent discovers and reports printers')
    content=b'^XA\r\n^FO10,10^FDexact raw bytes\x80\xff^FS^XZ'
    sent,request=job(iid,printer,content)
    result=wait(lambda: (v if (v:=call('api/jobs/'+sent['id'])[1])['status']=='Sent' else None))
    thread.join(3)
    expect(received==[content],'Real worker delivers unchanged bytes over TCP and confirms Sent')
    status,repeated=call('api/jobs',request)
    expect(status==202 and repeated['id']==sent['id'],'Idempotency prevents duplicate jobs')
    request['contentBase64']=base64.b64encode(b'different').decode()
    expect(call('api/jobs',request)[0]==409,'Idempotency key cannot change content')
    failed,_=job(iid,printer)
    result=wait(lambda:(v if (v:=call('api/jobs/'+failed['id'])[1])['status']=='Failed' else None))
    expect(bool(result['error']),'Connection refused is reported with an error')
    stop(worker)
    pending,_=job(iid,printer)
    stop(api);api=start_api();wait(lambda:call('health',auth=False)[0]==200)
    expect(call('api/jobs/'+pending['id'])[1]['status']=='Pending','Queue survives API restart')
    def claim():return call(agent+'claim',{},token=token,auth=False)
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:claims=list(pool.map(lambda _:claim(),range(8)))
    claimed=[body for status,body in claims if status==200]
    expect(len(claimed)==1 and sum(status==204 for status,_ in claims)==7,'Concurrent requests claim a job only once')
    claim_body=claimed[0]
    status,_=call(f"agent/{other['installationId']}/jobs/{pending['id']}/complete",{'claimId':claim_body['claimId'],'status':'Sent','error':None},token=other['agentToken'],auth=False)
    expect(status==409,'Other installation cannot complete the job')
    # Simulate crash after durable Started entry, before confirmation.
    journal=WORK/'agent'/('journal-'+uuid.UUID(iid).hex+'.db')
    with sqlite3.connect(journal) as db:db.execute('INSERT INTO journal(id,claim_id,status) VALUES(?,?,1)',(pending['id'],claim_body['claimId']))
    worker=start_agent()
    result=wait(lambda:(v if (v:=call('api/jobs/'+pending['id'])[1])['status']=='Unknown' else None))
    expect(bool(result['error']),'Restart reports interrupted send as Unknown without reprinting')
    stop(worker)
    expired,_=job(iid,printer);status,lease=claim();assert status==200
    with sqlite3.connect(DB) as db:db.execute("UPDATE jobs SET claimed_at='2000-01-01T00:00:00.0000000+00:00' WHERE id=?",(expired['id'],))
    expect(call('api/jobs/'+expired['id'])[1]['status']=='Unknown','Lost claim expires to Unknown, never automatic reprint')
    expect(claim()[0]==204,'Expired job is not sent again')
    # Acknowledgement retry from local journal must not send printer data again.
    with sqlite3.connect(journal) as db:db.execute('UPDATE journal SET acked=0 WHERE id=?',(sent['id'],))
    worker=start_agent()
    def acknowledged():
        with sqlite3.connect(journal) as db:return db.execute('SELECT acked FROM journal WHERE id=?',(sent['id'],)).fetchone()[0]==1
    wait(acknowledged)
    expect(call('api/jobs/'+sent['id'])[1]['status']=='Sent','Durable acknowledgement retry preserves result without resending bytes')
    stop(worker)
    sdk_env=os.environ.copy();sdk_env.update({'PRINT_API_URL':BASE,'PRINT_API_KEY':KEY})
    sdk=subprocess.run(['dotnet',str(ROOT/'samples/IntegrationDemo/bin/Debug/net10.0/IntegrationDemo.dll')],env=sdk_env,capture_output=True,text=True,timeout=15)
    expect(sdk.returncode==0 and 'TCP Test' in sdk.stdout,'C# client lists installations and printers')
    print('All integration checks passed. Logs:',WORK)
finally:
    for proc in processes:
        if proc.poll() is None:stop(proc)
    for handle in handles:handle.close()

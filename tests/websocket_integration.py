"""Protocol 2, real temporary agent and loopback TCP sink. No service installation or physical printing."""
import base64, hashlib, json, os, socket, sqlite3, struct, subprocess, threading, time, uuid
from pathlib import Path
from urllib.request import Request, urlopen
ROOT=Path(__file__).resolve().parents[1]
WORK=ROOT/'artifacts'/('websocket-'+uuid.uuid4().hex);WORK.mkdir(parents=True)
def port():
 with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
PORT=port();TCP=port();ORIGIN='https://print.example';OTHER='https://other.example';DENIED='https://untrusted.example'
processes=[];handles=[];received=[];sink=socket.socket();sink.bind(('127.0.0.1',TCP));sink.listen();sink.settimeout(.3);ending=False
def capture():
 while not ending:
  try:c,_=sink.accept()
  except socket.timeout:continue
  except OSError:return
  with c:
   data=b''
   while True:
    b=c.recv(65536)
    if not b:break
    data+=b
   received.append(data)
threading.Thread(target=capture,daemon=True).start()
def start():
 env=os.environ.copy()
 for k in list(env):
  if k.startswith('SINERGIAPRINT_'):del env[k]
 values={'DataPath':str(WORK/'data'),'LocalBridgePort':str(PORT),'EnableLegacyApi':'false','OnlyNetworkPrinters':'true','AllowedOrigins__0':ORIGIN,'AllowedOrigins__1':OTHER,'NetworkPrinters__0__Name':'TCP fixture','NetworkPrinters__0__Address':f'tcp://127.0.0.1:{TCP}'}
 env.update({'SINERGIAPRINT_Agent__'+k:v for k,v in values.items()})
 f=open(WORK/('agent-'+uuid.uuid4().hex+'.log'),'w');handles.append(f)
 p=subprocess.Popen(['dotnet',str(ROOT/'src/SinergiaAI.PrintAgent.Agent/bin/Debug/net10.0-windows/win-x64/SinergiaAI.PrintAgent.Agent.dll')],env=env,stdout=f,stderr=subprocess.STDOUT,cwd=ROOT);processes.append(p)
 wait(lambda:info());return p
def info():
 with urlopen(Request(f'http://127.0.0.1:{PORT}/info',headers={'Origin':ORIGIN}),timeout=2) as r:return json.load(r)['protocol']==2
def wait(fn,seconds=20):
 end=time.monotonic()+seconds
 while time.monotonic()<end:
  try:
   value=fn()
   if value:return value
  except (OSError,ValueError):pass
  time.sleep(.05)
 raise AssertionError('Timed out')
def stop(p):
 p.terminate()
 try:p.wait(timeout=10)
 except subprocess.TimeoutExpired:p.kill();p.wait()
class WS:
 def __init__(self,origin):
  self.s=socket.create_connection(('127.0.0.1',PORT),timeout=5);self.s.settimeout(10)
  key=base64.b64encode(os.urandom(16)).decode()
  self.s.sendall(f'GET /ws HTTP/1.1\r\nHost: 127.0.0.1:{PORT}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: {key}\r\nOrigin: {origin}\r\n\r\n'.encode())
  header=b''
  while not header.endswith(b'\r\n\r\n'):header+=self.s.recv(1)
  assert b'101' in header,header
  assert base64.b64encode(hashlib.sha1((key+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest()) in header
 def exact(self,n):
  data=b''
  while len(data)<n:
   b=self.s.recv(n-len(data));assert b,'closed';data+=b
  return data
 def send(self,method,**data):
  value=json.dumps({'id':uuid.uuid4().hex,'method':method,**data}).encode();mask=os.urandom(4);n=len(value)
  header=b'\x81'+(bytes([128+n]) if n<126 else b'\xfe'+struct.pack('!H',n) if n<65536 else b'\xff'+struct.pack('!Q',n))
  self.s.sendall(header+mask+bytes(v^mask[i%4] for i,v in enumerate(value)))
 def receive(self):
  while True:
   a,b=self.exact(2);n=b&127
   if n==126:n=struct.unpack('!H',self.exact(2))[0]
   elif n==127:n=struct.unpack('!Q',self.exact(8))[0]
   data=self.exact(n)
   if a&15==9:continue
   assert a&15==1,(a,data)
   result=json.loads(data)
   if result.get('eventType')=='jobChanged':continue
   return result
 def call(self,method,**data):self.send(method,**data);return self.receive()
 def close(self):self.s.close()
def approve_via_pipe(origin,approved):
 # Only this fixture's temporary pipe and isolated queue are addressed.
 script = f"$p=[System.IO.Pipes.NamedPipeClientStream]::new('.', 'SinergiaAI.PrintAgent.Approval.{PORT}',[System.IO.Pipes.PipeDirection]::InOut);$p.Connect(5000);$w=[System.IO.StreamWriter]::new($p);$w.AutoFlush=$true;$w.WriteLine((@{{Origin='{origin}';Approve=${str(approved).lower()}}}|ConvertTo-Json -Compress));$r=[System.IO.StreamReader]::new($p);Write-Output $r.ReadLine();$p.Dispose()"
 result=subprocess.run(['powershell','-NoProfile','-NonInteractive','-Command',script],capture_output=True,text=True,timeout=15)
 assert result.returncode==0 and result.stdout.strip()=='OK',(result.stdout,result.stderr)
def expect(x,message):assert x,message;print('PASS',message,flush=True)
def doc(key,text,printer=None):return {'printerId':printer or f'tcp:tcp://127.0.0.1:{TCP}','contentBase64':base64.b64encode(text).decode(),'idempotencyKey':key}
def result(ws,job):return ws.call('jobs',jobIds=[job])['result'][0]
try:
 p=start();a=WS(ORIGIN);machine=a.call('hello')['result'];expect(machine['protocol']==2,'Protocol 2 hello; no remote API')
 denied=WS(DENIED);expect(denied.call('hello')['error']=='LOCAL_ORIGIN_DENIED','Unapproved website cannot list printers');expect(denied.call('enqueue',documents=[doc('bad',b'bad')])['error']=='LOCAL_ORIGIN_DENIED','Unapproved website cannot print');approve_via_pipe(DENIED,True);expect(denied.call('hello')['result']['protocol']==2,'Windows named-pipe approval unlocks an existing socket');approve_via_pipe(DENIED,False);expect(denied.call('hello')['error']=='LOCAL_ORIGIN_DENIED','Windows named-pipe revocation takes effect');denied.close()
 docs=[doc('batch-'+str(i),('label '+str(i)).encode()) for i in range(15)]
 jobs=a.call('enqueue',documents=docs)['result'];ids=[j['id'] for j in jobs]
 wait(lambda:result(a,ids[-1])['status']==2);expect(received==[('label '+str(i)).encode() for i in range(15)],'FIFO delivery preserves exact RAW bytes')
 same=a.call('enqueue',documents=docs)['result'];expect([j['id'] for j in same]==ids and len(received)==15,'Repeated keys return original jobs without reprinting')
 expect(a.call('enqueue',documents=[doc('batch-0',b'changed')])['error']=='REQUEST_INVALID','Changed payload with same key rejected')
 expect(a.call('enqueue',documents=[doc('atomic',b'must not print'),doc('invalid',b'bad','windows:missing')])['error']=='REQUEST_INVALID','Invalid batch rejected atomically')
 b=WS(OTHER);expect(b.call('jobs',jobIds=ids)['result']==[],'Job history isolated by website origin')
 second=b.call('enqueue',documents=[doc('batch-0',b'another origin')])['result'][0];wait(lambda:result(b,second['id'])['status']==2);expect(received[-1]==b'another origin','Request keys scoped per website')
 a.send('enqueue',documents=[doc('lost-response',b'durable response lost')]);a.close();wait(lambda:len(received)==17)
 a=WS(ORIGIN);retry=a.call('enqueue',documents=[doc('lost-response',b'durable response lost')])['result'][0];expect(retry['status']==2 and len(received)==17,'Lost response and browser disconnect do not duplicate delivery')
 machine_id=machine['machineId'];a.close();b.close();stop(p)
 # Seed an interrupted intent and an accepted-but-not-started document in this isolated fixture only.
 now='2026-11-10T00:00:00+00:00';unknown=str(uuid.uuid4());pending=str(uuid.uuid4())
 with sqlite3.connect(WORK/'data/local-queue.db') as db:
  for jid,key,state,text in [(unknown,'interrupted',1,b'NEVER RESEND'),(pending,'pending',0,b'accepted before restart')]:
   db.execute('INSERT INTO jobs(id,origin,request_key,printer,hash,content,status,created,updated) VALUES(?,?,?,?,?,?,?,?,?)',(jid,ORIGIN,key,f'tcp:tcp://127.0.0.1:{TCP}',hashlib.sha256(text).hexdigest().upper(),text,state,now,now))
 p=start();a=WS(ORIGIN);expect(a.call('hello')['result']['machineId']==machine_id,'Machine identity survives restart')
 wait(lambda:result(a,pending)['status']==2);expect(result(a,unknown)['status']==4 and b'NEVER RESEND' not in received,'Interrupted delivery becomes Unknown and is never resent')
 expect(received[-1]==b'accepted before restart','Accepted local queue resumes after restart without browser submission')
 expect(a.call('enqueue',documents=docs)['result'][0]['id']==ids[0] and len(received)==18,'Idempotency survives process restart')
 with sqlite3.connect(WORK/'data/local-queue.db') as db:db.execute('DELETE FROM origins WHERE origin=?',(ORIGIN,))
 expect(a.call('printers')['error']=='LOCAL_ORIGIN_DENIED','Revocation applies to already-open sockets');a.close()
 print('All direct WebSocket integration checks passed. Logs:',WORK,flush=True)
finally:
 ending=True;sink.close()
 for p in processes:
  if p.poll() is None:stop(p)
 for f in handles:f.close()

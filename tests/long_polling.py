"""Real HTTP long-poll tests plus temporary workers against legacy/failing APIs.
No Windows service installation and no physical printer output.
"""
import concurrent.futures
import http.client
import json
import os
from pathlib import Path
import socket
import subprocess
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'artifacts' / ('long-poll-' + uuid.uuid4().hex)
WORK.mkdir(parents=True)
KEY = uuid.uuid4().hex * 2
processes, handles = [], []

def port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]

BASE = f'http://127.0.0.1:{port()}/'

def start(args, env, name):
    log = WORK / (name + '.log')
    handle = log.open('w'); handles.append(handle)
    proc = subprocess.Popen(args, cwd=ROOT, env={**os.environ, **env}, stdout=handle, stderr=subprocess.STDOUT)
    processes.append(proc)
    return proc, log

def stop(proc):
    if proc.poll() is not None: return
    proc.terminate()
    try: proc.wait(timeout=8)
    except subprocess.TimeoutExpired: proc.kill(); proc.wait()

def call(path, body=None, token=None, timeout=25):
    headers = {'Content-Type': 'application/json', 'X-Api-Key': KEY}
    if token: headers['X-Agent-Token'] = token
    request = Request(BASE + path, data=json.dumps(body).encode() if body is not None else None, headers=headers)
    try: response = urlopen(request, timeout=timeout)
    except HTTPError as error: response = error
    with response:
        data = response.read()
        return response.status, json.loads(data) if data else None, response.headers

def wait(fn, seconds=15):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            result = fn()
            if result: return result
        except (URLError, ConnectionError): pass
        time.sleep(.05)
    raise AssertionError('Timed out')

def check(value, message):
    assert value, message
    print('PASS ' + message, flush=True)

def installation():
    status, value, _ = call('api/installations', {'name': 'Long-poll test'})
    assert status == 200, (status, value)
    assert call(f'agent/{value["installationId"]}/printers',
        {'printers': [{'id': 'test:raw', 'name': 'Simulated destination', 'isDefault': False}]}, value['agentToken'])[0] == 204
    return value['installationId'], value['agentToken']

def submit(iid):
    status, job, _ = call('api/jobs', {'installationId': iid, 'printerId': 'test:raw',
        'contentBase64': 'dGVzdA==', 'idempotencyKey': uuid.uuid4().hex})
    assert status == 202, (status, job)
    return job

def claim(iid, token, seconds=0):
    return call(f'agent/{iid}/claim?waitSeconds={seconds}', {}, token)

def complete(iid, token, job):
    assert call(f'agent/{iid}/jobs/{job["id"]}/complete',
        {'claimId': job['claimId'], 'status': 'Sent', 'error': None}, token)[0] == 204

def worker(base, iid, token, label):
    return start(['dotnet', str(ROOT / 'src/SinergiaAI.PrintAgent.Agent/bin/Debug/net10.0-windows/win-x64/SinergiaAI.PrintAgent.Agent.dll')], {
        'SINERGIAPRINT_Agent__ApiBaseUrl': base,
        'SINERGIAPRINT_Agent__InstallationId': iid,
        'SINERGIAPRINT_Agent__Token': token,
        'SINERGIAPRINT_Agent__DataPath': str(WORK / label),
        'SINERGIAPRINT_Agent__LocalBridgePort': '0',
        'SINERGIAPRINT_Agent__PollSeconds': '1',
        'SINERGIAPRINT_Agent__LongPollSeconds': '20',
    }, label)

try:
    api, api_log = start(['dotnet', str(ROOT / 'src/SinergiaAI.PrintAgent.Api/bin/Debug/net10.0/SinergiaAI.PrintAgent.Api.dll')], {
        'ASPNETCORE_URLS': BASE, 'Printing__ApiKey': KEY,
        'Printing__DatabasePath': str(WORK / 'printing.db'),
        'Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics': 'Information'
    }, 'api')
    wait(lambda: call('health')[0] == 200)
    iid, token = installation()
    other, other_token = installation()
    for seconds in (-1, 21):
        check(claim(iid, token, seconds)[0] == 400, f'Reject waitSeconds={seconds}')
    started = time.monotonic()
    check(claim(iid, 'invalid', 20)[0] == 401 and time.monotonic() - started < 1,
          'Authenticate before waiting')
    started = time.monotonic()
    status, _, headers = claim(iid, token, 1)
    check(status == 204 and .85 <= time.monotonic() - started < 3 and headers['X-PrintAgent-Long-Poll'] == '20',
          'Empty queue waits asynchronously then returns 204 with capability header')
    started = time.monotonic()
    check(call(f'agent/{iid}/claim', {}, token)[0] == 204 and time.monotonic() - started < 1,
          'Legacy claim without wait remains immediate')
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        pending = pool.submit(claim, iid, token, 5)
        time.sleep(.2)
        started = time.monotonic()
        queued = submit(iid)
        response = pending.result(timeout=2)
        check(response[0] == 200 and response[1]['id'] == queued['id'] and time.monotonic() - started < 1.5,
              'Commit wakes waiting agent immediately; waiting holds no SQLite write lock')
        complete(iid, token, response[1])
        for _ in range(8):
            pending = pool.submit(claim, iid, token, 3)
            queued = submit(iid)  # Intentionally race subscription/initial database check.
            response = pending.result(timeout=2)
            assert response[0] == 200 and response[1]['id'] == queued['id']
            complete(iid, token, response[1])
        check(True, 'No lost wake-up when enqueue races waiter registration')
        a = pool.submit(claim, iid, token, 1)
        b = pool.submit(claim, iid, token, 1)
        time.sleep(.15); queued = submit(iid)
        responses = [a.result(timeout=3), b.result(timeout=3)]
        check(sorted(r[0] for r in responses) == [200, 204], 'Concurrent waiters cannot reserve the same job twice')
        complete(iid, token, next(r[1] for r in responses if r[0] == 200))
        submit(iid); first = claim(iid, token)[1]
        pending = pool.submit(claim, iid, token, 5)
        time.sleep(.15); second = submit(iid)
        time.sleep(.15)
        assert not pending.done()
        started = time.monotonic(); complete(iid, token, first)
        response = pending.result(timeout=2)
        check(response[0] == 200 and response[1]['id'] == second['id'] and time.monotonic() - started < 1.5,
              'Completing reserved job wakes waiter for the next queued job')
        complete(iid, token, response[1])
        pending = pool.submit(claim, iid, token, 1)
        time.sleep(.15); queued = submit(other)
        time.sleep(.15)
        check(not pending.done() and pending.result(timeout=2)[0] == 204, 'Other installation cannot consume queued work')
        complete(other, other_token, claim(other, other_token)[1])
    # Disconnect a real socket while the server is waiting, then verify the installation is usable.
    connection = http.client.HTTPConnection('127.0.0.1', int(BASE.split(':')[-1].strip('/')), timeout=3)
    connection.request('POST', f'/agent/{iid}/claim?waitSeconds=20', body=b'{}',
        headers={'X-Agent-Token': token, 'Content-Type': 'application/json'})
    time.sleep(.15)
    connection.sock.shutdown(socket.SHUT_RDWR); connection.close()
    queued = submit(iid); response = claim(iid, token, 1)
    check(response[0] == 200 and response[1]['id'] == queued['id'], 'Cancelled HTTP wait releases resources and does not reserve future work')
    complete(iid, token, response[1])
    idle_iid, idle_token = installation()
    idle, _ = worker(BASE, idle_iid, idle_token, 'idle-worker')
    wait(lambda: call(f'api/installations/{idle_iid}/printers')[1] is not None)
    time.sleep(22)
    stop(idle)
    starts = [line for line in api_log.read_text(errors='replace').splitlines()
        if 'Request starting' in line and f'/agent/{idle_iid}/claim?' in line]
    check(1 <= len(starts) <= 2, f'Idle worker uses at most two claims in 22 seconds (observed {len(starts)})')
    check(call('api/installations')[1] is not None, 'Backend remains responsive while a worker waits')

    for mode in ('legacy', 'failure'):
        times = []
        class Handler(BaseHTTPRequestHandler):
            protocol_version = 'HTTP/1.1'
            def handle(self):
                try: super().handle()
                except ConnectionResetError: pass  # Expected when the temporary worker is stopped.
            def log_message(self, *_): pass
            def do_POST(self):
                self.rfile.read(int(self.headers.get('Content-Length', 0)))
                is_claim = '/claim' in self.path
                if is_claim: times.append(time.monotonic())
                self.send_response(503 if mode == 'failure' and is_claim else 204)
                self.send_header('Content-Length', '0'); self.end_headers()
        server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        proc, log = worker(f'http://127.0.0.1:{server.server_port}/', str(uuid.uuid4()), 'test-token', mode)
        try:
            wait(lambda: len(times) >= 4, seconds=16)
            stop(proc)
            gaps = [b - a for a, b in zip(times, times[1:])]
            if mode == 'legacy':
                check(all(gap >= .85 for gap in gaps) and 'The API does not support long polling' in log.read_text(errors='replace'),
                      'New worker falls back safely on an older API without a request storm')
            else:
                check(gaps[0] >= .85 and gaps[1] >= 1.5 and gaps[2] >= 3,
                      'Unavailable API uses increasing retry delays with jitter')
        finally:
            stop(proc); server.shutdown(); server.server_close()
    print('All long-poll checks passed. Logs:', WORK, flush=True)
finally:
    for proc in processes: stop(proc)
    for handle in handles: handle.close()

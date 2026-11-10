// Protocol 2. RAW bytes travel directly from this browser to the local Windows service.
let socket, observer, heartbeat;
let sequence = 0;
const pending = new Map();
function fail(code) { const e = new Error(code); e.code = code; return e; }
function closePending() {
    clearInterval(heartbeat);
    for (const item of pending.values()) { clearTimeout(item.timer); item.reject(fail('AGENT_UNREACHABLE')); }
    pending.clear();
}
export async function connect(options, callback) {
    disconnect(); observer = callback;
    try {
        const info = await fetch(`http://127.0.0.1:${options.port}/info`, { signal: AbortSignal.timeout(5000), credentials: 'omit' });
        const version = await info.json();
        if (version.product !== 'SinergiaAI.PrintAgent' || version.protocol !== 2)
            return { error: 'AGENT_UPDATE_REQUIRED', version: version.version };
        const current = socket = new WebSocket(`ws://127.0.0.1:${options.port}/ws`);
        await new Promise((resolve, reject) => {
            const timer = setTimeout(() => { current.close(); reject(fail('AGENT_UNREACHABLE')); }, 5000);
            current.onopen = () => { clearTimeout(timer); resolve(); };
            current.onerror = () => { clearTimeout(timer); reject(fail('AGENT_UNREACHABLE')); };
        });
        current.onmessage = event => {
            let response; try { response = JSON.parse(event.data); } catch { current.close(); return; }
            if (response.eventType === 'jobChanged') { observer?.invokeMethodAsync('JobChanged',response.job).catch(() => {}); return; }
            const item = pending.get(response.id); if (!item) return;
            pending.delete(response.id); clearTimeout(item.timer);
            if (response.error) item.reject(fail(response.error)); else item.resolve(response.result);
        };
        current.onclose = () => {
            if (socket !== current) return;
            socket = undefined; closePending();
            observer?.invokeMethodAsync('BridgeClosed').catch(() => {});
        };
        let result;
        try { result = await request('hello'); }
        catch (e) { return { error: e.code || 'AGENT_UNREACHABLE', version: version.version }; }
        heartbeat = setInterval(() => {
            if (document.visibilityState !== 'visible') return;
            request('ping').catch(() => { if (socket === current) current.close(); });
        }, 25000);
        return result;
    } catch { return { error: 'AGENT_UNREACHABLE' }; }
}
export function request(method, data = {}) {
    if (socket?.readyState !== WebSocket.OPEN) return Promise.reject(fail('AGENT_UNREACHABLE'));
    return new Promise((resolve, reject) => {
        const id = String(++sequence);
        const timer = setTimeout(() => { pending.delete(id); reject(fail('OPERATION_TIMED_OUT')); }, 30000);
        pending.set(id, { resolve, reject, timer });
        try { const text = JSON.stringify({ id, method, ...data }); if (new TextEncoder().encode(text).length > 8 * 1024 * 1024) throw fail('REQUEST_INVALID'); socket.send(text); }
        catch (e) { clearTimeout(timer); pending.delete(id); reject(fail(e.code || 'AGENT_UNREACHABLE')); }
    });
}
export function disconnect() {
    const previous = socket; socket = undefined; observer = undefined;
    closePending(); if (previous) previous.close();
}
export function authorize(port, revoke = false) {
    const link = document.createElement('a');
    link.href = `sinergia-printagent://authorize?origin=${encodeURIComponent(location.origin)}&port=${port}&revoke=${revoke}`;
    link.click();
}
export function downloadInstaller(url) {
    const link = document.createElement('a'); link.href = url; link.rel = 'noopener'; link.click();
}

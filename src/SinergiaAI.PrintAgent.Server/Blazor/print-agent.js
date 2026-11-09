// Browser bridge. The Windows service only proves its installation; no permanent agent token enters this module.
export async function discover(options, existingProof = null) {
    const bridge = options.bridgeUrl;
    const local = { mode: 'cors', credentials: 'omit', cache: 'no-store', targetAddressSpace: 'loopback' };
    let version = null;
    try {
        const info = await fetch(bridge + '/info', { ...local, signal: AbortSignal.timeout(4000) });
        if (info.status === 403) return { status: 'origin-denied', error: 'The local service rejected this application origin. Check the API URL configured in the agent.' };
        if (!info.ok) return { status: 'error', error: 'The local service returned an error. Check the Windows service and try again.' };
        const details = await info.json();
        if (details.product !== 'SinergiaAI.PrintAgent') return { status: 'incompatible', error: 'The local port is being used by another service.' };
        version = typeof details.version === 'string' ? details.version : null;
        if (details.protocol !== 1) return { status: 'incompatible', version, error: 'The agent protocol is incompatible with this application. Check the integration and agent versions.' };
    } catch {
        // A failed fetch cannot reliably distinguish missing service, stopped service or browser denial.
        return { status: 'unavailable', error: 'The local service did not respond. Check that SinergiaPrint Agent is running and allow local network access in your browser. Download the installer if it is not installed.' };
    }
    try {
        const challengeResponse = await fetch(options.browserApiBaseUrl + 'machine/challenge', { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(10000) });
        if (!challengeResponse.ok) throw new Error('Could not start machine recognition. Sign in again.');
        const challenge = (await challengeResponse.json()).challenge;
        const connected = await fetch(bridge + '/connect', {
            ...local, method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ apiBaseUrl: options.agentApiBaseUrl, challenge }), signal: AbortSignal.timeout(18000)
        });
        if (connected.status === 409) return { status: 'other-application', version, error: 'The agent on this computer is connected to another application.' };
        if (connected.status === 403) return { status: 'origin-denied', version, error: 'The local service rejected this application origin. Check the API URL configured in the agent.' };
        if (!connected.ok) throw new Error('The local service was found but could not confirm its connection with this application.');
        const proofResponse = await fetch(options.browserApiBaseUrl + 'machine/proof', {
            credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(10000),
            headers: { 'X-PrintAgent-Challenge': challenge, ...(existingProof ? { 'X-PrintAgent-Machine': existingProof } : {}) }
        });
        if (!proofResponse.ok) throw new Error('Machine recognition expired. Try connecting again.');
        return { status: 'connected', version, ...(await proofResponse.json()) };
    } catch (error) { return { status: 'error', version, error: error.message || 'Machine recognition failed.' }; }
}

export async function downloadInstaller(url, proof) {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-PrintAgent-Machine': proof } });
    if (!response.ok) throw new Error('Could not generate an installer for this computer. Refresh the connection and try again.');
    const blob = await response.blob();
    const objectUrl = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = objectUrl; link.download = 'SinergiaAI.PrintAgent-Setup.exe';
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(objectUrl), 30000);
}

"""Validate install configuration without elevation, installing or stopping services."""
import copy
import json
from pathlib import Path
import subprocess
import uuid

root = Path(__file__).resolve().parents[1]
staging = root / 'artifacts' / ('installer-validation-' + uuid.uuid4().hex)
staging.mkdir(parents=True)
# Inno's preparation step extracts only this script, before copying the agent executable.
# Validate directly from scripts/ to cover that staging case without installing anything.
script = root / 'scripts/install.ps1'
assert not (script.parent / 'SinergiaAI.PrintAgent.Agent.exe').exists()
valid = {'Agent': {'ApiBaseUrl': 'https://printing.example.com/', 'InstallationId': str(uuid.uuid4()), 'Token': 'test-token'}}
cases = [('HTTPS', valid, True)]
for url, expected in [('http://localhost:5189/', True), ('http://127.0.0.1:5189/', True), ('http://[::1]:5189/', True), ('http://printing.example.com/', False), ('http://localhost.evil.example/', False), ('file:///C:/bad', False), ('invalid-url', False)]:
    config = copy.deepcopy(valid)
    config['Agent']['ApiBaseUrl'] = url
    cases.append((url, config, expected))
for name, field, value in [('Empty GUID', 'InstallationId', str(uuid.UUID(int=0))), ('Invalid GUID', 'InstallationId', 'invalid'), ('Empty token', 'Token', '')]:
    config = copy.deepcopy(valid)
    config['Agent'][field] = value
    cases.append((name, config, False))
for index, (label, config, expected) in enumerate(cases):
    path = staging / f'{index}.json'
    path.write_text(json.dumps(config), encoding='utf-8')
    result = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', str(script), '-ConfigurationFile', str(path), '-ValidateOnly'], capture_output=True, text=True, timeout=15)
    assert (result.returncode == 0) == expected, (label, result.stderr)
    print('PASS', label)
print('Installer validation passed; no services installed or stopped.')

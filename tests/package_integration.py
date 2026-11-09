"""Verify the actual unified nupkg in an isolated consumer; no Windows service installation."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
props = ET.parse(ROOT / "build/NuGetPackage.props")
version = props.findtext("./PropertyGroup/Version")
package = ROOT / "artifacts/nuget-unified" / f"SinergiaAI.PrintAgent.{version}.nupkg"

with zipfile.ZipFile(package) as archive:
    files = set(archive.namelist())
    for component in ("Server", "Client", "Contracts"):
        assert f"lib/net10.0/SinergiaAI.PrintAgent.{component}.dll" in files, component
    assert not any(name.startswith("lib/") and "/SinergiaPrint." in name for name in files)
    assert "README.md" in files and "LICENSE" in files
    assert "sinergia.png" in files and archive.read("sinergia.png").startswith(b'\x89PNG\r\n\x1a\n')
    manifest = ET.fromstring(archive.read("SinergiaAI.PrintAgent.nuspec"))
    ns = {"n": "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"}
    assert manifest.findtext("n:metadata/n:id", namespaces=ns) == "SinergiaAI.PrintAgent"
    assert manifest.findtext("n:metadata/n:license", namespaces=ns) == "MIT"
    assert manifest.findtext("n:metadata/n:icon", namespaces=ns) == "sinergia.png"
    assert manifest.findtext("n:metadata/n:projectUrl", namespaces=ns) == "https://www.sinergiaai.com/printagent"
    assert manifest.find("n:metadata/n:repository", ns).get("url") == "https://github.com/MohammadMassoud/PrintAgent.git"
    for dependency in manifest.findall(".//n:dependency", ns):
        assert not dependency.get("id", "").startswith(("SinergiaPrint", "SinergiaAI.PrintAgent")), dependency.attrib
    assert not any(name.endswith((".db", ".ps1")) or "appsettings" in name.lower() for name in files)
print("PASS package contents: three DLLs, README, MIT, repository, no old-package dependencies.", flush=True)

workspace = Path(tempfile.mkdtemp(prefix="package-test-", dir=ROOT / "artifacts"))
consumer = workspace / "Consumer"
shutil.copytree(ROOT / "tests/PackageConsumer", consumer, ignore=shutil.ignore_patterns("bin", "obj"))
environment = os.environ.copy()
environment["PRINTAGENT_PACKAGE_TEST_DATA"] = str(workspace / "state")
# Keep this package's restore isolated from any earlier local build of the same version.
environment["NUGET_PACKAGES"] = str(workspace / "packages")
cache = Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget/packages"))
nuget_config = ET.Element("configuration")
sources = ET.SubElement(nuget_config, "packageSources")
ET.SubElement(sources, "clear")
for name, value in (("package-under-test", str(package.parent)), ("cached-dependencies", str(cache)),
                    ("nuget.org", "https://api.nuget.org/v3/index.json")):
    ET.SubElement(sources, "add", key=name, value=value)
config_path = workspace / "NuGet.Config"
ET.ElementTree(nuget_config).write(config_path, encoding="utf-8", xml_declaration=True)
subprocess.run([
    "dotnet", "restore", str(consumer / "PackageConsumer.csproj"),
    f"-p:PrintAgentPackageVersion={version}",
    "--configfile", str(config_path), "--nologo",
], check=True, env=environment)
subprocess.run([
    "dotnet", "run", "--project", str(consumer / "PackageConsumer.csproj"),
    "--configuration", "Release", "--no-restore",
    f"-p:PrintAgentPackageVersion={version}",
], check=True, env=environment)
print(f"PASS isolated consumer. Evidence: {workspace}", flush=True)

"""Breaks the code one way at a time, in a throwaway copy of the project, runs the tests that should
notice, and says whether they did. A change the tests don't notice is a test that's missing.

    python tests/mutate.py <mutations file> [names of the ones to run]

The mutations file is Python that sets MUTATIONS to a list of
(name, file in the project, exact text to replace, what to put instead, test filter), for example:

    MUTATIONS = [
        ("next-picture-late", "src/Flyweight/Core/TrimSelection.cs",
         "seconds + 0.5 / picturesASecond", "seconds + 1.5 / picturesASecond",
         "FullyQualifiedName~TrimSelectionTests"),
    ]

Keep the file outside the repo; it's about one change. The copy is made in TestResults/mutate, and
its tests run on the hidden desktop through tests/run-tests.ps1."""
import os, subprocess, sys, re

TESTS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TESTS)
MUT = os.path.join(ROOT, "TestResults", "mutate")
LOGS = os.path.join(ROOT, "TestResults", "logs")

def sync():
    for part in ("src", "tests"):
        subprocess.run(["robocopy", os.path.join(ROOT, part), os.path.join(MUT, part), "/MIR", "/XD", "bin", "obj", "TestResults", "/NFL", "/NDL", "/NJH", "/NJS", "/NP"], stdout=subprocess.DEVNULL)

def run(name, filt):
    log = "mut-" + name
    runner = subprocess.run(["pwsh", "-NoProfile", "-File", os.path.join(TESTS, "run-tests.ps1"), "-Folder", MUT, "-Log", log, "-Filter", filt], capture_output=True, text=True, errors="replace")
    text = open(os.path.join(LOGS, log + ".log"), encoding="utf-8", errors="replace").read()
    if "error CS" in text or "error MC" in text:
        return "DID NOT BUILD"
    failed = re.findall(r"^\s+Failed (\S+)", text, re.M)
    if failed:
        return "failed: " + ", ".join(sorted(set(f.split(".")[-1][:60] for f in failed)))
    if runner.returncode == 0:
        passed = re.search(r"Passed:\s+(\d+)", text)
        return "ALL PASSED (%s), so no test noticed" % (passed.group(1) if passed else "?")
    # Nothing failed, yet the runner didn't call it a pass: stopped at its time limit, skipped tests,
    # or nothing matched the filter. Its last line says which.
    said = [line for line in runner.stdout.splitlines() if line.strip()]
    return "DID NOT RUN PROPERLY: " + (said[-1] if said else "exit code %d" % runner.returncode)

if len(sys.argv) < 2:
    sys.exit(__doc__)
MUTATIONS = None
exec(open(sys.argv[1], encoding="utf-8").read())

sync()
only = sys.argv[2:]
for name, path, old, new, filt in MUTATIONS:
    if only and name not in only:
        continue
    full = os.path.join(MUT, path)
    text = open(full, encoding="utf-8").read()
    norm = text.replace("\r\n", "\n")
    if norm.count(old) != 1:
        print(name, ": could not place the change (%d matches)" % norm.count(old))
        continue
    open(full, "w", encoding="utf-8", newline="").write(norm.replace(old, new))
    try:
        print(name, "->", run(name, filt), flush=True)
    finally:
        open(full, "w", encoding="utf-8", newline="").write(text)

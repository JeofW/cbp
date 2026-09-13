"""Build actual pinned Detour sources in an isolated directory and record evidence."""
from __future__ import annotations
import argparse
import hashlib
import json
import pathlib
import re
import shutil
import subprocess

PINNED = {
    "Detour/Include/DetourNode.h": "f34bf8bc7e88212c5007029f3179e57948bfbb74",
    "Detour/Source/DetourNode.cpp": "48abbba6b5b256e0b03c97c35cee780b379e8c8a",
}

def blob(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=pathlib.Path, help="Directory containing Navigation/ or Detour/")
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--sanitizers", action="store_true")
    parser.add_argument("--allow-modified", action="store_true", help="Label candidate input; record exact source hashes")
    args = parser.parse_args()
    source = args.source.resolve()
    if (source / "Navigation").is_dir(): source /= "Navigation"
    output = args.output.resolve()
    if output.exists(): raise ValueError("Output directory already exists; preserve previous evidence")
    if output == source or source in output.parents: raise ValueError("Output must be outside the native input tree")
    output.mkdir(parents=True)
    copied = output / "source"
    shutil.copytree(source / "Detour", copied / "Detour")
    provenance = {"source_modified": args.allow_modified, "game_attached": False, "inputs": {}, "instrumented_heap_expressions": 0}
    for relative, expected in PINNED.items():
        data = (source / relative).read_bytes()
        actual = blob(data)
        if not args.allow_modified and actual != expected:
            raise ValueError(f"Pinned source mismatch: {relative}: {actual} != {expected}")
        provenance["inputs"][relative] = {"blob": actual, "sha256": hashlib.sha256(data).hexdigest()}
    if not args.sanitizers:
        header = copied / "Detour/Include/DetourNode.h"
        # Instrument the original lvalue access, leaving algorithm and comparisons unchanged.
        text = header.read_text()
        helper = "\n#include <cstdint>\nstruct dtNode;\nextern std::uint64_t AuditHeapAccesses;\ninline dtNode*& AuditHeapElement(dtNode** heap, int index) { ++AuditHeapAccesses; return heap[index]; }\n"
        text = text.replace('#include "DetourNavMesh.h"', '#include "DetourNavMesh.h"' + helper, 1)
        header.write_text(text)
        for path in (header, copied / "Detour/Source/DetourNode.cpp"):
            text, count = re.subn(r"m_heap\[([^\[\]]+)\]", r"AuditHeapElement(m_heap, \1)", path.read_text())
            provenance["instrumented_heap_expressions"] += count
            path.write_text(text)
        if provenance["instrumented_heap_expressions"] < 8: raise ValueError("Instrumentation did not cover the queue")
    compiler = shutil.which("clang++" if args.sanitizers else "g++")
    if compiler is None: raise RuntimeError("C++ compiler not installed")
    subprocess.run([compiler, "--version"], stdout=(output / "compiler.txt").open("w"), check=True)
    command = [compiler, "-std=c++17", "-DDT_POLYREF64", "-g", "-Wall", "-Wextra", "-Werror=return-type",
               "-I" + str(copied / "Detour/Include")]
    command += ["-O1", "-fsanitize=address,undefined", "-fno-omit-frame-pointer"] if args.sanitizers else ["-O2", "-DAUDIT_HEAP_ACCESS"]
    command += [str(pathlib.Path(__file__).with_name("QueueRegression.cpp"))]
    command += [str(copied / "Detour/Source" / name) for name in ("DetourNode.cpp", "DetourAlloc.cpp", "DetourAssert.cpp")]
    command += ["-o", str(output / "queue-tests")]
    provenance["build_command"] = command
    (output / "provenance.json").write_text(json.dumps(provenance, indent=2))
    with (output / "build.txt").open("w") as log:
        build = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
    if build.returncode != 0: raise RuntimeError("Actual-source build failed; inspect build.txt")
    result = subprocess.run([str(output / "queue-tests")], capture_output=True, text=True, timeout=60)
    (output / "run.txt").write_text(result.stdout + result.stderr)
    (output / "result.json").write_text(json.dumps({"build_exit": 0, "run_exit": result.returncode,
        "sanitizers": args.sanitizers, "game_attached": False}, indent=2))
    print(result.stdout, end=""); print(result.stderr, end="")
    return result.returncode

if __name__ == "__main__":
    raise SystemExit(main())

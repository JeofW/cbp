"""Create two hash-locked Git objects and an UNPUBLISHED candidate; never update refs.

This temporary job uses its own short-lived repository token in memory only.
No token or credential is printed, saved, exported, or returned in an artifact.
Publishing remains a separate authenticated native connector ref operation.
"""
import difflib
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.request

REPOSITORY = "JeofW/CopilotBuddy-private"
BRANCH = "refs/heads/audit/next-42-ready-owner-20260914"
CLEANUP = [".github/workflows/audit-scan-exception-candidate.yml",
           "docs/audit/2026-09-14/preparation/prepare_scan_candidate.py"]
PATCHES = [{'path': 'runtime-snapshot/Bots/WholesomeAutoQuest-master/QuestScheduler.cs', 'before_sha256': 'a09876241e47a1a6b2ca006636d97d3f6fffbe29bacf3a13405c3549bc10d6c0', 'after_sha256': 'a886dfb7dbe6eefe6cd08400e1b5ade2c019348e066f11b8d5e399919cb1db26', 'before_blob': '26b8eb24ceab258b1dddcc303729f846d89cd7d7', 'after_blob': 'c99fab132e5ba3ba52b117a8ff48b6c249421292', 'old': '            QuestRecoveryRuntime.EnsureConfigured(\n                _dataLoader.DatasetFingerprint,\n                NavigationProviderFingerprint());', 'new': '            // A refresh is not permission to keep executing the previous plan while\n            // fresh identity/log/context reads can fail. Revoke before those reads;\n            // do not revoke again in a late catch that may belong to an older refresh.\n            InvalidatePublishedWork("Refreshing quest observations; prior work is not authorized.");\n            QuestRecoveryRuntime.EnsureConfigured(\n                _dataLoader.DatasetFingerprint,\n                NavigationProviderFingerprint());'}, {'path': 'runtime-snapshot/Bots/WholesomeAutoQuest-master/WholesomeAutoQuest.cs', 'before_sha256': '2482fc023412c92667e89df836e29dd3278b2b2f6a3ca9287b418150db621b98', 'after_sha256': '3f50c19cbb07eaa4465b5bcec7aedb58bac64ee83daf3fb07e981344b9d8bc07', 'before_blob': '879d2237ff4d482fbe870826d9e3c11f1e65943c', 'after_blob': '7e6165d1b7d6845c2f68067ed57c03e4b89d8863', 'old': '            catch (Exception ex)\n            {\n                Log($"Scan error: {ex.Message}");\n                return false;\n            }\n        }\n\n        internal static bool RunLeaseFencedRefresh(', 'new': '            catch (Exception ex) when (ex is not ThreadInterruptedException && ex is not OperationCanceledException)\n            {\n                Log($"Scan error: {ex.Message}");\n                return false;\n            }\n        }\n\n        internal static bool RunLeaseFencedRefresh('}]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def blob_id(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def patch_source(item, before):
    if digest(before) != item["before_sha256"] or blob_id(before) != item["before_blob"]:
        raise RuntimeError("Pinned source changed: " + item["path"])
    text = before.decode("utf-8")
    if text.count(item["old"]) != 1:
        raise RuntimeError("Ambiguous patch anchor: " + item["path"])
    after = text.replace(item["old"], item["new"], 1).encode("utf-8")
    if digest(after) != item["after_sha256"] or blob_id(after) != item["after_blob"]:
        raise RuntimeError("Patched source identity mismatch: " + item["path"])
    return after


def git(*arguments):
    return subprocess.check_output(["git", *arguments])


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, new_url):
        return None


def post(endpoint, data):
    if endpoint not in {"git/blobs", "git/trees", "git/commits"}:
        raise RuntimeError("Only immutable Git object creation is allowed")
    request = urllib.request.Request(
        "https://api.github.com/repos/" + REPOSITORY + "/" + endpoint,
        data=json.dumps(data).encode(), method="POST",
        headers={"Authorization": "Bearer " + os.environ["GITHUB_TOKEN"],
                 "Accept": "application/vnd.github+json", "Content-Type": "application/json",
                 "X-GitHub-Api-Version": "2022-11-28"})
    try:
        with urllib.request.build_opener(NoRedirect()).open(request, timeout=45) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError("Git object API failed: " + endpoint + "; status=" + str(error.code)) from None


def main():
    if (os.environ.get("GITHUB_ACTIONS") != "true" or
        os.environ.get("GITHUB_REPOSITORY") != REPOSITORY or
        os.environ.get("GITHUB_REF") != BRANCH or
        os.environ.get("GITHUB_EVENT_NAME") != "push"):
        raise RuntimeError("This candidate is restricted to the authorized private audit branch push")
    parent = os.environ["GITHUB_SHA"]
    if not re.fullmatch(r"[0-9a-f]{40}", parent) or git("rev-parse", "HEAD").decode().strip() != parent:
        raise RuntimeError("Checkout identity mismatch")
    fixture = "Tools/WholesomeQuestRecoveryRegressionTests/QuestScanFailureRegressionTests.cs"
    if git("rev-parse", parent + ":" + fixture).decode().strip() != "519ad9bf7bf8d7e22d849178c8314adeee036f0a":
        raise RuntimeError("Clean baseline fixture changed")
    prepared = []
    for item in PATCHES:
        before = git("show", parent + ":" + item["path"])
        after = patch_source(item, before)
        prepared.append((item, before, after))
    for path in CLEANUP:
        git("cat-file", "-e", parent + ":" + path)
    out = Path(os.environ["RUNNER_TEMP"]) / "scan-exception-candidate"
    out.mkdir()
    entries = []
    manifest = []
    patch = []
    for item, before, after in prepared:
        result = post("git/blobs", {"content": after.decode("utf-8"), "encoding": "utf-8"})
        if result["sha"] != item["after_blob"]:
            raise RuntimeError("Remote blob differs from validated source")
        entries.append({"path": item["path"], "mode": "100644", "type": "blob", "sha": result["sha"]})
        manifest.append({key: value for key, value in item.items() if key not in {"old", "new"}})
        patch.extend(difflib.unified_diff(before.decode().splitlines(keepends=True), after.decode().splitlines(keepends=True),
                                         fromfile="a/"+item["path"], tofile="b/"+item["path"]))
        (out / Path(item["path"]).name).write_bytes(after)
    entries += [{"path": path, "mode": "100644", "type": "blob", "sha": None} for path in CLEANUP]
    base_tree = git("rev-parse", parent + "^{tree}").decode().strip()
    tree = post("git/trees", {"base_tree": base_tree, "tree": entries})["sha"]
    commit = post("git/commits", {"message": "fix(w42): revoke prior scan execution before fresh observations and propagate cancellation",
                                  "tree": tree, "parents": [parent]})["sha"]
    report = {"repository": REPOSITORY, "parent": parent, "base_tree": base_tree,
              "candidate_commit": commit, "candidate_tree": tree, "files": manifest,
              "cleanup": CLEANUP, "refs_updated": False, "tests_run": False,
              "credentials_exported": False,
              "scope": "Immutable two-file candidate only; native ref publication and same-commit Windows validation remain required"}
    (out/"candidate.json").write_text(json.dumps(report, indent=2)+"\n")
    (out/"repair.patch").write_text("".join(patch))
    (out/"files-sha256.json").write_text(json.dumps([{"path": path.name, "sha256": digest(path.read_bytes())}
                                                   for path in sorted(out.iterdir()) if path.is_file()], indent=2)+"\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()

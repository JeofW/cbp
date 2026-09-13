"""Read-only reconciliation of all PRs against an explicitly pinned integration tree.

Exports private review metadata and Git facts; never merges, changes refs, downloads
LFS contents, or treats an ancestor as proof that every later behavior is correct.
"""
from __future__ import annotations
import csv, json, os, subprocess, urllib.request
from pathlib import Path

REPO = 'JeofW/CopilotBuddy-private'
TARGET = 'e674798b87ca792b5faab72dc324ea531e278153'
OUT = Path(os.environ['AUDIT_OUT'])
OUT.mkdir(parents=True, exist_ok=True)

def git(*args: str) -> str:
    return subprocess.check_output(['git', *args], text=True).strip()

def ancestor(base: str, head: str) -> bool:
    p = subprocess.run(['git', 'merge-base', '--is-ancestor', base, head], capture_output=True)
    if p.returncode not in (0, 1):
        raise RuntimeError(p.stderr.decode())
    return p.returncode == 0

def get(path: str):
    request = urllib.request.Request('https://api.github.com/repos/' + REPO + path,
        headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
                 'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28'})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)

pulls = []
page = 1
while True:
    batch = get(f'/pulls?state=all&sort=created&direction=asc&per_page=100&page={page}')
    pulls.extend(batch)
    if len(batch) < 100:
        break
    page += 1
master = get('/git/ref/heads/master')['object']['sha']
records = []
for pr in pulls:
    head = pr['head']['sha']; base = pr['base']['sha']
    # GitHub's non-null merge_commit_sha can describe a test merge, not an actual merge.
    # merged_at/state and ancestry are recorded separately.
    head_ancestor = ancestor(head, TARGET)
    diff = git('diff', '--numstat', master, TARGET).splitlines() if not records else None
    files = git('diff', '--name-only', git('merge-base', base, head), head).splitlines()
    identical, different, missing = [], [], []
    for name in files:
        old = subprocess.run(['git', 'rev-parse', head + ':' + name], text=True, capture_output=True)
        new = subprocess.run(['git', 'rev-parse', TARGET + ':' + name], text=True, capture_output=True)
        if old.returncode != 0:
            if new.returncode != 0: identical.append(name)
            else: different.append(name)
        elif new.returncode != 0: missing.append(name)
        elif old.stdout.strip() == new.stdout.strip(): identical.append(name)
        else: different.append(name)
    records.append({'number':pr['number'],'title':pr['title'],'url':pr['html_url'],
        'state':pr['state'],'draft':pr['draft'],'merged_at':pr['merged_at'],
        'base_ref':pr['base']['ref'],'base_sha':base,'head_ref':pr['head']['ref'],'head_sha':head,
        'head_is_ancestor_of_checkpoint':head_ancestor,'head_is_ancestor_of_master':ancestor(head,master),
        'same_final_file_bytes':identical,'later_or_different_file_bytes':different,'head_files_absent':missing,
        'body':pr.get('body')})
summary = {'repository':REPO,'checkpoint':TARGET,'checkpoint_tree':git('rev-parse',TARGET+'^{tree}'),
    'master':master,'count':len(records),'prs':records,
    'checkpoint_numstat':git('diff','--numstat',master,TARGET).splitlines(),
    'heads':git('show-ref','--heads').splitlines()}
(OUT/'pr-reconciliation.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
with (OUT/'pr-reconciliation.csv').open('w',newline='',encoding='utf-8') as file:
    fields=['number','title','state','draft','merged_at','base_ref','head_ref','head_sha',
            'head_is_ancestor_of_checkpoint','head_is_ancestor_of_master']
    writer=csv.DictWriter(file,fieldnames=fields,extrasaction='ignore');writer.writeheader();writer.writerows(records)
subprocess.run(['git','bundle','create',str(OUT/'source-history.bundle'),'--all'],check=True)
print(json.dumps({'prs':len(records),'merged':sum(p['merged_at'] is not None for p in records),
    'ancestor_of_checkpoint':sum(p['head_is_ancestor_of_checkpoint'] for p in records),
    'master':master,'checkpoint':TARGET}))

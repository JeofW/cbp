"""Run the real native mesh probe in its required DLL-relative asset layout.

The engine's recorded bytes and Git-LFS mesh objects are explicit inputs. This
does not establish client collision, physical movement or live quest acceptance.
"""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import gzip
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

HASH = re.compile(r'[0-9a-f]{64}\Z')
MESH = re.compile(r'mmaps/\d{3}(?:\d{4})?\.(?:mmap|mmtile)\Z')


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def materialize_meshes(repo: Path, destination: Path, manifest: dict) -> None:
    """Copy verified bytes only; never link tests to mutable source assets."""
    repo = repo.resolve()
    if destination.exists():
        raise FileExistsError('Preserve the existing native asset layout')
    selected = []
    for name, expected in manifest.items():
        if not isinstance(name, str) or not MESH.fullmatch(name) or not isinstance(expected, dict):
            raise ValueError('Only exact map/tile paths are accepted')
        value = expected.get('sha256'); size = expected.get('bytes')
        if not isinstance(value, str) or not HASH.fullmatch(value) or type(size) is not int or size <= 0:
            raise ValueError('Each mesh needs its positive byte length and SHA-256')
        path = repo / name
        if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(repo):
            raise ValueError('Mesh source is missing or outside the chosen checkout')
        if path.stat().st_size != size or digest(path) != value:
            raise ValueError('Mesh bytes differ from the source receipt: ' + name)
        if path.read_bytes()[:43].startswith(b'version https://git-lfs.github.com/spec/v1'):
            raise ValueError('Git-LFS pointer text is not a hydrated navigation tile')
        selected.append((path, expected))
    if not selected:
        raise ValueError('No source-bound meshes were selected')
    destination.mkdir(parents=True, exist_ok=False)
    for source, expected in selected:
        target = destination / source.name
        with source.open('rb') as reader, target.open('xb') as writer:
            shutil.copyfileobj(reader, writer)
        if target.stat().st_size != expected['bytes'] or digest(target) != expected['sha256']:
            raise ValueError('Copied native asset failed its identity check: ' + target.name)


def build_quest_route_cases(family: dict, dataset: dict) -> list[dict]:
    """Derive candidate chains from exact source rows, without route-success claims."""
    ids=family.get('candidate_replay_ids')
    if not isinstance(ids,list) or not ids or any(type(value) is not int or value<=0 for value in ids) or len(set(ids))!=len(ids):
        raise ValueError('A unique explicit quest population is required')
    rows=family.get('quests',[])
    if not isinstance(rows,list) or len({row['quest_id'] for row in rows})!=len(rows):
        raise ValueError('Ambiguous source quest membership')
    indexed={row['quest_id']:row for row in rows}; result=[]
    def point(row,primary):
        names=('position_x','position_y','position_z') if primary else ('X','Y','Z')
        value=[row.get(name) for name in names]
        if any(type(v) not in (int,float) or not math.isfinite(v) for v in value) or value==[0,0,0]:
            raise ValueError('A source coordinate is missing or non-finite')
        return value
    for ident in sorted(ids):
        row=indexed.get(ident)
        if not row or row.get('replay_candidate') is not True or row.get('additional_source_obligations'):
            raise ValueError('Unresolved source/script obligations cannot borrow a route case')
        objectives=sorted(row.get('normal_objectives',[]),key=lambda value:value['slot'])
        if not objectives or any(type(value.get('typed_entry')) is not int or value['typed_entry']>=0
                                 or type(value.get('count')) is not int or value['count']<=0 for value in objectives):
            raise ValueError('Direct-object routes need exact object and count semantics')
        if len({value['slot'] for value in objectives})!=len(objectives):
            raise ValueError('Duplicate objective slots')
        candidates=[[spawn for spawn in row.get('primary_spawns',[]) if spawn.get('id')==-value['typed_entry']]
                    for value in objectives]
        maps=set.intersection(*({spawn['map'] for spawn in group} for group in candidates))
        if not maps or any(type(value) is not int or value<0 or value>999 for value in maps):
            raise ValueError('One continuous source-map chain is required')
        map_id=min(maps)
        selected=[next(spawn for spawn in group if spawn['map']==map_id) for group in candidates]
        points=[point(spawn,True) for spawn in selected]
        def npc(relations,ender=False):
            choices=[(relation['id'],spawn) for relation in relations
                     if not ender or relation.get('type')=='Creature'
                     for spawn in dataset.get('CreatureSpawns',{}).get(str(relation['id']),[]) if spawn.get('Map')==map_id]
            if not choices:raise ValueError('Missing same-map source-related NPC coordinates')
            entry,spawn=choices[0]
            return entry,point(spawn,False)
        giver,giver_point=npc(row.get('primary_starters',[]))
        ender,ender_point=npc(row.get('primary_enders',[]),True)
        origin=[giver_point[0]-10,giver_point[1],giver_point[2]]
        destinations=[('giver',giver,giver_point,'shipped-npc-coordinate')]
        destinations += [('objective',-definition['typed_entry'],position,'primary-object-coordinate')
                         for definition,position in zip(objectives,points)]
        destinations += [('ender',ender,ender_point,'shipped-npc-coordinate')]
        prior_role,prior_entry,prior,authority='origin',None,origin,'synthetic-near-giver-control'
        for index,(role,entry,position,next_authority) in enumerate(destinations):
            result.append({'case_id':f'{ident}:{index}:{prior_role}-to-{role}','quest_id':ident,'map_id':map_id,
                'leg':prior_role+'-to-'+role,'start':list(prior),'end':list(position),
                'start_entry':prior_entry,'end_entry':entry,'start_authority':authority,'end_authority':next_authority})
            prior_role,prior_entry,prior,authority=role,entry,position,next_authority
    return result


def validate_quest_route_cases(repo: Path, document: dict) -> list[dict]:
    repo=repo.resolve(); source={}
    for kind in ('family','dataset'):
        name=document.get(kind+'_file'); expected=document.get(kind+'_sha256')
        if not isinstance(name,str) or not name or Path(name).is_absolute() or '..' in Path(name).parts:
            raise ValueError('Case source paths must remain inside the selected checkout')
        path=repo/name
        if path.is_symlink() or not path.resolve().is_relative_to(repo) or not path.is_file():
            raise ValueError('Case source is missing or outside the selected checkout')
        if not isinstance(expected,str) or not HASH.fullmatch(expected) or digest(path)!=expected:
            raise ValueError('Case source bytes do not match the declared identity')
        raw=path.read_bytes()
        source[kind]=json.loads(gzip.decompress(raw) if path.suffix=='.gz' else raw)
    expected=build_quest_route_cases(source['family'],source['dataset'])
    if document.get('cases')!=expected:
        raise ValueError('Route membership or endpoints differ from the bound source rows')
    return expected


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--native', type=Path, required=True)
    parser.add_argument('--native-sha256', required=True)
    parser.add_argument('--runtime', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--maps', nargs='+', type=int, default=[1, 530])
    parser.add_argument('--quest-routes', type=Path)
    args = parser.parse_args()
    repo = args.repo.resolve(); output = args.output.resolve(); native = args.native.resolve()
    if output.exists():
        raise FileExistsError('Read the existing run; no evidence is overwritten')
    if not HASH.fullmatch(args.native_sha256) or digest(native) != args.native_sha256:
        raise ValueError('The selected native binary differs from its explicit identity')
    if not args.maps or len(set(args.maps)) != len(args.maps) or any(value < 0 or value > 999 for value in args.maps):
        raise ValueError('Select unique original map IDs representable by the mesh filenames')
    sys.path.insert(0, str(repo / 'Tools/EvidenceAudit'))
    from release_evidence_335 import source_identity, source_inputs
    identity = source_identity(repo, True); before = source_inputs(repo)
    route_bytes=args.quest_routes.read_bytes() if args.quest_routes is not None else None
    route_cases=validate_quest_route_cases(repo,json.loads(route_bytes)) if route_bytes is not None else []
    if any(row['map_id'] not in args.maps for row in route_cases):
        raise ValueError('Every source route map must have verified hydrated meshes')
    include = ','.join('mmaps/' + str(value).zfill(3) + '*' for value in args.maps)
    raw = subprocess.check_output(['git','lfs','ls-files','--long','--include=' + include], cwd=repo).decode('utf-8')
    meshes = {}
    for line in raw.splitlines():
        match = re.fullmatch(r'([0-9a-f]{64}) ([*-]) (mmaps/\S+)', line)
        if match is None or match[2] != '*':
            raise ValueError('A selected LFS object is not materialized: ' + line)
        path = repo / match[3]
        if digest(path) != match[1] or before.get(match[3]) != match[1]:
            raise ValueError('Mesh disagrees with its versioned/source input identity')
        meshes[match[3]] = {'sha256':match[1], 'bytes':path.stat().st_size}
    if any('mmaps/' + str(value).zfill(3) + '.mmap' not in meshes for value in args.maps):
        raise ValueError('Selected map metadata is missing')
    output.mkdir(parents=True, exist_ok=False)
    def write(name, value):
        with (output/name).open('x', encoding='utf-8', newline='\n') as stream:
            json.dump(value, stream, indent=2); stream.write('\n')
    write('source-identity.json',identity);write('source-before.json',before)
    write('mesh-identity.json',meshes)
    if route_bytes is not None:
        with (output/'quest-route-input.json').open('xb') as stream:stream.write(route_bytes)
    runner = output/'runner'; result=[]
    commands=[('build',['dotnet','build','Tools/NativeMeshAudit/NativeMeshAudit.csproj','-c','Release',
                         '-p:Platform=x86','-p:PlatformTarget=x86','-o',str(runner),'-v','quiet'])]
    for stage, command in commands:
        with (output/(stage+'.stdout.log')).open('xb') as stdout,(output/(stage+'.stderr.log')).open('xb') as stderr:
            run=subprocess.run(command,cwd=repo,stdout=stdout,stderr=stderr,timeout=600,
                               creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
        row={'stage':stage,'exit':run.returncode,'command':command,
             'stdout_sha256':digest(output/(stage+'.stdout.log')),'stderr_sha256':digest(output/(stage+'.stderr.log'))}
        result.append(row);print(json.dumps(row),flush=True)
        if stage=='build' and run.returncode==0:
            shutil.copy2(runner/'Navigation.dll',output/'tracked-build-Navigation.dll')
            shutil.copy2(native,runner/'Navigation.dll')
            if digest(runner/'Navigation.dll') != args.native_sha256:
                raise ValueError('Selected native engine copy changed')
            materialize_meshes(repo,runner/'mmaps',meshes)
            print(json.dumps({'verified_deployment_layout_meshes':len(meshes),'native':args.native_sha256}),flush=True)
            arguments=[str(args.runtime.resolve()),str(runner/'NativeMeshAudit.dll'),str(repo),str(output/'replay')]
            if route_cases:arguments.append(str(output/'quest-route-input.json'))
            commands.append(('native-run',arguments))
    after=source_inputs(repo);write('source-after.json',after)
    if after!=before or source_identity(repo,True)!=identity or digest(native)!=args.native_sha256:
        raise RuntimeError('Source/engine bytes changed during the native probe')
    success=len(result)==2 and all(row['exit']==0 for row in result)
    summary={'identity':identity,'source_stable':True,'results':result,'native_sha256':args.native_sha256,
             'verified_mesh_files':len(meshes),'asset_layout':'mmaps beside the loaded native DLL',
             'quest_route_case_count':len(route_cases),'quest_route_exact_ids':sorted({row['quest_id'] for row in route_cases}),
             'quest_route_input_sha256':hashlib.sha256(route_bytes).hexdigest() if route_bytes is not None else None,
             'native_queries_completed':success,'physical_collision_proven':False,'live_completion_proven':False,
             'native_source_revision_inferred':False,'completed_utc':datetime.now(timezone.utc).isoformat()}
    write('summary.json',summary);print(json.dumps(summary,indent=2))
    raise SystemExit(0 if success else 1)


if __name__=='__main__':
    main()

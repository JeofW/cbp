"""Produce exact primary-source obligations for every remaining quest.

This is a read-only evidence index, not a strategy generator. It correlates
actors, relations, credit aliases, scripts, conditions and source-item spells
without promoting those joins to executable gameplay or realm equivalence.
"""
from pathlib import Path
from collections import defaultdict, Counter
import argparse
import gzip
import hashlib
import json
import re
import subprocess
from quest_repair_pack_335 import load_verified_tables, digest
from primary_closure_335 import classification_partition


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ['ledger','reference','profiles','output']:
        parser.add_argument('--'+name,type=Path,required=True)
    args=parser.parse_args()
    if args.output.exists():raise FileExistsError('Correlation evidence must be new')
    rows=[json.loads(line) for line in gzip.decompress(args.ledger.read_bytes()).splitlines()]
    classification_partition(rows, [row['quest_id'] for row in rows])
    tables,receipts=load_verified_tables(args.reference)
    qt={r['ID']:r for r in tables['quest_template']};qa={r['ID']:r for r in tables['quest_template_addon']}
    actors={kind:{r['entry']:r for r in tables[table]} for kind,table in [('Creature','creature_template'),('GameObject','gameobject_template')]}
    items={r['entry']:r for r in tables['item_template']}
    aliases=defaultdict(list);scripts=defaultdict(list);spell_scripts=defaultdict(list)
    credit_scripts=defaultdict(list);linked=defaultdict(list);timed_callers=defaultdict(list)
    header=(args.reference/'contracts/SmartScriptMgr.h').read_text(encoding='utf-8')
    codes={m[1]:int(m[2],0) for m in re.finditer(r'\b(SMART_[A-Z0-9_]+)\s*=\s*(0x[0-9a-fA-F]+|\d+)\s*,',header)}
    for r in tables['creature_template']:
        for key in ['KillCredit1','KillCredit2']:
            if r.get(key):aliases[r[key]].append({'entry':r['entry'],'field':key,'name':r['name'],'source_line':r['__source_line'],'row_sha256':digest(r)})
    for r in tables['smart_scripts']:
        scripts[(r['source_type'],r['entryorguid'])].append(r)
        if r['link']:linked[(r['source_type'],r['entryorguid'],r['link'])].append(r)
        if r['action_type']==codes['SMART_ACTION_CALL_KILLEDMONSTER']:credit_scripts[r['action_param1']].append(r)
        if r['action_type']==codes['SMART_ACTION_CALL_TIMED_ACTIONLIST']:timed_callers[r['action_param1']].append(r)
    for r in tables.get('spell_script_names',[]):spell_scripts[r['spell_id']].append(r)
    profile_index={r['quest_id']:r for r in json.loads(args.profiles.read_text(encoding='utf-8'))}
    conditions=defaultdict(list)
    for r in tables['conditions']:conditions[r['SourceEntry']].append(r)
    condition_header=(args.reference/'contracts/ConditionMgr.h').read_text(encoding='utf-8')
    condition_codes={int(m[2],0):m[1] for m in re.finditer(r'\b(CONDITION_SOURCE_TYPE_[A-Z0-9_]+)\s*=\s*(0x[0-9a-fA-F]+|\d+)\s*,',condition_header)}
    cpp_root=args.reference/'tc-source';cpp_files=[]
    for path in (cpp_root/'src/server/scripts').rglob('*.cpp'):
        raw=path.read_bytes();cpp_files.append((path.relative_to(cpp_root).as_posix(),raw.decode('utf-8-sig',errors='replace'),hashlib.sha256(raw).hexdigest()))
    cpp_cache={}
    def cpp(name):
        if not name:return []
        if name not in cpp_cache:
            pattern=re.compile(r'(?<![A-Za-z0-9_])'+re.escape(name)+r'(?![A-Za-z0-9_])')
            cpp_cache[name]=[{'path':path,'sha256':sha,'matching_lines':[i for i,line in enumerate(text.splitlines(),1) if pattern.search(line)]}
                for path,text,sha in cpp_files if pattern.search(text)]
        return cpp_cache[name]
    event_names={v:k for k,v in codes.items() if k.startswith('SMART_EVENT_') and '_FLAG_' not in k and not k.endswith('_END')}
    action_names={v:k for k,v in codes.items() if k.startswith('SMART_ACTION_') and not k.endswith('_END')}
    def script(row):
        return {'row':row,'row_sha256':digest(row),'event':event_names.get(row['event_type'],'UNKNOWN'),
            'action':action_names.get(row['action_type'],'UNKNOWN'),
            'linked_parents':[{'source_line':p['__source_line'],'row_sha256':digest(p),'row':p} for p in linked[(row['source_type'],row['entryorguid'],row['id'])]],
            'timed_action_callers':[{'source_line':p['__source_line'],'row_sha256':digest(p),'row':p} for p in timed_callers[row['entryorguid']]] if row['source_type']==9 else []}
    def evidence_kind(obligation):
        if obligation.startswith('source:primary-field-conflict'):
            return 'Resolve the exact primary-versus-dataset field conflict against the configured realm/export. No unreviewed overwrite or guessed prerequisite/flag semantics.'
        if obligation.startswith('source:server-condition'):
            return 'A source-bound condition model and its live player/world inputs are required; direct availability and unrelated quest-reference conditions must remain distinct.'
        if obligation.startswith('data:'):
            return 'The named acquisition, objective or relation/geometry contract remains unrepresented by a proved supported route. A source join alone cannot supply missing travel, item acquisition or scripted actions.'
        if obligation.startswith('script:'):
            return 'An explicit source-backed action recipe and supported behavior/progress owner are required. Confirm its exact trigger, invoker, target state, phase, item/spell and success credit before execution.'
        return 'Controlled simulation does not establish this outstanding runtime/world observation or live acceptance condition.'
    output=[]
    for ledger in rows:
        if ledger['classification'] in ('GENERIC-PROVEN','STRATEGY-PROVEN'):continue
        ident=ledger['quest_id'];q=qt.get(ident);addon=qa.get(ident,{})
        actor_ids={(a['type'],a['entry']) for a in ledger['primary_objective_actors']}
        actor_ids.update((r['object_type'],r['row']['id']) for r in ledger['primary_relations'])
        actor_records=[]
        for kind,entry in sorted(actor_ids):
            row=actors[kind].get(entry);name=row.get('ScriptName','') if row else ''
            actor_records.append({'kind':kind,'entry':entry,'template':row,'template_sha256':digest(row) if row else None,
                'script_name':name,'cpp_matches':cpp(name),'smart_scripts':[script(r) for r in scripts[(0 if kind=='Creature' else 1,entry)]],
                'credit_aliases':aliases[entry] if kind=='Creature' else [],
                'credit_granting_scripts':[script(r) for r in credit_scripts[entry]] if kind=='Creature' else [],
                'condition_references':[dict(r,source_contract=condition_codes.get(r['SourceTypeOrReferenceId'],'reference-or-unknown')) for r in conditions[entry]],
                'condition_limit':'These are exact source-ID references. Their source type/group/target determines applicability; they are not all admission requirements for this quest.'})
        item_ids=set()
        if q:
            if q['StartItem']:item_ids.add(q['StartItem'])
            item_ids.update(q[f'RequiredItemId{i}'] for i in range(1,7) if q[f'RequiredItemId{i}'])
        item_records=[]
        for item_id in sorted(item_ids):
            row=items.get(item_id);spells=[row.get(f'spellid_{i}',0) for i in range(1,6)] if row else []
            item_records.append({'item_id':item_id,'template':row,'template_sha256':digest(row) if row else None,
                'spell_script_bindings':[{'row':r,'cpp_matches':cpp(r['ScriptName'])} for spell in spells if spell for r in spell_scripts[spell]]})
        profile=profile_index.get(ident,{})
        output.append({'quest_id':ident,'classification':ledger['classification'],'ledger_record_sha256':digest(ledger),
            'primary_quest_template':q,'primary_quest_addon':addon,'actors':actor_records,'items':item_records,
            'quest_availability_conditions':ledger['primary_conditions'],'direct_quest_scripts':ledger['primary_direct_quest_scripts'],
            'existing_profile_evidence':{'nodes':profile.get('profile_nodes',[]),'parse_failures':profile.get('profile_parse_failures',[]),
                'authority_limit':'Existing profiles are historical candidates. Only independently corroborated original TC335 actions were admitted to the shipped strategy pack.'},
            'eventai_table_status':'No creature_ai_scripts table in this pinned primary database; no secondary EventAI behavior is imported as primary fact.',
            'remaining':[{'obligation':obligation,'closure_requirement':evidence_kind(obligation)} for obligation in ledger['remaining_obligations']],
            'live_evidence_required':['Configured realm/client/build and dataset/pack hashes','Fresh actor/map/phase/GUID and offered/accepted quest identity',
                'Observed action/target/item or route plus authoritative objective credit and rewarded-history acknowledgement'],
            'invented_actions':False,'live_completion_proven':False})
    args.output.mkdir(parents=True)
    raw=b'\n'.join(json.dumps(r,sort_keys=True,separators=(',',':'),ensure_ascii=True).encode() for r in output)+b'\n'
    with (args.output/'remaining-source-correlations.jsonl.gz').open('xb') as f:
        with gzip.GzipFile(fileobj=f,mode='wb',mtime=0) as stream:stream.write(raw)
    revision=subprocess.check_output(['git','rev-parse','HEAD'],cwd=cpp_root,text=True).strip()
    assert revision=='95657f54779467effea8a1749a61ff93abc1d707'
    summary={'remaining_quests':len(output),'classification_counts':dict(Counter(r['classification'] for r in output)),
        'ledger_sha256':hashlib.sha256(args.ledger.read_bytes()).hexdigest(),'primary_revision':revision,
        'profile_review_sha256':hashlib.sha256(args.profiles.read_bytes()).hexdigest(),'cpp_files_indexed':len(cpp_files),
        'cpp_script_names_queried':len(cpp_cache),'correlations_uncompressed_sha256':hashlib.sha256(raw).hexdigest(),
        'unresolved_ids':[r['quest_id'] for r in output],'live_completion_proven':False}
    (args.output/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k!='unresolved_ids'},indent=2))


if __name__=='__main__':main()

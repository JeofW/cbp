"""Reconcile observed routes and fully enumerate remaining source obligations.

All reference script triggers are decoded using pinned headers. Links and timed
action-list callers are reported as graph evidence, never synthesized recipes.
"""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
import gzip
import hashlib
import json
from pathlib import Path
import shutil
from quest_ledger_335 import CLASSIFICATIONS, apply_observed_route_evidence, read_reference_table
from enrich_quest_source_335 import constants

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path,value):
    with path.open('x',encoding='utf-8') as stream:json.dump(value,stream,indent=2,ensure_ascii=True,allow_nan=False);stream.write('\n')

def event_type_names(header):
    # SmartEventFlags shares the SMART_EVENT_ prefix and numeric values with
    # SmartEvents; a flag bit is not the event_type column's enum.
    return {row['value']:name for name,row in header.items()
            if name.startswith('SMART_EVENT_') and not name.startswith('SMART_EVENT_FLAG_') and name!='SMART_EVENT_END'}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ['input','route-results','route-cases','evidence','output']:parser.add_argument('--'+name,type=Path,required=True)
    args=parser.parse_args()
    if args.output.exists():raise FileExistsError('Evidence output must be new')
    records=[json.loads(line) for line in (args.input/'quest-ledger.jsonl').read_text().splitlines()]
    routes=[json.loads(line) for line in args.route_results.read_text().splitlines()]
    cases=read(args.route_cases);summary=read(args.input/'summary.json');ids={row['quest_id'] for row in records}
    expected={('item-starter',row['quest_id'],'') for row in cases['items']} | {('creature-credit',row['quest_id'],row['pointer']) for row in cases['credits']}
    actual={(row['route_type'],row['quest_id'],row['pointer']) for row in routes}
    if actual != expected or len(routes)!=len(actual) or len(routes)!=131:raise ValueError('The exact7+124 route rows are not covered once each')
    if any(row['dataset_sha256']!=summary['dataset_sha256'] or row['failed_cases'] for row in routes):raise ValueError('Route identity or validation failure')
    if not {row['quest_id'] for row in routes}<=ids:raise ValueError('Route quest missing from ledger')
    by_id=defaultdict(list)
    for row in routes:by_id[row['quest_id']].append(row)
    header=constants(args.evidence/'special-contracts/ac-SmartScriptMgr.h')
    primary=constants(args.evidence/'special-contracts/tc-SmartScriptMgr.h')
    event_names=event_type_names(header)
    action_names={row['value']:name for name,row in header.items() if name.startswith('SMART_ACTION_')}
    script_source=header['SMART_SCRIPT_TYPE_TIMED_ACTIONLIST']['value']
    linked_event=header['SMART_EVENT_LINK']['value']
    call_list=header['SMART_ACTION_CALL_TIMED_ACTIONLIST']['value']
    script_path=args.evidence/'reference-tables/ac-smart_scripts.sql'
    manifest={row['file']:row for row in read(args.evidence/'reference-tables/source-downloads.json')}
    if sha(script_path)!=manifest['ac-smart_scripts.sql']['sha256']:raise ValueError('Reference script hash mismatch')
    scripts=list(read_reference_table(script_path));script_groups=defaultdict(list);callers=defaultdict(list)
    for script in scripts:
        script_groups[(script['source_type'],script['entryorguid'])].append(script)
        if script['action_type']==call_list:callers[script['action_param1']].append(script)
    script_dispositions=[]
    for case in cases['credits']:
        evidence=[]
        for script in case['scripts']:
            event=event_names.get(script['event_type'],'UNMAPPED_EVENT_'+str(script['event_type']))
            action=action_names.get(script['action_type'],'UNMAPPED_ACTION_'+str(script['action_type']))
            if script['source_type']==script_source:
                category='timed-action-list-caller-context-required'
                parents=callers[script['entryorguid']]
            elif script['event_type']==linked_event:
                category='linked-script-trigger-context-required'
                parents=[row for row in script_groups[(script['source_type'],script['entryorguid'])] if row['link']==script['id']]
            else:
                category=event.lower().replace('smart_event_','trigger-')
                parents=[]
            evidence.append({'line':script['__source_line'],'source_type':script['source_type'],'actor_or_list':script['entryorguid'],
                'event':event,'action':action,'category':category,'parameters':script,
                'parent_script_lines':[row['__source_line'] for row in parents],
                'parent_triggers':[event_names.get(row['event_type'],'unknown') for row in parents],
                'primary_event_contract':primary.get(event),'secondary_event_contract':header.get(event),
                'primary_action_contract':primary.get(action),'secondary_action_contract':header.get(action),
                'remaining':'Validate actual realm trigger, target and credit acknowledgement; supply an explicit bound strategy before a nonordinary action.'})
        script_dispositions.append({'quest_id':case['quest_id'],'pointer':case['pointer'],'credit_entry':case['target_entry'],
            'has_client_alias_reference':bool(case['aliases']),'scripts':evidence,
            'classification_limit':'Secondary script participation is not a realm recipe. No script ID, spell or gossip option is executed from this join.'})
    review_by_id=defaultdict(list)
    for row in script_dispositions:review_by_id[row['quest_id']].append(row)
    unresolved=defaultdict(set)
    for record in records:
        apply_observed_route_evidence(record,by_id[record['quest_id']])
        record['credit_script_review']=review_by_id[record['quest_id']]
        obligations=set(record.get('execution_requirements',[])+record.get('source_obligations',[]))
        if record['classification'] not in ('GENERIC-PROVEN','STRATEGY-PROVEN'):
            for requirement in obligations:unresolved[requirement].add(record['quest_id'])
            if not obligations:unresolved['live-acceptance-and-runtime-observation-required'].add(record['quest_id'])
        record['remaining_category_review']={'reviewed':True,'categories':sorted(obligations),
            'source_or_live_obligations_retained':record['classification'] not in ('GENERIC-PROVEN','STRATEGY-PROVEN'),
            'no_action_inferred_from_reference_only':True}
    args.output.mkdir(parents=True)
    for name in ['flagged-rows.json','structural-checks.json','sources.json','source-contract-codes.json','review-receipt.json']:
        shutil.copyfile(args.input/name,args.output/name)
    ledger=args.output/'quest-ledger.jsonl'
    with ledger.open('x',encoding='utf-8') as stream:
        for row in records:stream.write(json.dumps(row,separators=(',',':'),ensure_ascii=True,allow_nan=False)+'\n')
    with (args.output/'quest-ledger.jsonl.gz').open('xb') as out:
        with gzip.GzipFile(fileobj=out,mode='wb',mtime=0) as compressed:compressed.write(ledger.read_bytes())
    classifications={kind:[row['quest_id'] for row in records if row['classification']==kind] for kind in CLASSIFICATIONS}
    summary['classification_counts']={kind:len(values) for kind,values in classifications.items()}
    summary['per_quest_ledger_sha256']=sha(ledger)
    summary['observed_route_extension']={'route_rows':len(routes),'passed_cases':sum(row['passed_cases'] for row in routes),'failed_cases':0,
        'whole_item_pipelines':sum(row['controlled_pipeline_passed'] for row in routes),
        'route_results':dict(Counter(row['route_result'] for row in routes)),
        'promoted_ids':[row['quest_id'] for row in records if row['observed_route_classification']['before']!=row['classification']],
        'source_result_sha256':sha(args.route_results),'source_cases_sha256':sha(args.route_cases),
        'remaining_category_count':len(unresolved),'all4335_categories_reviewed':len(records)==4335,
        'script_trigger_categories':dict(Counter(script['category'] for row in script_dispositions for script in row['scripts']))}
    write(args.output/'summary.json',summary);write(args.output/'classification-ids.json',classifications)
    write(args.output/'remaining-category-ids.json',{key:sorted(value) for key,value in sorted(unresolved.items())})
    write(args.output/'credit-script-dispositions.json',script_dispositions)
    shutil.copyfile(args.route_results,args.output/'observed-route-results.jsonl')
    shutil.copyfile(args.route_cases,args.output/'observed-route-cases.json')
    print(json.dumps({'classifications':summary['classification_counts'],'observed_routes':summary['observed_route_extension']},indent=2))

if __name__=='__main__':main()

"""Represent explicitly selected missing return materials as required carried stock.

This exporter does not prove or invent acquisition. Its output cannot replace
ordinary objectives or the existing acceptance-supplied supplemental owner.
"""
from __future__ import annotations

import re

from quest_repair_pack_335 import BASE_FIELDS, ADDON_FIELDS, indexed, digest


def build_contracts(data, tables, source, quest_ids):
    for name in ('quest_template', 'quest_template_addon', 'item_template'):
        if name not in tables:
            raise ValueError('Missing primary stock table: ' + name)
    for field, length in (('CoreRevision', 40), ('SourceSqlSha256', 64), ('QuestDataSha256', 64)):
        if not re.fullmatch('[0-9a-fA-F]{' + str(length) + '}', source.get(field, '')):
            raise ValueError('Missing exact stock source binding: ' + field)
    if not isinstance(source.get('DatabaseRevision'), str) or not source['DatabaseRevision']:
        raise ValueError('Stock source database identity is missing')
    if any(type(value) is not int or value <= 0 for value in quest_ids):
        raise ValueError('Stock export scope requires positive quest identities')
    model = indexed(data['Quests'], 'Id')
    primary = indexed(tables['quest_template'], 'ID')
    addons = indexed(tables['quest_template_addon'], 'ID')
    item_templates = indexed(tables['item_template'], 'entry')
    prefix = f"tc335:{source['CoreRevision']}:{source['DatabaseRevision']}:{source['SourceSqlSha256']}"
    contracts, review = [], []
    for ident in sorted(quest_ids):
        quest = model.get(ident); template = primary.get(ident); addon = addons.get(ident, {})
        problems = []; required = {}; represented = {}
        if quest is None or template is None:
            problems.append('subject-not-present-in-both-sources')
        else:
            if (any(name in template and quest.get(name) != template[name] for name in BASE_FIELDS)
                    or any(quest.get(name) != addon.get(name, 0) for name in ADDON_FIELDS)):
                problems.append('subject-primary-field-conflict')
            objectives = quest.get('Objectives')
            if (template.get('QuestType') != 2 or type(quest.get('SpecialFlags')) is not int
                    or quest['SpecialFlags'] & 0x22 or not isinstance(objectives, list) or not objectives
                    or any(not isinstance(obj, dict) for obj in objectives)
                    or all(obj.get('Type') == 'TurnInOnly' for obj in objectives)
                    or quest.get('DeliveryItems') is not None or quest.get('AcceptanceSupplies') is not None):
                problems.append('unsupported-or-separate-objective-owner')
            for index in range(1, 7):
                item = template.get('RequiredItemId' + str(index)); count = template.get('RequiredItemCount' + str(index))
                if type(item) is not int or type(count) is not int or not 0 <= item <= 2**31-1 or not 0 <= count <= 2**31-1:
                    problems.append('invalid-primary-item-quantity'); continue
                if item == count == 0:
                    continue
                if item <= 0 or count <= 0 or item in required:
                    problems.append('invalid-or-duplicate-primary-item'); continue
                required[item] = count
                if item not in item_templates:
                    problems.append('primary-item-template-absent:' + str(item))
            for objective in objectives or []:
                if not isinstance(objective, dict):
                    continue
                item = objective.get('ItemId', 0)
                if item in required:
                    if (objective.get('Type') not in ('CollectItem', 'CollectFromGameObject')
                            or type(objective.get('CollectCount')) is not int or objective['CollectCount'] != required[item]):
                        problems.append('modeled-item-quantity-or-owner-conflict:' + str(item))
                    else:
                        represented[item] = required[item]
            supplied = quest.get('SupplementalSupply')
            if supplied is not None:
                item = supplied.get('ItemId') if isinstance(supplied, dict) else None
                needed = supplied.get('RequiredCount') if isinstance(supplied, dict) else None
                provided = supplied.get('ProvidedCount') if isinstance(supplied, dict) else None
                if (type(item) is not int or item != quest.get('StartItem') or type(needed) is not int
                        or type(provided) is not int or required.get(item) != needed or provided < needed
                        or item in represented):
                    problems.append('existing-supplemental-owner-conflict')
                else:
                    represented[item] = needed
            missing = [{'ItemId': item, 'Count': count} for item, count in sorted(required.items()) if item not in represented]
            if any(item['ItemId'] == quest.get('StartItem') for item in missing):
                problems.append('unrepresented-start-item-supply-needs-separate-proof')
            existing = quest.get('RequiredStockItems')
            if existing is not None and existing != missing:
                problems.append('existing-stock-conflicts-with-primary')
        if quest is None or template is None:
            missing = []; existing = None
        contract = {'QuestId': ident, 'SourceRef': prefix + ':quest_template:required-items:' + str(ident), 'Items': missing}
        emit = bool(missing) and not problems and existing is None
        if emit:
            contracts.append(contract)
        review.append({'quest_id': ident, 'emitted': emit, 'remaining': sorted(set(problems)),
            'primary_required_items': required, 'represented_items': represented,
            'required_stock_items': missing, 'existing_matching_contract': existing == missing and existing is not None,
            'primary_quest_row_sha256': digest(template) if template else None,
            'primary_addon_row_sha256': digest(addon) if addon else None,
            'item_row_sha256': {str(item): digest(item_templates[item]) for item in required if item in item_templates},
            'contract_sha256': digest(contract) if emit else None,
            'acquisition_proven': False, 'acquisition_limit': 'Must already be carried; no acquisition action is exported.',
            'live_completion_proven': False})
    return contracts, review

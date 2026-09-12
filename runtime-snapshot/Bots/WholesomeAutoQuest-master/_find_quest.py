import json
data = json.load(open(r'C:\Games\wow\CB\Bots\WholesomeAutoQuest\quest_data\quest_data.json'))

# Find Gold Dust Exchange
for q in data['Quests']:
    if 'Gold Dust' in q.get('Name', ''):
        print(f'Id={q["Id"]} Name={q["Name"]} Lvl={q["QuestLevel"]} MinLvl={q["MinLevel"]}')
        print(f'  StartItem={q.get("StartItem",0)} SpecialFlags={q.get("SpecialFlags",0)}')
        print(f'  RequiredFaction1={q.get("RequiredFactionId1",0)} RequiredFaction2={q.get("RequiredFactionId2",0)}')
        print(f'  PrevQuest={q.get("PrevQuestID",0)} NextQuest={q.get("NextQuestID",0)}')
        print(f'  AllowableRaces={q.get("AllowableRaces",0)}')
        print(f'  QuestInfoID={q.get("QuestInfoID",0)}')
        print(f'  Objectives={len(q.get("Objectives",[]))}')
        print(f'  PreviousQuestsIds={q.get("PreviousQuestsIds",[])}')
        # Find givers
        givers = [g for g in data['QuestGivers'] if g['QuestId'] == q['Id']]
        print(f'  Givers: {len(givers)}')
        for g in givers:
            gid = str(g['GiverId'])
            print(f'    GiverId={gid} Name={g.get("GiverName","?")} Type={g.get("GiverType","?")}')
            cs = data.get('CreatureSpawns', {})
            if gid in cs:
                for sp in cs[gid][:3]:
                    print(f'      Spawn: map={sp["Map"]} ({sp["X"]:.1f}, {sp["Y"]:.1f})')
            gs = data.get('GameObjectSpawns', {})
            if gid in gs:
                for sp in gs[gid][:3]:
                    print(f'      GOSpawn: map={sp["Map"]} ({sp["X"]:.1f}, {sp["Y"]:.1f})')
            if gid not in cs and gid not in gs:
                print(f'      NO SPAWNS for giver {gid}')

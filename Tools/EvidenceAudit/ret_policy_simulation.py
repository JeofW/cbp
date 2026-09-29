#!/usr/bin/env python3
"""Deterministic Ret trade-off model, separate from real-owner admission tests.

This is a transparent sensitivity experiment, not a WoW combat emulator or a DPS
claim. Client costs/durations and pinned core coefficients are retained as facts;
gear, encounter health, proc timing, incoming damage and drink speed are explicit
assumptions. The production C# suites exercise the actual decision owners.
"""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
from dataclasses import asdict, dataclass
import itertools
import json
import math
from pathlib import Path
import statistics

FACT_PATH = Path(__file__).with_name("fixtures") / "ret_335a_mechanics.json"
POLICIES = ("retained", "consecration_at_two", "short_pack_reserve", "holy_wrath_reserve", "hardcast_exorcism")

@dataclass(frozen=True)
class Scenario:
    level: int = 60
    targets: int = 1
    durability: float = 1.0
    mana_fraction: float = 1.0
    undead: bool = False
    moving: bool = False
    pressured: bool = False
    wise: bool = True
    seal: str = "Command"
    art_of_war_rank: int = 2

def load_facts(path: Path = FACT_PATH) -> dict:
    facts = json.loads(path.read_text(encoding="utf-8"))
    if facts["client_build"] != 12340 or facts["rules"]["divine_storm_cap"] != 4:
        raise ValueError("Original-client/core-correction identity is required")
    return facts

def spell_facts(facts: dict, level: int) -> dict[str, dict]:
    spells: dict[str, dict] = {}
    for spell in facts["spells"]:
        if spell["level"] <= level and (spell["name"] not in spells or spells[spell["name"]]["level"] < spell["level"]):
            spells[spell["name"]] = spell
    return spells

def simulate(s: Scenario, policy: str, facts: dict | None = None, drink_rate: float = .04) -> dict:
    if policy not in POLICIES or s.targets < 1 or not 0 <= s.mana_fraction <= 1 or drink_rate <= 0:
        raise ValueError("Invalid policy/scenario")
    facts = facts or load_facts()
    spells = spell_facts(facts, s.level)
    rules = facts["rules"]
    # Synthetic gear, not observations of the user's character. Weapon damage
    # includes the attack-power contribution; physical damage has an assumed
    # 30% armor reduction. No hit/expertise/raid buffs/set bonuses are inferred.
    ap, sp, weapon_base, base_mana, max_mana, max_health = ((1100., 220., 230., 1512., 3300., 4500.)
        if s.level == 60 else (4000., 900., 600., 4394., 8000., 18000.))
    swing, crit, armor = 3.5, .20, .70
    weapon = weapon_base + ap / 14 * swing
    normalized_weapon = weapon_base + ap / 14 * 3.3
    enemy_initial = (2500. if s.level == 60 else 14000.) * s.durability
    enemies = [enemy_initial] * s.targets
    mana = initial_mana = max_mana * s.mana_fraction
    health = max_health * (.45 if s.pressured else 1.)
    cooldowns: dict[str, float] = defaultdict(float)
    counts: Counter[str] = Counter()
    spent = healed = overkill = 0.
    gcd = next_swing = 0.
    proc_budget = 0.
    proc_until = -1.
    consecration_end = plea_end = stun_until = -1.
    next_consecration = next_plea = next_dot = math.inf
    stacks = [0] * s.targets
    stack_until = [0.] * s.targets
    pending: tuple[float, str, int] | None = None
    blocked = 0
    dt = .25
    now = 0.
    def alive() -> list[int]:
        return [index for index, hp in enumerate(enemies) if hp > 0]
    def damage(index: int, value: float) -> None:
        nonlocal overkill
        if enemies[index] <= 0:
            overkill += value
            return
        overkill += max(0., value - enemies[index])
        enemies[index] = max(0., enemies[index] - value)
    def seal_hit(index: int, cleave: bool) -> None:
        if s.seal == "Command":
            for enemy in (alive()[:rules["command_cleave_cap"]] if cleave else [index]):
                damage(enemy, .36 * weapon * (1 + .5 * crit))
        elif s.seal == "Righteousness":
            damage(index, swing * (.022 * ap + .044 * sp))
        elif s.seal in {"Vengeance", "Corruption"}:
            damage(index, .066 * stacks[index] * weapon * (1 + .5 * crit))
    def hit(name: str, target: int) -> None:
        nonlocal healed, health, proc_budget, proc_until, stun_until
        if name == "Crusader Strike":
            damage(target, .75 * normalized_weapon * armor * (1 + crit)); seal_hit(target, True)
            proc_budget += crit
        elif name == "Divine Storm":
            before = sum(enemies)
            for enemy in alive()[:4]:
                damage(enemy, 1.1 * weapon * armor * (1 + crit)); seal_hit(enemy, False)
                proc_budget += crit
            gain = min(max_health - health, max(0., before - sum(enemies)) * .25)
            health += gain; healed += gain
        elif name == "Judgement":
            if s.seal == "Command": amount = .19 * weapon + .08 * ap + .13 * sp
            elif s.seal == "Righteousness": amount = 1 + .20 * ap + .32 * sp
            elif s.seal in {"Vengeance", "Corruption"}: amount = (1 + .14 * ap + .22 * sp) * (1 + .1 * stacks[target])
            else: amount = 1 + .16 * ap + .25 * sp
            damage(target, amount * (1 + .5 * crit))
        elif name in {"Exorcism", "Hammer of Wrath", "Holy Wrath"}:
            coefficients = facts["coefficients"][name]
            amount = spells[name]["base_average"] + coefficients["attack_power"] * ap + coefficients["spell_power"] * sp
            amount *= 1.5 if name == "Exorcism" and s.undead else 1 + .5 * crit
            for enemy in alive() if name == "Holy Wrath" else [target]: damage(enemy, amount)
            if name == "Holy Wrath": stun_until = now + 3
        if proc_budget >= 1 and s.art_of_war_rank:
            proc_budget -= 1; proc_until = now + 15

    while now < 120 and alive() and health > 0:
        targets = alive(); target = targets[0]
        mana = min(max_mana, mana + max_mana * .002 * dt)
        incoming = max_health * (.012 if s.pressured else .006) * len(targets)
        if now >= stun_until: health -= incoming * dt
        if pending and now >= pending[0]:
            _, name, index = pending; pending = None; hit(name, index)
        if next_plea <= now <= plea_end:
            mana = min(max_mana, mana + .05 * max_mana); next_plea += 3
        if next_consecration <= now <= consecration_end and not s.moving:
            tick = spells["Consecration"]["base_average"] + .04 * (ap + sp)
            for enemy in alive(): damage(enemy, tick)
            next_consecration += 1
        if next_dot <= now and s.seal in {"Vengeance", "Corruption"}:
            for enemy in alive():
                if now <= stack_until[enemy]: damage(enemy, stacks[enemy] * (.025 * ap + .013 * sp))
                else: stacks[enemy] = 0
            next_dot = now + 3
        if now >= next_swing and not pending and alive():
            target = alive()[0]
            damage(target, weapon * armor * (1 + crit))
            if s.seal in {"Vengeance", "Corruption"}:
                stacks[target] = min(5, stacks[target] + 1); stack_until[target] = now + 15
                if not math.isfinite(next_dot): next_dot = now + 3
                if stacks[target] == 5: seal_hit(target, False)
            elif s.seal in {"Wisdom", "Light"}:
                # Utility proc realizations are sensitivity assumptions, not a
                # prediction of server proc rolls: one proc every second swing.
                if counts["auto"] % 2 == 0:
                    if s.seal == "Wisdom": mana = min(max_mana, mana + .04 * max_mana)
                    else:
                        gain = min(max_health - health, .15 * (ap + sp)); health += gain; healed += gain
            else: seal_hit(target, True)
            counts["auto"] += 1; proc_budget += crit
            if proc_budget >= 1 and s.art_of_war_rank: proc_budget -= 1; proc_until = now + 15
            next_swing = now + swing * (1.35 if s.moving else 1)
        if now < gcd or pending or not alive(): now += dt; continue
        targets = alive(); target = targets[0]; mp = mana / max_mana
        proc = now < proc_until
        priority: list[str] = []
        if health < .35 * max_health: priority.append("Flash of Light")
        if mp < .5 and health > .7 * max_health and s.level >= 71: priority.append("Divine Plea")
        if mp <= .15: priority.append("Judgement")
        if proc and s.undead: priority.append("Exorcism")
        if enemies[target] / enemy_initial <= .2: priority.append("Hammer of Wrath")
        priority.extend(("Crusader Strike", "Divine Storm", "Judgement"))
        if proc or policy == "hardcast_exorcism" and not s.moving: priority.append("Exorcism")
        if s.undead and (policy != "holy_wrath_reserve" or mp > .5 or health <= .7 * max_health): priority.append("Holy Wrath")
        minimum = 2 if policy == "consecration_at_two" else 3
        forecast = sum(enemies) / max(1., weapon * len(targets) / swing)
        if len(targets) >= minimum and mp > .5 and not s.moving and (policy != "short_pack_reserve" or forecast >= 6):
            priority.append("Consecration")
        selected = None
        for name in priority:
            if cooldowns[name] > now: continue
            cost = base_mana * ({"Judgement":.05,"Flash of Light":.07}.get(name, spells.get(name,{}).get("cost_base_percent",0)/100))
            if cost <= mana:
                selected = name; break
        if selected is None: blocked += 1; now += dt; continue
        name = selected
        cost = base_mana * ({"Judgement":.05,"Flash of Light":.07}.get(name, spells.get(name,{}).get("cost_base_percent",0)/100))
        mana -= cost; spent += cost; counts[name] += 1; gcd = now + 1.5
        cooldowns[name] = now + ({"Judgement":8.,"Flash of Light":0.}.get(name,spells.get(name,{}).get("cooldown_seconds",0)))
        if name == "Consecration": consecration_end = now + 8; next_consecration = now + 1
        elif name == "Divine Plea": plea_end = now + 15; next_plea = now + 3
        elif name == "Flash of Light":
            gain = min(max_health - health, .20 * max_health * (.5 if now < plea_end else 1)); health += gain; healed += gain
            if proc: proc_until = -1
        elif name == "Exorcism":
            cast = 0 if proc and s.art_of_war_rank == 2 else .75 if proc else 1.5
            if cast:
                pending = (now + cast, name, target)
                # Conservative melee opportunity-cost assumption; test output
                # separately states that a native swing-timer reset is unmeasured.
                next_swing = max(next_swing, now + cast + swing)
            else: hit(name, target)
            proc_until = -1
        else:
            hit(name, target)
            if name == "Judgement" and s.wise: mana = min(max_mana, mana + .25 * base_mana)
        now += dt
    won = not alive() and health > 0
    refill = max(0., initial_mana - mana) / (max_mana * drink_rate)
    return {"scenario":asdict(s),"policy":policy,"won":won,"kill_seconds":round(now,2),
            "mana_end_fraction":round(mana/max_mana,4),"mana_spent":round(spent,2),"healing":round(healed,2),
            "overkill":round(overkill,2),"refill_seconds":round(refill,2),"kill_plus_refill_seconds":round(now+refill,2),
            "ability_counts":dict(counts),"idle_decisions":blocked}

def run_matrix(output: Path) -> dict:
    facts = load_facts(); rows = []
    for values in itertools.product((60,80),(1,2,3,4,6),(.35,1.,3.),(.2,.55,1.),(False,True),(False,True),(False,True),(False,True)):
        scenario = Scenario(*values)
        rows.extend(simulate(scenario,policy,facts) for policy in POLICIES)
    summary = {"schema_version":1,"scenario_count":len(rows)//len(POLICIES),"policy_runs":len(rows),"policies":{},
               "qualification":"Synthetic deterministic trade-off model, not measured DPS or exhaustive WoW/server behavior. Actual production admission is tested separately in C#.",
               "assumptions":{"tick_seconds":.25,"physical_armor_multiplier":.7,"crit_probability":.2,
                 "swing_seconds":3.5,"drink_max_mana_per_second":.04,"proc_realization":"deterministic accumulated expectation",
                 "gear":"Explicit synthetic level60/80 AP/SP/weapon/base-mana values in simulate(); no user gear inferred",
                 "unmodeled":["latency","miss/dodge/parry","gear set bonuses","random proc variance","ranged kiting geometry","PvP diminishing returns","exact healing ranks","individual realm overrides"]}}
    for policy in POLICIES:
        values=[r for r in rows if r['policy']==policy];wins=[r for r in values if r['won']]
        summary['policies'][policy]={"completed":len(wins),"runs":len(values),
            "median_kill_seconds":statistics.median(r['kill_seconds'] for r in wins),
            "median_kill_plus_refill_seconds":statistics.median(r['kill_plus_refill_seconds'] for r in wins),
            "median_mana_spent":statistics.median(r['mana_spent'] for r in wins)}
    comparisons=[]
    for index in range(0,len(rows),len(POLICIES)):
        base=rows[index]
        for other in rows[index+1:index+len(POLICIES)]:
            if base['won'] and other['won']:
                comparisons.append({'scenario':base['scenario'],'policy':other['policy'],
                    'kill_delta':round(other['kill_seconds']-base['kill_seconds'],2),
                    'cycle_delta':round(other['kill_plus_refill_seconds']-base['kill_plus_refill_seconds'],2),
                    'mana_delta':round(other['mana_spent']-base['mana_spent'],2)})
    summary['matched_comparisons']={p:{'faster':sum(r['kill_delta']<0 for r in comparisons if r['policy']==p),
        'slower':sum(r['kill_delta']>0 for r in comparisons if r['policy']==p),
        'cycle_improves':sum(r['cycle_delta']<0 for r in comparisons if r['policy']==p),
        'cycle_worsens':sum(r['cycle_delta']>0 for r in comparisons if r['policy']==p)} for p in POLICIES[1:]}
    seal_rows=[]
    for targets,durability,seal in itertools.product((1,2,3,4),(.35,1.,3.,6.),("Command","Righteousness","Vengeance","Corruption","Wisdom","Light")):
        seal_rows.append(simulate(Scenario(targets=targets,durability=durability,seal=seal),"retained",facts))
    output.mkdir(parents=True,exist_ok=True)
    for name,value in [('summary',summary),('runs',rows),('comparisons',comparisons),('seals',seal_rows)]:
        (output/(name+'.json')).write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
    return summary

if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True)
    arguments=parser.parse_args();print(json.dumps(run_matrix(arguments.output),indent=2))

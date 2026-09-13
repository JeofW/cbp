# Quest rest ownership correction

Source baseline dedc25fe369b6a1d1c7e3e2ea8b032906848dcc8. WoWPlayer.IsResting reads PlayerFlags[32]; LocalPlayer documents this inherited 0x20 flag. Four Wholesome activity/recovery call sites OR that area/rested-XP status into an intentional rest decision. Being in an inn/city is not proof that the bot is eating, drinking or has yielded to its rest routine.

Primary format corroboration reviewed 2026-09-13: AzerothCore WotLK Player.h defines PLAYER_FLAGS_RESTING as 0x20 and REST_FLAG_IN_TAVERN/IN_CITY; MiscHandler.cpp sets the tavern rest flag on entering the inn. Sources: https://github.com/azerothcore/azerothcore-wotlk/blob/master/src/server/game/Entities/Player/Player.h and https://github.com/azerothcore/azerothcore-wotlk/blob/master/src/server/game/Handlers/MiscHandler.cpp . This is format/semantic support, not evidence that every captured stall occurred in a resting area.

Test first: four actual compiled call-site guards must reject use of WoWPlayer.get_IsResting while retaining HasAura observations. Five tests execute the existing production pickup-ownership policy for active/rest/user-pause/both/stale-owner controls. These guards are not simulated live city traversal and must be reported as such.

Repair only removes the area-rest getter from Pulse pickup accounting, CreateLiveWorkSample, TryRecoverFailedPickupTravel and CapturePreDeathAttribution. Preserve routine-owned _restingPaused, Food/Drink auras, manual pause, combat, transport, death and generation predicates. Do not redefine the public IsResting property or change its meaning for other callers. Run the focused and all combined suites and retain source/run evidence before review. Live inn/city pickup/objective/travel remains an acceptance case.

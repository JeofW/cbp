# Original-client role contract: active owners, not only dormant FollowMe

Base: ed68d59f78763620650e4935f15aec78ded47bd9, the saved combined party/Wholesome source. Owner requested original WotLK 3.3.5a build 12340 compatibility.

Primary interface evidence: wowgaming/3.3.5-interface-files PlayerFrame.lua, blob 81290a5c54042692de8071cfc8b94719886a6552, PlayerFrame_UpdateRolesAssigned reads isTank, isHealer, isDamage and applies tank/healer/damage priority. The string-valued retail/Classic convention is not the original signature. Reference: https://github.com/wowgaming/3.3.5-interface-files/blob/main/PlayerFrame.lua .

Five active or retained call sites read return value zero as a role string: three WoWPlayerExtensions self helpers, WoWPartyMember.Role, and CombatBot.FollowMe (not enabled in Root). LazyRaider's live path uses WoWPartyMember.Role; its raw string query is under #if COMMENT. Fixing only dormant FollowMe would miss active group-role consumers.

Test-first: execute the literal production queries under Lua 5.1 with controlled client returns. Include each individual role, no role, nil, priority combinations, absent API, departed unit and rejection of a modern string response as a legacy flag. These tests do not impersonate an attached client. No green claim at test-only commit.

Repair design: one host helper normalizes the three original flags to existing TANK/HEALER/DAMAGER/NONE strings. Preserve public role enums and existing caller fallbacks; use validated original unit tokens, not player names or modern APIs. Each caller delegates to the shared owner. Missing/non-original observations remain NONE. Add host compilation and combined regressions. Do not enable dormant following, change inferred specialization fallbacks, or claim the GUID-only WoWPartyMember constructor now resolves roster slots. Those are separate existing limitations.

No automatic merge, installed-file write or native/mesh modification. Independent review and live group-role acceptance remain required. This is one verified compatibility repair, not a certificate for every legacy Lua command.

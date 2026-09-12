// Compile-only ABI assertions against the pinned native source. No game process.
#include <cstddef>
#include "PathResult.h"
#include "DetourNavMesh.h"
static_assert(sizeof(void*) == 4, "The host requires Win32, not x64.");
static_assert(sizeof(XYZ) == 12, "XYZ must be three contiguous floats.");
static_assert(sizeof(PathResult) == 32, "Managed PathResult expects 32 bytes on x86.");
static_assert(offsetof(PathResult, points) == 0);
static_assert(offsetof(PathResult, polyRefs) == 16);
static_assert(offsetof(PathResult, length) == 20);
static_assert(offsetof(PathResult, status) == 24);
static_assert(offsetof(PathResult, failStep) == 28);
static_assert(sizeof(StraightPathFlags) == 1, "Managed flags are byte arrays.");
static_assert(sizeof(dtPolyRef) == 8, "Managed polygon references require DT_POLYREF64.");
static_assert(sizeof(dtMeshHeader) == 100, "Unexpected serialized Detour header.");
static_assert(sizeof(dtPoly) == 32, "Unexpected serialized polygon layout.");
static_assert(sizeof(dtLink) == 16, "Unexpected serialized 64-bit link layout.");
static_assert(NAV_STEP_NONE == -1 && NAV_STEP_FIND_START_POLY == 0
    && NAV_STEP_FIND_END_POLY == 1 && NAV_STEP_INIT_PATHFIND == 2
    && NAV_STEP_UPDATE_PATHFIND == 3 && NAV_STEP_FINALIZE_PATHFIND == 4
    && NAV_STEP_FIND_STRAIGHT_PATH == 5, "Native failure-step contract changed.");

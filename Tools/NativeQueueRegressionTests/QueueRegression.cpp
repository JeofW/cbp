// Tests the pinned Detour queue itself, linked from the supplied native checkout.
// Only heap-array accesses are optionally instrumented; no queue is reimplemented.
#include "DetourNode.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <functional>
#include <iostream>
#include <limits>
#include <random>
#include <stdexcept>
#include <string>
#include <vector>

#ifdef AUDIT_HEAP_ACCESS
std::uint64_t AuditHeapAccesses = 0;
#endif
static void require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}
static void ordered()
{
    dtNodePool pool(4096, 1024);
    dtNodeQueue queue(4096);
    for (int i = 4096; i > 0; --i)
    {
        dtNode* node = pool.getNode(static_cast<dtPolyRef>(i));
        require(node != nullptr, "allocation"); node->total = static_cast<float>(i); queue.push(node);
    }
    for (int i = 1; i <= 4096; ++i)
    {
        require(queue.top()->total == i, "top ordered");
        require(queue.pop()->total == i, "pop ordered");
    }
    require(queue.empty(), "empty after drain");
}
static void singletonAndAbsent()
{
    dtNodePool pool(8, 8); dtNodeQueue queue(8);
    dtNode* first = pool.getNode(1); first->total = 4;
    dtNode* other = pool.getNode(2); other->total = 2;
    queue.modify(nullptr); queue.modify(first);
    queue.push(first); queue.modify(other);
    require(queue.pop() == first && queue.empty(), "absent modify changes contents");
    first->total = -1; queue.modify(first);
    queue.push(other); require(queue.pop() == other, "popped handle corrupts queue");
    queue.push(first); queue.clear(); queue.modify(first);
    queue.push(other); require(queue.pop() == other && queue.empty(), "clear leaves stale ownership");
}
static void equalCostsAndStates()
{
    dtNodePool pool(8, 8); dtNodeQueue queue(8);
    dtNode* nodes[4];
    for (int i = 0; i < 4; ++i) { nodes[i] = pool.getNode(123, static_cast<unsigned char>(i)); nodes[i]->total = 5; queue.push(nodes[i]); }
    // Preserve the existing tie order; do not introduce a different path tie-breaker.
    int expected[] = {0, 1, 3, 2};
    for (int index : expected) require(queue.pop() == nodes[index], "equal-cost order changed");
    for (int i = 0; i < 4; ++i) require(pool.findNode(123, static_cast<unsigned char>(i)) == nodes[i], "state identity changed");
}
static void seededOperations()
{
    constexpr int capacity = 512;
    dtNodePool pool(capacity, 128); dtNodeQueue queue(capacity);
    std::vector<dtNode*> active;
    std::mt19937 rng(20260913);
    unsigned int id = 1;
    for (int operation = 0; operation < 20000; ++operation)
    {
        unsigned int choice = rng() % 100;
        if (operation % 613 == 0)
        {
            // The production query clears the pool before the queue; memory is
            // still allocated, but old queue membership must not affect reuse.
            pool.clear(); queue.clear(); active.clear(); id = 1;
        }
        if (active.empty() || (choice < 40 && pool.getNodeCount() < capacity))
        {
            dtNode* node = pool.getNode(id++);
            require(node != nullptr, "pool unexpectedly exhausted");
            node->total = static_cast<float>((rng() % 100000) + id * 0.001);
            queue.push(node); active.push_back(node);
        }
        else if (choice < 80)
        {
            dtNode* node = active[rng() % active.size()];
            node->total -= static_cast<float>(rng() % 10000);
            queue.modify(node);
        }
        else
        {
            float expected = (*std::min_element(active.begin(), active.end(), [](dtNode* a, dtNode* b) { return a->total < b->total; }))->total;
            require(queue.top()->total == expected, "model top mismatch");
            dtNode* popped = queue.pop(); require(popped->total == expected, "model pop mismatch");
            active.erase(std::find(active.begin(), active.end(), popped));
            queue.modify(popped); // A stale handle must not modify another queued node.
        }
        require(queue.empty() == active.empty(), "model emptiness mismatch");
    }
    std::cout << "seeded operations=20000\n";
}
static void modifyComplexity()
{
#ifdef AUDIT_HEAP_ACCESS
    constexpr int count = 20000, updates = 2000;
    dtNodePool pool(count, 8192); dtNodeQueue queue(count);
    dtNode* last = nullptr;
    for (int i = 1; i <= count; ++i) { last = pool.getNode(i); last->total = static_cast<float>(i); queue.push(last); }
    AuditHeapAccesses = 0;
    for (int repeat = 0; repeat < updates; ++repeat)
    {
        // Remains a leaf: update cost is measured independently of node migration.
        last->total = std::nextafter(last->total, -std::numeric_limits<float>::infinity());
        queue.modify(last);
    }
    std::uint64_t measured = AuditHeapAccesses;
    unsigned int levels = 0; for (int n = count; n; n >>= 1) ++levels;
    const std::uint64_t ceiling = static_cast<std::uint64_t>(updates) * (8 * levels + 8);
    std::cout << "modify heap accesses=" << measured << " logarithmic ceiling=" << ceiling << " count=" << count << " updates=" << updates << "\n";
    require(queue.top()->total == 1, "complexity fixture changed minimum");
    require(measured <= ceiling, "modify scans the frontier instead of locating the requested node directly");
#else
    std::cout << "complexity instrumentation omitted in sanitizer execution\n";
#endif
}
int main()
{
    int failures = 0;
    const std::pair<const char*, std::function<void()>> cases[] = {
        {"ordered drain", ordered}, {"singleton/absent/popped/clear handles", singletonAndAbsent},
        {"equal-cost order and same-polygon state identity", equalCostsAndStates},
        {"seeded model sequence", seededOperations}, {"bounded update work", modifyComplexity}
    };
    for (const auto& test : cases)
    {
        try { test.second(); std::cout << "PASS " << test.first << "\n"; }
        catch (const std::exception& error) { ++failures; std::cerr << "FAIL " << test.first << ": " << error.what() << "\n"; }
    }
    std::cout << "queue scenarios=" << 5 - failures << "/5 sizeof(dtNode)=" << sizeof(dtNode) << "\n";
    return failures == 0 ? 0 : 1;
}

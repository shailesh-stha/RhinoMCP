using System.Text.Json;
using NUnit.Framework;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Pins down slot-routing semantics for plugin-side tool calls:
//   - An explicit `slot` arg must route to that exact Rhino.
//   - An unknown `slot` arg must produce a structured slot_not_found payload
//     (not a hang, and not a generic MCP error).
//
// Requires a real Rhino install.
[TestFixture]
[Explicit("Spawns real Rhinos; opt in with --filter \"Category=RequiresRhino\".")]
[Category("RequiresRhino")]
internal sealed class ToolDispatchBySlotTests : RouterFixture
{
    [Test]
    public async Task explicit_slot_routes_to_correct_rhino()
    {
        string spawnA = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        string spawnB = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        string slotA = JsonAssert.Parse(spawnA).GetProperty("slotId").GetString()!;
        string slotB = JsonAssert.Parse(spawnB).GetProperty("slotId").GetString()!;

        // Drop three lines into slot A; leave slot B untouched.
        _ = await _router.CallToolTextAsync(
            "run_python",
            new Dictionary<string, object?>
            {
                ["slot"] = slotA,
                ["script"] = """
                    from Rhino.Geometry import Point3d, Line
                    doc = __rhino_doc__
                    for i in range(3):
                        doc.Objects.AddLine(Line(Point3d(i, 0, 0), Point3d(i, 1, 0)))
                    """,
            });

        string listA = await _router.CallToolTextAsync("list_objects",
            new Dictionary<string, object?> { ["slot"] = slotA });

        string listB = await _router.CallToolTextAsync("list_objects",
            new Dictionary<string, object?> { ["slot"] = slotB });

        Assert.Multiple((Action)(() =>
        {
            Assert.That(GetObjectCount(listA), Is.EqualTo(3),
                $"Slot A should contain the 3 lines we added. Got: {listA}");
            Assert.That(GetObjectCount(listB), Is.EqualTo(0),
                $"Slot B should be empty. Got: {listB}");
        }));
    }

    [Test]
    public async Task tool_call_with_unknown_slot_returns_slot_not_found_payload()
    {
        // No spawn — just call a plugin tool with a bogus slot id. The router
        // must short-circuit in the dispatcher with a structured error, not
        // attempt to auto-spawn and not hang.
        string response = await _router.CallToolTextAsync(
            "list_objects",
            new Dictionary<string, object?> { ["slot"] = "made-up-slot-xyz" });

        Assert.That(response, Does.Contain("slot_not_found"),
            $"Expected slot_not_found error payload; got: {response}");
        Assert.That(response, Does.Contain("made-up-slot-xyz"));
    }

    // list_objects wraps its payload in an MCP content envelope:
    //   {"content":[{"type":"text","text":"{\"count\":N,...}"}]}
    private static int GetObjectCount(string envelope)
    {
        JsonElement root = JsonAssert.Parse(envelope);
        JsonElement content = root.GetProperty("content").EnumerateArray().ToArray()[0];
        string payload = content.GetProperty("text").GetString()!;
        return JsonAssert.Parse(payload).GetProperty("count").GetInt32();
    }
}

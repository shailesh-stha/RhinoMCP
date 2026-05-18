using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Slot rows in the SQLite registry are intent, not liveness — RhinoManager
// probes pid + port on every list_slots call and prunes dead rows. These tests
// pin down the reaping behaviour by killing the Rhino out from under the router
// and asserting that list_slots no longer reports the slot.
//
// Requires a real Rhino install on either platform.
[TestFixture]
[Explicit("Spawns a real Rhino; opt in with --filter \"Category=RequiresRhino\".")]
[Category("RequiresRhino")]
internal sealed class SlotReapingTests : RouterFixture
{
    [Test]
    public async Task externally_killed_rhino_is_pruned_from_list_slots()
    {
        string spawnJson = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        int pid = JsonAssert.Parse(spawnJson).GetProperty("pid").GetInt32();

        KillExternally(pid);

        // Give the OS a moment to actually tear the process down before we
        // probe pid+port from inside the router.
        for (int i = 0; i < 50 && IsProcessAlive(pid); i++)
        {
            await Task.Delay(100);
        }
        Assert.That(IsProcessAlive(pid), Is.False, "Test could not kill the spawned Rhino; reaping cannot be verified.");

        string listJson = await _router.CallToolTextAsync("list_slots");
        Assert.That(JsonAssert.Parse(listJson).GetArrayLength(), Is.EqualTo(0),
            "list_slots must prune a slot whose Rhino has died.");
    }

    [Test]
    public async Task animal_name_freed_by_reap_is_reusable_on_next_spawn()
    {
        // Two consecutive spawns: kill the first, spawn again, and verify the
        // second spawn either reuses the freed name or progresses without
        // waiting for the stale row. We don't pin the exact name (the pool's
        // ordering is implementation detail) — only that we can re-spawn and
        // list_slots returns one healthy row.
        string spawnJson = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        int pid = JsonAssert.Parse(spawnJson).GetProperty("pid").GetInt32();
        string firstSlotId = JsonAssert.Parse(spawnJson).GetProperty("slotId").GetString()!;

        KillExternally(pid);
        for (int i = 0; i < 50 && IsProcessAlive(pid); i++)
        {
            await Task.Delay(100);
        }

        string respawnJson = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        JsonElement respawn = JsonAssert.Parse(respawnJson);
        Assert.That(respawn.TryGetProperty("error", out _), Is.False,
            $"Respawn after reap should succeed. Payload: {respawnJson}");

        string listJson = await _router.CallToolTextAsync("list_slots");
        List<JsonElement> slots = JsonAssert.Parse(listJson).EnumerateArray().ToList();
        Assert.That(slots, Has.Count.EqualTo(1),
            "After reap + respawn, exactly one slot should be visible.");
        // Pool ordering is implementation detail — reusing the freed name or
        // picking the next one in the pool are both acceptable. We deliberately
        // don't pin `firstSlotId` here; the count assertion above is the contract.
        _ = firstSlotId;
    }

    private static void KillExternally(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* already gone */ }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}

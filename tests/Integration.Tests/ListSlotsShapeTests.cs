using System.Text.Json;
using NUnit.Framework;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Pins down the JSON shape list_slots returns post-spawn / post-close. The
// existing ListSlotsTests fixture only asserts the empty case; this one
// asserts the populated case and the close-removes path. Requires a real
// Rhino install.
[TestFixture]
[Explicit("Spawns a real Rhino; opt in with --filter \"Category=RequiresRhino\".")]
[Category("RequiresRhino")]
internal sealed class ListSlotsShapeTests : RouterFixture
{
    [Test]
    public async Task list_slots_after_spawn_contains_expected_fields()
    {
        string spawnJson = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        JsonElement spawn = JsonAssert.Parse(spawnJson);
        string slotId = spawn.GetProperty("slotId").GetString()!;
        int port = spawn.GetProperty("port").GetInt32();

        string listJson = await _router.CallToolTextAsync("list_slots");
        List<JsonElement> slots = JsonAssert.Parse(listJson).EnumerateArray().ToList();
        Assert.That(slots, Has.Count.EqualTo(1));

        JsonElement slot = slots[0];
        Assert.That(slot.GetProperty("slotId").GetString(), Is.EqualTo(slotId));
        Assert.That(slot.GetProperty("port").GetInt32(), Is.EqualTo(port));
        Assert.That(slot.GetProperty("version").GetString(), Is.EqualTo("8"));
        Assert.That(slot.GetProperty("adopted").GetBoolean(), Is.False);
        Assert.That(slot.GetProperty("pid").GetInt32(), Is.GreaterThan(0));
        Assert.That(slot.GetProperty("endpoint").GetString(), Is.EqualTo($"http://localhost:{port}"));
    }

    [Test]
    public async Task list_slots_after_close_does_not_include_closed_slot()
    {
        string spawnJson = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        string slotId = JsonAssert.Parse(spawnJson).GetProperty("slotId").GetString()!;

        string closeJson = await _router.CallToolTextAsync(
            "close_slot",
            new Dictionary<string, object?> { ["slot"] = slotId });
        Assert.That(JsonAssert.Parse(closeJson).GetProperty("closed").GetBoolean(), Is.True);

        string listJson = await _router.CallToolTextAsync("list_slots");
        Assert.That(JsonAssert.Parse(listJson).GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task close_slot_twice_returns_slot_not_found_on_second_call()
    {
        string spawnJson = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        string slotId = JsonAssert.Parse(spawnJson).GetProperty("slotId").GetString()!;

        _ = await _router.CallToolTextAsync(
            "close_slot",
            new Dictionary<string, object?> { ["slot"] = slotId });

        string secondClose = await _router.CallToolTextAsync(
            "close_slot",
            new Dictionary<string, object?> { ["slot"] = slotId });
        JsonElement root = JsonAssert.Parse(secondClose);
        Assert.That(root.GetProperty("closed").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("error").GetString(), Is.EqualTo("slot_not_found"));
    }
}

using System.Text.Json;
using NUnit.Framework;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Exercises spawn_slot end-to-end: the router must launch a real Rhino,
// receive its listener announcement, and return the slot metadata. Marked
// [Explicit] because it requires a working Rhino install + freshly-built
// plugin. Opt in with --filter "Category=RequiresRhino".
[TestFixture]
[Explicit("Spawns a real Rhino; opt in with --filter \"Category=RequiresRhino\".")]
[Category("RequiresRhino")]
internal sealed class SpawnSlotTests : SharedRouterFixture
{

    [TestCase("8")]
    [TestCase("9")] // TODO : Fails
    [TestCase("WIP")]
    public async Task spawn_slot_returns_slot_metadata(string version)
    {
        string spawnJson = await _router.CallToolTextAsync("spawn_slot", new() { { "version", version } });
        JsonElement root = JsonAssert.Parse(spawnJson);

        string slotId = root.GetProperty("slotId").GetString() ?? string.Empty;
        Assert.That(string.IsNullOrEmpty(slotId), Is.False);

        string vers = root.GetProperty("version").GetString() ?? string.Empty;
        Assert.That(vers, Is.EqualTo(version));

        bool adopted = root.GetProperty("adopted").GetBoolean();
        Assert.That(adopted, Is.False);
    }

    [Test]
    public async Task spawn_three_slots_returns_distinct_metadata()
    {
        string json_1 = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        string json_2 = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });
        string json_3 = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });

        foreach (string json in new List<string>() { json_1, json_2, json_3 })
        {
            JsonElement root = JsonAssert.Parse(json);
            string slotId = root.GetProperty("slotId").GetString() ?? string.Empty;
            Assert.That(string.IsNullOrEmpty(slotId), Is.False);

            string vers = root.GetProperty("version").GetString() ?? string.Empty;
            Assert.That(vers, Is.EqualTo("8"));

            bool adopted = root.GetProperty("adopted").GetBoolean();
            Assert.That(adopted, Is.False);
        }
    }

    // Round-trip: spawn produces a slotId that close_slot will accept.
    [Test]
    public async Task spawn_then_close_slot_round_trip()
    {
        string openJson = await _router.CallToolTextAsync("spawn_slot", new() { { "version", "8" } });

        JsonElement openElem = JsonAssert.Parse(openJson);
        string slotId = openElem.GetProperty("slotId").GetString() ?? string.Empty;
        Assert.That(string.IsNullOrEmpty(slotId), Is.False);

        string vers = openElem.GetProperty("version").GetString() ?? string.Empty;
        Assert.That(vers, Is.EqualTo("8"));

        bool adopted = openElem.GetProperty("adopted").GetBoolean();
        Assert.That(adopted, Is.False);

        string closeJson = await _router.CallToolTextAsync("close_slot", new() { { "slot", slotId } });
        JsonElement closeElem = JsonAssert.Parse(closeJson);
        Assert.That(closeElem.GetProperty("closed").GetBoolean(), Is.True);
    }
}

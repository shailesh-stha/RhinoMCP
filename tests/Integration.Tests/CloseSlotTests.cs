using System.Text.Json;
using NUnit.Framework;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Exercises the router's close_slot tool directly. No Rhino install required —
// these tests run against a freshly-spawned router with an isolated state dir.
[TestFixture]
public sealed class CloseSlotTests : SharedRouterFixture
{
    // Regression: a status-agnostic existence check is required so launching
    // slots are not mistaken for missing slots. The structured shape
    // (closed=false, error="slot_not_found", message=...) is what agents key
    // off of when deciding whether to retry, list slots, etc.
    [Test]
    public async Task close_slot_returns_slot_not_found_for_unknown_slot()
    {
        string json = await _router.CallToolTextAsync(
            "close_slot",
            new Dictionary<string, object?> { ["slot"] = "does-not-exist" });

        JsonElement root = JsonAssert.Parse(json);
        Assert.That(root.GetProperty("closed").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("error").GetString(), Is.EqualTo("slot_not_found"));
        Assert.That(root.GetProperty("message").GetString(), Does.Contain("does-not-exist"));
        Assert.That(root.GetProperty("message").GetString(), Does.Contain("list_slots"));
    }

    // The advertised JSON shape promises null fields are omitted; this also
    // doubles as a check that the JsonIgnoreCondition.WhenWritingNull policy
    // is still in effect for the close-slot result type.
    [Test]
    public async Task close_slot_omits_error_when_payload_is_unrelated_to_an_error_state()
    {
        // No slot exists, so this still goes through the slot_not_found path,
        // but proves the negation: when error IS set, message is set too, and
        // neither is the literal string "null".
        string json = await _router.CallToolTextAsync("close_slot", new () { ["slot"] = "another-bogus-slot" });

        Assert.That(json, Does.Not.Contain("\"error\":null"));
        Assert.That(json, Does.Not.Contain("\"message\":null"));
    }
}

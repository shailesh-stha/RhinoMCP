using System.Text.Json;
using NUnit.Framework;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Tests that need a live Rhino with the rh-mcp plugin loaded. The fixture
// spawns Rhino via the router's spawn_slot, so Rhino must be installed and
// licensed, and the freshly-built plugin must be loadable. Marked [Explicit]
// so `dotnet test` without filters skips them — opt in with
//   dotnet test --filter "Category=RequiresRhino"
// or by running a specific test by name in the IDE.
[TestFixture]
[Explicit("Spawns a real Rhino; opt in with --filter \"Category=RequiresRhino\".")]
[Category("RequiresRhino")]
internal sealed class PluginToolTests : SharedRouterFixture
{
    private string _slot = null!;

    // Base sets up the router; we then spawn a single slot shared across every
    // plugin-side test in the fixture.
    [OneTimeSetUp]
    public async Task SpawnSharedSlot()
    {
        string spawnJson = await _router.CallToolTextAsync("spawn_slot");
        JsonElement spawn = JsonAssert.Parse(spawnJson);
        if (spawn.TryGetProperty("error", out _))
        {
            Assert.Inconclusive(
                $"spawn_slot failed; cannot run plugin-side tests. Payload: {spawnJson}");
        }
        _slot = spawn.GetProperty("slotId").GetString()!;
    }

    // Runs before the base disposes the router.
    [OneTimeTearDown]
    public async Task CloseSharedSlot()
    {
        if (_slot is not null)
        {
            try
            {
                await _router.CallToolTextAsync(
                    "close_slot",
                    new Dictionary<string, object?> { ["slot"] = _slot });
            }
            catch { /* best effort */ }
        }
    }

    // Regression: a previous version of the filter stripped leading '_' / '-'
    // via TrimStart, which for the literal filter "_" produced an empty needle
    // and returned every registered command. The fix falls back to the
    // original filter when trimming empties it. Rhino command names never
    // contain '_', so a filter of "_" must return zero commands.
    [Test]
    public async Task get_commands_with_underscore_filter_does_not_return_every_command()
    {
        string text = await _router.CallToolTextAsync(
            "get_commands",
            new Dictionary<string, object?> { ["slot"] = _slot, ["filter"] = "_" });

        Assert.That(text, Does.Contain("No commands found"),
            $"Expected no matches for filter '_'; got:\n{text}");
    }

    // Sanity check that the trim-when-meaningful path still works. "_Box"
    // should be normalised to "Box" and find at least the Box command.
    [Test]
    public async Task get_commands_strips_leading_underscore_in_user_filter()
    {
        string text = await _router.CallToolTextAsync(
            "get_commands",
            new Dictionary<string, object?> { ["slot"] = _slot, ["filter"] = "_Box" });

        Assert.That(text, Does.Contain("Box"));
        Assert.That(text, Does.Not.StartWith("No commands found"));
    }

    // Regression: when a caller supplies a non-existent layer as the only
    // filter, the layer-index filter never gets set on ObjectEnumeratorSettings,
    // and the enumerator yields every object in the document. The fix
    // short-circuits the enumeration when the layer didn't resolve.
    [Test]
    public async Task set_selection_with_unresolved_layer_only_selects_nothing()
    {
        // Create a few objects so a select-all bug would be visible.
        await _router.CallToolTextAsync(
            "run_python",
            new Dictionary<string, object?>
            {
                ["slot"] = _slot,
                ["script"] = """
                    from Rhino.Geometry import Point3d, Line
                    doc = __rhino_doc__
                    for i in range(3):
                        doc.Objects.AddLine(Line(Point3d(i, 0, 0), Point3d(i, 1, 0)))
                    """,
            });

        string result = await _router.CallToolTextAsync(
            "set_selection",
            new Dictionary<string, object?>
            {
                ["slot"] = _slot,
                ["layer"] = "this-layer-does-not-exist",
            });

        Assert.That(result, Does.Contain("Selected 0 object(s)"));
        Assert.That(result, Does.Contain("Layer not found"));
    }

    // Regression: the script tools previously joined captured lines with "\n",
    // which produced doubled newlines because Rhino's CapturedCommandWindowStrings
    // already terminates each entry with '\n'. The fix concatenates without
    // injecting a separator. This test asserts the output between two prints
    // is a single newline rather than the legacy "\n\n".
    [Test]
    public async Task run_python_does_not_double_newlines_between_print_lines()
    {
        string json = await _router.CallToolTextAsync(
            "run_python",
            new Dictionary<string, object?>
            {
                ["slot"] = _slot,
                ["script"] = "print(\"first\")\nprint(\"second\")",
            });

        JsonElement root = JsonAssert.Parse(json);
        JsonElement content = root.GetProperty("content").EnumerateArray().ToArray()[0];
        string textJson = content.GetProperty("text").GetString()!;
        JsonElement text = JsonAssert.Parse(textJson);
        string stdout = text.GetProperty("stdout").GetString() ?? "";
        Assert.That(stdout, Does.Contain("first"));
        Assert.That(stdout, Does.Contain("second"));
        Assert.That(stdout, Does.Not.Contain("first\n\nsecond"),
            $"Expected at most a single newline between lines; stdout was:\n{stdout}");
    }
}

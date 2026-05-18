using System.Text.Json;
using NUnit.Framework;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Exercises open_doc / close_doc — the plugin-side document lifecycle tools.
// Marked [Explicit] because they require a real Rhino install.
[TestFixture]
[Explicit("Spawns a real Rhino; opt in with --filter \"Category=RequiresRhino\".")]
[Category("RequiresRhino")]
public sealed class DocLifecycleTests : SharedRouterFixture
{

    // TODO : Close without a path doesn't work on mac? Get's stuck waiting for a path
    // close_doc returns a plain string message ("Document closed without saving.",
    // etc.). The earlier version of this test asserted it returned an empty JSON
    // array, which never matched the contract in CloseDocTool.cs.
    [TestCase("8")]
    [TestCase("WIP")]
    public async Task close_doc_in_spawned_slot_returns_closed_message(string version)
    {
        _ = await _router.CallToolTextAsync("spawn_slot", new() { { "version", version } });

        string response = await _router.CallToolTextAsync("close_doc");

        Assert.That(response, Does.Contain("closed").IgnoreCase);
    }

    // TODO : close_doc with no active slot — semantics aren't pinned down yet
    // (does the router auto-spawn, no-op, or return a structured error?). The
    // earlier version asserted an empty JSON array, which never matched any
    // observed behaviour. Re-enable once the contract is decided.
    [Test]
    [Ignore("Pending: define router behaviour for close_doc with no active slot.")]
    public Task close_doc_with_no_active_slot_is_a_no_op()
    {
        return Task.CompletedTask;
    }

    // TODO : open_doc requires a `path` argument (see OpenDocTool.cs). The
    // earlier version of this test passed none, so it could only ever hit the
    // ArgumentException path. Add a real .3dm fixture under tests/Fixtures and
    // re-enable.
    [Test]
    [Ignore("Pending: add a .3dm fixture file and assert against the import response.")]
    public Task open_doc_imports_fixture_and_appears_in_list_slots()
    {
        return Task.CompletedTask;
    }
}

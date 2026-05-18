using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NUnit.Framework;
using RhMcp.Integration.Tests.Harness;

namespace RhMcp.Integration.Tests;

// Exercises the adoption path: when a user-started Rhino drops an announcement
// file in the listeners dir, the router should adopt it as a slot with
// `adopted=true`. close_slot on an adopted slot must refuse to kill the process
// and return a structured `cannot_close_adopted` payload.
//
// No real Rhino required — we fake the announcement by spinning up a TcpListener
// on the announced port (so IsPortListening returns true) and using the test
// process's own pid (so IsProcessAlive returns true). Adopted close-paths bail
// out before any kill, so reusing our own pid is safe.
[TestFixture]
public sealed class AdoptedSlotTests : RouterFixture
{
    [Test]
    public async Task announcement_with_listening_port_is_adopted_on_list_slots()
    {
        using FakeListener listener = FakeListener.Start();
        DropAnnouncement(version: "8", port: listener.Port, pid: Environment.ProcessId);

        string json = await _router.CallToolTextAsync("list_slots");
        JsonElement root = JsonAssert.Parse(json);

        Assert.That(root.GetArrayLength(), Is.EqualTo(1));
        JsonElement slot = root[0];
        Assert.That(slot.GetProperty("adopted").GetBoolean(), Is.True);
        Assert.That(slot.GetProperty("version").GetString(), Is.EqualTo("8"));
        Assert.That(slot.GetProperty("port").GetInt32(), Is.EqualTo(listener.Port));
        Assert.That(slot.GetProperty("pid").GetInt32(), Is.EqualTo(Environment.ProcessId));
        Assert.That(string.IsNullOrEmpty(slot.GetProperty("slotId").GetString()), Is.False);
    }

    [Test]
    public async Task close_slot_on_adopted_slot_returns_cannot_close_adopted()
    {
        using FakeListener listener = FakeListener.Start();
        DropAnnouncement(version: "WIP", port: listener.Port, pid: Environment.ProcessId);

        string listJson = await _router.CallToolTextAsync("list_slots");
        string slotId = JsonAssert.Parse(listJson)[0].GetProperty("slotId").GetString()!;

        string closeJson = await _router.CallToolTextAsync(
            "close_slot",
            new Dictionary<string, object?> { ["slot"] = slotId });
        JsonElement close = JsonAssert.Parse(closeJson);

        Assert.That(close.GetProperty("closed").GetBoolean(), Is.False);
        Assert.That(close.GetProperty("error").GetString(), Is.EqualTo("cannot_close_adopted"));
        Assert.That(close.GetProperty("message").GetString(), Does.Contain("Rhino window"));

        // The slot must still be present after a refused close — adoption is
        // sticky until the listener actually dies.
        string listAgain = await _router.CallToolTextAsync("list_slots");
        Assert.That(JsonAssert.Parse(listAgain).GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task duplicate_announcement_for_same_pid_port_does_not_create_two_slots()
    {
        using FakeListener listener = FakeListener.Start();
        DropAnnouncement(version: "8", port: listener.Port, pid: Environment.ProcessId);

        // First list_slots adopts; the file is then deleted by the router.
        _ = await _router.CallToolTextAsync("list_slots");

        // Plugin races and drops the same announcement again before noticing the
        // first one was already consumed. The (pid, port) dedupe in AdoptIfNew
        // must reject it.
        DropAnnouncement(version: "8", port: listener.Port, pid: Environment.ProcessId);
        string json = await _router.CallToolTextAsync("list_slots");

        Assert.That(JsonAssert.Parse(json).GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task announcement_with_dead_port_is_discarded_without_adoption()
    {
        // Pick a port, bind+release it so we know nothing is listening there.
        int deadPort = AllocateUnboundPort();
        DropAnnouncement(version: "8", port: deadPort, pid: Environment.ProcessId);

        string json = await _router.CallToolTextAsync("list_slots");
        Assert.That(JsonAssert.Parse(json).GetArrayLength(), Is.EqualTo(0));

        // And the doorbell file should have been deleted by the scan.
        Assert.That(Directory.GetFiles(_router.ListenersDir, "*.json"), Is.Empty);
    }

    private void DropAnnouncement(string version, int port, int pid)
    {
        Directory.CreateDirectory(_router.ListenersDir);
        string path = Path.Combine(_router.ListenersDir, $"ann-{Guid.NewGuid():N}.json");
        string body = JsonSerializer.Serialize(new
        {
            v = 1,
            pid,
            port,
            version,
        });
        File.WriteAllText(path, body);
    }

    private static int AllocateUnboundPort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed class FakeListener : IDisposable
    {
        private readonly TcpListener _listener;
        public int Port { get; }

        private FakeListener(TcpListener listener, int port)
        {
            _listener = listener;
            Port = port;
        }

        public static FakeListener Start()
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return new FakeListener(listener, port);
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* best effort */ }
        }
    }
}

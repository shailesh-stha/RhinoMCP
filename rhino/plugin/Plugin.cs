using Rhino.PlugIns;

namespace RhMcp;

public class RhMcpPlugin : PlugIn
{

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoDoc.BeginOpenDocument += Register;
        return base.OnLoad(ref errorMessage);
    }

    private void Register(object? sender, DocumentOpenEventArgs e)
    {
        RhinoDoc.BeginOpenDocument -= Register;

        int port = RhinoMcpHost.GetNextPort();
        RhinoMcpHost.StartOrRestart(e.Document, port);
    }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

}

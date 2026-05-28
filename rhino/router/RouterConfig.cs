namespace RhMcp.Router;

public record RouterConfig(string DefaultVersion, int StartupTimeoutSeconds = 120)
{
    public const int DefaultStartupTimeoutSeconds = 120;

    // Env var fallback for the startup timeout; the `--startup-timeout` CLI arg wins.
    public const string StartupTimeoutEnvVar = "RHINO_MCP_STARTUP_TIMEOUT";

    public static RouterConfig FromArgs(string[] args)
    {
        string defaultVersion = "8";
        int startupTimeoutSeconds = ReadStartupTimeoutFromEnv();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--default-version" || args[i] == "-v")
            {
                defaultVersion = args[i + 1];
            }
            else if (args[i] == "--startup-timeout")
            {
                if (int.TryParse(args[i + 1], out int parsed) && parsed > 0)
                {
                    startupTimeoutSeconds = parsed;
                }
            }
        }

        return new RouterConfig(defaultVersion, startupTimeoutSeconds);
    }

    private static int ReadStartupTimeoutFromEnv()
    {
        string? raw = Environment.GetEnvironmentVariable(StartupTimeoutEnvVar);
        if (int.TryParse(raw, out int parsed) && parsed > 0)
        {
            return parsed;
        }
        return DefaultStartupTimeoutSeconds;
    }
}

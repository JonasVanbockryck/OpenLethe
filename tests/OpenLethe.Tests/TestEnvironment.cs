using System.Runtime.CompilerServices;

// Keeps the suite hermetic. Runs before any test or WebApplicationFactory host is
// built, so Program.cs never reads the developer's .env: without this, whatever is
// in the repo-root .env (connection string, CLIENT_ID, FRONTEND_URL) silently
// becomes the configuration under test and the results depend on the machine.
internal static class TestEnvironment
{
    // MD_STARLIGHT=off: the server pins owned starlight to 99999 by default; the replay goldens
    // and round-trip tests compare against the captures' real 383.
    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("OPENLETHE_NO_DOTENV", "1");
        Environment.SetEnvironmentVariable("MD_STARLIGHT", "off");
    }
}

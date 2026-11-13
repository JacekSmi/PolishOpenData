using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace PolishOpenData.SmokeTests;

/// <summary>
/// Loads a git-ignored <c>.env</c> file from the repository root into the process environment before any live test runs, so
/// that your own keys (see <c>.env.example</c>) stay on your machine. Variables that are already set win, and CI has no such
/// file. Only the live tests read it; the libraries themselves never read environment variables or files.
/// </summary>
internal static class LocalEnvFile
{
    [ModuleInitializer]
    internal static void Load()
    {
        var path = Find();
        if (path is null)
        {
            return;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (line.Length == 0 || line[0] == '#' || eq <= 0)
            {
                continue;
            }

            var name = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            if (value.Length > 0 && Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    // Walks up from the test output folder to the folder that holds PolishOpenData.slnx; shortcut: no .env elsewhere.
    private static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PolishOpenData.slnx")))
            {
                var env = Path.Combine(dir.FullName, ".env");
                return File.Exists(env) ? env : null;
            }
        }

        return null;
    }
}

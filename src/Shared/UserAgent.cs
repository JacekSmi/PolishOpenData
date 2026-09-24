using System.Reflection;

namespace PolishOpenData.Internal;

/// <summary>The User-Agent sent to every registry, so operators can identify and contact the project.</summary>
internal static class UserAgent
{
    public static string Value { get; } = Create();

    private static string Create()
    {
        var version = typeof(UserAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = version.IndexOf('+');
        if (plus >= 0)
        {
            version = version.Substring(0, plus);
        }

        return "PolishOpenData/" + version + " (+https://github.com/JacekSmi/PolishOpenData)";
    }
}

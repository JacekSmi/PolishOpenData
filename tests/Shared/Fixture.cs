using System;
using System.IO;

namespace PolishOpenData.Tests.Shared;

/// <summary>Reads recorded upstream responses copied from tests/Fixtures to the test output folder.</summary>
internal static class Fixture
{
    public static string Read(string relativePath) => File.ReadAllText(PathOf(relativePath));

    /// <summary>The file's bytes exactly as stored (for recorded responses whose line endings and framing must not change).</summary>
    public static byte[] ReadBytes(string relativePath) => File.ReadAllBytes(PathOf(relativePath));

    private static string PathOf(string relativePath) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar));
}

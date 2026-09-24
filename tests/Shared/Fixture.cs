using System;
using System.IO;

namespace PolishOpenData.Tests.Shared;

/// <summary>Reads recorded upstream responses copied from tests/Fixtures to the test output folder.</summary>
internal static class Fixture
{
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar)));
}

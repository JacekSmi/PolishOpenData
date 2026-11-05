using System;
using System.Globalization;

namespace PolishOpenData.Demo.Tests;

internal static class CultureScope
{
    /// <summary>
    /// Runs <paramref name="action"/> with <see cref="CultureInfo.CurrentCulture"/> set to <paramref name="cultureName"/> ("" is the invariant culture).
    /// Only the current thread is touched, so tests that xunit runs in parallel do not see each other's culture.
    /// </summary>
    public static T Run<T>(string cultureName, Func<T> action)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

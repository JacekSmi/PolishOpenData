using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PolishOpenData.Tests.Shared;

/// <summary>Finds every non-empty <c>UnknownFields</c> extension-data dictionary in a model graph.</summary>
internal static class UnknownFields
{
    public static IReadOnlyList<string> Find(object? root)
    {
        var found = new List<string>();
        Walk(root, "$", found);
        return found;
    }

    private static void Walk(object? node, string path, List<string> found)
    {
        if (node is null || node is string || node is JsonElement || node is decimal || node is DateOnly ||
            node is DateTime || node is DateTimeOffset || node is Uri)
        {
            return;
        }

        var type = node.GetType();
        if (type.IsPrimitive || type.IsEnum)
        {
            return;
        }

        if (node is IEnumerable items)
        {
            var index = 0;
            foreach (var item in items)
            {
                Walk(item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", found);
                index++;
            }

            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var value = property.GetValue(node);
            if (property.Name == "UnknownFields")
            {
                if (value is IDictionary<string, JsonElement> extra)
                {
                    foreach (var key in extra.Keys)
                    {
                        found.Add(path + "." + key);
                    }
                }

                continue;
            }

            Walk(value, path + "." + property.Name, found);
        }
    }
}

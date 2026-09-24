using System.Collections.Generic;

namespace PolishOpenData.Mcp;

internal sealed record IdentifierValidationReport(string Input, IReadOnlyList<IdentifierValidation> Results);

internal sealed record IdentifierValidation
{
    public required string Kind { get; init; }

    public bool IsValid { get; init; }

    public string? Normalized { get; init; }

    public IReadOnlyList<string> Details { get; init; } = [];

    public IReadOnlyList<string> Suggestions { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

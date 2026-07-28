namespace Net7ClientManager.Addons.Development;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Registry;

public enum AddonDevelopmentDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record AddonDevelopmentDiagnostic
{
    public required AddonDevelopmentDiagnosticSeverity Severity { get; init; }

    public required string Message { get; init; }

    public int Line { get; init; }

    public int Column { get; init; }
}

public sealed record AddonDevelopmentValidationResult
{
    public bool Succeeded => this.Diagnostics.All(
        diagnostic => diagnostic.Severity !=
                      AddonDevelopmentDiagnosticSeverity.Error);

    public IReadOnlyList<AddonDevelopmentDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record AddonDevelopmentWorkspace
{
    /// <summary>
    /// Stable directory key used by the development workspace service.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Runtime addon id read from addon.json. Valid workspaces keep this equal
    /// to <see cref="Id" /> so source ownership cannot silently jump folders.
    /// </summary>
    public required string AddonId { get; init; }

    public required string Name { get; init; }

    public required string DirectoryPath { get; init; }

    public AddonManifest? Manifest { get; init; }

    public bool IsValid { get; init; }

    public string ValidationMessage { get; init; } = "";
}

public sealed record AddonDevelopmentDocument
{
    public required string RelativePath { get; init; }

    public required string FileName { get; init; }

    public required string Language { get; init; }

    public bool IsEntryPoint { get; init; }

    public bool IsGenerated { get; init; }

    public bool IsReadOnly { get; init; }
}

public sealed record AddonDevelopmentDocumentContent
{
    public required AddonDevelopmentWorkspace Workspace { get; init; }

    public required AddonDevelopmentDocument Document { get; init; }

    public required string Text { get; init; }
}

public sealed record AddonDevelopmentSaveResult
{
    public required AddonDevelopmentValidationResult Validation { get; init; }

    public bool Saved { get; init; }

    public string Error { get; init; } = "";
}

public sealed record AddonPublicationSourcePackage
{
    public required AddonManifest Manifest { get; init; }

    public required byte[] Bytes { get; init; }

    public required string Sha256 { get; init; }
}

public sealed record AddonPublicationResult
{
    public required AddonRegistryRelease Release { get; init; }

    public bool AlreadyPublished { get; init; }
}

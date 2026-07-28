namespace Net7ClientManager.Addons.Development;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lua;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Loading;

public sealed partial class AddonDevelopmentWorkspaceService
{
    public const string GeneratedApiStubRelativePath =
        ".net7-editor/net7-api.lua";

    public const string GeneratedApiReferenceRelativePath =
        ".net7-editor/NET7-API.md";

    private const int MaximumDocumentBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions readJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions writeJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string rootDirectory;
    private readonly AddonPackageReader packageReader = new();

    public AddonDevelopmentWorkspaceService(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory => this.rootDirectory;

    public IReadOnlyList<AddonDevelopmentWorkspace> GetWorkspaces()
    {
        Directory.CreateDirectory(this.rootDirectory);

        return Directory.EnumerateDirectories(this.rootDirectory)
            .Where(directory => !Path.GetFileName(directory).StartsWith(
                ".",
                StringComparison.Ordinal))
            .Select(this.CreateWorkspace)
            .OrderBy(workspace => workspace.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(workspace => workspace.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public AddonDevelopmentWorkspace CreateWorkspace(
        string addonId,
        string name,
        string? author = null)
    {
        addonId = addonId.Trim();
        name = name.Trim();

        var manifest = new AddonManifest
        {
            Id = addonId,
            Name = name,
            Version = "0.1.0",
            ApiVersion = AddonApiVersion.Current,
            EntryPoint = "main.lua",
            Description = "A locally developed Net7 Client Manager addon.",
            Author = string.IsNullOrWhiteSpace(author)
                ? null
                : author.Trim(),
            Activation = new AddonActivationOptions
            {
                Contexts = [AddonActivationContexts.InGame],
            },
        };

        var validationError = AddonManifestValidator.Validate(manifest);

        if (validationError != null)
        {
            throw new InvalidOperationException(validationError);
        }

        Directory.CreateDirectory(this.rootDirectory);
        var directory = this.ResolveWorkspaceDirectory(addonId);

        if (Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                "A development workspace with this addon id already exists.");
        }

        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, "addon.json"),
            JsonSerializer.Serialize(manifest, writeJsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        File.WriteAllText(
            Path.Combine(directory, "main.lua"),
            CreateStarterSource(name),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Directory.CreateDirectory(Path.Combine(directory, "lib"));
        return this.CreateWorkspace(directory);
    }

    public AddonDevelopmentWorkspace CreateWorkspaceFromPackage(
        string packagePath,
        string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var readResult = this.packageReader.ReadArchive(packagePath);
        var package = readResult.Package ??
                      throw new InvalidDataException(
                          readResult.Descriptor.Error);
        if (IsToolingPath(NormalizeRelativePath(package.Manifest.EntryPoint)))
        {
            throw new InvalidDataException(
                "The addon entry point uses the reserved .net7-editor directory.");
        }

        var directory = this.ResolveWorkspaceDirectory(package.Manifest.Id);

        if (Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                "A development workspace with this addon id already exists.");
        }

        Directory.CreateDirectory(this.rootDirectory);
        var stagingDirectory = Path.Combine(
            this.rootDirectory,
            string.Concat(
                ".net7-import-",
                Guid.NewGuid().ToString("N")));

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            using (var archive = ZipFile.OpenRead(packagePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var relativePath = NormalizeRelativePath(entry.FullName);

                    if (string.Equals(
                            relativePath,
                            "net7forge.json",
                            StringComparison.OrdinalIgnoreCase) ||
                        IsToolingPath(relativePath))
                    {
                        continue;
                    }

                    var destinationPath = EnsureContained(
                        stagingDirectory,
                        Path.Combine(
                            stagingDirectory,
                            relativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar)));
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(destinationPath)!);
                    entry.ExtractToFile(destinationPath, overwrite: false);
                }
            }

            var toolingDirectory = Path.Combine(
                stagingDirectory,
                ".net7-editor");
            Directory.CreateDirectory(toolingDirectory);
            File.WriteAllText(
                Path.Combine(toolingDirectory, "source.json"),
                JsonSerializer.Serialize(
                    new DevelopmentSourceMetadata
                    {
                        AddonId = package.Manifest.Id,
                        Version = package.Manifest.Version,
                        PackageSha256 = package.PackageSha256,
                        Source = source.Trim(),
                        ImportedAt = DateTimeOffset.UtcNow,
                    },
                    writeJsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Directory.Move(stagingDirectory, directory);
            return this.CreateWorkspace(directory);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    public void DeleteWorkspace(string workspaceId)
    {
        var directory = this.ResolveWorkspaceDirectory(workspaceId);

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public AddonDevelopmentWorkspace GetWorkspace(string workspaceId)
    {
        var directory = this.ResolveWorkspaceDirectory(workspaceId);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "The selected addon development workspace no longer exists.");
        }

        return this.CreateWorkspace(directory);
    }

    public IReadOnlyList<AddonDevelopmentDocument> GetDocuments(
        string workspaceId)
    {
        var workspace = this.GetWorkspace(workspaceId);
        var entryPoint = NormalizeRelativePath(
            workspace.Manifest?.EntryPoint ?? "");

        return Directory.EnumerateFiles(
                workspace.DirectoryPath,
                "*",
                SearchOption.AllDirectories)
            .Select(path => new
            {
                RelativePath = NormalizeRelativePath(
                    Path.GetRelativePath(workspace.DirectoryPath, path)),
            })
            .Where(item => IsEditorDocument(item.RelativePath))
            .OrderBy(item => string.Equals(
                    item.RelativePath,
                    "addon.json",
                    StringComparison.OrdinalIgnoreCase)
                ? 0
                : string.Equals(
                    item.RelativePath,
                    entryPoint,
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : IsGeneratedDocument(item.RelativePath)
                        ? 3
                        : 2)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(item => new AddonDevelopmentDocument
            {
                RelativePath = item.RelativePath,
                FileName = Path.GetFileName(item.RelativePath),
                Language = GetDocumentLanguage(item.RelativePath),
                IsEntryPoint = string.Equals(
                    item.RelativePath,
                    entryPoint,
                    StringComparison.OrdinalIgnoreCase),
                IsGenerated = IsGeneratedDocument(item.RelativePath),
                IsReadOnly = IsGeneratedDocument(item.RelativePath),
            })
            .ToArray();
    }

    public AddonDevelopmentDocumentContent ReadDocument(
        string workspaceId,
        string relativePath)
    {
        var workspace = this.GetWorkspace(workspaceId);
        var fullPath = this.ResolveEditorDocumentPath(workspace, relativePath);
        var info = new FileInfo(fullPath);

        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "The selected addon source file no longer exists.",
                fullPath);
        }

        if (info.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException(
                "The selected addon source file is too large for the editor.");
        }

        var normalizedPath = NormalizeRelativePath(
            Path.GetRelativePath(workspace.DirectoryPath, fullPath));
        var document = this.GetDocuments(workspaceId)
            .First(candidate => string.Equals(
                candidate.RelativePath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));

        return new AddonDevelopmentDocumentContent
        {
            Workspace = workspace,
            Document = document,
            Text = File.ReadAllText(fullPath),
        };
    }

    public AddonDevelopmentSaveResult SaveDocument(
        string workspaceId,
        string relativePath,
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var workspace = this.GetWorkspace(workspaceId);
        var fullPath = this.ResolveEditableDocumentPath(workspace, relativePath);
        var byteCount = Encoding.UTF8.GetByteCount(text);

        if (byteCount > MaximumDocumentBytes)
        {
            return new AddonDevelopmentSaveResult
            {
                Saved = false,
                Error = "The document exceeds the 2 MiB editor limit.",
                Validation = ErrorResult(
                    "The document exceeds the 2 MiB editor limit."),
            };
        }

        var validation = this.ValidateDocument(
            workspaceId,
            relativePath,
            text);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var toolingDirectory = this.GetToolingDirectory(workspace);
        Directory.CreateDirectory(toolingDirectory);
        var temporaryPath = Path.Combine(
            toolingDirectory,
            string.Concat(Path.GetRandomFileName(), ".tmp"));

        try
        {
            File.WriteAllText(
                temporaryPath,
                text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, fullPath, overwrite: true);

            return new AddonDevelopmentSaveResult
            {
                Saved = true,
                Validation = validation,
            };
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            return new AddonDevelopmentSaveResult
            {
                Saved = false,
                Error = ex.Message,
                Validation = validation,
            };
        }
    }

    public AddonDevelopmentValidationResult ValidateDocument(
        string workspaceId,
        string relativePath,
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var workspace = this.GetWorkspace(workspaceId);
        _ = this.ResolveEditableDocumentPath(workspace, relativePath);

        var extension = Path.GetExtension(relativePath);

        if (string.Equals(extension, ".lua", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateLua(text, relativePath);
        }

        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                    Path.GetFileName(relativePath),
                    "addon.json",
                    StringComparison.OrdinalIgnoreCase)
                ? this.ValidateManifest(workspaceId, text)
                : ValidateJson(text);
        }

        return ErrorResult("Only .lua and .json documents are supported.");
    }

    public AddonDevelopmentValidationResult ValidateWorkspace(
        string workspaceId)
    {
        List<AddonDevelopmentDiagnostic> diagnostics = [];
        var workspace = this.GetWorkspace(workspaceId);

        if (!workspace.IsValid)
        {
            diagnostics.Add(new AddonDevelopmentDiagnostic
            {
                Severity = AddonDevelopmentDiagnosticSeverity.Error,
                Message = string.IsNullOrWhiteSpace(workspace.ValidationMessage)
                    ? "The addon workspace is not loadable."
                    : workspace.ValidationMessage,
            });
        }

        foreach (var document in this.GetDocuments(workspaceId)
                     .Where(document => !document.IsReadOnly))
        {
            var content = this.ReadDocument(
                workspaceId,
                document.RelativePath);
            var result = this.ValidateDocument(
                workspaceId,
                document.RelativePath,
                content.Text);

            diagnostics.AddRange(result.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity !=
                    AddonDevelopmentDiagnosticSeverity.Information)
                .Select(diagnostic => diagnostic with
                {
                    Message = string.Concat(
                        document.RelativePath,
                        ": ",
                        diagnostic.Message),
                }));
        }

        if (diagnostics.Count == 0)
        {
            diagnostics.Add(new AddonDevelopmentDiagnostic
            {
                Severity = AddonDevelopmentDiagnosticSeverity.Information,
                Message = "Workspace validation succeeded.",
            });
        }

        return new AddonDevelopmentValidationResult
        {
            Diagnostics = diagnostics,
        };
    }

    public AddonPublicationSourcePackage BuildPublicationSourcePackage(
        string workspaceId)
    {
        var validation = this.ValidateWorkspace(workspaceId);

        if (!validation.Succeeded)
        {
            var errors = validation.Diagnostics
                .Where(diagnostic => diagnostic.Severity ==
                    AddonDevelopmentDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message)
                .Distinct(StringComparer.Ordinal)
                .Take(8);
            throw new InvalidOperationException(
                string.Concat(
                    "The addon workspace contains validation errors.",
                    Environment.NewLine,
                    Environment.NewLine,
                    string.Join(Environment.NewLine, errors)));
        }

        var workspace = this.GetWorkspace(workspaceId);
        var manifest = workspace.Manifest ??
            throw new InvalidOperationException(
                "The addon workspace does not contain a valid manifest.");
        var files = Directory
            .EnumerateFiles(
                workspace.DirectoryPath,
                "*",
                SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizeRelativePath(
                    Path.GetRelativePath(workspace.DirectoryPath, path)),
            })
            .Where(file => !IsToolingPath(file.RelativePath))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0 || files.Length >= 256)
        {
            throw new InvalidOperationException(
                "The addon workspace has an invalid number of publishable files.");
        }

        using var packageStream = new MemoryStream();
        var timestamp = new DateTimeOffset(
            1980,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);

        long expandedSize = 0;

        using (var archive = new ZipArchive(
                   packageStream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var file in files)
            {
                if (!IsSupportedDocument(file.RelativePath) ||
                    string.Equals(
                        file.RelativePath,
                        "net7forge.json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        string.Concat(
                            "The addon workspace contains a file that cannot be published: ",
                            file.RelativePath));
                }

                var info = new FileInfo(file.FullPath);

                if (info.Length > MaximumDocumentBytes)
                {
                    throw new InvalidOperationException(
                        string.Concat(
                            "The addon source file is too large: ",
                            file.RelativePath));
                }

                var text = File.ReadAllText(
                    file.FullPath,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true));
                var normalized = text
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n');

                if (!normalized.EndsWith('\n'))
                {
                    normalized = string.Concat(normalized, "\n");
                }

                var bytes = Encoding.UTF8.GetBytes(normalized);
                expandedSize += bytes.LongLength;

                if (expandedSize > 8L * 1024 * 1024)
                {
                    throw new InvalidOperationException(
                        "The expanded addon publication package is too large.");
                }

                var entry = archive.CreateEntry(
                    file.RelativePath,
                    CompressionLevel.Optimal);
                entry.LastWriteTime = timestamp;

                using var entryStream = entry.Open();
                entryStream.Write(bytes);
            }
        }

        var packageBytes = packageStream.ToArray();

        if (packageBytes.Length == 0 ||
            packageBytes.LongLength > 16L * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "The addon publication package has an invalid size.");
        }

        return new AddonPublicationSourcePackage
        {
            Manifest = manifest,
            Bytes = packageBytes,
            Sha256 = Convert.ToHexStringLower(
                SHA256.HashData(packageBytes)),
        };
    }

    public string WriteGeneratedApiStub(string workspaceId)
    {
        var workspace = this.GetWorkspace(workspaceId);
        var toolingDirectory = this.GetToolingDirectory(workspace);
        Directory.CreateDirectory(toolingDirectory);
        var path = Path.Combine(
            workspace.DirectoryPath,
            GeneratedApiStubRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        File.WriteAllText(
            path,
            AddonApiCatalog.GenerateEmmyLuaStub(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public string WriteGeneratedApiReference(string workspaceId)
    {
        var workspace = this.GetWorkspace(workspaceId);
        var toolingDirectory = this.GetToolingDirectory(workspace);
        Directory.CreateDirectory(toolingDirectory);
        var path = Path.Combine(
            workspace.DirectoryPath,
            GeneratedApiReferenceRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        File.WriteAllText(
            path,
            AddonApiCatalog.GenerateMarkdownReference(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static AddonDevelopmentValidationResult ValidateLua(
        string text,
        string relativePath)
    {
        const int wrapperLineCount = 1;
        var wrapped = string.Concat(
            "return function(...)\n",
            text,
            "\nend");

        try
        {
            using var state = LuaState.Create();
            _ = state.Load(
                chunk: wrapped,
                chunkName: string.Concat("@editor/", relativePath));

            return SuccessResult("Lua syntax is valid.");
        }
        catch (Exception ex)
        {
            var line = TryExtractLine(ex.Message);

            if (line > 0)
            {
                line = Math.Max(1, line - wrapperLineCount);
            }

            return new AddonDevelopmentValidationResult
            {
                Diagnostics =
                [
                    new AddonDevelopmentDiagnostic
                    {
                        Severity = AddonDevelopmentDiagnosticSeverity.Error,
                        Message = CleanLuaError(ex.Message),
                        Line = line,
                        Column = 1,
                    },
                ],
            };
        }
    }

    private AddonDevelopmentValidationResult ValidateManifest(
        string workspaceId,
        string text)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<AddonManifest>(
                text,
                readJsonOptions);
            var validationError = AddonManifestValidator.Validate(manifest);

            if (validationError != null)
            {
                return ErrorResult(validationError);
            }

            var workspace = this.GetWorkspace(workspaceId);

            if (!string.Equals(
                    manifest!.Id,
                    workspace.Id,
                    StringComparison.Ordinal))
            {
                return ErrorResult(
                    string.Concat(
                        "id must match the workspace folder '",
                        workspace.Id,
                        "'."));
            }

            var entryPoint = NormalizeRelativePath(manifest.EntryPoint);

            if (!string.Equals(
                    Path.GetExtension(entryPoint),
                    ".lua",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ErrorResult(
                    "entryPoint must resolve to a .lua file inside the workspace.");
            }

            var entryPointPath = this.ResolveEditableDocumentPath(
                workspace,
                entryPoint);

            if (!File.Exists(entryPointPath))
            {
                return ErrorResult(
                    string.Concat(
                        "Entry point '",
                        manifest.EntryPoint,
                        "' does not exist."));
            }

            return SuccessResult("addon.json is valid.");
        }
        catch (JsonException ex)
        {
            return new AddonDevelopmentValidationResult
            {
                Diagnostics =
                [
                    new AddonDevelopmentDiagnostic
                    {
                        Severity = AddonDevelopmentDiagnosticSeverity.Error,
                        Message = ex.Message,
                        Line = checked((int)(ex.LineNumber ?? 0) + 1),
                        Column = checked((int)(ex.BytePositionInLine ?? 0) + 1),
                    },
                ],
            };
        }
    }

    private static AddonDevelopmentValidationResult ValidateJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(
                text,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            return SuccessResult("JSON syntax is valid.");
        }
        catch (JsonException ex)
        {
            return new AddonDevelopmentValidationResult
            {
                Diagnostics =
                [
                    new AddonDevelopmentDiagnostic
                    {
                        Severity = AddonDevelopmentDiagnosticSeverity.Error,
                        Message = ex.Message,
                        Line = checked((int)(ex.LineNumber ?? 0) + 1),
                        Column = checked((int)(ex.BytePositionInLine ?? 0) + 1),
                    },
                ],
            };
        }
    }

    private AddonDevelopmentWorkspace CreateWorkspace(string directory)
    {
        var readResult = this.packageReader.ReadDirectory(directory);
        var manifest = readResult.Descriptor.Manifest;
        var workspaceId = Path.GetFileName(directory);
        var addonId = manifest?.Id ?? workspaceId;
        var identityMatches = string.Equals(
            workspaceId,
            addonId,
            StringComparison.Ordinal);
        var validationMessage = !readResult.Descriptor.IsValid
            ? readResult.Descriptor.Error
            : identityMatches
                ? ""
                : string.Concat(
                    "addon.json id '",
                    addonId,
                    "' must match workspace folder '",
                    workspaceId,
                    "'.");

        return new AddonDevelopmentWorkspace
        {
            Id = workspaceId,
            AddonId = addonId,
            Name = manifest?.Name ?? workspaceId,
            DirectoryPath = directory,
            Manifest = manifest,
            IsValid = readResult.Descriptor.IsValid && identityMatches,
            ValidationMessage = validationMessage,
        };
    }

    private string GetToolingDirectory(
        AddonDevelopmentWorkspace workspace)
    {
        return EnsureContained(
            workspace.DirectoryPath,
            Path.Combine(workspace.DirectoryPath, ".net7-editor"));
    }

    private string ResolveWorkspaceDirectory(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException(
                "A development workspace id is required.",
                nameof(workspaceId));
        }

        if (workspaceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            workspaceId.Contains(Path.DirectorySeparatorChar) ||
            workspaceId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException(
                "The development workspace id is not a valid directory name.");
        }

        return EnsureContained(
            this.rootDirectory,
            Path.Combine(this.rootDirectory, workspaceId));
    }

    private string ResolveEditorDocumentPath(
        AddonDevelopmentWorkspace workspace,
        string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);

        if (!IsEditorDocument(normalized))
        {
            throw new InvalidOperationException(
                "Only addon source and generated API documents can be opened in the editor.");
        }

        return EnsureContained(
            workspace.DirectoryPath,
            Path.Combine(
                workspace.DirectoryPath,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
    }

    private string ResolveEditableDocumentPath(
        AddonDevelopmentWorkspace workspace,
        string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);

        if (!IsSupportedDocument(normalized) ||
            IsToolingPath(normalized))
        {
            throw new InvalidOperationException(
                "Only .lua and .json source documents inside the workspace can be edited.");
        }

        return EnsureContained(
            workspace.DirectoryPath,
            Path.Combine(
                workspace.DirectoryPath,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string EnsureContained(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);

        if (!fullCandidate.StartsWith(
                fullRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The requested path escapes the addon development workspace.");
        }

        return fullCandidate;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static bool IsToolingPath(string path)
    {
        var normalized = NormalizeRelativePath(path);
        return normalized.StartsWith(
            ".net7-editor/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".lua", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEditorDocument(string path)
    {
        return IsGeneratedDocument(path) ||
               (IsSupportedDocument(path) && !IsToolingPath(path));
    }

    private static bool IsGeneratedDocument(string path)
    {
        var normalized = NormalizeRelativePath(path);
        return string.Equals(
                   normalized,
                   GeneratedApiStubRelativePath,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   GeneratedApiReferenceRelativePath,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDocumentLanguage(string path)
    {
        var extension = Path.GetExtension(path);

        if (string.Equals(extension, ".lua", StringComparison.OrdinalIgnoreCase))
        {
            return "lua";
        }

        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        return "markdown";
    }

    private static AddonDevelopmentValidationResult SuccessResult(
        string message)
    {
        return new AddonDevelopmentValidationResult
        {
            Diagnostics =
            [
                new AddonDevelopmentDiagnostic
                {
                    Severity = AddonDevelopmentDiagnosticSeverity.Information,
                    Message = message,
                },
            ],
        };
    }

    private static AddonDevelopmentValidationResult ErrorResult(string message)
    {
        return new AddonDevelopmentValidationResult
        {
            Diagnostics =
            [
                new AddonDevelopmentDiagnostic
                {
                    Severity = AddonDevelopmentDiagnosticSeverity.Error,
                    Message = message,
                },
            ],
        };
    }

    private static int TryExtractLine(string message)
    {
        var match = LuaLinePattern().Match(message);
        return match.Success &&
               int.TryParse(match.Groups[1].Value, out var line)
            ? line
            : 0;
    }

    private static string CleanLuaError(string message)
    {
        var separator = message.IndexOf(": ", StringComparison.Ordinal);
        return separator >= 0 && separator + 2 < message.Length
            ? message[(separator + 2)..]
            : message;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateStarterSource(string name)
    {
        return string.Concat(
            "-- ", name, "\n",
            "-- Ctrl+Space opens completion. F8 validates. Ctrl+Shift+S saves and reloads.\n\n",
            "addon.on_load(function()\n",
            "    addon.log.info(\"", EscapeLuaString(name), " loaded\")\n",
            "end)\n\n",
            "game.events.on(\"world.location_changed\", function(event)\n",
            "    local sector = game.world.sector_name or \"Unknown sector\"\n",
            "    addon.log.info(\"Location changed: \" .. sector)\n",
            "end)\n");
    }

    private static string EscapeLuaString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private sealed record DevelopmentSourceMetadata
    {
        public int FormatVersion { get; init; } = 1;

        public required string AddonId { get; init; }

        public required string Version { get; init; }

        public required string PackageSha256 { get; init; }

        public required string Source { get; init; }

        public required DateTimeOffset ImportedAt { get; init; }
    }

    [GeneratedRegex(@":(\d+):", RegexOptions.CultureInvariant)]
    private static partial Regex LuaLinePattern();
}

namespace Net7ClientManager.Addons.Runtime.LuaCSharp;

using Lua;

internal sealed class InMemoryAddonModuleLoader(
    IReadOnlyDictionary<string, string> modules)
    : ILuaModuleLoader
{
    public bool Exists(string moduleName)
    {
        return IsValidModuleName(moduleName) &&
               modules.ContainsKey(moduleName);
    }

    public ValueTask<LuaModule> LoadAsync(
        string moduleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValidModuleName(moduleName) ||
            !modules.TryGetValue(moduleName, out var source))
        {
            throw new InvalidOperationException(
                $"Addon module '{moduleName}' is not available.");
        }

        return ValueTask.FromResult(
            new LuaModule(
                string.Concat("@addon/", moduleName),
                source));
    }

    private static bool IsValidModuleName(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName) ||
            moduleName.Length > 128 ||
            moduleName.Contains("..", StringComparison.Ordinal) ||
            moduleName.Contains('/', StringComparison.Ordinal) ||
            moduleName.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        return moduleName.All(
            character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or '-');
    }
}


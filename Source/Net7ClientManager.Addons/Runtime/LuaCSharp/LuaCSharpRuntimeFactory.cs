namespace Net7ClientManager.Addons.Runtime.LuaCSharp;

using Net7ClientManager.Addons.Contracts;

public sealed class LuaCSharpRuntimeFactory : ILuaRuntimeFactory
{
    public ILuaRuntime Create(AddonRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new LuaCSharpRuntime(options);
    }
}


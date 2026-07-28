namespace Net7ClientManager.Addons.Runtime;

using Net7ClientManager.Addons.Contracts;

public interface ILuaRuntimeFactory
{
    ILuaRuntime Create(AddonRuntimeOptions options);
}


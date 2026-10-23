using System.Reflection;
using HarmonyLib;
using VRage.Plugins;

// Define assembly version when compiled by Pulsar
#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "FairServerList";

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    public void Init(object gameInstance)
    {
        new Harmony(Name).PatchAll(Assembly.GetExecutingAssembly());
    }

    public void Dispose()
    {
        // Do NOT call harmony.UnpatchAll() here! It may break other plugins.
    }

    public void Update() { }
}

using System.ComponentModel;
using P3R.ShaderPreload.Template.Configuration;
using Reloaded.Mod.Interfaces.Structs;
using System.ComponentModel.DataAnnotations;

namespace P3R.ShaderPreload.Configuration;

/// <summary>What the mod does about Persona 3 Reload's shader compilation stutter.</summary>
public enum ShaderMode
{
    /// <summary>Compile the game's shaders before its first D3D12 device is created (recommended).</summary>
    Precompile = 0,

    /// <summary>Deploy the async-compilation shim to the game folder; it takes effect on the next launch.</summary>
    Async = 1,
}

public class Config : Configurable<Config>
{
    [DisplayName("Shader mode")]
    [Description("Precompile: compile the game's shaders before it starts (a compile console shows progress). " +
                 "Async: install the asynchronous shader-compilation shim (graphical glitches possible; takes effect on the next launch).")]
    [DefaultValue(ShaderMode.Precompile)]
    [Category("Shaders")]
    public ShaderMode Mode { get; set; } = ShaderMode.Precompile;

    [DisplayName("Precompile threads")]
    [Description("Worker threads for the precompile. 0 picks a number from the CPU and GPU automatically.")]
    [DefaultValue(0)]
    [Category("Precompile")]
    public int PrecompileThreads { get; set; } = 0;

    [DisplayName("Precompile timeout (minutes)")]
    [Description("Stop waiting for the precompile after this long and let the game start. 0 waits until the compile ends " +
                 "or its console is closed (closing the console skips the rest of the compile).")]
    [DefaultValue(0)]
    [Category("Precompile")]
    public int PrecompileTimeoutMinutes { get; set; } = 0;

    [DisplayName("Show compile console")]
    [Description("Show the SCSKiller compile console while shaders are compiled. When off, the compile runs hidden and its " +
                 "progress goes to the Reloaded log instead (closing a hidden compile's console is not possible).")]
    [DefaultValue(true)]
    [Category("Precompile")]
    public bool ShowCompileConsole { get; set; } = true;
}

/// <summary>
/// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
/// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
/// </summary>
public class ConfiguratorMixin : ConfiguratorMixinBase
{
    //
}

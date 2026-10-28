using System.ComponentModel;
using P3R.ShaderPreload.Template.Configuration;
using Reloaded.Mod.Interfaces.Structs;
using System.ComponentModel.DataAnnotations;

namespace P3R.ShaderPreload.Configuration;

public class Config : Configurable<Config>
{
    [DisplayName("Asynchronous Shader Compilation")]
    [Description("Prevents stuttering when a new shader needs to be rendered. This can cause graphical glitches but prevents stuttering.")]
    [DefaultValue(true)]
    [Category("Shaders")]
    public bool AsyncShaders { get; set; } = true;

    [DisplayName("Precompile shaders")]
    [Description("Precompile shaders if they are not compiled. This prevents stutters at the cost of ~4GB of disk space and time to compile shaders.")]
    [DefaultValue(true)]
    [Category("Shaders")]
    public bool ShaderPrecompilation { get; set; } = true;
}

/// <summary>
/// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
/// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
/// </summary>
public class ConfiguratorMixin : ConfiguratorMixinBase
{
    // 
}
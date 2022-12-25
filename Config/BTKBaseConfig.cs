using System;

namespace BTKSAImmersiveHud.Config;

public interface IBTKBaseConfig
{
    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public Type Type { get; }
    public string DialogMessage { get; }
    
}
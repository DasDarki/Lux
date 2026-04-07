namespace Lux.Configuration;

public sealed class ScriptsSection
{
    public List<string> PreBuild { get; set; } = [];

    public List<string> PostBuild { get; set; } = [];

    internal void Merge(ScriptsSection section)
    {
        if (section.PreBuild.Count > 0) PreBuild = section.PreBuild;
        if (section.PostBuild.Count > 0) PostBuild = section.PostBuild;
    }
}

using System.Linq;
using System.Xml.Linq;
using Nuke.Common.ProjectModel;

namespace Nuke.Build.Custom.Helpers;

public static class ProjectEnumerationExtensions
{
    public static bool IsPackable(this Project project)
    {
        var projDefinition = XDocument.Load(project.Path);
        var properties = projDefinition
            .Element("Project")?
            .Elements("PropertyGroup")
            .Descendants()
            .Select(x => new { x.Name, x.Value });

        return properties is not null && properties.Any(x => x.Name == "IsPackable" && x.Value == "true" || x.Name == "PackageId");
    }
}
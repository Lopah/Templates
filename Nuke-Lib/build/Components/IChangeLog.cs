using JetBrains.Annotations;
using Nuke.Common;

namespace Nuke.Build.Custom.Components;

[PublicAPI]
public interface IChangeLog : INukeBuild
{
    string ChangeLogFile => RootDirectory / "CHANGELOG.md";
}
using JetBrains.Annotations;
using Nuke.Common;
using Nuke.Common.IO;

namespace Nuke.Build.Custom;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class Paths
{
    public static readonly AbsolutePath SourceDirectory = NukeBuild.RootDirectory / "src";
    public static readonly AbsolutePath NukeDirectory = NukeBuild.RootDirectory / "build";

    public static readonly AbsolutePath TestsDirectory = NukeBuild.RootDirectory / "tests";

    public static readonly AbsolutePath ArtifactsDirectory = NukeDirectory / "artifacts";
    public static readonly AbsolutePath BinDirectory = ArtifactsDirectory / "bin";
    public static readonly AbsolutePath OutputDirectory = ArtifactsDirectory / "output";
}
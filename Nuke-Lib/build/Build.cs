using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Build.Custom.Paths;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

namespace Nuke.Build.Custom;

public partial class Build : NukeBuild
{
    static Environment _environment = Environment.Undefined;

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [GitRepository] readonly GitRepository GitRepository;

    [GitVersion] readonly GitVersion GitVersion;

    [Solution] readonly Solution Solution;

    private string _personalAccessToken;

    [Parameter("Environment to use for dotnet tasks")]
    public static Environment DotNetEnvironment { get; private set; } = Environment.Undefined;

    [Parameter]
    public static Environment Environment
    {
        get
        {
            if (!_environment.Equals(Environment.Undefined)) return _environment;

            if (!DotNetEnvironment.Equals(Environment.Undefined)) return _environment;

            return Environment.Development;
        }
        set
        {
            if (value is null)
            {
                _environment = Environment.Undefined;
                return;
            }

            _environment = value;
        }
    }

    [Parameter]
    [Secret]
    string PersonalAccessToken
    {
        get
        {
            if (string.IsNullOrEmpty(_personalAccessToken))
                _personalAccessToken = Settings.GitHubSettings.GithubAccessToken;

            return _personalAccessToken;
        }
        set => _personalAccessToken = value;
    }

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Before(Compile)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProcessWorkingDirectory(SourceDirectory)
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            var projects = Solution.GetProjects("*.Application.*").ToList();

            Logger.Info($"Found {projects.Count()} projects");
            if (projects is null)
                throw new InvalidOperationException(
                    "Compilation of projects failed, could not find them. Verify the source directory pointer.");

            projects.ForEach(project =>
            {
                DotNetBuild(s => s
                    .SetProcessWorkingDirectory(project.Directory)
                    .SetOutputDirectory(BinDirectory / project.Name)
                    .SetConfiguration(Configuration)
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion)
                    .EnableNoRestore());
            });
        });


    Target UnitTests => _ => _
        .Description("Runs unit tests for the solution")
        .DependsOn(Compile)
        .Executes(() =>
        {
            TestsDirectory.GlobFiles("*.csproj").ForEach(testProject =>
            {
                DotNetTest(s => s
                    .SetProjectFile(testProject)
                    .SetConfiguration(Configuration.Debug));
            });
        });

    /// <summary>
    ///     If possible, don't release new version of library without testing client project that works with it
    /// </summary>
    Target Pack => _ => _
        .Description("Packs the shared library in this project to be then shared via NuGet")
        .DependsOn(Compile, Changelog)
        .DependsOn(UnitTests)
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(Solution.GetProject("Portal.Application.Shared"))
                .SetOutputDirectory(OutputDirectory)
                .SetConfiguration(Configuration.Release)
                .SetVersion(GitVersion.NuGetVersionV2)
                .SetPackageReleaseNotes(SourceDirectory / "CHANGELOG.md")
                .EnableNoRestore());

            DotNetPack(s => s
                .SetProject(Solution.GetProject("Portal.Application.Client"))
                .SetOutputDirectory(OutputDirectory)
                .SetConfiguration(Configuration.Release)
                .SetVersion(GitVersion.NuGetVersionV2)
                .SetPackageReleaseNotes(SourceDirectory / "CHANGELOG.md")
                .EnableNoRestore());
        });

    public static int Main() => Execute<Build>(x => x.Compile);

    T From<T>()
        where T : INukeBuild
        => (T)(object)this;
}
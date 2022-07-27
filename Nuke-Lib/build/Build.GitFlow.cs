using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Nuke.Build.Custom.Components;
using Nuke.CoberturaConverter;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotCover;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.ReportGenerator;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using Nuke.GitHub;
using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.Tools.DotCover.DotCoverTasks;
using static Nuke.CodeGeneration.CodeGenerator;
using static Nuke.CoberturaConverter.CoberturaConverterTasks;
using static Nuke.Common.Tools.ReportGenerator.ReportGeneratorTasks;
using static Nuke.Common.Tools.Git.GitTasks;
using static Nuke.Common.Tools.GitVersion.GitVersionTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Build.Custom.Git.Branch;
using static Nuke.Build.Custom.Paths;
using static Nuke.GitHub.GitHubTasks;

namespace Nuke.Build.Custom;

public partial class Build
{
    [Parameter] readonly bool AutoStash = true;

    string MajorMinorPatchVersion => GitVersion.MajorMinorPatch;

    Target Changelog => _ => _
        .Unlisted()
        .OnlyWhenStatic(() => GitRepository.IsOnReleaseBranch() || GitRepository.IsOnHotfixBranch())
        .Executes(() =>
        {
            var changeLogFile = From<IChangeLog>().ChangeLogFile;
            FinalizeChangelog(changeLogFile, MajorMinorPatchVersion, GitRepository);
            Logger.Info("Please review CHANGELOG.md and press any key to continue...");
            Console.ReadKey();

            GitTasks.Git($"add {changeLogFile}");
            GitTasks.Git($"commit -m \"Finalize {Path.GetFileName(changeLogFile)} for {MajorMinorPatchVersion}\"");
        });


    [PublicAPI]
    Target Release => _ => _
        .DependsOn(Changelog)
        .Requires(() => !GitRepository.IsOnReleaseBranch() || GitHasCleanWorkingCopy())
        .Executes(() =>
        {
            if (!GitRepository.IsOnReleaseBranch())
            {
                Checkout($"{ReleaseBranchPrefix}/{MajorMinorPatchVersion}", DevelopBranch);
                return;
            }

            FinishReleaseOrHotfix();
        });

    [PublicAPI]
    Target Hotfix => _ => _
        .DependsOn(Changelog)
        .Requires(() => !GitRepository.IsOnHotfixBranch() || GitHasCleanWorkingCopy())
        .Executes(() =>
        {
            var masterVersion = GitVersion(s => s
                .SetFramework("net6.0")
                .SetUrl(RootDirectory)
                .SetBranch(MasterBranch)
                .EnableNoFetch()
                .DisableProcessLogOutput()).Result;

            if (!GitRepository.IsOnHotfixBranch())
            {
                Checkout($"{HotfixBranch}/{masterVersion.Major}.{masterVersion.Minor}.{masterVersion.Patch + 1}",
                    MasterBranch);
                return;
            }

            FinishReleaseOrHotfix();
        });

    [PublicAPI]
    Target PublishGitHubRelease => _ => _
        .DependsOn(Pack)
        .Requires(() => PersonalAccessToken)
        .OnlyWhenDynamic(() => GitRepository.IsOnReleaseBranch() || GitRepository.IsOnMainOrMasterBranch() ||
                               GitRepository.IsOnHotfixBranch() || true)
        .Executes<Task>(async () =>
        {
            Logger.Info("Started creating release.");
            var releaseTag = $"v{GitVersion.MajorMinorPatch}";

            var changeLogSectionEntries = ExtractChangelogSectionNotes(From<IChangeLog>().ChangeLogFile);
            var latestChangeLog = changeLogSectionEntries
                .Aggregate((c, n) => c + System.Environment.NewLine + n);

            var completeChangeLog = $"## {releaseTag}" + System.Environment.NewLine + latestChangeLog;

            var (gitHubOwner, repositoryName) = GetGitHubRepositoryInfo(GitRepository);
            var nugetPackages = OutputDirectory.GlobFiles("*.nupkg").NotEmpty().Select(x => x.ToString()).ToArray();

            await PublishRelease(conf => conf
                .SetArtifactPaths(nugetPackages)
                .SetCommitSha(GitVersion.Sha)
                .SetReleaseNotes(completeChangeLog)
                .SetRepositoryName(repositoryName)
                .SetRepositoryOwner(gitHubOwner)
                .SetTag(releaseTag)
                .SetToken(PersonalAccessToken)
                .DisablePrerelease()
            );
        });

    [PublicAPI]
    Target Push => _ => _
        .DependsOn(Pack)
        .OnlyWhenStatic(() => !string.IsNullOrEmpty(Settings.GitHubSettings.GithubSource))
        .Requires(() => PersonalAccessToken)
        .Executes(() =>
        {
            Logger.Info("Running push to packages directory.");
            GlobFiles(OutputDirectory, "*.nupkg").NotEmpty()
                .Where(x => !x.EndsWith("symbols.nupkg"))
                .ForEach(x =>
                {
                    DotNetNuGetPush(s => s
                        .SetTargetPath(x)
                        .SetSource(Settings.GitHubSettings.GithubSource)
                        .SetApiKey(PersonalAccessToken)
                    );
                });
        });


    [PublicAPI]
    Target Generate => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            GenerateCode("", _ => SourceDirectory / "Nuke.CoberturaConverter");
        });

    [PublicAPI]
    Target Coverage => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            var testProjects = TestsDirectory.GlobFiles("*est*.csproj").ToList();
            if (testProjects.Count == 0)
                throw new InvalidOperationException("Could not run test coverage since you have no tests defined.");

            testProjects.ForEach((testProject, index) =>
            {
                var projectDirectory = Path.GetDirectoryName(testProject);
                var dotnetPath = ToolPathResolver.GetPathExecutable("dotnet");
                var snapshotIndex = index;

                var xUnitOutputDirectory = OutputDirectory / $"$test_{snapshotIndex:00}.testresults";
                DotCoverCover(c => c
                    .SetTargetExecutable(dotnetPath)
                    .SetTargetWorkingDirectory(projectDirectory)
                    .SetTargetArguments($"xunit -nobuild -xml {xUnitOutputDirectory.ToString().DoubleQuoteIfNeeded()}")
                    .SetFilters("+CoberturaConverter.Core")
                    .SetAttributeFilters("System.CodeDom.Compiler.GeneratedCodeAttribute")
                    .SetOutputFile(OutputDirectory / $"coverage{snapshotIndex:00}.snapshot")
                );
            });

            var snapshots = testProjects.Select((_, index) => OutputDirectory / $"coverage{index:00}.snapshot")
                .Select(p => p.ToString())
                .Aggregate((c, n) => c + ";" + n);

            DotCoverMerge(c => c
                .SetSource(snapshots)
                .SetOutputFile(OutputDirectory / "coverage.snapshot"));

            DotCoverReport(c => c
                .SetSource(OutputDirectory / "coverage.snapshot")
                .SetOutputFile(OutputDirectory / "coverage.xml")
                .SetReportType(DotCoverReportType.DetailedXml));


            // Jenkins report
            ReportGenerator(c => c
                .SetReports(OutputDirectory / "coverage.xml")
                .SetTargetDirectory(OutputDirectory / "CoverageReport"));


            // Cobertura format that looks good in Jenkins dashboard
            await DotCoverToCobertura(s => s
                .SetInputFile(OutputDirectory / "coverage.xml")
                .SetOutputFile(OutputDirectory / "cobertura_coverage.xml"));
        });

    void Checkout(string branch, string start)
    {
        var hasCleanWorkingCopy = GitHasCleanWorkingCopy();

        if (!hasCleanWorkingCopy && AutoStash) GitTasks.Git("stash");

        GitTasks.Git($"checkout -b {branch} {start}");

        if (!hasCleanWorkingCopy && AutoStash) GitTasks.Git("stash apply");
    }

    void FinishReleaseOrHotfix()
    {
        GitTasks.Git($"checkout {MasterBranch}");
        GitTasks.Git($"merge --no-ff --no-edit {GitRepository.Branch}");
        GitTasks.Git($"tag {MajorMinorPatchVersion}");

        GitTasks.Git($"checkout {DevelopBranch}");
        GitTasks.Git($"merge --no-ff --no-edit {GitRepository.Branch}");

        GitTasks.Git($"branch -D {GitRepository.Branch}");

        GitTasks.Git($"push origin {MasterBranch} {DevelopBranch} {MajorMinorPatchVersion}");
    }
}
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Nuke.Build.Custom.Git;
using Nuke.Build.Custom.Helpers;
using static Nuke.Build.Custom.Paths;

namespace Nuke.Build.Custom;

public static class Settings
{
    static Settings()
    {
        var configurationRoot = GetConfigurationRoot(Build.Environment);
        GitHubSettings = configurationRoot.GetSection(nameof(GitHubSettings)).Get<GitHubSettings>();
    }

    public static GitHubSettings GitHubSettings { get; }

    static IConfigurationRoot GetConfigurationRoot(Environment environment)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile(NukeDirectory / "settings.json", true, false)
            .AddJsonFile(NukeDirectory / $"settings.{environment.ValueToLower()}.json", true, false)
            .AddEnvironmentVariables();

        if (environment.Equals(Environment.Development))
            configurationBuilder.AddUserSecrets(Assembly.GetExecutingAssembly());

        configurationBuilder.AddEnvironmentVariables();

        return configurationBuilder.Build();
    }
}
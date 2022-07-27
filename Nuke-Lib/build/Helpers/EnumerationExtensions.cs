using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Nuke.Common.Tooling;

namespace Nuke.Build.Custom.Helpers;

[UsedImplicitly]
[ExcludeFromCodeCoverage]
public static class EnumerationExtensions
{
    public static string ValueToLower(this Enumeration enumeration) => enumeration.ToString().ToLower();
}
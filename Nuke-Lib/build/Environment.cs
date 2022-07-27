using System.ComponentModel;
using Nuke.Common.Tooling;

namespace Nuke.Build.Custom;

[TypeConverter(typeof(TypeConverter<Environment>))]
public class Environment : Enumeration
{
    public static Environment Undefined = new() { Value = nameof(Undefined) };
    public static Environment Development = new() { Value = nameof(Development) };
    public static Environment Test = new() { Value = nameof(Test) };
    public static Environment Production = new() { Value = nameof(Production) };

    public static bool EnvironmentIs(Environment environment) => ((object)Build.Environment).Equals(environment);

    public static implicit operator string(Environment environment) => environment.Value;
}
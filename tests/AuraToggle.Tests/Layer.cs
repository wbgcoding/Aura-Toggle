using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace AuraToggle.Tests;

/// <summary>
/// The groups the suite runs in, one trait per test class: <c>-- --filter-trait "Layer=Logic"</c>
/// runs only that group. Logic covers files, settings, presets and the protocol, UI covers what the
/// window draws and says, Integration whole command lines and switches against fake controllers.
/// </summary>
public static class Layer
{
    public const string Name = "Layer";
    public const string Logic = "Logic";
    public const string UI = "UI";
    public const string Integration = "Integration";

    public static readonly string[] All = [Logic, UI, Integration];
}

[Trait(Layer.Name, Layer.Logic)]
public sealed class LayerTests
{
    [Fact]
    public void EveryTestClassBelongsToExactlyOneGroup()
    {
        string[] stray = typeof(LayerTests).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.GetMethods().Any(IsTest))
            .Where(type =>
            {
                string[] layers = type.GetCustomAttributes<TraitAttribute>(inherit: true)
                    .Where(trait => trait.Name == Layer.Name)
                    .Select(trait => trait.Value)
                    .ToArray();
                return layers.Length != 1 || !Layer.All.Contains(layers[0]);
            })
            .Select(type => type.Name)
            .ToArray();

        Assert.True(stray.Length == 0, "Without exactly one known Layer trait: " + string.Join(", ", stray));
    }

    private static bool IsTest(MethodInfo method) =>
        method.GetCustomAttributes<FactAttribute>(inherit: true).Any();
}

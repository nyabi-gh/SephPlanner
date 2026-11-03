using SephPlanner.Plugin.Host;

namespace SephPlanner.Tests;

public sealed class ShortcutMatchTests
{
    public enum Name { K, RightAlt, AltGr, LeftShift, W, Mouse0 }

    private enum Key { K, RightAlt, LeftShift, W }

    private static readonly Dictionary<Name, Key> Physical = new()
    {
        [Name.K] = Key.K,
        [Name.RightAlt] = Key.RightAlt,
        [Name.AltGr] = Key.RightAlt,
        [Name.LeftShift] = Key.LeftShift,
        [Name.W] = Key.W,
    };

    private static readonly Key[] Keys = Enum.GetValues<Key>();

    private static bool IsDown(Name main, Name[] modifiers, params Key[] held) =>
        ShortcutMatch.IsDown(main, modifiers, Physical, Keys, key => key == Physical[main], held.Contains);

    [Theory]
    [InlineData(Name.RightAlt)]
    [InlineData(Name.AltGr)]
    public void EitherNameOfOnePhysicalKeyWorksAsModifier(Name modifier)
    {
        Assert.True(IsDown(Name.K, [modifier], Key.K, Key.RightAlt));
        Assert.True(IsDown(Name.K, [Name.RightAlt, Name.AltGr], Key.K, Key.RightAlt));
    }

    [Fact]
    public void MissingModifierOrExtraKeyBlocks()
    {
        Assert.False(IsDown(Name.K, [Name.RightAlt], Key.K));
        Assert.False(IsDown(Name.K, [Name.RightAlt], Key.K, Key.RightAlt, Key.LeftShift));
        Assert.False(IsDown(Name.K, [], Key.K, Key.W));
        Assert.True(IsDown(Name.K, [], Key.K));
    }

    [Fact]
    public void UnmappedModifierNeverCountsAsHeld() =>
        Assert.False(IsDown(Name.K, [Name.Mouse0], Key.K));
}

using SephPlanner.DataTool;

namespace SephPlanner.Tests;

public sealed class GameLocatorTests
{
    [Fact]
    public void MacGameUsesBundleDataDirectory()
    {
        var game = Directory.CreateTempSubdirectory();
        try
        {
            var data = Path.Combine(game.FullName, "Sephiria.app", "Contents", "Resources", "Data");
            Directory.CreateDirectory(data);

            Assert.Equal(Path.Combine(data, "StreamingAssets", "Localization"), GameLocator.LocalizationDir(game.FullName));
            Assert.Equal(Path.Combine(data, "Managed"), GameLocator.ManagedDir(game.FullName));
        }
        finally
        {
            game.Delete(true);
        }
    }

    [Fact]
    public void WindowsGameKeepsItsDataDirectory()
    {
        var game = Directory.CreateTempSubdirectory();
        try
        {
            Assert.Equal(Path.Combine(game.FullName, "Sephiria_Data", "StreamingAssets", "Localization"),
                GameLocator.LocalizationDir(game.FullName));
            Assert.Equal(Path.Combine(game.FullName, "Sephiria_Data", "Managed"), GameLocator.ManagedDir(game.FullName));
        }
        finally
        {
            game.Delete(true);
        }
    }
}

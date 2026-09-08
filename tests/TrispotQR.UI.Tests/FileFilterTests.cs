using TrispotQR.UI.Services;

namespace TrispotQR.UI.Tests;

public class FileFilterTests
{
    [Fact]
    public void ReadsASingleNameAndPatternPair()
    {
        var types = FileFilter.Parse("PNG image|*.png");

        var type = Assert.Single(types);
        Assert.Equal("PNG image", type.Name);
        Assert.Equal(["*.png"], type.Patterns);
    }

    [Fact]
    public void ReadsSeveralPairs()
    {
        // The exact string the logo picker passes today.
        var types = FileFilter.Parse(
            "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|PNG (best, supports transparency)|*.png|All files|*.*");

        Assert.Equal(3, types.Count);
        Assert.Equal("Images", types[0].Name);
        Assert.Equal(["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp"], types[0].Patterns);
        Assert.Equal("All files", types[2].Name);
        Assert.Equal(["*.*"], types[2].Patterns);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no separator at all")]
    public void AMalformedFilterYieldsNothingRatherThanThrowing(string filter)
    {
        // A picker with no file types still opens and still saves. Throwing here would turn a
        // cosmetic problem into the save button doing nothing, which is far worse.
        Assert.Empty(FileFilter.Parse(filter));
    }
}

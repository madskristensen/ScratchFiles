using System.Linq;

namespace ScratchFiles.Test;

[TestClass]
public sealed class GeneralOptionsCustomFoldersTests
{
    [TestMethod]
    public void GetCustomFolders_WhenEmptyOrNull_ReturnsEmpty()
    {
        var options = new GeneralOptions { CustomFolders = string.Empty };
        Assert.IsEmpty(options.GetCustomFolders());

        options.CustomFolders = null!;
        Assert.IsEmpty(options.GetCustomFolders());

        options.CustomFolders = "   \r\n  \t  ";
        Assert.IsEmpty(options.GetCustomFolders());
    }

    [TestMethod]
    public void GetCustomFolders_SplitsOnNewlinesAndTrims()
    {
        var options = new GeneralOptions
        {
            CustomFolders = "  C:\\one\r\nC:\\two\n  C:\\three  "
        };

        var result = options.GetCustomFolders().ToArray();

        CollectionAssert.AreEqual(
            new[] { "C:\\one", "C:\\two", "C:\\three" },
            result);
    }

    [TestMethod]
    public void GetCustomFolders_DeduplicatesCaseInsensitively()
    {
        var options = new GeneralOptions
        {
            CustomFolders = "C:\\Foo\nC:\\foo\nC:\\FOO\nC:\\Bar"
        };

        var result = options.GetCustomFolders().ToArray();

        Assert.HasCount(2, result);
        Assert.AreEqual("C:\\Foo", result[0]);
        Assert.AreEqual("C:\\Bar", result[1]);
    }

    [TestMethod]
    public void GetCustomFolders_SkipsEmptyLines()
    {
        var options = new GeneralOptions
        {
            CustomFolders = "C:\\one\n\n\r\n   \r\nC:\\two\n"
        };

        var result = options.GetCustomFolders().ToArray();

        CollectionAssert.AreEqual(new[] { "C:\\one", "C:\\two" }, result);
    }

    [TestMethod]
    public void SetCustomFolders_WithNull_ClearsValue()
    {
        var options = new GeneralOptions { CustomFolders = "C:\\foo" };

        options.SetCustomFolders(null);

        Assert.AreEqual(string.Empty, options.CustomFolders);
        Assert.IsEmpty(options.GetCustomFolders());
    }

    [TestMethod]
    public void SetCustomFolders_RoundTrips()
    {
        var options = new GeneralOptions();

        options.SetCustomFolders(new[] { "C:\\one", "  C:\\two  ", "", "  ", "C:\\three" });

        var result = options.GetCustomFolders().ToArray();

        CollectionAssert.AreEqual(
            new[] { "C:\\one", "C:\\two", "C:\\three" },
            result);
    }

    [TestMethod]
    public void SetCustomFolders_DeduplicatesOnWrite()
    {
        var options = new GeneralOptions();

        options.SetCustomFolders(new[] { "C:\\Foo", "C:\\foo", "C:\\Bar", "C:\\BAR" });

        var result = options.GetCustomFolders().ToArray();

        Assert.HasCount(2, result);
        Assert.AreEqual("C:\\Foo", result[0]);
        Assert.AreEqual("C:\\Bar", result[1]);
    }
}

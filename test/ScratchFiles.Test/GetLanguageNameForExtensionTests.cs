using ScratchFiles.Services;

namespace ScratchFiles.Test;

[TestClass]
public sealed class GetLanguageNameForExtensionTests
{
    [TestMethod]
    [DataRow(".cs", "CSharp")]
    [DataRow(".vb", "Basic")]
    [DataRow(".json", "JSON")]
    [DataRow(".xml", "XML")]
    [DataRow(".html", "HTML")]
    [DataRow(".sql", "SQL")]
    [DataRow(".ps1", "PowerShell")]
    [DataRow(".md", "Markdown")]
    [DataRow(".yaml", "YAML")]
    [DataRow(".ts", "TypeScript")]
    [DataRow(".js", "JavaScript")]
    [DataRow(".css", "CSS")]
    public void WhenExtensionIsKnownThenReturnsLanguageName(string extension, string expectedName)
    {
        string result = LanguageDetectionService.GetLanguageNameForExtension(extension);

        Assert.AreEqual(expectedName, result);
    }

    [TestMethod]
    [DataRow(".CS")]
    [DataRow(".Cs")]
    [DataRow(".JSON")]
    public void WhenExtensionDiffersByCaseThenStillMatches(string extension)
    {
        string result = LanguageDetectionService.GetLanguageNameForExtension(extension);

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void WhenExtensionIsUnknownThenReturnsNull()
    {
        string result = LanguageDetectionService.GetLanguageNameForExtension(".xyz");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void WhenExtensionIsNullThenReturnsNull()
    {
        string result = LanguageDetectionService.GetLanguageNameForExtension(null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void WhenExtensionIsEmptyThenReturnsNull()
    {
        string result = LanguageDetectionService.GetLanguageNameForExtension(string.Empty);

        Assert.IsNull(result);
    }
}

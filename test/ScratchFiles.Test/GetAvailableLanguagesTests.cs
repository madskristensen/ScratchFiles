using ScratchFiles.Services;

namespace ScratchFiles.Test;

[TestClass]
public sealed class GetAvailableLanguagesTests
{
    [TestMethod]
    public void WhenCalledThenReturnsNonEmptyList()
    {
        IReadOnlyList<LanguageOption> languages = LanguageDetectionService.GetAvailableLanguages();

        Assert.IsNotEmpty(languages);
    }

    [TestMethod]
    public void WhenCalledThenContainsPlainText()
    {
        IReadOnlyList<LanguageOption> languages = LanguageDetectionService.GetAvailableLanguages();

        Assert.IsTrue(
            languages.Any(l => l.DisplayName == "Plain Text" && l.Extension == ".txt"),
            "Available languages should include Plain Text");
    }

    [TestMethod]
    public void WhenCalledThenContainsCSharp()
    {
        IReadOnlyList<LanguageOption> languages = LanguageDetectionService.GetAvailableLanguages();

        Assert.IsTrue(
            languages.Any(l => l.DisplayName == "C#" && l.Extension == ".cs"),
            "Available languages should include C#");
    }

    [TestMethod]
    public void WhenCalledThenAllEntriesHaveDisplayNameAndExtension()
    {
        IReadOnlyList<LanguageOption> languages = LanguageDetectionService.GetAvailableLanguages();

        foreach (LanguageOption language in languages)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(language.DisplayName),
                $"DisplayName should not be empty for extension {language.Extension}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(language.Extension),
                $"Extension should not be empty for language {language.DisplayName}");
            Assert.StartsWith(".",
language.Extension, $"Extension '{language.Extension}' should start with a dot");
        }
    }

    [TestMethod]
    public void WhenCalledThenToStringReturnsDisplayName()
    {
        IReadOnlyList<LanguageOption> languages = LanguageDetectionService.GetAvailableLanguages();
        LanguageOption first = languages[0];

        Assert.AreEqual(first.DisplayName, first.ToString());
    }
}

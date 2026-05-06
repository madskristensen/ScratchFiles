using ScratchFiles.Services;

namespace ScratchFiles.Test;

[TestClass]
public sealed class ScratchFileServiceShadowTests
{
    [TestMethod]
    public void GetShadowPath_AppendsUnsavedExtension()
    {
        Assert.AreEqual(@"C:\foo\bar.cs.unsaved", ScratchFileService.GetShadowPath(@"C:\foo\bar.cs"));
        Assert.AreEqual(@"C:\foo\bar.unsaved", ScratchFileService.GetShadowPath(@"C:\foo\bar"));
    }

    [TestMethod]
    public void IsShadowFile_RecognizesUnsavedExtension()
    {
        Assert.IsTrue(ScratchFileService.IsShadowFile(@"C:\foo\bar.cs.unsaved"));
        Assert.IsTrue(ScratchFileService.IsShadowFile(@"C:\foo\bar.UNSAVED"));
        Assert.IsTrue(ScratchFileService.IsShadowFile(@"file.unsaved"));
    }

    [TestMethod]
    public void IsShadowFile_RejectsNonShadowFiles()
    {
        Assert.IsFalse(ScratchFileService.IsShadowFile(@"C:\foo\bar.cs"));
        Assert.IsFalse(ScratchFileService.IsShadowFile(@"C:\foo\bar.unsavedx"));
        Assert.IsFalse(ScratchFileService.IsShadowFile(string.Empty));
        Assert.IsFalse(ScratchFileService.IsShadowFile(null));
    }

    [TestMethod]
    public void GetOriginalPathFromShadow_StripsUnsavedSuffix()
    {
        Assert.AreEqual(@"C:\foo\bar.cs", ScratchFileService.GetOriginalPathFromShadow(@"C:\foo\bar.cs.unsaved"));
        Assert.AreEqual(@"C:\foo\bar.cs", ScratchFileService.GetOriginalPathFromShadow(@"C:\foo\bar.cs.UNSAVED"));
    }

    [TestMethod]
    public void GetOriginalPathFromShadow_ReturnsInputForNonShadow()
    {
        Assert.AreEqual(@"C:\foo\bar.cs", ScratchFileService.GetOriginalPathFromShadow(@"C:\foo\bar.cs"));
    }

    [TestMethod]
    public void GetShadowPath_RoundTripsViaGetOriginalPathFromShadow()
    {
        const string original = @"C:\folder\scratch1.scratch";
        string shadow = ScratchFileService.GetShadowPath(original);
        Assert.AreEqual(original, ScratchFileService.GetOriginalPathFromShadow(shadow));
    }

    [TestMethod]
    public void GetDisplayName_ReturnsFileNameForRegularFile()
    {
        Assert.AreEqual("bar.cs", ScratchFileService.GetDisplayName(@"C:\foo\bar.cs"));
        Assert.AreEqual("scratch1.scratch", ScratchFileService.GetDisplayName(@"C:\foo\scratch1.scratch"));
    }

    [TestMethod]
    public void GetDisplayName_StripsUnsavedExtensionForShadowFile()
    {
        Assert.AreEqual("bar.cs", ScratchFileService.GetDisplayName(@"C:\foo\bar.cs.unsaved"));
        Assert.AreEqual("scratch1.scratch", ScratchFileService.GetDisplayName(@"C:\foo\scratch1.scratch.unsaved"));
    }

    [TestMethod]
    public void GetDisplayName_HandlesShadowExtensionCaseInsensitively()
    {
        Assert.AreEqual("bar.cs", ScratchFileService.GetDisplayName(@"C:\foo\bar.cs.UNSAVED"));
    }
}

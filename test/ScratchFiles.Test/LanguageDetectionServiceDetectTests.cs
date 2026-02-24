using ScratchFiles.Services;

namespace ScratchFiles.Test;

[TestClass]
public sealed class LanguageDetectionServiceDetectTests
{
    [TestMethod]
    public void WhenContentIsNullThenReturnsNull()
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void WhenContentIsEmptyThenReturnsNull()
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(string.Empty);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void WhenContentIsWhitespaceThenReturnsNull()
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect("   \t\n  ");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void WhenContentMatchesNoPatternThenReturnsNull()
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect("hello world, just some plain text");

        Assert.IsNull(result);
    }

    [TestMethod]
    [DataRow("using System;\nnamespace Foo\n{\n    public class Bar { }\n}", "CSharp", ".cs")]
    [DataRow("using System.Linq;", "CSharp", ".cs")]
    [DataRow("namespace MyApp\n{", "CSharp", ".cs")]
    [DataRow("public class MyClass\n{", "CSharp", ".cs")]
    [DataRow("[assembly: AssemblyVersion(\"1.0\")]", "CSharp", ".cs")]
    public void WhenContentIsCSharpThenDetectsCSharp(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("Imports System\nModule MyModule\nEnd Module", "Basic", ".vb")]
    [DataRow("Public Class MyClass\nEnd Class", "Basic", ".vb")]
    [DataRow("Dim x As Integer", "Basic", ".vb")]
    public void WhenContentIsVisualBasicThenDetectsBasic(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("{\n  \"name\": \"test\"\n}", "JSON", ".json")]
    [DataRow("[{\"id\": 1}]", "JSON", ".json")]
    public void WhenContentIsJsonThenDetectsJson(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("<?xml version=\"1.0\" encoding=\"utf-8\"?>", "XML", ".xml")]
    [DataRow("<root xmlns=\"http://example.com\">", "XML", ".xml")]
    public void WhenContentIsXmlThenDetectsXml(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("<!DOCTYPE html>\n<html>", "HTML", ".html")]
    [DataRow("<html lang=\"en\">", "HTML", ".html")]
    public void WhenContentIsHtmlThenDetectsHtml(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("SELECT * FROM Users WHERE Id = 1", "SQL", ".sql")]
    [DataRow("CREATE TABLE Foo (Id INT)", "SQL", ".sql")]
    [DataRow("DECLARE @x INT", "SQL", ".sql")]
    public void WhenContentIsSqlThenDetectsSql(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("$variable = 'hello'\nfunction Get-Thing {\n}", "PowerShell", ".ps1")]
    [DataRow("Get-ChildItem -Path C:\\", "PowerShell", ".ps1")]
    [DataRow("param(\n  [string]$Name\n)", "PowerShell", ".ps1")]
    public void WhenContentIsPowerShellThenDetectsPowerShell(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("# My Heading\n\nSome text [link](http://example.com)", "Markdown", ".md")]
    public void WhenContentIsMarkdownThenDetectsMarkdown(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("---\nname: value\nother: data", "YAML", ".yaml")]
    public void WhenContentIsYamlThenDetectsYaml(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("import { Component } from '@angular/core';\nexport interface Foo {\n  name: string;\n}", "TypeScript", ".ts")]
    public void WhenContentIsTypeScriptThenDetectsTypeScript(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow("const express = require('express');\nvar app = express();", "JavaScript", ".js")]
    [DataRow("let x = 10;\nconst y = 20;\nfunction greet(name) {\n  return 'Hello ' + name;\n}", "JavaScript", ".js")]
    [DataRow("module.exports = { run };", "JavaScript", ".js")]
    public void WhenContentIsJavaScriptThenDetectsJavaScript(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    [DataRow(".container {\n  display: flex;\n}", "CSS", ".css")]
    [DataRow("@media screen and (max-width: 600px) {", "CSS", ".css")]
    public void WhenContentIsCssThenDetectsCss(string content, string expectedName, string expectedExtension)
    {
        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedName, result.LanguageName);
        Assert.AreEqual(expectedExtension, result.Extension);
    }

    [TestMethod]
    public void WhenMultiplePatternsMatchThenConfidenceReflectsMatchCount()
    {
        string content = "using System;\nnamespace Foo\n{\n    public class Bar { }\n}";

        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual("CSharp", result.LanguageName);
        Assert.IsGreaterThan(1, result.Confidence, "Multiple pattern matches should yield confidence > 1");
    }

    [TestMethod]
    public void WhenContentExceeds2000CharsThenOnlySampleIsUsed()
    {
        // Place a C# signature beyond the 2000-char boundary
        string padding = new string(' ', 2100);
        string content = padding + "using System;";

        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNull(result, "Patterns beyond the 2000-char sample window should not match");
    }

    [TestMethod]
    public void WhenCSharpWithinSampleWindowThenDetected()
    {
        string content = "using System;\n" + new string(' ', 1900);

        LanguageDetectionResult result = LanguageDetectionService.Detect(content);

        Assert.IsNotNull(result);
        Assert.AreEqual("CSharp", result.LanguageName);
    }
}

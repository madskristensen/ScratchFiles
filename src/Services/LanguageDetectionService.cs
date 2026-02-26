using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ScratchFiles.Services
{
    /// <summary>
    /// Detects programming language from file content using simple heuristics.
    /// Returns a suggestion that the user can accept or dismiss via the InfoBar.
    /// </summary>
    internal static class LanguageDetectionService
    {
        private static readonly List<LanguageRule> _rules = new List<LanguageRule>
        {
            new LanguageRule("CSharp", ".cs", new[]
            {
                @"^\s*using\s+System",
                @"^\s*namespace\s+\w",
                @"^\s*(public|private|internal|protected)\s+(class|struct|interface|enum|record)\s",
                @"^\s*\[assembly:",
            }),
            new LanguageRule("Basic", ".vb", new[]
            {
                @"^\s*Imports\s+System",
                @"^\s*Module\s+\w",
                @"^\s*(Public|Private|Friend)\s+(Class|Structure|Interface|Enum|Sub|Function)\s",
                @"^\s*Dim\s+\w+\s+As\s",
            }),
            new LanguageRule("JSON", ".json", new[]
            {
                @"^\s*\{[\s\r\n]*""",
                @"^\s*\[[\s\r\n]*\{",
            }),
            new LanguageRule("XML", ".xml", new[]
            {
                @"^\s*<\?xml\s",
                @"^\s*<\w+[^>]*xmlns\s*=",
            }),
            new LanguageRule("HTML", ".html", new[]
            {
                @"^\s*<!DOCTYPE\s+html",
                @"^\s*<html[\s>]",
            }),
            new LanguageRule("SQL", ".sql", new[]
            {
                @"^\s*(SELECT|INSERT|UPDATE|DELETE|CREATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE)\s",
                @"^\s*DECLARE\s+@",
                @"^\s*EXEC(\s+|UTE\s+)",
            }, RegexOptions.IgnoreCase),
            new LanguageRule("PowerShell", ".ps1", new[]
            {
                @"^\s*\$\w+\s*=",
                @"^\s*function\s+\w",
                @"^\s*param\s*\(",
                @"^\s*(Get|Set|New|Remove|Import|Export)-\w+",
            }),
            new LanguageRule("Markdown", ".md", new[]
            {
                @"^#{1,6}\s+\w",
                @"^\s*[-*]\s+\w.*\n\s*[-*]\s+\w",
                @"\[.+\]\(.+\)",
            }),
            new LanguageRule("YAML", ".yaml", new[]
            {
                @"^---\s*$",
                @"^\w[\w\s]*:\s+\S",
            }),
            new LanguageRule("TypeScript", ".ts", new[]
            {
                @"^\s*import\s+.*\s+from\s+['""]",
                @"^\s*(export\s+)?(interface|type|enum)\s+\w",
                @":\s*(string|number|boolean|any)\s*[;,=\)]",
            }),
            new LanguageRule("JavaScript", ".js", new[]
            {
                @"^\s*const\s+\w+\s*=\s*require\(",
                @"^\s*(var|let|const)\s+\w+\s*=",
                @"^\s*function\s+\w+\s*\(",
                @"^\s*module\.exports\s*=",
            }),
            new LanguageRule("CSS", ".css", new[]
            {
                @"^\s*[\.\#\w][\w\-]*\s*\{",
                @"^\s*@(media|import|keyframes|font-face)\s",
            }),
        };

        /// <summary>
        /// Attempts to detect the language of the given content.
        /// Returns null if no confident match is found.
        /// </summary>
        public static LanguageDetectionResult Detect(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            // Use the first ~2000 characters for detection
            string sample = content.Length > 2000 ? content.Substring(0, 2000) : content;

            LanguageDetectionResult bestMatch = null;
            int bestScore = 0;

            foreach (LanguageRule rule in _rules)
            {
                int score = 0;

                foreach (Regex pattern in rule.CompiledPatterns)
                {
                    if (pattern.IsMatch(sample))
                    {
                        score++;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = new LanguageDetectionResult(rule.LanguageName, rule.Extension, score);
                }
            }

            // Require at least one pattern match
            return bestScore >= 1 ? bestMatch : null;
        }

        /// <summary>
        /// Returns the well-known VS language service name for a file extension.
        /// </summary>
        public static string GetLanguageNameForExtension(string extension)
        {
            foreach (LanguageRule rule in _rules)
            {
                if (string.Equals(rule.Extension, extension, System.StringComparison.OrdinalIgnoreCase))
                {
                    return rule.LanguageName;
                }
            }

            return null;
        }

        private static readonly IReadOnlyList<LanguageOption> _availableLanguages = new List<LanguageOption>
        {
            new LanguageOption("Plain Text", ".txt"),
            new LanguageOption("C#", ".cs"),
            new LanguageOption("Visual Basic", ".vb"),
            new LanguageOption("JSON", ".json"),
            new LanguageOption("XML", ".xml"),
            new LanguageOption("HTML", ".html"),
            new LanguageOption("CSS", ".css"),
            new LanguageOption("JavaScript", ".js"),
            new LanguageOption("TypeScript", ".ts"),
            new LanguageOption("SQL", ".sql"),
            new LanguageOption("PowerShell", ".ps1"),
            new LanguageOption("Markdown", ".md"),
            new LanguageOption("YAML", ".yaml"),
            new LanguageOption("XAML", ".xaml"),
            new LanguageOption("Razor", ".razor"),
            new LanguageOption("Bicep", ".bicep"),
        }.AsReadOnly();

        /// <summary>
        /// Returns available languages for the language picker UI.
        /// </summary>
        public static IReadOnlyList<LanguageOption> GetAvailableLanguages()
        {
            return _availableLanguages;
        }
    }

    internal sealed class LanguageRule
    {
        public LanguageRule(string languageName, string extension, string[] patterns)
            : this(languageName, extension, patterns, RegexOptions.None)
        {
        }

        public LanguageRule(string languageName, string extension, string[] patterns, RegexOptions additionalOptions)
        {
            LanguageName = languageName;
            Extension = extension;

            // Pre-compile patterns at construction time to avoid compilation overhead during detection
            RegexOptions options = RegexOptions.Compiled | RegexOptions.Multiline | additionalOptions;
            CompiledPatterns = new Regex[patterns.Length];
            for (int i = 0; i < patterns.Length; i++)
            {
                CompiledPatterns[i] = new Regex(patterns[i], options);
            }
        }

        public string LanguageName { get; }
        public string Extension { get; }
        public Regex[] CompiledPatterns { get; }
    }

    internal sealed class LanguageDetectionResult
    {
        public LanguageDetectionResult(string languageName, string extension, int confidence)
        {
            LanguageName = languageName;
            Extension = extension;
            Confidence = confidence;
        }

        public string LanguageName { get; }
        public string Extension { get; }
        public int Confidence { get; }
    }

    internal sealed class LanguageOption
    {
        public LanguageOption(string displayName, string extension)
        {
            DisplayName = displayName;
            Extension = extension;
        }

        public string DisplayName { get; }
        public string Extension { get; }

        public override string ToString() => DisplayName;
    }
}

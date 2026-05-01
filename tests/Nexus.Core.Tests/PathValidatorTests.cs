using Nexus.Connectors;
using Nexus.Core.Config;
using System.Reflection;

namespace Nexus.Core.Tests;

public class PathValidatorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _tempDocsDir;
    private readonly string _tempModelDir;
    private readonly string _tempFile;

    public PathValidatorTests()
    {
        // Create a temp directory structure for testing
        _tempRoot = Path.Combine(Path.GetTempPath(), "nexus_test_" + Guid.NewGuid().ToString("N")[..8]);
        _tempDocsDir = Path.Combine(_tempRoot, "ecomerce", "docs");
        _tempModelDir = Path.Combine(_tempDocsDir, "model");
        Directory.CreateDirectory(_tempModelDir);
        _tempFile = Path.Combine(_tempRoot, "scrum_plan.md");
        File.WriteAllText(_tempFile, "test");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    private PathValidator CreateValidator(List<string>? allowedDirs = null)
    {
        var config = new NexusConfig
        {
            Mcp = new McpConfig
            {
                Servers = new List<McpServerEntry>
                {
                    new()
                    {
                        Name = "filesystem",
                        Args = new List<string>
                        {
                            "-y",
                            "@modelcontextprotocol/server-filesystem",
                        }.Concat(allowedDirs ?? new List<string> { _tempRoot }).ToList()
                    }
                }
            }
        };

        var registry = new ToolRegistry();
        return new PathValidator(config, registry, cacheTtl: TimeSpan.FromMilliseconds(100));
    }

    private static async Task<List<PathValidator.CatalogEntry>> GetCatalogViaReflectionAsync(PathValidator validator)
    {
        var method = typeof(PathValidator).GetMethod("GetCatalogAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task<List<PathValidator.CatalogEntry>>)method!.Invoke(validator, new object[] { CancellationToken.None })!;
        return await task;
    }

    // --- ExtractAllowedDirectories ---

    [Fact]
    public void ExtractAllowedDirectories_FilesystemServer_ReturnsCorrectPaths()
    {
        var servers = new List<McpServerEntry>
        {
            new()
            {
                Name = "filesystem",
                Args = new List<string>
                {
                    "-y",
                    "@modelcontextprotocol/server-filesystem",
                    @"D:\Projects\MyApp",
                    @"D:\Shared\Data"
                }
            }
        };

        var dirs = PathValidator.ExtractAllowedDirectories(servers);

        Assert.Equal(2, dirs.Count);
        Assert.Equal(@"D:\Projects\MyApp", dirs[0]);
        Assert.Equal(@"D:\Shared\Data", dirs[1]);
    }

    [Fact]
    public void ExtractAllowedDirectories_NonFilesystemServer_ReturnsEmpty()
    {
        var servers = new List<McpServerEntry>
        {
            new()
            {
                Name = "weather",
                Args = new List<string> { "-y", "@some/weather-server" }
            }
        };

        var dirs = PathValidator.ExtractAllowedDirectories(servers);
        Assert.Empty(dirs);
    }

    // --- NormalizePath ---

    [Fact]
    public void NormalizePath_TrimsAndNormalizesSlashes()
    {
        var raw = $"  {_tempRoot}{Path.DirectorySeparatorChar}ecomerce\\docs  ";
        var result = PathValidator.NormalizePath(raw);
        if (Path.DirectorySeparatorChar == '/')
            Assert.DoesNotContain("\\", result);
        else
            Assert.DoesNotContain("/", result);
        Assert.False(result.StartsWith(" "));
    }

    // --- ValidateAsync ---

    [Fact]
    public async Task ValidateAsync_NoArguments_ReturnsOk()
    {
        var validator = CreateValidator();
        var result = await validator.ValidateAsync("list_tools", null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_NonPathArgument_Unchanged()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["content"] = "hello world",
            ["verbose"] = true
        };

        var result = await validator.ValidateAsync("some_tool", args);

        Assert.True(result.IsValid);
        Assert.Equal("hello world", result.CorrectedArguments!["content"]);
    }

    [Fact]
    public async Task ValidateAsync_PathWithinAllowed_Exists_ReturnsOk()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["path"] = _tempFile
        };

        var result = await validator.ValidateAsync("read_file", args);

        Assert.True(result.IsValid);
        Assert.False(result.WasCorrected);
    }

    [Fact]
    public async Task ValidateAsync_PathOutsideAllowed_UsesCatalogCorrection()
    {
        var validator = CreateValidator();
        var wrongPath = @"Z:\totally\wrong\location\scrum_plan.md";

        var args = new Dictionary<string, object>
        {
            ["path"] = wrongPath
        };

        var result = await validator.ValidateAsync("read_file", args);

        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        Assert.Equal(_tempFile, (string)result.CorrectedArguments!["path"]);
    }

    [Fact]
    public async Task ValidateAsync_WriteFile_ParentExists_AllowsNewFile()
    {
        var validator = CreateValidator();
        var newFile = Path.Combine(_tempDocsDir, "new_file.txt");

        var args = new Dictionary<string, object>
        {
            ["path"] = newFile,
            ["content"] = "data"
        };

        var result = await validator.ValidateAsync("write_file", args);

        Assert.True(result.IsValid);
        Assert.Equal(newFile, result.CorrectedArguments!["path"]);
    }

    [Fact]
    public async Task ValidateAsync_MoveFile_ValidatesBothPaths()
    {
        var validator = CreateValidator();
        var destPath = Path.Combine(_tempDocsDir, "moved.md");

        var args = new Dictionary<string, object>
        {
            ["source"] = _tempFile,
            ["destination"] = destPath
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_MoveFile_DestinationIsDirectory_AppendsFilename()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["source"] = _tempFile,          // .../scrum_plan.md
            ["destination"] = _tempDocsDir    // .../ecomerce/docs  (directory)
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        var destResult = (string)result.CorrectedArguments!["destination"];
        Assert.EndsWith("scrum_plan.md", destResult);
        Assert.Contains("ecomerce", destResult);
        Assert.Contains("docs", destResult);
    }

    [Fact]
    public async Task ValidateAsync_CompletelyWrongPath_ReturnsFail()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["path"] = @"Z:\nonexistent\garbage\file.txt"
        };

        var result = await validator.ValidateAsync("read_file", args);

        Assert.False(result.IsValid);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_FuzzyMatch_CorrectsMisspelledDirectory()
    {
        var validator = CreateValidator();
        var typoPath = Path.Combine(_tempRoot, "ecomerc", "docs");

        var args = new Dictionary<string, object>
        {
            ["path"] = typoPath
        };

        var result = await validator.ValidateAsync("list_directory", args);

        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        Assert.Contains("ecomerce", (string)result.CorrectedArguments!["path"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_FileInSubdirectory_FoundByRecursiveSearch()
    {
        var validator = CreateValidator();
        var fileInSubdir = Path.Combine(_tempDocsDir, "nested_file.txt");
        File.WriteAllText(fileInSubdir, "test");

        var wrongPath = Path.Combine(_tempRoot, "nested_file.txt");

        var args = new Dictionary<string, object>
        {
            ["path"] = wrongPath
        };

        var result = await validator.ValidateAsync("read_file", args);

        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        Assert.Contains("nested_file.txt", (string)result.CorrectedArguments!["path"]);
        Assert.Contains("ecomerce", (string)result.CorrectedArguments!["path"]);
    }

    [Fact]
    public async Task ValidateAsync_ReadFile_FileNotExists_ReturnsFail()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["path"] = Path.Combine(_tempRoot, "nonexistent_file_xyz.txt")
        };

        var result = await validator.ValidateAsync("read_file", args);

        Assert.False(result.IsValid);
        Assert.Contains("not found", result.ErrorMessage);
    }

    // --- NormalizePath: trailing slash ---

    [Fact]
    public void NormalizePath_TrailingSlash_IsStripped()
    {
        var result = PathValidator.NormalizePath(@"D:\some\path\");
        Assert.False(result.EndsWith(@"\"));
    }

    [Fact]
    public void NormalizePath_TrailingForwardSlash_IsStripped()
    {
        var result = PathValidator.NormalizePath("D:/some/path/");
        Assert.False(result.EndsWith(@"\"));
    }

    [Fact]
    public void NormalizePath_RootDrive_PreservesBackslash()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = PathValidator.NormalizePath(@"C:\");
        Assert.Equal(@"C:\", result);
    }

    // --- Destination: no fuzzy match (falso positivo bug) ---

    [Fact]
    public async Task ValidateAsync_Destination_NoFuzzyLeaf_PreservesNewName()
    {
        var validator = CreateValidator();
        // "models" is a NEW name — must NOT fuzzy-match to existing "model" directory
        var newDir = Path.Combine(_tempRoot, "models");

        var args = new Dictionary<string, object>
        {
            ["source"] = _tempFile,
            ["destination"] = newDir
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.True(result.IsValid);
        var destResult = (string)result.CorrectedArguments!["destination"];
        Assert.Contains("models", destResult);          // leaf name preserved
        Assert.DoesNotContain("docs", destResult);       // NOT redirected to existing model dir
    }

    [Fact]
    public async Task ValidateAsync_Destination_ParentNotExists_ReturnsFail()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["source"] = _tempFile,
            ["destination"] = Path.Combine(_tempRoot, "fake_parent", "sub", "file.md")
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.False(result.IsValid);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_Destination_TrailingSlash_DoesNotBreakPath()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["source"] = _tempFile,
            ["destination"] = _tempDocsDir + "/" // trailing slash
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.True(result.IsValid);
        var destResult = (string)result.CorrectedArguments!["destination"];
        // Should append filename because destination is existing directory
        Assert.EndsWith("scrum_plan.md", destResult);
    }

    // --- Source: recursive search finds file in wrong level ---

    [Fact]
    public async Task ValidateAsync_Source_FileInDeepSubdir_Found()
    {
        var validator = CreateValidator();
        var deepFile = Path.Combine(_tempDocsDir, "report.md");
        File.WriteAllText(deepFile, "data");

        // Model says file is at root, but it's in ecommerce/docs/
        var wrongPath = Path.Combine(_tempRoot, "report.md");
        var args = new Dictionary<string, object>
        {
            ["path"] = wrongPath
        };

        var result = await validator.ValidateAsync("read_file", args);

        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        Assert.Contains("ecomerce", (string)result.CorrectedArguments!["path"]);
        Assert.Contains("report.md", (string)result.CorrectedArguments!["path"]);
    }

    // --- Source: fuzzy match verifies existence ---

    [Fact]
    public async Task ValidateAsync_Source_FuzzyMatchButFileNotThere_DoesNotFalsePositive()
    {
        var validator = CreateValidator();
        // "ecomerce/docs" fuzzy-matches "ecommerce/docs", but ghost_file.md doesn't exist there
        var typoPath = Path.Combine(_tempRoot, "ecomerce", "docs", "ghost_file.md");

        var args = new Dictionary<string, object>
        {
            ["path"] = typoPath
        };

        var result = await validator.ValidateAsync("read_file", args);

        // File doesn't exist anywhere — should fail, not false positive
        Assert.False(result.IsValid);
    }

    // --- MoveFile: directory as source ---

    [Fact]
    public async Task ValidateAsync_MoveDirectory_DestinationNotCorruptedByFuzzy()
    {
        var validator = CreateValidator();
        // Move existing dir "ecommerce" to new location "shop"
        var sourceDir = Path.Combine(_tempRoot, "ecomerce");
        var destDir = Path.Combine(_tempRoot, "shop");

        var args = new Dictionary<string, object>
        {
            ["source"] = sourceDir,
            ["destination"] = destDir
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.True(result.IsValid);
        var destResult = (string)result.CorrectedArguments!["destination"];
        Assert.Contains("shop", destResult);
        Assert.DoesNotContain("ecomerce", destResult); // Destination must NOT be fuzzy-matched back
    }

    // --- New internal matching flow ---

    [Fact]
    public void FindBestMatch_ExactName_FindsInSubdirectory()
    {
        var validator = CreateValidator();
        var catalog = new List<PathValidator.CatalogEntry>
        {
            new(Path.Combine(_tempRoot, "ecomerce", "docs", "model"), "model", true)
        };

        var result = validator.FindBestMatch(Path.Combine(_tempRoot, "ecommerce", "model"), catalog);

        Assert.NotNull(result);
        Assert.Contains(Path.Combine("ecomerce", "docs", "model"), result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindBestMatch_FuzzyName_CorrectTypo()
    {
        var validator = CreateValidator();
        var catalog = new List<PathValidator.CatalogEntry>
        {
            new(Path.Combine(_tempRoot, "ecomerce", "docs", "model"), "model", true)
        };

        var result = validator.FindBestMatch(Path.Combine(_tempRoot, "ecomerce", "docs", "models"), catalog);

        Assert.NotNull(result);
        Assert.EndsWith(Path.Combine("ecomerce", "docs", "model"), result);
    }

    [Fact]
    public void FindBestMatch_MultipleMatches_UsesFullPathToDisambiguate()
    {
        var validator = CreateValidator();
        var near = Path.Combine(_tempRoot, "ecomerce", "docs", "config");
        var far = Path.Combine(_tempRoot, "archive", "legacy", "config");
        var catalog = new List<PathValidator.CatalogEntry>
        {
            new(near, "config", true),
            new(far, "config", true),
        };

        var result = validator.FindBestMatch(Path.Combine(_tempRoot, "ecommerce", "docs", "config"), catalog);

        Assert.Equal(near, result);
    }

    [Fact]
    public void FindBestMatch_NoMatch_ReturnsNull()
    {
        var validator = CreateValidator();
        var catalog = new List<PathValidator.CatalogEntry>
        {
            new(Path.Combine(_tempRoot, "ecomerce", "docs", "model"), "model", true)
        };

        var result = validator.FindBestMatch(Path.Combine(_tempRoot, "xyznonexistent"), catalog);

        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatchForParent_FindsParentDir()
    {
        var validator = CreateValidator();
        var catalog = new List<PathValidator.CatalogEntry>
        {
            new(Path.Combine(_tempRoot, "ecomerce", "docs"), "docs", true),
            new(Path.Combine(_tempRoot, "ecomerce"), "ecomerce", true),
        };

        var parent = validator.FindBestMatchForParent(Path.Combine(_tempRoot, "ecommerce", "new.txt"), catalog);

        Assert.NotNull(parent);
        var newPath = Path.Combine(parent!, "new.txt");
        Assert.EndsWith(Path.Combine("ecomerce", "new.txt"), newPath);
    }

    [Fact]
    public async Task GetCatalog_IncludesFilesAndDirs()
    {
        var validator = CreateValidator();
        var nestedFile = Path.Combine(_tempDocsDir, "catalog_file.txt");
        File.WriteAllText(nestedFile, "data");

        var catalog = await GetCatalogViaReflectionAsync(validator);

        Assert.Contains(catalog, e => e.IsDirectory && e.FullPath.EndsWith(Path.Combine("ecomerce", "docs"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog, e => !e.IsDirectory && e.FullPath.EndsWith(Path.Combine("ecomerce", "docs", "catalog_file.txt"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_SkippedLevel_FindsCorrectPath()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["path"] = Path.Combine(_tempRoot, "ecommerce", "model")
        };

        var result = await validator.ValidateAsync("list_directory", args);

        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        Assert.EndsWith(Path.Combine("ecomerce", "docs", "model"), (string)result.CorrectedArguments!["path"], StringComparison.OrdinalIgnoreCase);
    }

    // --- AC-7: stale-state guard ---

    [Fact]
    public async Task Validate_OriginalPathExists_DoesNotCorrectEvenWithCloseMatch()
    {
        // Arrange — two files with nearly identical names; the original exists.
        var isolatedDir = Path.Combine(Path.GetTempPath(), "nexus_ac7_test_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(isolatedDir);
            var fooPath = Path.Combine(isolatedDir, "foo.txt");
            var foaPath = Path.Combine(isolatedDir, "foa.txt");
            File.WriteAllText(fooPath, "original");
            File.WriteAllText(foaPath, "neighbor");

            var validator = CreateValidator(new List<string> { isolatedDir });
            var args = new Dictionary<string, object>
            {
                ["path"] = fooPath
            };

            // Act
            var result = await validator.ValidateAsync("read_file", args);

            // Assert — existence wins: must return the original, not the fuzzy neighbor.
            Assert.True(result.IsValid);
            Assert.False(result.WasCorrected);
            Assert.Equal(fooPath, (string)result.CorrectedArguments!["path"]);
        }
        finally
        {
            try { Directory.Delete(isolatedDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Validate_OriginalMissing_StrictDistanceRejected_WhenScoreBelowThreshold()
    {
        // Arrange — multiple index.html files exist (the actual Bug 4 scenario: ambiguous
        // basename). The model's path includes deep non-existent subdirectories that push the
        // full-path similarity score well below 90. With basename-uniqueness short-circuit,
        // the strict-distance gate only applies when count > 1; this test exercises that path.
        var isolatedDir = Path.Combine(Path.GetTempPath(), "nexus_ac7_strict_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(isolatedDir);
            var existingFile = Path.Combine(isolatedDir, "index.html");
            File.WriteAllText(existingFile, "content");

            // Second index.html in a sibling dir → basename "index.html" is now ambiguous
            // (count > 1), so strict-distance must apply.
            var siblingDir = Path.Combine(isolatedDir, "sprint_1_tasks");
            Directory.CreateDirectory(siblingDir);
            File.WriteAllText(Path.Combine(siblingDir, "index.html"), "decoy");

            // Build a deeply nested missing path so that the full-path Fuzz.Ratio score
            // between this path and existingFile is reliably < 90 regardless of how
            // long the temp directory prefix is. The extra segments add ~50 characters.
            var missingPath = Path.Combine(
                isolatedDir,
                "archived", "backups", "old_sprint", "2024_q3", "index.html");

            var config = new NexusConfig
            {
                Mcp = new McpConfig
                {
                    PathValidatorStrictDistance = 90,
                    Servers = new List<McpServerEntry>
                    {
                        new()
                        {
                            Name = "filesystem",
                            Args = new List<string>
                            {
                                "-y",
                                "@modelcontextprotocol/server-filesystem",
                                isolatedDir
                            }
                        }
                    }
                }
            };
            var registry = new ToolRegistry();
            var validator = new PathValidator(config, registry, cacheTtl: TimeSpan.FromMilliseconds(100));
            var args = new Dictionary<string, object>
            {
                ["path"] = missingPath
            };

            // Act
            var result = await validator.ValidateAsync("read_file", args);

            // Assert — strict guard rejects the distant match.
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
            Assert.True(
                result.ErrorMessage!.Contains("too distant") || result.ErrorMessage.Contains("score"),
                $"Expected error to mention 'too distant' or 'score', got: {result.ErrorMessage}");
        }
        finally
        {
            try { Directory.Delete(isolatedDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Validate_OriginalMissing_StrictDistanceAccepted_WhenScoreAboveThreshold()
    {
        // Arrange — index.html exists; raw path differs only by case → very high similarity score.
        var isolatedDir = Path.Combine(Path.GetTempPath(), "nexus_ac7_accept_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(isolatedDir);
            var existingFile = Path.Combine(isolatedDir, "index.html");
            File.WriteAllText(existingFile, "content");

            // On Windows, Path.GetFullPath normalizes case, so the raw path and existingFile
            // will produce the same normalized string — triggering the fast-path.
            // Use a fuzzy variant: "Index.html" on a case-sensitive FS, or test via a
            // slightly different name that still scores above the threshold.
            // We use a low strict threshold (50) to ensure even a moderate score passes.
            var rawPath = Path.Combine(isolatedDir, "index.htm");   // missing extension: scores high

            var config = new NexusConfig
            {
                Mcp = new McpConfig
                {
                    PathValidatorStrictDistance = 50,   // low threshold — moderate match suffices
                    Servers = new List<McpServerEntry>
                    {
                        new()
                        {
                            Name = "filesystem",
                            Args = new List<string>
                            {
                                "-y",
                                "@modelcontextprotocol/server-filesystem",
                                isolatedDir
                            }
                        }
                    }
                }
            };
            var registry = new ToolRegistry();
            var validator = new PathValidator(config, registry, fuzzyThreshold: 50, cacheTtl: TimeSpan.FromMilliseconds(100));
            var args = new Dictionary<string, object>
            {
                ["path"] = rawPath
            };

            // Act
            var result = await validator.ValidateAsync("read_file", args);

            // Assert — high-confidence match accepted; corrected to the real file.
            Assert.True(result.IsValid, $"Expected valid but got error: {result.ErrorMessage}");
            Assert.True(result.WasCorrected);
            Assert.Equal(existingFile, (string)result.CorrectedArguments!["path"]);
        }
        finally
        {
            try { Directory.Delete(isolatedDir, true); } catch { }
        }
    }

    // --- Sprint 10 follow-up: basename-uniqueness short-circuit ---

    [Fact]
    public async Task Validate_UniqueBasename_AcceptsLowFullPathScore_WithinAllowedDirs()
    {
        // Arrange — single "ecomerce" directory in the catalog. The model passes a path
        // whose CWD-prefix differs significantly from the real path, dragging the full-path
        // Fuzz.Ratio below the strict threshold (90). With basename-uniqueness short-circuit,
        // the unique basename means no ambiguity → accept regardless of full-path score.
        // This is the real-world repro: "nexus/ecomerce" → "D:\Nexus\ecomerce".
        var isolatedDir = Path.Combine(Path.GetTempPath(), "nexus_ac7_unique_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(isolatedDir);
            var realDir = Path.Combine(isolatedDir, "ecomerce");
            Directory.CreateDirectory(realDir);

            // Build a path whose full-path Fuzz.Ratio against realDir is well below 90.
            // We reference the same basename through a deep non-existent subtree so the
            // shared-prefix penalty pushes the score down.
            var missingPath = Path.Combine(
                isolatedDir,
                "deeply", "nested", "fake", "branches", "ecomerce");

            var validator = CreateValidator(new List<string> { isolatedDir });
            var args = new Dictionary<string, object>
            {
                ["path"] = missingPath
            };

            // Act
            var result = await validator.ValidateAsync("list_directory", args);

            // Assert — unique basename → accepted; corrected to the real directory.
            Assert.True(result.IsValid, $"Expected valid but got error: {result.ErrorMessage}");
            Assert.True(result.WasCorrected);
            Assert.Equal(realDir, (string)result.CorrectedArguments!["path"]);
        }
        finally
        {
            try { Directory.Delete(isolatedDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Validate_FuzzyBasenameTypo_UniqueCandidate_AcceptsCorrection()
    {
        // Arrange — real-world repro: model writes "ecommerce" (English standard) but the
        // actual folder is "ecomerce" (single m, the user's spelling). Basename is NOT an
        // exact match, but is the only fuzzy candidate in the catalog (typo correction was
        // exactly the PathValidator's original purpose). With basename-uniqueness in the
        // fuzzy branch, this case is accepted; without it, the strict-distance gate (90)
        // rejects because the long CWD-prefix divergence drags the full-path score below.
        var isolatedDir = Path.Combine(Path.GetTempPath(), "nexus_ac7_typo_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(isolatedDir);
            var realDir = Path.Combine(isolatedDir, "ecomerce");      // single m (the user's folder)
            Directory.CreateDirectory(realDir);

            // Model produced typo with extra m AND deep nesting that would tank full-path score.
            var typoPath = Path.Combine(
                isolatedDir,
                "deeply", "nested", "fake", "branches", "ecommerce");  // double m + bogus prefix

            var validator = CreateValidator(new List<string> { isolatedDir });
            var args = new Dictionary<string, object>
            {
                ["path"] = typoPath
            };

            // Act
            var result = await validator.ValidateAsync("list_directory", args);

            // Assert — single fuzzy candidate → typo corrected to real folder.
            Assert.True(result.IsValid, $"Expected valid but got error: {result.ErrorMessage}");
            Assert.True(result.WasCorrected);
            Assert.Equal(realDir, (string)result.CorrectedArguments!["path"]);
        }
        finally
        {
            try { Directory.Delete(isolatedDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Validate_AmbiguousBasename_StillRequiresStrictDistance()
    {
        // Arrange — TWO directories named "models" in different branches → basename ambiguous
        // (count > 1). With ambiguity, the strict-distance gate must remain active, falling
        // back to full-path Fuzz.Ratio to disambiguate. A deeply nested missing path with
        // low full-path score against the closest match must be rejected (Bug 4 protection).
        // Test uses explicit strict=90 so it is robust to future default-threshold tweaks.
        var isolatedDir = Path.Combine(Path.GetTempPath(), "nexus_ac7_ambig_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(isolatedDir);
            Directory.CreateDirectory(Path.Combine(isolatedDir, "src", "models"));
            Directory.CreateDirectory(Path.Combine(isolatedDir, "tests", "models"));

            // Build a deeply nested missing path so the full-path score against either
            // candidate stays below the explicit strict threshold (90).
            var missingPath = Path.Combine(
                isolatedDir,
                "archived", "backups", "old_sprint", "2024_q3", "models");

            var config = new NexusConfig
            {
                Mcp = new McpConfig
                {
                    PathValidatorStrictDistance = 90,   // explicit — robust to default changes
                    Servers = new List<McpServerEntry>
                    {
                        new()
                        {
                            Name = "filesystem",
                            Args = new List<string>
                            {
                                "-y",
                                "@modelcontextprotocol/server-filesystem",
                                isolatedDir
                            }
                        }
                    }
                }
            };
            var validator = new PathValidator(config, new ToolRegistry(), cacheTtl: TimeSpan.FromMilliseconds(100));
            var args = new Dictionary<string, object>
            {
                ["path"] = missingPath
            };

            // Act
            var result = await validator.ValidateAsync("list_directory", args);

            // Assert — ambiguous basename + low full-path score → strict guard still rejects.
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
            Assert.True(
                result.ErrorMessage!.Contains("too distant") || result.ErrorMessage.Contains("score"),
                $"Expected error to mention 'too distant' or 'score', got: {result.ErrorMessage}");
        }
        finally
        {
            try { Directory.Delete(isolatedDir, true); } catch { }
        }
    }

    // --- IdentifyPathParameters ---

    [Fact]
    public void IdentifyPathParameters_DetectsCommonNames()
    {
        var validator = CreateValidator();
        var args = new Dictionary<string, object>
        {
            ["path"] = "/some/path",
            ["source"] = "/src",
            ["destination"] = "/dst",
            ["content"] = "not a path",
            ["verbose"] = true
        };

        var pathKeys = validator.IdentifyPathParameters("move_file", args);

        Assert.Contains("path", pathKeys);
        Assert.Contains("source", pathKeys);
        Assert.Contains("destination", pathKeys);
        Assert.DoesNotContain("content", pathKeys);
        Assert.DoesNotContain("verbose", pathKeys);
    }
}

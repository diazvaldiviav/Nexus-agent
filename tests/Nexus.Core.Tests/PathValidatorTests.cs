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
        var result = PathValidator.NormalizePath("  D:/some/path  ");
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
        var typoPath = Path.Combine(_tempRoot, "ecommerce", "docs");

        var args = new Dictionary<string, object>
        {
            ["path"] = typoPath
        };

        var result = await validator.ValidateAsync("list_directory", args);

        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        Assert.Contains("ecomerce", (string)result.CorrectedArguments!["path"]);
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
        Assert.Contains("does not exist", result.ErrorMessage);
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
        var result = PathValidator.NormalizePath(@"C:\");
        Assert.Equal(@"C:\", result);
    }

    // --- Destination: no fuzzy match (falso positivo bug) ---

    [Fact]
    public async Task ValidateAsync_Destination_NoFuzzyMatch_NewDirAllowed()
    {
        var validator = CreateValidator();
        // "models" is a new dir that doesn't exist — should NOT fuzzy to "ecommerce/docs"
        var newDir = Path.Combine(_tempRoot, "models");

        var args = new Dictionary<string, object>
        {
            ["source"] = _tempFile,
            ["destination"] = newDir
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.True(result.IsValid);
        var destResult = (string)result.CorrectedArguments!["destination"];
        Assert.Contains("models", destResult);
        Assert.DoesNotContain("ecommerce", destResult); // No fuzzy match to existing dir
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
        Assert.Contains("parent directory does not exist", result.ErrorMessage);
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

    // --- Destination: should NOT use recursive search ---

    [Fact]
    public async Task ValidateAsync_Destination_DoesNotRecursiveSearch()
    {
        var validator = CreateValidator();
        // nested_file.txt exists in ecommerce/docs/ but destination should NOT find it there
        var nestedFile = Path.Combine(_tempDocsDir, "nested_file.txt");
        File.WriteAllText(nestedFile, "data");

        var destPath = Path.Combine(_tempRoot, "nested_file.txt"); // doesn't exist at root

        var args = new Dictionary<string, object>
        {
            ["source"] = _tempFile,
            ["destination"] = destPath
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.True(result.IsValid);
        var destResult = (string)result.CorrectedArguments!["destination"];
        // Should stay at root, NOT redirect to ecommerce/docs/nested_file.txt
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}ecomerce{Path.DirectorySeparatorChar}docs{Path.DirectorySeparatorChar}nested_file.txt", destResult, StringComparison.OrdinalIgnoreCase);
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

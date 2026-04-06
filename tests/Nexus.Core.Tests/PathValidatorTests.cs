using Nexus.Connectors;
using Nexus.Core.Config;

namespace Nexus.Core.Tests;

public class PathValidatorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _tempSubDir;
    private readonly string _tempFile;

    public PathValidatorTests()
    {
        // Create a temp directory structure for testing
        _tempRoot = Path.Combine(Path.GetTempPath(), "nexus_test_" + Guid.NewGuid().ToString("N")[..8]);
        _tempSubDir = Path.Combine(_tempRoot, "ecommerce", "docs");
        Directory.CreateDirectory(_tempSubDir);
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

    // --- StripCommonRoot ---

    [Fact]
    public void StripCommonRoot_ExtractsRelativePart()
    {
        var result = PathValidator.StripCommonRoot(
            @"D:\Nova Tech\Nexus\scrum.md",
            @"D:\Nova Tech\Nexus\Nexus-agent");

        Assert.Equal("scrum.md", result);
    }

    [Fact]
    public void StripCommonRoot_DifferentDrive_ReturnsNull()
    {
        var result = PathValidator.StripCommonRoot(
            @"C:\Other\file.txt",
            @"D:\Nova Tech\Nexus\Nexus-agent");

        Assert.Null(result);
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
    public async Task ValidateAsync_PathOutsideAllowed_RepairsRoot()
    {
        var validator = CreateValidator();
        // Simulate: model uses parent of allowed dir + filename
        var parentDir = Path.GetDirectoryName(_tempRoot)!;
        var wrongPath = Path.Combine(parentDir, "scrum_plan.md");

        var args = new Dictionary<string, object>
        {
            ["path"] = wrongPath
        };

        var result = await validator.ValidateAsync("read_file", args);

        // Should repair root and find the file inside _tempRoot
        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        Assert.Contains("scrum_plan.md", (string)result.CorrectedArguments!["path"]);
    }

    [Fact]
    public async Task ValidateAsync_WriteFile_ParentExists_AllowsNewFile()
    {
        var validator = CreateValidator();
        var newFile = Path.Combine(_tempSubDir, "new_file.txt");

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
        var destPath = Path.Combine(_tempSubDir, "moved.md");

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
            ["destination"] = _tempSubDir    // .../ecommerce/docs  (directory)
        };

        var result = await validator.ValidateAsync("move_file", args);

        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        var destResult = (string)result.CorrectedArguments!["destination"];
        Assert.EndsWith("scrum_plan.md", destResult);
        Assert.Contains("ecommerce", destResult);
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
        Assert.Contains("outside allowed directories", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_FuzzyMatch_CorrectsMisspelledDirectory()
    {
        var validator = CreateValidator();
        // "ecomerce" (typo) should fuzzy-match "ecommerce"
        var typoPath = Path.Combine(_tempRoot, "ecomerce", "docs");

        var args = new Dictionary<string, object>
        {
            ["path"] = typoPath
        };

        var result = await validator.ValidateAsync("list_directory", args);

        // Should fuzzy-match to the correct "ecommerce/docs" directory
        Assert.True(result.IsValid);
        Assert.True(result.WasCorrected);
        Assert.Contains("ecommerce", (string)result.CorrectedArguments!["path"]);
    }

    [Fact]
    public async Task ValidateAsync_FileInSubdirectory_FoundByRecursiveSearch()
    {
        var validator = CreateValidator();
        // File is at _tempRoot/ecommerce/docs/ but model says it's at _tempRoot/
        var fileInSubdir = Path.Combine(_tempSubDir, "nested_file.txt");
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
        Assert.Contains("ecommerce", (string)result.CorrectedArguments!["path"]);
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

    [Fact]
    public void TryFindFileRecursive_FindsFileInNestedDirectory()
    {
        var validator = CreateValidator();
        var nestedFile = Path.Combine(_tempSubDir, "deep_file.md");
        File.WriteAllText(nestedFile, "test");

        var found = validator.TryFindFileRecursive("deep_file.md");

        Assert.NotNull(found);
        Assert.Contains("deep_file.md", found);
        Assert.Contains("ecommerce", found);
    }

    [Fact]
    public void TryFindFileRecursive_FileNotExists_ReturnsNull()
    {
        var validator = CreateValidator();
        var found = validator.TryFindFileRecursive("totally_missing_file.xyz");
        Assert.Null(found);
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
            ["destination"] = _tempSubDir + "/" // trailing slash
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
        var nestedFile = Path.Combine(_tempSubDir, "nested_file.txt");
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
        Assert.DoesNotContain("ecommerce", destResult);
    }

    // --- Source: recursive search finds file in wrong level ---

    [Fact]
    public async Task ValidateAsync_Source_FileInDeepSubdir_Found()
    {
        var validator = CreateValidator();
        var deepFile = Path.Combine(_tempSubDir, "report.md");
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
        Assert.Contains("ecommerce", (string)result.CorrectedArguments!["path"]);
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
        var sourceDir = Path.Combine(_tempRoot, "ecommerce");
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
        Assert.DoesNotContain("ecommerce", destResult); // Destination must NOT be fuzzy-matched back
    }

    // --- StripCommonRoot edge cases ---

    [Fact]
    public void StripCommonRoot_SamePath_ReturnsNull()
    {
        var result = PathValidator.StripCommonRoot(
            @"D:\Nova Tech\Nexus\Nexus-agent",
            @"D:\Nova Tech\Nexus\Nexus-agent");

        Assert.Null(result); // No remaining segments
    }

    [Fact]
    public void StripCommonRoot_DeepRelative_ReturnsAllRemaining()
    {
        var result = PathValidator.StripCommonRoot(
            @"D:\Nova Tech\Nexus\ecomerce\docs\file.md",
            @"D:\Nova Tech\Nexus\Nexus-agent");

        Assert.Equal(@"ecomerce\docs\file.md", result);
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

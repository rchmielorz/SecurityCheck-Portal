using securitycheck_portal.Core.Scanning;

namespace securitycheck_portal.Tests.Scanning;

public sealed class LockFileDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lockdetect-" + Guid.NewGuid().ToString("N"));
    private readonly LockFileDetector _detector = new();

    public LockFileDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Empty_checkout_has_nothing_missing()
    {
        Assert.Empty(_detector.FindMissing(_root));
    }

    [Fact]
    public void Csproj_without_lock_file_is_reported()
    {
        Touch("src/App/App.csproj");

        Assert.Equal(["src/App/packages.lock.json"], _detector.FindMissing(_root));
    }

    [Fact]
    public void Csproj_with_lock_file_is_complete()
    {
        Touch("App.csproj");
        Touch("packages.lock.json");

        Assert.Empty(_detector.FindMissing(_root));
    }

    [Fact]
    public void Package_json_without_any_lock_file_is_reported()
    {
        Touch("web/package.json");

        Assert.Equal(["web/package-lock.json"], _detector.FindMissing(_root));
    }

    [Theory]
    [InlineData("package-lock.json")]
    [InlineData("yarn.lock")]
    [InlineData("pnpm-lock.yaml")]
    public void Package_json_with_any_lock_variant_is_complete(string lockFile)
    {
        Touch("package.json");
        Touch(lockFile);

        Assert.Empty(_detector.FindMissing(_root));
    }

    [Fact]
    public void Lock_file_in_another_directory_does_not_count()
    {
        Touch("a/App.csproj");
        Touch("b/packages.lock.json");

        Assert.Equal(["a/packages.lock.json"], _detector.FindMissing(_root));
    }

    [Fact]
    public void Csproj_and_package_json_in_one_directory_are_both_checked()
    {
        Touch("App.csproj");
        Touch("package.json");

        Assert.Equal(["package-lock.json", "packages.lock.json"], _detector.FindMissing(_root));
    }

    [Theory]
    [InlineData("node_modules/lib/package.json")]
    [InlineData("node_modules/lib/Lib.csproj")]
    [InlineData(".git/hooks/package.json")]
    [InlineData("bin/Debug/App.csproj")]
    [InlineData("obj/App.csproj")]
    [InlineData("src/node_modules/lib/package.json")]
    public void Skipped_directories_are_not_searched(string path)
    {
        Touch(path);

        Assert.Empty(_detector.FindMissing(_root));
    }

    [Fact]
    public void Csproj_with_packages_config_is_complete()
    {
        Touch("App.csproj");
        Touch("packages.config");

        Assert.Empty(_detector.FindMissing(_root));
        Assert.Empty(_detector.FindDotnetProjectsWithoutLock(_root));
    }

    [Fact]
    public void Csproj_without_lock_file_is_listed_as_project_without_lock()
    {
        Touch("src/App/App.csproj");

        Assert.Equal(["src/App/packages.lock.json"], _detector.FindMissing(_root));
        Assert.Equal(["src/App/App.csproj"], _detector.FindDotnetProjectsWithoutLock(_root));
    }

    [Fact]
    public void Csproj_with_lock_file_is_not_listed_as_project_without_lock()
    {
        Touch("App.csproj");
        Touch("packages.lock.json");

        Assert.Empty(_detector.FindDotnetProjectsWithoutLock(_root));
    }

    [Fact]
    public void Projects_without_lock_are_listed_across_nested_directories_in_ordinal_order()
    {
        Touch("b/B.csproj");
        Touch("a/A.csproj");
        Touch("a/nested/N.csproj");
        Touch("c/C.csproj");
        Touch("c/packages.lock.json");

        Assert.Equal(["a/A.csproj", "a/nested/N.csproj", "b/B.csproj"], _detector.FindDotnetProjectsWithoutLock(_root));
    }

    [Fact]
    public void Directory_with_two_csproj_is_reported_as_multiple_projects_and_not_as_project_without_lock()
    {
        Touch("src/One.csproj");
        Touch("src/Two.csproj");

        Assert.Equal(["src/packages.lock.json"], _detector.FindMissing(_root));
        Assert.Empty(_detector.FindDotnetProjectsWithoutLock(_root));
        Assert.Equal(["src/packages.lock.json"], _detector.FindDirectoriesWithMultipleProjects(_root));
    }

    [Theory]
    [InlineData("packages.config")]
    [InlineData("packages.lock.json")]
    public void Directory_with_two_csproj_and_lock_or_packages_config_is_not_reported_as_multiple_projects(string file)
    {
        Touch("src/One.csproj");
        Touch("src/Two.csproj");
        Touch("src/" + file);

        Assert.Empty(_detector.FindDirectoriesWithMultipleProjects(_root));
    }

    [Fact]
    public void Directory_with_one_csproj_is_not_reported_as_multiple_projects()
    {
        Touch("src/One.csproj");

        Assert.Empty(_detector.FindDirectoriesWithMultipleProjects(_root));
    }

    [Fact]
    public void Multiple_projects_are_listed_across_nested_directories_in_ordinal_order()
    {
        Touch("b/B1.csproj");
        Touch("b/B2.csproj");
        Touch("A1.csproj");
        Touch("A2.csproj");
        Touch("a/nested/N1.csproj");
        Touch("a/nested/N2.csproj");
        Touch("c/C.csproj");
        Touch("d/D1.csproj");
        Touch("d/D2.csproj");
        Touch("d/packages.lock.json");

        Assert.Equal(
            ["a/nested/packages.lock.json", "b/packages.lock.json", "packages.lock.json"],
            _detector.FindDirectoriesWithMultipleProjects(_root));
    }

    [Theory]
    [InlineData("node_modules/lib")]
    [InlineData(".git/hooks")]
    [InlineData("bin/Debug")]
    [InlineData("obj")]
    [InlineData("src/node_modules/lib")]
    public void Skipped_directories_are_not_searched_for_multiple_projects(string directory)
    {
        Touch(directory + "/One.csproj");
        Touch(directory + "/Two.csproj");

        Assert.Empty(_detector.FindDirectoriesWithMultipleProjects(_root));
    }

    [Theory]
    [InlineData("node_modules/lib/Lib.csproj")]
    [InlineData(".git/hooks/Hook.csproj")]
    [InlineData("bin/Debug/App.csproj")]
    [InlineData("obj/App.csproj")]
    [InlineData("src/node_modules/lib/Lib.csproj")]
    public void Skipped_directories_are_not_searched_for_projects(string path)
    {
        Touch(path);

        Assert.Empty(_detector.FindDotnetProjectsWithoutLock(_root));
    }

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }
}

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

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }
}

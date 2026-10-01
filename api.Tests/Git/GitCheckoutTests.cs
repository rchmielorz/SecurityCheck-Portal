using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using securitycheck_portal.Core.Git;
using securitycheck_portal.Core.Processes;

namespace securitycheck_portal.Tests.Git;

public sealed class GitCheckoutTests
{
    private const string Url = "https://git.internal/team/app.git";
    private const string Tag = "2.1.10";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Token = "s3cr3t-pat-value";
    private const string Target = @"C:\work\scan-1";

    private static readonly GitOptions Settings = new()
    {
        ExecutablePath = @"C:\Program Files\Git\cmd\git.exe",
        AllowedHosts = ["git.internal"],
        UserName = "pat",
        Token = Token,
    };

    private readonly FakeProcessRunner _runner = new();

    private GitCliCheckout Checkout => new(Options.Create(Settings), _runner, NullLogger<GitCliCheckout>.Instance);

    [Fact]
    public async Task Clones_the_tag_shallowly_with_the_url_after_end_of_options_and_verifies_head()
    {
        _runner.Enqueue(Exited(0));
        _runner.Enqueue(Exited(0, Commit + "\n"));

        var result = await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        Assert.IsType<GitCheckoutResult.Success>(result);
        Assert.Equal(2, _runner.Calls.Count);

        var clone = _runner.Calls[0].StartInfo;
        var arguments = clone.ArgumentList.SkipWhile(a => a != "clone").ToArray();
        Assert.Equal(
            ["clone", "--depth", "1", "--branch", Tag, "--no-recurse-submodules", "--end-of-options", Url, Target],
            arguments);
        Assert.Contains("core.longpaths=true", clone.ArgumentList);

        var revParse = _runner.Calls[1].StartInfo;
        Assert.Equal(["rev-parse", "HEAD"], revParse.ArgumentList.SkipWhile(a => a != "rev-parse").ToArray());
        Assert.Equal(Target, revParse.WorkingDirectory);
    }

    [Fact]
    public async Task Token_is_only_in_the_clone_environment_and_never_in_rev_parse()
    {
        _runner.Enqueue(Exited(0));
        _runner.Enqueue(Exited(0, Commit));

        await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        var clone = _runner.Calls[0].StartInfo;
        var expectedHeader = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"pat:{Token}"));
        Assert.DoesNotContain(clone.ArgumentList,
            a => a.Contains(Token) || a.Contains("Authorization") || a.Contains("extraHeader"));
        Assert.Equal($"http.{Url}.extraHeader", clone.Environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal(expectedHeader, clone.Environment["GIT_CONFIG_VALUE_0"]);

        var revParse = _runner.Calls[1].StartInfo;
        Assert.DoesNotContain(revParse.Environment.Values, v => v is not null && v.Contains("Authorization"));
        Assert.False(revParse.Environment.ContainsKey("GIT_CONFIG_KEY_0"));
        Assert.Equal("0", revParse.Environment["GIT_TERMINAL_PROMPT"]);
    }

    [Fact]
    public async Task Clone_does_not_use_the_short_ls_remote_transfer_limit()
    {
        _runner.Enqueue(Exited(0));
        _runner.Enqueue(Exited(0, Commit));

        await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        var arguments = _runner.Calls[0].StartInfo.ArgumentList;
        Assert.DoesNotContain("http.lowSpeedTime=20", arguments);
        var lowSpeedTime = arguments.Single(a => a.StartsWith("http.lowSpeedTime=", StringComparison.Ordinal));
        Assert.True(int.Parse(lowSpeedTime["http.lowSpeedTime=".Length..]) >= 120);
    }

    [Fact]
    public async Task Clone_gets_its_own_timeout_and_rev_parse_the_short_one()
    {
        _runner.Enqueue(Exited(0));
        _runner.Enqueue(Exited(0, Commit));

        await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        Assert.Equal(GitCliCheckout.DefaultCloneTimeout, _runner.Calls[0].Timeout);
        Assert.Equal(TimeSpan.FromSeconds(Settings.TimeoutSeconds), _runner.Calls[1].Timeout);
        Assert.True(GitCliCheckout.DefaultCloneTimeout > _runner.Calls[1].Timeout);
    }

    [Fact]
    public async Task Moved_tag_is_a_commit_mismatch()
    {
        const string Other = "fedcba9876543210fedcba9876543210fedcba98";
        _runner.Enqueue(Exited(0));
        _runner.Enqueue(Exited(0, Other + "\n"));

        var result = await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        Assert.Equal(new GitCheckoutResult.CommitMismatch(Other), result);
    }

    [Fact]
    public async Task Failed_clone_is_a_failure_and_skips_rev_parse()
    {
        _runner.Enqueue(Exited(128, stderr: "fatal: Remote branch 2.1.10 not found"));

        var result = await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        Assert.Equal(new GitCheckoutResult.Failure(GitErrorKind.Failed, 128), result);
        Assert.Single(_runner.Calls);
    }

    [Fact]
    public async Task Clone_timeout_and_start_failure_are_failures()
    {
        _runner.Enqueue(new ProcessRunResult(ProcessOutcome.TimedOut, null, "", ""));
        var timedOut = await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        _runner.Enqueue(new ProcessRunResult(ProcessOutcome.StartFailed, null, "", ""));
        var notStarted = await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        Assert.Equal(new GitCheckoutResult.Failure(GitErrorKind.Timeout, null), timedOut);
        Assert.Equal(new GitCheckoutResult.Failure(GitErrorKind.Failed, null), notStarted);
    }

    [Fact]
    public async Task Failed_rev_parse_is_a_failure()
    {
        _runner.Enqueue(Exited(0));
        _runner.Enqueue(Exited(128, stderr: "fatal: not a git repository"));

        var result = await Checkout.CheckoutAsync(Url, Tag, Commit, Target, CancellationToken.None);

        Assert.Equal(new GitCheckoutResult.Failure(GitErrorKind.Failed, 128), result);
    }

    /// <summary>
    /// Real clone of a real tag. Set <c>SCAN_IT_REPO_URL</c>, <c>SCAN_IT_TAG</c>, <c>SCAN_IT_COMMIT</c> and the
    /// <c>Git__Token</c>, <c>Git__AllowedHosts__0</c> (and optionally <c>Git__ExecutablePath</c>) variables; without
    /// them the test is skipped.
    /// </summary>
    [SkippableFact]
    public async Task Real_clone_of_a_tag_reaches_the_expected_commit()
    {
        var url = Environment.GetEnvironmentVariable("SCAN_IT_REPO_URL");
        var tag = Environment.GetEnvironmentVariable("SCAN_IT_TAG");
        var commit = Environment.GetEnvironmentVariable("SCAN_IT_COMMIT");
        Skip.If(string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(commit),
            "SCAN_IT_REPO_URL, SCAN_IT_TAG i SCAN_IT_COMMIT nie ustawione — test integracyjny pominięty");

        var settings = new GitOptions();
        new ConfigurationBuilder().AddEnvironmentVariables().Build().GetSection(GitOptions.SectionName).Bind(settings);
        Skip.If(string.IsNullOrEmpty(settings.Token), "Git__Token nie ustawiony — test integracyjny pominięty");

        var target = Path.Combine(Path.GetTempPath(), "scan-it-" + Guid.NewGuid().ToString("N"));
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var checkout = new GitCliCheckout(Options.Create(settings), runner, NullLogger<GitCliCheckout>.Instance);
        try
        {
            var result = await checkout.CheckoutAsync(url!, tag!, commit!, target, CancellationToken.None);

            Assert.IsType<GitCheckoutResult.Success>(result);
            Assert.True(Directory.Exists(Path.Combine(target, ".git")));
        }
        finally
        {
            DeleteDirectory(target);
        }
    }

    private static ProcessRunResult Exited(int exitCode, string stdout = "", string stderr = "")
        => new(ProcessOutcome.Exited, exitCode, stdout, stderr);

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // Git marks pack files read-only.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessRunResult> _results = new();

        public List<(ProcessStartInfo StartInfo, TimeSpan Timeout)> Calls { get; } = [];

        public void Enqueue(ProcessRunResult result) => _results.Enqueue(result);

        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, int maxCapturedChars, CancellationToken cancellationToken)
        {
            Calls.Add((startInfo, timeout));
            return Task.FromResult(_results.Dequeue());
        }
    }
}

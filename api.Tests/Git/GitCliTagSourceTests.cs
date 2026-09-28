using System.Text;
using securitycheck_portal.Core.Git;

namespace securitycheck_portal.Tests.Git;

public sealed class GitCliTagSourceTests
{
    private const string Url = "https://git.internal/team/app.git";
    private const string Token = "s3cr3t-pat-value";

    private static readonly GitOptions Settings = new()
    {
        ExecutablePath = @"C:\Program Files\Git\cmd\git.exe",
        AllowedHosts = ["git.internal"],
        UserName = "pat",
        Token = Token,
    };

    private static readonly string ExpectedHeader =
        "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"pat:{Token}"));

    [Fact]
    public void Token_is_only_in_the_environment_scoped_to_the_url()
    {
        var startInfo = GitCliTagSource.CreateStartInfo(Settings, Url, isWindows: true);

        Assert.DoesNotContain(startInfo.ArgumentList,
            a => a.Contains(Token) || a.Contains("Authorization") || a.Contains("extraHeader"));
        Assert.Equal("1", startInfo.Environment["GIT_CONFIG_COUNT"]);
        Assert.Equal($"http.{Url}.extraHeader", startInfo.Environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal(ExpectedHeader, startInfo.Environment["GIT_CONFIG_VALUE_0"]);
    }

    [Fact]
    public void Url_comes_last_after_end_of_options()
    {
        var startInfo = GitCliTagSource.CreateStartInfo(Settings, Url, isWindows: true);

        Assert.Equal(Settings.ExecutablePath, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(
            [
                "-c", "credential.helper=",
                "-c", "http.followRedirects=false",
                "-c", "http.sslVerify=true",
                "-c", "http.lowSpeedLimit=1000",
                "-c", "http.lowSpeedTime=20",
                "-c", "http.sslBackend=schannel",
                "ls-remote", "--tags", "--exit-code", "--end-of-options", Url,
            ],
            startInfo.ArgumentList);
    }

    [Fact]
    public void Schannel_is_only_set_on_windows()
    {
        var startInfo = GitCliTagSource.CreateStartInfo(Settings, Url, isWindows: false);

        Assert.DoesNotContain("http.sslBackend=schannel", startInfo.ArgumentList);
    }

    [Fact]
    public void Git_never_prompts_and_speaks_only_https()
    {
        var environment = GitCliTagSource.CreateStartInfo(Settings, Url, isWindows: true).Environment;

        Assert.Equal("0", environment["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("false", environment["GCM_INTERACTIVE"]);
        Assert.Equal("https", environment["GIT_ALLOW_PROTOCOL"]);
    }

    [Fact]
    public void Inherited_git_settings_and_system_and_global_config_are_ignored()
    {
        Environment.SetEnvironmentVariable("GIT_SSL_NO_VERIFY", "1");
        try
        {
            var environment = GitCliTagSource.CreateStartInfo(Settings, Url, isWindows: true).Environment;

            Assert.False(environment.ContainsKey("GIT_SSL_NO_VERIFY"));
            Assert.Equal("1", environment["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal("/dev/null", environment["GIT_CONFIG_GLOBAL"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_SSL_NO_VERIFY", null);
        }
    }

    [Fact]
    public void Options_do_not_print_the_token()
    {
        Assert.DoesNotContain(Token, Settings.ToString());
        Assert.DoesNotContain(Token, new GitTagListing.Failure(GitErrorKind.Failed, 128).ToString());
    }
}

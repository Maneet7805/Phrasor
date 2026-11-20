using Phrasor.Core.Feedback;

namespace Phrasor.Core.Tests;

public class SecretScannerTests
{
    // All fake: built from repeated characters, never a real key.
    [Theory]
    [InlineData("use sk-abcdefghijklmnopqrstuv12 for the call")]
    [InlineData("sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("GROQ=gsk_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("github_pat_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("key AKIAABCDEFGHIJKLMNOP here")]
    [InlineData("xoxb-1234567890-abcdefghij")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEAabcdefghijklmnop")]
    [InlineData("{\"key\":\"-----BEGIN PRIVATE KEY-----\\nAAAAAAAAAAAAAAAAAAAAAAAA\\n-----END PRIVATE KEY-----\"}")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nProc-Type: 4,ENCRYPTED\nDEK-Info: AES-128-CBC,AAAA0000AAAA0000\n\nAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\\nProc-Type: 4,ENCRYPTED\\nDEK-Info: AES-128-CBC,AAAA0000\\n\\nAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("eyJhbGciOiJIUzI1.eyJzdWIiOiIxMjM0.abcdefghijkl")]
    [InlineData("password = hunter2hunter2")]
    [InlineData("{\"api_key\": \"abcdef123456\"}")]
    [InlineData("TOKEN: abcdefghij12")]
    public void Credential_shapes_are_flagged(string text) =>
        Assert.True(SecretScanner.LooksLikeSecret(text));

    [Theory]
    [InlineData("explain how a password reset email works")]
    [InlineData("my token count is high and the secret menu is empty")]
    [InlineData("the sk-learn tutorial is short")]
    [InlineData("token: none")]
    [InlineData("Write a function that checks the password length and returns a bool.")]
    [InlineData("var user = GetUser(id); if (user.IsActive) { Save(user); }")]
    [InlineData("git commit -m \"fix: handle skip-list ask-for-help case\"")]
    [InlineData("Set the secret to rotate every 90 days; the token = short")]
    [InlineData("def login(user: str, password: Optional[str] = None)")]
    [InlineData("function f(token: CancellationToken) {}")]
    [InlineData("var token = GenerateToken(user); return token;")]
    [InlineData("Password: required. Username: optional.")]
    [InlineData("Create a k8s secret: my-app-credentials and mount it")]
    [InlineData("?api_key=YOUR_API_KEY")]
    [InlineData("API_KEY=<your-key-here>")]
    [InlineData("Rename branch pk-migration-for-user-table")]
    [InlineData("see sk-learn-pipeline-tutorial-for-beginners")]
    [InlineData("Explain what Bearer authentication-scheme-description does")]
    [InlineData("the header is -----BEGIN PRIVATE KEY-----")]
    [InlineData("-----BEGIN PRIVATE KEY----- a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: a: !")]
    [InlineData("AWS docs use AKIAIOSFODNN7EXAMPLE as a sample id")]
    [InlineData("")]
    public void Ordinary_text_is_not_flagged(string text) =>
        Assert.False(SecretScanner.LooksLikeSecret(text));

    // The header-line loop once backtracked without end on text like this; a hang here would freeze the hotkey.
    [Theory]
    [InlineData(" a: a")]
    [InlineData(" aaa:x")]
    [InlineData(" ab:c d e f")]
    public void Long_header_like_text_is_checked_quickly(string unit)
    {
        var text = "-----BEGIN PRIVATE KEY-----" + string.Concat(Enumerable.Repeat(unit, 400)) + "!";
        var task = Task.Run(() => SecretScanner.LooksLikeSecret(text));
        Assert.True(task.Wait(TimeSpan.FromSeconds(5)), "the check did not finish in 5 seconds");
        Assert.False(task.Result);
    }

    [Fact]
    public void A_private_key_with_a_header_line_that_has_no_value_is_flagged() =>
        Assert.True(SecretScanner.LooksLikeSecret("-----BEGIN PRIVATE KEY-----\nProc-Type:\nAAAAAAAAAAAAAAAAAAAAAAAA"));
}

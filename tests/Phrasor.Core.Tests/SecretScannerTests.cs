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
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nComment: a:b: c\nX-Empty:\n\nAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\\r\\nProc-Type: 4,ENCRYPTED\\r\\nDEK-Info: AES-128-CBC,AAAA0000\\r\\n\\r\\nAAAAAAAAAAAAAAAAAAAAAAAA")]
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
}

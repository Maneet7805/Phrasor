using System.Security.Cryptography;
using Phrasor.Core.History;

namespace Phrasor.App.Infrastructure;

/// <summary>History encryption with Windows DPAPI (CurrentUser scope), with its own entropy so it cannot decrypt the API-key file or the reverse.</summary>
internal sealed class DpapiBlobProtector : IBlobProtector
{
    private static readonly byte[] Entropy = "Phrasor.v1.history"u8.ToArray();

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData) => ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}

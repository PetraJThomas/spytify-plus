using System;
using System.Security.Cryptography;
using System.Text;

namespace EspionSpotify.Wpf
{
    /// <summary>
    /// At-rest protection for sensitive settings using Windows DPAPI, scoped to the current
    /// user account (only this Windows user can decrypt). Encrypted values are prefixed so
    /// legacy plaintext values still read and can be migrated forward.
    /// </summary>
    internal static class Crypto
    {
        private const string Prefix = "enc:";

        public static bool IsEncrypted(string stored) =>
            !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);

        public static string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return plain;
            try
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(bytes);
            }
            catch
            {
                return plain; // never lose the value if protection fails
            }
        }

        public static string Decrypt(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;
            if (!IsEncrypted(stored)) return stored; // legacy plaintext
            try
            {
                var bytes = Convert.FromBase64String(stored.Substring(Prefix.Length));
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
            }
            catch
            {
                return ""; // unreadable (e.g. copied from another machine/user)
            }
        }
    }
}

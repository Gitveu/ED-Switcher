using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace EDAccountSwitcher.Core
{
    public enum CredStatus
    {
        Found,
        NotFound,
        UnexpectedFormat,
        Failure
    }

    public sealed record CredResult(
        CredStatus Status,
        string? Username = null,
        string? EncryptedPassword = null,
        string? Token = null,
        string? Message = null)
    {
        public static CredResult Found(string user, string pass, string? token) =>
            new(CredStatus.Found, user, pass, token);
        public static CredResult NotFound(string path) =>
            new(CredStatus.NotFound, Message: $"Credential file not found: {path}");
        public static CredResult UnexpectedFormat(string path) =>
            new(CredStatus.UnexpectedFormat, Message: $"Unable to parse credentials at '{path}'. Unexpected format");
        public static CredResult Failure(string message) =>
            new(CredStatus.Failure, Message: message);
    }

    public sealed class CredStore
    {
        private readonly byte[] _salt;

        public CredStore(byte[] salt, string? credDir = null)
        {
            _salt = salt ?? throw new ArgumentNullException(nameof(salt));
            CredDir = credDir ?? DefaultCredDir;
        }

        public string CredDir { get; }

        public static string DefaultCredDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "min-ed-launcher");

        public static CredStore FromEdInstallDir(string edInstallDir, string? credDir = null)
        {
            var dllPath = Path.Combine(edInstallDir, "ClientSupport.dll");
            if (!File.Exists(dllPath))
                throw new FileNotFoundException($"Unable to find ClientSupport.dll in '{edInstallDir}'", dllPath);

            var assembly = Assembly.LoadFrom(dllPath);
            var decoderRing = assembly.GetType("ClientSupport.DecoderRing")
                ?? throw new InvalidOperationException("Unable to reflect ClientSupport.DecoderRing type for salt");
            var saltField = decoderRing.GetField("salt", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Unable to reflect salt field");
            var salt = (byte[]?)saltField.GetValue(null)
                ?? throw new InvalidOperationException("Salt field was null");

            return new CredStore(salt, credDir);
        }

        public string CredPathForProfile(string profile) =>
            Path.Combine(CredDir, $".frontier-{profile.ToLowerInvariant()}.cred");

        public string Encrypt(string text)
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.Unicode.GetBytes(text), _salt, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string Decrypt(string text)
        {
            var unprotected = ProtectedData.Unprotect(
                Convert.FromBase64String(text), _salt, DataProtectionScope.CurrentUser);
            return Encoding.Unicode.GetString(unprotected);
        }

        public CredResult ReadCredentials(string path)
        {
            if (!File.Exists(path)) return CredResult.NotFound(path);

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e) { return CredResult.Failure(e.Message); }

            return lines.Length switch
            {
                2 => CredResult.Found(lines[0], lines[1], null),
                3 => DecryptToken(lines),
                _ => CredResult.UnexpectedFormat(path)
            };

            CredResult DecryptToken(string[] l)
            {
                try { return CredResult.Found(l[0], l[1], Decrypt(l[2])); }
                catch (Exception e) { return CredResult.Failure(e.ToString()); }
            }
        }

        public void SaveCredentials(string path, string username, string encryptedPassword, string? machineToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var nl = Environment.NewLine;
            var content = machineToken is not null
                ? $"{username}{nl}{encryptedPassword}{nl}{Encrypt(machineToken)}"
                : $"{username}{nl}{encryptedPassword}";
            File.WriteAllText(path, content);
        }

        public void DiscardToken(string path)
        {
            var lines = File.ReadAllLines(path);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines.Take(2)));
        }

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public IReadOnlyList<(string Profile, string Email)> ListAccounts()
        {
            if (!Directory.Exists(CredDir))
                return Array.Empty<(string Profile, string Email)>();

            var accounts = new List<(string Profile, string Email)>();
            foreach (var file in Directory.EnumerateFiles(CredDir, ".frontier-*.cred"))
            {
                var name = Path.GetFileName(file);
                var profile = name.Substring(".frontier-".Length, name.Length - ".frontier-".Length - ".cred".Length);
                var result = ReadCredentials(file);
                if (result.Status == CredStatus.Found && result.Username is not null)
                    accounts.Add((profile, result.Username));
            }
            return accounts.OrderBy(a => a.Profile, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
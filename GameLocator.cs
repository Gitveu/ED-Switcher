using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EDAccountSwitcher.Core
{
    public class InstalledProduct
    {
        public string Name { get; set; }
        public string DirectoryName { get; set; }
        public Version Version { get; set; }
        public string Executable { get; set; }
        public string Filter { get; set; }

        public InstalledProduct(string name, string directoryName, Version version, string executable, string filter)
        {
            Name = name;
            DirectoryName = directoryName;
            Version = version;
            Executable = executable;
            Filter = filter;
        }
    }

    public static class GameLocator
    {
        public static bool IsValidInstallDir(string? path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "EDLaunch.exe"));

        public static string? FindInstallDir(string? configuredGameLocation = null)
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new List<string?>
            {
                AppContext.BaseDirectory,
                Path.Combine(programFilesX86, @"Steam\steamapps\common\Elite Dangerous"),
                Path.Combine(programFilesX86, "Frontier"),
                Path.Combine(localAppData, "Frontier_Developments"),
                configuredGameLocation
            };

            return candidates.FirstOrDefault(IsValidInstallDir);
        }

        public static string? FindMinEdLauncher(string? edInstallDir, string? configuredPath = null)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return configuredPath;
            if (IsValidInstallDir(edInstallDir))
            {
                var candidate = Path.Combine(edInstallDir!, "MinEdLauncher.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        public static IReadOnlyList<InstalledProduct> EnumerateProducts(string edInstallDir)
        {
            var productsDir = Path.Combine(edInstallDir, "Products");
            if (!Directory.Exists(productsDir))
                return Array.Empty<InstalledProduct>();

            var products = new List<InstalledProduct>();
            bool hasOdyssey = false;
            InstalledProduct? odysseyProduct = null;

            foreach (var dir in Directory.EnumerateDirectories(productsDir))
            {
                var versionInfoPath = Path.Combine(dir, "VersionInfo.txt");
                if (!File.Exists(versionInfoPath))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(versionInfoPath));
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("Version", out var v) || !Version.TryParse(v.ToString(), out var version))
                        continue;
                    if (!root.TryGetProperty("executable", out var exe))
                        continue;

                    var rawName = root.TryGetProperty("name", out var n) ? n.ToString() : Path.GetFileName(dir);
                    var dirName = Path.GetFileName(dir);

                    var displayName = GetDisplayName(rawName, dirName, version);
                    var filter = FilterFor(dirName, version);

                    var product = new InstalledProduct(displayName, dirName, version, exe.ToString(), filter);
                    products.Add(product);

                    if (dirName.ToLowerInvariant() == "elite-dangerous-odyssey-64")
                    {
                        hasOdyssey = true;
                        odysseyProduct = product;
                    }
                }
                catch (JsonException) { }
            }

            if (hasOdyssey && odysseyProduct != null)
            {
                products.Add(new InstalledProduct(
                    "Elite Dangerous: Horizons (Live)",
                    odysseyProduct.DirectoryName,
                    odysseyProduct.Version,
                    odysseyProduct.Executable,
                    "edh4"));
            }

            return products.OrderBy(p => p.Name).ToList();
        }

        private static string GetDisplayName(string rawName, string directoryName, Version version)
        {
            return directoryName.ToLowerInvariant() switch
            {
                "elite-dangerous-odyssey-64" => "Elite Dangerous: Odyssey (Live)",
                "elite-dangerous-64" => version.Major >= 4 ? "Elite Dangerous: Horizons (Live)" : "Legacy Elite Dangerous",
                "elite-dangerous-pub-test" => "Elite Dangerous: Public Test",
                "multiplayer-arena-64" => "Elite Dangerous: Arena",
                "combat_tutorial_demo" => "Single Player Combat Training",
                _ => rawName
            };
        }

        private static string FilterFor(string directoryName, Version version) =>
            directoryName.ToLowerInvariant() switch
            {
                "elite-dangerous-odyssey-64" => "edo",
                "elite-dangerous-64" => version.Major >= 4 ? "edh4" : "ed",
                var other => other
            };
    }
}
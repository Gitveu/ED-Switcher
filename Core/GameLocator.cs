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

        public InstalledProduct(
            string name,
            string directoryName,
            Version version,
            string executable,
            string filter)
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
        public static bool IsValidInstallDir(string? path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && File.Exists(
                    Path.Combine(path, "EDLaunch.exe"));
        }

        public static string? FindInstallDir(
            string? configuredGameLocation = null)
        {
            var programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            var localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            var candidates = new List<string?>
            {
                AppContext.BaseDirectory,

                Path.Combine(
                    programFilesX86,
                    @"Steam\steamapps\common\Elite Dangerous"),

                Path.Combine(
                    programFilesX86,
                    "Frontier"),

                Path.Combine(
                    localAppData,
                    "Frontier_Developments"),

                configuredGameLocation
            };

            return candidates.FirstOrDefault(
                IsValidInstallDir);
        }

        public static string? FindMinEdLauncher(
            string? edInstallDir,
            string? configuredPath = null)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath)
                && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            if (IsValidInstallDir(edInstallDir))
            {
                var candidate = Path.Combine(
                    edInstallDir!,
                    "MinEdLauncher.exe");

                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        public static IReadOnlyList<InstalledProduct> EnumerateProducts(
            string edInstallDir)
        {
            var productsDir = Path.Combine(
                edInstallDir,
                "Products");

            if (!Directory.Exists(productsDir))
                return Array.Empty<InstalledProduct>();

            var products = new List<InstalledProduct>();

            InstalledProduct? odysseyProduct = null;

            foreach (var dir in Directory.EnumerateDirectories(productsDir))
            {
                var versionInfoPath = Path.Combine(
                    dir,
                    "VersionInfo.txt");

                if (!File.Exists(versionInfoPath))
                    continue;

                try
                {
                    using var document = JsonDocument.Parse(
                        File.ReadAllText(versionInfoPath));

                    var root = document.RootElement;

                    if (!root.TryGetProperty(
                            "Version",
                            out var versionElement))
                    {
                        continue;
                    }

                    if (!Version.TryParse(
                            versionElement.ToString(),
                            out var version))
                    {
                        continue;
                    }

                    if (!root.TryGetProperty(
                            "executable",
                            out var executableElement))
                    {
                        continue;
                    }

                    var directoryName =
                        Path.GetFileName(dir);

                    if (string.IsNullOrWhiteSpace(directoryName))
                        continue;

                    var rawName =
                        root.TryGetProperty(
                            "name",
                            out var nameElement)
                            ? nameElement.ToString()
                            : directoryName;

                    var displayName = GetDisplayName(
                        rawName,
                        directoryName,
                        version);

                    var filter = FilterFor(
                        directoryName,
                        version);

                    var product = new InstalledProduct(
                        displayName,
                        directoryName,
                        version,
                        executableElement.ToString(),
                        filter);

                    products.Add(product);

                    /*
                     * elite-dangerous-odyssey-64 — это Odyssey Live.
                     * Из него дополнительно создаётся Horizons Live
                     * с фильтром edh4.
                     */
                    if (string.Equals(
                            directoryName,
                            "elite-dangerous-odyssey-64",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        odysseyProduct = product;
                    }
                }
                catch (JsonException)
                {
                    // Некорректный VersionInfo.txt пропускаем.
                }
                catch (IOException)
                {
                    // Недоступный файл пропускаем.
                }
                catch (UnauthorizedAccessException)
                {
                    // Нет доступа к файлу — пропускаем.
                }
            }

            /*
             * Horizons Live использует каталог Odyssey,
             * но запускается с фильтром edh4.
             */
            if (odysseyProduct != null)
            {
                AddProductIfMissing(
                    products,
                    new InstalledProduct(
                        "Elite Dangerous: Horizons (Live)",
                        odysseyProduct.DirectoryName,
                        odysseyProduct.Version,
                        odysseyProduct.Executable,
                        "edh4"));
            }

            return products
                .GroupBy(
                    product =>
                        $"{product.DirectoryName}|{product.Filter}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(GetSortOrder)
                .ThenBy(
                    product => product.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddProductIfMissing(
            List<InstalledProduct> products,
            InstalledProduct product)
        {
            bool alreadyExists = products.Any(existing =>
                string.Equals(
                    existing.DirectoryName,
                    product.DirectoryName,
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    existing.Filter,
                    product.Filter,
                    StringComparison.OrdinalIgnoreCase));

            if (!alreadyExists)
                products.Add(product);
        }

        private static string GetDisplayName(
            string rawName,
            string directoryName,
            Version version)
        {
            return directoryName.ToLowerInvariant() switch
            {
                /*
                 * Это именно Horizons Legacy,
                 * а не обычный Legacy и не Horizons Live.
                 */
                "elite-dangerous-64" =>
                    "Elite Dangerous: Horizons (Legacy)",

                "elite-dangerous-odyssey-64" =>
                    "Elite Dangerous: Odyssey (Live)",

                "elite-dangerous-pub-test" =>
                    "Elite Dangerous: Public Test",

                "multiplayer-arena-64" =>
                    "Elite Dangerous: Arena",

                "combat_tutorial_demo" =>
                    "Single Player Combat Training",

                /*
                 * version оставлен в сигнатуре,
                 * потому что он используется общей схемой
                 * чтения VersionInfo.txt.
                 */
                _ => rawName
            };
        }

        private static string FilterFor(
            string directoryName,
            Version version)
        {
            return directoryName.ToLowerInvariant() switch
            {
                /*
                 * Odyssey Live.
                 */
                "elite-dangerous-odyssey-64" =>
                    "edo",

                /*
                 * Horizons Legacy.
                 *
                 * MinEdLauncher получает:
                 * /edh
                 */
                "elite-dangerous-64" =>
                    "edh",

                /*
                 * Если в Products существует отдельный
                 * каталог обычного Legacy, оставляем для него /ed.
                 */
                "elite-dangerous-legacy-64" =>
                    "ed",

                "elite-dangerous-legacy" =>
                    "ed",

                /*
                 * Arena.
                 */
                "multiplayer-arena-64" =>
                    "eda",

                var other =>
                    other
            };
        }

        private static int GetSortOrder(
            InstalledProduct product)
        {
            return product.Filter.ToLowerInvariant() switch
            {
                "edo" => 0,
                "edh4" => 1,
                "edh" => 2,
                "ed" => 3,
                "eda" => 4,
                _ => 100
            };
        }
    }
}

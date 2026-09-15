using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EDAccountSwitcher.Updating
{
    public sealed class UpdateInfo
    {
        public Version Version { get; set; }
        public string Tag { get; set; }
        public string Notes { get; set; }
        public string Url { get; set; }
        public string AssetName { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
    }

    public static class UpdateService
    {
        public const string ApplySwitch = "--apply-update";
        private const string Owner = "Gitveu";
        private const string Repo = "ED-Switcher";

        private static readonly HttpClient Http = BuildClient();

        public static string AppDirectory => Path.GetDirectoryName(Environment.ProcessPath);

        private static string DataDirectory
        {
            get
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ED Switcher");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        private static string LogPath => Path.Combine(DataDirectory, "update.log");
        private static string SkippedPath => Path.Combine(DataDirectory, "skipped-version.txt");
        private static string TempRoot => Path.Combine(Path.GetTempPath(), "EDSwitcherUpdate");

        // ---------- проверка обновлений ----------

        private static HttpClient BuildClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ED-Switcher-Updater");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static Version Current =>
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

        // Теги на GitHub двучастные (v1.5), версия сборки всегда четырёхчастная,
        // поэтому лишний нулевой сегмент в подписи не показываем.
        public static string CurrentTag
        {
            get
            {
                var version = Current;
                var build = Math.Max(version.Build, 0);
                return build == 0
                    ? "v" + version.Major + "." + version.Minor
                    : "v" + version.Major + "." + version.Minor + "." + build;
            }
        }

        public static string SkippedTag
        {
            get
            {
                try { return File.Exists(SkippedPath) ? File.ReadAllText(SkippedPath).Trim() : ""; }
                catch { return ""; }
            }
        }

        public static void SkipTag(string tag)
        {
            try { File.WriteAllText(SkippedPath, tag ?? ""); } catch { }
        }

        // null = обновлений нет
        public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
        {
            using var response = await Http.GetAsync(
                "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest", ct);

            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if ((int)response.StatusCode == 403) throw new IOException("GitHub API: превышен лимит запросов");
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() : null;
            if (!TryParseVersion(tag, out var version) || version <= Current) return null;

            var asset = PickAsset(root);
            if (asset == null) return null;

            string sha = null;
            if (asset.Value.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String)
            {
                var value = digest.GetString();
                if (value != null && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = value.Substring(7);
            }

            return new UpdateInfo
            {
                Version = version,
                Tag = tag,
                Notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                Url = asset.Value.GetProperty("browser_download_url").GetString(),
                AssetName = asset.Value.GetProperty("name").GetString(),
                Size = asset.Value.GetProperty("size").GetInt64(),
                Sha256 = sha
            };
        }

        private static JsonElement? PickAsset(JsonElement release)
        {
            if (!release.TryGetProperty("assets", out var assets)) return null;

            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            JsonElement? any = null, matching = null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (any == null) any = asset;
                if (name.IndexOf(arch, StringComparison.OrdinalIgnoreCase) >= 0) matching = asset;
            }
            return matching ?? any;
        }

        private static bool TryParseVersion(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag)) return false;

            var clean = tag.TrimStart('v', 'V');
            var dash = clean.IndexOf('-');
            if (dash > 0) clean = clean.Substring(0, dash);

            var parts = clean.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return false;

            var normalized = string.Join(".", parts.Concat(Enumerable.Repeat("0", Math.Max(0, 3 - parts.Length))));
            return Version.TryParse(normalized, out version);
        }

        // ---------- загрузка ----------

        // Возвращает путь к распакованному архиву
        public static async Task<string> StageAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct)
        {
            var root = Path.Combine(TempRoot, info.Version.ToString());
            if (Directory.Exists(root)) Directory.Delete(root, true);

            var staging = Path.Combine(root, "new");
            Directory.CreateDirectory(staging);
            var archive = Path.Combine(root, info.AssetName);

            using (var response = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? info.Size;

                using var source = await response.Content.ReadAsStreamAsync(ct);
                using var target = File.Create(archive);

                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;
                    progress?.Report(total > 0 ? (double)received / total : 0);
                }
            }

            if (!string.IsNullOrEmpty(info.Sha256))
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(archive);
                var actual = Convert.ToHexString(await sha.ComputeHashAsync(stream, ct));
                if (!actual.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA256 не совпал: " + actual);
            }

            ZipFile.ExtractToDirectory(archive, staging);
            File.Delete(archive);

            // если в архиве один вложенный каталог — поднимаем его наверх
            var entries = Directory.GetFileSystemEntries(staging);
            if (entries.Length == 1 && Directory.Exists(entries[0]) && Directory.GetFiles(staging).Length == 0)
                return entries[0];

            return staging;
        }

        public static bool CanSelfUpdate(out string error)
        {
            error = null;
            try
            {
                var probe = Path.Combine(AppDirectory, ".update-probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void OpenReleasePage() =>
            Process.Start(new ProcessStartInfo("https://github.com/" + Owner + "/" + Repo + "/releases/latest")
            {
                UseShellExecute = true
            });

        // ---------- запуск применения (из окна обновления) ----------

        // Запускает новый exe из staging как исполнителя и завершает текущий процесс.
        // Возврата отсюда нет.
        public static void StartApply(string staging, string tag)
        {
            var exeName = Path.GetFileName(Environment.ProcessPath);
            var stagedExe = Path.Combine(staging, exeName);
            if (!File.Exists(stagedExe)) throw new FileNotFoundException("В архиве нет " + exeName, stagedExe);

            Process.Start(new ProcessStartInfo(stagedExe,
                ApplySwitch + " --pid " + Environment.ProcessId + " --target \"" + AppDirectory + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            });

            Log("запущен исполнитель: " + stagedExe + " -> " + AppDirectory);
            Environment.Exit(0);   // снимаем локи на файлы целевого каталога
        }

        // ---------- исполнитель ----------

        public static int ApplyStagedUpdate(string[] args)
        {
            var targetText = GetArg(args, "--target");
            if (!int.TryParse(GetArg(args, "--pid"), out var pid) || string.IsNullOrWhiteSpace(targetText))
                return 1;

            var target = Path.GetFullPath(targetText);
            var self = Environment.ProcessPath ?? "";
            var source = Path.GetDirectoryName(self);
            var exeName = Path.GetFileName(self);
            var fresh = target + ".new";
            var backup = target + ".old";

            // CWD не должен лежать внутри переносимого каталога
            Directory.SetCurrentDirectory(Path.GetTempPath());

            if (string.IsNullOrEmpty(source)) return 2;
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return 3;
            if (!File.Exists(Path.Combine(target, exeName))) return 4;

            Log("исполнитель: ждём PID " + pid + ", цель " + target);
            if (!WaitForRelease(Path.Combine(target, exeName), pid, TimeSpan.FromSeconds(90)))
            {
                Log("цель занята, применение отменено");
                return 5;
            }

            DeleteDirectory(fresh);
            if (!Robocopy(source, fresh)) return 6;      // копия на том же томе, что и цель

            DeleteDirectory(backup);
            try
            {
                Directory.Move(target, backup);          // старая версия целиком в сторону
                try
                {
                    Directory.Move(fresh, target);       // новая версия занимает место
                }
                catch
                {
                    Directory.Move(backup, target);      // откат
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log("подмена каталога не удалась: " + ex.Message);
                return 7;
            }

            Log("обновление применено");
            Process.Start(new ProcessStartInfo(Path.Combine(target, exeName)) { UseShellExecute = true });
            return 0;
        }

        private static bool WaitForRelease(string targetExe, int pid, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var alive = false;
                try
                {
                    using var process = Process.GetProcessById(pid);
                    alive = !process.HasExited;
                }
                catch (ArgumentException) { }   // процесс уже мёртв

                if (!alive && CanWriteExclusive(targetExe)) return true;
                Thread.Sleep(300);
            }
            return false;
        }

        private static bool CanWriteExclusive(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static bool Robocopy(string source, string destination)
        {
            try
            {
                var psi = new ProcessStartInfo("robocopy",
                    "\"" + source + "\" \"" + destination + "\" /MIR /NFL /NDL /NJH /NJS /NP /R:5 /W:1")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var copy = Process.Start(psi);
                var output = copy.StandardOutput.ReadToEnd();
                copy.WaitForExit();
                Log("robocopy код " + copy.ExitCode + Environment.NewLine + output);
                return copy.ExitCode < 8;   // 0..7 у robocopy = успех
            }
            catch (Exception ex)
            {
                Log("robocopy не запустился: " + ex.Message);
                return false;
            }
        }

        // ---------- уборка ----------

        public static void CleanupAfterUpdate()
        {
            DeleteDirectory(TempRoot);
            DeleteDirectory(AppDirectory + ".new");
            DeleteDirectory(AppDirectory + ".old");
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
                // занято умирающим процессом — уберётся при следующем запуске
            }
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("s") + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        private static string GetArg(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
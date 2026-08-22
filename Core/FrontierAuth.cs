using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EDAccountSwitcher.Core
{
    public static class Hex
    {
        public static string EncodeUtf8(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    public abstract record SignInResult
    {
        public sealed record Success(string AuthToken, string? MachineToken) : SignInResult;
        public sealed record RequiresTwoFactor(string EncCode) : SignInResult;
        public sealed record Error(string Message) : SignInResult;
    }

    public abstract record TwoFactorResult
    {
        public sealed record Success(string MachineToken) : TwoFactorResult;
        public sealed record Error(string Message) : TwoFactorResult;
    }

    public sealed class FrontierAuth : IDisposable
    {
        public const string DefaultApiBase = "https://api.zaonce.net";

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _machineId;
        private readonly string _lang;

        private bool _timeSynced;
        private long _remoteTimestamp;
        private DateTime _syncedAtUtc;

        public FrontierAuth(string machineId, string lang = "en", HttpClient? httpClient = null, string apiBase = DefaultApiBase)
        {
            _machineId = machineId;
            _lang = lang;
            _ownsHttp = httpClient is null;
            _http = httpClient ?? new HttpClient();
            if (_http.BaseAddress is null)
                _http.BaseAddress = new Uri(apiBase);
            if (!_http.DefaultRequestHeaders.UserAgent.Any())
                _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EDAccountSwitcher/1.0/Win64");
        }

        public async Task SyncServerTimeAsync(CancellationToken ct = default)
        {
            var localTimestamp = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
            var timestamp = localTimestamp;
            try
            {
                using var response = await _http.GetAsync("/1.1/server/time", ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    await using var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(content, cancellationToken: ct).ConfigureAwait(false);
                    if (doc.RootElement.TryGetProperty("unixTimestamp", out var ts) && ts.TryGetInt64(out var value))
                        timestamp = value;
                }
            }
            catch { }

            _remoteTimestamp = timestamp;
            _syncedAtUtc = DateTime.UtcNow;
            _timeSynced = true;
        }

        private string FTime()
        {
            var elapsed = (DateTime.UtcNow - _syncedAtUtc).TotalSeconds;
            return ((double)_remoteTimestamp + elapsed).ToString(CultureInfo.InvariantCulture);
        }

        private async Task EnsureTimeSyncedAsync(CancellationToken ct)
        {
            if (!_timeSynced)
                await SyncServerTimeAsync(ct).ConfigureAwait(false);
        }

        public async Task<SignInResult> SignInAsync(string email, string plaintextPassword, CancellationToken ct = default)
        {
            await EnsureTimeSyncedAsync(ct).ConfigureAwait(false);

            var query = new Dictionary<string, string>
            {
                ["email"] = Hex.EncodeUtf8(email),
                ["password"] = Hex.EncodeUtf8(plaintextPassword),
                ["machineId"] = _machineId,
                ["lang"] = _lang,
                ["fTime"] = FTime()
            };

            using var response = await PostAsync("/3.0/user/frontier/auth", query, ct).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            JsonDocument doc;
            try { doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false); }
            catch (JsonException e) { return new SignInResult.Error($"Couldn't parse json - {e.Message}"); }

            using (doc)
            {
                var root = doc.RootElement;
                if (response.IsSuccessStatusCode)
                {
                    if (root.TryGetProperty("encCode", out var encCode))
                        return new SignInResult.RequiresTwoFactor(encCode.ToString());

                    if (root.TryGetProperty("authToken", out var authToken))
                    {
                        var machineToken = root.TryGetProperty("machineToken", out var mt) ? mt.ToString() : null;
                        return new SignInResult.Success(authToken.ToString(), machineToken);
                    }
                    return new SignInResult.Error($"Unexpected response: {root}");
                }
                return new SignInResult.Error(FormatError(response, root, "errorCode"));
            }
        }

        public async Task<TwoFactorResult> SubmitTwoFactorAsync(string encCode, string plainCode, CancellationToken ct = default)
        {
            var query = new Dictionary<string, string>
            {
                ["machineId"] = _machineId,
                ["plainCode"] = plainCode,
                ["encCode"] = encCode,
                ["lang"] = _lang
            };

            using var response = await PostAsync("/3.0/user/frontier/token", query, ct).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            JsonDocument doc;
            try { doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false); }
            catch (JsonException e) { return new TwoFactorResult.Error($"Couldn't parse json - {e.Message}"); }

            using (doc)
            {
                var root = doc.RootElement;
                if (response.IsSuccessStatusCode)
                {
                    if (root.TryGetProperty("machineToken", out var token))
                        return new TwoFactorResult.Success(token.ToString());
                    return new TwoFactorResult.Error($"Unexpected response: {root}");
                }
                return new TwoFactorResult.Error(FormatError(response, root, "error_num"));
            }
        }

        private async Task<HttpResponseMessage> PostAsync(string path, IReadOnlyDictionary<string, string> queryParams, CancellationToken ct)
        {
            var query = string.Join("&", queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_http.BaseAddress!, path + "?" + query));
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }

        private static string FormatError(HttpResponseMessage response, JsonElement root, string errorProp)
        {
            var code = root.TryGetProperty(errorProp, out var c) ? c.ToString() : "Unknown";
            var message = root.TryGetProperty("message", out var m) ? m.ToString() : "Unknown";
            return $"{(int)response.StatusCode}: {message} - ErrorCode = {code}";
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }
}
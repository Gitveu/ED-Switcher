using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EDAccountSwitcher.Localization;

namespace EDAccountSwitcher.Updating
{
    public enum UpdateCheckState
    {
        UpToDate,
        Available,
        Failed
    }

    public sealed class UpdateCheckResult
    {
        public UpdateCheckState State { get; init; }
        public UpdateInfo Info { get; init; }
        public string Error { get; init; }
    }

    public sealed partial class UpdateDialog : ContentDialog
    {
        private readonly UpdateInfo _info;
        private CancellationTokenSource _cts;
        private bool _skipRequested;
        private bool _busy;

        public UpdateDialog(UpdateInfo info)
        {
            _info = info;
            InitializeComponent();

            VersionsText.Text = UpdateService.CurrentTag + " \u2192 " + info.Tag;
            SizeText.Text = LocalizationManager.Format("Update_Size",
                (info.Size / 1024d / 1024d).ToString("0.0"));

            RenderNotes(info.Notes);

            Closing += OnClosing;
        }

        // manual: true — запрос из настроек, пропущенная версия предлагается снова.
        // Диалог показывается только когда обновление есть; результат проверки отдаётся наружу.
        public static async Task<UpdateCheckResult> CheckAsync(XamlRoot xamlRoot, bool manual = false)
        {
            if (xamlRoot == null)
                return new UpdateCheckResult { State = UpdateCheckState.Failed, Error = "XamlRoot is null" };

            UpdateInfo info;
            try
            {
                info = await UpdateService.CheckAsync();
            }
            catch (Exception ex)
            {
                UpdateService.Log("проверка обновлений: " + ex.Message);
                return new UpdateCheckResult { State = UpdateCheckState.Failed, Error = ex.Message };
            }

            if (info == null)
                return new UpdateCheckResult { State = UpdateCheckState.UpToDate };

            if (!manual && info.Tag == UpdateService.SkippedTag)
                return new UpdateCheckResult { State = UpdateCheckState.UpToDate };

            if (!UpdateService.CanSelfUpdate(out var error))
            {
                if (manual)
                {
                    var manualDialog = new ContentDialog
                    {
                        XamlRoot = xamlRoot,
                        Title = LocalizationManager.Get("Update_Title"),
                        Content = LocalizationManager.Get("Update_Manual") + "\n" + error,
                        PrimaryButtonText = LocalizationManager.Get("Update_OpenPage"),
                        CloseButtonText = LocalizationManager.Get("Update_Later"),
                        DefaultButton = ContentDialogButton.Primary
                    };

                    if (await manualDialog.ShowAsync() == ContentDialogResult.Primary)
                        UpdateService.OpenReleasePage();
                }

                return new UpdateCheckResult
                {
                    State = UpdateCheckState.Failed,
                    Info = info,
                    Error = error
                };
            }

            var dialog = new UpdateDialog(info) { XamlRoot = xamlRoot };
            await dialog.ShowAsync();

            if (dialog._skipRequested)
            {
                UpdateService.SkipTag(info.Tag);
                return new UpdateCheckResult { State = UpdateCheckState.UpToDate };
            }

            return new UpdateCheckResult { State = UpdateCheckState.Available, Info = info };
        }

        // ---------- кнопки ----------

        private void OnSkipClick(object sender, RoutedEventArgs e)
        {
            _skipRequested = true;
            Hide();
        }

        private void OnLaterClick(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            CancelDownload();
        }

        private async void OnUpdateClick(object sender, RoutedEventArgs e)
        {
            ActionRow.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;

            _busy = true;
            _cts = new CancellationTokenSource();

            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = LocalizationManager.Get("Update_Starting");

            var token = _cts.Token;
            var progress = new Progress<double>(value =>
            {
                Progress.IsIndeterminate = false;
                Progress.Value = value * 100;
                StatusText.Text = LocalizationManager.Format("Update_Progress", (int)(value * 100));
            });

            try
            {
                var staging = await UpdateService.StageAsync(_info, progress, token);

                // страховка: отмена могла прийти между последним чтением и распаковкой
                token.ThrowIfCancellationRequested();

                Progress.IsIndeterminate = true;
                StatusText.Text = LocalizationManager.Get("Update_Restarting");

                UpdateService.StartApply(staging, _info.Tag);   // завершает процесс
            }
            catch (OperationCanceledException)
            {
                RestoreUi();
                StatusText.Text = LocalizationManager.Get("Update_Cancelled");
            }
            catch (Exception ex)
            {
                RestoreUi();
                StatusText.Text = ex.Message;
            }
            finally
            {
                _busy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            // Esc или клик по затемнению во время загрузки — отмена, а не закрытие
            if (!_busy) return;
            args.Cancel = true;
            CancelDownload();
        }

        private void CancelDownload()
        {
            if (_cts == null || _cts.IsCancellationRequested) return;
            StatusText.Text = LocalizationManager.Get("Update_Cancelling");
            _cts.Cancel();
        }

        private void RestoreUi()
        {
            Progress.Visibility = Visibility.Collapsed;
            Progress.IsIndeterminate = false;
            Progress.Value = 0;

            CancelButton.Visibility = Visibility.Collapsed;
            ActionRow.Visibility = Visibility.Visible;
        }

        // ---------- заметки ----------

        // Из тела релиза берём только секцию «What's new» вместе с её подзаголовками.
        // Всё, что идёт после следующего заголовка того же или более высокого уровня,
        // в диалог не попадает: Features, Installation, Requirements и прочее.
        private static string ExtractWhatsNew(string markdown)
        {
            var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u2019', '\'');
            var lines = text.Split('\n');

            var start = -1;
            var level = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.Length == 0 || line[0] != '#') continue;

                var hashes = 0;
                while (hashes < line.Length && line[hashes] == '#') hashes++;

                var title = line.Substring(hashes).Trim().Trim(':').Trim().ToLowerInvariant();

                if (start < 0)
                {
                    bool isWhatsNew =
                        title.StartsWith("what's new") || title.StartsWith("whats new") ||
                        title.StartsWith("what's changed") || title.StartsWith("whats changed") ||
                        title.StartsWith("что нового") || title.StartsWith("што новага");

                    if (!isWhatsNew) continue;

                    start = i + 1;
                    level = hashes;
                    continue;
                }

                if (hashes <= level)
                    return string.Join("\n", lines, start, i - start);
            }

            if (start < 0) return markdown;   // заголовка нет — показываем тело целиком

            return string.Join("\n", lines, start, lines.Length - start);
        }

        // Упрощённый markdown: заголовки, жирный, ссылки, списки
        private void RenderNotes(string markdown)
        {
            NotesHost.Children.Clear();
            if (string.IsNullOrWhiteSpace(markdown)) return;

            foreach (var raw in ExtractWhatsNew(markdown).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("---")) continue;

                var isHeading = line.StartsWith("#");
                var text = Regex.Replace(line, @"^(#+|[-*+]\s+|\d+\.\s+)", "");
                text = Regex.Replace(text, @"\[(.+?)\]\(.+?\)", "$1");
                text = Regex.Replace(text, @"[`*>_]", "").Trim();
                if (text.Length == 0) continue;

                var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
                if (isHeading) block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                NotesHost.Children.Add(block);
            }
        }
    }
}
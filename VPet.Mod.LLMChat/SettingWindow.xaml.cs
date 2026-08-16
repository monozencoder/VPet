using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VPet.Mod.LLMChat.Providers;
using VPet.Mod.LLMChat.Voice;

namespace VPet.Mod.LLMChat
{
    public partial class SettingWindow : Window
    {
        private readonly LLMChatSettings settings;
        private readonly SpeechPlayer testVoicePlayer = new SpeechPlayer(null);

        public SettingWindow(LLMChatSettings settings)
        {
            InitializeComponent();
            this.settings = settings;
            LoadFromSettings();
            Closed += (_, _) => testVoicePlayer.Stop();
        }

        private void LoadFromSettings()
        {
            foreach (ComboBoxItem item in cbProvider.Items)
            {
                if ((string)item.Tag == settings.Provider.ToString())
                {
                    cbProvider.SelectedItem = item;
                    break;
                }
            }
            if (cbProvider.SelectedItem == null)
                cbProvider.SelectedIndex = 0;

            UpdateModelChoices();
            tbModel.Text = settings.Model;
            tbCustomEndpoint.Text = settings.CustomEndpoint;
            tbSystemPrompt.Text = settings.SystemPrompt;
            cbProactiveEnabled.IsChecked = settings.ProactiveChatEnabled;
            tbProactiveInterval.Text = settings.ProactiveChatIntervalMinutes.ToString();
            tbProactiveChance.Text = settings.ProactiveChatChancePercent.ToString();

            cbVoiceEnabled.IsChecked = settings.VoiceEnabled;
            foreach (ComboBoxItem item in cbTtsProvider.Items)
            {
                if ((string)item.Tag == settings.TtsProvider.ToString())
                {
                    cbTtsProvider.SelectedItem = item;
                    break;
                }
            }
            if (cbTtsProvider.SelectedItem == null)
                cbTtsProvider.SelectedIndex = 0;

            sliderVoiceVolume.Value = settings.VoiceVolumePercent;
            UpdateVoiceVolumeLabel();
            testVoicePlayer.Volume = settings.VoiceVolumePercent / 100f;

            tbVoiceEndpoint.Text = settings.VoiceEndpoint;
            cbVoiceSpeaker.Items.Clear();
            cbVoiceSpeaker.Items.Add(new ComboBoxItem { Content = $"ID: {settings.VoiceSpeakerId} (未取得。「一覧取得」で選択可能)", Tag = settings.VoiceSpeakerId });
            cbVoiceSpeaker.SelectedIndex = 0;

            tbTtsOpenAiModel.Text = settings.OpenAiTtsModel;
            tbTtsOpenAiVoice.Text = settings.OpenAiTtsVoice;
            UpdateTtsOpenAiKeyLabel();

            tbAiVoiceInstallDir.Text = settings.AiVoiceInstallDir;
            cbAiVoicePreset.Items.Clear();
            cbAiVoicePreset.Items.Add(string.IsNullOrWhiteSpace(settings.AiVoicePresetName)
                ? new ComboBoxItem { Content = "(未取得。「一覧取得」で選択可能。空欄のままだとEditor側で選択中のボイスを使用)", Tag = "" }
                : new ComboBoxItem { Content = $"{settings.AiVoicePresetName} (未取得。「一覧取得」で選択可能)", Tag = settings.AiVoicePresetName });
            cbAiVoicePreset.SelectedIndex = 0;

            UpdateTtsProviderVisibility();

            cbVoiceInputEnabled.IsChecked = settings.VoiceInputEnabled;
            cbVoiceInputAutoSend.IsChecked = settings.VoiceInputAutoSend;
            cbMicSoundEnabled.IsChecked = settings.MicSoundEnabled;
            tbVoiceInputModel.Text = settings.VoiceInputModel;
            SetKeyStatus(tbVoiceInputKeyStatus, CredentialStore.Exists(LLMChatSettings.VoiceInputCredentialKey));

            UpdateApiKeyLabel();
            UpdateCustomEndpointVisibility();
        }

        private LlmProviderKind SelectedProvider =>
            (LlmProviderKind)System.Enum.Parse(typeof(LlmProviderKind), (string)((ComboBoxItem)cbProvider.SelectedItem).Tag);

        private TtsProviderKind SelectedTtsProvider =>
            (TtsProviderKind)System.Enum.Parse(typeof(TtsProviderKind), (string)((ComboBoxItem)cbTtsProvider.SelectedItem).Tag);

        private void UpdateTtsOpenAiKeyLabel()
        {
            SetKeyStatus(tbTtsOpenAiKeyStatus, CredentialStore.Exists(LLMChatSettings.OpenAiTtsCredentialKey));
        }

        private static void SetKeyStatus(TextBlock statusBlock, bool hasKey)
        {
            statusBlock.Text = hasKey ? "✓ 設定済み" : "未設定";
            statusBlock.Foreground = hasKey ? Brushes.Green : Brushes.Gray;
        }

        private void UpdateTtsProviderVisibility()
        {
            if (cbTtsProvider.SelectedItem == null)
                return;
            var provider = SelectedTtsProvider;
            spVoicevoxSettings.Visibility = provider == TtsProviderKind.Voicevox ? Visibility.Visible : Visibility.Collapsed;
            spOpenAiTtsSettings.Visibility = provider == TtsProviderKind.OpenAi ? Visibility.Visible : Visibility.Collapsed;
            spAiVoiceSettings.Visibility = provider == TtsProviderKind.AiVoice ? Visibility.Visible : Visibility.Collapsed;
        }

        private void cbTtsProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTtsProviderVisibility();
        }

        private void UpdateVoiceVolumeLabel()
        {
            tbVoiceVolumeValue.Text = $"{(int)sliderVoiceVolume.Value}%";
        }

        private void sliderVoiceVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateVoiceVolumeLabel();
            testVoicePlayer.Volume = (float)(sliderVoiceVolume.Value / 100.0);
        }

        private void UpdateApiKeyLabel()
        {
            SetKeyStatus(tbApiKeyStatus, CredentialStore.Exists(SelectedProvider.ToString()));
        }

        private void UpdateCustomEndpointVisibility()
        {
            spCustomEndpoint.Visibility = SelectedProvider == LlmProviderKind.Custom
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void cbProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbProvider.SelectedItem == null)
                return;
            // プロバイダーを切り替えたら、前のプロバイダーのモデル名が残らないよう常に既定値へ更新する
            // (LoadFromSettings実行中は、この直後に保存済みのModelで上書きされるため問題ない)
            UpdateModelChoices();
            tbModel.Text = LLMChatSettings.DefaultModelFor(SelectedProvider);
            UpdateApiKeyLabel();
            UpdateCustomEndpointVisibility();
        }

        /// <summary>選択中プロバイダーでよく使われるモデル名をドロップダウンの候補として表示する(一覧にない名前も自由入力可能)</summary>
        private void UpdateModelChoices()
        {
            tbModel.Items.Clear();
            foreach (var name in LLMChatSettings.KnownModelsFor(SelectedProvider))
                tbModel.Items.Add(name);
        }

        /// <summary>
        /// 現在入力中のプロバイダー/モデル名/APIキーで実際に短い応答を1回リクエストし、
        /// モデル名が存在するか・APIキーが有効かをその場で確認する(保存はしない)
        /// </summary>
        private async void btnTestModel_Click(object sender, RoutedEventArgs e)
        {
            btnTestModel.IsEnabled = false;
            tbModelTestStatus.Foreground = Brushes.Gray;
            tbModelTestStatus.Text = "テスト中...";
            try
            {
                var provider = SelectedProvider;
                var model = tbModel.Text?.Trim();
                if (string.IsNullOrWhiteSpace(model))
                    throw new InvalidOperationException("モデル名が未入力です");

                var apiKey = !string.IsNullOrEmpty(pbApiKey.Password)
                    ? pbApiKey.Password
                    : CredentialStore.Load(provider.ToString()) ?? string.Empty;
                if (string.IsNullOrEmpty(apiKey))
                    throw new InvalidOperationException("APIキーが未設定です");

                var testSettings = new LLMChatSettings
                {
                    Provider = provider,
                    Model = model,
                    CustomEndpoint = tbCustomEndpoint.Text?.Trim() ?? "",
                };
                //思考(推論)モデルは本文の前に思考過程で数十トークン消費することがあるため、ある程度余裕を持たせる
                var chatProvider = ProviderFactory.Create(testSettings, apiKey, maxTokens: 64);
                var history = new List<ChatMessage> { new ChatMessage("user", "Hi") };
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await chatProvider.ChatStreamAsync("", history, _ => { }, cts.Token);

                tbModelTestStatus.Foreground = Brushes.Green;
                tbModelTestStatus.Text = "✓ 接続成功。このモデルは利用できます";
            }
            catch (OperationCanceledException)
            {
                tbModelTestStatus.Foreground = Brushes.Red;
                tbModelTestStatus.Text = "接続失敗: タイムアウトしました";
            }
            catch (Exception ex)
            {
                tbModelTestStatus.Foreground = Brushes.Red;
                tbModelTestStatus.Text = $"接続失敗: {ex.Message}";
            }
            finally
            {
                btnTestModel.IsEnabled = true;
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            settings.Provider = SelectedProvider;
            settings.Model = tbModel.Text?.Trim();
            settings.CustomEndpoint = tbCustomEndpoint.Text?.Trim();
            settings.SystemPrompt = tbSystemPrompt.Text;
            settings.ProactiveChatEnabled = cbProactiveEnabled.IsChecked == true;
            settings.ProactiveChatIntervalMinutes = ParseIntOrDefault(tbProactiveInterval.Text, 1, 1440, settings.ProactiveChatIntervalMinutes);
            settings.ProactiveChatChancePercent = ParseIntOrDefault(tbProactiveChance.Text, 0, 100, settings.ProactiveChatChancePercent);

            settings.VoiceEnabled = cbVoiceEnabled.IsChecked == true;
            settings.TtsProvider = SelectedTtsProvider;
            settings.VoiceVolumePercent = (int)sliderVoiceVolume.Value;
            if (!string.IsNullOrWhiteSpace(tbVoiceEndpoint.Text))
                settings.VoiceEndpoint = tbVoiceEndpoint.Text.Trim();
            settings.VoiceSpeakerId = SelectedVoiceSpeakerId;
            if (!string.IsNullOrWhiteSpace(tbTtsOpenAiModel.Text))
                settings.OpenAiTtsModel = tbTtsOpenAiModel.Text.Trim();
            if (!string.IsNullOrWhiteSpace(tbTtsOpenAiVoice.Text))
                settings.OpenAiTtsVoice = tbTtsOpenAiVoice.Text.Trim();
            settings.AiVoiceInstallDir = tbAiVoiceInstallDir.Text?.Trim() ?? "";
            settings.AiVoicePresetName = SelectedAiVoicePresetName ?? "";

            settings.VoiceInputEnabled = cbVoiceInputEnabled.IsChecked == true;
            settings.VoiceInputAutoSend = cbVoiceInputAutoSend.IsChecked == true;
            settings.MicSoundEnabled = cbMicSoundEnabled.IsChecked == true;
            if (!string.IsNullOrWhiteSpace(tbVoiceInputModel.Text))
                settings.VoiceInputModel = tbVoiceInputModel.Text.Trim();

            var newKey = pbApiKey.Password;
            if (!string.IsNullOrEmpty(newKey))
                CredentialStore.Save(settings.CredentialKey, newKey);

            var newVoiceInputKey = pbVoiceInputKey.Password;
            if (!string.IsNullOrEmpty(newVoiceInputKey))
                CredentialStore.Save(LLMChatSettings.VoiceInputCredentialKey, newVoiceInputKey);

            var newTtsOpenAiKey = pbTtsOpenAiKey.Password;
            if (!string.IsNullOrEmpty(newTtsOpenAiKey))
                CredentialStore.Save(LLMChatSettings.OpenAiTtsCredentialKey, newTtsOpenAiKey);

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private int SelectedVoiceSpeakerId =>
            cbVoiceSpeaker.SelectedItem is ComboBoxItem item && item.Tag is int id ? id : settings.VoiceSpeakerId;

        private string SelectedAiVoicePresetName =>
            cbAiVoicePreset.SelectedItem is ComboBoxItem item && item.Tag is string name ? name : settings.AiVoicePresetName;

        private async void btnFetchAiVoicePresets_Click(object sender, RoutedEventArgs e)
        {
            btnFetchAiVoicePresets.IsEnabled = false;
            tbVoiceStatus.Text = "取得中...(A.I.VOICE Editorの起動待ちで時間がかかる場合があります)";
            try
            {
                var client = new AiVoiceClient(tbAiVoiceInstallDir.Text);
                var response = await client.GetPresetsAsync(CancellationToken.None);
                var presets = response.Presets ?? Array.Empty<string>();

                cbAiVoicePreset.Items.Clear();
                foreach (var preset in presets)
                    cbAiVoicePreset.Items.Add(new ComboBoxItem { Content = preset, Tag = preset });

                foreach (ComboBoxItem item in cbAiVoicePreset.Items)
                {
                    if ((string)item.Tag == settings.AiVoicePresetName)
                    {
                        cbAiVoicePreset.SelectedItem = item;
                        break;
                    }
                }
                if (cbAiVoicePreset.SelectedItem == null && cbAiVoicePreset.Items.Count > 0)
                    cbAiVoicePreset.SelectedIndex = 0;

                tbVoiceStatus.Text = $"{presets.Length}件のボイスを取得しました";
            }
            catch (Exception ex)
            {
                tbVoiceStatus.Text = $"取得失敗: {ex.Message}";
            }
            finally
            {
                btnFetchAiVoicePresets.IsEnabled = true;
            }
        }

        private async void btnFetchSpeakers_Click(object sender, RoutedEventArgs e)
        {
            btnFetchSpeakers.IsEnabled = false;
            tbVoiceStatus.Text = "取得中...";
            try
            {
                var client = new VoicevoxClient(tbVoiceEndpoint.Text);
                var speakers = await client.GetSpeakersAsync(CancellationToken.None);

                cbVoiceSpeaker.Items.Clear();
                foreach (var style in speakers)
                    cbVoiceSpeaker.Items.Add(new ComboBoxItem { Content = style.ToString(), Tag = style.Id });

                foreach (ComboBoxItem item in cbVoiceSpeaker.Items)
                {
                    if ((int)item.Tag == settings.VoiceSpeakerId)
                    {
                        cbVoiceSpeaker.SelectedItem = item;
                        break;
                    }
                }
                if (cbVoiceSpeaker.SelectedItem == null && cbVoiceSpeaker.Items.Count > 0)
                    cbVoiceSpeaker.SelectedIndex = 0;

                tbVoiceStatus.Text = $"{speakers.Count}件のスタイルを取得しました";
            }
            catch (Exception ex)
            {
                tbVoiceStatus.Text = $"取得失敗: {ex.Message}";
            }
            finally
            {
                btnFetchSpeakers.IsEnabled = true;
            }
        }

        private void btnTestVoice_Click(object sender, RoutedEventArgs e)
        {
            tbVoiceStatus.Text = "再生中...";
            void OnSuccess() => Dispatcher.Invoke(() => tbVoiceStatus.Text = "再生開始しました(聞こえない場合は音量/出力デバイスをご確認ください)");
            void OnError(string message) => Dispatcher.Invoke(() => tbVoiceStatus.Text = $"再生失敗: {message}");

            if (SelectedTtsProvider == TtsProviderKind.OpenAi)
            {
                var apiKey = !string.IsNullOrEmpty(pbTtsOpenAiKey.Password)
                    ? pbTtsOpenAiKey.Password
                    : CredentialStore.Load(LLMChatSettings.OpenAiTtsCredentialKey) ?? string.Empty;
                if (string.IsNullOrEmpty(apiKey))
                {
                    tbVoiceStatus.Text = "再生失敗: OpenAI TTS用のAPIキーが未設定です";
                    return;
                }
                testVoicePlayer.SpeakForTestOpenAi("こんにちは、よろしくね！", tbTtsOpenAiModel.Text, tbTtsOpenAiVoice.Text, apiKey, OnSuccess, OnError);
            }
            else if (SelectedTtsProvider == TtsProviderKind.AiVoice)
            {
                testVoicePlayer.SpeakForTestAiVoice("こんにちは、よろしくね！", tbAiVoiceInstallDir.Text, SelectedAiVoicePresetName, OnSuccess, OnError);
            }
            else
            {
                testVoicePlayer.SpeakForTestVoicevox("こんにちは、よろしくね！", tbVoiceEndpoint.Text, SelectedVoiceSpeakerId, OnSuccess, OnError);
            }
        }

        private static int ParseIntOrDefault(string text, int min, int max, int fallback)
        {
            if (!int.TryParse(text, out var value))
                return fallback;
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}

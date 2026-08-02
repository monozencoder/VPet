using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using VPet.Mod.LLMChat.Voice;

namespace VPet.Mod.LLMChat
{
    public partial class SettingWindow : Window
    {
        private readonly LLMChatSettings settings;
        private readonly VoicevoxSpeechPlayer testVoicePlayer = new VoicevoxSpeechPlayer(null);

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

            tbModel.Text = settings.Model;
            tbCustomEndpoint.Text = settings.CustomEndpoint;
            tbSystemPrompt.Text = settings.SystemPrompt;
            cbProactiveEnabled.IsChecked = settings.ProactiveChatEnabled;
            tbProactiveInterval.Text = settings.ProactiveChatIntervalMinutes.ToString();
            tbProactiveChance.Text = settings.ProactiveChatChancePercent.ToString();

            cbVoiceEnabled.IsChecked = settings.VoiceEnabled;
            tbVoiceEndpoint.Text = settings.VoiceEndpoint;
            cbVoiceSpeaker.Items.Clear();
            cbVoiceSpeaker.Items.Add(new ComboBoxItem { Content = $"ID: {settings.VoiceSpeakerId} (未取得。「一覧取得」で選択可能)", Tag = settings.VoiceSpeakerId });
            cbVoiceSpeaker.SelectedIndex = 0;

            cbVoiceInputEnabled.IsChecked = settings.VoiceInputEnabled;
            cbVoiceInputAutoSend.IsChecked = settings.VoiceInputAutoSend;
            cbMicSoundEnabled.IsChecked = settings.MicSoundEnabled;
            tbVoiceInputModel.Text = settings.VoiceInputModel;
            var hasVoiceInputKey = CredentialStore.Exists(LLMChatSettings.VoiceInputCredentialKey);
            tbVoiceInputKeyLabel.Text = hasVoiceInputKey ? "Whisper用 OpenAI APIキー (設定済み・変更する場合のみ入力)" : "Whisper用 OpenAI APIキー";

            UpdateApiKeyLabel();
            UpdateCustomEndpointVisibility();
        }

        private LlmProviderKind SelectedProvider =>
            (LlmProviderKind)System.Enum.Parse(typeof(LlmProviderKind), (string)((ComboBoxItem)cbProvider.SelectedItem).Tag);

        private void UpdateApiKeyLabel()
        {
            var hasKey = CredentialStore.Exists(SelectedProvider.ToString());
            tbApiKeyLabel.Text = hasKey ? "APIキー (設定済み・変更する場合のみ入力)" : "APIキー";
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
            tbModel.Text = LLMChatSettings.DefaultModelFor(SelectedProvider);
            UpdateApiKeyLabel();
            UpdateCustomEndpointVisibility();
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
            if (!string.IsNullOrWhiteSpace(tbVoiceEndpoint.Text))
                settings.VoiceEndpoint = tbVoiceEndpoint.Text.Trim();
            settings.VoiceSpeakerId = SelectedVoiceSpeakerId;

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
            testVoicePlayer.SpeakForTest("こんにちは、よろしくね！", tbVoiceEndpoint.Text, SelectedVoiceSpeakerId);
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

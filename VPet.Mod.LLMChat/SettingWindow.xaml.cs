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
        private readonly string petName;
        private readonly SpeechPlayer testVoicePlayer = new SpeechPlayer(null);

        /// <summary>「記憶を消去」ボタンでディスク上の記憶を消去したか(呼び出し元で実行中の会話履歴もクリアする必要がある)</summary>
        public bool MemoryCleared { get; private set; }

        public SettingWindow(LLMChatSettings settings, string petName)
        {
            InitializeComponent();
            this.settings = settings;
            this.petName = petName;
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
            tbProactiveInterval.Text = settings.ProactiveChatIntervalSeconds.ToString();
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

            tbAivisSpeechEndpoint.Text = settings.AivisSpeechEndpoint;
            cbAivisSpeechSpeaker.Items.Clear();
            cbAivisSpeechSpeaker.Items.Add(new ComboBoxItem { Content = $"ID: {settings.AivisSpeechSpeakerId} (未取得。「一覧取得」で選択可能)", Tag = settings.AivisSpeechSpeakerId });
            cbAivisSpeechSpeaker.SelectedIndex = 0;

            cbAivisSpeechPreset.Items.Clear();
            cbAivisSpeechPreset.Items.Add(new ComboBoxItem { Content = "(使用しない・上の話者のみ使用)", Tag = null });
            if (settings.AivisSpeechPresetId is int savedPresetId)
            {
                cbAivisSpeechPreset.Items.Add(new ComboBoxItem
                {
                    Content = $"ID: {savedPresetId} (未取得。「一覧取得」で選択可能)",
                    Tag = (savedPresetId, settings.AivisSpeechPresetStyleId),
                });
                cbAivisSpeechPreset.SelectedIndex = 1;
            }
            else
            {
                cbAivisSpeechPreset.SelectedIndex = 0;
            }

            tbVoiceSpeedScale.Text = settings.VoiceSpeedScale.ToString("0.00");
            tbVoicePitchScale.Text = settings.VoicePitchScale.ToString("0.00");
            tbVoiceIntonationScale.Text = settings.VoiceIntonationScale.ToString("0.00");
            tbVoiceTempoDynamicsScale.Text = settings.VoiceTempoDynamicsScale.ToString("0.00");
            tbVoiceVolumeScale.Text = settings.VoiceVolumeScale.ToString("0.00");
            tbVoicePrePhonemeLength.Text = settings.VoicePrePhonemeLength.ToString("0.00");
            tbVoicePostPhonemeLength.Text = settings.VoicePostPhonemeLength.ToString("0.00");

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

            cbMemoryEnabled.IsChecked = settings.MemoryPersistenceEnabled;

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
            spAivisSpeechSettings.Visibility = provider == TtsProviderKind.AivisSpeech ? Visibility.Visible : Visibility.Collapsed;
            spVoiceAdjustments.Visibility = provider == TtsProviderKind.Voicevox || provider == TtsProviderKind.AivisSpeech
                ? Visibility.Visible : Visibility.Collapsed;
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
            settings.ProactiveChatIntervalSeconds = ParseIntOrDefault(tbProactiveInterval.Text, 15, 86400, settings.ProactiveChatIntervalSeconds);
            settings.ProactiveChatChancePercent = ParseIntOrDefault(tbProactiveChance.Text, 0, 100, settings.ProactiveChatChancePercent);

            settings.VoiceEnabled = cbVoiceEnabled.IsChecked == true;
            settings.TtsProvider = SelectedTtsProvider;
            settings.VoiceVolumePercent = (int)sliderVoiceVolume.Value;
            if (!string.IsNullOrWhiteSpace(tbVoiceEndpoint.Text))
                settings.VoiceEndpoint = tbVoiceEndpoint.Text.Trim();
            settings.VoiceSpeakerId = SelectedVoiceSpeakerId;
            if (!string.IsNullOrWhiteSpace(tbAivisSpeechEndpoint.Text))
                settings.AivisSpeechEndpoint = tbAivisSpeechEndpoint.Text.Trim();
            settings.AivisSpeechSpeakerId = SelectedAivisSpeechSpeakerId;
            var selectedAivisPreset = SelectedAivisSpeechPreset;
            settings.AivisSpeechPresetId = selectedAivisPreset.PresetId;
            settings.AivisSpeechPresetStyleId = selectedAivisPreset.StyleId;
            settings.VoiceSpeedScale = ParseDoubleOrDefault(tbVoiceSpeedScale.Text, 0.5, 2.0, settings.VoiceSpeedScale);
            settings.VoicePitchScale = ParseDoubleOrDefault(tbVoicePitchScale.Text, -0.15, 0.15, settings.VoicePitchScale);
            settings.VoiceIntonationScale = ParseDoubleOrDefault(tbVoiceIntonationScale.Text, 0.0, 2.0, settings.VoiceIntonationScale);
            settings.VoiceTempoDynamicsScale = ParseDoubleOrDefault(tbVoiceTempoDynamicsScale.Text, 0.0, 2.0, settings.VoiceTempoDynamicsScale);
            settings.VoiceVolumeScale = ParseDoubleOrDefault(tbVoiceVolumeScale.Text, 0.0, 2.0, settings.VoiceVolumeScale);
            settings.VoicePrePhonemeLength = ParseDoubleOrDefault(tbVoicePrePhonemeLength.Text, 0.0, 1.5, settings.VoicePrePhonemeLength);
            settings.VoicePostPhonemeLength = ParseDoubleOrDefault(tbVoicePostPhonemeLength.Text, 0.0, 1.5, settings.VoicePostPhonemeLength);
            if (!string.IsNullOrWhiteSpace(tbTtsOpenAiModel.Text))
                settings.OpenAiTtsModel = tbTtsOpenAiModel.Text.Trim();
            if (!string.IsNullOrWhiteSpace(tbTtsOpenAiVoice.Text))
                settings.OpenAiTtsVoice = tbTtsOpenAiVoice.Text.Trim();
            settings.AiVoiceInstallDir = tbAiVoiceInstallDir.Text?.Trim() ?? "";
            settings.AiVoicePresetName = SelectedAiVoicePresetName ?? "";

            settings.MemoryPersistenceEnabled = cbMemoryEnabled.IsChecked == true;

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

        private void btnClearMemory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this, "保存されているこれまでの会話記憶(履歴・要約)を消去します。よろしいですか？",
                "記憶の消去", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            ChatMemoryStore.Clear(petName);
            MemoryCleared = true;
            tbMemoryStatus.Text = "記憶を消去しました";
            tbMemoryStatus.Foreground = Brushes.Green;
        }

        private int SelectedVoiceSpeakerId =>
            cbVoiceSpeaker.SelectedItem is ComboBoxItem item && item.Tag is int id ? id : settings.VoiceSpeakerId;

        private int SelectedAivisSpeechSpeakerId =>
            cbAivisSpeechSpeaker.SelectedItem is ComboBoxItem item && item.Tag is int id ? id : settings.AivisSpeechSpeakerId;

        /// <summary>選択中のAivisSpeechプリセット(未選択時はPresetId=null)</summary>
        private (int? PresetId, int StyleId) SelectedAivisSpeechPreset =>
            cbAivisSpeechPreset.SelectedItem is ComboBoxItem item && item.Tag is ValueTuple<int, int> preset
                ? (preset.Item1, preset.Item2)
                : (null, 0);

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

        private async void btnFetchAivisSpeechSpeakers_Click(object sender, RoutedEventArgs e)
        {
            btnFetchAivisSpeechSpeakers.IsEnabled = false;
            tbVoiceStatus.Text = "取得中...";
            try
            {
                var client = new VoicevoxClient(tbAivisSpeechEndpoint.Text);
                var speakers = await client.GetSpeakersAsync(CancellationToken.None);

                cbAivisSpeechSpeaker.Items.Clear();
                foreach (var style in speakers)
                    cbAivisSpeechSpeaker.Items.Add(new ComboBoxItem { Content = style.ToString(), Tag = style.Id });

                foreach (ComboBoxItem item in cbAivisSpeechSpeaker.Items)
                {
                    if ((int)item.Tag == settings.AivisSpeechSpeakerId)
                    {
                        cbAivisSpeechSpeaker.SelectedItem = item;
                        break;
                    }
                }
                if (cbAivisSpeechSpeaker.SelectedItem == null && cbAivisSpeechSpeaker.Items.Count > 0)
                    cbAivisSpeechSpeaker.SelectedIndex = 0;

                tbVoiceStatus.Text = $"{speakers.Count}件のスタイルを取得しました";
            }
            catch (Exception ex)
            {
                tbVoiceStatus.Text = $"取得失敗: {ex.Message}";
            }
            finally
            {
                btnFetchAivisSpeechSpeakers.IsEnabled = true;
            }
        }

        private async void btnFetchAivisSpeechPresets_Click(object sender, RoutedEventArgs e)
        {
            btnFetchAivisSpeechPresets.IsEnabled = false;
            tbVoiceStatus.Text = "取得中...";
            try
            {
                var client = new VoicevoxClient(tbAivisSpeechEndpoint.Text);
                var presets = await client.GetPresetsAsync(CancellationToken.None);

                cbAivisSpeechPreset.Items.Clear();
                cbAivisSpeechPreset.Items.Add(new ComboBoxItem { Content = "(使用しない・上の話者のみ使用)", Tag = null });
                foreach (var preset in presets)
                    cbAivisSpeechPreset.Items.Add(new ComboBoxItem { Content = preset.ToString(), Tag = (preset.Id, preset.StyleId) });

                cbAivisSpeechPreset.SelectedIndex = 0;
                foreach (ComboBoxItem item in cbAivisSpeechPreset.Items)
                {
                    if (item.Tag is ValueTuple<int, int> tuple && tuple.Item1 == settings.AivisSpeechPresetId)
                    {
                        cbAivisSpeechPreset.SelectedItem = item;
                        break;
                    }
                }

                tbVoiceStatus.Text = $"{presets.Count}件のプリセットを取得しました";
            }
            catch (Exception ex)
            {
                tbVoiceStatus.Text = $"取得失敗: {ex.Message}";
            }
            finally
            {
                btnFetchAivisSpeechPresets.IsEnabled = true;
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
            else if (SelectedTtsProvider == TtsProviderKind.AivisSpeech)
            {
                var preset = SelectedAivisSpeechPreset;
                if (preset.PresetId is int presetId)
                    testVoicePlayer.SpeakForTestAivisSpeechPreset("こんにちは、よろしくね！", tbAivisSpeechEndpoint.Text, presetId, preset.StyleId, OnSuccess, OnError);
                else
                    testVoicePlayer.SpeakForTestAivisSpeech("こんにちは、よろしくね！", tbAivisSpeechEndpoint.Text, SelectedAivisSpeechSpeakerId, ReadVoiceAdjustmentsFromUi(), OnSuccess, OnError);
            }
            else
            {
                testVoicePlayer.SpeakForTestVoicevox("こんにちは、よろしくね！", tbVoiceEndpoint.Text, SelectedVoiceSpeakerId, ReadVoiceAdjustmentsFromUi(), OnSuccess, OnError);
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

        private static double ParseDoubleOrDefault(string text, double min, double max, double fallback)
        {
            if (!double.TryParse(text, out var value))
                return fallback;
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        /// <summary>設定画面上の詳細設定欄(未保存の入力値含む)からVOICEVOX/AivisSpeech用の調整値を組み立てる(試し読み用)</summary>
        private VoicevoxSynthesisAdjustments ReadVoiceAdjustmentsFromUi() => new VoicevoxSynthesisAdjustments
        {
            SpeedScale = ParseDoubleOrDefault(tbVoiceSpeedScale.Text, 0.5, 2.0, settings.VoiceSpeedScale),
            PitchScale = ParseDoubleOrDefault(tbVoicePitchScale.Text, -0.15, 0.15, settings.VoicePitchScale),
            IntonationScale = ParseDoubleOrDefault(tbVoiceIntonationScale.Text, 0.0, 2.0, settings.VoiceIntonationScale),
            TempoDynamicsScale = ParseDoubleOrDefault(tbVoiceTempoDynamicsScale.Text, 0.0, 2.0, settings.VoiceTempoDynamicsScale),
            VolumeScale = ParseDoubleOrDefault(tbVoiceVolumeScale.Text, 0.0, 2.0, settings.VoiceVolumeScale),
            PrePhonemeLength = ParseDoubleOrDefault(tbVoicePrePhonemeLength.Text, 0.0, 1.5, settings.VoicePrePhonemeLength),
            PostPhonemeLength = ParseDoubleOrDefault(tbVoicePostPhonemeLength.Text, 0.0, 1.5, settings.VoicePostPhonemeLength),
        };
    }
}

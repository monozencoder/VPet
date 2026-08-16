using System;

namespace VPet.Mod.LLMChat.Providers
{
    /// <summary>
    /// 設定内容からILlmChatProviderの実装を組み立てる
    /// </summary>
    public static class ProviderFactory
    {
        public static ILlmChatProvider Create(LLMChatSettings settings, string apiKey, int maxTokens = 1024)
        {
            switch (settings.Provider)
            {
                case LlmProviderKind.Claude:
                    return new ClaudeChatProvider(apiKey, settings.Model, maxTokens);
                case LlmProviderKind.OpenAI:
                    return new OpenAICompatibleChatProvider("https://api.openai.com/v1/chat/completions", apiKey, settings.Model, maxTokens);
                case LlmProviderKind.DeepSeek:
                    return new OpenAICompatibleChatProvider("https://api.deepseek.com/v1/chat/completions", apiKey, settings.Model, maxTokens);
                case LlmProviderKind.Kimi:
                    return new OpenAICompatibleChatProvider("https://api.moonshot.ai/v1/chat/completions", apiKey, settings.Model, maxTokens);
                case LlmProviderKind.Custom:
                    return new OpenAICompatibleChatProvider(settings.CustomEndpoint, apiKey, settings.Model, maxTokens);
                default:
                    throw new NotSupportedException($"未対応のプロバイダーです: {settings.Provider}");
            }
        }
    }
}

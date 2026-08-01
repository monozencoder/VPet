using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VPet.Mod.LLMChat.Providers
{
    /// <summary>
    /// LLMプロバイダー共通インターフェース。新しいプロバイダーを追加する場合はこれを実装する。
    /// </summary>
    public interface ILlmChatProvider
    {
        /// <summary>
        /// 会話履歴を渡してLLMの応答テキストを取得する
        /// </summary>
        /// <param name="systemPrompt">キャラクター設定等のシステムプロンプト(空文字可)</param>
        /// <param name="history">これまでの会話履歴(最後の要素が今回のユーザー発言)</param>
        Task<string> ChatAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken);
    }
}

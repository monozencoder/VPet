using System;
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
        /// 会話履歴を渡してLLMの応答をストリーミングで取得する。
        /// </summary>
        /// <param name="systemPrompt">キャラクター設定等のシステムプロンプト(空文字可)</param>
        /// <param name="history">これまでの会話履歴(最後の要素が今回のユーザー発言)</param>
        /// <param name="onDelta">新しく生成されたテキスト断片が届くたびに呼ばれる</param>
        /// <returns>最終的に結合された応答全文</returns>
        Task<string> ChatStreamAsync(string systemPrompt, IReadOnlyList<ChatMessage> history, Action<string> onDelta, CancellationToken cancellationToken);
    }
}

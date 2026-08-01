namespace VPet.Mod.LLMChat
{
    /// <summary>
    /// 会話履歴の1発言
    /// </summary>
    public class ChatMessage
    {
        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        /// <summary>
        /// "user" または "assistant"
        /// </summary>
        public string Role { get; set; }
        public string Content { get; set; }
    }
}

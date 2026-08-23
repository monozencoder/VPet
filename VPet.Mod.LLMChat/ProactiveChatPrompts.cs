using System;

namespace VPet.Mod.LLMChat
{
    /// <summary>
    /// 自発発言(ProactiveMessage)用の指示文をランダムに組み立てる。
    /// ペットのステータスや会話履歴がほぼ動かない状況でも、固定の1文だけをLLMに渡すと
    /// 毎回ほぼ同じ返答になりがちなため、話題/トーン/形式/長さの4軸から都度ランダムに選び、
    /// 組み合わせ(20*15*12*4 = 14400通り)で指示文自体にばらつきを持たせて多様な返答を誘発する。
    /// </summary>
    internal static class ProactiveChatPrompts
    {
        private static readonly Random Rnd = new Random();

        private static readonly string[] Topics =
        {
            "最近の天気や季節の変化",
            "少し前に見た夢のような話",
            "飼い主が最近構ってくれているかどうか",
            "自分(ペット)が今なんとなく考えていること",
            "お腹が空いたかどうかや眠気について",
            "何か新しい発見をしたようなふり",
            "特に理由はないけどふと思い出したこと",
            "飼い主の一日がどうだったか気になるという話",
            "窓の外や部屋の様子について",
            "自分の好きなものについて",
            "ちょっとした不満や甘え",
            "退屈しているという話",
            "何か遊びに誘いたい気持ち",
            "些細な自慢話",
            "急に思いついた質問",
            "特に内容はなく、ただの独り言",
            "少し眠そうな寝言のような一言",
            "元気いっぱいな挨拶",
            "何かを褒めてほしいという話",
            "季節のイベントや記念日っぽい話題",
        };

        private static readonly string[] Tones =
        {
            "眠そうにのんびりした",
            "ちょっとはしゃいだ",
            "落ち着いた",
            "少し甘えた",
            "ぼんやりした",
            "元気いっぱいな",
            "照れくさそうな",
            "拗ねたような",
            "気だるい",
            "ちょっとおどけた",
            "しみじみとした",
            "興味津々な",
            "ぽかんとした",
            "満足げな",
            "そわそわした",
        };

        private static readonly string[] Formats =
        {
            "問いかける形で",
            "報告するような形で",
            "ひとりごとのように",
            "感想を述べる形で",
            "ちょっとした提案として",
            "褒めてほしそうに",
            "軽い冗談を交えて",
            "思い出話のように",
            "実況するように",
            "感嘆する形で",
            "確認するように",
            "囁くように小声で",
        };

        private static readonly string[] Lengths =
        {
            "一言だけ、短く",
            "二言くらいで、少し詳しく",
            "テンポよく手短に",
            "ゆったり間を持たせながら",
        };

        /// <summary>
        /// 話題/トーン/形式/長さをランダムに1つずつ選び、自発発言をLLMに依頼する指示文を組み立てる。
        /// </summary>
        public static string BuildInstruction()
        {
            string topic = Topics[Rnd.Next(Topics.Length)];
            string tone = Tones[Rnd.Next(Tones.Length)];
            string format = Formats[Rnd.Next(Formats.Length)];
            string length = Lengths[Rnd.Next(Lengths.Length)];

            return $"（少し時間が経ちました。今回は「{topic}」を話題に、{tone}トーンで、{format}、" +
                   $"飼い主に自然に話しかけてください。{length}話してください。" +
                   "これまでの発言と同じ言い回しの繰り返しは避けてください。）";
        }
    }
}

using System;
using System.Drawing;
using System.Timers;
using System.Windows.Forms;

namespace VPet.Mod.LLMChat
{
    /// <summary>
    /// Windowsの通知(バルーン/アクションセンター)でエラー等を知らせるためのヘルパー。
    /// 常駐トレイアイコンは持たず、通知のたびに一時的なNotifyIconを作成して表示後に破棄する。
    /// </summary>
    internal static class TrayNotifier
    {
        public static void ShowWarning(string title, string text, Action onClick = null)
        {
            var notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Warning,
                Visible = true,
                BalloonTipTitle = title,
                BalloonTipText = text,
                BalloonTipIcon = ToolTipIcon.Warning,
            };

            bool disposed = false;
            void Cleanup()
            {
                if (disposed)
                    return;
                disposed = true;
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }

            notifyIcon.BalloonTipClicked += (s, e) =>
            {
                onClick?.Invoke();
                Cleanup();
            };
            notifyIcon.BalloonTipClosed += (s, e) => Cleanup();
            notifyIcon.ShowBalloonTip(8000);

            // BalloonTipClicked/Closedが発火しない環境でもアイコンが残り続けないための保険
            var timer = new System.Timers.Timer(15000) { AutoReset = false };
            timer.Elapsed += (s, e) =>
            {
                Cleanup();
                timer.Dispose();
            };
            timer.Start();
        }
    }
}

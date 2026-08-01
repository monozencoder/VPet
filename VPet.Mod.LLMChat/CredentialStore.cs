using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VPet.Mod.LLMChat
{
    /// <summary>
    /// APIキーをセーブデータには含めず、Windows資格情報マネージャー(Credential Manager)に保存するためのラッパー。
    /// </summary>
    internal static class CredentialStore
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint DateTimeLow;
            public uint DateTimeHigh;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite([In] ref CREDENTIAL credential, [In] uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree")]
        private static extern void CredFree(IntPtr buffer);

        private static string TargetName(string key) => $"VPet_LLMChat_{key}";

        /// <summary>APIキー等の秘匿情報を保存する</summary>
        public static void Save(string key, string secret)
        {
            var targetPtr = Marshal.StringToCoTaskMemUni(TargetName(key));
            var userPtr = Marshal.StringToCoTaskMemUni(Environment.UserName);
            var bytes = Encoding.Unicode.GetBytes(secret ?? string.Empty);
            var blobPtr = Marshal.AllocCoTaskMem(Math.Max(bytes.Length, 1));
            try
            {
                if (bytes.Length > 0)
                    Marshal.Copy(bytes, 0, blobPtr, bytes.Length);

                var credential = new CREDENTIAL
                {
                    Type = CredTypeGeneric,
                    TargetName = targetPtr,
                    CredentialBlobSize = bytes.Length,
                    CredentialBlob = blobPtr,
                    Persist = CredPersistLocalMachine,
                    UserName = userPtr,
                };

                if (!CredWrite(ref credential, 0))
                    throw new InvalidOperationException($"資格情報の保存に失敗しました (Win32エラー: {Marshal.GetLastWin32Error()})");
            }
            finally
            {
                Marshal.FreeCoTaskMem(targetPtr);
                Marshal.FreeCoTaskMem(userPtr);
                Marshal.FreeCoTaskMem(blobPtr);
            }
        }

        /// <summary>保存済みの秘匿情報を取得する。存在しない場合はnull</summary>
        public static string Load(string key)
        {
            if (!CredRead(TargetName(key), CredTypeGeneric, 0, out var credentialPtr))
                return null;
            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
                    return string.Empty;
                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, credential.CredentialBlobSize);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        /// <summary>保存済みの秘匿情報が存在するか</summary>
        public static bool Exists(string key)
        {
            if (!CredRead(TargetName(key), CredTypeGeneric, 0, out var credentialPtr))
                return false;
            CredFree(credentialPtr);
            return true;
        }

        /// <summary>保存済みの秘匿情報を削除する</summary>
        public static void Delete(string key)
        {
            CredDelete(TargetName(key), CredTypeGeneric, 0);
        }
    }
}

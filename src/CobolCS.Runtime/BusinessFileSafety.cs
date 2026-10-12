using System.Runtime.InteropServices;

namespace CobolCS.Runtime;

/// <summary>採取元を読む前に通常ファイルを確認し、FIFO 等で読み取りが停止することを防ぎます。</summary>
internal static class BusinessFileSafety
{
    /// <summary>リンクを経由せず、Linux ではファイル種別を statx で照合します。</summary>
    /// <param name="path">存在するローカルファイル。</param>
    internal static void RequireRegular(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
        {
            throw new ArgumentException("reference-data-error: Capture is not a regular file " + path);
        }

        if (OperatingSystem.IsLinux())
        {
            if (Statx(-100, path, 0x100, 1, out var status) != 0)
            {
                throw new IOException("Unable to inspect business file type " + path);
            }

            if ((status.Mode & 0xf000) != 0x8000)
            {
                throw new ArgumentException("reference-data-error: Capture is not a regular file " + path);
            }
        }
    }

    /// <summary>Linux の固定 ABI を使い、パスを開く前に種別だけを照会します。</summary>
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out StatxStatus status);

    /// <summary>statx の ABI はアーキテクチャ非依存で256バイト、mode は28バイト目からです。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxStatus
    {
        /// <summary>通常ファイルの種別ビットを含む mode。</summary>
        [FieldOffset(28)]
        internal ushort Mode;
    }
}

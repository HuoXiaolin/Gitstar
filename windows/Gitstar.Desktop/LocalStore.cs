using System.Runtime.InteropServices;
using System.Text;
namespace Gitstar.Desktop;

// Uses the SQLite runtime shipped with Windows; prepared statements isolate user data from SQL.
public sealed class LocalStore : IDisposable
{
    private IntPtr db;
    private readonly object gate = new();
    public string Root { get; }
    public LocalStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gitstar");
        Directory.CreateDirectory(Root);
        if (sqlite3_open(Path.Combine(Root, "gitstar.db"), out db) != 0) throw new IOException("无法打开本地数据库。");
        Exec("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS documents(key TEXT PRIMARY KEY, value TEXT NOT NULL); PRAGMA user_version=1;");
    }
    private void Exec(string sql) { if (sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out var error) != 0) { var msg = Marshal.PtrToStringUTF8(error); sqlite3_free(error); throw new IOException(msg); } }
    public T Get<T>(string key, T fallback)
    {
        lock (gate) {
            Prepare("SELECT value FROM documents WHERE key=?1", out var s);
            try { Bind(s, 1, key); if (sqlite3_step(s) != 100) return fallback;
                var bytes=new byte[sqlite3_column_bytes(s,0)];Marshal.Copy(sqlite3_column_text(s,0),bytes,0,bytes.Length);
                return JsonSerializer.Deserialize<T>(bytes) ?? fallback;
            } finally { sqlite3_finalize(s); }
        }
    }
    public void Set<T>(string key, T value)
    {
        lock (gate) { Prepare("INSERT INTO documents(key,value) VALUES(?1,?2) ON CONFLICT(key) DO UPDATE SET value=excluded.value", out var s);
            try { Bind(s, 1, key);var bytes=JsonSerializer.SerializeToUtf8Bytes(value);if(sqlite3_bind_text(s,2,bytes,bytes.Length,new IntPtr(-1))!=0)throw new IOException("数据库绑定失败。");if (sqlite3_step(s) != 101) throw new IOException("保存数据失败。"); }
            finally { sqlite3_finalize(s); }
        }
    }
    private void Prepare(string sql, out IntPtr stmt) { if (sqlite3_prepare_v2(db, sql, -1, out stmt, IntPtr.Zero) != 0) throw new IOException("数据库查询失败。"); }
    private static void Bind(IntPtr s, int i, string v) { var b = Encoding.UTF8.GetBytes(v); if (sqlite3_bind_text(s, i, b, b.Length, new IntPtr(-1)) != 0) throw new IOException("数据库绑定失败。"); }
    public void Dispose() { lock (gate) { if (db != IntPtr.Zero) { sqlite3_close(db); db = IntPtr.Zero; } } }
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,IntPtr cb,IntPtr arg,out IntPtr error);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr p);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,int n,out IntPtr stmt,IntPtr tail);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr stmt,int i,byte[] val,int n,IntPtr destructor);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt,int i);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr stmt,int i);
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
}
public static class SecretStore
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32", SetLastError=true)] private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32", SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32")] private static extern IntPtr LocalFree(IntPtr p);
    public static string Protect(string value) => System.Convert.ToBase64String(Convert(Encoding.UTF8.GetBytes(value), true));
    public static string Unprotect(string value) => value.Length == 0 ? "" : Encoding.UTF8.GetString(Convert(System.Convert.FromBase64String(value), false));
    private static byte[] Convert(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length=bytes.Length, Data=Marshal.AllocHGlobal(bytes.Length) }; Marshal.Copy(bytes,0,input.Data,bytes.Length);
        try { Blob output; var ok = encrypt ? CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output) : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!ok) throw new IOException("Windows 凭据加密失败："+new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data,result,0,result.Length); return result; } finally { LocalFree(output.Data); }
        } finally { Marshal.FreeHGlobal(input.Data); }
    }
}

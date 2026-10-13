using System.Runtime.InteropServices;
using System.Text;
namespace Gitstar.Desktop;

// Uses the SQLite runtime shipped with Windows; prepared statements isolate user data from SQL.
public sealed class LocalStore : IDisposable
{
    public record StorageSize(long Database,long Journal,long Free,long Content);
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
    public Dictionary<string,long> DocumentSizes()
    {
        lock(gate){Prepare("SELECT key,length(CAST(value AS BLOB)) FROM documents",out var statement);try{var result=new Dictionary<string,long>(StringComparer.Ordinal);int code;while((code=sqlite3_step(statement))==100){var bytes=new byte[sqlite3_column_bytes(statement,0)];Marshal.Copy(sqlite3_column_text(statement,0),bytes,0,bytes.Length);result[Encoding.UTF8.GetString(bytes)]=sqlite3_column_int64(statement,1);}if(code!=101)throw new IOException("读取缓存大小失败。");return result;}finally{sqlite3_finalize(statement);}}
    }
    public void RemoveDocuments(IEnumerable<string> exact,IEnumerable<string> prefixes)
    {
        lock(gate){Exec("BEGIN IMMEDIATE");try{foreach(var key in exact.Distinct(StringComparer.Ordinal))Delete("DELETE FROM documents WHERE key=?1",key);foreach(var prefix in prefixes.Distinct(StringComparer.Ordinal)){if(prefix.Length==0)throw new ArgumentException("缓存前缀不能为空。");Delete("DELETE FROM documents WHERE substr(key,1,length(?1))=?1",prefix);}Exec("COMMIT");}catch{Exec("ROLLBACK");throw;}}
        void Delete(string sql,string key){Prepare(sql,out var statement);try{Bind(statement,1,key);if(sqlite3_step(statement)!=101)throw new IOException("清理缓存记录失败。");}finally{sqlite3_finalize(statement);}}
    }
    public StorageSize StorageUsage(){lock(gate){long Scalar(string sql){Prepare(sql,out var s);try{if(sqlite3_step(s)!=100)throw new IOException("读取数据库空间失败。");return sqlite3_column_int64(s,0);}finally{sqlite3_finalize(s);}}long Size(string file)=>File.Exists(file)?new FileInfo(file).Length:0;return new(Size(Path.Combine(Root,"gitstar.db")),Size(Path.Combine(Root,"gitstar.db-wal")),Scalar("PRAGMA freelist_count")*Scalar("PRAGMA page_size"),Scalar("SELECT coalesce(sum(length(CAST(value AS BLOB))),0) FROM documents"));}}
    public void Compact(){lock(gate){Exec("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA auto_vacuum=INCREMENTAL; VACUUM; PRAGMA wal_checkpoint(TRUNCATE);");}}
    public void ReclaimUnusedSpace(){lock(gate){var size=StorageUsage();if(size.Free>=8*1024*1024&&size.Free>=size.Database/3)Compact();}}
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
    [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr stmt,int i);
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

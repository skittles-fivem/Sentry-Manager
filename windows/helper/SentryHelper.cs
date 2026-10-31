// SentryHelper.exe: the Windows-only half of Sentry Six Manager's platform
// layer. Node has no way to call the Win32 functions the Docker build gets
// from Linux for free (rename without replacing, "delete only if this is
// still the same file", deviceless SMB connections, GPU PCI addresses,
// performance counters), so node.exe starts this one long-lived helper and
// talks to it over stdin/stdout. The protocol is in
// docs/plans/windows-helper-protocol.md; server/platform/winHelper.js is the
// client.
//
// Wire format: one JSON object per line, UTF-8, "\n".
//   request  {"id":7,"op":"fs.renameNoReplace","args":{...}}
//   response {"id":7,"ok":true,"result":{...}}
//            {"id":7,"ok":false,"code":"EEXIST","winerr":183,"message":"..."}
// The first line is an unprompted hello (id 0). The helper exits when stdin
// closes, so it never outlives node. 64-bit numbers go out as decimal
// strings because JSON numbers lose precision past 2^53.
//
// Secrets: passwords arrive only in smb.connect / smb.test arguments. They
// are passed straight to the Win32 call and never written anywhere: not to
// stdout, not to stderr, not into an error message (error messages come
// from the Win32 error number, never from the arguments).
//
// Builds with the csc.exe that ships with .NET Framework 4.8 and with Mono's
// mcs (Linux CI checks that): C# 7.3 at most, .NET Framework 4.8 APIs only,
// no NuGet.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: System.Reflection.AssemblyTitle("Sentry Six Manager helper")]
[assembly: System.Reflection.AssemblyProduct("Sentry Six Manager")]

namespace SentrySixManager.Helper
{
    // An error that goes back to node as {ok:false, code, winerr, message}.
    sealed class HelperError : Exception
    {
        public readonly string Code;
        public readonly int WinErr;

        public HelperError(string code, int winErr, string message) : base(message)
        {
            Code = code;
            WinErr = winErr;
        }

        // The message is the system's text for the error number only, never
        // anything built from the request (paths, user names, passwords).
        public static HelperError FromWin32(int err)
        {
            return new HelperError(Errors.CodeFor(err), err, Errors.Text(err));
        }

        public static HelperError Invalid(string message)
        {
            return new HelperError("EINVAL", 87, message);
        }
    }

    static class Errors
    {
        // Win32 error number -> the code node sees. The table is the
        // protocol's; anything not in it is EIO.
        public static string CodeFor(int err)
        {
            switch (err)
            {
                case 2: case 3: return "ENOENT";
                case 5: return "EACCES";
                case 32: case 33: return "EBUSY";
                case 80: case 183: return "EEXIST";
                case 17: return "EXDEV";
                case 1219: return "ESESSION";
                case 1326: case 86: return "EAUTH";
                case 53: case 67: case 1231: case 64: return "EHOSTUNREACH";
                case 1272: return "EGUEST";
                case 4390: case 1920: return "ELOOP";
                case 87: return "EINVAL";
                // ERROR_DIR_NOT_EMPTY, only from fs.removeDir: node's own name for it
                case 145: return "ENOTEMPTY";
                // ERROR_DIRECTORY, only from fs.removeDir on a file
                case 267: return "ENOTDIR";
                default: return "EIO";
            }
        }

        public static string Text(int err)
        {
            try { return new Win32Exception(err).Message; }
            catch (Exception) { return "Windows error " + err.ToString(CultureInfo.InvariantCulture) + "."; }
        }

        // .NET wraps Win32 errors in exceptions whose HResult is
        // 0x8007xxxx (FACILITY_WIN32); the low word is the Win32 error.
        public static int Win32From(Exception e)
        {
            var w = e as Win32Exception;
            if (w != null) return w.NativeErrorCode;
            int hr = e.HResult;
            if ((hr & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)) return hr & 0xFFFF;
            if (e is UnauthorizedAccessException) return 5;
            if (e is FileNotFoundException) return 2;
            if (e is DirectoryNotFoundException) return 3;
            return 0;
        }

        // Any exception -> a HelperError without request data in the text.
        public static HelperError From(Exception e)
        {
            var h = e as HelperError;
            if (h != null) return h;
            int err = Win32From(e);
            if (err != 0) return HelperError.FromWin32(err);
            if (e is ManagementException) return new HelperError("EIO", 0, "WMI query failed: " + ((ManagementException)e).ErrorCode);
            return new HelperError("EIO", 0, e.GetType().Name + ": " + e.Message);
        }
    }

    // ---------------------------------------------------------------- JSON
    //
    // A small JSON reader and writer, because .NET Framework's serializers
    // live in assemblies the build does not reference.

    // An object whose keys keep their order on the wire.
    sealed class JObj : List<KeyValuePair<string, object>>
    {
        public void Add(string key, object value) { Add(new KeyValuePair<string, object>(key, value)); }
    }

    static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException("trailing characters");
            return v;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var o = new Dictionary<string, object>(StringComparer.Ordinal);
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                for (;;)
                {
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException("expected a key");
                    string key = Str(s, ref i);
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':'");
                    i++;
                    o[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new FormatException("expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                for (;;)
                {
                    a.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException("expected ',' or ']'");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("unexpected character");
            string num = s.Substring(start, i - start);
            long l;
            if (long.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
            return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("bad escape");
                }
            }
            throw new FormatException("unterminated string");
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            Write(sb, v);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            var s = v as string;
            if (s != null) { WriteString(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long || v is uint || v is short || v is ushort || v is byte)
            {
                sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (v is ulong) { sb.Append(((ulong)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var jo = v as JObj;
            if (jo != null)
            {
                sb.Append('{');
                for (int k = 0; k < jo.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    WriteString(sb, jo[k].Key);
                    sb.Append(':');
                    Write(sb, jo[k].Value);
                }
                sb.Append('}');
                return;
            }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Write(sb, e.Value);
                }
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        // Control characters and lone UTF-16 surrogates (legal in NTFS file
        // names) are escaped, so the line is plain valid UTF-8 and node's
        // JSON.parse gets back exactly the name Windows has.
        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        bool pairedHigh = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
                        if (pairedHigh) { sb.Append(c).Append(s[i + 1]); i++; }
                        else if (c < 0x20 || c == 0x2028 || c == 0x2029 || char.IsSurrogate(c))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    // Typed access to request arguments; a missing or wrong-typed argument
    // is EINVAL naming the argument (never its value).
    static class Args
    {
        public static string Str(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || !(v is string) || ((string)v).Length == 0 || ((string)v).IndexOf('\0') >= 0)
                throw HelperError.Invalid("Missing or invalid argument: " + key + ".");
            return (string)v;
        }

        public static string OptStr(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return null;
            var s = v as string;
            if (s == null || s.IndexOf('\0') >= 0) throw HelperError.Invalid("Invalid argument: " + key + ".");
            return s.Length == 0 ? null : s;
        }

        public static bool Bool(Dictionary<string, object> a, string key, bool fallback)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return fallback;
            if (!(v is bool)) throw HelperError.Invalid("Invalid argument: " + key + ".");
            return (bool)v;
        }

        public static long Int(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || !(v is long)) throw HelperError.Invalid("Missing or invalid argument: " + key + ".");
            return (long)v;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> a, string key, bool required)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null)
            {
                if (required) throw HelperError.Invalid("Missing argument: " + key + ".");
                return null;
            }
            var o = v as Dictionary<string, object>;
            if (o == null) throw HelperError.Invalid("Invalid argument: " + key + ".");
            return o;
        }
    }

    // ---------------------------------------------------------------- Win32

    static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint DELETE = 0x00010000;
        public const uint FILE_READ_ATTRIBUTES = 0x0080;
        public const uint FILE_WRITE_ATTRIBUTES = 0x0100;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        public const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
        public const uint FILE_ATTRIBUTE_READONLY = 0x1;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
        public const uint MOVEFILE_REPLACE_EXISTING = 0x1;
        public const uint MOVEFILE_WRITE_THROUGH = 0x8;
        public const int FileBasicInfo = 0, FileRenameInfo = 3, FileDispositionInfo = 4;
        public const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint TH32CS_SNAPPROCESS = 0x2;
        public const int RESOURCETYPE_DISK = 1;
        public const uint CONNECT_TEMPORARY = 0x4;
        public const int ERROR_NOT_CONNECTED = 2250;
        public const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
        public const int LOGON32_PROVIDER_WINNT50 = 3;
        public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
        public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
        public const int KMTQAITYPE_ADAPTERADDRESS = 6;

        [StructLayout(LayoutKind.Sequential)]
        public struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public uint CreationTimeLow, CreationTimeHigh;
            public uint LastAccessTimeLow, LastAccessTimeHigh;
            public uint LastWriteTimeLow, LastWriteTimeHigh;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh, FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileInformationByHandle(SafeFileHandle handle, out BY_HANDLE_FILE_INFORMATION info);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVolumeInformationByHandleW(SafeFileHandle handle, StringBuilder volumeName, int volumeNameSize,
            out uint serial, out uint maxComponent, out uint flags, StringBuilder fsName, int fsNameSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, IntPtr info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadFile(SafeFileHandle handle, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveFileExW(string from, string to, uint flags);

        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetAddConnection2W(ref NETRESOURCE resource, string password, string user, uint flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetCancelConnection2W(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool force);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LogonUserW(string user, string domain, string password, int logonType, int provider, out SafeAccessTokenHandle token);

        [DllImport("dxgi.dll")]
        public static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_OPENADAPTERFROMLUID
        {
            public LUID AdapterLuid;
            public uint hAdapter;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_QUERYADAPTERINFO
        {
            public uint hAdapter;
            public int Type;
            public IntPtr pPrivateDriverData;
            public uint PrivateDriverDataSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_ADAPTERADDRESS
        {
            public uint BusNumber;
            public uint DeviceNumber;
            public uint FunctionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_CLOSEADAPTER
        {
            public uint hAdapter;
        }

        [DllImport("gdi32.dll")]
        public static extern int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID open);

        [DllImport("gdi32.dll")]
        public static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO query);

        [DllImport("gdi32.dll")]
        public static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER close);
    }

    // DXGI through COM interop. A [ComImport] interface must list every
    // vtable slot in order, inherited ones included (C# interface
    // inheritance does not carry slots), so the IUnknown methods are implied
    // and IDXGIObject's and IDXGIFactory's / IDXGIAdapter's methods are
    // declared before the ones used. Unused methods only hold their slot.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Native.LUID AdapterLuid;
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIFactory1
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid name, uint size, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
        // IDXGIFactory
        [PreserveSig] int EnumAdapters(uint index, out IntPtr adapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr window, uint flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr window);
        [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        // IDXGIFactory1
        [PreserveSig] int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 adapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIAdapter1
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid name, uint size, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
        // IDXGIAdapter
        [PreserveSig] int EnumOutputs(uint index, out IntPtr output);
        [PreserveSig] int GetDesc(IntPtr desc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid name, out long umdVersion);
        // IDXGIAdapter1
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }

    // ---------------------------------------------------------------- files

    sealed class FileIdentity
    {
        public string Dev, Ino, Size, MtimeNs;
        public uint Attributes;
        public bool Reparse { get { return (Attributes & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0; } }
        public bool Directory { get { return (Attributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0; } }
    }

    static class Files
    {
        // 100 ns ticks between 1601-01-01 and 1970-01-01.
        const long EpochDiff = 116444736000000000L;

        // Paths past MAX_PATH need the \\?\ prefix (the helper is not long
        // path aware by manifest). Only already-absolute, normalised paths
        // get it, because \\?\ also turns off Windows' own normalisation.
        public static string Long(string p)
        {
            if (p.Length < 248 || p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.IndexOf('/') >= 0
                || p.Contains(@"\.\") || p.Contains(@"\..\") || p.EndsWith(@"\.", StringComparison.Ordinal) || p.EndsWith(@"\..", StringComparison.Ordinal))
                return p;
            if (p.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + p.Substring(2);
            if (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && p[2] == '\\') return @"\\?\" + p;
            return p;
        }

        public static SafeFileHandle Open(string path, uint access, uint share, uint flags)
        {
            var h = Native.CreateFileW(Long(path), access, share, IntPtr.Zero, Native.OPEN_EXISTING, flags, IntPtr.Zero);
            if (h.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                h.Dispose();
                throw HelperError.FromWin32(err);
            }
            return h;
        }

        // The same numbers libuv gives node's fs.stat({bigint:true}) on
        // Windows: dev = volume serial number, ino = 64-bit file index,
        // mtimeNs = (LastWriteTime - 1601->1970) * 100.
        public static FileIdentity Identity(SafeFileHandle h)
        {
            Native.BY_HANDLE_FILE_INFORMATION info;
            if (!Native.GetFileInformationByHandle(h, out info)) throw HelperError.FromWin32(Marshal.GetLastWin32Error());
            long write = ((long)info.LastWriteTimeHigh << 32) | info.LastWriteTimeLow;
            return new FileIdentity
            {
                Attributes = info.FileAttributes,
                Dev = info.VolumeSerialNumber.ToString(CultureInfo.InvariantCulture),
                Ino = (((ulong)info.FileIndexHigh << 32) | info.FileIndexLow).ToString(CultureInfo.InvariantCulture),
                Size = (((ulong)info.FileSizeHigh << 32) | info.FileSizeLow).ToString(CultureInfo.InvariantCulture),
                MtimeNs = (((decimal)write - EpochDiff) * 100m).ToString(CultureInfo.InvariantCulture)
            };
        }

        static decimal? Number(object v)
        {
            if (v == null) return null;
            if (v is long) return (long)v;
            if (v is double) return (decimal)(double)v;
            var s = v as string;
            decimal d;
            if (s != null && decimal.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out d)) return d;
            throw HelperError.Invalid("Invalid expected identity value.");
        }

        // Every field the caller gave must match; fields it left out are not
        // checked. A mismatch is EIDENTITY (not a Win32 error).
        public static void Check(FileIdentity id, Dictionary<string, object> expected)
        {
            string[] keys = { "dev", "ino", "size", "mtimeNs" };
            string[] ours = { id.Dev, id.Ino, id.Size, id.MtimeNs };
            bool any = false;
            for (int k = 0; k < keys.Length; k++)
            {
                object v;
                if (!expected.TryGetValue(keys[k], out v) || v == null) continue;
                any = true;
                decimal? want = Number(v);
                if (want.Value != decimal.Parse(ours[k], CultureInfo.InvariantCulture))
                    throw new HelperError("EIDENTITY", 0, "The file is not the one expected (" + keys[k] + " differs); nothing was changed.");
            }
            if (!any) throw HelperError.Invalid("The expected identity has no fields.");
        }

        public static HelperError ReparseRefused()
        {
            return new HelperError("ELOOP", 0, "The path is a reparse point (link or junction); refused.");
        }

        static void SetInfo(SafeFileHandle h, int infoClass, IntPtr buf, int size)
        {
            if (!Native.SetFileInformationByHandle(h, infoClass, buf, (uint)size)) throw HelperError.FromWin32(Marshal.GetLastWin32Error());
        }

        // FILE_RENAME_INFO: BOOLEAN ReplaceIfExists (padded to pointer
        // alignment), HANDLE RootDirectory, DWORD FileNameLength (bytes),
        // WCHAR FileName[]. Laid out by hand so it is right on x86 and x64.
        public static void RenameHandle(SafeFileHandle h, string to, bool replace)
        {
            string name = Long(to);
            int offRoot = IntPtr.Size;
            int offLen = offRoot + IntPtr.Size;
            int offName = offLen + 4;
            int nameBytes = name.Length * 2;
            // name plus a terminating NUL, rounded up to the struct's alignment
            int size = (offName + nameBytes + 2 + IntPtr.Size - 1) / IntPtr.Size * IntPtr.Size;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
                Marshal.WriteByte(buf, 0, (byte)(replace ? 1 : 0));
                Marshal.WriteIntPtr(buf, offRoot, IntPtr.Zero);
                Marshal.WriteInt32(buf, offLen, nameBytes);
                Marshal.Copy(name.ToCharArray(), 0, IntPtr.Add(buf, offName), name.Length);
                SetInfo(h, Native.FileRenameInfo, buf, size);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // FILE_DISPOSITION_INFO: one BOOLEAN DeleteFile. The file goes when
        // the handle closes.
        public static void DeleteHandle(SafeFileHandle h)
        {
            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(buf, 0, 1);
                SetInfo(h, Native.FileDispositionInfo, buf, 1);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // FILE_BASIC_INFO: four LARGE_INTEGER times (0 = leave unchanged) and
        // DWORD FileAttributes (0 = leave unchanged, so NORMAL stands in for
        // "no attributes").
        public static void SetAttributes(SafeFileHandle h, uint attributes)
        {
            IntPtr buf = Marshal.AllocHGlobal(40);
            try
            {
                for (int i = 0; i < 40; i++) Marshal.WriteByte(buf, i, 0);
                Marshal.WriteInt32(buf, 32, (int)(attributes == 0 ? Native.FILE_ATTRIBUTE_NORMAL : attributes));
                SetInfo(h, Native.FileBasicInfo, buf, 40);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static string Sha256(SafeFileHandle h)
        {
            using (var sha = SHA256.Create())
            {
                var buf = new byte[1 << 20];
                for (;;)
                {
                    uint read;
                    if (!Native.ReadFile(h, buf, (uint)buf.Length, out read, IntPtr.Zero)) throw HelperError.FromWin32(Marshal.GetLastWin32Error());
                    if (read == 0) break;
                    sha.TransformBlock(buf, 0, (int)read, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                var sb = new StringBuilder(64);
                foreach (byte b in sha.Hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }

    // ---------------------------------------------------------------- ops

    static class Ops
    {
        public static object Hello()
        {
            return new JObj { { "op", "hello" }, { "version", 1 }, { "pid", Process.GetCurrentProcess().Id } };
        }

        public static object FsIdentity(Dictionary<string, object> a)
        {
            string path = Args.Str(a, "path");
            using (var h = Files.Open(path, Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE,
                Native.FILE_FLAG_OPEN_REPARSE_POINT | Native.FILE_FLAG_BACKUP_SEMANTICS))
            {
                var id = Files.Identity(h);
                var fs = new StringBuilder(261);
                uint serial, maxComponent, flags;
                string fsName = null;
                if (Native.GetVolumeInformationByHandleW(h, null, 0, out serial, out maxComponent, out flags, fs, fs.Capacity)) fsName = fs.ToString();
                return new JObj
                {
                    { "dev", id.Dev }, { "ino", id.Ino }, { "size", id.Size }, { "mtimeNs", id.MtimeNs },
                    { "reparse", id.Reparse }, { "directory", id.Directory }, { "fsName", fsName }
                };
            }
        }

        static void RefuseReparse(string path)
        {
            using (var h = Files.Open(path, Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE,
                Native.FILE_FLAG_OPEN_REPARSE_POINT | Native.FILE_FLAG_BACKUP_SEMANTICS))
            {
                if (Files.Identity(h).Reparse) throw Files.ReparseRefused();
            }
        }

        // MoveFileExW without REPLACE_EXISTING or COPY_ALLOWED: never
        // replaces (EEXIST) and never copies across volumes (EXDEV).
        public static object FsRenameNoReplace(Dictionary<string, object> a)
        {
            string from = Args.Str(a, "from"), to = Args.Str(a, "to");
            RefuseReparse(from);
            if (!Native.MoveFileExW(Files.Long(from), Files.Long(to), Native.MOVEFILE_WRITE_THROUGH)) throw HelperError.FromWin32(Marshal.GetLastWin32Error());
            return new JObj();
        }

        // The identity check and the rename happen on one open handle, so the
        // file checked is the file renamed.
        public static object FsRenameExpected(Dictionary<string, object> a)
        {
            string from = Args.Str(a, "from"), to = Args.Str(a, "to");
            var expected = Args.Obj(a, "expected", true);
            bool replace = Args.Bool(a, "replace", false);
            // BACKUP_SEMANTICS lets a directory or junction be opened at all,
            // so a junction is refused as ELOOP rather than failing as EACCES;
            // it loosens neither the share mode nor the identity check.
            using (var h = Files.Open(from, Native.DELETE | Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_READ,
                Native.FILE_FLAG_OPEN_REPARSE_POINT | Native.FILE_FLAG_BACKUP_SEMANTICS))
            {
                var id = Files.Identity(h);
                if (id.Reparse) throw Files.ReparseRefused();
                Files.Check(id, expected);
                Files.RenameHandle(h, to, replace);
            }
            return new JObj();
        }

        // Identity (and, when given, SHA-256 of the content read through the
        // same handle) must match before anything changes. The handle also
        // has FILE_WRITE_ATTRIBUTES so a read-only file can be cleared for
        // deletion without opening it a second time.
        public static object FsGuardedRemove(Dictionary<string, object> a)
        {
            string path = Args.Str(a, "path");
            var expected = Args.Obj(a, "expected", true);
            string sha = Args.OptStr(expected, "sha256");
            using (var h = Files.Open(path, Native.DELETE | Native.GENERIC_READ | Native.FILE_WRITE_ATTRIBUTES, Native.FILE_SHARE_READ,
                Native.FILE_FLAG_OPEN_REPARSE_POINT | Native.FILE_FLAG_BACKUP_SEMANTICS | Native.FILE_FLAG_SEQUENTIAL_SCAN))
            {
                // BACKUP_SEMANTICS so a directory or junction opens and is
                // refused below (ELOOP, EISDIR) instead of failing as EACCES
                var id = Files.Identity(h);
                if (id.Reparse) throw Files.ReparseRefused();
                if (id.Directory) throw new HelperError("EISDIR", 0, "The path is a directory; refused.");
                Files.Check(id, expected);
                if (sha != null && !string.Equals(Files.Sha256(h), sha, StringComparison.OrdinalIgnoreCase))
                    throw new HelperError("EIDENTITY", 0, "The file content is not the one expected (sha256 differs); nothing was changed.");
                bool readOnly = (id.Attributes & Native.FILE_ATTRIBUTE_READONLY) != 0;
                if (readOnly) Files.SetAttributes(h, id.Attributes & ~Native.FILE_ATTRIBUTE_READONLY);
                try { Files.DeleteHandle(h); }
                catch
                {
                    if (readOnly) { try { Files.SetAttributes(h, id.Attributes); } catch (Exception) { } }
                    throw;
                }
            }
            return new JObj();
        }

        public static object FsRemoveDir(Dictionary<string, object> a)
        {
            string path = Args.Str(a, "path");
            using (var h = Files.Open(path, Native.DELETE | Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE,
                Native.FILE_FLAG_OPEN_REPARSE_POINT | Native.FILE_FLAG_BACKUP_SEMANTICS))
            {
                var id = Files.Identity(h);
                if (id.Reparse) throw Files.ReparseRefused();
                if (!id.Directory) throw new HelperError("ENOTDIR", 267, Errors.Text(267));
                // a directory with entries fails here with 145 (ENOTEMPTY)
                Files.DeleteHandle(h);
            }
            return new JObj();
        }

        // \\host\share[\...] -> host, share. Anything else is EINVAL.
        static void SplitUnc(string unc, out string host, out string share)
        {
            var m = Regex.Match(unc, @"^\\\\([^\\/]+)\\([^\\/]+)\\?$");
            if (!m.Success) throw HelperError.Invalid("Invalid share path (expected \\\\host\\share).");
            host = m.Groups[1].Value;
            share = m.Groups[2].Value;
        }

        // The account smb.connect and smb.test both present, so a passing
        // test means the real connection uses the same credentials. Without
        // a domain it is the server's own account (HOST\user), which is what
        // a NAS expects for its local users; a Windows server that does not
        // know HOST as a domain checks its local accounts as well. A user
        // already written as DOMAIN\user or user@domain is taken as it is.
        // domain comes back null only for the user@domain form.
        static void Account(Dictionary<string, object> a, string host, out string user, out string domain)
        {
            user = Args.Str(a, "user");
            domain = Args.OptStr(a, "domain");
            if (domain != null && domain.Length == 0) domain = null;
            int slash = user.IndexOf('\\');
            if (domain == null && slash > 0)
            {
                domain = user.Substring(0, slash);
                user = user.Substring(slash + 1);
            }
            else if (domain == null && user.IndexOf('@') < 0)
            {
                domain = host;
            }
        }

        static string UserName(Dictionary<string, object> a, string host)
        {
            string user, domain;
            Account(a, host, out user, out domain);
            return domain == null ? user : domain + "\\" + user;
        }

        // A deviceless connection (no drive letter) that lasts until it is
        // cancelled or the logon session ends.
        public static object SmbConnect(Dictionary<string, object> a)
        {
            string unc = Args.Str(a, "unc"), host, share;
            SplitUnc(unc, out host, out share);
            string password = Args.OptStr(a, "password") ?? "";
            var res = new Native.NETRESOURCE { dwType = Native.RESOURCETYPE_DISK, lpLocalName = null, lpRemoteName = @"\\" + host + @"\" + share };
            int err = Native.WNetAddConnection2W(ref res, password, UserName(a, host), Native.CONNECT_TEMPORARY);
            if (err != 0) throw HelperError.FromWin32(err);
            return new JObj();
        }

        public static object SmbDisconnect(Dictionary<string, object> a)
        {
            string unc = Args.Str(a, "unc"), host, share;
            SplitUnc(unc, out host, out share);
            int err = Native.WNetCancelConnection2W(@"\\" + host + @"\" + share, 0, Args.Bool(a, "force", true));
            if (err == Native.ERROR_NOT_CONNECTED) throw new HelperError("ENOENT", err, Errors.Text(err));
            if (err != 0) throw HelperError.FromWin32(err);
            return new JObj();
        }

        static string Wql(string s)
        {
            return s.Replace("\\", "\\\\").Replace("'", "\\'");
        }

        // MSFT_SmbConnection rows for one share. With several rows (one per
        // session) the weakest is reported: lowest dialect, and encrypted /
        // signed only when every row is.
        static JObj SmbStatusRows(string server, string share)
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\SMB", new ConnectionOptions { Impersonation = ImpersonationLevel.Impersonate });
            var rows = Wmi.Query(scope, "SELECT Dialect, Encrypted, Signed FROM MSFT_SmbConnection WHERE ServerName = '" + Wql(server) + "' AND ShareName = '" + Wql(share) + "'");
            if (rows.Count == 0) return new JObj { { "connected", false }, { "dialect", null }, { "encrypted", null }, { "signed", null } };
            string dialect = null;
            bool? encrypted = null, signed = null;
            foreach (var r in rows)
            {
                string d = Wmi.Str(r, "Dialect");
                if (d != null && (dialect == null || CompareDialect(d, dialect) < 0)) dialect = d;
                bool? e = Wmi.Bool(r, "Encrypted"), s = Wmi.Bool(r, "Signed");
                if (e.HasValue) encrypted = encrypted.HasValue ? encrypted.Value && e.Value : e.Value;
                if (s.HasValue) signed = signed.HasValue ? signed.Value && s.Value : s.Value;
            }
            return new JObj { { "connected", true }, { "dialect", dialect }, { "encrypted", encrypted }, { "signed", signed } };
        }

        static int CompareDialect(string x, string y)
        {
            Version vx, vy;
            if (Version.TryParse(x, out vx) && Version.TryParse(y, out vy)) return vx.CompareTo(vy);
            return string.CompareOrdinal(x, y);
        }

        public static object SmbStatus(Dictionary<string, object> a)
        {
            return SmbStatusRows(Args.Str(a, "server"), Args.Str(a, "share"));
        }

        // Test a share with typed credentials without touching the helper's
        // own SMB sessions: LogonUser(NEW_CREDENTIALS) makes a token whose
        // network identity is those credentials, and the checks run
        // impersonating it, so a wrong password is caught even when the
        // service already has a session to that server.
        public static object SmbTest(Dictionary<string, object> a)
        {
            string unc = Args.Str(a, "unc"), host, share;
            SplitUnc(unc, out host, out share);
            // the same account smb.connect presents (see Account)
            string user, domain;
            Account(a, host, out user, out domain);
            string password = Args.OptStr(a, "password") ?? "";
            bool write = Args.Bool(a, "write", false);
            string folder = Args.OptStr(a, "folder");
            string root = @"\\" + host + @"\" + share;
            string dir = root;
            if (folder != null)
            {
                string rel = folder.Replace('/', '\\').Trim('\\');
                foreach (string seg in rel.Split('\\'))
                    if (seg == ".." || seg == ".") throw HelperError.Invalid("Invalid argument: folder.");
                if (rel.Length > 0) dir = root + @"\" + rel;
            }

            SafeAccessTokenHandle token;
            if (!Native.LogonUserW(user, domain, password, Native.LOGON32_LOGON_NEW_CREDENTIALS, Native.LOGON32_PROVIDER_WINNT50, out token))
                throw HelperError.FromWin32(Marshal.GetLastWin32Error());

            var result = new JObj();
            using (token)
            {
                WindowsIdentity.RunImpersonated(token, () =>
                {
                    var entries = new List<object>();
                    bool readable = false, writable = false;
                    try
                    {
                        foreach (string e in Directory.EnumerateFileSystemEntries(dir))
                        {
                            if (entries.Count >= 200) break;
                            entries.Add(Path.GetFileName(e));
                        }
                        readable = true;
                    }
                    catch (Exception e)
                    {
                        // No read permission is an answer (readable:false);
                        // a wrong password, a missing folder or an
                        // unreachable server is an error.
                        var err = Errors.From(e);
                        if (err.Code != "EACCES") throw err;
                    }
                    JObj written = write ? TryWrite(dir) : null;
                    if (written != null) foreach (var kv in written) if (kv.Key == "writable") writable = (bool)kv.Value;
                    JObj status;
                    try { status = SmbStatusRows(host, share); }
                    catch (Exception) { status = new JObj { { "dialect", null }, { "encrypted", null }, { "signed", null } }; }
                    result.Add("readable", readable);
                    result.Add("writable", writable);
                    result.Add("entries", entries);
                    if (written != null) foreach (var kv in written) if (kv.Key != "writable") result.Add(kv.Key, kv.Value);
                    foreach (var kv in status) if (kv.Key != "connected") result.Add(kv.Key, kv.Value);
                });
            }
            return result;
        }

        // Docker's write check (share.js checkWritable), step for step, on two
        // files of its own: write a, write b and flush it to disk, rename b
        // over a (if that is refused: delete a, then rename), set a's date to
        // 2020-01-01 UTC, read back "new", delete. A NAS that refuses any of
        // them is not writable for the Recycle Bin, Protect or replacing
        // originals. The answer says which step failed and why: writeStep
        // (write|sync|rename|utime|read|remove), writeCode, writeError (the
        // system's text, never a path), renameOverwrite (null until tried).
        static JObj TryWrite(string dir)
        {
            string tag = ".sentry-six-manager-check-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            string a = Path.Combine(dir, tag + "-a");
            string b = Path.Combine(dir, tag + "-b");
            string step = "write";
            object renameOverwrite = null;
            var answer = new JObj();
            try
            {
                File.WriteAllBytes(a, Encoding.ASCII.GetBytes("old"));
                using (var fs = new FileStream(b, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] data = Encoding.ASCII.GetBytes("new");
                    fs.Write(data, 0, data.Length);
                    step = "sync";
                    fs.Flush(true);
                }
                step = "rename";
                if (Native.MoveFileExW(Files.Long(b), Files.Long(a), Native.MOVEFILE_REPLACE_EXISTING | Native.MOVEFILE_WRITE_THROUGH))
                    renameOverwrite = true;
                else
                {
                    renameOverwrite = false;
                    step = "remove";
                    File.Delete(a);
                    step = "rename";
                    if (!Native.MoveFileExW(Files.Long(b), Files.Long(a), Native.MOVEFILE_WRITE_THROUGH)) throw HelperError.FromWin32(Marshal.GetLastWin32Error());
                }
                step = "utime";
                var when = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(a, when);
                File.SetLastAccessTimeUtc(a, when);
                step = "read";
                if (Encoding.ASCII.GetString(File.ReadAllBytes(a)) != "new") throw new HelperError("EIO", 0, "What was read back differs from what was written.");
                step = "remove";
                File.Delete(a);
                answer.Add("writable", true);
                answer.Add("renameOverwrite", renameOverwrite);
                return answer;
            }
            catch (Exception e)
            {
                try { File.Delete(a); } catch (Exception) { }
                try { File.Delete(b); } catch (Exception) { }
                var err = Errors.From(e);
                answer.Add("writable", false);
                answer.Add("writeStep", step);
                answer.Add("writeCode", err.Code);
                answer.Add("writeError", err.Message);
                answer.Add("renameOverwrite", renameOverwrite);
                return answer;
            }
        }

        public static object GpuList()
        {
            var adapters = new List<object>();
            var iid = typeof(IDXGIFactory1).GUID;
            IDXGIFactory1 factory;
            int hr = Native.CreateDXGIFactory1(ref iid, out factory);
            if (hr < 0 || factory == null) throw new HelperError("EIO", hr, "CreateDXGIFactory1 failed (0x" + hr.ToString("x8", CultureInfo.InvariantCulture) + ").");
            try
            {
                for (uint i = 0; ; i++)
                {
                    IDXGIAdapter1 adapter;
                    hr = factory.EnumAdapters1(i, out adapter);
                    if (hr == Native.DXGI_ERROR_NOT_FOUND || adapter == null) break;
                    if (hr < 0) throw new HelperError("EIO", hr, "EnumAdapters1 failed (0x" + hr.ToString("x8", CultureInfo.InvariantCulture) + ").");
                    try
                    {
                        DXGI_ADAPTER_DESC1 d;
                        if (adapter.GetDesc1(out d) < 0) continue;
                        ulong luid = ((ulong)(uint)d.AdapterLuid.HighPart << 32) | d.AdapterLuid.LowPart;
                        bool software = (d.Flags & Native.DXGI_ADAPTER_FLAG_SOFTWARE) != 0 || d.VendorId == 0x1414;
                        adapters.Add(new JObj
                        {
                            { "index", (long)i },
                            { "luid", "0x" + luid.ToString("x16", CultureInfo.InvariantCulture) },
                            { "vendorId", d.VendorId }, { "deviceId", d.DeviceId }, { "subsysId", d.SubSysId }, { "revision", d.Revision },
                            { "name", (d.Description ?? "").Trim() },
                            { "dedicatedVideoMemory", d.DedicatedVideoMemory.ToUInt64().ToString(CultureInfo.InvariantCulture) },
                            { "pci", PciAddress(d.AdapterLuid) },
                            { "software", software }
                        });
                    }
                    finally { Marshal.ReleaseComObject(adapter); }
                }
            }
            finally { Marshal.ReleaseComObject(factory); }
            return new JObj { { "adapters", adapters } };
        }

        // Bus, device and function from the kernel-mode adapter, as Linux
        // writes it: 0000:bb:dd.f in lower-case hex. null when the driver
        // does not say (software adapters, some remote sessions).
        static string PciAddress(Native.LUID luid)
        {
            var open = new Native.D3DKMT_OPENADAPTERFROMLUID { AdapterLuid = luid };
            if (Native.D3DKMTOpenAdapterFromLuid(ref open) != 0) return null;
            IntPtr buf = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.D3DKMT_ADAPTERADDRESS)));
            try
            {
                var q = new Native.D3DKMT_QUERYADAPTERINFO
                {
                    hAdapter = open.hAdapter,
                    Type = Native.KMTQAITYPE_ADAPTERADDRESS,
                    pPrivateDriverData = buf,
                    PrivateDriverDataSize = (uint)Marshal.SizeOf(typeof(Native.D3DKMT_ADAPTERADDRESS))
                };
                if (Native.D3DKMTQueryAdapterInfo(ref q) != 0) return null;
                var addr = (Native.D3DKMT_ADAPTERADDRESS)Marshal.PtrToStructure(buf, typeof(Native.D3DKMT_ADAPTERADDRESS));
                if (addr.BusNumber > 0xff || addr.DeviceNumber > 0x1f || addr.FunctionNumber > 0x7) return null;
                return string.Format(CultureInfo.InvariantCulture, "0000:{0:x2}:{1:x2}.{2:x}", addr.BusNumber, addr.DeviceNumber, addr.FunctionNumber);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
                var close = new Native.D3DKMT_CLOSEADAPTER { hAdapter = open.hAdapter };
                Native.D3DKMTCloseAdapter(ref close);
            }
        }

        // Keep-awake is a per-thread state, so it is always set on the
        // helper's main thread (which lives as long as the helper).
        public static object PowerKeepAwake(Dictionary<string, object> a)
        {
            bool on = Args.Bool(a, "on", false);
            uint flags = on ? Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED : Native.ES_CONTINUOUS;
            if (Native.SetThreadExecutionState(flags) == 0) throw new HelperError("EIO", 0, "SetThreadExecutionState failed.");
            return new JObj();
        }

        // Every descendant of pid (not pid itself), from one Toolhelp
        // snapshot of all processes.
        public static object ProcTree(Dictionary<string, object> a)
        {
            long root = Args.Int(a, "pid");
            if (root <= 0 || root > uint.MaxValue) throw HelperError.Invalid("Invalid argument: pid.");
            IntPtr snap = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
            if (snap == new IntPtr(-1)) throw HelperError.FromWin32(Marshal.GetLastWin32Error());
            var children = new Dictionary<uint, List<uint>>();
            try
            {
                var e = new Native.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf(typeof(Native.PROCESSENTRY32W)) };
                for (bool more = Native.Process32FirstW(snap, ref e); more; more = Native.Process32NextW(snap, ref e))
                {
                    if (e.th32ProcessID == e.th32ParentProcessID) continue;
                    List<uint> list;
                    if (!children.TryGetValue(e.th32ParentProcessID, out list)) children[e.th32ParentProcessID] = list = new List<uint>();
                    list.Add(e.th32ProcessID);
                }
            }
            finally { Native.CloseHandle(snap); }
            var pids = new List<object>();
            var seen = new HashSet<uint> { (uint)root };
            var queue = new Queue<uint>();
            queue.Enqueue((uint)root);
            while (queue.Count > 0)
            {
                List<uint> list;
                if (!children.TryGetValue(queue.Dequeue(), out list)) continue;
                foreach (uint c in list)
                {
                    if (!seen.Add(c)) continue;
                    pids.Add((long)c);
                    queue.Enqueue(c);
                }
            }
            return new JObj { { "pids", pids } };
        }
    }

    // ---------------------------------------------------------------- WMI

    static class Wmi
    {
        public static List<ManagementBaseObject> Query(ManagementScope scope, string wql)
        {
            var list = new List<ManagementBaseObject>();
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(30), ReturnImmediately = true, Rewindable = false };
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), options))
            using (var results = searcher.Get())
            {
                foreach (ManagementBaseObject o in results) list.Add(o);
            }
            return list;
        }

        public static List<ManagementBaseObject> Query(string ns, string wql)
        {
            return Query(new ManagementScope(ns), wql);
        }

        static object Prop(ManagementBaseObject o, string name)
        {
            try { return o[name]; }
            catch (ManagementException) { return null; }
        }

        public static string Str(ManagementBaseObject o, string name)
        {
            object v = Prop(o, name);
            if (v == null) return null;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
            return s.Length == 0 ? null : s;
        }

        public static string Num(ManagementBaseObject o, string name)
        {
            object v = Prop(o, name);
            if (v == null) return null;
            try { return Convert.ToUInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
        }

        public static long? Int(ManagementBaseObject o, string name)
        {
            object v = Prop(o, name);
            if (v == null) return null;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
        }

        public static bool? Bool(ManagementBaseObject o, string name)
        {
            object v = Prop(o, name);
            if (v is bool) return (bool)v;
            return null;
        }
    }

    static class SysInfo
    {
        const string Cimv2 = @"\\.\root\cimv2";
        const string Storage = @"\\.\root\Microsoft\Windows\Storage";

        // Each part is read on its own, so one WMI class that is missing or
        // broken leaves that part empty instead of failing the whole answer.
        static void Try(string what, Action part)
        {
            try { part(); }
            catch (Exception e) { Program.Diag("sys.info: " + what + " unavailable (" + e.GetType().Name + ")"); }
        }

        static string MediaType(long? v)
        {
            if (!v.HasValue) return null;
            switch (v.Value) { case 3: return "HDD"; case 4: return "SSD"; case 5: return "SCM"; case 0: return null; default: return v.Value.ToString(CultureInfo.InvariantCulture); }
        }

        static readonly Dictionary<long, string> BusTypes = new Dictionary<long, string>
        {
            { 1, "SCSI" }, { 2, "ATAPI" }, { 3, "ATA" }, { 4, "1394" }, { 5, "SSA" }, { 6, "Fibre Channel" }, { 7, "USB" }, { 8, "RAID" },
            { 9, "iSCSI" }, { 10, "SAS" }, { 11, "SATA" }, { 12, "SD" }, { 13, "MMC" }, { 14, "Virtual" }, { 15, "File Backed Virtual" },
            { 16, "Storage Spaces" }, { 17, "NVMe" }, { 18, "SCM" }, { 19, "UFS" }
        };

        public static object Read()
        {
            JObj os = new JObj { { "caption", null }, { "version", null }, { "build", null } };
            var cpus = new List<object>();
            string memTotal = null, bootTime = null, account = null;
            var disks = new List<object>();
            var volumes = new List<object>();
            var adapters = new List<object>();
            bool elevated = false;

            Try("Win32_OperatingSystem", () =>
            {
                foreach (var o in Wmi.Query(Cimv2, "SELECT Caption, Version, BuildNumber, LastBootUpTime FROM Win32_OperatingSystem"))
                {
                    os = new JObj { { "caption", Wmi.Str(o, "Caption") }, { "version", Wmi.Str(o, "Version") }, { "build", Wmi.Str(o, "BuildNumber") } };
                    string boot = Wmi.Str(o, "LastBootUpTime");
                    if (boot != null)
                        bootTime = ManagementDateTimeConverter.ToDateTime(boot).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                }
            });
            Try("Win32_ComputerSystem", () =>
            {
                foreach (var o in Wmi.Query(Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")) memTotal = Wmi.Num(o, "TotalPhysicalMemory");
            });
            Try("Win32_Processor", () =>
            {
                foreach (var o in Wmi.Query(Cimv2, "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, SocketDesignation FROM Win32_Processor"))
                    cpus.Add(new JObj
                    {
                        { "name", Wmi.Str(o, "Name") }, { "cores", Wmi.Int(o, "NumberOfCores") },
                        { "threads", Wmi.Int(o, "NumberOfLogicalProcessors") }, { "socket", Wmi.Str(o, "SocketDesignation") }
                    });
            });
            Try("MSFT_PhysicalDisk", () =>
            {
                foreach (var o in Wmi.Query(Storage, "SELECT DeviceId, FriendlyName, Model, SerialNumber, Size, MediaType, BusType FROM MSFT_PhysicalDisk"))
                {
                    long n;
                    long? number = long.TryParse(Wmi.Str(o, "DeviceId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : (long?)null;
                    long? bus = Wmi.Int(o, "BusType");
                    string busName = null;
                    if (bus.HasValue && !BusTypes.TryGetValue(bus.Value, out busName)) busName = bus.Value == 0 ? null : bus.Value.ToString(CultureInfo.InvariantCulture);
                    disks.Add(new JObj
                    {
                        { "number", number }, { "model", Wmi.Str(o, "Model") ?? Wmi.Str(o, "FriendlyName") }, { "serial", Wmi.Str(o, "SerialNumber") },
                        { "size", Wmi.Num(o, "Size") }, { "mediaType", MediaType(Wmi.Int(o, "MediaType")) }, { "busType", busName }
                    });
                }
            });
            Try("Win32_Volume", () =>
            {
                // drive letter -> disk number, from the partition each
                // logical disk sits on ("Disk #1, Partition #0")
                var diskOf = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var o in Wmi.Query(Cimv2, "SELECT * FROM Win32_LogicalDiskToPartition"))
                    {
                        var part = Regex.Match(Wmi.Str(o, "Antecedent") ?? "", "Disk #(\\d+)");
                        var letter = Regex.Match(Wmi.Str(o, "Dependent") ?? "", "DeviceID=\"([A-Za-z]:)\"");
                        if (part.Success && letter.Success) diskOf[letter.Groups[1].Value] = long.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception) { }
                foreach (var o in Wmi.Query(Cimv2, "SELECT DriveLetter, Label, FileSystem, Capacity, FreeSpace FROM Win32_Volume"))
                {
                    string letter = Wmi.Str(o, "DriveLetter");
                    long disk;
                    volumes.Add(new JObj
                    {
                        { "letter", letter }, { "label", Wmi.Str(o, "Label") }, { "fs", Wmi.Str(o, "FileSystem") },
                        { "size", Wmi.Num(o, "Capacity") }, { "free", Wmi.Num(o, "FreeSpace") },
                        { "disk", letter != null && diskOf.TryGetValue(letter, out disk) ? disk : (long?)null }
                    });
                }
            });
            Try("Win32_NetworkAdapter", () =>
            {
                foreach (var o in Wmi.Query(Cimv2, "SELECT NetConnectionID, Description, MACAddress, Speed, PhysicalAdapter FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL"))
                    adapters.Add(new JObj
                    {
                        { "name", Wmi.Str(o, "NetConnectionID") }, { "description", Wmi.Str(o, "Description") }, { "mac", Wmi.Str(o, "MACAddress") },
                        { "speed", Wmi.Num(o, "Speed") }, { "physical", Wmi.Bool(o, "PhysicalAdapter") ?? false }
                    });
            });
            Try("account", () =>
            {
                using (var me = WindowsIdentity.GetCurrent())
                {
                    account = me.Name;
                    // a UAC-filtered administrator is not elevated
                    elevated = new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator);
                }
            });

            return new JObj
            {
                { "os", os }, { "cpus", cpus }, { "memory", new JObj { { "total", memTotal } } }, { "bootTime", bootTime },
                { "disks", disks }, { "volumes", volumes }, { "adapters", adapters }, { "elevated", elevated }, { "account", account }
            };
        }
    }

    // ---------------------------------------------------------------- perf

    // English performance object and counter names -> the names this
    // Windows uses. Perflib\009 lists "index, English name" pairs and
    // Perflib\CurrentLanguage the same indexes in the display language, so
    // the English name from the protocol finds its local name on a German or
    // Japanese Windows. When anything is missing, the English name is used.
    static class PerfNames
    {
        const string Perflib = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib";
        static readonly object Gate = new object();
        static Dictionary<string, List<int>> english;
        static Dictionary<int, string> local;

        static Dictionary<string, List<int>> ByName(string[] pairs)
        {
            var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            if (pairs == null) return map;
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                int index;
                if (!int.TryParse(pairs[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) continue;
                List<int> list;
                if (!map.TryGetValue(pairs[i + 1], out list)) map[pairs[i + 1]] = list = new List<int>();
                list.Add(index);
            }
            return map;
        }

        static Dictionary<int, string> ByIndex(string[] pairs)
        {
            var map = new Dictionary<int, string>();
            if (pairs == null) return map;
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                int index;
                if (int.TryParse(pairs[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && !map.ContainsKey(index)) map[index] = pairs[i + 1];
            }
            return map;
        }

        static string[] Read(string sub)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(Perflib + @"\" + sub))
                    return k == null ? null : k.GetValue("Counter") as string[];
            }
            catch (Exception) { return null; }
        }

        static void Load()
        {
            lock (Gate)
            {
                if (english != null) return;
                english = ByName(Read("009"));
                string[] cur = Read("CurrentLanguage");
                if (cur == null || cur.Length == 0)
                    cur = Read((CultureInfo.InstalledUICulture.LCID & 0x3ff).ToString("x3", CultureInfo.InvariantCulture));
                local = ByIndex(cur);
            }
        }

        // The English name first, then each local name it may have.
        public static List<string> Candidates(string name)
        {
            Load();
            var list = new List<string> { name };
            List<int> indexes;
            if (english.TryGetValue(name, out indexes))
                foreach (int i in indexes)
                {
                    string l;
                    if (local.TryGetValue(i, out l) && !list.Contains(l)) list.Add(l);
                }
            return list;
        }
    }

    // The CPU package energy from SentryPower.exe (the SentrySixPower
    // service, installed with Setup's "Measure CPU power"), which reads the
    // RAPL counters through the PawnIO driver: perf.sample's 'rapl' set,
    // {"package-0": {"EnergyUj": "n", "Time100ns": "n"}, ...}. EnergyUj is a
    // 64-bit running total in microjoules (the service has already dealt
    // with the 32-bit counter's wrap), Time100ns the service's monotonic
    // clock when it read it. {} when the service is not installed or has no
    // reading (no driver, a CPU it does not know): then Energy Meter, if
    // any, is what node uses.
    static class CpuPower
    {
        const string PipeName = "SentrySixPower";
        const int ConnectMs = 200;
        const int ReadMs = 1000;
        static readonly Regex Digits = new Regex(@"^\d{1,20}$");
        // after a failed connect (no service on this PC) the pipe is not
        // tried again for a minute, so perf.sample does not wait each time
        static DateTime skipUntil = DateTime.MinValue;

        public static JObj Read()
        {
            var result = new JObj();
            string line = null;
            if (DateTime.UtcNow < skipUntil) return result;
            try
            {
                // Identification: the service learns who asks, but can never
                // act as us (were the name ever held by another program)
                using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.In,
                    System.IO.Pipes.PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    pipe.Connect(ConnectMs);
                    var buf = new byte[4096];
                    int got = 0;
                    DateTime until = DateTime.UtcNow.AddMilliseconds(ReadMs);
                    while (got < buf.Length)
                    {
                        int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
                        if (left <= 0) return result;
                        IAsyncResult r = pipe.BeginRead(buf, got, buf.Length - got, null, null);
                        if (!r.AsyncWaitHandle.WaitOne(left)) return result;
                        int n = pipe.EndRead(r);
                        if (n <= 0) break;
                        got += n;
                        if (Array.IndexOf(buf, (byte)'\n', 0, got) >= 0) break;
                    }
                    line = new UTF8Encoding(false, false).GetString(buf, 0, got);
                }
            }
            catch (TimeoutException) { skipUntil = DateTime.UtcNow.AddMinutes(1); return result; } // not installed, or not running
            catch (IOException) { return result; }
            catch (UnauthorizedAccessException) { Program.Diag("perf.rapl: the CPU power service refused the connection"); return result; }
            catch (Exception e) { Program.Diag("perf.rapl: failed (" + e.GetType().Name + ")"); return result; }

            int nl = line.IndexOf('\n');
            if (nl >= 0) line = line.Substring(0, nl);
            Dictionary<string, object> msg;
            try { msg = Json.Parse(line.Trim()) as Dictionary<string, object>; }
            catch (Exception) { msg = null; }
            object ok, pkgs;
            if (msg == null || !msg.TryGetValue("ok", out ok) || !(ok is bool) || !(bool)ok) return result;
            if (!msg.TryGetValue("packages", out pkgs) || !(pkgs is List<object>)) return result;
            foreach (object o in (List<object>)pkgs)
            {
                var p = o as Dictionary<string, object>;
                object socket, energy, t;
                if (p == null || !p.TryGetValue("socket", out socket) || !(socket is long)) continue;
                if (!p.TryGetValue("energyUj", out energy) || !(energy is string) || !Digits.IsMatch((string)energy)) continue;
                if (!p.TryGetValue("t100ns", out t) || !(t is string) || !Digits.IsMatch((string)t)) continue;
                long s = (long)socket;
                if (s < 0 || s > 63) continue;
                result.Add("package-" + s.ToString(CultureInfo.InvariantCulture), new JObj { { "EnergyUj", (string)energy }, { "Time100ns", (string)t } });
            }
            return result;
        }
    }

    static class Perf
    {
        static readonly object Gate = new object();

        static readonly string[] SetOrder = { "cpu", "disk", "net", "gpu", "gpumem", "energy", "rapl", "proc" };

        static readonly Dictionary<string, KeyValuePair<string, string[]>> Sets = new Dictionary<string, KeyValuePair<string, string[]>>
        {
            { "cpu", new KeyValuePair<string, string[]>("Processor Information", new[] { "% Processor Time", "% Privileged Time", "% User Time", "% Idle Time", "% Processor Performance", "Processor Frequency" }) },
            { "disk", new KeyValuePair<string, string[]>("PhysicalDisk", new[] { "Disk Read Bytes/sec", "Disk Write Bytes/sec", "Disk Reads/sec", "Disk Writes/sec", "% Idle Time" }) },
            { "net", new KeyValuePair<string, string[]>("Network Interface", new[] { "Bytes Received/sec", "Bytes Sent/sec" }) },
            { "gpu", new KeyValuePair<string, string[]>("GPU Engine", new[] { "Utilization Percentage" }) },
            { "gpumem", new KeyValuePair<string, string[]>("GPU Adapter Memory", new[] { "Dedicated Usage", "Shared Usage" }) },
            { "energy", new KeyValuePair<string, string[]>("Energy Meter", new[] { "Energy", "Power" }) },
            { "proc", new KeyValuePair<string, string[]>("Process", new[] { "% Processor Time", "Working Set - Private", "ID Process", "Creating Process ID", "IO Read Bytes/sec", "IO Write Bytes/sec" }) }
        };

        static PerformanceCounterCategory Find(string english)
        {
            foreach (string name in PerfNames.Candidates(english))
            {
                try { if (PerformanceCounterCategory.Exists(name)) return new PerformanceCounterCategory(name); }
                catch (Exception) { }
            }
            return null;
        }

        // One category -> {instance: {counter: raw}}. Raw cumulative values
        // only; node does all rate math. Counters with a base (averages,
        // fractions) also carry "<counter> Base".
        static JObj ReadSet(string set, out CounterSample? stamp)
        {
            stamp = null;
            var spec = Sets[set];
            var instances = new Dictionary<string, JObj>(StringComparer.Ordinal);
            var order = new List<string>();
            var category = Find(spec.Key);
            if (category != null)
            {
                InstanceDataCollectionCollection data = null;
                try { data = category.ReadCategory(); }
                catch (Exception e) { Program.Diag("perf." + set + ": category unreadable (" + e.GetType().Name + ")"); }
                if (data != null)
                    foreach (string counter in spec.Value)
                    {
                        InstanceDataCollection coll = null;
                        foreach (string name in PerfNames.Candidates(counter))
                            if (data.Contains(name)) { coll = data[name]; break; }
                        if (coll == null) continue;
                        foreach (InstanceData item in coll.Values)
                        {
                            string inst = item.InstanceName ?? "";
                            if (inst == "systemdiagnosticsperfcounterlibsingleinstance") inst = "";
                            JObj row;
                            if (!instances.TryGetValue(inst, out row)) { instances[inst] = row = new JObj(); order.Add(inst); }
                            CounterSample s = item.Sample;
                            if (!stamp.HasValue) stamp = s;
                            row.Add(counter, s.RawValue.ToString(CultureInfo.InvariantCulture));
                            if (s.BaseValue != 0) row.Add(counter + " Base", s.BaseValue.ToString(CultureInfo.InvariantCulture));
                        }
                    }
            }
            var result = new JObj();
            foreach (string inst in order) result.Add(inst, instances[inst]);
            return result;
        }

        public static object Sample(Dictionary<string, object> a)
        {
            var wanted = new List<string>();
            object v;
            if (a != null && a.TryGetValue("sets", out v) && v != null)
            {
                var list = v as List<object>;
                if (list == null) throw HelperError.Invalid("Invalid argument: sets.");
                foreach (object o in list)
                {
                    var s = o as string;
                    if (s == null || (!Sets.ContainsKey(s) && s != "rapl")) throw HelperError.Invalid("Invalid argument: sets (unknown set).");
                    if (!wanted.Contains(s)) wanted.Add(s);
                }
            }
            else wanted.AddRange(SetOrder);

            lock (Gate)
            {
                var sets = new JObj();
                var times = new JObj();
                string t100 = null;
                foreach (string set in wanted)
                {
                    if (set == "rapl")
                    {
                        // not a performance counter category: the CPU power
                        // service's latest reading (it carries its own times)
                        sets.Add(set, CpuPower.Read());
                        continue;
                    }
                    CounterSample? stamp;
                    sets.Add(set, ReadSet(set, out stamp));
                    if (stamp.HasValue)
                    {
                        string t = stamp.Value.TimeStamp100nSec.ToString(CultureInfo.InvariantCulture);
                        if (t100 == null) t100 = t;
                        times.Add(set, new JObj
                        {
                            { "t100ns", t }, { "freq", "10000000" },
                            { "perfTime", stamp.Value.TimeStamp.ToString(CultureInfo.InvariantCulture) },
                            { "perfFreq", stamp.Value.SystemFrequency.ToString(CultureInfo.InvariantCulture) }
                        });
                    }
                }
                // Timestamp_Sys100NS is FILETIME (100 ns since 1601), so the
                // clock stands in when no category had a sample.
                if (t100 == null) t100 = DateTime.UtcNow.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);
                return new JObj { { "t100ns", t100 }, { "freq", "10000000" }, { "sets", sets }, { "times", times } };
            }
        }
    }

    // ---------------------------------------------------------------- main

    static class Program
    {
        static readonly object OutLock = new object();
        static Stream stdout;
        static readonly BlockingCollection<Action> MainQueue = new BlockingCollection<Action>();
        static int inFlight;

        // Diagnostics for service.log. Callers pass fixed text and exception
        // type names only, never request arguments.
        public static void Diag(string text)
        {
            try { Console.Error.WriteLine("SentryHelper: " + text); }
            catch (Exception) { }
        }

        static void Send(JObj message)
        {
            byte[] line = new UTF8Encoding(false).GetBytes(Json.Write(message) + "\n");
            lock (OutLock)
            {
                try
                {
                    stdout.Write(line, 0, line.Length);
                    stdout.Flush();
                }
                catch (Exception)
                {
                    // node is gone: nothing left to answer
                    Environment.Exit(0);
                }
            }
        }

        static void Reply(long id, string op, Dictionary<string, object> args)
        {
            try { Answer(id, op, args); }
            finally { Interlocked.Decrement(ref inFlight); }
        }

        static void Answer(long id, string op, Dictionary<string, object> args)
        {
            object result;
            try
            {
                result = Handle(op, args);
            }
            catch (Exception e)
            {
                var err = Errors.From(e);
                Send(new JObj { { "id", id }, { "ok", false }, { "code", err.Code }, { "winerr", err.WinErr }, { "message", err.Message } });
                return;
            }
            Send(new JObj { { "id", id }, { "ok", true }, { "result", result } });
        }

        static object Handle(string op, Dictionary<string, object> a)
        {
            switch (op)
            {
                case "hello": return Ops.Hello();
                case "fs.identity": return Ops.FsIdentity(a);
                case "fs.renameNoReplace": return Ops.FsRenameNoReplace(a);
                case "fs.renameExpected": return Ops.FsRenameExpected(a);
                case "fs.guardedRemove": return Ops.FsGuardedRemove(a);
                case "fs.removeDir": return Ops.FsRemoveDir(a);
                case "smb.connect": return Ops.SmbConnect(a);
                case "smb.disconnect": return Ops.SmbDisconnect(a);
                case "smb.status": return Ops.SmbStatus(a);
                case "smb.test": return Ops.SmbTest(a);
                case "gpu.list": return Ops.GpuList();
                case "perf.sample": return Perf.Sample(a);
                case "sys.info": return SysInfo.Read();
                case "power.keepAwake": return Ops.PowerKeepAwake(a);
                case "proc.tree": return Ops.ProcTree(a);
                default: throw HelperError.Invalid("Unknown op.");
            }
        }

        // Quick file and process ops go to the thread pool; ones that can
        // take seconds (SMB, WMI, DXGI, performance counters) get their own
        // thread so they never hold up the quick ones; keep-awake runs on the
        // main thread because the state belongs to the thread that set it.
        static void Dispatch(string line)
        {
            Dictionary<string, object> req;
            try { req = Json.Parse(line) as Dictionary<string, object>; }
            catch (Exception) { req = null; }
            object idv, opv, argv;
            if (req == null || !req.TryGetValue("id", out idv) || !(idv is long))
            {
                Diag("unreadable request line ignored");
                return;
            }
            long id = (long)idv;
            string op = req.TryGetValue("op", out opv) ? opv as string : null;
            Dictionary<string, object> args = null;
            if (req.TryGetValue("args", out argv) && argv != null)
            {
                args = argv as Dictionary<string, object>;
                if (args == null)
                {
                    Send(new JObj { { "id", id }, { "ok", false }, { "code", "EINVAL" }, { "winerr", 87 }, { "message", "Invalid args." } });
                    return;
                }
            }
            if (args == null) args = new Dictionary<string, object>();
            if (op == null) op = "";
            Interlocked.Increment(ref inFlight);

            if (op == "power.keepAwake")
            {
                MainQueue.Add(() => Reply(id, op, args));
            }
            else if (op.StartsWith("smb.", StringComparison.Ordinal) || op == "gpu.list" || op == "perf.sample" || op == "sys.info")
            {
                var t = new Thread(() => Reply(id, op, args)) { IsBackground = true, Name = op };
                t.Start();
            }
            else
            {
                ThreadPool.QueueUserWorkItem(_ => Reply(id, op, args));
            }
        }

        static void ReadLoop(Stream input)
        {
            try
            {
                using (var reader = new StreamReader(input, new UTF8Encoding(false, false), false, 64 * 1024))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Trim().Length == 0) continue;
                        Dispatch(line);
                    }
                }
            }
            catch (Exception e) { Diag("stdin failed (" + e.GetType().Name + ")"); }
            MainQueue.CompleteAdding();
        }

        [MTAThread]
        static int Main(string[] argv)
        {
            stdout = Console.OpenStandardOutput();
            Stream input = Console.OpenStandardInput();
            Send(new JObj { { "id", 0L }, { "ok", true }, { "result", Ops.Hello() } });

            var reader = new Thread(() => ReadLoop(input)) { IsBackground = true, Name = "stdin" };
            reader.Start();

            // The main thread runs keep-awake requests until stdin closes.
            foreach (Action work in MainQueue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception e) { Diag("main thread work failed (" + e.GetType().Name + ")"); }
            }

            // stdin closed: node has gone or closed us. Requests already
            // received get a moment to answer (a closing node may still read
            // them); slow ones are not waited for. Then keep-awake is
            // released and the helper exits.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (Volatile.Read(ref inFlight) > 0 && DateTime.UtcNow < deadline) Thread.Sleep(20);
            try { Native.SetThreadExecutionState(Native.ES_CONTINUOUS); }
            catch (Exception) { }
            Environment.Exit(0);
            return 0;
        }
    }
}

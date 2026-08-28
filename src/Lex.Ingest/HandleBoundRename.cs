using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Lex.Ingest;

internal static class HandleBoundRename
{
    public static HandleBoundRoot OpenRoot(string root) => HandleBoundRoot.Open(root);
}

internal sealed class HandleBoundRoot : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint Synchronize = 0x00100000;
    private const uint ShareAll = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePointFlag = 0x00200000;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint ObjectCaseInsensitive = 0x00000040;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint FileOpen = 1;
    private const uint FileCreate = 2;
    private const uint FileOpenIf = 3;
    private const int FileRenameInformation = 10;
    private const int FileDispositionInformation = 13;
    private const int FileAttributeTagInformation = 9;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAlreadyExists = 183;
    private const int OReadOnly = 0;
    private const int OWriteOnly = 1;
    private const int OCreate = 0x40;
    private const int OExclusive = 0x80;
    private const int ODirectory = 0x10000;
    private const int ONoFollow = 0x20000;
    private const int OCloseOnExec = 0x80000;
    private const uint OwnerFile = 0x180;
    private const uint OwnerDirectory = 0x1C0;
    private const uint RenameNoReplace = 1;
    private const int AtRemovedDirectory = 0x200;
    private const int ENoEntry = 2;
    private const int EExists = 17;
    private const int ENotDirectory = 20;
    private const int ELoop = 40;
    private static readonly int StatusObjectNameNotFound = unchecked((int)0xC0000034);
    private static readonly int StatusObjectPathNotFound = unchecked((int)0xC000003A);

    private readonly string _path;
    private readonly SafeFileHandle _handle;

    private HandleBoundRoot(string path, SafeFileHandle handle)
    {
        _path = path;
        _handle = handle;
    }

    public string RootPath => _path;

    public HandleBoundRoot OpenRelativeRoot(
        string relative, string absolutePath, bool create)
    {
        var handle = OpenDirectory(relative, create);
        return new HandleBoundRoot(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(absolutePath)),
            handle);
    }

    public string StableIdentity
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                if (!GetFileInformationByHandle(_handle, out var info))
                    throw new IOException(
                        "Could not identify the trusted corpus root handle.",
                        new Win32Exception(Marshal.GetLastWin32Error()));
                return $"windows:{info.VolumeSerialNumber:x8}:"
                       + $"{info.FileIndexHigh:x8}{info.FileIndexLow:x8}";
            }
            if (fstat(_handle.DangerousGetHandle().ToInt32(), out var status) != 0)
                ThrowUnix("identify root handle");
            return $"linux:{status.Device:x16}:{status.Inode:x16}";
        }
    }

    public bool EntryExists(string relative)
    {
        using var handle = TryOpenAny(relative, write: false);
        return handle is not null;
    }

    public static HandleBoundRoot Open(string root)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException(
                "Handle-bound corpus replacement is supported only on Windows and Linux.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        SafeFileHandle handle;
        if (OperatingSystem.IsWindows())
        {
            handle = CreateFileW(ExtendedPath(full),
                GenericRead | GenericWrite | DeleteAccess,
                ShareAll, IntPtr.Zero, OpenExisting,
                OpenReparsePointFlag | BackupSemantics, IntPtr.Zero);
            ThrowIfInvalid(handle, "root");
            RequireSafeWindowsHandle(handle, expectDirectory: true, "root");
        }
        else
        {
            var fd = open(full,
                OReadOnly | ODirectory | ONoFollow | OCloseOnExec);
            if (fd < 0) ThrowUnix("root");
            handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        }
        return new HandleBoundRoot(full, handle);
    }

    public bool Exists(string relative, bool expectDirectory)
    {
        using var handle = TryOpenAny(relative, write: false);
        if (handle is null) return false;
        RequireKind(handle, expectDirectory, relative);
        return true;
    }

    public void EnsureDirectory(string relative)
    {
        using var _ = OpenDirectory(relative, create: true);
    }

    public FileStream CreateNewFile(string relative)
    {
        var (parent, name) = ParentAndName(relative);
        EnsureDirectory(parent);
        using var parentHandle = OpenDirectory(parent, create: false);
        SafeFileHandle handle;
        if (OperatingSystem.IsWindows())
        {
            handle = OpenWindowsChild(parentHandle, name,
                GenericRead | GenericWrite | DeleteAccess | Synchronize,
                FileCreate, FileNonDirectoryFile, missingAllowed: false)
                ?? throw new IOException(
                    $"Could not create corpus transaction file '{relative}'.");
        }
        else
        {
            var fd = openat(parentHandle.DangerousGetHandle().ToInt32(), name,
                OWriteOnly | OCreate | OExclusive | ONoFollow | OCloseOnExec,
                OwnerFile);
            if (fd < 0) ThrowUnix($"create file '{relative}'");
            handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        }
        return new FileStream(handle, FileAccess.Write, 128 * 1024, isAsync: false);
    }

    public void WriteNewFile(string relative, byte[] bytes)
    {
        using var stream = CreateNewFile(relative);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        FlushDirectory(ParentAndName(relative).Parent);
    }

    public FileStream OpenRead(string relative)
    {
        var handle = TryOpenAny(relative, write: false)
            ?? throw new FileNotFoundException(
                "A handle-bound corpus file is missing.", relative);
        try
        {
            RequireKind(handle, expectDirectory: false, relative);
            return new FileStream(
                handle, FileAccess.Read, 128 * 1024, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public string HashFile(string relative)
    {
        using var stream = OpenRead(relative);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public string? HashFileOrNull(string relative)
    {
        var handle = TryOpenAny(relative, write: false);
        if (handle is null) return null;
        try
        {
            RequireKind(handle, expectDirectory: false, relative);
            using var stream = new FileStream(
                handle, FileAccess.Read, 128 * 1024, isAsync: false);
            handle = null;
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        finally { handle?.Dispose(); }
    }

    public string HashTree(string relative)
    {
        if (!Exists(relative, expectDirectory: true))
            throw new DirectoryNotFoundException(relative);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var root = Absolute(relative);
        _ = VerifiedCorpusPath.RequireExisting(
            _path, root, "transaction tree digest root");
        Visit(root, "");
        return Convert.ToHexStringLower(hash.GetHashAndReset());

        void Visit(string directory, string prefix)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory)
                         .Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry);
                ValidateComponent(name);
                var local = prefix.Length == 0 ? name : prefix + "/" + name;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "A corpus transaction tree contains a link.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Add("directory");
                    Add(local);
                    Visit(entry, local);
                }
                else
                {
                    Add("file");
                    Add(local);
                    Add(HashFile(Relative(entry)));
                }
            }
        }

        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(
                System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
            hash.AppendData(bytes);
        }
    }

    public void Move(string source, string destination, bool replace,
        Action? beforeMutation = null, Action? validateAfterHook = null)
    {
        var (sourceParent, sourceName) = ParentAndName(source);
        var (destinationParent, destinationName) = ParentAndName(destination);
        EnsureDirectory(destinationParent);
        using var sourceParentHandle = OpenDirectory(sourceParent, create: false);
        using var destinationParentHandle = OpenDirectory(
            destinationParent, create: false);
        using var sourceHandle = OpenChildAny(
            sourceParentHandle, sourceName, write: true)
            ?? throw new FileNotFoundException(
                "The handle-bound corpus source is missing.", source);

        beforeMutation?.Invoke();
        validateAfterHook?.Invoke();

        if (OperatingSystem.IsWindows())
            MoveWindows(sourceHandle, destinationParentHandle,
                destinationName, replace);
        else
        {
            try
            {
                if (renameat2(
                        sourceParentHandle.DangerousGetHandle().ToInt32(), sourceName,
                        destinationParentHandle.DangerousGetHandle().ToInt32(), destinationName,
                        replace ? 0u : RenameNoReplace) != 0)
                    ThrowUnix("rename");
            }
            catch (EntryPointNotFoundException error)
            {
                throw new PlatformNotSupportedException(
                    "Linux handle-bound replacement requires renameat2.", error);
            }
        }
        FlushHandle(sourceHandle, "renamed file");
        FlushHandle(destinationParentHandle, "destination directory");
    }

    public void FlushFile(string relative)
    {
        using var handle = TryOpenAny(relative, write: true)
            ?? throw new FileNotFoundException(
                "A corpus file disappeared before its durability flush.", relative);
        RequireKind(handle, expectDirectory: false, relative);
        FlushHandle(handle, relative);
    }

    public void FlushDirectory(string relative)
    {
        using var handle = OpenDirectory(relative, create: false);
        FlushHandle(handle, relative);
    }

    public void DeleteFile(string relative) => Delete(relative, directory: false);

    public void DeleteDirectory(string relative) => Delete(relative, directory: true);

    public void DeleteTree(string relative)
    {
        if (!Exists(relative, expectDirectory: true)) return;
        var absolute = Absolute(relative);
        _ = VerifiedCorpusPath.RequireExisting(_path, absolute,
            "transaction cleanup directory");
        foreach (var entry in Directory.EnumerateFileSystemEntries(absolute).ToArray())
        {
            var child = Relative(Path.Combine(
                absolute, Path.GetFileName(entry)));
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "A corpus transaction cleanup path contains a link.");
            if ((attributes & FileAttributes.Directory) != 0)
                DeleteTree(child);
            else
                DeleteFile(child);
        }
        DeleteDirectory(relative);
    }

    private void Delete(string relative, bool directory)
    {
        var (parent, name) = ParentAndName(relative);
        using var parentHandle = OpenDirectory(parent, create: false);
        if (OperatingSystem.IsWindows())
        {
            using var target = OpenWindowsChild(parentHandle, name,
                DeleteAccess | GenericRead | GenericWrite | Synchronize,
                FileOpen, directory ? FileDirectoryFile : FileNonDirectoryFile,
                missingAllowed: false)
                ?? throw new FileNotFoundException(
                    "The corpus cleanup target is missing.", relative);
            var memory = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(memory, 1);
                var status = NtSetInformationFile(target, out _, memory, 1,
                    FileDispositionInformation);
                ThrowNt(status, $"delete '{relative}'");
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        else if (unlinkat(parentHandle.DangerousGetHandle().ToInt32(), name,
                     directory ? AtRemovedDirectory : 0) != 0)
            ThrowUnix($"delete '{relative}'");
        FlushHandle(parentHandle, "cleanup directory");
    }

    private SafeFileHandle OpenDirectory(string relative, bool create)
    {
        var components = Components(relative);
        SafeFileHandle? current = Duplicate(_handle);
        try
        {
            foreach (var component in components)
            {
                SafeFileHandle? next;
                if (OperatingSystem.IsWindows())
                {
                    next = OpenWindowsChild(current, component,
                        GenericRead | GenericWrite | DeleteAccess | Synchronize,
                        create ? FileOpenIf : FileOpen,
                        FileDirectoryFile, missingAllowed: false);
                }
                else
                {
                    var fd = openat(current.DangerousGetHandle().ToInt32(), component,
                        OReadOnly | ODirectory | ONoFollow | OCloseOnExec, 0);
                    if (fd < 0 && create && Marshal.GetLastPInvokeError() == ENoEntry)
                    {
                        if (mkdirat(current.DangerousGetHandle().ToInt32(),
                                component, OwnerDirectory) != 0
                            && Marshal.GetLastPInvokeError() != EExists)
                            ThrowUnix($"create directory '{component}'");
                        fd = openat(current.DangerousGetHandle().ToInt32(), component,
                            OReadOnly | ODirectory | ONoFollow | OCloseOnExec, 0);
                    }
                    if (fd < 0) ThrowUnix($"open directory '{component}'");
                    next = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
                }
                if (create)
                    FlushHandle(current, "directory parent");
                current.Dispose();
                current = next!;
            }
            var result = current;
            current = null;
            return result;
        }
        finally { current?.Dispose(); }
    }

    private SafeFileHandle? TryOpenAny(string relative, bool write)
    {
        var (parent, name) = ParentAndName(relative);
        SafeFileHandle parentHandle;
        try { parentHandle = OpenDirectory(parent, create: false); }
        catch (FileNotFoundException) { return null; }
        using (parentHandle)
            return OpenChildAny(parentHandle, name, write);
    }

    private SafeFileHandle? OpenChildAny(
        SafeFileHandle parent, string name, bool write)
    {
        if (OperatingSystem.IsWindows())
            return OpenWindowsChild(parent, name,
                GenericRead | (write ? GenericWrite | DeleteAccess : 0) | Synchronize,
                FileOpen, options: 0, missingAllowed: true);

        var fd = openat(parent.DangerousGetHandle().ToInt32(), name,
            OReadOnly | ONoFollow | OCloseOnExec, 0);
        if (fd >= 0)
            return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        var error = Marshal.GetLastPInvokeError();
        if (error is ENoEntry or ENotDirectory) return null;
        if (error == ELoop)
            throw new InvalidDataException(
                "A handle-bound corpus path contains a symbolic link.");
        ThrowUnix($"open entry '{name}'");
        return null;
    }

    private static SafeFileHandle? OpenWindowsChild(
        SafeFileHandle parent,
        string name,
        uint access,
        uint disposition,
        uint options,
        bool missingAllowed)
    {
        ValidateComponent(name);
        var buffer = Marshal.StringToHGlobalUni(name);
        var unicode = new UnicodeString
        {
            Length = checked((ushort)(name.Length * 2)),
            MaximumLength = checked((ushort)(name.Length * 2)),
            Buffer = buffer,
        };
        var unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            Marshal.StructureToPtr(unicode, unicodePointer, false);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodePointer,
                Attributes = ObjectCaseInsensitive,
            };
            var status = NtCreateFile(out var handle, access, ref attributes,
                out _, IntPtr.Zero, FileAttributeNormal, ShareAll, disposition,
                options | FileOpenReparsePoint | FileSynchronousIoNonAlert,
                IntPtr.Zero, 0);
            if (status < 0)
            {
                handle?.Dispose();
                if (missingAllowed
                    && (status == StatusObjectNameNotFound
                        || status == StatusObjectPathNotFound))
                    return null;
                ThrowNt(status, $"open entry '{name}'");
            }
            ThrowIfInvalid(handle!, $"entry '{name}'");
            RequireSafeWindowsHandle(handle!, expectDirectory: null, name);
            return handle;
        }
        finally
        {
            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void MoveWindows(
        SafeFileHandle source,
        SafeFileHandle destinationParent,
        string destinationName,
        bool replace)
    {
        ValidateComponent(destinationName);
        var name = System.Text.Encoding.Unicode.GetBytes(destinationName);
        var offset = IntPtr.Size == 8 ? 20 : 12;
        var bufferLength = checked(offset + name.Length + sizeof(char));
        var memory = Marshal.AllocHGlobal(bufferLength);
        try
        {
            for (var index = 0; index < bufferLength; index++)
                Marshal.WriteByte(memory, index, 0);
            Marshal.WriteByte(memory, replace ? (byte)1 : (byte)0);
            Marshal.WriteIntPtr(memory, IntPtr.Size == 8 ? 8 : 4,
                destinationParent.DangerousGetHandle());
            Marshal.WriteInt32(memory, IntPtr.Size == 8 ? 16 : 8,
                name.Length);
            Marshal.Copy(name, 0, memory + offset, name.Length);
            var status = NtSetInformationFile(source, out _, memory,
                (uint)bufferLength, FileRenameInformation);
            ThrowNt(status, "rename");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static void RequireKind(
        SafeFileHandle handle, bool expectDirectory, string relative)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandleEx(handle, FileAttributeTagInformation,
                    out var info, (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
                throw new IOException(
                    $"Could not inspect handle-bound corpus path '{relative}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            var isDirectory = (info.FileAttributes
                               & (uint)FileAttributes.Directory) != 0;
            if (isDirectory != expectDirectory)
                throw new InvalidDataException(
                    $"Handle-bound corpus path has the wrong type: {relative}");
        }
        else
        {
            if (fstat(handle.DangerousGetHandle().ToInt32(), out var status) != 0)
                ThrowUnix($"inspect '{relative}'");
            const uint fileTypeMask = 0xF000;
            const uint directoryType = 0x4000;
            var isDirectory = (status.Mode & fileTypeMask) == directoryType;
            if (isDirectory != expectDirectory)
                throw new InvalidDataException(
                    $"Handle-bound corpus path has the wrong type: {relative}");
        }
    }

    private static void RequireSafeWindowsHandle(
        SafeFileHandle handle, bool? expectDirectory, string role)
    {
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInformation,
                out var info, (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
            throw new IOException(
                $"Could not inspect handle-bound corpus {role}.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        if ((info.FileAttributes & FileAttributeReparsePoint) != 0)
            throw new InvalidDataException(
                $"Handle-bound corpus {role} is a reparse point or symbolic link.");
        if (expectDirectory is not null)
        {
            var isDirectory = (info.FileAttributes
                               & (uint)FileAttributes.Directory) != 0;
            if (isDirectory != expectDirectory.Value)
                throw new InvalidDataException(
                    $"Handle-bound corpus {role} has the wrong type.");
        }
    }

    private static void FlushHandle(SafeFileHandle handle, string role)
    {
        if (OperatingSystem.IsWindows())
        {
            var status = NtFlushBuffersFile(handle, out _);
            ThrowNt(status, $"flush '{role}'");
        }
        else if (fsync(handle.DangerousGetHandle().ToInt32()) != 0)
            ThrowUnix($"flush '{role}'");
    }

    private static SafeFileHandle Duplicate(SafeFileHandle source)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!DuplicateHandle(GetCurrentProcess(), source,
                    GetCurrentProcess(), out var duplicate,
                    0, false, 2))
                throw new IOException("Could not duplicate the trusted corpus root handle.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            return duplicate;
        }
        var fd = dup(source.DangerousGetHandle().ToInt32());
        if (fd < 0) ThrowUnix("duplicate root handle");
        return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
    }

    private static (string Parent, string Name) ParentAndName(string relative)
    {
        var components = Components(relative);
        if (components.Length == 0)
            throw new InvalidDataException(
                "The trusted corpus root cannot be used as a file target.");
        return (components.Length == 1
                ? "." : string.Join('/', components[..^1]),
            components[^1]);
    }

    private static string[] Components(string relative)
    {
        if (relative == ".") return [];
        var canonical = relative.Replace('\\', '/');
        if (canonical.Length == 0 || canonical.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new InvalidDataException(
                "A handle-bound corpus path is not relative.");
        var components = canonical.Split('/');
        foreach (var component in components) ValidateComponent(component);
        return components;
    }

    private static void ValidateComponent(string component)
    {
        if (component is "" or "." or ".."
            || component.IndexOfAny(['/', '\\', '\0']) >= 0
            || OperatingSystem.IsWindows()
               && (component.Contains(':', StringComparison.Ordinal)
                   || component.EndsWith(' ')
                   || component.EndsWith('.')))
            throw new InvalidDataException(
                "A handle-bound corpus path component is invalid.");
    }

    private string Absolute(string relative)
    {
        var components = Components(relative);
        return components.Aggregate(_path,
            (current, component) => Path.Combine(current, component));
    }

    private string Relative(string absolute) =>
        Path.GetRelativePath(_path, absolute).Replace('\\', '/');

    private static void ThrowIfInvalid(SafeFileHandle handle, string role)
    {
        if (handle.IsInvalid)
            throw new IOException(
                $"Could not open the handle-bound corpus {role}.",
                new Win32Exception(Marshal.GetLastWin32Error()));
    }

    private static string ExtendedPath(string path) => path.StartsWith("\\\\?\\",
        StringComparison.Ordinal) ? path : "\\\\?\\" + Path.GetFullPath(path);

    private static void ThrowNt(int status, string role)
    {
        if (status >= 0) return;
        var error = unchecked((int)RtlNtStatusToDosError(status));
        if (error is ErrorFileNotFound or ErrorPathNotFound)
            throw new FileNotFoundException(
                $"Handle-bound corpus {role} failed.");
        if (error == ErrorAlreadyExists)
            throw new IOException(
                $"Handle-bound corpus {role} refused to replace an existing target.");
        throw new IOException($"Handle-bound corpus {role} failed.",
            new Win32Exception(error));
    }

    private static void ThrowUnix(string role) => throw new IOException(
        $"Handle-bound corpus {role} failed.",
        new Win32Exception(Marshal.GetLastPInvokeError()));

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public nuint Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnixStat
    {
        public ulong Device;
        public ulong Inode;
        public ulong HardLinks;
        public uint Mode;
        public uint UserId;
        public uint GroupId;
        public uint Padding;
        public ulong DeviceId;
        public long Size;
        public long BlockSize;
        public long Blocks;
        public long AccessTime;
        public ulong AccessTimeNsec;
        public long ModifyTime;
        public ulong ModifyTimeNsec;
        public long ChangeTime;
        public ulong ChangeTimeNsec;
        public long Reserved0;
        public long Reserved1;
        public long Reserved2;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access,
        uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int informationClass,
        out FileAttributeTagInfo information, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle, out ByHandleFileInformation information);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess, SafeFileHandle sourceHandle,
        IntPtr targetProcess, out SafeFileHandle targetHandle,
        uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint options);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out SafeFileHandle fileHandle,
        uint desiredAccess,
        ref ObjectAttributes objectAttributes,
        out IoStatusBlock ioStatusBlock,
        IntPtr allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        IntPtr eaBuffer,
        uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle file,
        out IoStatusBlock ioStatusBlock, IntPtr information, uint size,
        int informationClass);

    [DllImport("ntdll.dll")]
    private static extern int NtFlushBuffersFile(
        SafeFileHandle file, out IoStatusBlock ioStatusBlock);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int openat(int dirfd, string path, int flags, uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int mkdirat(int dirfd, string path, uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int renameat2(int olddirfd, string oldpath,
        int newdirfd, string newpath, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlinkat(int dirfd, string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int fsync(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int fstat(int fd, out UnixStat status);
}

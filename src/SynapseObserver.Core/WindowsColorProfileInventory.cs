using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace SynapseObserver.Core;

public sealed partial class WindowsInventorySource
{
    internal const int MaxColorProfilesPerScope = 32;
    internal const int MaxColorProfileRecordsPerScope = MaxColorProfilesPerScope + 2;
    internal const long MaxColorProfileFileBytes = 16 * 1024 * 1024;

    private const uint SystemWideColorProfileScope = 0;
    private const uint CurrentUserColorProfileScope = 1;
    private const uint IccColorProfileType = 0;
    private const uint StandardDisplayColorProfileSubtype = 7;
    private const uint ExtendedDisplayColorProfileSubtype = 8;
    private const int HResultFileNotFound = unchecked((int)0x80070002);

    private static readonly IReadOnlyDictionary<string, string> SafeColorProfileFileNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Native_CTR.icm"] = "Native_CTR.icm",
            ["DisplayP3_CTR.icm"] = "DisplayP3_CTR.icm",
            ["sRGB_CTR.icm"] = "sRGB_CTR.icm",
            ["BT709_CTR.icm"] = "BT709_CTR.icm",
            ["AdobeRGB_CTR.icm"] = "AdobeRGB_CTR.icm",
        };

    internal readonly record struct ColorProfileTarget(uint AdapterLowPart, int AdapterHighPart, uint SourceId);

    internal delegate int ColorProfileScopeReader(ColorProfileTarget target, out uint scope);

    internal delegate int ColorProfileListReader(
        ColorProfileTarget target,
        uint scope,
        out IntPtr profileList,
        out uint profileCount);

    internal delegate int ColorProfileDefaultReader(
        ColorProfileTarget target,
        uint scope,
        uint subtype,
        out IntPtr profileName);

    internal readonly record struct ColorProfileMaterializationResult(
        string ProfileLabel,
        string? FileName,
        string? Sha256,
        bool Complete);

    private DisplayColorProfileInventoryEvidence CollectDisplayColorProfiles(
        DisplayConfigLuid adapterId,
        uint sourceId,
        CancellationToken cancellationToken)
    {
        var target = new ColorProfileTarget(adapterId.LowPart, adapterId.HighPart, sourceId);
        try
        {
            return CollectDisplayColorProfiles(
                target,
                GetColorProfileScope,
                GetColorProfileList,
                GetColorProfileDefault,
                NativeLocalFree,
                value => MaterializeColorProfile(value, GetWindowsColorDirectory(), MakeProfileLabel),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new DisplayColorProfileInventoryEvidence(
                null,
                CollectionState.Unavailable,
                [],
                "Windows color-profile APIs were unavailable.");
        }
    }

    internal static DisplayColorProfileInventoryEvidence CollectDisplayColorProfiles(
        ColorProfileTarget target,
        ColorProfileScopeReader readScope,
        ColorProfileListReader readList,
        ColorProfileDefaultReader readDefault,
        Action<IntPtr> free,
        Func<string, ColorProfileMaterializationResult> materialize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readScope);
        ArgumentNullException.ThrowIfNull(readList);
        ArgumentNullException.ThrowIfNull(readDefault);
        ArgumentNullException.ThrowIfNull(free);
        ArgumentNullException.ThrowIfNull(materialize);
        cancellationToken.ThrowIfCancellationRequested();

        var selectedScopeState = CollectionState.Collected;
        DisplayColorProfileScope? selectedScope = null;
        int scopeResult;
        uint nativeSelectedScope;
        try
        {
            scopeResult = readScope(target, out nativeSelectedScope);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            and not OutOfMemoryException and not StackOverflowException)
        {
            scopeResult = unchecked((int)0x80004005);
            nativeSelectedScope = uint.MaxValue;
        }
        if (scopeResult >= 0 && TryConvertColorProfileScope(nativeSelectedScope, out var convertedScope))
        {
            selectedScope = convertedScope;
        }
        else
        {
            selectedScopeState = CollectionState.Partial;
        }

        var scopes = new List<DisplayColorProfileScope> { DisplayColorProfileScope.CurrentUser };
        if (selectedScope is DisplayColorProfileScope.SystemWide)
        {
            scopes.Add(DisplayColorProfileScope.SystemWide);
        }

        var collectedScopes = scopes
            .Select(scope => CollectColorProfileScope(
                target,
                scope,
                readList,
                readDefault,
                free,
                materialize,
                cancellationToken))
            .ToArray();

        CollectionState state;
        if (collectedScopes.All(static scope => scope.State == CollectionState.Unavailable))
        {
            state = CollectionState.Unavailable;
        }
        else if (selectedScopeState == CollectionState.Partial
            || collectedScopes.Any(static scope => scope.State is CollectionState.Partial or CollectionState.Unavailable))
        {
            state = CollectionState.Partial;
        }
        else if (collectedScopes.All(static scope => scope.State == CollectionState.Absent))
        {
            state = CollectionState.Absent;
        }
        else
        {
            state = CollectionState.Collected;
        }

        return new DisplayColorProfileInventoryEvidence(
            selectedScope,
            state,
            collectedScopes,
            state is CollectionState.Partial or CollectionState.Unavailable
                ? "One or more Windows color-profile details were unavailable or reached a documented bound."
                : null);
    }

    private static DisplayColorProfileScopeEvidence CollectColorProfileScope(
        ColorProfileTarget target,
        DisplayColorProfileScope scope,
        ColorProfileListReader readList,
        ColorProfileDefaultReader readDefault,
        Action<IntPtr> free,
        Func<string, ColorProfileMaterializationResult> materialize,
        CancellationToken cancellationToken)
    {
        var profiles = new Dictionary<string, MutableColorProfile>(StringComparer.Ordinal);
        var successfulCalls = 0;
        var failedCalls = 0;
        var partial = false;
        var truncated = false;
        uint nativeScope = scope == DisplayColorProfileScope.CurrentUser
            ? CurrentUserColorProfileScope
            : SystemWideColorProfileScope;

        cancellationToken.ThrowIfCancellationRequested();
        IntPtr profileList = IntPtr.Zero;
        try
        {
            int listResult;
            uint profileCount;
            try
            {
                listResult = readList(target, nativeScope, out profileList, out profileCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                and not OutOfMemoryException and not StackOverflowException)
            {
                listResult = unchecked((int)0x80004005);
                profileList = IntPtr.Zero;
                profileCount = 0;
            }
            if (listResult == HResultFileNotFound)
            {
                successfulCalls++;
            }
            else if (listResult < 0)
            {
                failedCalls++;
            }
            else if (profileCount > 0 && profileList == IntPtr.Zero)
            {
                failedCalls++;
            }
            else
            {
                successfulCalls++;
                uint countToRead = Math.Min(profileCount, (uint)MaxColorProfilesPerScope);
                truncated = profileCount > countToRead;
                partial |= truncated;
                for (uint index = 0; index < countToRead; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IntPtr pointer = Marshal.ReadIntPtr(profileList, checked((int)(index * IntPtr.Size)));
                    if (!TryReadNativeProfileName(pointer, out string? profileName))
                    {
                        partial = true;
                        continue;
                    }

                    AddOrUpdateProfile(profiles, profileName!, isAssociated: true, subtype: null, materialize, ref partial);
                }
            }
        }
        finally
        {
            if (profileList != IntPtr.Zero)
            {
                free(profileList);
            }
        }

        ReadDefault(DisplayColorProfileSubtype.StandardSdr, StandardDisplayColorProfileSubtype);
        ReadDefault(DisplayColorProfileSubtype.ExtendedHdr, ExtendedDisplayColorProfileSubtype);

        CollectionState state = successfulCalls == 0 && failedCalls > 0
            ? CollectionState.Unavailable
            : failedCalls > 0 || partial
                ? CollectionState.Partial
                : profiles.Count == 0
                    ? CollectionState.Absent
                    : CollectionState.Collected;

        return new DisplayColorProfileScopeEvidence(
            scope,
            state,
            profiles.Values
                .Select(static profile => profile.ToEvidence())
                .OrderBy(static profile => profile.ProfileLabel, StringComparer.Ordinal)
                .ToArray(),
            truncated,
            state is CollectionState.Partial or CollectionState.Unavailable
                ? "One or more color-profile associations, defaults, or hashes were unavailable."
                : null);

        void ReadDefault(DisplayColorProfileSubtype subtype, uint nativeSubtype)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IntPtr profileNamePointer = IntPtr.Zero;
            try
            {
                int defaultResult;
                try
                {
                    defaultResult = readDefault(
                        target,
                        nativeScope,
                        nativeSubtype,
                        out profileNamePointer);
                }
                catch (Exception ex) when (ex is not OperationCanceledException
                    and not OutOfMemoryException and not StackOverflowException)
                {
                    defaultResult = unchecked((int)0x80004005);
                    profileNamePointer = IntPtr.Zero;
                }
                if (defaultResult == HResultFileNotFound)
                {
                    successfulCalls++;
                    return;
                }

                if (defaultResult < 0 || !TryReadNativeProfileName(profileNamePointer, out string? profileName))
                {
                    failedCalls++;
                    return;
                }

                successfulCalls++;
                AddOrUpdateProfile(profiles, profileName!, isAssociated: false, subtype, materialize, ref partial);
            }
            finally
            {
                if (profileNamePointer != IntPtr.Zero)
                {
                    free(profileNamePointer);
                }
            }
        }
    }

    private static void AddOrUpdateProfile(
        Dictionary<string, MutableColorProfile> profiles,
        string nativeName,
        bool isAssociated,
        DisplayColorProfileSubtype? subtype,
        Func<string, ColorProfileMaterializationResult> materialize,
        ref bool partial)
    {
        ColorProfileMaterializationResult item;
        try
        {
            item = materialize(nativeName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            and not OutOfMemoryException and not StackOverflowException)
        {
            partial = true;
            return;
        }
        partial |= !item.Complete;
        if (!profiles.TryGetValue(item.ProfileLabel, out var profile))
        {
            profile = new MutableColorProfile(item.ProfileLabel, item.FileName, item.Sha256);
            profiles.Add(item.ProfileLabel, profile);
        }
        else
        {
            profile.MergeMaterialization(item);
        }

        profile.IsAssociated |= isAssociated;
        if (subtype is not null)
        {
            profile.DefaultSubtypes.Add(subtype.Value);
        }
    }

    private static bool TryReadNativeProfileName(IntPtr pointer, out string? profileName)
    {
        profileName = null;
        if (pointer == IntPtr.Zero)
        {
            return false;
        }

        const int maximumCharacters = EvidenceLimits.MaxTextLength * 4;
        var characters = new char[maximumCharacters];
        for (var index = 0; index < maximumCharacters; index++)
        {
            char character = unchecked((char)(ushort)Marshal.ReadInt16(pointer, index * sizeof(char)));
            if (character == '\0')
            {
                profileName = new string(characters, 0, index);
                return !string.IsNullOrWhiteSpace(profileName) && IsWellFormedProfileName(profileName);
            }

            characters[index] = character;
        }

        return false;
    }

    private static bool IsWellFormedProfileName(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsControl(character) || char.IsLowSurrogate(character))
            {
                return false;
            }

            if (char.IsHighSurrogate(character))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal ColorProfileMaterializationResult MaterializeColorProfile(
        string nativeName,
        string colorDirectory,
        Func<string, string> labeler)
    {
        ArgumentNullException.ThrowIfNull(labeler);
        string labelIdentity = GetColorProfileLabelIdentity(nativeName);
        string label = labeler(labelIdentity);
        string? safeFileName = SafeColorProfileFileNames.TryGetValue(labelIdentity, out string? knownName)
            ? knownName
            : null;
        if (!TryResolveColorProfilePath(nativeName, colorDirectory, out string? path))
        {
            return new ColorProfileMaterializationResult(label, safeFileName, null, false);
        }

        string? hash = TryHashBoundedFile(path!);
        return new ColorProfileMaterializationResult(label, safeFileName, hash, hash is not null);
    }

    private string MakeProfileLabel(string labelIdentity) => MakeOpaqueLabel("profile", labelIdentity);

    internal static string GetColorProfileLabelIdentity(string nativeName)
    {
        try
        {
            string? fileName = Path.GetFileName(nativeName.Trim());
            return string.IsNullOrWhiteSpace(fileName) ? nativeName.Trim() : fileName.ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return nativeName.Trim();
        }
    }

    internal static string? GetSafeColorProfileFileName(string? value) =>
        value is not null && SafeColorProfileFileNames.TryGetValue(value, out string? knownName)
            ? knownName
            : null;

    internal static bool TryResolveColorProfilePath(
        string nativeName,
        string colorDirectory,
        out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(nativeName) || string.IsNullOrWhiteSpace(colorDirectory))
        {
            return false;
        }

        try
        {
            string approvedDirectory = Path.GetFullPath(colorDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate;
            if (Path.IsPathRooted(nativeName))
            {
                candidate = Path.GetFullPath(nativeName);
            }
            else
            {
                string trimmed = nativeName.Trim();
                if (!string.Equals(trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal))
                {
                    return false;
                }

                candidate = Path.GetFullPath(Path.Combine(approvedDirectory, trimmed));
            }

            string candidateDirectory = Path.GetDirectoryName(candidate) ?? string.Empty;
            if (!string.Equals(candidateDirectory, approvedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    internal static string? TryHashBoundedFile(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            if (!OpenedFileMatchesCandidate(stream.SafeFileHandle, path))
            {
                return null;
            }

            if (stream.Length < 0 || stream.Length > MaxColorProfileFileBytes)
            {
                return null;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > MaxColorProfileFileBytes)
                {
                    return null;
                }

                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool OpenedFileMatchesCandidate(SafeFileHandle handle, string candidate)
    {
        const int maximumFinalPathCharacters = 4096;
        var buffer = new char[maximumFinalPathCharacters];
        uint length = GetFinalPathNameByHandle(
            handle,
            buffer,
            checked((uint)buffer.Length),
            flags: 0);
        if (length == 0 || length >= checked((uint)buffer.Length))
        {
            return false;
        }

        string finalPath = NormalizeFinalPath(new string(buffer, 0, checked((int)length)));
        string expectedPath = Path.GetFullPath(candidate);
        return string.Equals(finalPath, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFinalPath(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string localPrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase)
            ? path[localPrefix.Length..]
            : path;
    }

    private static bool TryConvertColorProfileScope(uint value, out DisplayColorProfileScope scope)
    {
        scope = value switch
        {
            SystemWideColorProfileScope => DisplayColorProfileScope.SystemWide,
            CurrentUserColorProfileScope => DisplayColorProfileScope.CurrentUser,
            _ => default,
        };
        return value is SystemWideColorProfileScope or CurrentUserColorProfileScope;
    }

    private static string GetWindowsColorDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "spool",
        "drivers",
        "color");

    private static int GetColorProfileScope(ColorProfileTarget target, out uint scope)
    {
        var adapter = new DisplayConfigLuid
        {
            LowPart = target.AdapterLowPart,
            HighPart = target.AdapterHighPart,
        };
        return ColorProfileGetDisplayUserScope(adapter, target.SourceId, out scope);
    }

    private static int GetColorProfileList(
        ColorProfileTarget target,
        uint scope,
        out IntPtr profileList,
        out uint profileCount)
    {
        var adapter = new DisplayConfigLuid
        {
            LowPart = target.AdapterLowPart,
            HighPart = target.AdapterHighPart,
        };
        return ColorProfileGetDisplayList(scope, adapter, target.SourceId, out profileList, out profileCount);
    }

    private static int GetColorProfileDefault(
        ColorProfileTarget target,
        uint scope,
        uint subtype,
        out IntPtr profileName)
    {
        var adapter = new DisplayConfigLuid
        {
            LowPart = target.AdapterLowPart,
            HighPart = target.AdapterHighPart,
        };
        return ColorProfileGetDisplayDefault(
            scope,
            adapter,
            target.SourceId,
            IccColorProfileType,
            subtype,
            out profileName);
    }

    private sealed class MutableColorProfile(string label, string? fileName, string? sha256)
    {
        public string Label { get; } = label;

        public string? FileName { get; private set; } = fileName;

        public string? Sha256 { get; private set; } = sha256;

        public bool IsAssociated { get; set; }

        public HashSet<DisplayColorProfileSubtype> DefaultSubtypes { get; } = [];

        public void MergeMaterialization(ColorProfileMaterializationResult materialization)
        {
            FileName ??= materialization.FileName;
            Sha256 ??= materialization.Sha256;
        }

        public DisplayColorProfileEvidence ToEvidence() => new(
            Label,
            FileName,
            Sha256,
            IsAssociated,
            DefaultSubtypes.Order().ToArray());
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("mscms.dll", ExactSpelling = true)]
    private static extern int ColorProfileGetDisplayList(
        uint scope,
        DisplayConfigLuid targetAdapterId,
        uint sourceId,
        out IntPtr profileList,
        out uint profileCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("mscms.dll", ExactSpelling = true)]
    private static extern int ColorProfileGetDisplayDefault(
        uint scope,
        DisplayConfigLuid targetAdapterId,
        uint sourceId,
        uint profileType,
        uint profileSubtype,
        out IntPtr profileName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("mscms.dll", ExactSpelling = true)]
    private static extern int ColorProfileGetDisplayUserScope(
        DisplayConfigLuid targetAdapterId,
        uint sourceId,
        out uint scope);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "LocalFree", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr LocalFreeColorProfileMemory(IntPtr memory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "GetFinalPathNameByHandleW",
        ExactSpelling = true,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        [Out] char[] path,
        uint pathLength,
        uint flags);

    private static void NativeLocalFree(IntPtr memory) => _ = LocalFreeColorProfileMemory(memory);
}

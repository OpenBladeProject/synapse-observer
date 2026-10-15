using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SynapseObserver.Core.Tests;

public sealed class ColorProfileInventoryTests
{
    [Fact]
    public void CollectDisplayColorProfiles_CollectsCurrentScopeAndProvesOnlyDefaultSubtypes()
    {
        using var memory = new NativeMemoryFixture();
        var labels = new OpaqueLabels();
        var result = WindowsInventorySource.CollectDisplayColorProfiles(
            new WindowsInventorySource.ColorProfileTarget(1, 2, 3),
            ReadScope,
            ReadList,
            ReadDefault,
            static _ => { },
            value => Materialize(value, labels));

        Assert.Equal(DisplayColorProfileScope.CurrentUser, result.SelectedScope);
        Assert.Equal(CollectionState.Collected, result.State);
        var scope = Assert.Single(result.Scopes);
        Assert.Equal(DisplayColorProfileScope.CurrentUser, scope.Scope);
        Assert.False(scope.Truncated);
        Assert.Equal(2, scope.Profiles.Count);

        var known = Assert.Single(scope.Profiles, static profile => profile.FileName == "sRGB_CTR.icm");
        Assert.True(known.IsAssociated);
        Assert.Equal([DisplayColorProfileSubtype.StandardSdr], known.DefaultSubtypes);
        var custom = Assert.Single(scope.Profiles, static profile => profile.FileName is null);
        Assert.True(custom.IsAssociated);
        Assert.Equal([DisplayColorProfileSubtype.ExtendedHdr], custom.DefaultSubtypes);
        Assert.DoesNotContain("\u7528\u6237", string.Join('|', scope.Profiles.Select(static profile => profile.ProfileLabel)), StringComparison.Ordinal);

        static int ReadScope(WindowsInventorySource.ColorProfileTarget target, out uint scope)
        {
            scope = 1;
            return 0;
        }

        int ReadList(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            out IntPtr list,
            out uint count)
        {
            list = memory.AllocateList("sRGB_CTR.icm", "\u7528\u6237-panel.icm");
            count = 2;
            return 0;
        }

        int ReadDefault(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            uint subtype,
            out IntPtr name)
        {
            name = memory.AllocateString(subtype == 7 ? "sRGB_CTR.icm" : "\u7528\u6237-panel.icm");
            return 0;
        }
    }

    [Fact]
    public void CollectDisplayColorProfiles_BoundsListAndKeepsScopeFailuresPartial()
    {
        using var memory = new NativeMemoryFixture();
        var labels = new OpaqueLabels();
        var result = WindowsInventorySource.CollectDisplayColorProfiles(
            default,
            ReadScope,
            ReadList,
            ReadDefault,
            static _ => { },
            value => Materialize(value, labels));

        Assert.Equal(DisplayColorProfileScope.SystemWide, result.SelectedScope);
        Assert.Equal(CollectionState.Partial, result.State);
        Assert.Equal(2, result.Scopes.Count);
        var current = Assert.Single(result.Scopes, static scope => scope.Scope == DisplayColorProfileScope.CurrentUser);
        Assert.Equal(CollectionState.Partial, current.State);
        Assert.True(current.Truncated);
        Assert.Equal(WindowsInventorySource.MaxColorProfilesPerScope, current.Profiles.Count);
        var system = Assert.Single(result.Scopes, static scope => scope.Scope == DisplayColorProfileScope.SystemWide);
        Assert.Equal(CollectionState.Partial, system.State);

        static int ReadScope(WindowsInventorySource.ColorProfileTarget target, out uint scope)
        {
            scope = 0;
            return 0;
        }

        int ReadList(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            out IntPtr list,
            out uint count)
        {
            if (scope == 0)
            {
                list = IntPtr.Zero;
                count = 0;
                return unchecked((int)0x80004005);
            }

            string[] names = Enumerable.Range(0, WindowsInventorySource.MaxColorProfilesPerScope + 3)
                .Select(static index => $"profile-{index}.icm")
                .ToArray();
            list = memory.AllocateList(names);
            count = checked((uint)names.Length);
            return 0;
        }

        int ReadDefault(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            uint subtype,
            out IntPtr name)
        {
            name = IntPtr.Zero;
            return unchecked((int)0x80070002);
        }
    }

    [Fact]
    public void CollectDisplayColorProfiles_RejectsNullListAndInvalidEntryPointers()
    {
        using var memory = new NativeMemoryFixture();
        var labels = new OpaqueLabels();
        var nullList = WindowsInventorySource.CollectDisplayColorProfiles(
            default,
            CurrentUserScope,
            NullList,
            MissingDefault,
            static _ => { },
            value => Materialize(value, labels));
        Assert.Equal(CollectionState.Partial, nullList.State);
        Assert.Empty(Assert.Single(nullList.Scopes).Profiles);

        var invalidEntry = WindowsInventorySource.CollectDisplayColorProfiles(
            default,
            CurrentUserScope,
            InvalidEntryList,
            MissingDefault,
            static _ => { },
            value => Materialize(value, labels));
        Assert.Equal(CollectionState.Partial, invalidEntry.State);
        Assert.Empty(Assert.Single(invalidEntry.Scopes).Profiles);

        static int CurrentUserScope(WindowsInventorySource.ColorProfileTarget target, out uint scope)
        {
            scope = 1;
            return 0;
        }

        static int NullList(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            out IntPtr list,
            out uint count)
        {
            list = IntPtr.Zero;
            count = 1;
            return 0;
        }

        int InvalidEntryList(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            out IntPtr list,
            out uint count)
        {
            list = memory.AllocatePointer(IntPtr.Zero);
            count = 1;
            return 0;
        }

        static int MissingDefault(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            uint subtype,
            out IntPtr name)
        {
            name = IntPtr.Zero;
            return unchecked((int)0x80070002);
        }
    }

    [Fact]
    public void CollectDisplayColorProfiles_ReportsUnavailableWithoutDiscardingChildShape()
    {
        var result = WindowsInventorySource.CollectDisplayColorProfiles(
            default,
            FailScope,
            FailList,
            FailDefault,
            static _ => { },
            static _ => throw new InvalidOperationException("Materialization should not run."));

        Assert.Null(result.SelectedScope);
        Assert.Equal(CollectionState.Unavailable, result.State);
        var scope = Assert.Single(result.Scopes);
        Assert.Equal(DisplayColorProfileScope.CurrentUser, scope.Scope);
        Assert.Equal(CollectionState.Unavailable, scope.State);

        static int FailScope(WindowsInventorySource.ColorProfileTarget target, out uint scope)
        {
            scope = 99;
            return unchecked((int)0x80004005);
        }

        static int FailList(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            out IntPtr list,
            out uint count)
        {
            list = IntPtr.Zero;
            count = 0;
            return unchecked((int)0x80004005);
        }

        static int FailDefault(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            uint subtype,
            out IntPtr name)
        {
            name = IntPtr.Zero;
            return unchecked((int)0x80004005);
        }
    }

    [Fact]
    public void CollectDisplayColorProfiles_ContainsIndependentApiAndHashFailures()
    {
        using var memory = new NativeMemoryFixture();
        var result = WindowsInventorySource.CollectDisplayColorProfiles(
            default,
            CurrentUserScope,
            static (WindowsInventorySource.ColorProfileTarget target, uint scope, out IntPtr list, out uint count) =>
            {
                list = IntPtr.Zero;
                count = 0;
                throw new EntryPointNotFoundException("Synthetic missing list API.");
            },
            ReadDefault,
            static _ => { },
            static _ => new WindowsInventorySource.ColorProfileMaterializationResult(
                "profile-safe",
                null,
                null,
                false));

        Assert.Equal(CollectionState.Partial, result.State);
        var profile = Assert.Single(Assert.Single(result.Scopes).Profiles);
        Assert.Equal("profile-safe", profile.ProfileLabel);
        Assert.Null(profile.Sha256);
        Assert.Equal([DisplayColorProfileSubtype.StandardSdr], profile.DefaultSubtypes);

        static int CurrentUserScope(WindowsInventorySource.ColorProfileTarget target, out uint scope)
        {
            scope = 1;
            return 0;
        }

        int ReadDefault(
            WindowsInventorySource.ColorProfileTarget target,
            uint scope,
            uint subtype,
            out IntPtr name)
        {
            if (subtype == 7)
            {
                name = memory.AllocateString("private-owner-panel.icm");
                return 0;
            }

            name = IntPtr.Zero;
            throw new EntryPointNotFoundException("Synthetic missing extended-default API.");
        }
    }

    [Fact]
    public void MaterializeColorProfile_HashesOnlyDirectColorDirectoryFilesAndHidesCustomNames()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"profile-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string customName = "private-owner-panel.icm";
            byte[] content = [1, 2, 3, 4];
            File.WriteAllBytes(Path.Combine(directory, customName), content);
            var source = new WindowsInventorySource();
            var custom = source.MaterializeColorProfile(customName, directory, static _ => "profile-safe");
            Assert.Equal("profile-safe", custom.ProfileLabel);
            Assert.Null(custom.FileName);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), custom.Sha256);
            Assert.True(custom.Complete);

            File.WriteAllBytes(Path.Combine(directory, "sRGB_CTR.icm"), content);
            var known = source.MaterializeColorProfile("sRGB_CTR.icm", directory, static _ => "profile-known");
            Assert.Equal("sRGB_CTR.icm", known.FileName);
            Assert.True(known.Complete);

            Assert.False(WindowsInventorySource.TryResolveColorProfilePath(
                Path.Combine("..", customName), directory, out _));
            Assert.False(WindowsInventorySource.TryResolveColorProfilePath(
                Path.Combine(Path.GetPathRoot(directory)!, "outside.icm"), directory, out _));

            string oversized = Path.Combine(directory, "oversized.icm");
            using (var stream = new FileStream(oversized, FileMode.CreateNew, FileAccess.Write))
            {
                stream.SetLength(WindowsInventorySource.MaxColorProfileFileBytes + 1);
            }
            Assert.Null(WindowsInventorySource.TryHashBoundedFile(oversized));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SupportSanitizer_DropsUnsafeProfileFieldsAndBoundsProfiles()
    {
        var profiles = Enumerable.Range(0, WindowsInventorySource.MaxColorProfileRecordsPerScope + 1)
            .Select(index => new DisplayColorProfileEvidence(
                index == 0 ? @"profile\unsafe" : $"profile-{index}",
                index == 1 ? "private-owner-panel.icm" : "sRGB_CTR.icm",
                index == 2 ? "not-a-hash" : new string('a', 64),
                true,
                index == 3
                    ? [(DisplayColorProfileSubtype)99]
                    : [DisplayColorProfileSubtype.StandardSdr]))
            .ToArray();
        var display = new DisplayEvidence("display-safe", "Display", true, null, [])
        {
            ColorProfiles = new DisplayColorProfileInventoryEvidence(
                DisplayColorProfileScope.CurrentUser,
                CollectionState.Collected,
                [new DisplayColorProfileScopeEvidence(
                    DisplayColorProfileScope.CurrentUser,
                    CollectionState.Collected,
                    profiles,
                    false,
                    null)],
                null),
        };
        var snapshot = SupportInventorySnapshot.Empty with
        {
            Displays = SectionResult<IReadOnlyList<DisplayEvidence>>.Collected([display]),
        };

        var sanitized = SupportInventorySanitizer.Sanitize(snapshot);
        var color = Assert.Single(sanitized.Displays.Value).ColorProfiles!;
        Assert.Equal(CollectionState.Partial, color.State);
        var scope = Assert.Single(color.Scopes);
        Assert.Equal(CollectionState.Partial, scope.State);
        Assert.True(scope.Truncated);
        Assert.True(scope.Profiles.Count < profiles.Length);
        Assert.All(scope.Profiles, static profile => Assert.DoesNotContain("owner", profile.FileName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.All(scope.Profiles, static profile => Assert.True(profile.Sha256 is null or { Length: 64 }));
        Assert.DoesNotContain(scope.Profiles.SelectMany(static profile => profile.DefaultSubtypes), static subtype => (int)subtype == 99);
    }

    private static WindowsInventorySource.ColorProfileMaterializationResult Materialize(
        string value,
        OpaqueLabels labels)
    {
        string identity = WindowsInventorySource.GetColorProfileLabelIdentity(value);
        return new WindowsInventorySource.ColorProfileMaterializationResult(
            labels.Get(identity),
            WindowsInventorySource.GetSafeColorProfileFileName(identity),
            new string('A', 64),
            true);
    }

    private sealed class OpaqueLabels
    {
        private readonly Dictionary<string, string> labels = new(StringComparer.OrdinalIgnoreCase);

        public string Get(string identity)
        {
            if (!labels.TryGetValue(identity, out string? label))
            {
                label = $"profile-{labels.Count + 1}";
                labels.Add(identity, label);
            }

            return label;
        }
    }

    private sealed class NativeMemoryFixture : IDisposable
    {
        private readonly List<IntPtr> allocations = [];

        public IntPtr AllocateString(string value)
        {
            IntPtr pointer = Marshal.StringToHGlobalUni(value);
            allocations.Add(pointer);
            return pointer;
        }

        public IntPtr AllocateList(params string[] values)
        {
            IntPtr list = Marshal.AllocHGlobal(checked(values.Length * IntPtr.Size));
            allocations.Add(list);
            for (var index = 0; index < values.Length; index++)
            {
                Marshal.WriteIntPtr(list, index * IntPtr.Size, AllocateString(values[index]));
            }

            return list;
        }

        public IntPtr AllocatePointer(IntPtr value)
        {
            IntPtr pointer = Marshal.AllocHGlobal(IntPtr.Size);
            allocations.Add(pointer);
            Marshal.WriteIntPtr(pointer, value);
            return pointer;
        }

        public void Dispose()
        {
            foreach (IntPtr pointer in allocations.AsEnumerable().Reverse())
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}

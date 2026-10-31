using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Spine;

namespace SpineViewer.Services;

/// <summary>
/// VLote: a safe, resumable queue. Work items are Spine models; move units are
/// top-level subfolders of the chosen root, so a folder containing several
/// .skel files moves only when every model in it has a decision.
/// </summary>
public enum VLoteDecision { Pending, OK, Null }

public sealed class VLoteEntry
{
    public required string SkelPath { get; init; }
    public required string GroupPath { get; init; }
    public VLoteDecision Decision { get; set; }
    public string Name => Path.GetFileName(SkelPath);
}

public sealed class VLoteQueue
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private const string StateName = ".vlote-progress.json";
    private readonly List<VLoteEntry> _entries = [];
    private readonly Dictionary<string, VLoteDecision> _saved = new(PathComparer);

    public string Root { get; }
    public IReadOnlyList<VLoteEntry> Entries => _entries;
    public int Total => _entries.Count;
    public int Finished => _entries.Count(e => e.Decision != VLoteDecision.Pending);
    public int Remaining => Total - Finished;
    public VLoteEntry? Current => _entries.FirstOrDefault(e => e.Decision == VLoteDecision.Pending);
    public string Progress => $"{Finished}/{Total} finalizados  •  {Remaining} restantes";

    public VLoteQueue(string root)
    {
        Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(Root))
            throw new DirectoryNotFoundException(Root);
        LoadProgress();

        // Never include any _OK or _Null folder, even when nested.
        foreach (var group in Directory.EnumerateDirectories(Root).OrderBy(p => p, PathComparer))
        {
            var name = Path.GetFileName(group);
            if (IsExcludedFolder(name) || IsReparsePoint(group))
                continue;
            EnumerateModels(group, group);
        }

        _entries.Sort((a, b) =>
            PathComparer.Compare(Path.GetRelativePath(Root, a.SkelPath),
                                 Path.GetRelativePath(Root, b.SkelPath)));
    }

    public string ExportDirectoryFor(VLoteEntry entry)
    {
        var relative = Path.GetRelativePath(entry.GroupPath, entry.SkelPath);
        var id = relative.Replace(Path.DirectorySeparatorChar, '_')
                         .Replace(Path.AltDirectorySeparatorChar, '_');
        foreach (var character in Path.GetInvalidFileNameChars())
            id = id.Replace(character, '_');
        return Path.Combine(entry.GroupPath, "_VLote_Exports", id);
    }

    private static bool IsExcludedFolder(string name) =>
        name.Equals("_OK", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("_Null", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("_VLote_Exports", StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private void EnumerateModels(string folder, string group)
    {
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(p => p, PathComparer))
        {
            if (IsReparsePoint(file))
                continue;
            var skel = file.EndsWith(".skel", StringComparison.OrdinalIgnoreCase) ||
                       file.EndsWith(".skel.bytes", StringComparison.OrdinalIgnoreCase);
            var json = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.ChangeExtension(file, ".atlas"));
            if (!skel && !json)
                continue;

            var relative = Path.GetRelativePath(Root, file);
            _entries.Add(new VLoteEntry
            {
                SkelPath = file,
                GroupPath = group,
                Decision = _saved.GetValueOrDefault(relative, VLoteDecision.Pending)
            });
        }

        foreach (var child in Directory.EnumerateDirectories(folder).OrderBy(p => p, PathComparer))
        {
            if (!IsExcludedFolder(Path.GetFileName(child)) && !IsReparsePoint(child))
                EnumerateModels(child, group);
        }
    }

    public string Complete(VLoteEntry entry, VLoteDecision decision)
    {
        if (decision == VLoteDecision.Pending)
            throw new ArgumentException("A decision must be OK or Null.", nameof(decision));
        if (!_entries.Contains(entry) || entry.Decision != VLoteDecision.Pending)
            throw new InvalidOperationException("This model has already been classified.");

        entry.Decision = decision;
        SaveProgress();

        var group = _entries.Where(e => PathComparer.Equals(e.GroupPath, entry.GroupPath)).ToArray();
        if (group.Any(e => e.Decision == VLoteDecision.Pending))
            return $"Modelo marcado {decision}; pasta aguardando os outros {group.Count(e => e.Decision == VLoteDecision.Pending)} modelo(s).";

        try
        {
            var target = MoveCompletedGroup(entry.GroupPath, group);
            foreach (var item in group)
                _saved.Remove(Path.GetRelativePath(Root, item.SkelPath));
            SaveProgress();
            return $"Pasta finalizada → {target}";
        }
        catch
        {
            // Preserve the source AND allow the last item to be retried.
            entry.Decision = VLoteDecision.Pending;
            SaveProgress();
            throw;
        }
    }

    private string MoveCompletedGroup(string groupSource, VLoteEntry[] group)
    {
        if (!Directory.Exists(groupSource))
            throw new DirectoryNotFoundException(groupSource);

        var allOk = group.All(e => e.Decision == VLoteDecision.OK);
        var allNull = group.All(e => e.Decision == VLoteDecision.Null);
        if (allOk || allNull)
        {
            var root = Path.Combine(Root, allOk ? "_OK" : "_Null");
            var dest = UniqueTarget(root, Path.GetFileName(groupSource));
            Directory.CreateDirectory(root);
            Directory.Move(groupSource, dest);
            return dest;
        }

        // Mixed decisions: copy the WHOLE group to both sides, then remove
        // the foreign skeleton/atlas and the foreign model's output folder.
        // Texture pages/shared resources are deliberately retained in both
        // copies. Never delete the original until BOTH destinations exist.
        string okTarget = UniqueTarget(Path.Combine(Root, "_OK"), Path.GetFileName(groupSource));
        string nullTarget = UniqueTarget(Path.Combine(Root, "_Null"), Path.GetFileName(groupSource));
        string okStage = okTarget + ".vlote-stage-" + Guid.NewGuid().ToString("N");
        string nullStage = nullTarget + ".vlote-stage-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyTree(groupSource, okStage);
            CopyTree(groupSource, nullStage);

            foreach (var entry in group)
            {
                var foreignCopy = entry.Decision == VLoteDecision.OK ? nullStage : okStage;
                var relative = Path.GetRelativePath(groupSource, entry.SkelPath);
                var foreignSkel = Path.Combine(foreignCopy, relative);
                if (File.Exists(foreignSkel)) File.Delete(foreignSkel);

                var atlas = MatchingAtlas(entry.SkelPath);
                if (atlas is not null && File.Exists(atlas) &&
                    !group.Any(other =>
                        other.Decision != entry.Decision &&
                        PathComparer.Equals(MatchingAtlas(other.SkelPath), atlas)))
                {
                    var foreignAtlas = Path.Combine(foreignCopy, Path.GetRelativePath(groupSource, atlas));
                    if (File.Exists(foreignAtlas)) File.Delete(foreignAtlas);
                }
                var relativeExport = Path.GetRelativePath(groupSource, ExportDirectoryFor(entry));
                var foreignExports = Path.Combine(foreignCopy, relativeExport);
                if (Directory.Exists(foreignExports))
                    Directory.Delete(foreignExports, true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(okTarget)!);
            Directory.CreateDirectory(Path.GetDirectoryName(nullTarget)!);
            Directory.Move(okStage, okTarget);
            Directory.Move(nullStage, nullTarget);
            // Both complete; remove source only at the end.
            Directory.Delete(groupSource, true);
            return $"{okTarget} + {nullTarget} (pasta mista)";
        }
        catch
        {
            // Do NOT delete originals. Staged output is disposable.
            if (Directory.Exists(okStage)) Directory.Delete(okStage, true);
            if (Directory.Exists(nullStage)) Directory.Delete(nullStage, true);
            throw;
        }
    }

    private static string? MatchingAtlas(string skel)
    {
        foreach (var suffix in SpineObject.PossibleSuffixMapping)
        {
            if (skel.EndsWith(suffix.Key, StringComparison.OrdinalIgnoreCase))
                return skel[..^suffix.Key.Length] + suffix.Value;
        }
        return null;
    }

    private static string UniqueTarget(string parent, string name)
    {
        var target = Path.Combine(parent, name);
        for (var i = 1; Directory.Exists(target) || File.Exists(target); i++)
            target = Path.Combine(parent, $"{name} ({i})");
        return target;
    }

    private static void CopyTree(string source, string destination)
    {
        if (IsReparsePoint(source))
            throw new IOException("Symbolic links/junctions cannot be copied by VLote.");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (IsReparsePoint(file))
                throw new IOException($"Unsafe symlink: {file}");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        }
        foreach (var sub in Directory.EnumerateDirectories(source))
            CopyTree(sub, Path.Combine(destination, Path.GetFileName(sub)));
    }

    private void LoadProgress()
    {
        var file = Path.Combine(Root, StateName);
        if (!File.Exists(file)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
            if (saved is null) return;
            foreach (var item in saved)
                if (Enum.TryParse<VLoteDecision>(item.Value, true, out var value))
                    _saved[item.Key] = value;
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to read VLote progress file: {file}", ex);
        }
    }

    private void SaveProgress()
    {
        var state = _entries.Where(e => e.Decision != VLoteDecision.Pending &&
                                         File.Exists(e.SkelPath))
            .ToDictionary(e => Path.GetRelativePath(Root, e.SkelPath),
                          e => e.Decision.ToString(), PathComparer);
        var file = Path.Combine(Root, StateName);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, file, true);
    }
}

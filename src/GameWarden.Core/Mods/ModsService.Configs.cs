using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameWarden.Core.Api;
using GameWarden.Core.Data;
using GameWarden.Core.Hosting;
using GameWarden.Core.Instances;
using GameWarden.Core.Supervision;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Mods;

/// <summary>
/// Config files edited in the panel, with revisions (docs/13 §4.7, ADR-0021). The active set's config folder is the
/// instance's config: an edit or a restore writes the file there at once, as gamewarden-run, through a temporary file in
/// the same folder that replaces it, so the game never reads half a file. It holds the mods gate while it writes, so it
/// cannot race a <c>mods.sync</c> build that copies those files. Revisions live in SQLite, per instance and path.
/// </summary>
public sealed partial class ModsService
{
    /// <summary>Revisions kept per instance and path; older ones are deleted at each save.</summary>
    public const int ConfigRevisionsKept = 50;

    /// <summary>409: the file on disk is no longer the one the edit started from; nothing was written.</summary>
    public const string ConfigChanged = "config_changed";

    /// <summary>The extensions of the config files that can be edited (text files).</summary>
    public static readonly IReadOnlyList<string> EditableConfigExtensions = [".cfg", ".json", ".yml", ".yaml", ".txt", ".ini"];

    private const string TemporarySuffix = ".gamewarden-tmp";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonDocumentOptions ConfigJson = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private enum WriteState
    {
        Written,
        Missing,
        TooLarge,
        Changed,
    }

    /// <summary>Whether a config file's extension is one the panel edits; its size is checked separately.</summary>
    public static bool IsEditableConfig(string path) => EditableConfigExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>PUT /mods/configs/content?path=</c>: writes <paramref name="content"/> into an existing config file of the active
    /// set when its SHA-256 is still <paramref name="baseSha256"/>, and stores it as an <c>edit</c> revision.
    /// </summary>
    public Task<ModsResult<ModConfigSaved>> SaveConfigAsync(string instanceId, string? path, string? content, string? baseSha256, CancellationToken cancellationToken) =>
        content is null
            ? Task.FromResult(ModsResult<ModConfigSaved>.Problem(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, "content is required."))
            : WriteConfigAsync(instanceId, path, new ConfigWrite(content, null), baseSha256, ModConfigRevisionKind.Edit, cancellationToken);

    /// <summary>
    /// <c>POST /mods/configs/revisions/{revisionId}/restore</c>: writes a revision's bytes back into its file, with the
    /// rules of an edit, as a new <c>restore</c> revision.
    /// </summary>
    public async Task<ModsResult<ModConfigSaved>> RestoreConfigAsync(string instanceId, long revisionId, string? baseSha256, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModConfigSaved>.From(problem!);
        }

        ModConfigRevisionRecord? revision;
        await using (var db = await context.Database.CreateDbContextAsync(cancellationToken))
        {
            revision = await db.ModConfigRevisions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == revisionId && row.InstanceId == instanceId, cancellationToken);
        }

        return revision is null
            ? ModsResult<ModConfigSaved>.Problem(StatusCodes.Status404NotFound, "not_found", $"'{instanceId}' has no config revision {revisionId.ToString(CultureInfo.InvariantCulture)}.")
            : await WriteConfigAsync(instanceId, revision.Path, new ConfigWrite(null, revision.Content), baseSha256, ModConfigRevisionKind.Restore, cancellationToken);
    }

    /// <summary><c>GET /mods/configs/revisions?path=</c>: a file's revisions, newest first; none before its first save.</summary>
    public async Task<ModsResult<ModConfigRevisionList>> ConfigRevisionsAsync(string instanceId, string? path, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModConfigRevisionList>.From(problem!);
        }

        if (ConfigPath(path) is not { } relative)
        {
            return ModsResult<ModConfigRevisionList>.Problem(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, PathProblem(path));
        }

        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        var revisions = await db.ModConfigRevisions.AsNoTracking()
            .Where(row => row.InstanceId == instanceId && row.Path == relative)
            .OrderByDescending(row => row.Id)
            .Select(row => new ModConfigRevisionInfo(row.Id, row.Kind, row.Sha256, row.Size, row.CreatedAt))
            .ToListAsync(cancellationToken);
        return ModsResult<ModConfigRevisionList>.Ok(new ModConfigRevisionList(relative, revisions));
    }

    /// <summary><c>GET /mods/configs/revisions/{revisionId}</c>: one revision with its text.</summary>
    public async Task<ModsResult<ModConfigRevisionContent>> ConfigRevisionAsync(string instanceId, long revisionId, CancellationToken cancellationToken)
    {
        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModConfigRevisionContent>.From(problem!);
        }

        await using var db = await context.Database.CreateDbContextAsync(cancellationToken);
        if (await db.ModConfigRevisions.AsNoTracking().FirstOrDefaultAsync(row => row.Id == revisionId && row.InstanceId == instanceId, cancellationToken) is not { } revision)
        {
            return ModsResult<ModConfigRevisionContent>.Problem(StatusCodes.Status404NotFound, "not_found", $"'{instanceId}' has no config revision {revisionId.ToString(CultureInfo.InvariantCulture)}.");
        }

        return ModsResult<ModConfigRevisionContent>.Ok(new ModConfigRevisionContent(
            revision.Id,
            revision.Path,
            revision.Kind,
            revision.Sha256,
            revision.Size,
            revision.CreatedAt,
            await ConfigTextAsync(revision.Content, cancellationToken)));
    }

    /// <summary>A config file's bytes as text: UTF-8, a byte order mark left out.</summary>
    internal static async Task<string> ConfigTextAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    internal static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private async Task<ModsResult<ModConfigSaved>> WriteConfigAsync(
        string instanceId,
        string? path,
        ConfigWrite write,
        string? baseSha256,
        ModConfigRevisionKind kind,
        CancellationToken cancellationToken)
    {
        static ModsResult<ModConfigSaved> Invalid(string message) => ModsResult<ModConfigSaved>.Problem(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, message);

        var (target, problem) = FindTarget(instanceId);
        if (target is null)
        {
            return ModsResult<ModConfigSaved>.From(problem!);
        }

        if (ConfigPath(path) is not { } relative)
        {
            return Invalid(PathProblem(path));
        }

        if (!IsEditableConfig(relative))
        {
            return Invalid($"'{relative}' cannot be edited; only {string.Join(", ", EditableConfigExtensions)} files can.");
        }

        if (baseSha256 is not { Length: 64 } || !baseSha256.All(char.IsAsciiHexDigit))
        {
            return Invalid("baseSha256: the SHA-256 (64 hex digits) of the file the edit started from is required.");
        }

        var size = write.Text is { } text ? Encoding.UTF8.GetByteCount(text) : write.Bytes!.LongLength;
        if (size > ConfigViewMaxBytes)
        {
            return Invalid($"The new content is {size.ToString(CultureInfo.InvariantCulture)} bytes; a config file can have at most {ConfigViewMaxBytes.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (JsonProblem(relative, write) is { } json)
        {
            return Invalid(json);
        }

        if (!gate.TryEnter(instanceId, ModJobGate.ConfigHolder))
        {
            return ModsResult<ModConfigSaved>.Problem(StatusCodes.Status409Conflict, ProblemCodes.ModsBusy, gate.BusyMessage(instanceId));
        }

        try
        {
            if (await ActiveSetIdAsync(instanceId, cancellationToken) is not { } setId)
            {
                return ModsResult<ModConfigSaved>.Problem(StatusCodes.Status404NotFound, "not_found", $"'{instanceId}' has no active mod set.");
            }

            string full;
            try
            {
                full = ModLayout.Resolve(ConfigFolder(target, setId), relative);
            }
            catch (InvalidDataException ex)
            {
                return Invalid($"path: {ex.Message}");
            }

            string? newest;
            await using (var db = await context.Database.CreateDbContextAsync(cancellationToken))
            {
                newest = await db.ModConfigRevisions.AsNoTracking()
                    .Where(row => row.InstanceId == instanceId && row.Path == relative)
                    .OrderByDescending(row => row.Id)
                    .Select(row => row.Sha256)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var expected = baseSha256.ToLowerInvariant();
            var (state, old, written) = context.Files.Run(() => WriteFile(full, write, expected));
            switch (state)
            {
                case WriteState.Missing:
                    return ModsResult<ModConfigSaved>.Problem(StatusCodes.Status404NotFound, "not_found", $"Mod set {setId.ToString(CultureInfo.InvariantCulture)} has no config file '{relative}'.");
                case WriteState.TooLarge:
                    return ModsResult<ModConfigSaved>.Problem(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.InvalidState,
                        $"'{relative}' is over {ConfigViewMaxBytes.ToString(CultureInfo.InvariantCulture)} bytes; it cannot be edited here.");
                case WriteState.Changed:
                    return ModsResult<ModConfigSaved>.Problem(
                        StatusCodes.Status409Conflict,
                        ConfigChanged,
                        $"'{relative}' changed on the server since it was loaded (the game, a mod or someone else wrote it); nothing was written. Reload it, or compare it with your text.");
            }

            // The file is written; the revisions are stored even if the request is cancelled now.
            var now = context.Time.GetUtcNow();
            ModConfigRevisionRecord saved;
            await using (var db = await context.Database.CreateDbContextAsync(CancellationToken.None))
            {
                var oldSha = Sha256Hex(old!);
                if (oldSha != newest)
                {
                    // What the game, a mod update or a code wrote since the last save (or before the first one).
                    db.ModConfigRevisions.Add(Revision(instanceId, relative, ModConfigRevisionKind.Server, old!, now));
                }

                saved = Revision(instanceId, relative, kind, written!, now);
                db.ModConfigRevisions.Add(saved);
                await db.SaveChangesAsync(CancellationToken.None);
                var stale = await db.ModConfigRevisions
                    .Where(row => row.InstanceId == instanceId && row.Path == relative)
                    .OrderByDescending(row => row.Id)
                    .Skip(ConfigRevisionsKept)
                    .Select(row => row.Id)
                    .ToListAsync(CancellationToken.None);
                if (stale.Count > 0)
                {
                    await db.ModConfigRevisions.Where(row => stale.Contains(row.Id)).ExecuteDeleteAsync(CancellationToken.None);
                }
            }

            var running = await RunsSetAsync(instanceId, setId);
            logger.LogInformation(
                "Config {Path} of {Instance} {Action} (set {Set}, revision {Revision}{Running}).",
                LogText.Clean(relative),
                LogText.Clean(instanceId),
                kind == ModConfigRevisionKind.Restore ? "restored" : "edited",
                setId,
                saved.Id,
                running ? ", the server runs it" : string.Empty);
            return ModsResult<ModConfigSaved>.Ok(new ModConfigSaved(relative, saved.Sha256, saved.Size, saved.Id, running));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Writing config {Path} of {Instance} failed.", LogText.Clean(path ?? string.Empty), LogText.Clean(instanceId));
            return ModsResult<ModConfigSaved>.Problem(StatusCodes.Status409Conflict, ProblemCodes.InvalidState, $"The config file could not be written: {ex.Message}");
        }
        finally
        {
            gate.Exit(instanceId);
        }
    }

    /// <summary>
    /// Runs as gamewarden-run, synchronously (ADR-0009 §3): when the file's SHA-256 is still <paramref name="expected"/>,
    /// writes the new bytes into a temporary file next to it and replaces the file with it. Text keeps the file's byte
    /// order mark and its CRLF line ends, which a browser's text box drops.
    /// </summary>
    private static (WriteState State, byte[]? Old, byte[]? Written) WriteFile(string full, ConfigWrite write, string expected)
    {
        var file = new FileInfo(full);
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return (WriteState.Missing, null, null);
        }

        if (file.Length > ConfigViewMaxBytes)
        {
            return (WriteState.TooLarge, null, null);
        }

        var old = File.ReadAllBytes(full);
        if (Sha256Hex(old) != expected)
        {
            return (WriteState.Changed, old, null);
        }

        var written = write.Bytes ?? TextLikeFile(old, write.Text!);
        var temporary = Path.Combine(file.DirectoryName!, "." + file.Name + TemporarySuffix);
        try
        {
            File.WriteAllBytes(temporary, written);
            File.Move(temporary, full, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }

        return (WriteState.Written, old, written);
    }

    /// <summary>The text as UTF-8 bytes, with the old file's byte order mark and CRLF line ends when it had them.</summary>
    internal static byte[] TextLikeFile(byte[] old, string text)
    {
        var bom = Encoding.UTF8.Preamble;
        var hadBom = old.AsSpan().StartsWith(bom);
        if (old.AsSpan().IndexOf("\r\n"u8) >= 0)
        {
            text = text.ReplaceLineEndings("\r\n");
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        return hadBom && !bytes.AsSpan().StartsWith(bom) ? [.. bom, .. bytes] : bytes;
    }

    private static string? JsonProblem(string relative, ConfigWrite write)
    {
        if (!string.Equals(Path.GetExtension(relative), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var text = write.Text ?? StrictUtf8.GetString(write.Bytes!).TrimStart('﻿');
            using var document = JsonDocument.Parse(text, ConfigJson);
            return null;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            return $"'{relative}' is not valid JSON: {ex.Message}";
        }
    }

    /// <summary>A path relative to the config folder with <c>/</c> separators, or null when it names no file or leaves it.</summary>
    private static string? ConfigPath(string? path)
    {
        try
        {
            return string.IsNullOrEmpty(path) ? null : ModLayout.Normalize(path);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string PathProblem(string? path)
    {
        try
        {
            return string.IsNullOrEmpty(path) || ModLayout.Normalize(path) is null ? "path names no file." : $"path: '{path}' is not a config file path.";
        }
        catch (InvalidDataException ex)
        {
            return $"path: {ex.Message}";
        }
    }

    /// <summary>The server runs (or starts) the set with <i>Mods on</i>: mods that watch their file see a write at once.</summary>
    private async Task<bool> RunsSetAsync(string instanceId, long setId)
    {
        if (supervisor.Find(instanceId)?.Snapshot.State is not (ActualState.Starting or ActualState.Running))
        {
            return false;
        }

        await using var db = await context.Database.CreateDbContextAsync();
        var row = await db.InstanceMods.AsNoTracking().FirstOrDefaultAsync(row => row.InstanceId == instanceId);
        return row is not null && row.RunningSetId == setId && (row.LastStartModsOn ?? true);
    }

    private static ModConfigRevisionRecord Revision(string instanceId, string path, ModConfigRevisionKind kind, byte[] content, DateTimeOffset at) => new()
    {
        InstanceId = instanceId,
        Path = path,
        Kind = kind,
        Sha256 = Sha256Hex(content),
        Size = content.LongLength,
        Content = content,
        CreatedAt = at,
    };

    /// <summary>What to write: the Owner's text, or a revision's bytes as they were.</summary>
    private sealed record ConfigWrite(string? Text, byte[]? Bytes);
}

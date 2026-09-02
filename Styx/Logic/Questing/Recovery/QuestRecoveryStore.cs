using System.IO;
using System.Text.Json;

namespace Styx.Logic.Questing.Recovery;

public sealed class QuestRecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly Action<string> _log;

    public QuestRecoveryStore(string filePath, Action<string>? log = null)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _log = log ?? (_ => { });
    }

    public QuestRecoveryDocument Load()
    {
        if (!File.Exists(_filePath))
        {
            return new QuestRecoveryDocument();
        }

        try
        {
            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var document = JsonSerializer.Deserialize<QuestRecoveryDocument>(stream, JsonOptions)
                ?? throw new JsonException("The quest recovery document was empty.");
            if (document.SchemaVersion != 1)
            {
                throw new NotSupportedException(
                    $"Quest recovery schema version {document.SchemaVersion} is not supported.");
            }

            return document;
        }
        catch (JsonException ex)
        {
            var directory = Path.GetDirectoryName(_filePath) ?? "";
            var quarantinePath = Path.Combine(
                directory,
                $"quest-recovery.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");

            try
            {
                File.Move(_filePath, quarantinePath);
            }
            catch (Exception quarantineException)
            {
                _log($"Quest recovery store load failed: {ex}\nCorrupt-file quarantine failed: {quarantineException}");
                throw;
            }

            _log($"Quest recovery store load failed; moved corrupt data to '{quarantinePath}': {ex}");
            return new QuestRecoveryDocument();
        }
        catch (Exception ex)
        {
            _log($"Quest recovery store load failed; live data was left in place: {ex}");
            throw;
        }
    }

    public void Save(QuestRecoveryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, document, JsonOptions);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(_filePath))
        {
            File.Replace(temporaryPath, _filePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporaryPath, _filePath);
        }
    }

    public static IReadOnlyCollection<uint> ReadLegacyIds(string legacyPath)
    {
        ArgumentNullException.ThrowIfNull(legacyPath);
        if (!File.Exists(legacyPath))
        {
            return Array.Empty<uint>();
        }

        return File.ReadAllText(legacyPath)
            .Split(new[] { ',', ';', '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => uint.TryParse(value, out var id) ? id : 0)
            .Where(id => id != 0)
            .Distinct()
            .ToArray();
    }

    public static void BackupLegacyOnce(string legacyPath)
    {
        ArgumentNullException.ThrowIfNull(legacyPath);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        var backupPath = Path.Combine(
            Path.GetDirectoryName(legacyPath) ?? "",
            "quest_blacklist.legacy.bak");
        if (!File.Exists(backupPath))
        {
            File.Copy(legacyPath, backupPath, overwrite: false);
        }
    }
}

using System.IO;
using Newtonsoft.Json;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Pipeline;

public class DownloadQueue
{
    private readonly string _stateDir;
    private readonly List<QueueEntry> _entries = new();
    private readonly object _lock = new();

    public DownloadQueue(string stateDir) => _stateDir = stateDir;

    public static DownloadQueue LoadFrom(string stateDir)
    {
        DownloadQueue queue = new DownloadQueue(stateDir);
        string path = Path.Combine(stateDir, "queue.json");
        if (!File.Exists(path)) return queue;

        string json = File.ReadAllText(path);
        List<QueueEntry> entries = JsonConvert.DeserializeObject<List<QueueEntry>>(json) ?? new List<QueueEntry>();
        foreach (QueueEntry entry in entries)
        {
            if (entry.Status == DownloadStatus.Active || entry.Status == DownloadStatus.Extracting
                || entry.Status == DownloadStatus.Paused)
                entry.Status = DownloadStatus.Waiting;
        }
        queue._entries.AddRange(entries);
        return queue;
    }

    public void Enqueue(QueueEntry entry)
    {
        lock (_lock)
        {
            entry.Status = DownloadStatus.Waiting;
            _entries.Add(entry);
            Persist();
        }
    }

    public void Cancel(string entryId)
    {
        lock (_lock)
        {
            QueueEntry? entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry != null) _entries.Remove(entry);
            Persist();
        }
    }

    public QueueEntry? Dequeue()
    {
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => e.Status == DownloadStatus.Waiting);
        }
    }

    public void UpdateEntry(QueueEntry updated)
    {
        lock (_lock)
        {
            int idx = _entries.FindIndex(e => e.Id == updated.Id);
            if (idx >= 0) _entries[idx] = updated;
            Persist();
        }
    }

    public IReadOnlyList<QueueEntry> GetAll()
    {
        lock (_lock) return _entries.ToList();
    }

    private void Persist()
    {
        string path = Path.Combine(_stateDir, "queue.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(_entries, Formatting.Indented));
    }
}

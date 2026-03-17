using System.Text.Json;
using Audiomatic.Models;

namespace Audiomatic.Services;

public sealed class QueueManager
{
    private List<TrackInfo> _originalQueue = [];
    private List<TrackInfo> _playQueue = [];
    private int _currentIndex = -1;
    private bool _shuffle;
    private RepeatMode _repeat = RepeatMode.None;
    private readonly Random _rng = new();

    private static readonly string QueueDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audiomatic");
    private static readonly string QueuePath = Path.Combine(QueueDir, "queue.json");

    public event Action? QueueChanged;
    public event Action<TrackInfo>? TrackChanged;

    public IReadOnlyList<TrackInfo> Queue => _playQueue;
    public int CurrentIndex => _currentIndex;
    public TrackInfo? CurrentTrack => _currentIndex >= 0 && _currentIndex < _playQueue.Count
        ? _playQueue[_currentIndex] : null;
    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            _shuffle = value;
            if (_shuffle)
                ShuffleQueue();
            else
                RestoreOriginalOrder();
        }
    }
    public RepeatMode Repeat
    {
        get => _repeat;
        set => _repeat = value;
    }

    // ── Queue setup ──────────────────────────────────────────

    public void SetQueue(List<TrackInfo> tracks, int startIndex = 0)
    {
        _originalQueue = [.. tracks];
        _playQueue = [.. tracks];
        _currentIndex = startIndex;

        if (_shuffle)
            ShuffleQueue();

        QueueChanged?.Invoke();
        if (CurrentTrack != null)
            TrackChanged?.Invoke(CurrentTrack);
    }

    public void AddToQueue(TrackInfo track)
    {
        _originalQueue.Add(track);
        _playQueue.Add(track);
        QueueChanged?.Invoke();
    }

    public void AddToQueueNext(TrackInfo track)
    {
        var insertAt = _currentIndex + 1;
        if (insertAt > _playQueue.Count) insertAt = _playQueue.Count;
        _originalQueue.Add(track);
        _playQueue.Insert(insertAt, track);
        QueueChanged?.Invoke();
    }

    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= _playQueue.Count) return;

        var track = _playQueue[index];
        _playQueue.RemoveAt(index);
        _originalQueue.Remove(track);

        if (index < _currentIndex)
            _currentIndex--;
        else if (index == _currentIndex)
        {
            if (_currentIndex >= _playQueue.Count)
                _currentIndex = _playQueue.Count - 1;
        }

        QueueChanged?.Invoke();
    }

    public void Clear()
    {
        _originalQueue.Clear();
        _playQueue.Clear();
        _currentIndex = -1;
        QueueChanged?.Invoke();
    }

    public void MoveInQueue(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _playQueue.Count) return;
        toIndex = Math.Clamp(toIndex, 0, _playQueue.Count - 1);
        if (fromIndex == toIndex) return;

        var track = _playQueue[fromIndex];
        _playQueue.RemoveAt(fromIndex);
        _playQueue.Insert(toIndex, track);
        _originalQueue = [.. _playQueue];

        if (_currentIndex == fromIndex)
            _currentIndex = toIndex;
        else if (fromIndex < _currentIndex && toIndex >= _currentIndex)
            _currentIndex--;
        else if (fromIndex > _currentIndex && toIndex <= _currentIndex)
            _currentIndex++;

        QueueChanged?.Invoke();
    }

    public void ReorderQueue(List<TrackInfo> newOrder)
    {
        var current = CurrentTrack;
        _playQueue = [.. newOrder];
        _originalQueue = [.. newOrder];
        if (current != null)
        {
            _currentIndex = _playQueue.FindIndex(t => t.Id == current.Id);
            if (_currentIndex < 0) _currentIndex = 0;
        }
        QueueChanged?.Invoke();
    }

    // ── Navigation ───────────────────────────────────────────

    public TrackInfo? PlayIndex(int index)
    {
        if (index < 0 || index >= _playQueue.Count) return null;
        _currentIndex = index;
        var track = _playQueue[_currentIndex];
        TrackChanged?.Invoke(track);
        return track;
    }

    public TrackInfo? Next()
    {
        if (_playQueue.Count == 0) return null;

        switch (_repeat)
        {
            case RepeatMode.One:
                // Stay on same track
                break;
            case RepeatMode.All:
                _currentIndex = (_currentIndex + 1) % _playQueue.Count;
                break;
            case RepeatMode.None:
                if (_currentIndex >= _playQueue.Count - 1)
                    return null; // End of queue
                _currentIndex++;
                break;
        }

        var track = _playQueue[_currentIndex];
        TrackChanged?.Invoke(track);
        return track;
    }

    public TrackInfo? Previous()
    {
        if (_playQueue.Count == 0) return null;

        switch (_repeat)
        {
            case RepeatMode.One:
                break;
            case RepeatMode.All:
                _currentIndex = (_currentIndex - 1 + _playQueue.Count) % _playQueue.Count;
                break;
            case RepeatMode.None:
                _currentIndex = Math.Max(0, _currentIndex - 1);
                break;
        }

        var track = _playQueue[_currentIndex];
        TrackChanged?.Invoke(track);
        return track;
    }

    /// <summary>
    /// Returns the next track that would play without advancing the index.
    /// </summary>
    public TrackInfo? PeekNext()
    {
        if (_playQueue.Count == 0) return null;
        return _repeat switch
        {
            RepeatMode.One => CurrentTrack,
            RepeatMode.All => _playQueue[(_currentIndex + 1) % _playQueue.Count],
            _ => _currentIndex < _playQueue.Count - 1 ? _playQueue[_currentIndex + 1] : null
        };
    }

    public bool HasNext()
    {
        if (_repeat == RepeatMode.All || _repeat == RepeatMode.One) return _playQueue.Count > 0;
        return _currentIndex < _playQueue.Count - 1;
    }

    public bool HasPrevious()
    {
        if (_repeat == RepeatMode.All || _repeat == RepeatMode.One) return _playQueue.Count > 0;
        return _currentIndex > 0;
    }

    // ── Shuffle ──────────────────────────────────────────────

    private void ShuffleQueue()
    {
        var current = CurrentTrack;

        // Fisher-Yates shuffle
        for (int i = _playQueue.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_playQueue[i], _playQueue[j]) = (_playQueue[j], _playQueue[i]);
        }

        // Move current track to front
        if (current != null)
        {
            var idx = _playQueue.FindIndex(t => t.Id == current.Id);
            if (idx > 0)
            {
                (_playQueue[0], _playQueue[idx]) = (_playQueue[idx], _playQueue[0]);
            }
            _currentIndex = 0;
        }

        QueueChanged?.Invoke();
    }

    private void RestoreOriginalOrder()
    {
        var current = CurrentTrack;
        _playQueue = [.. _originalQueue];
        if (current != null)
        {
            _currentIndex = _playQueue.FindIndex(t => t.Id == current.Id);
            if (_currentIndex < 0) _currentIndex = 0;
        }
        QueueChanged?.Invoke();
    }

    // ── Persistence ──────────────────────────────────────────

    public void SaveState()
    {
        try
        {
            Directory.CreateDirectory(QueueDir);
            var state = new QueueState
            {
                TrackPaths = _originalQueue.Select(t => t.Path).ToList(),
                CurrentIndex = _currentIndex,
                Shuffle = _shuffle,
                Repeat = _repeat.ToString()
            };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(QueuePath, json);
        }
        catch { }
    }

    public void LoadState(List<TrackInfo> allTracks)
    {
        try
        {
            if (!File.Exists(QueuePath)) return;
            var json = File.ReadAllText(QueuePath);
            var state = JsonSerializer.Deserialize<QueueState>(json);
            if (state == null) return;

            var tracksByPath = allTracks.ToDictionary(t => t.Path, StringComparer.OrdinalIgnoreCase);
            var queue = new List<TrackInfo>();
            foreach (var path in state.TrackPaths)
            {
                if (tracksByPath.TryGetValue(path, out var track))
                    queue.Add(track);
            }

            if (queue.Count > 0)
            {
                _originalQueue = queue;
                _playQueue = [.. queue];
                _currentIndex = Math.Clamp(state.CurrentIndex, 0, queue.Count - 1);
                _shuffle = state.Shuffle;
                _repeat = Enum.TryParse<RepeatMode>(state.Repeat, out var rm) ? rm : RepeatMode.None;

                if (_shuffle)
                    ShuffleQueue();
            }
        }
        catch { }
    }

    private sealed class QueueState
    {
        public List<string> TrackPaths { get; set; } = [];
        public int CurrentIndex { get; set; }
        public bool Shuffle { get; set; }
        public string Repeat { get; set; } = "None";
    }
}

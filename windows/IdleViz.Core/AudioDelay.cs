using System.Globalization;

namespace IdleViz.Core;

/// <summary>
/// The Audio delay setting: how long the visuals wait so they match what the speakers play.
/// It is saved per output device, since wired speakers, Bluetooth and network speakers differ a lot.
/// Ported from <c>AudioDelay.swift</c>.
/// </summary>
public static class AudioDelaySetting
{
    /// <summary>A map from the output device's ID to its delay in seconds.</summary>
    public const string Key = "audioDelayByDevice";

    public const double Minimum = 0;
    public const double Maximum = 2.5;
    public const double Step = 0.01;

    private const double StepsPerSecond = 100;

    /// <summary>A delay inside the slider's range, on one of its 10 ms steps.</summary>
    public static double Normalized(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return 0;
        }

        var clamped = Math.Min(Math.Max(seconds, Minimum), Maximum);
        // Divided, not multiplied by the step, so 0.07 comes out as 0.07 and not 0.07000000000000001.
        return Math.Round(clamped / Step, MidpointRounding.AwayFromZero) / StepsPerSecond;
    }

    /// <summary>
    /// The delay to use for a device: the saved one, or for a device seen for the first time the
    /// latency Windows reports for it. That is a starting point for Detect delay, not a replacement.
    /// </summary>
    public static double DelayFor(string deviceId, IReadOnlyDictionary<string, double> saved, double reportedLatency)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return Normalized(saved.TryGetValue(deviceId, out var delay) ? delay : reportedLatency);
    }

    public static string Label(double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(Normalized(seconds) * 1000, MidpointRounding.AwayFromZero)} ms");

    /// <summary>The call that tells the page the delay, so the progress bar shows the position you hear.</summary>
    public static string Script(double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"window.setAudioDelay?.({Normalized(seconds)})");
}

/// <summary>Holds analysed frames and releases each one <c>delay</c> seconds after it was captured.</summary>
public sealed class DelayLine<T>
    where T : class
{
    /// <summary>More than the longest delay at 60 frames a second.</summary>
    public const int Capacity = 240;

    private readonly Queue<(double Time, T Element)> _items = new();

    public int Count => _items.Count;

    public void Push(T element, double time)
    {
        _items.Enqueue((time, element));
        while (_items.Count > Capacity)
        {
            _items.Dequeue();
        }
    }

    /// <summary>
    /// An element captured at or before <c>time - delay</c>, or null if none has come due since the
    /// last call. Normally that is one element per call, in order. When more than two are due (the
    /// delay was shortened, or the caller fell behind), the newest of them is returned and the
    /// older ones are dropped.
    /// </summary>
    /// <remarks>
    /// Always returning the newest and dropping the rest, as the Mac's version first did, loses
    /// frames. With a delay that is a whole number of frames (every 50 ms is, at 60 frames a second)
    /// each frame then comes due right as a call is made, and the timer's jitter decides whether
    /// that call gets none or the next gets two: measured here, a quarter of the frames were
    /// dropped. Handing a second due element out on the next call instead costs at most one frame
    /// of extra delay.
    /// </remarks>
    public T? Pop(double time, double delay)
    {
        var due = time - delay;
        var dueCount = 0;
        foreach (var (itemTime, _) in _items)
        {
            if (itemTime > due)
            {
                break;
            }

            dueCount++;
        }

        if (dueCount == 0)
        {
            return null;
        }

        for (var skipped = dueCount > 2 ? dueCount - 1 : 0; skipped > 0; skipped--)
        {
            _items.Dequeue();
        }

        return _items.Dequeue().Element;
    }

    public void Clear() => _items.Clear();
}

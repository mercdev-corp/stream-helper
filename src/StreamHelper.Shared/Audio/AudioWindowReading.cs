namespace StreamHelper.Shared.Audio;

public readonly record struct AudioWindowReading(
    long TimestampUnixMs,
    double RmsDbfs,
    double PeakDbfs);

namespace Opipp.Core.Processing;

/// <summary>
/// A calibration master cannot be applied to the capture — wrong geometry, channel count or bit
/// depth. This is a configuration error, not a per-frame rejection, so it is deliberately not an
/// <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/>: the processing loop
/// catches those and records the frame as "no object", which would hide the real problem.
/// </summary>
public sealed class CalibrationConfigurationException : Exception
{
    public CalibrationConfigurationException(string message) : base(message) { }
}

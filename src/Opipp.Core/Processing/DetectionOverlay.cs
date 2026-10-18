using Opipp.Core.Alignment;
using Opipp.Core.Frames;

namespace Opipp.Core.Processing;

/// <summary>
/// Builds the <c>-sdp</c> ("show detected pixels") overlay. The engine replaces the frame with a
/// diagnostic view: a mid-grey copy of the image with every sample at or above the detection
/// threshold painted red, and a dashed dark-red rectangle one pixel outside the detected box.
/// </summary>
/// <remarks>
/// Verified against <c>pipp_core.exe</c> on a 96x96 capture whose disc is the only bright content:
/// the output is three-channel 8-bit, the background is exactly <c>(92,92,92)</c> regardless of the
/// source's dark level, the detected disc is <c>(255,0,0)</c> (1255 pixels for a radius-20 disc), and
/// the box border is <c>(207,0,0)</c> in 5-on/5-off dashes one pixel outside the red region. A frame
/// that fails detection still gets the red threshold mask but no box (oracle: 32x24 capture).
/// </remarks>
public static class DetectionOverlay
{
    /// <summary>Background grey used for every pixel below the threshold.</summary>
    public const int BackgroundGrey = 92;

    /// <summary>Colour of a sample at or above the detection threshold.</summary>
    public const int DetectedRed = 255;

    /// <summary>Colour of the dashed bounding box.</summary>
    public const int BoxRed = 207;

    /// <summary>Dash period and length, in pixels.</summary>
    private const int DashPeriod = 10;
    private const int DashLength = 5;

    /// <summary>Renders the overlay for one frame.</summary>
    /// <param name="frame">Frame after processing.</param>
    /// <param name="threshold">8-bit detection threshold (PIPP's <c>-mp</c> value).</param>
    /// <param name="minSize">Minimum object size used for the box (<c>-ms</c>).</param>
    public static Frame Render(Frame frame, int threshold, int minSize = 1)
    {
        ArgumentNullException.ThrowIfNull(frame);

        int width = frame.Width;
        int height = frame.Height;
        var output = new Frame(width, height, 3, 8);
        int limit = Math.Clamp(threshold, 0, 255);
        PlanetDetection detection = PlanetDetector.Detect(frame, limit, Math.Max(1, minSize));
        bool clip = detection.Found;
        int maskLeft = int.MaxValue, maskTop = int.MaxValue, maskRight = -1, maskBottom = -1;

        for (int i = 0; i < width * height; i++)
        {
            int value = Luma8(frame, i);
            // The engine dims the frame towards white by a quarter before painting the mask:
            // grey = (3*v + 255) / 4 (fitted exactly over the 38..123 ramp of the oracle capture).
            int grey = ((3 * value) + 255) / 4;
            int index = i * 3;
            int x = i % width;
            int y = i / width;
            bool inside = !clip ||
                (x >= detection.Left && x <= detection.Right && y >= detection.Top && y <= detection.Bottom);
            if (value >= limit && inside)
            {
                output.Samples[index] = DetectedRed;
                output.Samples[index + 1] = 0;
                output.Samples[index + 2] = 0;
                if (x < maskLeft) maskLeft = x;
                if (x > maskRight) maskRight = x;
                if (y < maskTop) maskTop = y;
                if (y > maskBottom) maskBottom = y;
            }
            else
            {
                output.Samples[index] = (ushort)grey;
                output.Samples[index + 1] = (ushort)grey;
                output.Samples[index + 2] = (ushort)grey;
            }
        }

        if (detection.Found && maskRight >= maskLeft)
        {
            // The engine's dashed box is the red region grown by one pixel on each side (oracle:
            // red 28..68 x 29..67, border 27..69 x 28..68).
            DrawBox(output, maskLeft, maskTop, maskRight, maskBottom);
        }

        return output;
    }

    /// <summary>One sample (mono) or the Rec.601 luma of a colour pixel, scaled to 8 bits.</summary>
    private static int Luma8(Frame frame, int pixel)
    {
        int channels = frame.Channels;
        int value;
        if (channels == 1)
        {
            value = frame.Samples[pixel];
        }
        else
        {
            int index = pixel * channels;
            value = ((frame.Samples[index] * 299) + (frame.Samples[index + 1] * 587) + (frame.Samples[index + 2] * 114)) / 1000;
        }

        return frame.BitDepth == 8 ? value : value >> 8;
    }

    /// <summary>Draws the 5-on/5-off box one pixel outside the mask rectangle.</summary>
    private static void DrawBox(Frame output, int left, int top, int right, int bottom)
    {
        left -= 1;
        top -= 1;
        right += 1;
        bottom += 1;

        for (int x = left; x <= right; x++)
        {
            if (x < 0 || x >= output.Width) continue;
            if ((x - left) % DashPeriod < DashLength)
            {
                SetBoxPixel(output, x, top);
                SetBoxPixel(output, x, bottom);
            }
        }

        for (int y = top; y <= bottom; y++)
        {
            if (y < 0 || y >= output.Height) continue;
            // The engine's vertical dashes are offset five pixels from the top edge (oracle: the
            // left column is on at y=33..37, 43..47, ... while the top and bottom corners are drawn
            // by the horizontal runs).
            int phase = ((y - top - 5) % DashPeriod + DashPeriod) % DashPeriod;
            if (phase < DashLength)
            {
                SetBoxPixel(output, left, y);
                SetBoxPixel(output, right, y);
            }
        }
    }

    private static void SetBoxPixel(Frame output, int x, int y)
    {
        if (x < 0 || y < 0 || x >= output.Width || y >= output.Height) return;
        int index = ((y * output.Width) + x) * 3;
        output.Samples[index] = BoxRed;
        output.Samples[index + 1] = 0;
        output.Samples[index + 2] = 0;
    }
}

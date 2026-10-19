using System.Drawing;
using System.Drawing.Imaging;
using Opipp.Core.Pipeline;
using PIPP_Application;

namespace Opipp.App.Tests;

/// <summary>
/// The "Add Source Files" picker must advertise exactly the formats the engine can open.
/// </summary>
/// <remarks>
/// The original filter was wider than <c>FrameSourceFactory</c>: it offered JPEG, MOV/MP4/MKV and
/// every camera RAW the native dcraw build knew. Choosing one of those files put a row in the list
/// and then failed when the run started, because the factory has no mapping for it. These tests
/// enumerate the real filter string, check every pattern against the factory, and drive the real
/// list control to prove an unsupported file is refused at add time with a message that says why.
/// </remarks>
public sealed class PickerFilterTests
{
    /// <summary>The picker entries, in the order the dialog shows them.</summary>
    private static readonly string[] ExpectedEntries =
    [
        "All Supported Types",
        "Video File(s)",
        "LuCam Recorder SER",
        "Image File(s)",
        "Raw Image File(s)",
    ];

    /// <summary>Splits the production filter string into its label/pattern pairs.</summary>
    private static List<(string Label, string[] Patterns)> ParseFilter()
    {
        string[] fields = c_filelist.SourceFilter.Split('|');
        Assert.True(fields.Length % 2 == 0, "every picker entry must be a label/pattern pair: " + c_filelist.SourceFilter);

        var entries = new List<(string, string[])>();
        for (int i = 0; i < fields.Length; i += 2)
        {
            entries.Add((fields[i], fields[i + 1].Split(';')));
        }

        return entries;
    }

    [Fact]
    public void PickerKeepsTheOriginalEntriesInOrder()
    {
        Assert.Equal(ExpectedEntries, ParseFilter().Select(entry => entry.Label));
    }

    [Fact]
    public void EveryOfferedExtensionIsOpenableByTheEngine()
    {
        foreach ((string label, string[] patterns) in ParseFilter())
        {
            Assert.NotEmpty(patterns);
            foreach (string pattern in patterns)
            {
                // A bare "*." or "*.*" would put every file in the dialog; the engine cannot open
                // what the pattern matches, so the entry must name concrete extensions.
                Assert.StartsWith("*.", pattern);
                Assert.True(pattern.Length > 2, label + " offers " + pattern + ", which names no extension");

                string extension = pattern[1..];
                Assert.True(
                    FrameSourceFactory.IsSupported("frame" + extension),
                    label + " offers " + pattern + ", but FrameSourceFactory cannot open " + extension + " files");
            }
        }
    }

    [Fact]
    public void AllSupportedTypesEntryIsExactlyTheEngineFormats()
    {
        (string label, string[] patterns) = ParseFilter()[0];
        Assert.Equal("All Supported Types", label);

        string[] offeredByAll = patterns
            .Select(pattern => pattern[1..])
            .OrderBy(extension => extension, StringComparer.Ordinal)
            .ToArray();
        string[] decodable = FrameSourceFactory.Extensions
            .OrderBy(extension => extension, StringComparer.Ordinal)
            .ToArray();

        // The headline entry claims universal support, so it must be the engine's set exactly.
        Assert.Equal(decodable, offeredByAll);

        // And the narrower entries must not hide a format the engine can open.
        string[] offeredAnywhere = c_filelist.OfferedExtensions
            .Select(extension => "." + extension)
            .OrderBy(extension => extension, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(decodable, offeredAnywhere);
    }

    [Fact]
    public void JpegIsRejectedAtAddTime_NotAtRunTime()
    {
        AppTestSupport.RunSta(() =>
        {
            string directory = AppTestSupport.TempDirectory();
            string? previousRawExtensions = cPersistentData.raw_file_extensions;
            try
            {
                string jpeg = Path.Combine(directory, "frame.jpg");
                using (var bitmap = new Bitmap(8, 8))
                {
                    bitmap.Save(jpeg, ImageFormat.Jpeg);
                }

                // The legacy guard trusted this saved setting; seeding it with JPEG must not
                // re-admit the file now that the factory is the authority.
                cPersistentData.raw_file_extensions = "jpg,jpeg,nef,cr2";

                using var list = new c_filelist();
                var reports = new List<string>();
                list.AddFailureDialogOverride = reports.Add;

                list.add_source([jpeg]);

                Assert.Empty(list.Items);
                Assert.Equal(0, list.total_frame_count);
                string report = Assert.Single(reports);
                Assert.Contains("frame.jpg", report);
                Assert.Contains("unsupported file type", report);
                Assert.Contains(".dng", report);
            }
            finally
            {
                cPersistentData.raw_file_extensions = previousRawExtensions;
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void NonDngRawIsRejectedAtAddTime()
    {
        AppTestSupport.RunSta(() =>
        {
            string directory = AppTestSupport.TempDirectory();
            try
            {
                string raw = Path.Combine(directory, "capture.nef");
                File.WriteAllBytes(raw, new byte[64]);

                using var list = new c_filelist();
                var reports = new List<string>();
                list.AddFailureDialogOverride = reports.Add;

                list.add_source([raw]);

                Assert.Empty(list.Items);
                string report = Assert.Single(reports);
                Assert.Contains("capture.nef", report);
                Assert.Contains("unsupported file type", report);
                Assert.Contains(".ser", report);
            }
            finally
            {
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void DngFileTheReaderCannotOpenIsRejectedAtAddTime()
    {
        AppTestSupport.RunSta(() =>
        {
            string directory = AppTestSupport.TempDirectory();
            try
            {
                // A file wearing the extension that is not a baseline DNG: the picker cannot tell
                // by name, so the add-time probe must refuse it before the run starts.
                string dng = Path.Combine(directory, "compressed.dng");
                File.WriteAllBytes(dng, new byte[64]);

                using var list = new c_filelist();
                var reports = new List<string>();
                list.AddFailureDialogOverride = reports.Add;

                list.add_source([dng]);

                Assert.Empty(list.Items);
                string report = Assert.Single(reports);
                Assert.Contains("compressed.dng", report);
                Assert.Contains("unsupported DNG variant", report);
            }
            finally
            {
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }

    [Fact]
    public void SupportedStillImageIsStillAdded()
    {
        AppTestSupport.RunSta(() =>
        {
            string directory = AppTestSupport.TempDirectory();
            try
            {
                string bmp = Path.Combine(directory, "frame.bmp");
                using (var bitmap = new Bitmap(8, 8))
                {
                    bitmap.Save(bmp, ImageFormat.Bmp);
                }

                using var list = new c_filelist();
                var reports = new List<string>();
                FileListEventArgs? updated = null;
                list.AddFailureDialogOverride = reports.Add;
                list.FileListUpdated += (_, e) => updated = (FileListEventArgs)e;

                list.add_source([bmp]);

                Assert.Empty(reports);
                Assert.Single(list.Items);
                Assert.Equal(1, list.total_frame_count);
                Assert.NotNull(updated);
                Assert.True(updated.ImageAdded, "a still image must still select join mode");
            }
            finally
            {
                AppTestSupport.DeleteQuietly(directory);
            }
        });
    }
}

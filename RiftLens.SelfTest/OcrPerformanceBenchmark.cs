using System.Diagnostics;
using System.Drawing.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RiftLens.Services;

internal static class OcrPerformanceBenchmark
{
    public static async Task RunAsync(string outputPath, int iterations = 10)
    {
        var reusesInferenceBuffers = typeof(BundledOcrService).Assembly
            .GetType("RiftLens.Services.PaddleOcrEngine")!
            .GetField("_recognitionValues", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) is not null;
        Console.WriteLine($"OCR benchmark: reusable inference buffers = {reusesInferenceBuffers}");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        using var ocr = new BundledOcrService(Path.Combine(root, ".tools", "ocr-benchmark-models"));
        var cases = new[]
        {
            (Name: "mixed-three-lines", Width: 860, Height: 150, FontSize: 19,
                Lines: new[] { "아리: mid 来一下", "레오나: 탑 조심, dragon 先看视野", "Jinx: 용 앞 vision 先做" }),
            (Name: "small-korean-four-lines", Width: 760, Height: 150, FontSize: 15,
                Lines: new[] { "레오나: 탑 합류 좀, 상대 정글 조심해", "아리: 미드 밀고 바텀 로밍 갈게요", "징크스: 용 앞 시야 먼저 잡아 주세요", "쓰레쉬: 점멸 빠졌어요 후퇴하세요" }),
            (Name: "dense-mixed-eight-lines", Width: 960, Height: 300, FontSize: 18,
                Lines: new[] { "아리: mid 来一下", "레오나: 탑 조심, dragon 先看视野", "Jinx: 용 앞 vision 先做", "쓰레쉬: 점멸 빠졌어요 후퇴하세요", "PlayerOne: careful jungle", "아리: 미드 밀고 바텀 로밍 갈게요", "레오나: 탑 합류 좀", "징크스: 용 앞 시야 먼저 잡아 주세요" })
        };
        var rows = new List<object>();
        var expectedHashes = new Dictionary<string, string>();
        using var process = Process.GetCurrentProcess();
        foreach (var sample in cases)
        {
            using var bitmap = new Bitmap(sample.Width, sample.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Malgun Gothic", sample.FontSize, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.FromArgb(35, 43, 54));
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (var index = 0; index < sample.Lines.Length; index++)
                {
                    var y = 13 + index * 32;
                    graphics.DrawString(sample.Lines[index], font, Brushes.Black, 17, y + 1);
                    graphics.DrawString(sample.Lines[index], font, Brushes.WhiteSmoke, 16, y);
                }
            }
            // Warm every shape before measuring; model loading is outside the timed region.
            for (var i = 0; i < 2; i++) await ocr.RecognizeDetailedAsync(bitmap, CancellationToken.None);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var elapsed = new List<double>();
            var allocated = new List<long>();
            var hashes = new HashSet<string>();
            var collectionsBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var cpuBefore = process.TotalProcessorTime;
            for (var i = 0; i < iterations; i++)
            {
                var before = GC.GetTotalAllocatedBytes(precise: true);
                var timer = Stopwatch.StartNew();
                var document = await ocr.RecognizeDetailedAsync(bitmap, CancellationToken.None);
                timer.Stop();
                allocated.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
                elapsed.Add(timer.Elapsed.TotalMilliseconds);
                // Include line positions, confidence, and colour evidence, not only text.
                hashes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document)))));
            }
            var cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / iterations;
            var sorted = elapsed.Order().ToArray();
            var row = new
            {
                sample.Name, iterations,
                p50Ms = Math.Round(sorted[(sorted.Length - 1) / 2], 2),
                p95Ms = Math.Round(sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], 2),
                meanAllocatedBytes = Math.Round(allocated.Average()),
                meanCpuMs = Math.Round(cpuMs, 2),
                gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collectionsBefore[i]).ToArray(),
                outputHashes = hashes.ToArray()
            };
            rows.Add(row);
            if (hashes.Count != 1) throw new InvalidOperationException("OCR output changed between repetitions: " + sample.Name);
            expectedHashes.Add(sample.Name, hashes.Single());
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        // Exercise retained buffers in the opposite size order as well, including
        // a larger detection scale before returning to the original dimensions.
        foreach (var sample in cases.Reverse())
        {
            using var bitmap = new Bitmap(sample.Width, sample.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Malgun Gothic", sample.FontSize, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.FromArgb(35, 43, 54));
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (var index = 0; index < sample.Lines.Length; index++)
                {
                    var y = 13 + index * 32;
                    graphics.DrawString(sample.Lines[index], font, Brushes.Black, 17, y + 1);
                    graphics.DrawString(sample.Lines[index], font, Brushes.WhiteSmoke, 16, y);
                }
            }
            await ocr.RecognizeDetailedAsync(bitmap, CancellationToken.None, ocrScale: 4);
            var document = await ocr.RecognizeDetailedAsync(bitmap, CancellationToken.None);
            var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document))));
            if (actual != expectedHashes[sample.Name]) throw new InvalidOperationException("Changing dimensions contaminated OCR buffers: " + sample.Name);
        }
        Console.WriteLine("PASS: reversed image sizes and OCR scale 4 -> 3 preserve the complete output.");
        var fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        await File.WriteAllTextAsync(fullOutputPath, JsonSerializer.Serialize(new
        {
            measuredAt = DateTimeOffset.Now,
            runtime = Environment.Version.ToString(),
            reusesInferenceBuffers,
            kind = "Synthetic warm OCR benchmark; not in-game accuracy or FPS",
            varyingDimensionsVerified = true,
            samples = rows
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

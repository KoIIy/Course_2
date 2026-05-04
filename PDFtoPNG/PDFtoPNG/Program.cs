
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;

return PdfToPngApp.Run(args);

internal static class PdfToPngApp
{
    internal const int DefaultDpi = 400;
    private const int Success = 0;
    private const int Error = 1;

    public static int Run(string[] args)
    {
        try
        {
            var options = AppOptions.Parse(args);
            var pdfFiles = FindPdfFiles(options.InputPath);

            if (pdfFiles.Count == 0)
            {
                Console.Error.WriteLine("No PDF files found.");
                return Error;
            }

            Console.WriteLine($"DPI: {options.Dpi}");
            Console.WriteLine($"PDF files: {pdfFiles.Count}");

            var totalPages = 0;
            var totalImages = 0;
            var stopwatch = Stopwatch.StartNew();

            foreach (var pdfPath in pdfFiles)
            {
                var outputDirectory = ResolveOutputDirectory(pdfPath, options, pdfFiles.Count);
                var result = ConvertPdf(pdfPath, outputDirectory, options.Dpi);

                totalPages += result.PageCount;
                totalImages += result.ImageCount;

                Console.WriteLine(
                    $"{Path.GetFileName(pdfPath)}: {result.ImageCount}/{result.PageCount} PNG -> {outputDirectory}");
            }

            stopwatch.Stop();
            Console.WriteLine(
                $"Done: {totalImages} PNG for {totalPages} pages in {stopwatch.Elapsed.TotalSeconds:F1}s.");

            return Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return Error;
        }
    }

    private static List<string> FindPdfFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            if (!Path.GetExtension(inputPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Input file must have .pdf extension.");
            }

            return [Path.GetFullPath(inputPath)];
        }

        if (!Directory.Exists(inputPath))
        {
            throw new DirectoryNotFoundException($"Input path not found: {inputPath}");
        }

        return Directory
            .EnumerateFiles(inputPath, "*.pdf", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFullPath)
            .ToList();
    }

    private static ConversionResult ConvertPdf(string pdfPath, string outputDirectory, int dpi)
    {
        var pageScale = dpi / 72.0;
        using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(pageScale));

        var pageCount = docReader.GetPageCount();
        if (pageCount <= 0)
        {
            throw new InvalidOperationException($"PDF has no pages: {pdfPath}");
        }

        Directory.CreateDirectory(outputDirectory);
        DeleteGeneratedPageImages(outputDirectory);

        var pageNumberWidth = Math.Max(3, pageCount.ToString(CultureInfo.InvariantCulture).Length);
        var background = new NaiveTransparencyRemover(255, 255, 255);
        var renderFlags = RenderFlags.RenderAnnotations;

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            using var pageReader = docReader.GetPageReader(pageIndex);
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            var bytes = checked(width * height * 4);
            var bgra = GC.AllocateUninitializedArray<byte>(bytes);

            pageReader.WriteImageToBuffer(background, renderFlags, bgra);

            var pageNumber = pageIndex + 1;
            var outputPath = Path.Combine(
                outputDirectory,
                $"page_{pageNumber.ToString($"D{pageNumberWidth}", CultureInfo.InvariantCulture)}.png");

            PngWriter.WriteBgraAsRgbPng(outputPath, bgra, width, height);
        }

        var imageCount = Directory
            .EnumerateFiles(outputDirectory, "*.png", SearchOption.TopDirectoryOnly)
            .Count(IsGeneratedPageImage);

        if (imageCount != pageCount)
        {
            throw new InvalidOperationException(
                $"Expected {pageCount} PNG files, but found {imageCount} in {outputDirectory}.");
        }

        return new ConversionResult(pageCount, imageCount);
    }

    private static string ResolveOutputDirectory(string pdfPath, AppOptions options, int pdfCount)
    {
        var pdfName = Path.GetFileNameWithoutExtension(pdfPath);

        if (options.OutputPath is null)
        {
            return Path.Combine(Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory, $"{pdfName}_png");
        }

        if (pdfCount == 1 && File.Exists(options.InputPath))
        {
            return Path.GetFullPath(options.OutputPath);
        }

        return Path.Combine(Path.GetFullPath(options.OutputPath), $"{pdfName}_png");
    }

    private static void DeleteGeneratedPageImages(string outputDirectory)
    {
        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*.png", SearchOption.TopDirectoryOnly))
        {
            if (IsGeneratedPageImage(path))
            {
                File.Delete(path);
            }
        }
    }

    private static bool IsGeneratedPageImage(string path)
    {
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith("page_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var digits = name.AsSpan("page_".Length);
        if (digits.IsEmpty)
        {
            return false;
        }

        foreach (var digit in digits)
        {
            if (!char.IsAsciiDigit(digit))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed record AppOptions(string InputPath, string? OutputPath, int Dpi)
{
    public static AppOptions Parse(string[] args)
    {
        var inputPath = Environment.CurrentDirectory;
        string? outputPath = null;
        var dpi = PdfToPngApp.DefaultDpi;
        var positional = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];

            if (arg is "-h" or "--help" or "/?")
            {
                PrintUsage();
                Environment.Exit(0);
            }

            if (arg.Equals("--dpi", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out dpi))
                {
                    throw new ArgumentException("--dpi requires a number.");
                }

                if (dpi < 72 || dpi > 600)
                {
                    throw new ArgumentOutOfRangeException(nameof(args), "DPI must be between 72 and 600.");
                }

                continue;
            }

            positional.Add(arg);
        }

        if (positional.Count > 2)
        {
            throw new ArgumentException("Too many arguments.");
        }

        if (positional.Count >= 1)
        {
            inputPath = positional[0];
        }

        if (positional.Count == 2)
        {
            outputPath = positional[1];
        }

        return new AppOptions(Path.GetFullPath(inputPath), outputPath, dpi);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PDFtoPNG [input-pdf-or-folder] [output-folder] [--dpi 400]");
        Console.WriteLine("No arguments: convert all PDF files in the current folder.");
    }
}

internal sealed record ConversionResult(int PageCount, int ImageCount);

internal static class PngWriter
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrType = Encoding.ASCII.GetBytes("IHDR");
    private static readonly byte[] IdatType = Encoding.ASCII.GetBytes("IDAT");
    private static readonly byte[] IendType = Encoding.ASCII.GetBytes("IEND");
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static void WriteBgraAsRgbPng(string path, byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        }

        var expectedLength = checked(width * height * 4);
        if (bgra.Length < expectedLength)
        {
            throw new ArgumentException("BGRA buffer is smaller than image dimensions.", nameof(bgra));
        }

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024);
        file.Write(PngSignature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(file, IhdrType, ihdr);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(width * 3 + 1)];

            for (var y = 0; y < height; y++)
            {
                row[0] = 0;
                var source = y * width * 4;
                var target = 1;

                for (var x = 0; x < width; x++)
                {
                    row[target++] = bgra[source + 2];
                    row[target++] = bgra[source + 1];
                    row[target++] = bgra[source];
                    source += 4;
                }

                zlib.Write(row, 0, row.Length);
            }
        }

        if (!compressed.TryGetBuffer(out var compressedBytes) || compressedBytes.Array is null)
        {
            WriteChunk(file, IdatType, compressed.ToArray());
        }
        else
        {
            WriteChunk(file, IdatType, compressedBytes.Array.AsSpan(compressedBytes.Offset, compressedBytes.Count));
        }

        WriteChunk(file, IendType, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        output.Write(type);
        output.Write(data);

        var crc = CalculateCrc(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        output.Write(buffer);
    }

    private static uint CalculateCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        return crc ^ 0xffffffffu;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < table.Length; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

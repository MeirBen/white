using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

return NuggetTextToolProgram.Run(args);

internal static class NuggetTextToolProgram
{
    private const string ManifestMagic = "NUGGETTXT/1";
    private const string Base64EncodingName = "base64";
    private const string FileNameHeader = "FileNameUtf8Base64";
    private const string LengthHeader = "Length";
    private const string HashHeader = "Sha256";
    private const string EncodingHeader = "Encoding";

    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "flatten" => RunFlatten(args),
                "restore" => RunRestore(args),
                _ => ExitWithUsage($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int RunFlatten(string[] args)
    {
        if (args.Length != 3)
        {
            return ExitWithUsage("flatten requires <input-file> and <output-text-file>.");
        }

        var inputPath = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[2]);

        EnsureReadableFile(inputPath);
        EnsureDistinctPaths(inputPath, outputPath);
        EnsureTargetDoesNotExist(outputPath);

        using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.NewLine = "\n";
            WriteBase64Payload(inputPath, writer);
        }

        Console.WriteLine($"Flattened '{inputPath}' to raw Base64 payload '{outputPath}'.");
        return 0;
    }

    private static int RunRestore(string[] args)
    {
        if (args.Length != 3)
        {
            return ExitWithUsage("restore requires <input-text-file> and <output-file>.");
        }

        var manifestPath = Path.GetFullPath(args[1]);
        EnsureReadableFile(manifestPath);
        var outputPath = Path.GetFullPath(args[2]);

        EnsureDistinctPaths(manifestPath, outputPath);
        EnsureTargetDoesNotExist(outputPath);

        try
        {
            using var reader = new StreamReader(manifestPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var firstLine = reader.ReadLine();
            if (firstLine is null)
            {
                throw new InvalidDataException("Input text file is empty.");
            }

            Manifest? manifest = null;
            if (string.Equals(firstLine, ManifestMagic, StringComparison.Ordinal))
            {
                manifest = ReadManifest(reader);
                DecodeBase64Payload(reader, outputPath);
            }
            else
            {
                DecodeBase64Payload(reader, outputPath, firstLine);
            }

            var restoredDescription = DescribeFile(outputPath);

            if (manifest is not null)
            {
                if (restoredDescription.Length != manifest.ExpectedLength)
                {
                    File.Delete(outputPath);
                    throw new InvalidDataException(
                        $"Length mismatch after restore. Expected {manifest.ExpectedLength}, got {restoredDescription.Length}.");
                }

                if (!string.Equals(restoredDescription.Sha256Hex, manifest.ExpectedSha256Hex, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(outputPath);
                    throw new InvalidDataException("SHA-256 mismatch after restore. The text file is incomplete or corrupted.");
                }
            }

            Console.WriteLine($"Restored '{outputPath}'.");
            Console.WriteLine(
                manifest is null
                    ? $"Output SHA-256: {restoredDescription.Sha256Hex}"
                    : $"SHA-256 verified: {restoredDescription.Sha256Hex}");
            return 0;
        }
        catch
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            throw;
        }
    }

    private static void WriteBase64Payload(string inputPath, StreamWriter writer)
    {
        byte[] readBuffer = new byte[8192];
        byte[] pendingBytes = new byte[2];
        var pendingCount = 0;

        using var input = File.OpenRead(inputPath);

        while (true)
        {
            var bytesRead = input.Read(readBuffer, 0, readBuffer.Length);
            if (bytesRead == 0)
            {
                break;
            }

            var totalCount = pendingCount + bytesRead;
            var combinedBuffer = ArrayPool<byte>.Shared.Rent(totalCount);

            try
            {
                if (pendingCount > 0)
                {
                    Buffer.BlockCopy(pendingBytes, 0, combinedBuffer, 0, pendingCount);
                }

                Buffer.BlockCopy(readBuffer, 0, combinedBuffer, pendingCount, bytesRead);

                var encodableCount = totalCount - (totalCount % 3);
                if (encodableCount > 0)
                {
                    writer.WriteLine(Convert.ToBase64String(combinedBuffer, 0, encodableCount, Base64FormattingOptions.InsertLineBreaks));
                }

                pendingCount = totalCount - encodableCount;
                if (pendingCount > 0)
                {
                    Buffer.BlockCopy(combinedBuffer, encodableCount, pendingBytes, 0, pendingCount);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(combinedBuffer);
            }
        }

        if (pendingCount > 0)
        {
            writer.WriteLine(Convert.ToBase64String(pendingBytes, 0, pendingCount));
        }
    }

    private static Manifest ReadManifest(StreamReader reader)
    {
        string? lengthValue = null;
        string? sha256Value = null;
        string? encodingValue = null;

        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                throw new InvalidDataException("Manifest ended before the Base64 payload started.");
            }

            if (line.Length == 0)
            {
                break;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                throw new InvalidDataException($"Invalid manifest line '{line}'.");
            }

            var key = line[..separatorIndex];
            var value = line[(separatorIndex + 1)..];

            switch (key)
            {
                case FileNameHeader:
                    break;
                case LengthHeader:
                    lengthValue = value;
                    break;
                case HashHeader:
                    sha256Value = value;
                    break;
                case EncodingHeader:
                    encodingValue = value;
                    break;
                default:
                    throw new InvalidDataException($"Unknown manifest key '{key}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(lengthValue) ||
            string.IsNullOrWhiteSpace(sha256Value) ||
            string.IsNullOrWhiteSpace(encodingValue))
        {
            throw new InvalidDataException("Manifest is missing one or more required metadata fields.");
        }

        if (!string.Equals(encodingValue, Base64EncodingName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported payload encoding '{encodingValue}'.");
        }

        if (!long.TryParse(lengthValue, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedLength) || expectedLength < 0)
        {
            throw new InvalidDataException($"Invalid file length '{lengthValue}'.");
        }

        var expectedSha256Hex = NormalizeSha256(sha256Value);

        return new Manifest(expectedLength, expectedSha256Hex);
    }

    private static string NormalizeSha256(string sha256Value)
    {
        if (sha256Value.Length != 64)
        {
            throw new InvalidDataException("SHA-256 must be a 64-character hexadecimal string.");
        }

        foreach (var character in sha256Value)
        {
            if (!Uri.IsHexDigit(character))
            {
                throw new InvalidDataException("SHA-256 contains non-hexadecimal characters.");
            }
        }

        return sha256Value.ToUpperInvariant();
    }

    private static void DecodeBase64Payload(StreamReader reader, string outputPath, string? firstPayloadLine = null)
    {
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var transform = new FromBase64Transform(FromBase64TransformMode.IgnoreWhiteSpaces);
        using var decoder = new CryptoStream(output, transform, CryptoStreamMode.Write);

        try
        {
            if (firstPayloadLine is not null)
            {
                WriteBase64Line(decoder, firstPayloadLine);
            }

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                WriteBase64Line(decoder, line);
            }

            decoder.FlushFinalBlock();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new InvalidDataException("The payload is not valid Base64.", ex);
        }
    }

    private static void WriteBase64Line(CryptoStream decoder, string line)
    {
        var encodedBytes = Encoding.ASCII.GetBytes(line);
        decoder.Write(encodedBytes, 0, encodedBytes.Length);
        decoder.WriteByte((byte)'\n');
    }

    private static FileDescription DescribeFile(string path)
    {
        var fileInfo = new FileInfo(path);
        using var stream = File.OpenRead(path);
        var hashBytes = SHA256.HashData(stream);
        return new FileDescription(fileInfo.Length, Convert.ToHexString(hashBytes));
    }

    private static void EnsureReadableFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }
    }

    private static void EnsureTargetDoesNotExist(string path)
    {
        if (File.Exists(path))
        {
            throw new IOException($"Target file already exists: {path}");
        }
    }

    private static void EnsureDistinctPaths(string inputPath, string outputPath)
    {
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Input and output paths must be different.");
        }
    }

    private static bool IsHelp(string arg) =>
        string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase);

    private static int ExitWithUsage(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("NuggetTextTool");
        Console.WriteLine();
        Console.WriteLine("Flatten a binary file into raw Base64 text, then restore it byte-for-byte.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- flatten <input-file> <output-text-file>");
        Console.WriteLine("  dotnet run -- restore <input-text-file> <output-file>");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine(@"  dotnet run -- flatten .\MyPackage.nupkg .\MyPackage.txt");
        Console.WriteLine(@"  dotnet run -- restore .\MyPackage.txt .\MyPackage-restored.nupkg");
    }

    private sealed record FileDescription(long Length, string Sha256Hex);

    private sealed record Manifest(long ExpectedLength, string ExpectedSha256Hex);
}

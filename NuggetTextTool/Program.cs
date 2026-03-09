using System.Buffers;
using System.Security.Cryptography;
using System.Text;

return NuggetTextToolProgram.Run(args);

internal static class NuggetTextToolProgram
{
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
                "flatten-folder" => RunFlattenFolder(args),
                "restore" => RunRestore(args),
                "restore-folder" => RunRestoreFolder(args),
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
        var description = FlattenFile(inputPath, outputPath);

        Console.WriteLine($"Flattened '{inputPath}' to single-file payload '{outputPath}'.");
        Console.WriteLine($"SHA-256: {description.Sha256Hex}");
        return 0;
    }

    private static int RunFlattenFolder(string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            return ExitWithUsage("flatten-folder requires <input-folder> and optional [output-subfolder-name].");
        }

        var inputFolderPath = Path.GetFullPath(args[1]);
        EnsureReadableDirectory(inputFolderPath);

        var outputFolderPath = ResolveOutputFolder(inputFolderPath, args.Length == 3 ? args[2] : "flattened");
        Directory.CreateDirectory(outputFolderPath);

        var inputFiles = Directory.EnumerateFiles(inputFolderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                return !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".sha256", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (inputFiles.Length == 0)
        {
            throw new InvalidOperationException($"No files found to flatten in '{inputFolderPath}'.");
        }

        var successCount = 0;
        var failureCount = 0;

        foreach (var inputFile in inputFiles)
        {
            var outputFile = Path.Combine(outputFolderPath, $"{Path.GetFileName(inputFile)}.txt");

            try
            {
                FlattenFile(inputFile, outputFile);
                Console.WriteLine($"Flattened '{Path.GetFileName(inputFile)}' -> '{Path.GetFileName(outputFile)}'");
                successCount++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to flatten '{inputFile}': {ex.Message}");
                failureCount++;
            }
        }

        Console.WriteLine(
            $"Folder flatten complete. Output folder: '{outputFolderPath}'. Success: {successCount}. Failed: {failureCount}.");
        return failureCount == 0 ? 0 : 1;
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

        var result = RestoreFile(manifestPath, outputPath);

        Console.WriteLine($"Restored '{outputPath}'.");
        Console.WriteLine($"SHA-256 verified: {result.Sha256Hex}");
        return 0;
    }

    private static int RunRestoreFolder(string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            return ExitWithUsage("restore-folder requires <input-folder> and optional [output-subfolder-name].");
        }

        var inputFolderPath = Path.GetFullPath(args[1]);
        EnsureReadableDirectory(inputFolderPath);

        var outputFolderPath = ResolveOutputFolder(inputFolderPath, args.Length == 3 ? args[2] : "restored");
        Directory.CreateDirectory(outputFolderPath);

        var inputFiles = Directory.EnumerateFiles(inputFolderPath, "*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (inputFiles.Length == 0)
        {
            throw new InvalidOperationException($"No .txt payload files found in '{inputFolderPath}'.");
        }

        var successCount = 0;
        var failureCount = 0;

        foreach (var inputFile in inputFiles)
        {
            try
            {
                var outputFileName = GetRestoreOutputFileName(inputFile);
                var outputFile = Path.Combine(outputFolderPath, outputFileName);
                RestoreFile(inputFile, outputFile);

                Console.WriteLine($"Restored '{Path.GetFileName(inputFile)}' -> '{outputFileName}' (verified)");
                successCount++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to restore '{inputFile}': {ex.Message}");
                failureCount++;
            }
        }

        Console.WriteLine(
            $"Folder restore complete. Output folder: '{outputFolderPath}'. Success: {successCount}. Failed: {failureCount}.");
        return failureCount == 0 ? 0 : 1;
    }

    private static FileDescription FlattenFile(string inputPath, string outputPath)
    {
        EnsureDistinctPaths(inputPath, outputPath);
        EnsureTargetDoesNotExist(outputPath);

        var description = DescribeFile(inputPath);

        try
        {
            using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.NewLine = "\n";
                writer.WriteLine(description.Sha256Hex);
                WriteBase64Payload(inputPath, writer);
            }

            return description;
        }
        catch
        {
            DeleteIfExists(outputPath);
            throw;
        }
    }

    private static RestoreResult RestoreFile(string inputPath, string outputPath)
    {
        EnsureDistinctPaths(inputPath, outputPath);
        EnsureTargetDoesNotExist(outputPath);

        try
        {
            using var reader = new StreamReader(inputPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var expectedSha256Hex = ReadEmbeddedSha256(reader);
            DecodeBase64Payload(reader, outputPath);

            var restoredDescription = DescribeFile(outputPath);
            VerifyRestoredFile(restoredDescription, expectedSha256Hex);

            return new RestoreResult(restoredDescription.Sha256Hex);
        }
        catch
        {
            DeleteIfExists(outputPath);
            throw;
        }
    }

    private static string ReadEmbeddedSha256(StreamReader reader)
    {
        var firstLine = reader.ReadLine();
        if (firstLine is null)
        {
            throw new InvalidDataException("Input text file is empty.");
        }

        if (TryNormalizeSha256(firstLine, out var embeddedSha256))
        {
            return embeddedSha256;
        }

        throw new InvalidDataException("Line 1 must contain a 64-character SHA-256 hash.");
    }

    private static void VerifyRestoredFile(FileDescription restoredDescription, string expectedSha256Hex)
    {
        if (!string.Equals(restoredDescription.Sha256Hex, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("SHA-256 mismatch after restore. The text file is incomplete or corrupted.");
        }
    }

    private static string GetRestoreOutputFileName(string inputPath)
    {
        var inputFileName = Path.GetFileName(inputPath);
        if (!inputFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Cannot infer restore file name from '{inputFileName}'.");
        }

        var outputFileName = inputFileName[..^4];
        if (string.IsNullOrWhiteSpace(outputFileName))
        {
            throw new InvalidDataException($"Cannot infer restore file name from '{inputFileName}'.");
        }

        return outputFileName;
    }

    private static string ResolveOutputFolder(string inputFolderPath, string outputFolderOrSubfolder)
    {
        var outputFolderPath = Path.IsPathRooted(outputFolderOrSubfolder)
            ? Path.GetFullPath(outputFolderOrSubfolder)
            : Path.GetFullPath(Path.Combine(inputFolderPath, outputFolderOrSubfolder));

        if (string.Equals(inputFolderPath, outputFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Input folder and output folder must be different.");
        }

        return outputFolderPath;
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

    private static bool TryNormalizeSha256(string? sha256Value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(sha256Value) || sha256Value.Length != 64)
        {
            return false;
        }

        foreach (var character in sha256Value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        normalized = sha256Value.ToUpperInvariant();
        return true;
    }

    private static void DecodeBase64Payload(StreamReader reader, string outputPath)
    {
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var transform = new FromBase64Transform(FromBase64TransformMode.IgnoreWhiteSpaces);
        using var decoder = new CryptoStream(output, transform, CryptoStreamMode.Write);

        try
        {
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

    private static void EnsureReadableDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
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
        Console.WriteLine("  dotnet run -- flatten-folder <input-folder> [output-subfolder-name]");
        Console.WriteLine("  dotnet run -- restore <input-text-file> <output-file>");
        Console.WriteLine("  dotnet run -- restore-folder <input-folder> [output-subfolder-name]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine(@"  dotnet run -- flatten .\MyPackage.nupkg .\MyPackage.txt");
        Console.WriteLine(@"  dotnet run -- flatten-folder .\nupkgs");
        Console.WriteLine(@"  dotnet run -- restore .\MyPackage.txt .\MyPackage-restored.nupkg");
        Console.WriteLine(@"  dotnet run -- restore-folder .\nupkgs\flattened");
        Console.WriteLine();
        Console.WriteLine("Each payload file stores the SHA-256 on line 1 and the Base64 payload on the lines below.");
    }

    private sealed record FileDescription(long Length, string Sha256Hex);

    private sealed record RestoreResult(string Sha256Hex);
}

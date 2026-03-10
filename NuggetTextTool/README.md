# NuggetTextTool

`NuggetTextTool` converts any binary file, including a `.nupkg`, into a text file and restores it back to the exact original bytes.

It uses:

- Base64 for the text payload
- embedded SHA-256 verification
- embedded original file name for folder restore

## Why this approach

If the goal is "text only, but no corruption", Base64 is the standard choice. It increases file size by about 33%, but it is deterministic and reversible.

Restore accepts only the current format:

```text
line 1: SHA-256
line 2: NAME:<original-file-name>
line 3+: Base64 payload
```

## Commands

```powershell
dotnet run -- flatten .\MyPackage.nupkg .\MyPackage.txt
dotnet run -- flatten-folder .\nupkgs
dotnet run -- restore .\MyPackage.txt .\MyPackage-restored.nupkg
dotnet run -- restore-folder .\nupkgs\flattened
```

The generated text file is plain UTF-8 text.
It stores the SHA-256 hash on line 1, the original file name on line 2, and the Base64 payload below:

```text
366A997806C9ED10AE32EDF19C6E83B00689C07F46782A14B9F50B5AC3AFE1C7
NAME:MyPackage.nupkg
TVqQAAMAAAAEAAAA//8AALgAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
...
```

If the payload is malformed or the restored bytes do not match the stored SHA-256 hash, restore fails and deletes the partial output.

## Folder mode

`flatten-folder` creates a subfolder named `flattened` by default and writes one payload file per source file using this naming rule:

```text
original-file-name.ext -> <SHA-256-of-original-file-name>.txt
```

`restore-folder` creates a `restored` subfolder by default and restores the original file name from the embedded `NAME:` line, so the hashed `.txt` file name does not need to match the output file name.

Examples:

```powershell
dotnet run -- flatten-folder .\nupkgs
dotnet run -- flatten-folder .\nupkgs payloads
dotnet run -- restore-folder .\nupkgs\flattened
dotnet run -- restore-folder .\nupkgs\flattened rebuilt
```

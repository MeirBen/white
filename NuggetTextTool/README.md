# NuggetTextTool

`NuggetTextTool` converts any binary file, including a `.nupkg`, into a text file and restores it back to the exact original bytes.

It uses:

- Base64 for the text payload
- embedded SHA-256 verification

## Why this approach

If the goal is "text only, but no corruption", Base64 is the standard choice. It increases file size by about 33%, but it is deterministic and reversible.

Restore accepts only the current format:

```text
line 1: SHA-256
line 2+: Base64 payload
```

## Commands

```powershell
dotnet run -- flatten .\MyPackage.nupkg .\MyPackage.txt
dotnet run -- flatten-folder .\nupkgs
dotnet run -- restore .\MyPackage.txt .\MyPackage-restored.nupkg
dotnet run -- restore-folder .\nupkgs\flattened
```

The generated text file is plain UTF-8 text.
The first line stores the SHA-256 hash and the remaining lines store the Base64 payload:

```text
366A997806C9ED10AE32EDF19C6E83B00689C07F46782A14B9F50B5AC3AFE1C7
TVqQAAMAAAAEAAAA//8AALgAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
...
```

If the payload is malformed or the restored bytes do not match the stored SHA-256 hash, restore fails and deletes the partial output.

## Folder mode

`flatten-folder` creates a subfolder named `flattened` by default and writes one payload file per source file using this naming rule:

```text
original-file-name.ext -> original-file-name.ext.txt
```

That preserves the original name for `restore-folder`, which creates a `restored` subfolder by default and strips the final `.txt`.

Examples:

```powershell
dotnet run -- flatten-folder .\nupkgs
dotnet run -- flatten-folder .\nupkgs payloads
dotnet run -- restore-folder .\nupkgs\flattened
dotnet run -- restore-folder .\nupkgs\flattened rebuilt
```

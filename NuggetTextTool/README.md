# NuggetTextTool

`NuggetTextTool` converts any binary file, including a `.nupkg`, into a text file and restores it back to the exact original bytes.

It uses:

- Base64 for the text payload only

## Why this approach

If the goal is "text only, but no corruption", Base64 is the standard choice. It increases file size by about 33%, but it is deterministic and reversible.

## Commands

```powershell
dotnet run -- flatten .\MyPackage.nupkg .\MyPackage.txt
dotnet run -- flatten-folder .\nupkgs
dotnet run -- restore .\MyPackage.txt .\MyPackage-restored.nupkg
dotnet run -- restore-folder .\nupkgs\flattened
```

The generated text file is plain UTF-8 text containing only the Base64 payload.

If the payload is malformed, restore fails and deletes the partial output.

Restore also accepts the older `NUGGETTXT/1` manifest format for backward compatibility, but new output is payload-only.

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

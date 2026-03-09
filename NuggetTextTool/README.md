# NuggetTextTool

`NuggetTextTool` converts any binary file, including a `.nupkg`, into a text file and restores it back to the exact original bytes.

It uses:

- Base64 for the text payload only

## Why this approach

If the goal is "text only, but no corruption", Base64 is the standard choice. It increases file size by about 33%, but it is deterministic and reversible.

## Commands

```powershell
dotnet run -- flatten .\MyPackage.nupkg .\MyPackage.txt
dotnet run -- restore .\MyPackage.txt .\MyPackage-restored.nupkg
```

The generated text file is plain UTF-8 text containing only the Base64 payload.

If the payload is malformed, restore fails and deletes the partial output.

Restore also accepts the older `NUGGETTXT/1` manifest format for backward compatibility, but new output is payload-only.

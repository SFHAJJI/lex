# Lex V3

Lex V3 is a temporal coordinate system over official Luxembourg and European Union law, with a reading surface and an optional answer engine.

This integration line is being built cleanly from the V3 specification. This candidate defines only the new source, test, browser, and continuous-integration boundaries. It contains no release corpus or production index yet.

## Local verification

```powershell
dotnet test Lex.V3.slnx --configuration Release
npm test --prefix web
npm run build --prefix web
pwsh -File eng/verify-v3-tree.ps1
```

<!-- Temporary exact-head branch-protection probe. -->

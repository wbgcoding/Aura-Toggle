# Contributing

Thanks for wanting to help. Aura Toggle is a small tool and wants to stay one, so the bar for new
features is "does it make turning the lighting off or on easier".

## Questions, ideas and bugs

- Questions and feature ideas go to [Discussions](https://github.com/wbgcoding/Aura-Toggle/discussions).
- Bugs go to [Issues](https://github.com/wbgcoding/Aura-Toggle/issues/new/choose), using the bug
  report form. The log file it asks for has your Windows user name replaced already.
- Security problems go through [private reporting](SECURITY.md), never a public issue.

## Building

You need the .NET 10 SDK, and Inno Setup 6 for the installer. See
[Building](README.md#building) in the README; `build.bat` is the whole build.

Before you open a pull request, run both and make sure they pass:

```bat
dotnet build AuraToggle.csproj -c Release
powershell -ExecutionPolicy Bypass -File tests\static-checks.ps1
```

The same two run on every pull request.

## Pull requests

- One change per pull request, with a short description of what it fixes or adds and how you
  tested it.
- New interface text goes into all ten `src\Strings*.resx` files and `src\Strings.cs`. If you
  cannot translate a language, put the English text there and say so in the pull request.
- Fixed pixel distances go through `Scaled()`, so the window looks right at every display scale.
- Never add code that writes to the controller's flash memory or that opens a network connection.
- If you have an ASUS board with Aura lighting, mention the board model and whether you ran
  `tests\aura-tests.ps1` on it. That suite switches your real lighting and leaves it on.

By contributing you agree that your contribution is licensed under the [MIT licence](LICENSE).

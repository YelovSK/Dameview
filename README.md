# Dameview

A Windows image viewer built with C#, Native AOT, Win32, and Direct2D.

## Tech stack

- .NET 10
- Native AOT for Windows x64
- Raw Win32 window and message loop
- Direct2D through Vortice.Windows
- WIC for image decoding
- Custom-drawn UI

## Features

- A single <4 MB executable
- Tabs
- Split view (BSP)
- Command palette (Ctrl + Shift + P)
- Gallery panel for the open folder
- Animations
- Color themes
- Built-in installer
- Built-in updater
- Fast to launch, fast to navigate

## Motivation

I was struggling to find an image viewer that is both fast and has a nice, modern UI. Upon trying a 3rd party file manager [File Pilot](https://filepilot.tech/), I realized that modern software does not have to feel like shit. So I wanted to achieve a similar feeling in an image viewer, which is a piece of software I use a lot.

This project started only as an experiment out of curiosity. Pretty much all of the code is AI-generated, with me driving the architecture, but letting the agent generate the actual implementation. It's `{{current_year}}`, after all.

My main constraint is having a single executable that's at most 5 MB. Choosing .NET for this might not be smartest, but since C# is the language I know best, and it supports compiling into a native binary via Native AOT, it seemed like the best choice for me. Ideally, the app would be cross-platform, but the binary would likely have to grow quite a bit because I am "cheating" a bit by using WIC for decoding images, not requiring libraries like ImageSharp. For the rendering I initially wanted to go with Raylib, but ended up going with Direct2D because Raylib has some quirks I am not a fan of, and it is not as easy to compile into a single binary. The remaining functionality like window management is done via Win32 APIs, even including the HTTP calls for the built-in updater because referencing System.Net.Http adds like 2 MB.

This is an app made specifically for me, no one else. However, I open-source my personal projects because there might be people who might find them useful.

## Run

```powershell
dotnet run --project src/Dameview
```

An image path can also be passed at startup:

```powershell
dotnet run --project src/Dameview -- "C:\path\to\image.jpg"
```

## Publish

```powershell
dotnet publish src/Dameview -c Release
```

Code style is defined in `.editorconfig` and enforced by the built-in .NET analyzers. Use `dotnet format` to apply it.

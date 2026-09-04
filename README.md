# Dameview

A small Windows image viewer built with C#, Native AOT, Win32, and Direct2D through Vortice.Windows.

## Tech stack

- .NET 10
- Native AOT for Windows x64
- Raw Win32 window and message loop
- Vortice.Windows for Direct2D and DirectWrite
- WIC for image decoding
- Custom-drawn UI

## Run

```powershell
dotnet run --project src/Dameview
```

Drop an image onto the window to open it. Use the Left and Right arrow keys to move through the other images in its folder.

- Mouse wheel: zoom toward the pointer
- Left-button drag: pan
- Double-click: toggle fit and actual size
- `F`: fit to window
- `1`: actual size

An image path can also be passed at startup:

```powershell
dotnet run --project src/Dameview -- "C:\path\to\image.jpg"
```

## Publish

```powershell
dotnet publish src/Dameview -c Release
```

Code style is defined in `.editorconfig` and enforced by the built-in .NET analyzers. Use `dotnet format` to apply it.

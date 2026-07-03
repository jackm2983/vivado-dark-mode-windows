# Vivado Dark Mode

![Vivado dark mode](example_dark_mode.png)

This uses the Windows magnification API to invert a window and make it dark mode.

>> Make sure to go into Form1.cs to change the name of the window you want to invert:
```EX. IntPtr found = FindWindowContaining("TigerVNC");```

## Installation Instructions

1. Clone the repo in C:\Github.
2. Download Microsoft Visual Studio Build Tools.
3. Create a folder C:Users\user\dotnet
4. Download the Windows x86 binary zipfile from here: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
5. Unzip it into the dotnet folder.
6. Launch the Visual Studio terminal.
7. ```cd C:\Github\vivado-dark-mode-windows```
8. ```set DOTNET_ROOT=C:\Users\user\dotnet```
9. ```set PATH=C:\Users\user\dotnet;%PATH%```
10. ```set MSBuildSDKsPath=C:\Users\user\dotnet\sdk\10.0.301\Sdks```
11. ```dotnet publish WindowOverlayApp.csproj -c Debug -r win-x64 -p:PublishSelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true```
12. ```cd bin\Debug\net8.0-windows\win-x64\publish```
13. ```WindowOverlayApp.exe```
14. Then you can right click on it to create shortcut, and put that shortcut on your desktop. You could also have that shortcut run on computer startup. You can change the name and icon for that shortcut too.


## Bugs/Notes

- When switching windows, it will momentarily flash. This is because of some latency in handling the window foreground event, I am not sure if it is possible to improve on this to make it less noticeable.
- Window title is hardcoded, meaning if the dark mode program was started after the window title does not match "Vivado" it will fail.
- Currently it just inverts the program, but the magnification API supports transformation matrices so it is possible to use a different color mapping that might be better.
- Currently only supports one window of Vivado.

## License

[karna-magnification](https://github.com/perevoznyk/karna-magnification?tab=MPL-2.0-1-ov-file#readme) is licensed under the MPL 2.0. See `LICENSE_Karna.Magnification`

This is licensed under GPL-3.0

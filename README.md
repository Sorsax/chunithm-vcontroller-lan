chunithm-vcontroller
---

A touch-based virtual controller for Chunithm. The simulated 16-keys touchpad maps from A to P. The space above emulates the IR sensor. The screenshot below shows how it looks in-game. 

![screenshot](https://raw.githubusercontent.com/Nat-Lab/chunithm-vcontroller/master/doc/screenshot.png)

There are a few options available at the bottom of the window. Here's what they do:

Item|Description
---|---
Key width|Controls how wide the keys are. 
Key length|Controls how long (tall) the keys are.
Sensor height|Controls how tall the IR sensor area is.
Apply|Apply width & height settings. 
Opacity|Controls window opacity.
Allow Mouse|Allow the mouse to interact with virtual keys. The virtual controller only accepts touch control by default. 
Lock Window|Lock the window in the current position. (disable dragging)
Coin|Insert coin.
Service|Service button.
Test|Test button.
S+T|Press "Service" and "Test" at the same time.
Exit|Exit.

### Usage

Downloads are available on the [release](https://github.com/Nat-Lab/chunithm-vcontroller/releases) page. Replace the "chuniio.dll" in your game folder with the one provided in the zip file. Run ChuniVController.exe, then start the game as you usually would.

The modified `chuniio.dll` binds on UDP port 24864 and listens for incoming IO messages. `ChuniVController.exe` connects to it on the localhost. The protocol specification can be found [here](https://github.com/Nat-Lab/chunithm-vcontroller/blob/master/ChuniVController/ChuniIO/chuniio.h). It should be straightforward to create other clients. (e.g., touchscreen tablet client)

#### LAN Setup

To run over a LAN, ensure the server machine is running the modified `chuniio.dll` (it listens on all interfaces by default).

On the client machine, edit `ChuniVController/ChuniVController/App.config` and set:

```
<appSettings>
	<add key="ServerIp" value="192.168.1.100" />
	<add key="ServerPort" value="24864" />
</appSettings>
```

Replace `ServerIp` with the server’s LAN IP. `ServerPort` defaults to `24864` if omitted or invalid.

Then run `ChuniVController.exe`. The client will connect to the configured IP/port.

### Build (Windows)

Prerequisites (fresh install):
- Visual Studio Community (or Build Tools)
	- Workloads: “.NET Desktop Development” and “Desktop development with C++”
	- Include Windows 10/11 SDK and MSVC toolset (v142 or newer). If v142 is missing, you can retarget to the installed toolset in Visual Studio.
- .NET Framework 4.7.2 Developer Pack (installed with the workload or separately)

Option A: Visual Studio IDE:
- Open the solution at [ChuniVController/ChuniVController.sln](ChuniVController/ChuniVController.sln).
- Restore NuGet packages (right-click the solution → Restore NuGet Packages).
- Select `Release|x64` to build the C++ `CHUNIIO` DLL for 64-bit.
- Build the solution.
- Outputs:
	- DLL: [ChuniVController/ChuniIO/x64/Release/CHUNIIO.dll](ChuniVController/ChuniIO/x64/Release/CHUNIIO.dll)
	- EXE: [ChuniVController/ChuniVController/bin/Release/ChuniVController.exe](ChuniVController/ChuniVController/bin/Release/ChuniVController.exe)

Option: Command line:
- Open “Developer PowerShell for VS” or a PowerShell with VS Build Tools on PATH.
- Restore packages, then build:

```powershell
# From the repo root
nuget.exe restore "ChuniVController\ChuniVController.sln"
msbuild "ChuniVController\ChuniVController.sln" /p:Configuration=Release /p:Platform=x64
```

Deployment:
- Copy `CHUNIIO.dll` to your game folder, replacing the original `chuniio.dll`.
- Edit [ChuniVController/ChuniVController/App.config](ChuniVController/ChuniVController/App.config) for LAN (see above), then run `ChuniVController.exe`.
- Ensure Windows Firewall allows inbound UDP on port `24864` on the server machine.

Notes:
- Server DLL changes: not needed. It already binds to all interfaces (`INADDR_ANY`).
- Client changes: not required beyond setting `ServerIp`/`ServerPort` in `App.config` (defaults: `127.0.0.1:24864`).
- If the C++ project complains about toolset `v142`, you can install the v142 toolset in the VS Installer or retarget the project to the installed toolset (e.g., v143) via Project Properties.

### Build Without Visual Studio (DLL only)

If you don't want Visual Studio, you can build `CHUNIIO.dll` using MSYS2 + MinGW‑w64:

1) Install MSYS2 (https://www.msys2.org/) and open the "MSYS2 MinGW x64" shell.
2) Install the toolchain:

```
pacman -S --needed mingw-w64-x86_64-gcc
```

3) Build the DLL in the project folder:

```
cd /d/Webbivelhoilut/chunithm-vcontroller-lan/ChuniVController/ChuniIO
make -f Makefile.mingw
```

This produces `CHUNIIO.dll` in the same folder. Copy it to your game directory (replace `chuniio.dll`).

Client EXE (WPF) still needs MSBuild/Visual Studio to compile. If you prefer avoiding Visual Studio entirely, you can use the prebuilt release EXE, or we can refactor the project to SDK‑style so it builds with the .NET SDK. 

### Webcam Air Notes (Experimental)

The controller supports using a webcam for air note detection via motion tracking. Position your webcam above the play area looking down, and it will detect hand motion in 6 horizontal zones (matching the 6 IR sensors).

**Setup:**
1. Enable in [ChuniVController/ChuniVController/App.config](ChuniVController/ChuniVController/App.config):
```xml
<add key="WebcamEnabled" value="true" />
<add key="WebcamDeviceIndex" value="0" />
<add key="WebcamMotionThreshold" value="25" />
```

2. Mount your webcam above the play area, looking straight down
3. Start the controller — a popup will confirm webcam detection started
4. Wave hands over different zones to test detection

**Settings:**
- `WebcamDeviceIndex`: 0 for first webcam, 1 for second, etc.
- `WebcamMotionThreshold`: 15-50 (lower = more sensitive, higher = less noise)
- The frame is divided into 6 vertical zones; motion in each zone triggers the corresponding IR sensor

**Notes:**
- Uses AForge.NET for .NET Framework 4.7.2 compatibility
- Simple motion detection via frame differencing (no ML/hand tracking)
- Good lighting and clear background improve accuracy
- For better precision, consider adding MediaPipe hand tracking later

### Webcam IR Detection (Experimental)

The controller now supports webcam-based hand detection for "air notes" (IR sensors) instead of using mouse/touch.

**How it works:**
- Captures webcam frames at ~30 FPS
- Divides the frame into 6 horizontal zones matching the 6 IR sensors
- Detects motion via frame differencing
- Sends `IR_BLOCKED`/`IR_UNBLOCKED` messages when hands are detected

**Configuration (App.config):**
```xml
<appSettings>
  <add key="WebcamEnabled" value="true" />
  <add key="WebcamDeviceIndex" value="0" />
  <add key="WebcamMotionThreshold" value="25" />
</appSettings>
```

- `WebcamEnabled`: Set to `true` to enable webcam IR detection
- `WebcamDeviceIndex`: Webcam device index (0 for default camera)
- `WebcamMotionThreshold`: Motion sensitivity threshold (15-50, lower = more sensitive)

**Requirements:**
- .NET Framework 4.8 (upgrade from 4.7.2)
- OpenCvSharp4 4.5.5+ (installed via NuGet)
- Webcam with sufficient frame rate

**Build notes:**
- Requires .NET Framework 4.8 Developer Pack: `winget install --id Microsoft.DotNet.Framework.DeveloperPack_4 --version 4.8.1`
- After installing, rebuild the solution

**Position the webcam:**
- Mount webcam above your play area looking down
- Ensure all 6 zones are visible in frame
- Wave your hands over zones to test detection

### License

UNLICENSE

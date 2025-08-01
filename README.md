# Tractus Monitor for NDI

This utility displays a single NDI source in a window on Windows 10/11. If you've used Studio Monitor from [https://ndi.video/tools/](NDI Tools), this application will feel quite familiar.

## Studio Monitor exists - why create this?

Studio Monitor is awesome, and you should use it.

I created this application as I eventually will need a studio monitor-like application for Linux. Plus I want to see if we can
support scenarios where a GPU is not present or supported - Studio Monitor requires a GPU for pixel shaders.

## Running Monitor for NDI

Download the latest release from https://u.tractus.ca/nditoolbelt/mon_win_x64, extract it to your PC. Run `Tractus.Ndi.SdlMonitor.exe`.

Note that you can launch multiple copies of this application.

Unlike regular Studio Monitor, this does not remember any settings on exit.

## Features

- Joystick PTZ control: Plug in any HID-compliant joystick and it can be used for PTZ.
- Right-click source select: Select a source by right-clicking the viewer.
- Full-Screen or Windowed mode: Alt-Enter full-screens the application.
- Assign sources via Discovery: New to NDI 6.2, you can use Discovery to assign sources to the viewport.
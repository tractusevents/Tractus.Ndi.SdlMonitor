using NewTek;
using NewTek.NDI;
using SDL2;
using Serilog;
using System.Runtime.InteropServices;
using Tractus.Ndi;
public class NdiSdlWindow : IDisposable
{
    int gradientOffset = 0;
    SDL.SDL_Color colorA = new SDL.SDL_Color { r = 80, g = 40, b = 120, a = 255 };
    SDL.SDL_Color colorB = new SDL.SDL_Color { r = 160, g = 80, b = 200, a = 255 };
    private Finder ndiFinder;

    // tweak this to suit your joystick’s “dead” zone
    private const short DEADZONE = 2000;

    // the maximum raw magnitude we expect (positive side)
    private const float MAX_RAW = 32767f;
    // precompute the inverse of (MAX_RAW – DEADZONE) to speed up the math
    private const float INV_RANGE = 1.0f / (MAX_RAW - DEADZONE);

    private nint renderer;
    private nint window;
    private nint receiver;
    private nint ndiAdvertiser;
    private nint frameSyncPtr;
    private nint ndiSurfacePtr;
    private nint noSourceBmpTex;

    private SDL.SDL_Rect noSourceBmpPos;

    private bool disposedValue;
    private volatile bool running = true;

    private Dictionary<int, nint> joystickDict = new Dictionary<int, nint>();

    private SDL.SDL_Rect lastWidthHeight = new SDL.SDL_Rect
    {
        w = 0,
        h = 0
    };

    private float panSpeed = 0f;
    private float tiltSpeed = 0f;
    private float zoomSpeed = 0f;


    public NdiSdlWindow()
    {
        this.ndiFinder = new Finder(true, null, null);

        var windowFlags = SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN
        | SDL.SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI
        | SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE;

        this.window = SDL.SDL_CreateWindow("Tractus Monitor for NDI",
            SDL.SDL_WINDOWPOS_CENTERED, SDL.SDL_WINDOWPOS_CENTERED,
            1280, 720,
            windowFlags);

        //SDL.SDL_ShowCursor((int)SDL.SDL_DISABLE);

        this.renderer = SDL.SDL_CreateRenderer(this.window, -1,
            SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED
            | SDL.SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC
            | SDL.SDL_RendererFlags.SDL_RENDERER_TARGETTEXTURE);

        var createSettings = new NDIlib.recv_create_v3_t()
        {
            allow_video_fields = true,
            bandwidth = NDIlib.recv_bandwidth_e.recv_bandwidth_highest,
            color_format = NDIlib.recv_color_format_e.recv_color_format_fastest,
            source_to_connect_to = new NDIlib.source_t
            {
            },
            p_ndi_recv_name = UTF.StringToUtf8($"Tractus Monitor for NDI")
        };

        this.receiver = NDIWrapper.recv_create_v3(ref createSettings);
        Marshal.FreeHGlobal(createSettings.p_ndi_recv_name);

        var advertiserSettings = new NDIlib_recv_advertiser_create_t
        {

        };
        this.ndiAdvertiser = NDIWrapper.recv_advertiser_create(ref advertiserSettings);

        NDIWrapper.recv_advertiser_add_receiver(this.ndiAdvertiser, this.receiver, true, true, string.Empty);

        this.frameSyncPtr = NDIWrapper.framesync_create(this.receiver);
        this.ndiSurfacePtr = nint.Zero;

        //var noSourceBitmap = SDL.SDL_LoadBMP("noSource.bmp");
        this.noSourceBmpTex =
            //SDL.SDL_CreateTextureFromSurface(renderer, noSourceBitmap);
            LoadPngTextureBigGustave(this.renderer, "noSource.png");

        //SDL.SDL_FreeSurface(noSourceBitmap);
        SDL.SDL_QueryTexture(this.noSourceBmpTex, out _, out _, out var bmpW, out var bmpH);

        SDL.SDL_GetRendererOutputSize(this.renderer, out var rendererWidth, out var rendererHeight);

        this.noSourceBmpPos = new SDL.SDL_Rect
        {
            x = (rendererWidth - bmpW) / 2,
            y = (rendererHeight - bmpH) / 2,
            w = bmpW,
            h = bmpH
        };
    }

    public void Run()
    {
        var sourceName = "";
        while (this.running)
        {
            var connectionActive = NDIWrapper.recv_get_no_connections(this.receiver);
            var connectedToSource = NDIWrapper.GetReceiverSourceName(this.receiver, out var name, 1);

            if (connectedToSource && !string.Equals(sourceName, name))
            {
                sourceName = name;
                SDL.SDL_SetWindowTitle(this.window, $"Tractus Monitor for NDI - {sourceName}");
            }

            while (SDL.SDL_PollEvent(out var e) == 1)
            {
                if (e.type == SDL.SDL_EventType.SDL_QUIT)
                {
                    Environment.Exit(0);
                    this.running = false;
                    return;
                }
                else if (e.type == SDL.SDL_EventType.SDL_WINDOWEVENT)
                {
                }
                else if (e.type == SDL.SDL_EventType.SDL_KEYUP)
                {
                    var altHeld = (e.key.keysym.mod & SDL.SDL_Keymod.KMOD_LALT) == SDL.SDL_Keymod.KMOD_LALT
                           || (e.key.keysym.mod & SDL.SDL_Keymod.KMOD_RALT) == SDL.SDL_Keymod.KMOD_RALT;

                    if ((e.key.keysym.sym == SDL.SDL_Keycode.SDLK_KP_ENTER || e.key.keysym.sym == SDL.SDL_Keycode.SDLK_RETURN)
                        && altHeld)
                    {
                        ToggleFullscreen(this.window);
                    }
                }
                else if (e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN)
                {
                    if (e.button.button == SDL.SDL_BUTTON_RIGHT)
                    {
                        var sources = this.ndiFinder.Sources;
                        var requestedNewSourceName = string.Empty;
                        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        {
                            requestedNewSourceName = PInvokeHelpersWindows.ShowContextMenu(this.window, sources);

                            Log.Information($"Result: {requestedNewSourceName}");
                            if (requestedNewSourceName?.StartsWith("!!") == true)
                            {
                                if (requestedNewSourceName == "!!TOGGLEFS")
                                {
                                    ToggleFullscreen(this.window);
                                }
                                else if (requestedNewSourceName == "!!EXIT")
                                {
                                    Environment.Exit(0);
                                    this.running = false;
                                    return;
                                }
                                else if(requestedNewSourceName == "!!DISC")
                                {
                                    sourceName = "(None)";
                                    SDL.SDL_SetWindowTitle(this.window, $"Tractus Monitor for NDI - (None)");
                                    NDIWrapper.recv_connect(this.receiver, nint.Zero);
                                }
                            }
                            else if (!string.IsNullOrEmpty(requestedNewSourceName))
                            {
                                var newSource = new NDIlib.source_t
                                {
                                    p_ndi_name = UTF.StringToUtf8(requestedNewSourceName)
                                };

                                NDIWrapper.recv_connect(this.receiver, ref newSource);
                                sourceName = requestedNewSourceName;
                                Log.Information($"About to connect to {requestedNewSourceName}...");
                                SDL.SDL_SetWindowTitle(this.window, $"Tractus Monitor for NDI - {requestedNewSourceName}");

                                Marshal.FreeHGlobal(newSource.p_ndi_name);
                            }

                        }

                    }
                }
                else if (e.type == SDL.SDL_EventType.SDL_JOYAXISMOTION)
                {

                    var axis = e.jaxis.axis;
                    var movementAmount = -NormalizeAxis(e.jaxis.axisValue);

                    Log.Debug($"Axis {e.jaxis.axis}, Direction {e.jaxis.axisValue} : {movementAmount:0.000}, {e.jaxis.which}");

                    Log.Debug("Commanding a movement...");
                    if (axis == 0)
                    {
                        this.panSpeed = movementAmount;
                        // Left-right
                    }
                    else if (axis == 1)
                    {
                        this.tiltSpeed = movementAmount;
                        // Up-down
                    }
                    else if (axis == 2)
                    {
                    }
                    else if (axis == 3)
                    {
                        this.zoomSpeed = movementAmount;
                    }

                    NDIWrapper.recv_ptz_zoom_speed(this.receiver, this.zoomSpeed);
                    NDIWrapper.recv_ptz_pan_tilt_speed(this.receiver, this.panSpeed, this.tiltSpeed);
                }
                else if (e.type == SDL.SDL_EventType.SDL_JOYBUTTONDOWN
                || e.type == SDL.SDL_EventType.SDL_JOYBUTTONUP)
                {
                    Log.Debug($"JButton: {e.jbutton.button}");

                    if (e.jbutton.button == 0
                        && e.type == SDL.SDL_EventType.SDL_JOYBUTTONUP)
                    {
                        NDIWrapper.recv_ptz_zoom(this.receiver, 0);
                        NDIWrapper.recv_ptz_pan_tilt(this.receiver, 0, 0);
                    }
                }
                else if (e.type == SDL.SDL_EventType.SDL_JOYHATMOTION)
                {

                }
                else if (e.type == SDL.SDL_EventType.SDL_JOYDEVICEADDED)
                {
                    var jName = SDL.SDL_JoystickNameForIndex(e.jdevice.which);
                    var joystickPtr = SDL.SDL_JoystickOpen(e.jdevice.which);
                    Log.Information($"Joystick {e.jdevice.which} : {jName} added.");

                    this.joystickDict[e.jdevice.which] = joystickPtr;
                }
                else if (e.type == SDL.SDL_EventType.SDL_JOYDEVICEREMOVED)
                {
                    var jName = SDL.SDL_JoystickNameForIndex(e.jdevice.which);
                    Log.Information($"Joystick {e.jdevice.which} : {jName} removed.");

                    if (this.joystickDict.TryGetValue(e.jdevice.which, out var joystickPtr))
                    {
                        SDL.SDL_JoystickClose(joystickPtr);
                        this.joystickDict.Remove(e.jdevice.which);
                    }
                }
            }

            SDL.SDL_SetRenderDrawColor(this.renderer, 0, 0, 0, 255);
            SDL.SDL_RenderClear(this.renderer);

            var videoFrame = new NDIlib.video_frame_v2_t();

            NDIWrapper.framesync_capture_video(this.frameSyncPtr, ref videoFrame, NDIlib.frame_format_type_e.frame_format_type_progressive);

            if (videoFrame.p_data != nint.Zero && connectionActive > 0)
            {
                SDL.SDL_GetRendererOutputSize(this.renderer, out var rendererWidth, out var rendererHeight);

                if (videoFrame.xres != this.lastWidthHeight.w || videoFrame.yres != this.lastWidthHeight.h)
                {
                    SDL.SDL_DestroyTexture(this.ndiSurfacePtr);
                    this.ndiSurfacePtr = nint.Zero;
                }

                if (this.ndiSurfacePtr == nint.Zero)
                {
                    this.ndiSurfacePtr = SDL.SDL_CreateTexture(
                        this.renderer,
                        SDL.SDL_PIXELFORMAT_UYVY, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
                        videoFrame.xres,
                        videoFrame.yres);

                    this.lastWidthHeight.w = videoFrame.xres;
                    this.lastWidthHeight.h = videoFrame.yres;
                }

                SDL.SDL_LockTexture(this.ndiSurfacePtr, nint.Zero, out var pixels, out var pitch);
                unsafe
                {
                    if (pitch == videoFrame.line_stride_in_bytes)
                    {
                        Buffer.MemoryCopy(
                            videoFrame.p_data.ToPointer(), pixels.ToPointer(),
                            videoFrame.line_stride_in_bytes * videoFrame.yres,
                            videoFrame.line_stride_in_bytes * videoFrame.yres);
                    }
                    else
                    {
                        for (var y = 0; y < videoFrame.yres; y++)
                        {
                            var videoFramePtrOffset = nint.Add(videoFrame.p_data, y * videoFrame.line_stride_in_bytes);
                            var texturePtrOffset = nint.Add(pixels, y * pitch);

                            Buffer.MemoryCopy(videoFramePtrOffset.ToPointer(),
                                texturePtrOffset.ToPointer(),
                                pitch,
                                pitch);
                        }
                    }

                    SDL.SDL_UnlockTexture(this.ndiSurfacePtr);

                    var vidW = videoFrame.xres;
                    var vidH = videoFrame.yres;
                    var winAR = (float)rendererWidth / rendererHeight;
                    var vidAR = (float)vidW / vidH;

                    SDL.SDL_Rect dst;
                    if (winAR > vidAR)
                    {
                        // window is wider than the video → letterbox top/bottom
                        dst.h = rendererHeight;
                        dst.w = (int)(rendererHeight * vidAR);
                        dst.x = (rendererWidth - dst.w) / 2;
                        dst.y = 0;
                    }
                    else
                    {
                        // window is taller than the video → pillarbox left/right
                        dst.w = rendererWidth;
                        dst.h = (int)(rendererWidth / vidAR);
                        dst.x = 0;
                        dst.y = (rendererHeight - dst.h) / 2;
                    }


                    SDL.SDL_RenderCopy(this.renderer, this.ndiSurfacePtr, nint.Zero, ref dst);
                }

                NDIWrapper.framesync_free_video(this.frameSyncPtr, ref videoFrame);
            }
            else
            {
                SDL.SDL_GetRendererOutputSize(this.renderer, out var rendererWidth, out var rendererHeight);
                RenderMovingGradient(this.renderer, rendererWidth, rendererHeight, ref this.gradientOffset, this.colorA, this.colorB);


                this.noSourceBmpPos.x = (rendererWidth - this.noSourceBmpPos.w) / 2;
                this.noSourceBmpPos.y = (rendererHeight - this.noSourceBmpPos.h) / 2;

                SDL.SDL_RenderCopy(this.renderer, this.noSourceBmpTex, nint.Zero, ref this.noSourceBmpPos);
            }

            SDL.SDL_RenderPresent(this.renderer);
        }
    }

    /// <summary>
    /// Normalize a raw joystick axis value to –1..+1, with a dead-zone around 0.
    /// </summary>
    static float NormalizeAxis(short raw)
    {
        // inside dead-zone → 0
        if (raw > -DEADZONE && raw < DEADZONE)
            return 0f;

        // positive side
        if (raw > 0)
        {
            var v = (raw - DEADZONE) * INV_RANGE;
            return v > 1f ? 1f : v;   // clamp just in case
        }

        // negative side
        var vn = (raw + DEADZONE) * INV_RANGE;
        return vn < -1f ? -1f : vn;  // clamp
    }

    static void ToggleFullscreen(nint window)
    {
        var flags = (SDL.SDL_WindowFlags)SDL.SDL_GetWindowFlags(window);

        var isFullScreen = (flags & SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP) == SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP;

        SDL.SDL_SetWindowFullscreen(window, 0);
        SDL.SDL_SetWindowBordered(window, SDL.SDL_bool.SDL_TRUE);

        if (!isFullScreen)
        {
            SDL.SDL_SetWindowBordered(window, SDL.SDL_bool.SDL_FALSE);
            SDL.SDL_SetWindowFullscreen(window, (int)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP);
            SDL.SDL_SetWindowResizable(window, SDL.SDL_bool.SDL_FALSE);
        }
        else
        {
            SDL.SDL_SetWindowBordered(window, SDL.SDL_bool.SDL_TRUE);
            SDL.SDL_SetWindowResizable(window, SDL.SDL_bool.SDL_TRUE);

        }
    }

    static void RenderMovingGradient(
        nint renderer,
        int windowW, int windowH,
        ref int gradientOffset,
        SDL.SDL_Color colorA,
        SDL.SDL_Color colorB)
    {
        var phase = gradientOffset / (float)windowW;

        for (var x = 0; x < windowW; x++)
        {
            // wrap t in [0,1)
            var t = ((float)x / windowW + phase) % 1f;

            // cosine‐interpolation: w=0 at t=0, w=1 at t=0.5, w=0 at t=1
            var wgt = 0.5f * (1 - (float)Math.Cos(2 * Math.PI * t));

            var r = (byte)(colorA.r + (colorB.r - colorA.r) * wgt);
            var g = (byte)(colorA.g + (colorB.g - colorA.g) * wgt);
            var b = (byte)(colorA.b + (colorB.b - colorA.b) * wgt);

            SDL.SDL_SetRenderDrawColor(renderer, r, g, b, 255);
            SDL.SDL_RenderDrawLine(renderer, x, 0, x, windowH);
        }

        // scroll by 2px each frame (tweak for speed)
        gradientOffset = (gradientOffset + 2) % windowW;
    }

    static nint LoadPngTextureBigGustave(nint renderer, string filePath)
    {
        var png = BigGustave.Png.Open(filePath);
        var w = png.Width;
        var h = png.Height;

        var pixels = new byte[w * h * 4];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var px = png.GetPixel(x, y);
                var i = (y * w + x) * 4;
                pixels[i + 0] = px.R;
                pixels[i + 1] = px.G;
                pixels[i + 2] = px.B;
                pixels[i + 3] = px.A;
            }
        }

        var tex = SDL.SDL_CreateTexture(
            renderer,
            SDL.SDL_PIXELFORMAT_ABGR8888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC,
            w, h);

        SDL.SDL_SetTextureBlendMode(tex, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
        var texRect = new SDL.SDL_Rect()
        {
            w = w,
            h = h
        };

        var gcHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        var pixelPtr = gcHandle.AddrOfPinnedObject();

        SDL.SDL_UpdateTexture(tex, ref texRect, pixelPtr, w * 4);

        gcHandle.Free();

        return tex;
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposedValue)
        {
            if (disposing)
            {
                this.running = false;

                NDIWrapper.framesync_destroy(this.frameSyncPtr);
                NDIWrapper.recv_advertiser_del_receiver(this.ndiAdvertiser, this.receiver);
                NDIWrapper.recv_advertiser_destroy(this.ndiAdvertiser);
                NDIWrapper.recv_destroy(this.receiver);

                SDL.SDL_DestroyTexture(this.noSourceBmpTex);
                SDL.SDL_DestroyRenderer(this.renderer);
                SDL.SDL_DestroyWindow(this.window);
                SDL.SDL_Quit();

                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            this.disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~NdiSdlWindow()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

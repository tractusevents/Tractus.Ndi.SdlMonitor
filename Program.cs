using NewTek;
using NewTek.NDI;
using SDL2;
using Serilog;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Tractus.Ndi;
internal class Program
{
    private static void Main(string[] args)
    {
        var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        Directory.SetCurrentDirectory(exeDirectory);

        NDIWrapper.Initialize(false);
        AppManagement.Initialize(args);

        SDL.SDL_SetHint(SDL.SDL_HINT_WINDOWS_DISABLE_THREAD_NAMING, "1");
        // SDL.SDL_SetHint(SDL.SDL_HINT_VIDEO_X11_XRANDR, "0");
        SDL.SDL_SetHint(SDL.SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        SDL.SDL_SetHint("SDL_WINDOWS_DPI_AWARENESS", "permonitorv2");
        if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_AUDIO | SDL.SDL_INIT_JOYSTICK) < 0)
        {
            Log.Error("Could not init SDL. Exit.");
            return;
        }

        SDL.SDL_JoystickEventState(SDL.SDL_ENABLE);

        SDL.SDL_SetHint(SDL.SDL_HINT_RENDER_SCALE_QUALITY, "1");

        Log.Debug("SDL2 Video driver list:");
        var videoDriverCount = SDL.SDL_GetNumVideoDrivers();
        for (var i = 0; i < videoDriverCount; i++)
        {
            var videoDriverName = SDL.SDL_GetVideoDriver(i);
            Log.Debug($"\t{i}: {videoDriverName}");
        }

        Log.Information($"Video Driver in use: {SDL.SDL_GetCurrentVideoDriver()}");

        Log.Information($"Tractus Monitor for NDI - v{AppManagement.Version}");
        Log.Information($"NDI(R) is a registered trademark of Vizrt NDI AB. https://ndi.video/");

        var window = new NdiSdlWindow();
        window.Run();
        window.Dispose();
    }
}

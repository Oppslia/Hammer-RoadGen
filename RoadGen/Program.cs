using VTFLib;

namespace RoadGen;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Initialize the native VTFLib used to decode VTF/VMT textures. Not fatal:
        // if the native DLL is missing, the texture cache falls back to a checkerboard.
        try
        {
            VTFAPI.Initialize();
        }
        catch (Exception)
        {
        }

        Application.Run(new MainWindow());
    }
}

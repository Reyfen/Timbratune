using UIKit;

namespace Reyfen.Timbratune.iOS;

public static class Program
{
    private static void Main(string[] args)
    {
        CrashLog.Install();
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}

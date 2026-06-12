using Foundation;
using UIKit;

namespace LamaERP.Mobile.WebApp;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // Hard-lock the app to portrait (belt-and-suspenders with Info.plist's UISupportedInterfaceOrientations).
    // The base delegate doesn't expose this as virtual, so register the selector directly — the OS calls
    // it to decide allowed rotations, disabling landscape regardless of how the bundled plist was processed.
    [Export("application:supportedInterfaceOrientationsForWindow:")]
    public UIInterfaceOrientationMask GetSupportedInterfaceOrientations(UIApplication application, UIWindow forWindow)
        => UIInterfaceOrientationMask.Portrait;
}

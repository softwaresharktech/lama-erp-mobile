using Foundation;
using UIKit;

namespace LamaERP.Mobile.WebApp;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // Hard-lock the app to portrait (belt-and-suspenders with Info.plist's UISupportedInterfaceOrientations).
    // The OS calls this to decide allowed rotations, so landscape is disabled regardless of how the
    // bundled plist was processed.
    public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations(UIApplication application, UIWindow forWindow)
        => UIInterfaceOrientationMask.Portrait;
}

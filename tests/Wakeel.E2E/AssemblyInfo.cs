using Xunit;

// Every test in this assembly ultimately drives a real Wakeel.Desktop.exe process talking to a real
// WebView2 profile folder (see Wakeel.Desktop.Services.WakeelPaths.WebView2UserDataFolder, a fixed
// path under %LocalAppData%\Wakeel — not parameterizable from here since that file sits outside this
// package's allowed edit paths). WebView2 locks its profile directory, so two Wakeel.Desktop.exe
// instances launched at the same time would collide. Keeping the whole assembly sequential (instead
// of xunit's default of parallel test collections) avoids that regardless of how many test classes
// this project grows to.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

using System.Windows;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.UI.Interop;

namespace ClearOfficeViewer.Tests;

public class TransparencyEngineTests
{
    [Fact]
    public void TransparencyEngine_ConfiguresAllMaterialsAndClampsBounds()
    {
        Exception? threadEx = null;
        var t = new Thread(() =>
        {
            try
            {
                var win = new Window { Width = 320, Height = 240, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                using var engine = new TransparencyEngine(win);

                win.SourceInitialized += (_, _) => engine.Attach();
                win.Show();

                engine.Configure(BackdropMaterial.DeepAcrylic, AppThemePreference.Dark, 68, 36, backgroundAdaptation: true);
                Assert.Equal(BackdropMaterial.DeepAcrylic, engine.Material);
                Assert.Equal(68, engine.BlurIntensityPercent);
                Assert.Equal(36, engine.TintStrengthPercent);
                Assert.True(engine.BackgroundAdaptation);

                engine.ChangeBlurIntensity(150);
                Assert.Equal(100, engine.BlurIntensityPercent);

                engine.ChangeTintOpacity(-10);
                Assert.Equal(0, engine.TintStrengthPercent);

                engine.SetBackgroundAdaptation(false);
                Assert.False(engine.BackgroundAdaptation);

                engine.Configure(BackdropMaterial.CustomGlass, AppThemePreference.Dark, 55, 25, backgroundAdaptation: true);
                engine.Configure(BackdropMaterial.Acrylic, AppThemePreference.System, 50, 30, backgroundAdaptation: true);
                engine.Configure(BackdropMaterial.Mica, AppThemePreference.Light, 50, 30, backgroundAdaptation: true);
                engine.EnableAcrylic();
                Assert.Equal(BackdropMaterial.DeepAcrylic, engine.Material);

                _ = engine.DetectWindowsTheme();
                engine.HandleBackdropChanges();
                win.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        Assert.Null(threadEx);
    }
}

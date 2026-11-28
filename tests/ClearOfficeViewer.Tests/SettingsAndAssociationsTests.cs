using System.IO;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.Core.Services;

namespace ClearOfficeViewer.Tests;

public class SettingsAndAssociationsTests
{
    [Fact]
    public void SettingsService_PersistsRecentsFavoritesAndBackdrop()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cov_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string settingsPath = Path.Combine(tempDir, "settings.json");
        string sampleFile = Path.Combine(tempDir, "notes.docx");
        File.WriteAllText(sampleFile, "dummy");

        try
        {
            var svc = new SettingsService(settingsPath);
            svc.Current.Backdrop = BackdropMaterial.CustomGlass;
            svc.Current.BlurIntensityPercent = 80;
            svc.Current.TintStrengthPercent = 28;
            svc.Current.BackgroundAdaptation = true;
            svc.RecordFileOpened(sampleFile);
            bool fav = svc.ToggleFavorite(sampleFile);

            Assert.True(fav);
            Assert.Single(svc.Current.RecentFiles);
            Assert.True(svc.Current.RecentFiles[0].IsFavorite);

            var reloaded = new SettingsService(settingsPath);
            Assert.Equal(BackdropMaterial.CustomGlass, reloaded.Current.Backdrop);
            Assert.Equal(80, reloaded.Current.BlurIntensityPercent);
            Assert.Equal(28, reloaded.Current.TintStrengthPercent);
            Assert.True(reloaded.Current.BackgroundAdaptation);
            Assert.Single(reloaded.GetFavorites());
            Assert.True(reloaded.IsFavorite(sampleFile));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FileAssociationService_FormatsCommandAndListsAllSevenExtensions()
    {
        string cmd = FileAssociationService.GetOpenCommand(@"C:\Program Files\ClearOffice Viewer\ClearOfficeViewer.exe");
        Assert.Equal("\"C:\\Program Files\\ClearOffice Viewer\\ClearOfficeViewer.exe\" \"%1\"", cmd);
        Assert.Equal(7, FileAssociationService.SupportedAssociations.Count);
    }

    [Fact]
    public void SettingsService_FallsBackSafelyOnLegacyOrUnknownBackdropNames()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cov_legacy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            File.WriteAllText(settingsPath, """
            {
              "Backdrop": "UnknownLegacyMaterial",
              "FavoritePaths": [ "C:\\temp\\report.pdf" ],
              "RecentFiles": [
                {
                  "FilePath": "C:\\temp\\report.pdf",
                  "FileName": "report.pdf",
                  "FileKind": "Pdf",
                  "IsFavorite": true
                }
              ]
            }
            """);

            var svc = new SettingsService(settingsPath);
            Assert.Equal(BackdropMaterial.DeepAcrylic, svc.Current.Backdrop);
            Assert.Single(svc.Current.RecentFiles);
            Assert.True(svc.Current.RecentFiles[0].IsFavorite);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

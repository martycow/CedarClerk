using CedarClerk.Server;

namespace CedarClerk.Tests;

public sealed class DesktopInstallersTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cedar-installers-" + Guid.NewGuid().ToString("N"));

    public DesktopInstallersTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Publish(string manifest, string body, params string[] files)
    {
        foreach (var file in files) File.WriteAllText(Path.Combine(_dir, file), "x");
        File.WriteAllText(Path.Combine(_dir, manifest), body);
    }

    [Fact]
    public void Nothing_published_offers_no_platform()
    {
        Assert.Empty(DesktopInstallers.Published(_dir));
        Assert.Null(DesktopInstallers.Find(_dir, "windows"));
    }

    [Fact]
    public void Windows_manifest_names_its_installer_and_version()
    {
        Publish("latest.yml",
            "version: 0.25.1\nfiles:\n  - url: Cedar-Clerk-Setup-0.25.1.exe\n    sha512: abc\npath: Cedar-Clerk-Setup-0.25.1.exe\n",
            "Cedar-Clerk-Setup-0.25.1.exe");

        var installer = Assert.Single(DesktopInstallers.Published(_dir));
        Assert.Equal(new DesktopInstaller("windows", "0.25.1", "Cedar-Clerk-Setup-0.25.1.exe"), installer);
        Assert.Equal(installer, DesktopInstallers.Find(_dir, "Windows"));
    }

    [Fact]
    public void Mac_prefers_the_disk_image_over_the_updater_zip()
    {
        Publish("latest-mac.yml",
            "version: 0.26.0\nfiles:\n  - url: CedarClerk-0.26.0-mac.zip\n  - url: 'CedarClerk-0.26.0.dmg'\npath: CedarClerk-0.26.0-mac.zip\n",
            "CedarClerk-0.26.0-mac.zip", "CedarClerk-0.26.0.dmg");

        Assert.Equal("CedarClerk-0.26.0.dmg", DesktopInstallers.Find(_dir, "mac")?.File);
    }

    [Fact]
    public void Each_platform_reads_its_own_manifest()
    {
        Publish("latest.yml", "version: 1.0.0\npath: a.exe\n", "a.exe");
        Publish("latest-linux.yml", "version: 1.1.0\npath: b.AppImage\n", "b.AppImage");

        Assert.Equal(["windows", "linux"], DesktopInstallers.Published(_dir).Select(i => i.Platform));
        Assert.Equal("1.1.0", DesktopInstallers.Find(_dir, "linux")?.Version);
        Assert.Null(DesktopInstallers.Find(_dir, "mac"));
    }

    [Fact]
    public void A_manifest_whose_file_is_absent_or_outside_the_folder_is_not_offered()
    {
        Publish("latest.yml", "version: 1.0.0\npath: missing.exe\n");
        Publish("latest-linux.yml", "version: 1.0.0\npath: ../escape.AppImage\n");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_dir)!, "escape.AppImage"), "x");
        try
        {
            Assert.Empty(DesktopInstallers.Published(_dir));
        }
        finally
        {
            File.Delete(Path.Combine(Path.GetDirectoryName(_dir)!, "escape.AppImage"));
        }
    }
}

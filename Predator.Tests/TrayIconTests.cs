using Predator;
using Xunit;

public class TrayIconTests
{
    [Theory]
    [InlineData(45, 16)]
    [InlineData(88, 24)]
    [InlineData(95, 32)]
    [InlineData(0, 16)]
    public void Renders_at_requested_size(int temp, int size)
    {
        using var icon = TrayIcon.Render(temp, 90, size);
        Assert.Equal(size, icon.Width);

        // Keep a copy for eyeballing the look.
        using var bmp = icon.ToBitmap();
        bmp.Save(Path.Combine(Path.GetTempPath(), $"predator_tray_{temp}_{size}.png"));
    }
}

using Lumos.Core.Security;
using Lumos.Core.Tests.Fakes;
using Xunit;

namespace Lumos.Core.Tests;

public class ClipboardServiceTests
{
    [Fact]
    public void SetTextWithAutoClear_writes_to_clipboard()
    {
        var fake = new FakeClipboard();
        using var svc = new ClipboardService(fake, TimeSpan.FromSeconds(30));

        svc.SetTextWithAutoClear("secret");
        Assert.Equal("secret", fake.GetText());
    }

    [Fact]
    public async Task Auto_clear_fires_after_timeout()
    {
        var fake = new FakeClipboard();
        using var svc = new ClipboardService(fake, TimeSpan.FromMilliseconds(50));

        svc.SetTextWithAutoClear("secret");
        Assert.Equal("secret", fake.GetText());

        await Task.Delay(200);

        Assert.Equal("", fake.GetText());
        Assert.Equal(1, fake.ClearCount);
    }

    [Fact]
    public async Task Auto_clear_does_not_wipe_user_replaced_content()
    {
        // User copies a secret, then copies an email paragraph before the
        // timer fires. We must NOT clear their email.
        var fake = new FakeClipboard();
        using var svc = new ClipboardService(fake, TimeSpan.FromMilliseconds(80));

        svc.SetTextWithAutoClear("secret");
        await Task.Delay(20);

        // Simulate the user copying something else.
        fake.SetText("an important email I was writing");

        await Task.Delay(200);

        Assert.Equal("an important email I was writing", fake.GetText());
        Assert.Equal(0, fake.ClearCount);
    }

    [Fact]
    public async Task Second_copy_cancels_first_timer()
    {
        var fake = new FakeClipboard();
        using var svc = new ClipboardService(fake, TimeSpan.FromMilliseconds(80));

        svc.SetTextWithAutoClear("first");
        await Task.Delay(20);
        svc.SetTextWithAutoClear("second");
        await Task.Delay(50);  // first timer would have fired by now if not cancelled

        // The second value is still there because its own timer hasn't fired.
        Assert.Equal("second", fake.GetText());

        // Wait out the second timer.
        await Task.Delay(150);
        Assert.Equal("", fake.GetText());
        Assert.Equal(1, fake.ClearCount);
    }

    [Fact]
    public void ClearNowIfOurs_clears_when_value_still_present()
    {
        var fake = new FakeClipboard();
        using var svc = new ClipboardService(fake, TimeSpan.FromSeconds(30));

        svc.SetTextWithAutoClear("secret");
        svc.ClearNowIfOurs();

        Assert.Equal("", fake.GetText());
        Assert.Equal(1, fake.ClearCount);
    }

    [Fact]
    public void ClearNowIfOurs_does_not_clear_after_user_overwrite()
    {
        var fake = new FakeClipboard();
        using var svc = new ClipboardService(fake, TimeSpan.FromSeconds(30));

        svc.SetTextWithAutoClear("secret");
        fake.SetText("user wrote this");
        svc.ClearNowIfOurs();

        Assert.Equal("user wrote this", fake.GetText());
        Assert.Equal(0, fake.ClearCount);
    }

    [Fact]
    public void Dispose_cancels_pending_clear()
    {
        // After Dispose, no clear should happen even if the timer would have.
        var fake = new FakeClipboard();
        var svc = new ClipboardService(fake, TimeSpan.FromMilliseconds(50));
        svc.SetTextWithAutoClear("secret");
        svc.Dispose();

        Thread.Sleep(200);
        Assert.Equal(0, fake.ClearCount);
    }

    [Fact]
    public void Constructor_rejects_invalid_timeout()
    {
        var fake = new FakeClipboard();
        Assert.Throws<ArgumentException>(() => new ClipboardService(fake, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new ClipboardService(fake, TimeSpan.FromSeconds(-1)));
    }
}

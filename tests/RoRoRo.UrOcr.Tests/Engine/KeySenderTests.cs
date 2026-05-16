using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

public class KeySenderTests
{
    [Fact]
    public void Press_with_single_key_does_not_throw()
    {
        var s = new KeySender { HoldMs = 1 };
        s.Press(new KeyCombo("F12", Array.Empty<string>()));
    }

    [Fact]
    public void Press_with_modifiers_does_not_throw()
    {
        var s = new KeySender { HoldMs = 1 };
        s.Press(new KeyCombo("A", new[] { "Ctrl", "Shift" }));
    }

    [Fact]
    public void Press_with_unknown_key_throws()
    {
        var s = new KeySender { HoldMs = 1 };
        Assert.Throws<ArgumentException>(() => s.Press(new KeyCombo("NotAKey", Array.Empty<string>())));
    }

    [Fact]
    public void Press_with_unknown_modifier_throws()
    {
        var s = new KeySender { HoldMs = 1 };
        Assert.Throws<ArgumentException>(() => s.Press(new KeyCombo("A", new[] { "Hyper" })));
    }
}

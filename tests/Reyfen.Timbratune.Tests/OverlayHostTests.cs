using Reyfen.Timbratune.Shared.Navigation;

namespace Reyfen.Timbratune.Tests;

public sealed class OverlayHostTests
{
    [Fact]
    public void TheLastOpenedIsInFrontAndClosesFirst()
    {
        var host = new OverlayHost();
        object a = "metric", b = "take dialog";
        host.Push(a);
        host.Push(b);
        Assert.Same(b, host.Top);
        Assert.True(host.CloseTop());
        Assert.Same(a, host.Top);
        Assert.True(host.CloseTop());
        Assert.Null(host.Top);
        Assert.False(host.CloseTop());
    }

    [Fact]
    public void ReopeningMovesAnOverlayToTheFrontOnce()
    {
        var host = new OverlayHost();
        object a = "a", b = "b";
        host.Push(a);
        host.Push(b);
        host.Push(a);
        Assert.Equal([b, a], host.Overlays);
    }

    [Fact]
    public void ClosingOneBehindKeepsTheFront()
    {
        var host = new OverlayHost();
        object a = "a", b = "b";
        host.Push(a);
        host.Push(b);
        Assert.True(host.Close(a));
        Assert.False(host.Close(a));
        Assert.Equal([b], host.Overlays);
    }

    [Fact]
    public void EveryChangeIsAnnouncedAndNoOpsAreNot()
    {
        var host = new OverlayHost();
        var changes = 0;
        var tops = new List<string?>();
        host.Changed += (_, _) => changes++;
        host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayHost.Top)) tops.Add(host.Top as string);
        };
        host.Push("a");
        host.Push("a");
        host.Close("missing");
        host.CloseTop();
        Assert.Equal(2, changes);
        Assert.Equal(["a", null], tops);
    }

    [Fact]
    public void RoutersDisposeWhatTheyCloseAndKeepOneOfAKind()
    {
        var host = new OverlayHost();
        var router = new TestRouter(host);
        var first = new Dialog();
        var second = new Dialog();
        router.Show(first);
        router.Show(second);
        Assert.True(first.Disposed);
        Assert.Equal([second], host.Overlays);
        router.Hide(second);
        Assert.True(second.Disposed);
        Assert.Null(host.Top);
    }

    private sealed class Dialog : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class TestRouter(IOverlayHost host) : RouterBase(host)
    {
        public void Show(Dialog d)
        {
            CloseAll<Dialog>();
            Open(d);
        }

        public void Hide(Dialog d) => Close(d);
    }
}

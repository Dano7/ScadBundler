using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScadBundler.Web.Components;
using ScadBundler.Web.State;
using Xunit;

namespace ScadBundler.Web.Tests;

/// <summary>
/// bUnit tests for the Slice W5 §C3 <see cref="LargeProjectNotice"/>: it sets expectations for a large project
/// (bundling takes seconds, set options then Bundle) with a link to the instant command-line tool, and can be
/// dismissed. App gates it on <c>IsLargeProject</c>; the component itself just shows the note until dismissed.
/// </summary>
public sealed class LargeProjectNoticeTests : TestContext
{
    [Fact]
    public void ShowsExpectationNote_WithCliLink()
    {
        Services.AddSingleton(new WorkspaceController());

        IRenderedComponent<LargeProjectNotice> cut = RenderComponent<LargeProjectNotice>();

        Assert.Contains("Large project", cut.Markup);
        Assert.Contains("Bundle", cut.Markup);                       // explains the deferred manual bundle
        Assert.Contains("scadbundler", cut.Markup);                  // names the CLI
        IElement link = cut.Find(".large-notice-body a");
        Assert.StartsWith("https://", link.GetAttribute("href"));    // a real, navigable link
    }

    [Fact]
    public void Dismiss_RemovesTheNotice()
    {
        Services.AddSingleton(new WorkspaceController());
        IRenderedComponent<LargeProjectNotice> cut = RenderComponent<LargeProjectNotice>();
        Assert.NotEmpty(cut.FindAll(".large-notice"));

        cut.Find(".large-notice-dismiss").Click();

        Assert.Empty(cut.FindAll(".large-notice"));                  // one-time dismiss hides it
    }
}

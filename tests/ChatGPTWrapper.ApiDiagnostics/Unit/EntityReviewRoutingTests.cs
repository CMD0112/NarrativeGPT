using System.Reflection;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection("WpfUi")]
public sealed class EntityReviewRoutingTests
{
    [Fact]
    public void FocusEntityReviewQueue_selects_index_zero_when_queue_has_items()
    {
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureChromeResources();

            var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-test");
            bundle.Entities.ReviewQueue.Add(new EntityReviewItem
            {
                EntityType = "character",
                ProposedChange = """{"name":"Ada"}""",
            });
            AdventureStore.Save(bundle);

            var view = new AdventurePlayView();
            view.LoadAdventure(bundle.Metadata.Id);
            typeof(AdventurePlayView).GetMethod(
                "FocusEntityReviewQueue",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, [true]);

            var list = (System.Windows.Controls.ListBox)view.GetType().GetField(
                "ReviewQueueList",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            Assert.Equal(0, list.SelectedIndex);
        });
    }

    [Fact]
    public void Pending_entity_review_queue_persists_on_adventure_reload()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: null);
        bundle.Entities.ReviewQueue.Add(new EntityReviewItem
        {
            EntityType = "character",
            ProposedChange = """{"name":"Ada"}""",
        });
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.Entities.ReviewQueue);
        Assert.Equal("character", reloaded.Entities.ReviewQueue[0].EntityType);
    }
}

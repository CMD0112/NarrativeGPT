using System.Reflection;
using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class EntityReviewRoutingTests
{
    [Fact]
    public void FocusEntityReviewQueue_selects_index_zero_when_queue_has_items()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new System.Windows.Application();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/ChatGPT Wrapper;component/Themes/WrapperChrome.xaml", UriKind.Relative),
                    });
                }

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
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));
        if (failure is not null)
            throw failure;
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

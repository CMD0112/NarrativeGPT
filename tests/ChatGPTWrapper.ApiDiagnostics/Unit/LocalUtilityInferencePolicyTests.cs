using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
[Collection(FileLockAwareCollectionNames.Name)]
public sealed class LocalUtilityInferencePolicyTests : IClassFixture<FileLockAwareFixture>, IDisposable
{
  private readonly string _tempRoot;

  public LocalUtilityInferencePolicyTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "ChatGPTWrapper-LocalInferencePolicy-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
    AppDirectories.TestRootOverride = _tempRoot;
    AppDirectories.EnsureCreated();
  }

  public void Dispose()
  {
    AppDirectories.TestRootOverride = null;
    try
    {
      if (Directory.Exists(_tempRoot))
        Directory.Delete(_tempRoot, recursive: true);
    }
    catch
    {
      /* ignore */
    }
  }

  [Fact]
  public void ShouldRunLocalLeg_returns_false_when_worker_attachments_staged()
  {
    var bundle = new AdventureBundle
    {
      Metadata = new AdventureMetadata
      {
        Settings = new AdventureSettings
        {
          LocalUtilityInference = new LocalUtilityInferenceSettings { Enabled = true },
        },
      },
    };

    var context = new GenerationJobContext
    {
      JobAttachments = AttachmentContext.FromMeta(
      [
        new ComposerAttachmentMeta { Name = "entities.json", MimeType = "application/json", SizeBytes = 12 },
      ]),
    };

    Assert.False(LocalUtilityInferencePolicy.ShouldRunLocalLeg(
      bundle,
      GenerationJobId.ExtractEntities,
      context));

    Assert.False(LocalUtilityInferencePolicy.ShouldUseLocalExclusive(
      bundle,
      GenerationJobId.ExtractEntities,
      context));
  }

  [Fact]
  public void ShouldRunLocalLeg_returns_true_when_local_enabled_and_no_attachments()
  {
    var bundle = new AdventureBundle
    {
      Metadata = new AdventureMetadata
      {
        Settings = new AdventureSettings
        {
          LocalUtilityInference = new LocalUtilityInferenceSettings { Enabled = true },
        },
      },
    };

    Assert.True(LocalUtilityInferencePolicy.ShouldRunLocalLeg(
      bundle,
      GenerationJobId.ExtractEntities));
    Assert.True(LocalUtilityInferencePolicy.ShouldUseLocalExclusive(
      bundle,
      GenerationJobId.ExtractEntities));
  }

  [Fact]
  public void ShouldRunLocalLeg_reads_disabled_setting_from_disk_when_bundle_is_stale()
  {
    var bundle = AdventureStore.CreateNew("Local inference disk sync");
    AdventureStore.Save(bundle);

    bundle.Metadata.Settings.LocalUtilityInference.Enabled = true;
    TransportSettingsStore.Commit(bundle, caller: "test-enabled");

    bundle.Metadata.Settings.LocalUtilityInference.Enabled = false;
    TransportSettingsStore.Commit(bundle, caller: "test-disabled");

    var inMemory = AdventureStore.Load(bundle.Metadata.Id)!;
    inMemory.Metadata.Settings.LocalUtilityInference.Enabled = true;

    Assert.False(LocalUtilityInferencePolicy.ShouldRunLocalLeg(
      inMemory,
      GenerationJobId.ExtractEntities));

    var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
    Assert.False(reloaded.Metadata.Settings.LocalUtilityInference.Enabled);
  }
}

using System.IO;
using Piko.Models;
using Piko.Services;

namespace Piko.Tests;

public class PikoCoreTests : IDisposable
{
    private readonly string _tempTestDir;

    public PikoCoreTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "Piko_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempTestDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void BridgeMessage_Validation_AcceptsValidEventsAndRejectsInvalid()
    {
        const string validToken = "piko-dev-bridge-token-2026";

        var startMsg = new BridgeMessage { Type = "ai-start", Token = validToken, Source = "ChatGPT" };
        Assert.True(startMsg.IsValid(validToken));

        var completeMsg = new BridgeMessage { Type = "ai-complete", Token = validToken, Source = "ChatGPT" };
        Assert.True(completeMsg.IsValid(validToken));

        var cancelMsg = new BridgeMessage { Type = "ai-cancel", Token = validToken, Source = "ChatGPT" };
        Assert.True(cancelMsg.IsValid(validToken));

        var genStartMsg = new BridgeMessage { Type = "generation_started", Token = validToken, Source = "ChatGPT" };
        Assert.True(genStartMsg.IsValid(validToken));

        var genCompleteMsg = new BridgeMessage { Type = "generation_completed", Token = validToken, Source = "ChatGPT" };
        Assert.True(genCompleteMsg.IsValid(validToken));

        var genCancelMsg = new BridgeMessage { Type = "generation_cancelled", Token = validToken, Source = "ChatGPT" };
        Assert.True(genCancelMsg.IsValid(validToken));

        var mediaPlayingMsg = new BridgeMessage { Type = "media_playing", Token = validToken, Source = "YouTube", Title = "Song Title" };
        Assert.True(mediaPlayingMsg.IsValid(validToken));

        var mediaPausedMsg = new BridgeMessage { Type = "media_paused", Token = validToken, Source = "YouTube" };
        Assert.True(mediaPausedMsg.IsValid(validToken));

        var mediaStoppedMsg = new BridgeMessage { Type = "media_stopped", Token = validToken, Source = "YouTube" };
        Assert.True(mediaStoppedMsg.IsValid(validToken));

        var mediaPlayingAlt = new BridgeMessage { Type = "media-playing", Token = validToken, Source = "YouTube", Title = "Song Title" };
        Assert.True(mediaPlayingAlt.IsValid(validToken));

        var mediaPausedAlt = new BridgeMessage { Type = "media-paused", Token = validToken, Source = "YouTube" };
        Assert.True(mediaPausedAlt.IsValid(validToken));

        var mediaStoppedAlt = new BridgeMessage { Type = "media-stopped", Token = validToken, Source = "YouTube" };
        Assert.True(mediaStoppedAlt.IsValid(validToken));

        var wrongTokenMsg = new BridgeMessage { Type = "ai-complete", Token = "wrong-token" };
        Assert.False(wrongTokenMsg.IsValid(validToken));

        var invalidTypeMsg = new BridgeMessage { Type = "arbitrary-command", Token = validToken };
        Assert.False(invalidTypeMsg.IsValid(validToken));
    }

    [Fact]
    public void PikoEyeController_Geometries_AreDefinedAndDistinct()
    {
        // Verify eye geometries can be parsed and are distinct
        Assert.NotNull(PikoEyeController.IdleEyeGeom);
        Assert.NotNull(PikoEyeController.OpenEyeGeom);
        Assert.NotNull(PikoEyeController.SuccessEyeGeom);
        Assert.NotNull(PikoEyeController.SlitEyeGeom);
        Assert.NotNull(PikoEyeController.SleepyLeftGeom);
        Assert.NotNull(PikoEyeController.SleepyRightGeom);

        // Verify vertical pill line for idle and curved lines for sleepy/success
        Assert.False(PikoEyeController.IdleEyeGeom.IsEmpty());
        Assert.False(PikoEyeController.SuccessEyeGeom.IsEmpty());
        Assert.False(PikoEyeController.SleepyLeftGeom.IsEmpty());
        Assert.False(PikoEyeController.SleepyRightGeom.IsEmpty());
    }

    [Fact]
    public void PikoStateManager_EmotionsAndPriorityTransitions_FollowExpectedFlow()
    {
        var manager = new PikoStateManager();
        Assert.Equal(PikoEmotion.Idle, manager.CurrentEmotion);

        // 1. Idle -> Sleepy
        manager.SetEmotion(PikoEmotion.Sleepy);
        Assert.Equal(PikoEmotion.Sleepy, manager.CurrentEmotion);
        Assert.Equal("SNOOZING", PikoEmotion.Sleepy.GetEmotionBadge());

        // 2. Sleepy -> Listening (Music starts)
        manager.SetEmotion(PikoEmotion.Listening);
        Assert.Equal(PikoEmotion.Listening, manager.CurrentEmotion);
        Assert.Equal("LISTENING", PikoEmotion.Listening.GetEmotionBadge());

        // 3. Listening -> Working (ChatGPT starts generating)
        manager.SetEmotion(PikoEmotion.Working);
        Assert.Equal(PikoEmotion.Working, manager.CurrentEmotion);
        Assert.Equal("WORKING", PikoEmotion.Working.GetEmotionBadge());

        // 4. Working -> Success (ChatGPT finishes)
        manager.TriggerNotification("ChatGPT selesai! ✨", "Jawabanmu sudah siap ✨", "ChatGPT");
        Assert.Equal(PikoEmotion.Success, manager.CurrentEmotion);
        Assert.True(manager.HasActiveNotification);
        Assert.Equal("DONE ✨", PikoEmotion.Success.GetEmotionBadge());
    }

    [Fact]
    public async Task StorageScanner_DetectsCategoriesAndDuplicates()
    {
        // 1. Create dummy files
        var docFile = Path.Combine(_tempTestDir, "laporan_tugas.pdf");
        var imgFile = Path.Combine(_tempTestDir, "screenshot_2026.png");
        var codeFile = Path.Combine(_tempTestDir, "script.cs");
        var zipFile = Path.Combine(_tempTestDir, "backup.zip");

        // Duplicate pair with identical bytes
        var dupFile1 = Path.Combine(_tempTestDir, "photo1.jpg");
        var dupFile2 = Path.Combine(_tempTestDir, "photo2_copy.jpg");

        var dummyContent = new byte[2048];
        Array.Fill(dummyContent, (byte)42);

        await File.WriteAllBytesAsync(docFile, new byte[500]);
        await File.WriteAllBytesAsync(imgFile, new byte[600]);
        await File.WriteAllBytesAsync(codeFile, new byte[700]);
        await File.WriteAllBytesAsync(zipFile, new byte[800]);
        await File.WriteAllBytesAsync(dupFile1, dummyContent);
        await File.WriteAllBytesAsync(dupFile2, dummyContent);

        // 2. Scan
        var scanner = new StorageScannerService();
        var result = await scanner.ScanFolderAsync(_tempTestDir, false, null, CancellationToken.None);

        Assert.Equal(6, result.TotalFiles);
        Assert.NotEmpty(result.Categories);

        var docCategory = result.Categories.First(c => c.Category == StorageCategoryType.Documents);
        Assert.Equal(1, docCategory.FileCount);

        var imgCategory = result.Categories.First(c => c.Category == StorageCategoryType.Images);
        Assert.Equal(3, imgCategory.FileCount); // imgFile, dupFile1, dupFile2

        // 3. Verify duplicate detection
        Assert.NotEmpty(result.DuplicateGroups);
        var dupGroup = result.DuplicateGroups.First();
        Assert.Equal(2, dupGroup.Files.Count);
    }

    [Fact]
    public async Task StorageOrganizer_ExecutesPlanSafely_AndUndoesReversibly()
    {
        // 1. Create files in source folder
        var sourceDir = Path.Combine(_tempTestDir, "MyDownloads");
        Directory.CreateDirectory(sourceDir);

        var pdfFile = Path.Combine(sourceDir, "assignment.pdf");
        var imgFile = Path.Combine(sourceDir, "screenshot_design.png");
        await File.WriteAllTextAsync(pdfFile, "Dummy PDF Content");
        await File.WriteAllTextAsync(imgFile, "Dummy PNG Content");

        // 2. Scan
        var scanner = new StorageScannerService();
        var scanResult = await scanner.ScanFolderAsync(sourceDir, false, null, CancellationToken.None);

        // 3. Generate plan
        var organizer = new StorageOrganizerService();
        var targetBase = Path.Combine(sourceDir, "Organized_Piko");
        var plan = await organizer.GeneratePlanAsync(scanResult, targetBase, CancellationToken.None);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, p => Assert.Equal(PlanItemStatus.Pending, p.Status));

        // 4. Execute move
        var journal = await organizer.ExecutePlanAsync(plan, null, CancellationToken.None);
        Assert.Equal(2, journal.Entries.Count);
        Assert.False(File.Exists(pdfFile), "Original PDF should have been moved");
        Assert.False(File.Exists(imgFile), "Original PNG should have been moved");

        // Check destination files exist
        foreach (var entry in journal.Entries)
        {
            Assert.True(File.Exists(entry.DestinationPath), $"File should exist at {entry.DestinationPath}");
        }

        // 5. Test Undo
        var (reverted, failed) = await organizer.UndoJournalAsync(journal, CancellationToken.None);
        Assert.Equal(2, reverted);
        Assert.Equal(0, failed);

        Assert.True(File.Exists(pdfFile), "PDF should be restored to original path");
        Assert.True(File.Exists(imgFile), "PNG should be restored to original path");
    }

    [Fact]
    public async Task StorageOrganizer_CollisionSafety_DoesNotOverwrite()
    {
        var sourceDir = Path.Combine(_tempTestDir, "Source");
        var targetDir = Path.Combine(_tempTestDir, "Target");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);

        var srcFile = Path.Combine(sourceDir, "document.pdf");
        var existingTarget = Path.Combine(targetDir, "document.pdf");

        await File.WriteAllTextAsync(srcFile, "New Content From Source");
        await File.WriteAllTextAsync(existingTarget, "Existing Old Target Content");

        var plan = new List<FileOrganizePlanItem>
        {
            new()
            {
                SourcePath = srcFile,
                DestinationPath = existingTarget,
                Category = StorageCategoryType.Documents,
                IsSelected = true
            }
        };

        var organizer = new StorageOrganizerService();
        var journal = await organizer.ExecutePlanAsync(plan, null, CancellationToken.None);

        Assert.Single(journal.Entries);
        var entry = journal.Entries[0];

        // Ensure existing target was untouched
        Assert.True(File.Exists(existingTarget));
        Assert.Equal("Existing Old Target Content", await File.ReadAllTextAsync(existingTarget));

        // Ensure moved file was renamed safely with _piko1
        Assert.Contains("_piko1.pdf", entry.DestinationPath);
        Assert.True(File.Exists(entry.DestinationPath));
        Assert.Equal("New Content From Source", await File.ReadAllTextAsync(entry.DestinationPath));
    }

    [Fact]
    public void PikoStateManager_TransitionsAndNotificationsWork()
    {
        var manager = new PikoStateManager();
        Assert.Equal(PikoEmotion.Idle, manager.CurrentEmotion);

        PikoEmotion? observedEmotion = null;
        manager.EmotionChanged += (_, newEmotion) => observedEmotion = newEmotion;

        manager.SetEmotion(PikoEmotion.Working);
        Assert.Equal(PikoEmotion.Working, manager.CurrentEmotion);
        Assert.Equal(PikoEmotion.Working, observedEmotion);

        manager.TriggerNotification("ChatGPT selesai! ✨", "Jawaban siap", "ChatGPT");
        Assert.Equal(PikoEmotion.Success, manager.CurrentEmotion);
        Assert.True(manager.HasActiveNotification);
        Assert.Equal("ChatGPT selesai! ✨", manager.ActiveNotificationTitle);

        manager.DismissNotification();
        Assert.False(manager.HasActiveNotification);
        Assert.Equal(PikoEmotion.Idle, manager.CurrentEmotion);
    }

    [Fact]
    public void BridgeMessage_Validation_AcceptsMediaMetadataAndRichPayload()
    {
        const string validToken = "piko-dev-bridge-token-2026";

        var metaMsg = new BridgeMessage
        {
            Type = BridgeMessage.TypeMediaMetadata,
            Token = validToken,
            Source = "YouTube",
            Title = "Starboy",
            Channel = "The Weeknd",
            VideoId = "34Na4j8AVgA",
            ThumbnailUrl = "https://i.ytimg.com/vi/34Na4j8AVgA/hqdefault.jpg",
            PageUrl = "https://www.youtube.com/watch?v=34Na4j8AVgA",
            Duration = 230,
            CurrentTime = 45,
            PlaybackState = "playing",
            TabId = 101
        };

        Assert.True(metaMsg.IsValid(validToken));
        Assert.Equal("The Weeknd", metaMsg.Channel);
        Assert.Equal("34Na4j8AVgA", metaMsg.VideoId);
        Assert.Equal(101, metaMsg.TabId);

        var altMetaMsg = new BridgeMessage
        {
            Type = BridgeMessage.TypeMediaMetadataAlt,
            Token = validToken
        };
        Assert.True(altMetaMsg.IsValid(validToken));

        var progressMsg = new BridgeMessage
        {
            Type = BridgeMessage.TypeMediaProgress,
            Token = validToken
        };
        Assert.True(progressMsg.IsValid(validToken));
    }

    [Fact]
    public void SharedMediaState_ResetAndFormatting_OperatesCorrectly()
    {
        var media = new SharedMediaState
        {
            Source = "YouTube",
            Title = "Blinding Lights",
            Channel = "The Weeknd",
            VideoId = "4NRXx6U8ABQ",
            PlaybackState = "playing",
            Duration = 200,
            CurrentTime = 65
        };

        Assert.True(media.IsActive);
        Assert.True(media.HasMedia);
        Assert.Equal("1:05", media.FormatTime(65));
        Assert.Equal("3:20", media.FormatTime(200));
        Assert.Equal("1:01:05", media.FormatTime(3665));

        media.Reset();
        Assert.False(media.IsActive);
        Assert.False(media.HasMedia);
        Assert.Equal(string.Empty, media.Title);
        Assert.Equal("stopped", media.PlaybackState);
    }

    [Fact]
    public void LocalBridgeService_CommandQueue_QueuesAndDequeues()
    {
        var service = new LocalBridgeService(port: 17325);
        var cmd1 = service.EnqueueMediaCommand("pause", 42);
        var cmd2 = service.EnqueueMediaCommand("play", 42);

        Assert.NotNull(service);
        Assert.NotNull(cmd1);
        Assert.NotNull(cmd2);
        Assert.Equal("pause", cmd1.Action);
        Assert.Equal("play", cmd2.Action);
        Assert.Equal(42, cmd1.TabId);
        Assert.False(string.IsNullOrWhiteSpace(cmd1.Id));
        Assert.False(string.IsNullOrWhiteSpace(cmd2.Id));
        Assert.NotEqual(cmd1.Id, cmd2.Id);
    }

    [Fact]
    public void BridgeMessage_MediaCommandResult_ValidationAndProperties()
    {
        const string validToken = "piko-dev-bridge-token-2026";
        var resultMsg = new BridgeMessage
        {
            Type = BridgeMessage.TypeMediaCommandResult,
            Token = validToken,
            Source = "YouTube",
            CommandId = "cmd_test_123",
            CommandAction = "pause",
            CommandSuccess = true,
            PlaybackState = "paused",
            TabId = 99
        };

        Assert.True(resultMsg.IsValid(validToken));
        Assert.Equal("cmd_test_123", resultMsg.CommandId);
        Assert.Equal("pause", resultMsg.CommandAction);
        Assert.True(resultMsg.CommandSuccess);
        Assert.Equal("paused", resultMsg.PlaybackState);
        Assert.Equal(99, resultMsg.TabId);
    }

    [Fact]
    public void DynamicIslandState_AllStatesAreDefinedAndDistinct()
    {
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.Idle));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.Hover));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.Sleepy));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.MusicActive));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.MediaPaused));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.AiWorking));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.Success));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.Notification));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.Expanded));
        Assert.True(Enum.IsDefined(typeof(DynamicIslandState), DynamicIslandState.Pinned));
    }
}
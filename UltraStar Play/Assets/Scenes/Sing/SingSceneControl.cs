﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using ProTrans;
using UniInject;
using UniInject.Extensions;
using UniRx;
using UnityEngine;
using UnityEngine.UIElements;
using IBinding = UniInject.IBinding;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class SingSceneControl : MonoBehaviour, INeedInjection, IBinder
{
    private static SingSceneControl instance;
    public static SingSceneControl Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<SingSceneControl>();
            }
            return instance;
        }
    }

    [InjectedInInspector]
    public PlayerControl playerControlPrefab;

    [InjectedInInspector]
    public VisualTreeAsset playerUi;

    [InjectedInInspector]
    public VisualTreeAsset sentenceRatingUi;

    [InjectedInInspector]
    public VisualTreeAsset noteUi;

    [InjectedInInspector]
    public VisualTreeAsset dialogUi;

    [InjectedInInspector]
    public VisualTreeAsset perfectEffectStarUi;

    [InjectedInInspector]
    public VisualTreeAsset goldenNoteStarUi;

    [InjectedInInspector]
    public VisualTreeAsset goldenNoteHitStarUi;

    [Inject(UxmlName = R.UxmlNames.background)]
    public VisualElement background;

    [InjectedInInspector]
    public SongAudioPlayer songAudioPlayer;

    [InjectedInInspector]
    public SongVideoPlayer songVideoPlayer;

    [Inject]
    private Injector injector;

    [Inject]
    private Settings settings;

    [Inject]
    private SceneNavigator sceneNavigator;

    [Inject]
    private ServerSideConnectRequestManager serverSideConnectRequestManager;

    [Inject]
    private Statistics statistics;

    [Inject]
    private UltraStarPlayInputManager inputManager;

    [Inject(UxmlName = R.UxmlNames.topLyricsContainer)]
    private VisualElement topLyricsContainer;

    [Inject(UxmlName = R.UxmlNames.bottomLyricsContainer)]
    private VisualElement bottomLyricsContainer;

    [Inject(UxmlName = R.UxmlNames.playerUiContainer)]
    private VisualElement playerUiContainer;

    [Inject(UxmlName = R.UxmlNames.pauseOverlay)]
    private VisualElement pauseOverlay;

    [Inject(UxmlName = R.UxmlNames.doubleClickToTogglePauseElement)]
    private VisualElement doubleClickToTogglePauseElement;

    [Inject(UxmlName = R.UxmlNames.inputLegend)]
    private VisualElement inputLegend;

    [Inject]
    private UIDocument uiDocument;

    [Inject]
    private ThemeManager themeManager;

    public List<PlayerControl> PlayerControls { get; private set; } = new();

    private PlayerControl lastLeadingPlayerControl;

    private VisualElement[] playerUiColumns;

    private SingSceneData sceneData;
    public SingSceneData SceneData
    {
        get
        {
            if (sceneData == null)
            {
                sceneData = SceneNavigator.Instance.GetSceneDataOrThrow<SingSceneData>();
            }
            return sceneData;
        }
    }

    public SongMeta SongMeta
    {
        get
        {
            return SceneData.SelectedSongMeta;
        }
    }

    public double DurationOfSongInMillis
    {
        get
        {
            return songAudioPlayer.DurationOfSongInMillis;
        }
    }

    public double PositionInSongInMillis
    {
        get
        {
            return songAudioPlayer.PositionInSongInMillis;
        }
    }

    public double CurrentBeat
    {
        get
        {
            return songAudioPlayer.GetCurrentBeat(false);
        }
    }

    private SingingLyricsControl topSingingLyricsControl;
    private SingingLyricsControl bottomSingingLyricsControl;

    private TimeBarControl timeBarControl;

    private MessageDialogControl dialogControl;
    public bool IsDialogOpen => dialogControl != null;

    private ContextMenuControl contextMenuControl;
    private CommonScoreControl commonScoreControl;

    public bool IsCommonScore => settings.GameSettings.ScoreMode == EScoreMode.CommonAverage
                                 && SceneData.SelectedPlayerProfiles.Count >= 2;

    public bool IsIndividualScore => settings.GameSettings.ScoreMode == EScoreMode.Individual
                                     || (settings.GameSettings.ScoreMode == EScoreMode.CommonAverage
                                         && SceneData.SelectedPlayerProfiles.Count <= 1);

    private float startTimeInSeconds;
    private bool hasRecordedSongStartedStatistics;

    private void Start()
    {
        string playerProfilesCsv = SceneData.SelectedPlayerProfiles.Select(it => it.Name).ToCsv();
        Debug.Log($"{playerProfilesCsv} start (or continue) singing of {SongMeta.Title} at {SceneData.PositionInSongInMillis} ms.");

        startTimeInSeconds = Time.time;

        pauseOverlay.HideByDisplay();
        new DoubleClickControl(doubleClickToTogglePauseElement).DoublePointerDownEventStream
            .Subscribe(_ => TogglePlayPause());

        // Prepare player UI layout (depends on player count)
        PreparePlayerUiLayout();

        // Create PlayerControl (and PlayerUi) for each player
        List<PlayerProfile> playerProfilesWithoutMic = new();
        for (int i = 0; i < SceneData.SelectedPlayerProfiles.Count; i++)
        {
            PlayerProfile playerProfile = SceneData.SelectedPlayerProfiles[i];
            SceneData.PlayerProfileToMicProfileMap.TryGetValue(playerProfile, out MicProfile micProfile);
            if (micProfile == null)
            {
                playerProfilesWithoutMic.Add(playerProfile);
            }
            PlayerControl playerControl = CreatePlayerControl(playerProfile, micProfile, i);

            if (SceneData.PlayerProfileToScoreDataMap.TryGetValue(playerProfile, out PlayerScoreControlData scoreData))
            {
                playerControl.PlayerScoreControl.ScoreData = scoreData;
            }

            // Update leading player icon
            if (SceneData.SelectedPlayerProfiles.Count > 1)
            {
                playerControl.PlayerScoreControl.SentenceScoreEventStream
                    .Subscribe(_ => UpdateLeadingPlayerIcon());
            }
        }

        // Handle dummy singers
        if (Application.isEditor)
        {
            InitDummySingers();
        }

        // Create warning about missing microphones
        string playerNameCsv = string.Join(", ", playerProfilesWithoutMic.Select(it => it.Name).ToList());
        if (!playerProfilesWithoutMic.IsNullOrEmpty())
        {
            string title = TranslationManager.GetTranslation(R.Messages.singScene_missingMicrophones_title);
            string message = TranslationManager.GetTranslation(R.Messages.singScene_missingMicrophones_message,
                "playerNameCsv", playerNameCsv);

            VisualElement visualElement = dialogUi.CloneTree();
            visualElement.AddToClassList("overlay");
            background.Add(visualElement);

            dialogControl = injector
                .WithRootVisualElement(visualElement)
                .CreateAndInject<MessageDialogControl>();
            dialogControl.Title = title;
            dialogControl.Message = message;
            dialogControl.DialogTitleImage.ShowByDisplay();
            dialogControl.DialogTitleImage.AddToClassList(R.UxmlClasses.warning);
            Button okButton = dialogControl.AddButton("OK", CloseDialog);
            okButton.Focus();

            themeManager.UpdateThemeSpecificStyleSheets();
        }

        // Associate LyricsDisplayer with one of the (duett) players
        InitSingingLyricsControls();

        StartCoroutine(StartMusicAndVideo());

        // Input legend (in pause overlay)
        UpdateInputLegend();
        inputManager.InputDeviceChangeEventStream.Subscribe(_ => UpdateInputLegend());

        // Register ContextMenu
        contextMenuControl = injector
            .WithRootVisualElement(doubleClickToTogglePauseElement)
            .CreateAndInject<ContextMenuControl>();
        contextMenuControl.FillContextMenuAction = FillContextMenu;

        // Automatically start recording on companion apps
        PlayerControls.ForEach(playerControl =>
        {
            playerControl.PlayerMicPitchTracker.SendMicProfileToConnectedClient();
            playerControl.PlayerMicPitchTracker.SendStartRecordingMessageToConnectedClient();
        });

        commonScoreControl = injector.CreateAndInject<CommonScoreControl>();

        // Skip beginning of song via #START tag of txt file
        if (SceneData.PositionInSongInMillis <= 0
            && SongMeta.Start > 0)
        {
            // #START tag in txt file is in seconds (but #END is in milliseconds).
            SkipToPositionInSong(SongMeta.Start * 1000);
        }

        // Update TimeBar every second
        StartCoroutine(CoroutineUtils.ExecuteRepeatedlyInSeconds(1f, () =>
        {
            timeBarControl?.UpdateTimeValueLabel(songAudioPlayer.PositionInSongInMillis, songAudioPlayer.DurationOfSongInMillis);
        }));
    }

    private void InitDummySingers()
    {
        bool includeInactive = false;
        AbstractDummySinger[] findObjectsOfType = FindObjectsOfType<AbstractDummySinger>(includeInactive);
        foreach (AbstractDummySinger dummySinger in findObjectsOfType)
        {
            if (dummySinger.playerIndexToSimulate < PlayerControls.Count)
            {
                dummySinger.SetPlayerControl(PlayerControls[dummySinger.playerIndexToSimulate]);
                injector.Inject(dummySinger);
            }
            else
            {
                Debug.LogWarning("DummySinger cannot simulate player with index " + dummySinger.playerIndexToSimulate);
                dummySinger.gameObject.SetActive(false);
            }
        }
    }

    private void PreparePlayerUiLayout()
    {
        int playerCount = SceneData.SelectedPlayerProfiles.Count;
        playerUiContainer.Children()
            .Where(it => it.name != R.UxmlNames.commonScoreSentenceRatingContainer)
            .ToList()
            .ForEach(it => it.RemoveFromHierarchy());
        if (playerCount <= 1)
        {
            // Add empty VisualElement as spacer. Otherwise the player UI would take all the available space.
            VisualElement spacer = new();
            spacer.style.flexGrow = 1;
            playerUiContainer.Add(spacer);
            return;
        }

        if (playerCount > 3)
        {
            // Create row
            playerUiContainer.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);

            // Create columns
            int columnCount = (int)Math.Sqrt(playerCount);
            playerUiColumns = new VisualElement[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                VisualElement column = new();
                column.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Column);
                column.style.flexGrow = 1;
                column.style.height = new StyleLength(new Length(100, LengthUnit.Percent));

                playerUiContainer.Add(column);
                playerUiColumns[i] = column;
            }
        }
    }

    private void InitSingingLyricsControls()
    {
        if (PlayerControls.IsNullOrEmpty()
            || !settings.GraphicSettings.showStaticLyrics)
        {
            topLyricsContainer.HideByDisplay();
            bottomLyricsContainer.HideByDisplay();
            return;
        }

        SingingLyricsControl CreateSingingLyricsControl(VisualElement visualElement, PlayerControl playerController)
        {
            Injector lyricsControlInjector = UniInjectUtils.CreateInjector(injector);
            lyricsControlInjector.AddBindingForInstance(playerController);
            SingingLyricsControl singingLyricsControl = lyricsControlInjector
                .WithRootVisualElement(visualElement)
                .CreateAndInject<SingingLyricsControl>();
            return singingLyricsControl;
        }

        Dictionary<Voice, List<PlayerControl>> voiceToPlayerControlsMap = new();
        PlayerControls.ForEach(it => voiceToPlayerControlsMap.AddInsideList(it.Voice, it));
        if (voiceToPlayerControlsMap.Keys.Count >= 2)
        {
            // There are two different sets of lyrics that need to be displayed
            List<PlayerControl> playerControlsUsingFirstVoice = voiceToPlayerControlsMap[voiceToPlayerControlsMap.Keys.FirstOrDefault()];
            List<PlayerControl> playerControlsUsingSecondVoice = voiceToPlayerControlsMap[voiceToPlayerControlsMap.Keys.LastOrDefault()];
            topSingingLyricsControl = CreateSingingLyricsControl(topLyricsContainer, playerControlsUsingFirstVoice.FirstOrDefault());
            bottomSingingLyricsControl = CreateSingingLyricsControl(bottomLyricsContainer, playerControlsUsingSecondVoice.FirstOrDefault());
        }
        else
        {
            topLyricsContainer.HideByDisplay();
            bottomSingingLyricsControl = CreateSingingLyricsControl(bottomLyricsContainer, PlayerControls[0]);
        }
    }

    private void UpdateLeadingPlayerIcon()
    {
        // Find best player with score > 0
        PlayerControl leadingPlayerControl = null;
        foreach (PlayerControl playerController in PlayerControls)
        {
            if ((leadingPlayerControl == null && playerController.PlayerScoreControl.TotalScore > 0)
               || (leadingPlayerControl != null && playerController.PlayerScoreControl.TotalScore > leadingPlayerControl.PlayerScoreControl.TotalScore))
            {
                leadingPlayerControl = playerController;
            }
        }

        // // Show icon for best player only
        if (leadingPlayerControl != null
            && lastLeadingPlayerControl != leadingPlayerControl)
        {
            leadingPlayerControl.PlayerUiControl.ShowLeadingPlayerIcon();
        }
        foreach (PlayerControl playerController in PlayerControls)
        {
            if (playerController != leadingPlayerControl)
            {
                playerController.PlayerUiControl.HideLeadingPlayerIcon();
            }
        }

        lastLeadingPlayerControl = leadingPlayerControl;
    }

    private void InitTimeBar()
    {
        timeBarControl = new TimeBarControl();
        injector.Inject(timeBarControl);
        timeBarControl.UpdateTimeBarRectangles(SongMeta, PlayerControls, DurationOfSongInMillis);
    }

    private IEnumerator StartMusicAndVideo()
    {
        // Start the music
        yield return StartAudioPlayback();

        // Start any associated video
        songVideoPlayer.SongMeta = SongMeta;
        if (SongMeta.Video.IsNullOrEmpty())
        {
            songVideoPlayer.ShowBackgroundImage();
        }
        else
        {
            songVideoPlayer.StartVideoOrShowBackgroundImage();
        }
    }

    void OnDisable()
    {
        if (SceneData.IsRestart)
        {
            SceneData.IsRestart = false;
            SceneData.PositionInSongInMillis = 0;
        }
        else
        {
            SceneData.PositionInSongInMillis = PositionInSongInMillis;
        }
    }

    void Update()
    {
        PlayerControls.ForEach(playerControl =>
        {
            if (songAudioPlayer.IsPlaying)
            {
                playerControl.SetCurrentBeat(CurrentBeat);
                playerControl.UpdateUi();
            }
        });
        timeBarControl.UpdatePositionIndicator(songAudioPlayer.PositionInSongInMillis, songAudioPlayer.DurationOfSongInMillis);
        topSingingLyricsControl?.Update(songAudioPlayer.PositionInSongInMillis);
        bottomSingingLyricsControl?.Update(songAudioPlayer.PositionInSongInMillis);

        if (!hasRecordedSongStartedStatistics)
        {
            // Save information that the song has been started after some seconds or half of the song.
            float songSingingDuration = Time.time - startTimeInSeconds;
            float songDurationInSeconds = (float)songAudioPlayer.DurationOfSongInMillis / 1000;
            if (songSingingDuration >= 30
                || (songDurationInSeconds > 0
                    && songSingingDuration >= songDurationInSeconds / 2))
            {
                hasRecordedSongStartedStatistics = true;
                statistics.RecordSongStarted(SongMeta);
            }
        }
    }

    public void SkipToNextSingableNote()
    {
        IEnumerable<int> nextSingableNotes = PlayerControls
            .Select(it => it.GetNextSingableNote(CurrentBeat))
            .Where(nextSingableNote => nextSingableNote != null)
            .Select(nextSingableNote => nextSingableNote.StartBeat);
        if (nextSingableNotes.Count() <= 0)
        {
            return;
        }
        int nextStartBeat = nextSingableNotes.Min();

        // For debugging, go fast to next lyrics. In production, give the player some time to prepare.
        double offsetInMillis = Application.isEditor ? 500 : 1500;
        double targetPositionInMillis = BpmUtils.BeatToMillisecondsInSong(SongMeta, nextStartBeat) - offsetInMillis;
        if (targetPositionInMillis > 0 && targetPositionInMillis > PositionInSongInMillis)
        {
            SkipToPositionInSong(targetPositionInMillis);
        }
    }

    public void SkipToPositionInSong(double positionInSongInMillis)
    {
        int nextBeatToScore = (int)Math.Max(CurrentBeat, sceneData.NextBeatToScore);
        Debug.Log($"Skipping forward to {positionInSongInMillis} milliseconds, next beat to score is {nextBeatToScore}");
        songAudioPlayer.PositionInSongInMillis = positionInSongInMillis;
        foreach (PlayerControl playerController in PlayerControls)
        {
            playerController.PlayerScoreControl.NextBeatToScore = nextBeatToScore;
            playerController.PlayerMicPitchTracker.SkipToBeat(CurrentBeat);
        }
    }

    public void Restart()
    {
        SceneData.IsRestart = true;
        sceneNavigator.LoadScene(EScene.SingScene, SceneData);
    }

    public void OpenSongInEditor()
    {
        int maxBeatToScore = PlayerControls
            .Select(playerController => playerController.PlayerScoreControl.NextBeatToScore)
            .Max();
        SceneData.NextBeatToScore = Math.Max((int)CurrentBeat, maxBeatToScore);

        SceneData.PlayerProfileToScoreDataMap = new Dictionary<PlayerProfile, PlayerScoreControlData>();
        foreach (PlayerControl playerController in PlayerControls)
        {
            SceneData.PlayerProfileToScoreDataMap.Add(playerController.PlayerProfile, playerController.PlayerScoreControl.ScoreData);
        }

        SongEditorSceneData songEditorSceneData = new()
        {
            PreviousSceneData = SceneData,
            PreviousScene = EScene.SingScene,
            PositionInSongInMillis = PositionInSongInMillis,
            SelectedSongMeta = SongMeta,
            PlayerProfileToMicProfileMap = sceneData.PlayerProfileToMicProfileMap,
            SelectedPlayerProfiles = sceneData.SelectedPlayerProfiles,
        };
        PlayerControls.ForEach(playerControl => playerControl.PlayerMicPitchTracker.SendStopRecordingMessageToConnectedClient());
        sceneNavigator.LoadScene(EScene.SongEditorScene, songEditorSceneData);
    }

    public void FinishScene(bool isAfterEndOfSong)
    {
        if (settings.GameSettings.ScoreMode == EScoreMode.None)
        {
            FinishSceneToSongSelect();
        }
        else
        {
            FinishSceneToSingingResults(isAfterEndOfSong);
        }
    }

    private void FinishSceneToSongSelect()
    {
        // Open song select without recording scores
        SongSelectSceneData songSelectSceneData = new();
        songSelectSceneData.SongMeta = SongMeta;
        PlayerControls.ForEach(playerControl => playerControl.PlayerMicPitchTracker.SendStopRecordingMessageToConnectedClient());
        sceneNavigator.LoadScene(EScene.SongSelectScene, songSelectSceneData);
    }

    public void FinishSceneToSingingResults(bool isAfterEndOfSong)
    {
        // Open the singing results scene.
        SingingResultsSceneData singingResultsSceneData = new();
        singingResultsSceneData.SongMeta = SongMeta;
        singingResultsSceneData.SongDurationInMillis = (int)songAudioPlayer.DurationOfSongInMillis;

        // Add scores, either for individual players, or as one common score.
        List<SongStatistic> songStatistics = new();
        if (IsIndividualScore)
        {
            // Add and record score for each player individually.
            singingResultsSceneData.PlayerProfileToMicProfileMap = sceneData.PlayerProfileToMicProfileMap;
            PlayerControls.ForEach(playerControl =>
            {
                singingResultsSceneData.AddPlayerScores(playerControl.PlayerProfile, playerControl.PlayerScoreControl.ScoreData);
            });

            songStatistics = PlayerControls
                .Select(playerControl => new SongStatistic(playerControl.PlayerProfile.Name,
                    playerControl.PlayerProfile.Difficulty,
                    playerControl.PlayerScoreControl.TotalScore,
                    EScoreMode.Individual))
                .ToList();
        }
        else if (IsCommonScore)
        {
            // Add and record score as average of all players.
            List<PlayerScoreControl> scoreControls = PlayerControls
                .Select(playerControl => playerControl.PlayerScoreControl)
                .ToList();
            string commonPlayerProfileName = PlayerControls
                .Select(playerControl => playerControl.PlayerProfile.Name)
                .JoinWith(settings.GameSettings.CommonScoreNameSeparator);
            EDifficulty easiestPlayerProfileDifficulty = PlayerControls
                .FindMinElement(playerControl => (int)playerControl.PlayerProfile.Difficulty)
                .PlayerProfile.Difficulty;
            EAvatar commonPlayerProfileAvatar = PlayerControls.FirstOrDefault().PlayerProfile.Avatar;
            PlayerProfile commonPlayerProfile = new(commonPlayerProfileName, easiestPlayerProfileDifficulty, commonPlayerProfileAvatar);
            PlayerScoreControlData commonScoreData = new()
            {
                TotalScore = (int)scoreControls.Select(scoreControl => scoreControl.TotalScore).Average(),
                GoldenNotesTotalScore = (int)scoreControls.Select(scoreControl => scoreControl.GoldenNotesTotalScore).Average(),
                NormalNotesTotalScore = (int)scoreControls.Select(scoreControl => scoreControl.NormalNotesTotalScore).Average(),
                PerfectSentenceBonusTotalScore = (int)scoreControls.Select(scoreControl => scoreControl.PerfectSentenceBonusTotalScore).Average(),
                PerfectSentenceCount = (int)scoreControls.Select(scoreControl => scoreControl.ScoreData.PerfectSentenceCount).Average(),
                TotalSentenceCount = (int)scoreControls.Select(scoreControl => scoreControl.ScoreData.TotalSentenceCount).Average(),
                NormalNoteLengthTotal = (int)scoreControls.Select(scoreControl => scoreControl.ScoreData.NormalNoteLengthTotal).Average(),
                GoldenNoteLengthTotal = (int)scoreControls.Select(scoreControl => scoreControl.ScoreData.GoldenNoteLengthTotal).Average()
            };
            singingResultsSceneData.AddPlayerScores(commonPlayerProfile, commonScoreData);

            SongStatistic commonSongStatistic = new SongStatistic(
                commonPlayerProfileName,
                easiestPlayerProfileDifficulty,
                commonScoreData.TotalScore,
                EScoreMode.CommonAverage);
            songStatistics = new() { commonSongStatistic };
        }

        // Check if the full song has been sung, i.e., the playback position is after the last note.
        // This determines whether the statistics should be updated and the score should be recorded.
        bool isAfterLastNote = true;
        PlayerControls.ForEach(playerControl =>
        {
            Note lastNoteInSong = playerControl.GetLastNoteInSong();
            if (!isAfterEndOfSong && CurrentBeat < lastNoteInSong.EndBeat)
            {
                isAfterLastNote = false;
            }
        });
        if (isAfterLastNote
            && !songStatistics.IsNullOrEmpty())
        {
            UpdateSongFinishedStats(songStatistics);
        }

        PlayerControls.ForEach(playerControl => playerControl.PlayerMicPitchTracker.SendStopRecordingMessageToConnectedClient());
        sceneNavigator.LoadScene(EScene.SingingResultsScene, singingResultsSceneData);
    }

    private List<ConnectedClientHandlerAndMicProfile> GetConnectedClientHandlers()
    {
        IEnumerable<MicProfile> micProfiles = PlayerControls.Select(playerProfile => playerProfile.MicProfile);
        return serverSideConnectRequestManager.GetConnectedClientHandlers(micProfiles);
    }

    private void UpdateSongFinishedStats(List<SongStatistic> songStatistics)
    {
        statistics.RecordSongFinished(SongMeta, songStatistics);
    }

    private PlayerControl CreatePlayerControl(PlayerProfile playerProfile, MicProfile micProfile, int playerIndex)
    {
        Voice voice = GetVoice(playerProfile);

        PlayerControl playerControl = GameObject.Instantiate<PlayerControl>(playerControlPrefab);

        Injector playerControlInjector = UniInjectUtils.CreateInjector(injector);
        playerControlInjector.AddBindingForInstance(playerProfile);
        playerControlInjector.AddBindingForInstance(voice);
        playerControlInjector.AddBindingForInstance(micProfile);
        playerControlInjector.AddBindingForInstance(playerControlInjector, RebindingBehavior.Ignore);
        playerControlInjector.Inject(playerControl);

        PlayerControls.Add(playerControl);

        AddPlayerUi(playerControl.PlayerUiControl.RootVisualElement, playerIndex);

        return playerControl;
    }

    private void AddPlayerUi(VisualElement visualElement, int playerIndex)
    {
        int playerCount = SceneData.SelectedPlayerProfiles.Count;
        if (playerCount <= 3)
        {
            playerUiContainer.Add(visualElement);
            return;
        }

        int columnIndex = (int)((float)playerUiColumns.Length * (float)playerIndex / (float)playerCount);
        VisualElement column = playerUiColumns[columnIndex];
        column.Add(visualElement);
    }

    private string GetVoiceName(PlayerProfile playerProfile)
    {
        List<string> voiceNames = new(SongMeta.VoiceNames.Keys);
        int voiceNameCount = voiceNames.Count;
        if (voiceNameCount <= 1)
        {
            return Voice.soloVoiceName;
        }

        if (sceneData.PlayerProfileToVoiceNameMap.TryGetValue(playerProfile, out string voiceName))
        {
            return voiceName;
        }

        if (SceneData.SelectedPlayerProfiles.Count == 1)
        {
            return Voice.soloVoiceName;
        }

        int voiceIndex = SceneData.SelectedPlayerProfiles.IndexOf(playerProfile) % voiceNames.Count;
        return voiceNames[voiceIndex];
    }

    public void TogglePlayPause()
    {
        if (songAudioPlayer.IsPlaying)
        {
            pauseOverlay.ShowByDisplay();
            songAudioPlayer.PauseAudio();
            PlayerControls.ForEach(playerControl => playerControl.PlayerMicPitchTracker.SendStopRecordingMessageToConnectedClient());
        }
        else
        {
            pauseOverlay.HideByDisplay();
            songAudioPlayer.PlayAudio();
            PlayerControls.ForEach(playerControl =>
            {
                playerControl.PlayerMicPitchTracker.SendPositionInSongToClientRapidly();
                playerControl.PlayerMicPitchTracker.SendStartRecordingMessageToConnectedClient();
            });
        }
    }

    private IEnumerator StartAudioPlayback()
    {
        if (songAudioPlayer.IsPlaying)
        {
            Debug.LogWarning("Song already playing");
            yield break;
        }

        songAudioPlayer.Init(SongMeta);

        if (!songAudioPlayer.HasAudioClip)
        {
            // Loading the audio failed.
            PlayerControls.ForEach(playerControl => playerControl.PlayerMicPitchTracker.SendStopRecordingMessageToConnectedClient());
            sceneNavigator.LoadScene(EScene.SongSelectScene);
            yield break;
        }

        // The time bar needs the duration of the song to calculate positions.
        // The duration of the song should be available now.
        InitTimeBar();

        songAudioPlayer.PlayAudio();
        if (SceneData.PositionInSongInMillis > 0)
        {
            SkipToPositionInSong(SceneData.PositionInSongInMillis);
        }
    }

    public List<IBinding> GetBindings()
    {
        // Binding happens before the injection finished. Thus, no fields can be used here that have been injected.
        BindingBuilder bb = new();
        bb.BindExistingInstance(this);
        bb.BindExistingInstance(SongMeta);
        bb.BindExistingInstance(songAudioPlayer);
        bb.BindExistingInstance(songVideoPlayer);
        bb.BindExistingInstance(gameObject);
        bb.Bind(nameof(playerUi)).ToExistingInstance(playerUi);
        bb.Bind(nameof(sentenceRatingUi)).ToExistingInstance(sentenceRatingUi);
        bb.Bind(nameof(noteUi)).ToExistingInstance(noteUi);
        bb.Bind(nameof(perfectEffectStarUi)).ToExistingInstance(perfectEffectStarUi);
        bb.Bind(nameof(goldenNoteStarUi)).ToExistingInstance(goldenNoteStarUi);
        bb.Bind(nameof(goldenNoteHitStarUi)).ToExistingInstance(goldenNoteHitStarUi);
        return bb.GetBindings();
    }

    private Voice GetVoice(PlayerProfile playerProfile)
    {
        string voiceName = GetVoiceName(playerProfile);
        IReadOnlyCollection<Voice> voices = sceneData.SelectedSongMeta.GetVoices();
        Voice matchingVoice = voices.FirstOrDefault(it => it.VoiceNameEquals(voiceName));
        if (matchingVoice != null)
        {
            return matchingVoice;
        }

        string voiceNameCsv = voices.Select(it => it.Name).ToCsv();
        Debug.LogError($"The song data does not contain a voice with name {voiceName}."
                       + $" Available voice names: {voiceNameCsv}");
        return voices.FirstOrDefault();
    }

    private void UpdateInputLegend()
    {
        inputLegend.Query<Label>()
            .Where(label => label is not FontIcon)
            .ForEach(label => label.RemoveFromHierarchy());

        InputLegendControl.TryAddInputActionInfo(R.InputActions.usplay_back,
            TranslationManager.GetTranslation(R.Messages.back),
            inputLegend);
        InputLegendControl.TryAddInputActionInfo(R.InputActions.usplay_openSongEditor,
            TranslationManager.GetTranslation(R.Messages.action_openSongEditor),
            inputLegend);
        InputLegendControl.TryAddInputActionInfo(R.InputActions.usplay_restartSong,
            TranslationManager.GetTranslation(R.Messages.action_restart),
            inputLegend);

        if (inputManager.InputDeviceEnum == EInputDevice.Touch)
        {
            inputLegend.Add(InputLegendControl.CreateInputActionInfoUi(new InputActionInfo(
                TranslationManager.GetTranslation(R.Messages.continue_),
                TranslationManager.GetTranslation(R.Messages.action_doubleTap))));
            inputLegend.Add(InputLegendControl.CreateInputActionInfoUi(new InputActionInfo(
                TranslationManager.GetTranslation(R.Messages.action_openContextMenu),
                TranslationManager.GetTranslation(R.Messages.action_longPress))));
        }
        else
        {
            inputLegend.Add(InputLegendControl.CreateInputActionInfoUi(new InputActionInfo(
                TranslationManager.GetTranslation(R.Messages.action_skipToNextLyrics),
                TranslationManager.GetTranslation(R.Messages.action_navigateRight))));
        }
    }

    public void CloseDialog()
    {
        if (dialogControl == null)
        {
            return;
        }

        dialogControl.CloseDialog();
        dialogControl = null;
    }

    protected void FillContextMenu(ContextMenuPopupControl contextMenuPopup)
    {
        contextMenuPopup.AddItem(TranslationManager.GetTranslation(R.Messages.action_togglePause),
            () => TogglePlayPause());
        contextMenuPopup.AddItem(TranslationManager.GetTranslation(R.Messages.action_restart),
            () => Restart());
        contextMenuPopup.AddItem(TranslationManager.GetTranslation(R.Messages.action_skipToNextLyrics),
            () => SkipToNextSingableNote());
        contextMenuPopup.AddItem(TranslationManager.GetTranslation(R.Messages.action_exitSong),
            () => FinishScene(false));
        contextMenuPopup.AddItem(TranslationManager.GetTranslation(R.Messages.action_openSongEditor),
            () => OpenSongInEditor());
    }
}

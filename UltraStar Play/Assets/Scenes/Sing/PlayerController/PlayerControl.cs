﻿using System;
using System.Collections.Generic;
using System.Linq;
using UniInject;
using UniInject.Extensions;
using UniRx;
using UnityEngine;
using UnityEngine.UIElements;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class PlayerControl : MonoBehaviour, INeedInjection, IInjectionFinishedListener
{
    [Inject(SearchMethod = SearchMethods.GetComponentInChildren)]
    public PlayerNoteRecorder PlayerNoteRecorder { get; private set; }

    [Inject(SearchMethod = SearchMethods.GetComponentInChildren)]
    public PlayerMicPitchTracker PlayerMicPitchTracker { get; private set; }

    [Inject(SearchMethod = SearchMethods.GetComponentInChildren)]
    public MicSampleRecorder MicSampleRecorder { get; private set; }

    [Inject(SearchMethod = SearchMethods.GetComponentInChildren)]
    public PlayerScoreControl PlayerScoreControl { get; private set; }

    [Inject]
    public PlayerProfile PlayerProfile { get; private set; }

    [Inject(Key = nameof(playerProfileIndex))]
    private int playerProfileIndex;
    
    [Inject(Optional = true)]
    public MicProfile MicProfile { get; private set; }

    [Inject]
    public Voice Voice { get; private set; }

    [Inject(Key = nameof(playerUi))]
    private VisualTreeAsset playerUi;
    
    [Inject(Key = nameof(playerInfoUi))]
    private VisualTreeAsset playerInfoUi;
    
    [Inject(UxmlName = R.UxmlNames.playerInfoUiListBottomLeft)]
    private VisualElement playerInfoUiListBottomLeft;
    
    [Inject(UxmlName = R.UxmlNames.playerInfoUiListBottomRight)]
    private VisualElement playerInfoUiListBottomRight;
    
    [Inject(UxmlName = R.UxmlNames.playerInfoUiListTopLeft)]
    private VisualElement playerInfoUiListTopLeft;
    
    [Inject(UxmlName = R.UxmlNames.playerInfoUiListTopRight)]
    private VisualElement playerInfoUiListTopRight;

    [Inject]
    private SingSceneData sceneData;
    
    private readonly Subject<EnterSentenceEvent> enterSentenceEventStream = new();
    public IObservable<EnterSentenceEvent> EnterSentenceEventStream => enterSentenceEventStream;

    // The sorted sentences of the Voice
    public List<Sentence> SortedSentences { get; private set; } = new();

    [Inject]
    private Injector injector;

    // An injector with additional bindings, such as the PlayerProfile and the MicProfile.
    private Injector childrenInjector;

    public PlayerUiControl PlayerUiControl { get; private set; }

    [Inject]
    private SongMeta songMeta;

    private int displaySentenceIndex;

    public void OnInjectionFinished()
    {
        this.PlayerUiControl = new PlayerUiControl();
        this.childrenInjector = CreateChildrenInjectorWithAdditionalBindings();

        SortedSentences = Voice.Sentences.ToList();
        SortedSentences.Sort(Sentence.comparerByStartBeat);

        // Create UI
        VisualElement playerUiVisualElement = playerUi.CloneTree().Children().First();
        playerUiVisualElement.userData = this;
        VisualElement playerInfoUiVisualElement = playerInfoUi.CloneTree().Children().First();
        playerInfoUiVisualElement.userData = this;
        AddPlayerInfoUiToUiDocument(playerInfoUiVisualElement);
        
        // Inject all children.
        // The injector hierarchy is searched from the bottom up.
        // Thus, we can create an injection hierarchy with elements that are not necessarily in the same VisualElement hierarchy.
        Injector playerUiControlInjector = childrenInjector.CreateChildInjector()
            .WithRootVisualElement(playerInfoUiVisualElement)
            .CreateChildInjector()
            .WithRootVisualElement(playerUiVisualElement);
        playerUiControlInjector.Inject(PlayerUiControl);
        foreach (INeedInjection childThatNeedsInjection in gameObject.GetComponentsInChildren<INeedInjection>(true))
        {
            if (childThatNeedsInjection is not PlayerControl)
            {
                childrenInjector.Inject(childThatNeedsInjection);
            }
        }
        SetDisplaySentenceIndex(0);
    }

    private void AddPlayerInfoUiToUiDocument(VisualElement playerInfoUiVisualElement)
    {
        bool hasTopPlayerInfoUiRow = (sceneData.SelectedPlayerProfiles.Count > 1 
                                      && sceneData.PlayerProfileToVoiceNameMap.Values
                                          .Distinct()
                                          .Count() > 1) 
                                     || sceneData.SelectedPlayerProfiles.Count > 8;
        
        int voiceIndex = songMeta.GetVoices().IndexOf(Voice);
        if (hasTopPlayerInfoUiRow
            && voiceIndex <= 0)
        {
            // Prefer position near the top lyrics
            Debug.Log("Prefer top");
            List<VisualElement> playerInfoUiLists = new()
            {
                playerInfoUiListTopLeft,
                playerInfoUiListTopRight,
                playerInfoUiListBottomLeft,
                playerInfoUiListBottomRight,
            };

            AddPlayerInfoUiToFreePlayerInfoUiList(playerInfoUiVisualElement, playerInfoUiLists);
        }
        else
        {
            // Prefer position near the bottom lyrics
            Debug.Log("Prefer bottom");
            List<VisualElement> playerInfoUiLists = new()
            {
                playerInfoUiListBottomLeft,
                playerInfoUiListBottomRight,
                playerInfoUiListTopLeft,
                playerInfoUiListTopRight,
            };

            AddPlayerInfoUiToFreePlayerInfoUiList(playerInfoUiVisualElement, playerInfoUiLists);
        }
    }

    private void AddPlayerInfoUiToFreePlayerInfoUiList(VisualElement playerInfoUiVisualElement, List<VisualElement> playerInfoUiLists)
    {
        VisualElement playerInfoUiList = playerInfoUiLists.FirstOrDefault(it => it.childCount < 4);
        if (playerInfoUiList == null)
        {
            playerInfoUiList = playerInfoUiLists.LastOrDefault();
        }
        playerInfoUiList.Add(playerInfoUiVisualElement);
    }

    public void UpdateUi()
    {
        PlayerUiControl.Update();
    }

    private Injector CreateChildrenInjectorWithAdditionalBindings()
    {
        Injector newInjector = UniInjectUtils.CreateInjector(injector);
        newInjector.AddBindingForInstance(MicSampleRecorder);
        newInjector.AddBindingForInstance(PlayerMicPitchTracker);
        newInjector.AddBindingForInstance(PlayerNoteRecorder);
        newInjector.AddBindingForInstance(PlayerScoreControl);
        newInjector.AddBindingForInstance(PlayerUiControl);
        newInjector.AddBindingForInstance(newInjector);
        newInjector.AddBindingForInstance(this);
        return newInjector;
    }

    public void SetCurrentBeat(double currentBeat)
    {
        // Change the current display sentence, when the current beat is over its last note.
        if (displaySentenceIndex < SortedSentences.Count && currentBeat >= GetDisplaySentence().LinebreakBeat)
        {
            Sentence nextDisplaySentence = GetUpcomingSentenceForBeat(currentBeat);
            int nextDisplaySentenceIndex = SortedSentences.IndexOf(nextDisplaySentence);
            if (nextDisplaySentenceIndex >= 0)
            {
                SetDisplaySentenceIndex(nextDisplaySentenceIndex);
            }
        }
    }

    private void SetDisplaySentenceIndex(int newValue)
    {
        displaySentenceIndex = newValue;

        Sentence displaySentence = GetSentence(displaySentenceIndex);

        // Update the UI
        enterSentenceEventStream.OnNext(new EnterSentenceEvent(displaySentence, displaySentenceIndex));
    }

    public Sentence GetSentence(int index)
    {
        Sentence sentence = (index >= 0 && index < SortedSentences.Count) ? SortedSentences[index] : null;
        return sentence;
    }

    public Note GetNextSingableNote(double currentBeat)
    {
        Note nextSingableNote = SortedSentences
            .SelectMany(sentence => sentence.Notes)
            // Freestyle notes are not displayed and not sung.
            // They do not contribute to the score.
            .Where(note => !note.IsFreestyle)
            .Where(note => currentBeat <= note.StartBeat)
            .OrderBy(note => note.StartBeat)
            .FirstOrDefault();
        return nextSingableNote;
    }

    private Sentence GetUpcomingSentenceForBeat(double currentBeat)
    {
        Sentence result = Voice.Sentences
            .FirstOrDefault(sentence => currentBeat < sentence.LinebreakBeat);
        return result;
    }

    public Sentence GetDisplaySentence()
    {
        return GetSentence(displaySentenceIndex);
    }

    public Note GetLastNoteInSong()
    {
        if (SortedSentences.IsNullOrEmpty())
        {
            return null;
        }
        return SortedSentences.Last().Notes.OrderBy(note => note.EndBeat).Last();
    }

    public class EnterSentenceEvent
    {
        public Sentence Sentence { get; private set; }
        public int SentenceIndex { get; private set; }

        public EnterSentenceEvent(Sentence sentence, int sentenceIndex)
        {
            Sentence = sentence;
            SentenceIndex = sentenceIndex;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UniInject;
using UniRx;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class ManualMicDelayCalibrationControl : MonoBehaviour, INeedInjection
{
    private static readonly float calibrationTargetTimeInSeconds = 0.5f;
    private static readonly float calibrationMaxTimeInSeconds = 1f;
    private static readonly float micSampleThreshold = 0.3f;
    
    [InjectedInInspector]
    public Button manualCalibrationButton;
    
    [InjectedInInspector]
    public RectTransform manualCalibrationBarPositionIndicator;

    [InjectedInInspector]
    public MicSampleRecorder micSampleRecorder;
    
    private bool isCalibrating;
    
    private float calibrationTimeInSeconds;
    private bool isWaitingForMicSound;
    
	private void Start()
    {
        manualCalibrationButton.OnClickAsObservable().Subscribe(_ => ToggleCalibration());
        micSampleRecorder.RecordingEventStream.Subscribe(evt => OnRecordingEvent(evt));
    }

    private void Update()
    {
        if (isCalibrating)
        {
            calibrationTimeInSeconds += Time.deltaTime;
            if (calibrationTimeInSeconds >= calibrationMaxTimeInSeconds)
            {
                calibrationTimeInSeconds -= calibrationMaxTimeInSeconds;
                // Wait for next mic input
                isWaitingForMicSound = true;
            }

            float calibrationTimePercent = calibrationTimeInSeconds / calibrationMaxTimeInSeconds;
            manualCalibrationBarPositionIndicator.anchorMin = new Vector2(calibrationTimePercent - 0.01f, 0);
            manualCalibrationBarPositionIndicator.anchorMax = new Vector2(calibrationTimePercent + 0.01f, 1);
            manualCalibrationBarPositionIndicator.MoveCornersToAnchors();
        }
    }

    private void ToggleCalibration()
    {
        isCalibrating = !isCalibrating;
        if (isCalibrating)
        {
            manualCalibrationButton.GetComponentInChildren<Text>().text = "Stop Calibration";
        }
        else
        {
            manualCalibrationButton.GetComponentInChildren<Text>().text = "Start Calibration";
        }
    }
    
    private void OnRecordingEvent(RecordingEvent evt)
    {
        if (!isCalibrating
            || !isWaitingForMicSound)
        {
            return;
        }
        
        // Detect start of significant mic input from the player.
        for (int i = evt.NewSamplesStartIndex; i < evt.NewSamplesEndIndex; i++)
        {
            if (evt.MicSamples[i] > micSampleThreshold)
            {
                isWaitingForMicSound = false;
                // Check the distance from calibrationTime to calibrationTargetTime and use this as mic delay.
                float timeDistanceInSeconds = calibrationTimeInSeconds - calibrationTargetTimeInSeconds;
                Debug.Log("timeDistance: " + timeDistanceInSeconds);
                return;
            }
        }
    }
}

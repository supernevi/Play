using UniInject;
using UniRx;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class InGameDebugConsoleManager : AbstractInGameDebugConsoleManager, INeedInjection
{
    public static InGameDebugConsoleManager Instance => DontDestroyOnLoadManager.Instance.FindComponentOrThrow<InGameDebugConsoleManager>();

    [Inject]
    private SceneNavigator sceneNavigator;

    protected override object GetInstance()
    {
        return Instance;
    }

    protected override void StartSingleton()
    {
        base.Init();

        sceneNavigator.SceneChangedEventStream.Subscribe(_ =>
            {
                // The EventSystem may be disabled afterwards because of EventSystemOptInOnAndroid. Thus, update after a frame.
                StartCoroutine(CoroutineUtils.ExecuteAfterDelayInFrames(1, () =>
                {
                    if (debugLogManager.IsLogWindowVisible)
                    {
                        EnableInGameDebugConsoleEventSystemIfNeeded();
                    }
                }));

                UpdateDebugLogPopupVisible();
            })
            .AddTo(gameObject);
    }
}

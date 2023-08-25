using UniInject;
using UniRx;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class InGameDebugConsoleManager : AbstractInGameDebugConsoleManager, INeedInjection
{
    public static InGameDebugConsoleManager Instance => DontDestroyOnLoadManager.Instance.FindComponentOrThrow<InGameDebugConsoleManager>();

    protected override object GetInstance()
    {
        return Instance;
    }

    protected override void StartSingleton()
    {
        base.Init();
    }
}

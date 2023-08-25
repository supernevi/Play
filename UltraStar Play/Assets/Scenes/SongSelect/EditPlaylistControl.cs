using ProTrans;
using UniInject;
using UniRx;
using UnityEngine;
using UnityEngine.UIElements;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class EditPlaylistControl : MonoBehaviour, INeedInjection, ITranslator
{
    [Inject(UxmlName = R.UxmlNames.editPlaylistButton)]
    private Button editPlaylistButton;

    [Inject(UxmlName = R.UxmlNames.createPlaylistButton)]
    private Button createPlaylistButton;

    [Inject(UxmlName = R.UxmlNames.editPlaylistOverlay)]
    private VisualElement editPlaylistOverlay;
    
    [Inject(UxmlName = R.UxmlNames.searchPropertyDropdownOverlay)]
    private VisualElement searchPropertyDropdownOverlay;

    [Inject(UxmlName = R.UxmlNames.playlistChooserDropdownTitle)]
    private Label playlistChooserDropdownTitle;

    [Inject(UxmlName = R.UxmlNames.submitEditPlaylistButton)]
    private Button submitEditPlaylistButton;

    [Inject(UxmlName = R.UxmlNames.deletePlaylistButton)]
    private Button deletePlaylistButton;

    [Inject(UxmlName = R.UxmlNames.confirmDeletePlaylistButton)]
    private Button confirmDeletePlaylistButton;

    [Inject(UxmlName = R.UxmlNames.cancelDeletePlaylistButton)]
    private Button cancelDeletePlaylistButton;

    [Inject(UxmlName = R.UxmlNames.playlistNameTextField)]
    private TextField playlistNameTextField;

    [Inject(UxmlName = R.UxmlNames.editPlaylistDialogTitle)]
    private Label editPlaylistDialogTitle;

    [Inject]
    private SongSelectSceneControl songSelectSceneControl;

    [Inject]
    private PlaylistManager playlistManager;

    [Inject]
    private UiManager uiManager;

    private UltraStarPlaylist currentPlaylist;

    private string titleText;

    private void Start()
    {
        songSelectSceneControl.PlaylistChooserControl.Selection
            .Subscribe(newValue => currentPlaylist = newValue);

        editPlaylistButton.RegisterCallbackButtonTriggered(_ => ShowEditCurrentPlaylistDialog());
        createPlaylistButton.RegisterCallbackButtonTriggered(_ => CreateThenEditNewPlaylist());
        submitEditPlaylistButton.RegisterCallbackButtonTriggered(_ => SubmitEditPlaylistDialog());
        playlistNameTextField.RegisterValueChangedCallback(evt => OnPlaylistNameTextFieldChanged(evt.newValue));

        deletePlaylistButton.RegisterCallbackButtonTriggered(_ =>
        {
            ShowConfirmAndCancelDeleteButtons();
            cancelDeletePlaylistButton.Focus();
        });
        cancelDeletePlaylistButton.RegisterCallbackButtonTriggered(_ =>
        {
            ShowDeleteAndSubmitButtons();
            submitEditPlaylistButton.Focus();
        });
        confirmDeletePlaylistButton.RegisterCallbackButtonTriggered(_ =>
        {
            string errorMessage = playlistManager.TryRemovePlaylist(currentPlaylist);
            if (!errorMessage.IsNullOrEmpty())
            {
                Debug.LogError(errorMessage);
                UiManager.CreateNotification(errorMessage);
            }
            HideEditPlaylistDialog();
        });
    }

    private void CreateThenEditNewPlaylist()
    {
        UltraStarPlaylist newPlaylist = playlistManager.CreateNewPlaylist("New Playlist");
        songSelectSceneControl.PlaylistChooserControl.Selection.Value = newPlaylist;
        ShowEditCurrentPlaylistDialog();
    }

    private void OnPlaylistNameTextFieldChanged(string newPlaylistName)
    {
        EPlaylistNameIssue playlistNameIssue = playlistManager.GetPlaylistNameIssue(currentPlaylist, newPlaylistName);
        switch (playlistNameIssue)
        {
            case EPlaylistNameIssue.Invalid:
                editPlaylistDialogTitle.text = "Invalid playlist name";
                submitEditPlaylistButton.text = TranslationManager.GetTranslation(R.Messages.cancel);
                break;
            case EPlaylistNameIssue.Duplicate:
                editPlaylistDialogTitle.text = "Duplicate playlist name";
                submitEditPlaylistButton.text = TranslationManager.GetTranslation(R.Messages.cancel);
                break;
            default:
                editPlaylistDialogTitle.text = titleText;
                submitEditPlaylistButton.text = TranslationManager.GetTranslation(R.Messages.continue_);
                break;
        }
    }

    public void HideEditPlaylistDialog()
    {
        editPlaylistOverlay.HideByDisplay();
    }

    public void ShowEditCurrentPlaylistDialog()
    {
        if (currentPlaylist == null
            || currentPlaylist is UltraStarAllSongsPlaylist
            || playlistManager.IsFavoritesPlaylist(currentPlaylist))
        {
            return;
        }

        titleText = "Edit Playlist";
        editPlaylistDialogTitle.text = titleText;
        playlistNameTextField.value = currentPlaylist.Name;
        editPlaylistOverlay.ShowByDisplay();
        searchPropertyDropdownOverlay.HideByDisplay();

        ShowDeleteAndSubmitButtons();
    }

    private void ShowDeleteAndSubmitButtons()
    {
        deletePlaylistButton.ShowByDisplay();
        submitEditPlaylistButton.ShowByDisplay();
        confirmDeletePlaylistButton.HideByDisplay();
        cancelDeletePlaylistButton.HideByDisplay();
        submitEditPlaylistButton.Focus();
    }

    private void ShowConfirmAndCancelDeleteButtons()
    {
        deletePlaylistButton.HideByDisplay();
        submitEditPlaylistButton.HideByDisplay();
        confirmDeletePlaylistButton.ShowByDisplay();
        cancelDeletePlaylistButton.ShowByDisplay();
        cancelDeletePlaylistButton.Focus();
    }

    private void SubmitEditPlaylistDialog()
    {
        string newPlaylistName = playlistNameTextField.value;
        if (playlistManager.GetPlaylistNameIssue(currentPlaylist, newPlaylistName) != EPlaylistNameIssue.None)
        {
            // Submit works as cancel button
            HideEditPlaylistDialog();
            return;
        }

        // Try to rename playlist
        if (!playlistManager.TrySetPlaylistName(currentPlaylist, newPlaylistName, out string errorMessage))
        {
            // Show error in UI
            Debug.LogError(errorMessage);
            UiManager.CreateNotification(errorMessage);
        }
        HideEditPlaylistDialog();
    }

    public void UpdateTranslation()
    {
        playlistChooserDropdownTitle.text = TranslationManager.GetTranslation(R.Messages.songSelectScene_playlistDropdownTitle);
    }
}

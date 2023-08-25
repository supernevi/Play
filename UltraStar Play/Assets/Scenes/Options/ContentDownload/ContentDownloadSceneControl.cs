﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using ProTrans;
using UniInject;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using static ThreadPool;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class ContentDownloadSceneControl : AbstractOptionsSceneControl, INeedInjection, ITranslator
{
    [InjectedInInspector]
    public TextAsset songArchiveEntryTextAsset;

    [InjectedInInspector]
    public VisualTreeAsset dialogUi;

    [Inject]
    private UIDocument uiDocument;

    [Inject(UxmlName = R.UxmlNames.statusLabel)]
    private Label statusLabel;

    [Inject(UxmlName = R.UxmlNames.outputTextField)]
    private TextField logText;

    [Inject(UxmlName = R.UxmlNames.urlLabel)]
    private Label urlLabel;

    [Inject(UxmlName = R.UxmlNames.urlTextField)]
    private TextField downloadPath;

    [Inject(UxmlName = R.UxmlNames.sizeLabel)]
    private Label fileSize;

    [Inject(UxmlName = R.UxmlNames.startButton)]
    private Button startDownloadButton;

    [Inject(UxmlName = R.UxmlNames.cancelButton)]
    private Button cancelDownloadButton;

    [Inject(UxmlName = R.UxmlNames.urlChooserButton)]
    private Button urlChooserButton;

    [Inject]
    private SettingsManager settingsManager;
    
    [Inject]
    private SongMetaManager songMetaManager;

    [Inject]
    private Injector injector;

    [Inject]
    private UiManager uiManager;

    private MessageDialogControl urlChooserDialogControl;

    private string DownloadUrl => downloadPath.value.Trim();

    private UnityWebRequest downloadRequest;

    private List<SongArchiveEntry> songArchiveEntries = new();

    // The Ui log may only be filled from the main thread.
    // This string caches new log lines for other threads.
    private string newLogText;

    private int extractedEntryCount;
    private bool hasFileSize;

    protected override void Start()
    {
        base.Start();

        songArchiveEntries = JsonConverter.FromJson<List<SongArchiveEntry>>(songArchiveEntryTextAsset.text);

        statusLabel.text = "";
        SelectSongArchiveUrl(songArchiveEntries[0].Url);

        startDownloadButton.RegisterCallbackButtonTriggered(_ => StartDownload());
        cancelDownloadButton.RegisterCallbackButtonTriggered(_ => CancelDownload());
        downloadPath.RegisterValueChangedCallback(evt => FetchFileSize());
        if (!DownloadUrl.IsNullOrEmpty())
        {
            FetchFileSize();
        }
        
        urlChooserButton.RegisterCallbackButtonTriggered(_ => ShowUrlChooserDialog());
    }

    private void ShowUrlChooserDialog()
    {
        if (urlChooserDialogControl != null)
        {
            return;
        }

        VisualElement dialog = dialogUi.CloneTree().Children().FirstOrDefault();
        uiDocument.rootVisualElement.Add(dialog);

        urlChooserDialogControl = injector
            .WithRootVisualElement(dialog)
            .CreateAndInject<MessageDialogControl>();

        urlChooserDialogControl.Title = TranslationManager.GetTranslation(R.Messages.contentDownloadScene_archiveUrlLabel);

        // Create a button in the dialog for every archive URL
        songArchiveEntries.ForEach(songArchiveEntry =>
        {
            Button songArchiveUrlButton = new();
            songArchiveUrlButton.AddToClassList("songArchiveUrlButton");
            songArchiveUrlButton.text = songArchiveEntry.Url;
            songArchiveUrlButton.RegisterCallbackButtonTriggered(_ =>
            {
                SelectSongArchiveUrl(songArchiveEntry.Url);
                CloseUrlChooserDialog();
            });
            songArchiveUrlButton.style.height = new StyleLength(StyleKeyword.Auto);
            urlChooserDialogControl.AddVisualElement(songArchiveUrlButton);

            Label songArchiveInfoLabel = new(songArchiveEntry.Description);
            songArchiveInfoLabel.AddToClassList("songArchiveInfoLabel");
            urlChooserDialogControl.AddVisualElement(songArchiveInfoLabel);
        });
        
        ThemeManager.ApplyThemeSpecificStylesToVisualElements(dialog);
    }

    private void CloseUrlChooserDialog()
    {
        if (urlChooserDialogControl == null)
        {
            return;
        }

        urlChooserDialogControl.CloseDialog();
        urlChooserDialogControl = null;
    }

    private void SelectSongArchiveUrl(string url)
    {
        downloadPath.value = url;
    }

    public void UpdateTranslation()
    {
        urlLabel.text = TranslationManager.GetTranslation(R.Messages.contentDownloadScene_archiveUrlLabel);
        startDownloadButton.text = TranslationManager.GetTranslation(R.Messages.contentDownloadScene_startDownloadButton);
        cancelDownloadButton.text = TranslationManager.GetTranslation(R.Messages.contentDownloadScene_cancelDownloadButton);
    }

    void Update()
    {
        if (downloadRequest != null
            && (downloadRequest.isDone || !downloadRequest.error.IsNullOrEmpty()))
        {
            Debug.Log("Disposing downloadRequest");
            downloadRequest.Dispose();
            downloadRequest = null;
        }

        if (!newLogText.IsNullOrEmpty())
        {
            logText.value = newLogText + logText.value;
            newLogText = "";
        }
    }

    private string GetDownloadTargetPath(string url)
    {
        Uri uri = new(url);
        string filename = Path.GetFileName(uri.LocalPath);
        string targetPath = ApplicationManager.PersistentTempPath() + "/" + filename;
        return targetPath;
    }

    private DownloadHandler CreateDownloadHandler(string targetPath)
    {
        DownloadHandlerFile downloadHandler = new(targetPath);
        downloadHandler.removeFileOnAbort = true;
        return downloadHandler;
    }

    private void StartDownload()
    {
        StartCoroutine(DownloadFileAsync(DownloadUrl));
    }

    private void CancelDownload()
    {
        if (downloadRequest != null && !downloadRequest.isDone)
        {
            Debug.Log("Aborting download");
            downloadRequest.Abort();
            AddToUiLog("Canceled download");
            SetCanceledStatus();
        }
    }

    private void FetchFileSize()
    {
        StartCoroutine(FileSizeUpdateAsync(DownloadUrl));
    }

    private IEnumerator DownloadFileAsync(string url)
    {
        if (downloadRequest != null
            && !downloadRequest.isDone)
        {
            AddToUiLog("Download in progress. Cancel the other download first.");
            yield break;
        }

        if (url.IsNullOrEmpty())
        {
            yield break;
        }

        Debug.Log($"Started download: {url}");
        AddToUiLog($"Started download");

        string targetPath = GetDownloadTargetPath(url);
        DownloadHandler downloadHandler = CreateDownloadHandler(targetPath);

        downloadRequest = UnityWebRequest.Get(url);
        downloadRequest.downloadHandler = downloadHandler;

        StartCoroutine(TrackProgressAsync());

        yield return downloadRequest?.SendWebRequest();

        if (downloadRequest is { result: UnityWebRequest.Result.ConnectionError
                                      or UnityWebRequest.Result.ProtocolError })
        {
            Debug.LogError($"Error downloading {url}: {downloadRequest.error}");
            AddToUiLog("Error downloading the requested file");
        }
        else if (downloadRequest != null)
        {
            statusLabel.text = "100%";
            AddToUiLog($"Download saved to {targetPath}. {downloadRequest.error}");
            UnpackArchive(targetPath);
        }
    }

    private IEnumerator TrackProgressAsync()
    {
        while (downloadRequest != null
            && downloadRequest.downloadHandler != null
            && !downloadRequest.downloadHandler.isDone)
        {
            string progressText;
            try
            {
                if (hasFileSize)
                {
                    progressText = Math.Round(downloadRequest.downloadProgress * 100) + "%";
                }
                else
                {
                    progressText = ByteSizeUtils.GetHumanReadableByteSize((long)downloadRequest.downloadedBytes);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                AddToUiLog(ex.Message);
                statusLabel.text = "?";
                yield break;
            }

            statusLabel.text = progressText;
            yield return new WaitForSeconds(0.1f);
        }
    }

    private void AddToDebugAndUiLog(string message, bool isError = false)
    {
        if (isError)
        {
            Debug.LogError(message);
        }
        else
        {
            Debug.Log(message);
        }
        AddToUiLog(message);
    }

    private void AddToUiLog(string message)
    {
        newLogText += message + "\n";
    }

    private IEnumerator FileSizeUpdateAsync(string url)
    {
        if (url.IsNullOrEmpty())
        {
            // Do not continue with the coroutine
            ResetFileSizeText();
            yield break;
        }

        using UnityWebRequest request = UnityWebRequest.Head(url);
        yield return request.SendWebRequest();

        if (request.result
            is UnityWebRequest.Result.ConnectionError
            or UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"Error fetching size: {request.error}");
            AddToUiLog($"Error fetching size: {request.error}");
            ResetFileSizeText();
        }
        else
        {
            string contentLength = request.GetResponseHeader("Content-Length");
            if (contentLength.IsNullOrEmpty())
            {
                ResetFileSizeText();
            }
            else
            {
                long size = Convert.ToInt64(contentLength);
                fileSize.text = ByteSizeUtils.GetHumanReadableByteSize(size);
                hasFileSize = true;
            }
        }
    }

    private void UnpackArchive(string archivePath)
    {
        if (downloadRequest == null)
        {
            return;
        }

        if (!File.Exists(archivePath))
        {
            AddToDebugAndUiLog("Can not unpack file because it does not exist on the storage! Did the download fail?", true);
            return;
        }

        AddToDebugAndUiLog("Preparing to unpack the downloaded song package.");
        string songsPath = ApplicationManager.PersistentSongsPath();
        PoolHandle handle = QueueUserWorkItem(poolHandle =>
        {
            if (archivePath.ToLowerInvariant().EndsWith("tar"))
            {
                ExtractTarArchive(archivePath, songsPath, poolHandle);
            }
            else if (archivePath.ToLowerInvariant().EndsWith("zip"))
            {
                ExtractZipArchive(archivePath, songsPath, poolHandle);
            }
        });
        StartCoroutine(TrackUnpackAsync(handle, archivePath, songsPath));
    }

    private void ExtractZipArchive(string archivePath, string targetFolder, PoolHandle poolHandle)
    {
        using Stream archiveStream = File.OpenRead(archivePath);
        using ZipFile zipFile = new(archiveStream);
        extractedEntryCount = 0;

        try
        {
            foreach (ZipEntry entry in zipFile)
            {
                ExtractZipEntry(zipFile, entry, targetFolder);
            }

            poolHandle.done = true;
        }
        catch (Exception ex)
        {
            AddToDebugAndUiLog($"Unpacking failed: {ex.Message}");
            SetErrorStatus();
        }
    }

    private void ExtractZipEntry(ZipFile zipFile, ZipEntry zipEntry, string targetFolder)
    {
        string entryPath = zipEntry.Name;
        if (!zipEntry.IsFile)
        {
            string targetSubFolder = targetFolder + "/" + zipEntry.Name;
            Directory.CreateDirectory(targetSubFolder);
            return;
        }

        Debug.Log($"Extracting {entryPath}");
        string targetFilePath = targetFolder + "/" + entryPath;

        byte[] buffer = new byte[4096];
        using Stream zipEntryStream = zipFile.GetInputStream(zipEntry);
        using FileStream targetFileStream = File.Create(targetFilePath);
        StreamUtils.Copy(zipEntryStream, targetFileStream, buffer);
        extractedEntryCount++;
    }

    private void ExtractTarArchive(string archivePath, string targetFolder, PoolHandle poolHandle)
    {
        using Stream archiveStream = File.OpenRead(archivePath);
        using TarArchive archive = TarArchive.CreateInputTarArchive(archiveStream, Encoding.UTF8);

        try
        {
            archive.ExtractContents(targetFolder);
            poolHandle.done = true;
        }
        catch (Exception ex)
        {
            AddToDebugAndUiLog($"Unpacking failed: {ex.Message}");
            SetErrorStatus();
        }
    }

    private IEnumerator TrackUnpackAsync(PoolHandle handle, string archivePath, string songsPath)
    {
        if (downloadRequest == null)
        {
            yield break;
        }

        string progress1 = "Unpacking file.  ";
        string progress2 = "Unpacking file.. ";
        string progress3 = "Unpacking file...";

        string GetStatusLabelProgress(string prefix)
        {
            return prefix + (extractedEntryCount > 0 ? $" {extractedEntryCount}" : "");
        }

        while (handle != null && !handle.done)
        {
            statusLabel.text = GetStatusLabelProgress(progress1);
            yield return new WaitForSeconds(0.5f);
            if (handle == null || handle.done)
            {
                break;
            }
            statusLabel.text = GetStatusLabelProgress(progress2);
            yield return new WaitForSeconds(0.5f);
            if (handle == null || handle.done)
            {
                break;
            }
            statusLabel.text = GetStatusLabelProgress(progress3);
            yield return new WaitForSeconds(0.5f);
        }

        if (handle == null)
        {
            AddToDebugAndUiLog($"Unpacking the song package failed. Handle was null for {archivePath}.", true);
            statusLabel.text = "Failed. Handle was null";

        }
        else if (handle.done)
        {
            AddToDebugAndUiLog($"Finished unpacking the song package to {songsPath}");
            SetFinishedStatus();
            downloadPath.value = "";
            List<string> songDirs = settings.GameSettings.songDirs;
            if (!songDirs.Contains(songsPath))
            {
                songDirs.Add(songsPath);
                settingsManager.Save();
            }
            // Reload SongMetas if they had been loaded already.
            if (SongMetaManager.IsSongScanFinished)
            {
                Debug.Log("Rescan songs after successful download.");
                SongMetaManager.ResetSongMetas();
                songMetaManager.ScanFilesIfNotDoneYet();
            }
        }
        else
        {
            AddToDebugAndUiLog($"Unpacking the song package failed with an unknown error. Please check the log. File: {archivePath}", true);
            statusLabel.text = "Failed";
        }
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }
    }

    private void SetFinishedStatus()
    {
        statusLabel.text = TranslationManager.GetTranslation(R.Messages.contentDownloadScene_status_finished);
    }

    private void SetErrorStatus()
    {
        statusLabel.text = TranslationManager.GetTranslation(R.Messages.contentDownloadScene_status_failed);
    }

    private void SetCanceledStatus()
    {
        statusLabel.text = TranslationManager.GetTranslation(R.Messages.contentDownloadScene_status_canceled);
    }

    private void ResetFileSizeText()
    {
        fileSize.text = "??? KB";
        hasFileSize = false;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        
        if (downloadRequest != null)
        {
            downloadRequest.Dispose();
        }
    }

    public override bool HasHelpDialog => true;
    public override MessageDialogControl CreateHelpDialogControl()
    {
        Dictionary<string, string> titleToContentMap = new()
        {
            { TranslationManager.GetTranslation(R.Messages.contentDownloadScene_helpDialog_demoSongPackage_title),
                TranslationManager.GetTranslation(R.Messages.contentDownloadScene_helpDialog_demoSongPackage) },
            { TranslationManager.GetTranslation(R.Messages.contentDownloadScene_helpDialog_archiveDownload_title),
                TranslationManager.GetTranslation(R.Messages.contentDownloadScene_helpDialog_archiveDownload) },
            { TranslationManager.GetTranslation(R.Messages.contentDownloadScene_helpDialog_thirdPartyDownloads_title),
                TranslationManager.GetTranslation(R.Messages.contentDownloadScene_helpDialog_thirdPartyDownloads) },
        };
        MessageDialogControl helpDialogControl = uiManager.CreateHelpDialogControl(
            TranslationManager.GetTranslation(R.Messages.contentDownloadScene_helpDialog_title),
            titleToContentMap);
        helpDialogControl.AddButton(TranslationManager.GetTranslation(R.Messages.viewMore),
            _ => Application.OpenURL(TranslationManager.GetTranslation(R.Messages.uri_howToDownloadSongs)));
        return helpDialogControl;
    }
}

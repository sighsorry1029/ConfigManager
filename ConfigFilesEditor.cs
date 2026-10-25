using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static ConfigurationManager.ConfigurationManagerStyles;
using static ConfigurationManager.ConfigurationManager;
using BepInEx;
using System;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;

namespace ConfigurationManager
{
    public class ConfigFilesEditor : IDisposable
    {
        private enum FileEditState
        {
            None,
            CreatingFolder,
            CreatingFile,
            RenamingFile
        }

        private readonly static string _trashBinDirectory = Path.Combine(Paths.CachePath, "ConfigManagerTrashBin");
        private readonly static string[] _directories = { Paths.ConfigPath, Paths.PluginPath, _trashBinDirectory };
        private readonly static HashSet<string> _editableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json",
            ".yaml",
            ".yml",
            ".cfg"
        };

        private readonly Dictionary<string, bool> _folderStates = new Dictionary<string, bool>();

        private Vector2 _scrollPosition;
        private string _fileContent;
        private string _originalFileContent;
        private Vector2 _textScrollPosition;

        private Rect _windowRect = new Rect(_windowPositionTextEditor.Value, _windowSizeTextEditor.Value);
        private bool _windowGeometryDirty;
        private bool _windowGeometrySaveFailed;
        private Vector2? _pendingWindowSize;

        private const int WindowId = -680;

        private const string SearchBoxName = "searchBoxEditor";
        private const int DirectoryOffset = 20;
        private const string TextEditorControlName = "textEditorTextField";
        private bool _focusSearchBox;
        private bool _focusTextArea;
        private string _searchString;
        private string _errorText;
        private string _activeFile;
        private string _activeDirectory;
        private string _newItemName;
        private string _newItemErrorText;

        private FileEditState _fileNameState;

        private bool _isOpen;
        private bool _fileTreeDirty;
        private bool _watchingEnabled = true;

        private void SetFileEditState(FileEditState newState)
        {
            _fileNameState = newState;
            _newItemErrorText = string.Empty;
            _newItemName = string.Empty;
        }

        private string SearchString
        {
            get => _searchString;
            set
            {
                if (value == null)
                    value = string.Empty;

                _searchString = value;
            }
        }

        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen == value) return;
                if (!value) FlushWindowGeometry();
                _isOpen = value;
                InvalidateFileTree();
                UpdateWatcherState();
            }
        }

        internal void ResetWindowGeometry()
        {
            _windowRect = new Rect(_windowPositionTextEditor.Value, _windowSizeTextEditor.Value);
            _windowGeometryDirty = false;
            _windowGeometrySaveFailed = false;
            _pendingWindowSize = null;
        }

        internal void FlushWindowGeometry()
        {
            if (!_windowGeometryDirty) return;
            try { SaveCurrentSizeAndPosition(); }
            catch (Exception ex)
            {
                _windowGeometrySaveFailed = true;
                LogWarning("Could not save file editor geometry: " + ex.Message);
            }
        }

        public void OnGUI()
        {
            if (!IsOpen)
                return;

            if (!_windowGeometryDirty)
            {
                _windowRect.size = _windowSizeTextEditor.Value;
                _windowRect.position = _windowPositionTextEditor.Value;
            }

            Color color = GUI.backgroundColor;
            GUI.backgroundColor = Palette.WindowBackground;

            Rect drawnWindow = GUI.Window(WindowId, _windowRect, DrawWindow,
                _activeFile.IsNullOrWhiteSpace()
                    ? UiText.WindowTitleTextEditor
                    : "..." + _activeFile.Replace(Path.GetDirectoryName(Paths.BepInExRootPath) ?? string.Empty, ""), GetWindowStyle());

            if (_pendingWindowSize.HasValue)
            {
                drawnWindow.size = _pendingWindowSize.Value;
                _pendingWindowSize = null;
            }
            _windowRect = drawnWindow;
            if (_windowRect.position != _windowPositionTextEditor.Value)
                _windowGeometryDirty = true;
            bool mouseHeld = UnityInput.Current.GetMouseButton(0);
            if (mouseHeld) _windowGeometrySaveFailed = false;
            if (_windowGeometryDirty && !mouseHeld && !_windowGeometrySaveFailed)
                FlushWindowGeometry();

            GUI.backgroundColor = color;
        }

        internal void SaveCurrentSizeAndPosition()
        {
            var size = new Vector2(Mathf.Clamp(_windowRect.size.x, 1000f / instance.ScaleFactor, instance.ScreenWidth), Mathf.Clamp(_windowRect.size.y, 600f / instance.ScaleFactor, instance.ScreenHeight));
            var position = new Vector2(Mathf.Clamp(_windowRect.position.x, 0f, instance.ScreenWidth - _windowSize.Value.x / 4f), Mathf.Clamp(_windowRect.position.y, 0f, instance.ScreenHeight - HeaderSize * 2));
            SaveWindowConfigChanges(instance.Config, () =>
            {
                _windowSizeTextEditor.Value = size;
                _windowPositionTextEditor.Value = position;
            });
            _windowRect = new Rect(position, size);
            _windowGeometryDirty = false;
            _windowGeometrySaveFailed = false;
            _pendingWindowSize = null;
        }

        private void DrawSearchBox()
        {
            GUILayout.BeginHorizontal();
            {
                GUILayout.Label(UiText.SearchTextEditor, GetLabelStyle(), GUILayout.Width(GetLabelStyle().CalcSize(new GUIContent(UiText.SearchTextEditor)).x + 4));

                GUI.SetNextControlName(SearchBoxName);
                SearchString = GUILayout.TextField(SearchString, GetTextStyle(), GUILayout.ExpandWidth(true));

                if (_focusSearchBox)
                {
                    GUI.FocusWindow(WindowId);
                    GUI.FocusControl(SearchBoxName);
                    _focusSearchBox = false;
                }
                Color color = GUI.backgroundColor;
                GUI.backgroundColor = Palette.Widget;
                if (GUILayout.Button(UiText.ClearText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    SearchString = string.Empty;
                GUI.backgroundColor = color;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawContentButtons()
        {
            GUILayout.BeginHorizontal();
            {
                bool fileIsActive = !_activeFile.IsNullOrWhiteSpace();
                GUI.enabled = fileIsActive && _fileContent != _originalFileContent;

                if (GUILayout.Button(UiText.SaveFileTextEditor, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    SaveActiveFile();

                GUI.enabled = true;

                GUILayout.Label(_errorText, GetLabelStyle(isDefaultValue:false), GUILayout.ExpandWidth(true));

                GUILayout.Label(UiText.FontSizeTextEditor, GetLabelStyle(), GUILayout.ExpandWidth(false));
                if (int.TryParse(GUILayout.TextField(_textEditorFontSize.Value.ToFastString(), GetTextStyle(_textEditorFontSize.Value, (int)_textEditorFontSize.DefaultValue), GUILayout.Width(30)), out int fontSize))
                    _textEditorFontSize.Value = Mathf.Clamp(fontSize, 8, 36);

                _textEditorWordWrap.Value = GUILayout.Toggle(_textEditorWordWrap.Value, UiText.WordWrapTextEditor, GetToggleStyle(), GUILayout.ExpandWidth(false));
                if (GUILayout.Button(UiText.CloseText, GetButtonStyle(), GUILayout.ExpandWidth(false)))
                    IsOpen = false;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawWindow(int windowID)
        {
            GUILayout.BeginHorizontal();
            {
                var backgroundColor = GUI.backgroundColor;
                GUI.backgroundColor = Palette.EntryBackground;

                // Tree
                GUILayout.BeginVertical(GetBackgroundStyle(), GUILayout.MaxWidth(GetFileListWidth()));
                {
                    DrawSearchBox();

                    _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Width(_windowRect.width * 0.3f));
                    _directoryDepth = 0;
                    PrepareFileTreeDraw();
                    DrawDirectories(_directories);
                    GUILayout.EndScrollView();
                    DrawDirectoriesMenu();
                }
                GUILayout.EndVertical();

                // Content
                GUILayout.BeginVertical(GetBackgroundStyle(), GUILayout.MaxWidth(_windowRect.width * 0.7f));
                {
                    DrawContentButtons();

                    _textScrollPosition = GUILayout.BeginScrollView(_textScrollPosition);

                    GUI.enabled = File.Exists(_activeFile);

                    GUI.SetNextControlName(TextEditorControlName);
                    _fileContent = GUILayout.TextArea(_fileContent, GetFileEditorTextArea(), GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));

                    if (_focusTextArea || GUI.GetNameOfFocusedControl() == TextEditorControlName)
                    {
                        GUI.FocusWindow(WindowId);
                        GUI.FocusControl(TextEditorControlName);
                        _focusTextArea = false;
                    }

                    GUI.enabled = true;
                    GUILayout.EndScrollView();

                    if (_showFullName.Value)
                        GUILayout.TextField(_activeFile, GetTextStyle(), GUILayout.ExpandWidth(true));
                }
                GUILayout.EndVertical();

                GUI.backgroundColor = backgroundColor;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, HeaderSize));

            if (!SettingFieldDrawer.DrawCurrentDropdown())
                DrawTooltip(_windowRect);

            _windowRect = Utilities.Utils.ResizeWindow(windowID, _windowRect, out bool sizeChanged);

            if (sizeChanged)
            {
                _windowRect.size = new Vector2(Mathf.Clamp(_windowRect.width, 1000f / instance.ScaleFactor, instance.ScreenWidth),
                    Mathf.Clamp(_windowRect.height, 600f / instance.ScaleFactor, instance.ScreenHeight));
                _pendingWindowSize = _windowRect.size;
                _windowGeometryDirty = true;
            }
        }

        private float GetFileListWidth() => _windowRect.width * 0.3f;

        private void DrawDirectory(string path)
        {
            _directoryDepth++;

            DrawDirectories(GetDirectories(path));

            if (path == _activeDirectory && _fileNameState == FileEditState.CreatingFolder)
                DrawFileNameField();

            DrawFiles(path);

            _directoryDepth--;
        }

        private int _directoryDepth;

        private void DrawDirectories(IEnumerable<string> directories)
        {
            foreach (string directory in directories)
            {
                if (!_showTrashBin.Value && directory == _trashBinDirectory)
                    continue;

                if (!_showEmptyFolders.Value && !DirectoryContainsValidFiles(directory))
                    continue;

                if (!_folderStates.ContainsKey(directory))
                    _folderStates[directory] = false;

                if (_folderStates[directory] != (_folderStates[directory] = GUILayout.Toggle(_folderStates[directory], Path.GetFileName(directory), GetDirectoryStyle(directory == _activeDirectory))) && _folderStates[directory])
                {
                    _activeDirectory = directory;
                    SetFileEditState(FileEditState.None);
                }

                if (_folderStates[directory])
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(DirectoryOffset);

                    GUILayout.BeginVertical();
                    DrawDirectory(directory);
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                }
            }
        }

        private void DrawFileNameField()
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal(GUILayout.MaxWidth(GetFileListWidth() - DirectoryOffset * (_directoryDepth + 1) - 5));

            if (_fileNameState == FileEditState.CreatingFolder || _fileNameState == FileEditState.CreatingFile)
                GUILayout.Label(_fileNameState == FileEditState.CreatingFolder ? UiText.NewFolderLabelTextEditor : UiText.NewFileLabelTextEditor, GetLabelStyle(), GUILayout.ExpandWidth(false));

            _newItemName = GUILayout.TextField(_newItemName, GetFileNameFieldStyle(), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(UiText.NewEntryOKButtonTextEditor, GetButtonStyle(), GUILayout.ExpandWidth(false)))
            {
                if (_fileNameState == FileEditState.CreatingFolder || _fileNameState == FileEditState.CreatingFile)
                    CreateNewItem(_newItemName, _fileNameState == FileEditState.CreatingFolder);
                else if (_fileNameState == FileEditState.RenamingFile)
                    RenameActiveFile();
            }

            GUILayout.EndHorizontal();

            if (!_newItemErrorText.IsNullOrWhiteSpace())
                GUILayout.Label(_newItemErrorText, GetFileNameErrorStyle(), GUILayout.ExpandWidth(true));

            GUILayout.EndVertical();
        }

        private void RenameActiveFile()
        {
            if (_newItemName == Path.GetFileName(_activeFile))
            {
                SetFileEditState(FileEditState.None);
                return;
            }

            string directory = Path.GetDirectoryName(_activeFile);

            if (directory == null)
                return;

            if (!TryGetSafeChildPath(directory, _newItemName, out string newPath))
            {
                _newItemErrorText = UiText.InvalidFileNameTextEditor;
                return;
            }

            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                _newItemErrorText = UiText.FileExistsTextEditor;
                return;
            }

            try
            {
                File.Move(_activeFile, newPath);
                InvalidateFileTree();
                _activeFile = newPath;
                SetFileEditState(FileEditState.None);
            }
            catch (Exception e)
            {
                _newItemErrorText = e.Message;
            }
        }

        private void MoveActiveFileToTrash()
        {
            if (_activeFile.IsNullOrWhiteSpace())
                return;

            try
            {
                Directory.CreateDirectory(_trashBinDirectory);
                string filename = Path.Combine(_trashBinDirectory, Path.GetFileName(_activeFile));
                if (File.Exists(filename))
                    filename = Path.Combine(_trashBinDirectory, $"{Path.GetFileNameWithoutExtension(filename)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{Path.GetExtension(filename)}");

                File.Move(_activeFile, filename);
                InvalidateFileTree();

                _activeFile = string.Empty;
                _fileContent = string.Empty;
                _originalFileContent = string.Empty;
                SetFileEditState(FileEditState.None);
            }
            catch (Exception e)
            {
                _newItemErrorText = e.Message;
            }
        }

        private readonly Dictionary<string, string[]> _cachedFileTree = new Dictionary<string, string[]>();
        private readonly Dictionary<string, string[]> _cachedDirectories = new Dictionary<string, string[]>();
        private readonly Dictionary<string, bool> _validFilesThisDraw = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _validDirectoriesThisDraw = new Dictionary<string, bool>();
        // Match the original config-path comparison (string ==), including its case policy.
        private readonly HashSet<string> _pluginConfigPaths = new HashSet<string>(StringComparer.Ordinal);
        private FileSystemWatcher[] _watchers = Array.Empty<FileSystemWatcher>();

        public ConfigFilesEditor()
        {
            InitializeFileWatcher();
        }

        private void InitializeFileWatcher()
        {
            _watchers = new[] { new FileSystemWatcher(Paths.ConfigPath), new FileSystemWatcher(Paths.PluginPath) };

            foreach (var watcher in _watchers)
            {
                watcher.IncludeSubdirectories = true;
                watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;

                watcher.Changed += (_, __) => InvalidateFileTree();
                watcher.Created += (_, __) => InvalidateFileTree();
                watcher.Deleted += (_, __) => InvalidateFileTree();
                watcher.Renamed += (_, __) => InvalidateFileTree();
            }
        }

        private void InvalidateFileTree()
        {
            _fileTreeDirty = true;
        }

        internal void SetWatching(bool enabled)
        {
            _watchingEnabled = enabled;
            // A change while disabled cannot be reported by the watchers.
            InvalidateFileTree();
            UpdateWatcherState();
        }

        private void UpdateWatcherState()
        {
            foreach (FileSystemWatcher watcher in _watchers)
                watcher.EnableRaisingEvents = _watchingEnabled && IsOpen;
        }

        private void ClearInvalidatedFileTree()
        {
            if (!_fileTreeDirty)
                return;

            _cachedFileTree.Clear();
            _cachedDirectories.Clear();
            _validFilesThisDraw.Clear();
            _validDirectoriesThisDraw.Clear();
            _fileTreeDirty = false;
        }

        private void PrepareFileTreeDraw()
        {
            ClearInvalidatedFileTree();
            _validFilesThisDraw.Clear();
            _validDirectoriesThisDraw.Clear();
            _pluginConfigPaths.Clear();
            foreach (var plugin in Chainloader.PluginInfos.Values)
                if (plugin.Instance?.Config?.ConfigFilePath is string configPath)
                    _pluginConfigPaths.Add(configPath);
        }

        private string[] GetFiles(string path)
        {
            ClearInvalidatedFileTree();

            if (_cachedFileTree.TryGetValue(path, out var cachedFiles))
                return cachedFiles;

            string[] files;
            try
            {
                files = Directory.Exists(path) ? Directory.GetFiles(path) : Array.Empty<string>();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                LogWarning($"Could not read files from {path}: {e.Message}");
                files = Array.Empty<string>();
            }
            _cachedFileTree[path] = files;
            return files;
        }

        private string[] GetDirectories(string path)
        {
            ClearInvalidatedFileTree();

            if (_cachedDirectories.TryGetValue(path, out var cachedDirs))
                return cachedDirs;

            string[] directories;
            try
            {
                directories = Directory.Exists(path)
                    ? Directory.GetDirectories(path).Where(directory => (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0).ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                LogWarning($"Could not read directories from {path}: {e.Message}");
                directories = Array.Empty<string>();
            }
            _cachedDirectories[path] = directories;
            return directories;
        }

        private void DrawFiles(string path)
        {
            bool newFileDrawed = false;
            foreach (string file in GetFiles(path))
                if (IsValidFile(file))
                {
                    if (file == _activeFile && _fileNameState == FileEditState.RenamingFile)
                        DrawFileNameField();
                    else if (GUILayout.Button(Path.GetFileName(file), GetFileStyle(file == _activeFile)))
                        LoadFileToEditor(file);

                    if (path == _activeDirectory && file == _activeFile && _fileNameState == FileEditState.CreatingFile)
                    {
                        DrawFileNameField();
                        newFileDrawed = true;
                    }
                }

            if (!newFileDrawed && path == _activeDirectory && _fileNameState == FileEditState.CreatingFile)
                DrawFileNameField();
        }

        private bool IsValidFile(string file)
        {
            ClearInvalidatedFileTree();
            if (_validFilesThisDraw.TryGetValue(file, out bool valid))
                return valid;

            valid = EvaluateFileVisibility(file);
            _validFilesThisDraw[file] = valid;
            return valid;
        }

        private bool EvaluateFileVisibility(string file)
        {
            string filename = Path.GetFileName(file);
            if (filename == "manifest.json")
                return false;

            try
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }

            if (string.Equals(filename, hiddenSettingsFileName, StringComparison.OrdinalIgnoreCase))
                return SearchString.IsNullOrWhiteSpace() || filename.IndexOf(SearchString, StringComparison.OrdinalIgnoreCase) > -1;

            if (_pluginConfigPaths.Contains(file))
                return false;

            string extension = Path.GetExtension(file);
            if (_editableExtensions.Contains(extension))
                return SearchString.IsNullOrWhiteSpace() || filename.IndexOf(SearchString, StringComparison.OrdinalIgnoreCase) > -1;

            return false;
        }

        private bool DirectoryContainsValidFiles(string path)
        {
            ClearInvalidatedFileTree();
            if (_validDirectoriesThisDraw.TryGetValue(path, out bool valid))
                return valid;

            valid = GetFiles(path).Any(IsValidFile) || GetDirectories(path).Any(DirectoryContainsValidFiles);
            _validDirectoriesThisDraw[path] = valid;
            return valid;
        }

        private void LoadFileToEditor(string filePath)
        {
            try
            {
                _fileContent = File.ReadAllText(filePath);
                _originalFileContent = _fileContent;
                _activeFile = filePath;
                _activeDirectory = Path.GetDirectoryName(filePath);
                _errorText = string.Empty;
                SetFileEditState(FileEditState.None);
                _focusTextArea = true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                LogError($"Failed to load file {filePath}: {e.Message}");
                _errorText = UiText.LoadFileFailedTextEditor;
                _fileContent = e.Message;
                _originalFileContent = string.Empty;
                _activeFile = string.Empty;
            }
        }

        private void DrawDirectoriesMenu()
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(UiText.NewFolderButtonTextEditor, GetButtonStyle()))
            {
                SetFileEditState(FileEditState.CreatingFolder);
            }
            if (GUILayout.Button(UiText.NewFileButtonTextEditor, GetButtonStyle()))
            {
                SetFileEditState(FileEditState.CreatingFile);
            }
            if (GUILayout.Button(UiText.RenameFileButtonTextEditor, GetButtonStyle()))
            {
                SetFileEditState(FileEditState.RenamingFile);
                _newItemName = Path.GetFileName(_activeFile);
            }
            if (GUILayout.Button(new GUIContent(UiText.DeleteFileButtonTextEditor, UiText.DeleteFileTooltipTextEditor), GetButtonStyle()))
                MoveActiveFileToTrash();

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _showEmptyFolders.Value = GUILayout.Toggle(_showEmptyFolders.Value, UiText.ShowEmptyTextEditor, GetToggleStyle(), GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            _showFullName.Value = GUILayout.Toggle(_showFullName.Value, new GUIContent(UiText.ShowFullNameTextEditor, UiText.ShowFullNameTooltipTextEditor), GetToggleStyle());
            _showTrashBin.Value = GUILayout.Toggle(_showTrashBin.Value, UiText.ShowTrashBinTextEditor, GetToggleStyle());
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void CreateNewItem(string itemName, bool isFolder)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            if (string.IsNullOrEmpty(_activeDirectory))
                return;

            if (!TryGetSafeChildPath(_activeDirectory, itemName, out string newPath))
            {
                _newItemErrorText = UiText.InvalidFileOrFolderNameTextEditor;
                return;
            }

            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                _newItemErrorText = UiText.FileExistsTextEditor;
                return;
            }

            try
            {
                if (isFolder)
                {
                    Directory.CreateDirectory(newPath);
                    InvalidateFileTree();
                    _showEmptyFolders.Value = true;
                    _activeDirectory = newPath;
                    _activeFile = string.Empty;
                }
                else
                {
                    using (new FileStream(newPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    InvalidateFileTree();
                    LoadFileToEditor(newPath);
                }

                SetFileEditState(FileEditState.None);
            }
            catch (Exception e)
            {
                _newItemErrorText = e.Message;
            }
        }

        private void SaveActiveFile()
        {
            string stagedPath = null;
            string backupPath = null;
            bool keepBackup = false;
            try
            {
                if (_activeFile.IsNullOrWhiteSpace() || !File.Exists(_activeFile))
                {
                    _errorText = UiText.ActiveFileMissingTextEditor;
                    return;
                }

                using (FileStream stream = new FileStream(_activeFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    string currentDiskContent;
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
                        currentDiskContent = reader.ReadToEnd();

                    if (!string.Equals(currentDiskContent, _originalFileContent, StringComparison.Ordinal))
                    {
                        _errorText = UiText.ActiveFileChangedTextEditor;
                        return;
                    }

                    // File.Replace requires releasing or weakening FileShare.None on Windows.
                    // Keep the existing conflict lock through staging, writing and recovery instead.
                    using (FileStream staged = CreateSaveFile(_activeFile, ".tmp", out stagedPath))
                    using (FileStream backup = CreateSaveFile(_activeFile, ".bak", out backupPath))
                    {
                        using (StreamWriter writer = new StreamWriter(staged, new UTF8Encoding(false), 1024, true))
                        {
                            writer.Write(_fileContent);
                            writer.Flush();
                        }
                        staged.Flush(true);

                        // Preserve bytes, including the original encoding/BOM, before touching the file.
                        stream.Position = 0;
                        stream.CopyTo(backup);
                        backup.Flush(true);
                        CommitStagedContent(stream, staged, backup, out keepBackup);
                    }
                }

                _originalFileContent = _fileContent;
                _errorText = string.Empty;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _errorText = e.Message;
                if (keepBackup)
                    _errorText += Environment.NewLine + "Original file backup: " + backupPath;
            }
            finally
            {
                DeleteSaveFile(stagedPath);
                if (!keepBackup)
                    DeleteSaveFile(backupPath);
            }
        }

        private static FileStream CreateSaveFile(string activeFile, string extension, out string createdPath)
        {
            string path = Path.Combine(Path.GetDirectoryName(activeFile), ".configmanager-save-" + Guid.NewGuid().ToString("N") + extension);
            FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            // Only files successfully created by this save may be cleaned up.
            createdPath = path;
            return stream;
        }

        private static void CommitStagedContent(Stream destination, Stream staged, Stream backup, out bool keepBackup)
        {
            // Retain the durable original for any unexpected failure after writing starts too.
            keepBackup = true;
            try
            {
                CopyCompleteContent(staged, destination);
                keepBackup = false;
            }
            catch (Exception writeError) when (writeError is IOException || writeError is UnauthorizedAccessException)
            {
                try
                {
                    CopyCompleteContent(backup, destination);
                    keepBackup = false;
                }
                catch (Exception restoreError) when (restoreError is IOException || restoreError is UnauthorizedAccessException)
                {
                    throw new IOException($"Saving failed ({writeError.Message}) and restoring the original failed ({restoreError.Message}).",
                        new AggregateException(writeError, restoreError));
                }

                throw;
            }
        }

        private static void CopyCompleteContent(Stream source, Stream destination)
        {
            source.Position = 0;
            destination.Position = 0;
            source.CopyTo(destination);
            // Delay truncation until all replacement bytes have been written.
            destination.SetLength(destination.Position);
            if (destination is FileStream file)
                file.Flush(true);
            else
                destination.Flush();
        }

        private void DeleteSaveFile(string path)
        {
            if (path == null)
                return;

            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                string message = $"Could not remove temporary save file {path}: {e.Message}";
                _errorText = string.IsNullOrEmpty(_errorText) ? message : _errorText + Environment.NewLine + message;
            }
        }

        private static bool TryGetSafeChildPath(string directory, string itemName, out string path)
        {
            path = null;
            if (directory.IsNullOrWhiteSpace() || itemName.IsNullOrWhiteSpace() || Path.IsPathRooted(itemName))
                return false;

            string trimmedName = itemName.Trim();
            if (trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                trimmedName.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                trimmedName.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                trimmedName.IndexOf('/') >= 0 ||
                trimmedName.IndexOf('\\') >= 0)
                return false;

            try
            {
                string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!IsPathInsideEditorRoots(fullDirectory))
                    return false;

                string candidate = Path.GetFullPath(Path.Combine(fullDirectory, trimmedName));
                string candidateDirectory = Path.GetDirectoryName(candidate)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(fullDirectory, candidateDirectory, GetPathComparison()))
                    return false;

                path = candidate;
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return false;
            }
        }

        private static bool IsPathInsideEditorRoots(string path)
        {
            foreach (string root in _directories)
            {
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(path, fullRoot, GetPathComparison()) ||
                    path.StartsWith(fullRoot + Path.DirectorySeparatorChar, GetPathComparison()))
                    return true;
            }

            return false;
        }

        private static StringComparison GetPathComparison()
        {
            return Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public void Dispose()
        {
            FlushWindowGeometry();
            foreach (FileSystemWatcher watcher in _watchers)
                watcher.Dispose();

            _watchers = Array.Empty<FileSystemWatcher>();
        }

    }
}

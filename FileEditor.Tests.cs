using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ConfigurationManager.Tests
{
    internal static class FileEditorTests
    {
        private delegate void CommitContent(Stream destination, Stream staged, Stream backup, out bool keepBackup);
        private static Type _editorType;
        private static CommitContent _commit;
        private static string _suiteRoot;
        private static string _trashRoot;

        internal static void Run(Assembly assembly, ICollection<string> failures, string temporaryRoot)
        {
            _editorType = assembly.GetType("ConfigurationManager.ConfigFilesEditor", true);
            _commit = (CommitContent)Delegate.CreateDelegate(typeof(CommitContent),
                _editorType.GetMethod("CommitStagedContent", BindingFlags.Static | BindingFlags.NonPublic));
            string[] roots = (string[])_editorType.GetField("_directories", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            string allowedRoot = Path.GetFullPath(temporaryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string root in roots)
                Check(Path.GetFullPath(root).StartsWith(allowedRoot, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
                    "file editor tests must use isolated BepInEx paths");
            _suiteRoot = Path.Combine(roots[0], "ConfigManagerFileEditorTests-" + Guid.NewGuid().ToString("N"));
            _trashRoot = roots[2];
            Directory.CreateDirectory(_suiteRoot);
            int passed = 0;
            try
            {
                Case("UTF-8 save, truncation and cleanup", SaveSuccess, failures, ref passed);
                Case("external content conflict preserves both buffers", SaveConflict, failures, ref passed);
                Case("locked file remains unchanged", SaveLocked, failures, ref passed);
                Case("missing file is not recreated", SaveMissing, failures, ref passed);
                Case("staging failure preserves original", StagingFailure, failures, ref passed);
                Case("partial write rolls back exact original bytes", () => Rollback("write"), failures, ref passed);
                Case("truncate failure rolls back", () => Rollback("truncate"), failures, ref passed);
                Case("flush failure rolls back", () => Rollback("flush"), failures, ref passed);
                Case("failed rollback retains recoverable backup", FailedRollback, failures, ref passed);
                Case("own mutations refresh cached file trees", MutationsInvalidate, failures, ref passed);
                Case("visibility is refreshed for each draw", VisibilityRefresh, failures, ref passed);
                Case("disabled watchers stay disabled after invalidation", WatcherState, failures, ref passed);
                Console.WriteLine("FileEditor: " + passed + " cases passed");
            }
            finally
            {
                // _suiteRoot was created above under a validated isolated BepInEx root.
                Directory.Delete(_suiteRoot, true);
            }
        }

        private static void SaveSuccess()
        {
            string directory = NewDirectory("success");
            string path = Path.Combine(directory, "settings.cfg");
            // Reading retains existing BOM detection; writing still produces UTF-8 without BOM.
            const string original = "original contents with a longer tail";
            File.WriteAllText(path, original, Encoding.Unicode);
            const string replacement = "한글\nvalue=1";
            object editor = Editor(path, original, replacement);
            Call(editor, "SaveActiveFile");
            Check(File.ReadAllBytes(path).SequenceEqual(new UTF8Encoding(false).GetBytes(replacement)), "saved bytes and truncation");
            Check((string)Field(editor, "_originalFileContent") == replacement && string.IsNullOrEmpty((string)Field(editor, "_errorText")), "save acknowledged only after completion");
            Check(Directory.GetFiles(directory).SequenceEqual(new[] { path }), "successful save removes staging and backup files");
        }

        private static void SaveConflict()
        {
            string directory = NewDirectory("conflict");
            string path = Path.Combine(directory, "settings.cfg");
            File.WriteAllText(path, "outside change");
            object editor = Editor(path, "original", "my edit");
            Call(editor, "SaveActiveFile");
            Check(File.ReadAllText(path) == "outside change", "outside edit preserved");
            Check((string)Field(editor, "_fileContent") == "my edit" && (string)Field(editor, "_originalFileContent") == "original", "unsaved editor buffers retained");
            Check(!string.IsNullOrEmpty((string)Field(editor, "_errorText")) && Directory.GetFiles(directory).Length == 1, "conflict reports failure without save artifacts");
        }

        private static void SaveLocked()
        {
            string directory = NewDirectory("locked");
            string path = Path.Combine(directory, "settings.cfg");
            File.WriteAllText(path, "original");
            object editor = Editor(path, "original", "edit");
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Call(editor, "SaveActiveFile");
            Check(File.ReadAllText(path) == "original" && (string)Field(editor, "_originalFileContent") == "original", "locked file and buffer preserved");
            Check(!string.IsNullOrEmpty((string)Field(editor, "_errorText")) && Directory.GetFiles(directory).Length == 1, "lock failure reported without artifacts");
        }

        private static void SaveMissing()
        {
            string path = Path.Combine(NewDirectory("missing"), "settings.cfg");
            object editor = Editor(path, "original", "edit");
            Call(editor, "SaveActiveFile");
            Check(!File.Exists(path) && !string.IsNullOrEmpty((string)Field(editor, "_errorText")), "deleted file must be reloaded, not recreated");
        }

        private static void StagingFailure()
        {
            if (Path.DirectorySeparatorChar != '\\')
                throw new PlatformNotSupportedException("The staging ACL failure test requires Windows; run the filesystem suite on Windows.");
            string directory = NewDirectory("staging-failure");
            string path = Path.Combine(directory, "settings.cfg");
            File.WriteAllText(path, "original");
            object editor = Editor(path, "original", "edit");
            DirectorySecurity originalSecurity = Directory.GetAccessControl(directory);
            DirectorySecurity deniedSecurity = Directory.GetAccessControl(directory);
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                deniedSecurity.AddAccessRule(new FileSystemAccessRule(identity.User,
                    FileSystemRights.CreateFiles, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
            try
            {
                Directory.SetAccessControl(directory, deniedSecurity);
                Call(editor, "SaveActiveFile");
            }
            finally
            {
                Directory.SetAccessControl(directory, originalSecurity);
            }
            Check(File.ReadAllText(path) == "original" && (string)Field(editor, "_originalFileContent") == "original", "staging failure never touched original");
            Check(!string.IsNullOrEmpty((string)Field(editor, "_errorText")) && Directory.GetFiles(directory).Length == 1, "staging error and cleanup");
        }

        private static void Rollback(string failure)
        {
            byte[] original = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("exact original bytes")).ToArray();
            using (var destination = new FailingStream(original, failure, false))
            using (var staged = new MemoryStream(Encoding.UTF8.GetBytes("replacement")))
            using (var backup = new MemoryStream(original, false))
            {
                bool keepBackup = true;
                ExpectIOException(() => _commit(destination, staged, backup, out keepBackup));
                Check(destination.ToArray().SequenceEqual(original), "original bytes restored after " + failure);
                Check(!keepBackup && backup.ToArray().SequenceEqual(original), "successful rollback releases backup requirement without altering backup");
            }
        }

        private static void FailedRollback()
        {
            string backupPath = Path.Combine(NewDirectory("failed-rollback"), "original.bak");
            byte[] original = Encoding.UTF8.GetBytes("original recoverable bytes");
            File.WriteAllBytes(backupPath, original);
            using (var destination = new FailingStream(original, "write", true))
            using (var staged = new MemoryStream(Encoding.UTF8.GetBytes("replacement")))
            using (var backup = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool keepBackup = false;
                IOException error = ExpectIOException(() => _commit(destination, staged, backup, out keepBackup));
                Check(keepBackup, "failed rollback requires retaining the backup");
                Check(error.InnerException is AggregateException errors && errors.InnerExceptions.Count == 2, "write and restoration failures are both reported");
            }
            Check(File.ReadAllBytes(backupPath).SequenceEqual(original), "backup survives failed commit and failed restoration intact");
        }

        private static void MutationsInvalidate()
        {
            string directory = NewDirectory("mutations");
            object editor = Editor(null, null, null);
            Set(editor, "_activeDirectory", directory);
            Check(((string[])Call(editor, "GetFiles", directory)).Length == 0, "warm empty cache");
            string name = "file-editor-test-" + Guid.NewGuid().ToString("N") + ".cfg";
            Call(editor, "CreateNewItem", name, false);
            string path = Path.Combine(directory, name);
            Check(((string[])Call(editor, "GetFiles", directory)).Contains(path), "create invalidated warmed cache");
            string renamed = "renamed-" + name;
            Set(editor, "_newItemName", renamed);
            Call(editor, "RenameActiveFile");
            Check(((string[])Call(editor, "GetFiles", directory)).SequenceEqual(new[] { Path.Combine(directory, renamed) }), "rename invalidated warmed cache");
            Call(editor, "GetFiles", _trashRoot);
            string trashedPath = Path.Combine(_trashRoot, renamed);
            try
            {
                Call(editor, "MoveActiveFileToTrash");
                Check(((string[])Call(editor, "GetFiles", directory)).Length == 0 && ((string[])Call(editor, "GetFiles", _trashRoot)).Contains(trashedPath), "trash move invalidated both roots");
                Set(editor, "_activeFile", trashedPath);
                Set(editor, "_newItemName", "again-" + name);
                Call(editor, "RenameActiveFile");
                trashedPath = Path.Combine(_trashRoot, "again-" + name);
                Check(((string[])Call(editor, "GetFiles", _trashRoot)).Contains(trashedPath), "rename inside unobserved trash root invalidated cache");
            }
            finally
            {
                File.Delete(trashedPath);
            }
        }

        private static void VisibilityRefresh()
        {
            string directory = NewDirectory("visibility");
            string child = Directory.CreateDirectory(Path.Combine(directory, "child")).FullName;
            string path = Path.Combine(child, "example.cfg");
            File.WriteAllText(path, "value");
            object editor = Editor(null, null, null);
            Call(editor, "PrepareFileTreeDraw");
            Check((bool)Call(editor, "DirectoryContainsValidFiles", directory), "descendant config makes parent visible");
            Set(editor, "_searchString", "absent");
            Call(editor, "PrepareFileTreeDraw");
            Check(!(bool)Call(editor, "DirectoryContainsValidFiles", directory), "new draw sees changed search");
            Set(editor, "_searchString", "EXAMPLE");
            Call(editor, "PrepareFileTreeDraw");
            Check((bool)Call(editor, "DirectoryContainsValidFiles", directory), "search remains case-insensitive");
            Call(editor, "PrepareFileTreeDraw");
            ((HashSet<string>)Field(editor, "_pluginConfigPaths")).Add(path.ToUpperInvariant());
            Check((bool)Call(editor, "IsValidFile", path), "plugin path exclusion retains the existing ordinal case policy");
            Call(editor, "PrepareFileTreeDraw");
            ((HashSet<string>)Field(editor, "_pluginConfigPaths")).Add(path);
            Check(!(bool)Call(editor, "IsValidFile", path), "plugin config path is excluded on an exact match");
            Call(editor, "PrepareFileTreeDraw");
            File.Delete(path);
            Call(editor, "InvalidateFileTree");
            Check(!(bool)Call(editor, "DirectoryContainsValidFiles", directory), "invalidation discards cached visibility and directory contents together");
        }

        private static void WatcherState()
        {
            using (var watcher = new FileSystemWatcher(NewDirectory("watcher")))
            {
                object editor = Editor(null, null, null);
                Set(editor, "_watchers", new[] { watcher });
                _editorType.GetProperty("IsOpen").SetValue(editor, true);
                Check(watcher.EnableRaisingEvents, "open enables active watcher");
                Call(editor, "SetWatching", false);
                Call(editor, "InvalidateFileTree");
                _editorType.GetProperty("IsOpen").SetValue(editor, false);
                _editorType.GetProperty("IsOpen").SetValue(editor, true);
                Check(!watcher.EnableRaisingEvents, "invalidating and reopening do not override disabled lifecycle state");
                Call(editor, "SetWatching", true);
                Check(watcher.EnableRaisingEvents, "lifecycle enable restores watching");
            }
        }

        private static object Editor(string path, string original, string content)
        {
            // Bypass Unity geometry/config construction; exercise the production filesystem methods.
            object editor = FormatterServices.GetUninitializedObject(_editorType);
            Set(editor, "_cachedFileTree", new Dictionary<string, string[]>());
            Set(editor, "_cachedDirectories", new Dictionary<string, string[]>());
            Set(editor, "_validFilesThisDraw", new Dictionary<string, bool>());
            Set(editor, "_validDirectoriesThisDraw", new Dictionary<string, bool>());
            Set(editor, "_pluginConfigPaths", new HashSet<string>(StringComparer.Ordinal));
            Set(editor, "_watchers", Array.Empty<FileSystemWatcher>());
            Set(editor, "_watchingEnabled", true);
            Set(editor, "_activeFile", path);
            Set(editor, "_originalFileContent", original);
            Set(editor, "_fileContent", content);
            return editor;
        }

        private sealed class FailingStream : MemoryStream
        {
            private readonly string _failure;
            private readonly bool _failRepeatedly;
            private bool _failed;

            internal FailingStream(byte[] original, string failure, bool failRepeatedly)
            {
                base.Write(original, 0, original.Length);
                Position = 0;
                _failure = failure;
                _failRepeatedly = failRepeatedly;
            }

            private bool Fail(string operation)
            {
                if (_failure != operation || (_failed && !_failRepeatedly)) return false;
                _failed = true;
                return true;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Fail("write"))
                {
                    base.Write(buffer, offset, Math.Min(count, 3));
                    throw new IOException("injected partial write failure");
                }
                base.Write(buffer, offset, count);
            }

            public override void SetLength(long value)
            {
                if (Fail("truncate")) throw new IOException("injected truncate failure");
                base.SetLength(value);
            }

            public override void Flush()
            {
                if (Fail("flush")) throw new IOException("injected flush failure");
                base.Flush();
            }
        }

        private static string NewDirectory(string name) => Directory.CreateDirectory(Path.Combine(_suiteRoot, name)).FullName;
        private static object Call(object target, string name, params object[] args) => _editorType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        private static object Field(object target, string name) => _editorType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value) => _editorType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static IOException ExpectIOException(Action action)
        {
            try { action(); }
            catch (IOException error) { return error; }
            throw new Exception("Expected an I/O failure.");
        }
        private static void Case(string name, Action test, ICollection<string> failures, ref int passed)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures.Add("FileEditor " + name + ": " + (error is TargetInvocationException call && call.InnerException != null ? call.InnerException : error)); }
        }
    }
}

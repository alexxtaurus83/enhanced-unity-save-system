using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlexibleSave.Enhanced.Security;
using Newtonsoft.Json.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FlexibleSave.Enhanced
{
    public enum EncryptionMode
    {
        None = 0,
        Light = 1
    }

    public enum LoadStatus
    {
        Ok,
        NoSaveFile,
        RestoredFromBackup,
        Corrupt
    }

    public readonly struct LoadResult
    {
        public LoadStatus Status { get; }
        public int Restored { get; }
        public int Skipped { get; }
        public double Ms { get; }
        public string Error { get; }

        public LoadResult(LoadStatus status, int restored, int skipped, double ms, string error = null)
        {
            Status = status;
            Restored = restored;
            Skipped = skipped;
            Ms = ms;
            Error = error;
        }
    }

    public enum SaveStatus
    {
        Ok,
        Blocked,
        Failed
    }

    public readonly struct SaveResult
    {
        public SaveStatus Status { get; }
        public double Ms { get; }
        public string Error { get; }

        public SaveResult(SaveStatus status, double ms, string error = null)
        {
            Status = status;
            Ms = ms;
            Error = error;
        }
    }

    public readonly struct SaveError
    {
        public string Operation { get; }
        public string Message { get; }
        public Exception Exception { get; }

        public SaveError(string operation, string message, Exception exception = null)
        {
            Operation = operation;
            Message = message;
            Exception = exception;
        }
    }

    [DisallowMultipleComponent]
    public partial class SaveSystem : MonoBehaviour
    {
        private static readonly byte[] s_headerMagic = { (byte)'F', (byte)'S', (byte)'V' };
        private static readonly UTF8Encoding s_strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        [AutoStaticsCleanup]
        private static SaveSystem s_instance;

        [AutoStaticsCleanup]
        public static bool VerboseLogging;

        [AutoStaticsCleanup]
        public static Func<JObject, int, JObject> Migrations;

        [SerializeField] private string m_fileName = "save.json";
        [SerializeField] private string m_folder = "Saves";
        [SerializeField] private EncryptionMode m_encryption = EncryptionMode.None;
        [SerializeField] private bool m_useBackgroundWrite;

        private string m_basePath;
        private ISaveEncryptor m_encryptor;
        private bool m_customEncryptorAssigned;
        private Func<string> m_secretProvider;
        private SaveDocument m_cachedDocument;
        private IFileSystem m_fileSystem = PhysicalFileSystem.Instance;

        private readonly object m_writeLock = new object();
        private readonly ManualResetEventSlim m_idleEvent = new ManualResetEventSlim(initialState: true);
        private PendingWrite m_pendingWrite;
        private bool m_writerRunning;
        private bool m_disposed;
        private TaskCompletionSource<SaveResult> m_pendingBatchTcs;
        private bool m_pendingBatchObserved;

        [AutoStaticsCleanup]
        private static Dictionary<string, (string hashHex, string copyPath)> s_lastQuarantinedCorruptCopies =
            new Dictionary<string, (string hashHex, string copyPath)>(StringComparer.OrdinalIgnoreCase);

        private sealed class PendingWrite
        {
            public string Json;
            public string FullPath;
            public ISaveEncryptor Encryptor;
            public bool Verbose;
        }

        public static SaveSystem Instance => s_instance;

        public string FileName
        {
            get => m_fileName;
            set => m_fileName = value;
        }

        public string Folder
        {
            get => m_folder;
            set => m_folder = value;
        }

        public EncryptionMode Encryption
        {
            get => m_encryption;
            set
            {
                m_encryption = value;
                m_customEncryptorAssigned = false;
                SyncDefaultEncryptor();
            }
        }

        public ISaveEncryptor Encryptor
        {
            get
            {
                if (!m_customEncryptorAssigned && m_encryptor == null && m_encryption == EncryptionMode.Light)
                {
                    SyncDefaultEncryptor();
                }
                return m_encryptor;
            }
            set
            {
                m_customEncryptorAssigned = true;
                m_encryptor = value;
            }
        }

        public bool UseBackgroundWrite
        {
            get => m_useBackgroundWrite;
            set => m_useBackgroundWrite = value;
        }

        public Func<string> SecretProvider
        {
            get => m_secretProvider;
            set
            {
                m_secretProvider = value;
                if (!m_customEncryptorAssigned)
                {
                    SyncDefaultEncryptor();
                }
            }
        }

        public string BasePath
        {
            get => string.IsNullOrEmpty(m_basePath) ? Application.persistentDataPath : m_basePath;
            set => m_basePath = value;
        }

        public string FullPath => ComputeFullPath(BasePath, m_folder, m_fileName);

        public bool HasLoaded { get; private set; }
        public bool IsCorrupt { get; private set; }
        public bool IsBusy { get; private set; }

        public event Action<LoadResult> Loaded;
        public event Action<SaveResult> Saved;
        public event Action<SaveError> Failed;

        internal IFileSystem FileSystem
        {
            get => m_fileSystem;
            set => m_fileSystem = value ?? PhysicalFileSystem.Instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitStaticsOnLoad()
        {
            VerboseLogging = Application.isEditor;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitEditorDefaults()
        {
            VerboseLogging = true;
        }
#endif

        internal static string ComputeFullPath(string basePath, string folder, string fileName)
        {
            string root = string.IsNullOrEmpty(basePath) ? Application.persistentDataPath : basePath;
            string safeFile = string.IsNullOrEmpty(fileName) ? "save.json" : fileName;
            return string.IsNullOrEmpty(folder)
                ? Path.Combine(root, safeFile)
                : Path.Combine(root, folder, safeFile);
        }

        private static bool IsWebGLPlatform
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FlexibleSave_SyncFileSystem();
#endif

        private static void SyncWebGLFileSystemIfNeeded()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            FlexibleSave_SyncFileSystem();
#endif
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Debug.LogError($"[FlexibleSave] Duplicate SaveSystem instance on '{gameObject.name}'. Destroying duplicate component.", this);
                Destroy(this);
                return;
            }

            s_instance = this;
            if (string.IsNullOrEmpty(m_basePath))
            {
                m_basePath = Application.persistentDataPath;
            }

            SyncDefaultEncryptor();
        }

        private void OnValidate()
        {
            if (!m_customEncryptorAssigned)
            {
                SyncDefaultEncryptor();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Flush();
            }
        }

        private void OnApplicationQuit()
        {
            Flush();
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                Flush();
                s_instance = null;
            }

            bool canDispose = false;
            lock (m_writeLock)
            {
                m_disposed = true;
                canDispose = !m_writerRunning;
            }

            if (canDispose)
            {
                m_idleEvent.Dispose();
            }
        }

        private void SyncDefaultEncryptor()
        {
            if (m_encryption == EncryptionMode.Light)
            {
                Func<string> provider = m_secretProvider ?? AppSecretProvider.GetSecret;
                m_encryptor = new LightSaveEncryptor(provider);
            }
            else
            {
                m_encryptor = null;
            }
        }

        public LoadResult Load()
        {
            if (IsBusy)
            {
                throw new InvalidOperationException("Cannot call Load while a load operation is already in progress.");
            }

            IsBusy = true;
            var sw = Stopwatch.StartNew();
            try
            {
                Flush();

                string fullPath = FullPath;
                ISaveEncryptor encryptor = Encryptor;
                IFileSystem fs = m_fileSystem;
                Func<JObject, int, JObject> migrations = Migrations;

                WarmSecretCacheIfNeeded(encryptor);

                ReadOutcome outcome = ReadAndParseFile(fs, fullPath, encryptor, migrations);
                return ApplyLoadOutcome(outcome, sw, progress: null);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public Awaitable<LoadResult> LoadAsync(IProgress<float> p = null, CancellationToken ct = default)
        {
            if (IsBusy)
            {
                throw new InvalidOperationException("Cannot call LoadAsync while another load operation is already in progress.");
            }

            IsBusy = true;
            return LoadAsyncInternal(p, ct);
        }

        private async Awaitable<LoadResult> LoadAsyncInternal(IProgress<float> p, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                ct.ThrowIfCancellationRequested();
                p?.Report(0f);

                Flush();

                string fullPath = FullPath;
                ISaveEncryptor encryptor = Encryptor;
                IFileSystem fs = m_fileSystem;
                Func<JObject, int, JObject> migrations = Migrations;

                WarmSecretCacheIfNeeded(encryptor);

                await Awaitable.BackgroundThreadAsync();
                ct.ThrowIfCancellationRequested();

                ReadOutcome outcome = ReadAndParseFile(fs, fullPath, encryptor, migrations);

                await Awaitable.MainThreadAsync();
                if (this == null)
                {
                    return new LoadResult(
                        LoadStatus.NoSaveFile,
                        0,
                        0,
                        sw.Elapsed.TotalMilliseconds,
                        "SaveSystem was destroyed during LoadAsync.");
                }
                ct.ThrowIfCancellationRequested();

                p?.Report(0.1f);
                return ApplyLoadOutcome(outcome, sw, p);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public SaveResult Save()
        {
            if (!HasLoaded)
            {
                const string msg = "Save blocked: Load() has not completed yet.";
                return NotifySaveBlocked(msg);
            }

            if (IsCorrupt)
            {
                const string msg = "Save blocked: current save file is corrupt. Resolve or call Delete() before saving.";
                return NotifySaveBlocked(msg);
            }

            if (IsBusy)
            {
                const string msg = "Save blocked: LoadAsync is currently in progress.";
                return NotifySaveBlocked(msg);
            }

            var sw = Stopwatch.StartNew();
            string json;
            string fullPath;
            ISaveEncryptor encryptor;
            bool verbose;

            try
            {
                PrepareSavePayload(out json, out fullPath, out encryptor, out verbose);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var fail = new SaveResult(SaveStatus.Failed, sw.Elapsed.TotalMilliseconds, ex.Message);
                RaiseError("Save", ex.Message, ex);
                Saved?.Invoke(fail);
                return fail;
            }

            if (m_useBackgroundWrite && !IsWebGLPlatform)
            {
                Task<SaveResult> writeTask = EnqueueBackgroundWrite(json, fullPath, encryptor, verbose, out bool shouldNotify);
                if (shouldNotify)
                {
                    ObserveBackgroundWrite(writeTask, fullPath, verbose);
                }
                sw.Stop();
                return new SaveResult(SaveStatus.Ok, sw.Elapsed.TotalMilliseconds);
            }

            Flush();
            SaveResult result = ExecuteWrite(m_fileSystem, json, fullPath, encryptor, verbose, sw);
            if (result.Status == SaveStatus.Failed)
            {
                RaiseError("Save", result.Error, logError: false);
            }
            else if (verbose)
            {
                Debug.Log($"[FlexibleSave] Saved to '{fullPath}' in {result.Ms:F2} ms.");
            }
            Saved?.Invoke(result);
            return result;
        }

        public async Awaitable<SaveResult> SaveAsync(CancellationToken ct = default)
        {
            if (!HasLoaded)
            {
                return NotifySaveBlocked("Save blocked: Load() has not completed yet.");
            }

            if (IsCorrupt)
            {
                return NotifySaveBlocked("Save blocked: current save file is corrupt. Resolve or call Delete() before saving.");
            }

            if (IsBusy)
            {
                return NotifySaveBlocked("Save blocked: LoadAsync is currently in progress.");
            }

            ct.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            string json;
            string fullPath;
            ISaveEncryptor encryptor;
            bool verbose;

            try
            {
                PrepareSavePayload(out json, out fullPath, out encryptor, out verbose);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var fail = new SaveResult(SaveStatus.Failed, sw.Elapsed.TotalMilliseconds, ex.Message);
                RaiseError("SaveAsync", ex.Message, ex);
                Saved?.Invoke(fail);
                return fail;
            }

            if (m_useBackgroundWrite && !IsWebGLPlatform)
            {
                Task<SaveResult> writeTask = EnqueueBackgroundWrite(json, fullPath, encryptor, verbose, out bool shouldNotify);
                SaveResult bgResult = await writeTask;
                await Awaitable.MainThreadAsync();
                if (this == null)
                {
                    return bgResult;
                }

                sw.Stop();
                var finalResult = new SaveResult(bgResult.Status, sw.Elapsed.TotalMilliseconds, bgResult.Error);
                if (shouldNotify)
                {
                    NotifyBackgroundSaveResult("SaveAsync", fullPath, verbose, finalResult);
                }

                ct.ThrowIfCancellationRequested();
                return finalResult;
            }

            Flush();
            SaveResult syncResult = ExecuteWrite(m_fileSystem, json, fullPath, encryptor, verbose, sw);
            if (syncResult.Status == SaveStatus.Failed)
            {
                RaiseError("SaveAsync", syncResult.Error, logError: false);
            }
            else if (verbose)
            {
                Debug.Log($"[FlexibleSave] Saved to '{fullPath}' in {syncResult.Ms:F2} ms.");
            }
            Saved?.Invoke(syncResult);
            return syncResult;
        }

        public void Flush(int timeoutMs = 2000)
        {
            lock (m_writeLock)
            {
                if (m_disposed)
                {
                    return;
                }
            }

            try
            {
                if (!m_idleEvent.IsSet)
                {
                    m_idleEvent.Wait(Math.Max(0, timeoutMs));
                }
            }
            catch (ObjectDisposedException)
            {
                // SaveSystem was destroyed concurrently while waiting.
            }
        }

        public void Delete()
        {
            Flush();

            string fullPath = FullPath;
            TryDeleteFile(m_fileSystem, fullPath);
            TryDeleteFile(m_fileSystem, fullPath + ".bak");
            TryDeleteFile(m_fileSystem, fullPath + ".tmp");
            SyncWebGLFileSystemIfNeeded();

            m_cachedDocument = null;
            IsCorrupt = false;
            HasLoaded = true;
            SaveableEntityRegistry.ClearPendingDeletedIds();

            if (VerboseLogging)
            {
                Debug.Log($"[FlexibleSave] Deleted save slot at '{fullPath}'.");
            }
        }

        private void PrepareSavePayload(out string json, out string fullPath, out ISaveEncryptor encryptor, out bool verbose)
        {
            m_cachedDocument ??= new SaveDocument();
            m_cachedDocument.format = SaveDocument.FormatName;
            m_cachedDocument.formatVersion = SaveDocument.CurrentFormatVersion;
            m_cachedDocument.savedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            SaveableEntityRegistry.CaptureState(m_cachedDocument);

            verbose = VerboseLogging;
            bool indent = verbose && Application.isEditor;
            json = SaveJson.Serialize(m_cachedDocument, indent);
            fullPath = FullPath;
            encryptor = Encryptor;

            WarmSecretCacheIfNeeded(encryptor);
        }

        private void WarmSecretCacheIfNeeded(ISaveEncryptor encryptor)
        {
            if (encryptor is LightSaveEncryptor)
            {
                if (m_secretProvider != null)
                {
                    _ = m_secretProvider();
                }
                else
                {
                    AppSecretProvider.WarmCache();
                }
            }
        }

        private Task<SaveResult> EnqueueBackgroundWrite(
            string json,
            string fullPath,
            ISaveEncryptor encryptor,
            bool verbose,
            out bool shouldNotify)
        {
            lock (m_writeLock)
            {
                if (m_disposed)
                {
                    shouldNotify = true;
                    return Task.FromResult(new SaveResult(SaveStatus.Failed, 0, "SaveSystem has been disposed."));
                }

                m_pendingWrite = new PendingWrite
                {
                    Json = json,
                    FullPath = fullPath,
                    Encryptor = encryptor,
                    Verbose = verbose
                };

                if (m_pendingBatchTcs == null)
                {
                    m_pendingBatchTcs = new TaskCompletionSource<SaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_pendingBatchObserved = false;
                }

                TaskCompletionSource<SaveResult> tcs = m_pendingBatchTcs;
                if (!m_pendingBatchObserved)
                {
                    m_pendingBatchObserved = true;
                    shouldNotify = true;
                }
                else
                {
                    shouldNotify = false;
                }

                if (!m_writerRunning)
                {
                    m_writerRunning = true;
                    m_idleEvent.Reset();
                    IFileSystem fs = m_fileSystem;
                    Task.Run(() => BackgroundWriteLoop(fs));
                }

                return tcs.Task;
            }
        }

        private void BackgroundWriteLoop(IFileSystem fs)
        {
            while (true)
            {
                PendingWrite work;
                TaskCompletionSource<SaveResult> batchTcs;

                lock (m_writeLock)
                {
                    work = m_pendingWrite;
                    m_pendingWrite = null;
                    batchTcs = m_pendingBatchTcs;
                    m_pendingBatchTcs = null;
                    m_pendingBatchObserved = false;

                    if (work == null)
                    {
                        m_writerRunning = false;
                        if (!m_disposed)
                        {
                            m_idleEvent.Set();
                        }
                        else
                        {
                            m_idleEvent.Dispose();
                        }
                        return;
                    }
                }

                var writeSw = Stopwatch.StartNew();
                SaveResult result = ExecuteWrite(fs, work.Json, work.FullPath, work.Encryptor, work.Verbose, writeSw);
                batchTcs?.TrySetResult(result);
            }
        }

        private async void ObserveBackgroundWrite(Task<SaveResult> writeTask, string fullPath, bool verbose)
        {
            try
            {
                SaveResult result = await writeTask;
                if (this == null)
                {
                    return;
                }

                NotifyBackgroundSaveResult("Save", fullPath, verbose, result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FlexibleSave] Unexpected background write exception: {ex}");
            }
        }

        private void NotifyBackgroundSaveResult(string operation, string fullPath, bool verbose, SaveResult result)
        {
            if (result.Status == SaveStatus.Failed)
            {
                RaiseError(operation, result.Error, logError: false);
            }
            else if (verbose)
            {
                Debug.Log($"[FlexibleSave] Background save to '{fullPath}' completed in {result.Ms:F2} ms.");
            }
            Saved?.Invoke(result);
        }

        private static SaveResult ExecuteWrite(
            IFileSystem fs,
            string json,
            string fullPath,
            ISaveEncryptor encryptor,
            bool verbose,
            Stopwatch sw)
        {
            string tmpPath = fullPath + ".tmp";
            string bakPath = fullPath + ".bak";
            try
            {
                byte[] payload = BuildFilePayload(json, encryptor);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    fs.CreateDirectory(directory);
                }

                fs.WriteAllBytes(tmpPath, payload);

                if (fs.Exists(fullPath))
                {
                    fs.Replace(tmpPath, fullPath, bakPath);
                }
                else
                {
                    fs.Move(tmpPath, fullPath);
                }

                SyncWebGLFileSystemIfNeeded();

                sw.Stop();
                return new SaveResult(SaveStatus.Ok, sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                TryDeleteFile(fs, tmpPath);
                sw.Stop();
                Debug.LogError($"[FlexibleSave] Save failed for '{fullPath}': {ex}");
                return new SaveResult(SaveStatus.Failed, sw.Elapsed.TotalMilliseconds, ex.Message);
            }
        }

        internal static byte[] BuildFilePayload(string json, ISaveEncryptor encryptor)
        {
            byte[] plainBytes = s_strictUtf8.GetBytes(json);
            if (encryptor == null)
            {
                return plainBytes;
            }

            string id = encryptor.Id;
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException("ISaveEncryptor.Id cannot be null or empty.");
            }

            byte[] idBytes = Encoding.UTF8.GetBytes(id);
            if (idBytes.Length > byte.MaxValue)
            {
                throw new InvalidOperationException($"ISaveEncryptor.Id '{id}' exceeds 255 UTF-8 bytes.");
            }

            byte[] cipherBytes = encryptor.Encrypt(plainBytes);
            var output = new byte[s_headerMagic.Length + 1 + idBytes.Length + cipherBytes.Length];
            Buffer.BlockCopy(s_headerMagic, 0, output, 0, s_headerMagic.Length);
            output[s_headerMagic.Length] = (byte)idBytes.Length;
            Buffer.BlockCopy(idBytes, 0, output, s_headerMagic.Length + 1, idBytes.Length);
            Buffer.BlockCopy(cipherBytes, 0, output, s_headerMagic.Length + 1 + idBytes.Length, cipherBytes.Length);
            return output;
        }

        internal static bool HasEncryptedHeader(byte[] bytes)
        {
            return bytes != null &&
                   bytes.Length >= 4 &&
                   bytes[0] == s_headerMagic[0] &&
                   bytes[1] == s_headerMagic[1] &&
                   bytes[2] == s_headerMagic[2];
        }

        internal static SaveDocument ParsePayload(
            byte[] bytes,
            ISaveEncryptor encryptor,
            Func<JObject, int, JObject> migrations)
        {
            if (bytes == null || bytes.Length == 0)
            {
                throw new InvalidDataException("Save file is empty.");
            }

            string json;
            if (HasEncryptedHeader(bytes))
            {
                int idLen = bytes[3];
                int headerTotal = 4 + idLen;
                if (idLen == 0 || bytes.Length <= headerTotal)
                {
                    throw new InvalidDataException("Save file header is malformed or truncated.");
                }

                string fileEncryptorId = Encoding.UTF8.GetString(bytes, 4, idLen);
                if (encryptor == null)
                {
                    throw new InvalidOperationException(
                        $"Encryptor ID mismatch: save file is encrypted with '{fileEncryptorId}', but configured encryptor is 'none'.");
                }

                if (!string.Equals(fileEncryptorId, encryptor.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Encryptor ID mismatch: save file uses '{fileEncryptorId}', but configured encryptor is '{encryptor.Id}'.");
                }

                int cipherLen = bytes.Length - headerTotal;
                var cipherBytes = new byte[cipherLen];
                Buffer.BlockCopy(bytes, headerTotal, cipherBytes, 0, cipherLen);

                byte[] plainBytes = encryptor.Decrypt(cipherBytes);
                json = s_strictUtf8.GetString(plainBytes);
            }
            else
            {
                if (encryptor != null)
                {
                    throw new InvalidDataException(
                        $"Encryptor mismatch: save file is unencrypted plaintext, but configured encryptor is '{encryptor.Id}'.");
                }

                json = s_strictUtf8.GetString(bytes);
            }

            if (json.Length > 0 && json[0] == '\uFEFF')
            {
                json = json.Substring(1);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("Save file JSON content is empty.");
            }

            JObject root = JObject.Parse(json);
            string format = root.Value<string>("format");
            if (!string.Equals(format, SaveDocument.FormatName, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Invalid save format '{format ?? "<null>"}'; expected '{SaveDocument.FormatName}'.");
            }

            JToken versionToken = root["formatVersion"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
            {
                throw new InvalidDataException("Save file is missing a valid integer 'formatVersion'.");
            }

            int version = versionToken.Value<int>();
            if (version != SaveDocument.CurrentFormatVersion)
            {
                if (migrations == null)
                {
                    throw new InvalidDataException(
                        $"Unsupported save formatVersion {version} (expected {SaveDocument.CurrentFormatVersion}) and no migration handler is registered.");
                }

                root = migrations(root, version);
                if (root == null)
                {
                    throw new InvalidDataException($"Save migration from version {version} returned null.");
                }

                JToken migratedVersionToken = root["formatVersion"];
                int migratedVersion = migratedVersionToken != null && migratedVersionToken.Type == JTokenType.Integer
                    ? migratedVersionToken.Value<int>()
                    : -1;

                if (migratedVersion != SaveDocument.CurrentFormatVersion)
                {
                    throw new InvalidDataException(
                        $"Save migration from version {version} produced version {migratedVersion} instead of {SaveDocument.CurrentFormatVersion}.");
                }
            }

            SaveDocument doc = root.ToObject<SaveDocument>(SaveJson.Serializer);
            if (doc == null)
            {
                throw new InvalidDataException("Failed to deserialize SaveDocument.");
            }

            doc.entities ??= new Dictionary<string, EntityRecord>(StringComparer.Ordinal);
            doc.deleted ??= new List<string>();
            return doc;
        }

        private sealed class ReadOutcome
        {
            public LoadStatus Status;
            public SaveDocument Document;
            public string Error;
            public Exception Exception;
            public string CorruptCopyPath;
        }

        private static ReadOutcome ReadAndParseFile(
            IFileSystem fs,
            string fullPath,
            ISaveEncryptor encryptor,
            Func<JObject, int, JObject> migrations)
        {
            if (!fs.Exists(fullPath))
            {
                return new ReadOutcome { Status = LoadStatus.NoSaveFile };
            }

            string primaryError;
            Exception primaryException;
            byte[] primaryBytes = null;
            try
            {
                primaryBytes = fs.ReadAllBytes(fullPath);
                SaveDocument doc = ParsePayload(primaryBytes, encryptor, migrations);
                return new ReadOutcome
                {
                    Status = LoadStatus.Ok,
                    Document = doc
                };
            }
            catch (Exception ex)
            {
                primaryError = ex.Message;
                primaryException = ex;
            }

            // Primary file failed: copy it aside as <name>.corrupt.<utc>.<ext> (deduplicating identical corrupt snapshots).
            string corruptCopyPath = TryBackupCorruptFile(fs, fullPath, primaryBytes);

            // Try .bak fallback
            string bakPath = fullPath + ".bak";
            if (fs.Exists(bakPath))
            {
                try
                {
                    byte[] bakBytes = fs.ReadAllBytes(bakPath);
                    SaveDocument bakDoc = ParsePayload(bakBytes, encryptor, migrations);
                    return new ReadOutcome
                    {
                        Status = LoadStatus.RestoredFromBackup,
                        Document = bakDoc,
                        Error = primaryError,
                        Exception = primaryException,
                        CorruptCopyPath = corruptCopyPath
                    };
                }
                catch (Exception bakEx)
                {
                    primaryError = $"Primary error: {primaryError}; Backup error: {bakEx.Message}";
                    primaryException = bakEx;
                }
            }

            return new ReadOutcome
            {
                Status = LoadStatus.Corrupt,
                Error = primaryError,
                Exception = primaryException,
                CorruptCopyPath = corruptCopyPath
            };
        }

        private LoadResult ApplyLoadOutcome(ReadOutcome outcome, Stopwatch sw, IProgress<float> progress)
        {
            switch (outcome.Status)
            {
                case LoadStatus.NoSaveFile:
                {
                    SaveableEntityRegistry.RegisterUnregisteredSceneEntities();
                    m_cachedDocument = null;
                    IsCorrupt = false;
                    HasLoaded = true;
                    progress?.Report(1f);
                    sw.Stop();
                    if (VerboseLogging)
                    {
                        Debug.Log($"[FlexibleSave] No save file at '{FullPath}'. Starting with clean state.");
                    }
                    var res = new LoadResult(LoadStatus.NoSaveFile, 0, 0, sw.Elapsed.TotalMilliseconds);
                    Loaded?.Invoke(res);
                    return res;
                }

                case LoadStatus.Corrupt:
                {
                    IsCorrupt = true;
                    HasLoaded = false;
                    progress?.Report(1f);
                    sw.Stop();
                    string message = $"Save file '{FullPath}' is corrupt ({outcome.Error})." +
                                     (string.IsNullOrEmpty(outcome.CorruptCopyPath)
                                         ? string.Empty
                                         : $" Corrupt copy saved to '{outcome.CorruptCopyPath}'.");
                    Debug.LogError($"[FlexibleSave] {message}");
                    RaiseError("Load", message, outcome.Exception);
                    var res = new LoadResult(LoadStatus.Corrupt, 0, 0, sw.Elapsed.TotalMilliseconds, message);
                    Loaded?.Invoke(res);
                    return res;
                }

                case LoadStatus.Ok:
                case LoadStatus.RestoredFromBackup:
                {
                    SaveableEntityRegistry.RestoreFromDocument(outcome.Document, progress, out int restored, out int skipped);
                    m_cachedDocument = outcome.Document;
                    IsCorrupt = false;
                    HasLoaded = true;
                    sw.Stop();

                    if (outcome.Status == LoadStatus.RestoredFromBackup)
                    {
                        Debug.LogWarning($"[FlexibleSave] Primary save '{FullPath}' was corrupt ({outcome.Error}); restored from backup.");
                    }
                    else if (VerboseLogging)
                    {
                        Debug.Log($"[FlexibleSave] Loaded {restored} root entities ({skipped} skipped) in {sw.Elapsed.TotalMilliseconds:F2} ms.");
                    }

                    var res = new LoadResult(outcome.Status, restored, skipped, sw.Elapsed.TotalMilliseconds, outcome.Error);
                    Loaded?.Invoke(res);
                    return res;
                }

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static string TryBackupCorruptFile(IFileSystem fs, string fullPath, byte[] primaryBytes)
        {
            try
            {
                string dir = Path.GetDirectoryName(fullPath);
                string baseName = Path.GetFileNameWithoutExtension(fullPath);
                string ext = Path.GetExtension(fullPath);
                string resolvedExt = string.IsNullOrEmpty(ext) ? ".json" : ext;

                string hashHex = primaryBytes != null ? ComputeSha256Hex(primaryBytes) : null;
                if (hashHex != null)
                {
                    s_lastQuarantinedCorruptCopies ??= new Dictionary<string, (string hashHex, string copyPath)>(StringComparer.OrdinalIgnoreCase);
                    if (s_lastQuarantinedCorruptCopies.TryGetValue(fullPath, out var cached) &&
                        string.Equals(cached.hashHex, hashHex, StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(cached.copyPath) &&
                        fs.Exists(cached.copyPath))
                    {
                        return cached.copyPath;
                    }

                    if (fs is PhysicalFileSystem)
                    {
                        string searchDir = string.IsNullOrEmpty(dir) ? "." : dir;
                        if (Directory.Exists(searchDir))
                        {
                            string existingMatch = FindExistingCorruptCopy(searchDir, baseName, resolvedExt, primaryBytes);
                            if (!string.IsNullOrEmpty(existingMatch))
                            {
                                s_lastQuarantinedCorruptCopies[fullPath] = (hashHex, existingMatch);
                                return existingMatch;
                            }
                        }
                    }
                }

                long utcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string copyName = $"{baseName}.corrupt.{utcMs}{resolvedExt}";
                string destPath = string.IsNullOrEmpty(dir) ? copyName : Path.Combine(dir, copyName);
                fs.Copy(fullPath, destPath, overwrite: true);

                if (hashHex != null)
                {
                    s_lastQuarantinedCorruptCopies[fullPath] = (hashHex, destPath);
                }

                return destPath;
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        private static string FindExistingCorruptCopy(string searchDir, string baseName, string resolvedExt, byte[] expectedBytes)
        {
            string[] existingCopies = Directory.GetFiles(searchDir, $"{baseName}.corrupt.*{resolvedExt}");
            for (int i = 0; i < existingCopies.Length; i++)
            {
                string candidate = existingCopies[i];
                try
                {
                    var info = new FileInfo(candidate);
                    if (info.Length != expectedBytes.Length)
                    {
                        continue;
                    }

                    byte[] candidateBytes = File.ReadAllBytes(candidate);
                    if (candidateBytes.Length == expectedBytes.Length)
                    {
                        bool equal = true;
                        for (int b = 0; b < expectedBytes.Length; b++)
                        {
                            if (candidateBytes[b] != expectedBytes[b])
                            {
                                equal = false;
                                break;
                            }
                        }

                        if (equal)
                        {
                            return candidate;
                        }
                    }
                }
                catch
                {
                    // Ignore unreadable candidate and continue checking.
                }
            }

            return null;
        }

        private static void TryDeleteFile(IFileSystem fs, string path)
        {
            try
            {
                if (fs.Exists(path))
                {
                    fs.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FlexibleSave] Could not delete '{path}': {ex.Message}");
            }
        }

        private SaveResult NotifySaveBlocked(string msg)
        {
            var result = new SaveResult(SaveStatus.Blocked, 0, msg);
            RaiseError("Save", msg);
            Saved?.Invoke(result);
            return result;
        }

        private void RaiseError(string operation, string message, Exception ex = null, bool logError = true)
        {
            if (logError && operation != "Load")
            {
                Debug.LogError($"[FlexibleSave] {operation} error: {message}");
            }
            Failed?.Invoke(new SaveError(operation, message, ex));
        }
    }
}

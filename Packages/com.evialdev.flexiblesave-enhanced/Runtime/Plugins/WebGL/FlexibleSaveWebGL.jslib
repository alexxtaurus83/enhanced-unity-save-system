mergeInto(LibraryManager.library, {
    FlexibleSave_SyncFileSystem: function () {
        if (typeof FS !== 'undefined' && typeof FS.syncfs === 'function') {
            FS.syncfs(false, function (err) {
                if (err) {
                    console.warn('FlexibleSave: FS.syncfs error:', err);
                }
            });
        }
    }
});

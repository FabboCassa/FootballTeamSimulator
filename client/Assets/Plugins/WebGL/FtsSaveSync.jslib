// WebGL save persistence (Roadmap 6.3).
// Application.persistentDataPath on WebGL is backed by Emscripten's IDBFS, but
// in-memory writes are only committed to the browser's IndexedDB when
// FS.syncfs(false, ...) is called. Unity flushes lazily; we flush explicitly
// right after every save/delete so a tab close or refresh can never lose a
// career. Reading back (sync true) happens once at startup, handled by Unity.
mergeInto(LibraryManager.library, {
  FtsSyncFilesystem: function () {
    try {
      if (typeof FS !== 'undefined' && FS && typeof FS.syncfs === 'function') {
        FS.syncfs(false, function (err) {
          if (err) console.error('[FTS] IndexedDB flush failed: ' + err);
        });
      }
    } catch (e) {
      console.error('[FTS] IndexedDB flush threw: ' + e);
    }
  }
});

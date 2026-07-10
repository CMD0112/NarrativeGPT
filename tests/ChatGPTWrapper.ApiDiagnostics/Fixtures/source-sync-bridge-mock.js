(function () {
  'use strict';

  var GIZMO_ID = 'g-p-mock-perf';
  var PROJECT_TITLE = 'Perf Mock Project';
  var REMOTE_SUFFIX = '\n\n<!-- remote-mock-content -->';

  var FILE_NAMES = [
    'scenario.md',
    'world.md',
    'plot.md',
    'cast.md',
    'instructions-snippet.md'
  ];

  var filesById = {};
  var orphanLibraryUploads = {};
  var nextUploadId = 1000;

  function initFiles() {
    for (var i = 0; i < FILE_NAMES.length; i++) {
      var id = 'mock-file-' + i;
      filesById[id] = {
        file_id: id,
        name: FILE_NAMES[i],
        location: 'sediment',
        text: '# ' + FILE_NAMES[i] + REMOTE_SUFFIX
      };
    }
  }

  initFiles();

  function delay() {
    var ms = globalThis.__cgwMockDelayMs || 0;
    if (ms <= 0) return;
    var end = Date.now() + ms;
    while (Date.now() < end) {}
  }

  function okResult(extra) {
    var base = { type: 'apiResult', ok: true, status: 200 };
    if (extra) {
      for (var k in extra) base[k] = extra[k];
    }
    return base;
  }

  function errResult(message) {
    return { type: 'apiError', ok: false, error: message || 'mock_error' };
  }

  function listFileArray() {
    var arr = [];
    for (var id in filesById) {
      if (Object.prototype.hasOwnProperty.call(filesById, id)) {
        var f = filesById[id];
        arr.push({ file_id: f.file_id, name: f.name, location: f.location || 'sediment' });
      }
    }
    return arr;
  }

  function buildGizmoNode() {
    return {
      id: GIZMO_ID,
      instructions: 'mock instructions for perf tests',
      training_disabled: false,
      display: { name: PROJECT_TITLE, description: '', prompt_starters: [] },
      sharing: [{ type: 'private', capabilities: { can_read: true, can_view_config: false, can_write: false, can_delete: false, can_export: false, can_share: false } }],
      tools: [],
      files: listFileArray()
    };
  }

  function buildDetailJson() {
    return { gizmo: { gizmo: buildGizmoNode() } };
  }

  function buildSidebarJson() {
    return {
      items: [
        {
          gizmo: {
            gizmo: buildGizmoNode(),
            files: listFileArray()
          }
        }
      ]
    };
  }

  function handleGetSession() {
    return okResult({
      json: { authenticated: true, accountId: 'mock-account-perf', user: { id: 'mock-user' } }
    });
  }

  function handleListProjects() {
    return okResult({
      json: {
        projects: [{ id: GIZMO_ID, title: PROJECT_TITLE, instructions: 'mock instructions for perf tests' }]
      }
    });
  }

  function handleApiRequest(cmd) {
    var method = (cmd.method || 'GET').toUpperCase();
    var path = cmd.path || '';

    if (method === 'GET' && path.indexOf('/backend-api/gizmos/snorlax/sidebar') >= 0) {
      return okResult({ json: buildSidebarJson() });
    }

    if (method === 'GET' && path.indexOf('/backend-api/gizmos/') >= 0 && path.indexOf(GIZMO_ID) >= 0) {
      return okResult({ json: buildDetailJson() });
    }

    if (method === 'GET' && path.indexOf('/backend-api/projects/' + GIZMO_ID + '/files') >= 0) {
      return okResult({ json: { files: listFileArray() } });
    }

    if (method === 'POST' && path.indexOf('/backend-api/projects/' + GIZMO_ID + '/files') >= 0) {
      var bodyFiles = (cmd.body && cmd.body.files) || [];
      for (var i = 0; i < bodyFiles.length; i++) {
        var bf = bodyFiles[i];
        var fid = bf.file_id || bf.fileId;
        if (!fid) continue;
        if (orphanLibraryUploads[fid]) {
          filesById[fid] = orphanLibraryUploads[fid];
          delete orphanLibraryUploads[fid];
        } else if (!filesById[fid]) {
          filesById[fid] = {
            file_id: fid,
            name: bf.name || fid,
            location: bf.location || 'sediment',
            text: '# attached ' + (bf.name || fid)
          };
        }
      }
      return okResult({ json: { files: listFileArray() } });
    }

    if (method === 'POST' && path.indexOf('/backend-api/gizmos/snorlax/upsert') >= 0) {
      var upsertBody = cmd.body || {};
      if (upsertBody.files && upsertBody.files.length) {
        for (var j = 0; j < upsertBody.files.length; j++) {
          var uf = upsertBody.files[j];
          var uid = uf.file_id || uf.fileId;
          if (!uid) continue;
          if (!filesById[uid]) {
            filesById[uid] = {
              file_id: uid,
              name: uf.name || uid,
              location: uf.location || 'sediment',
              text: '# upsert ' + (uf.name || uid)
            };
          }
        }
      }
      return okResult({ json: { gizmo: { gizmo: buildGizmoNode() } } });
    }

    if (method === 'DELETE') {
      return okResult({ json: {} });
    }

    return okResult({ json: {} });
  }

  function handleDownload(cmd) {
    var fileId = cmd.fileId || cmd.file_id;
    var entry = filesById[fileId];
    if (!entry) {
      var paths = cmd.paths || [];
      var attempts = paths.map(function (p) {
        return { status: 404, path: p };
      });
      var lastPath = paths.length ? paths[paths.length - 1] : '(none)';
      return {
        type: 'apiError',
        ok: false,
        error: 'download_failed',
        status: 404,
        message: 'download_failed 404 ' + lastPath,
        detail: { status: 404, path: lastPath, attempts: attempts }
      };
    }

    if (entry.requireProjectPath && cmd.paths && cmd.paths.length) {
      var genericOnly = cmd.paths.every(function (p) {
        return p.indexOf('/backend-api/files/') === 0;
      });
      if (genericOnly) {
        var failPath = cmd.paths[cmd.paths.length - 1];
        return {
          type: 'apiError',
          ok: false,
          error: 'download_failed',
          status: 404,
          message: 'download_failed 404 ' + failPath,
          detail: { status: 404, path: failPath }
        };
      }
    }

    var text = entry.text || '';
    return okResult({ base64: btoa(unescape(encodeURIComponent(text))) });
  }

  function handleUpload(cmd) {
    var id = 'mock-upload-' + (nextUploadId++);
    var text = cmd.base64 ? decodeURIComponent(escape(atob(cmd.base64))) : '# upload';
    var entry = {
      file_id: id,
      name: cmd.fileName || 'upload.md',
      location: 'sediment',
      text: text
    };

    if (cmd.useProjectLibrary === true && cmd.gizmoId) {
      orphanLibraryUploads[id] = entry;
      return okResult({
        fileId: id,
        libraryUpload: true,
        location: 'sediment',
        json: { file_id: id, name: cmd.fileName || 'upload.md' }
      });
    }

    filesById[id] = entry;
    return okResult({ fileId: id, json: { file_id: id, name: cmd.fileName || 'upload.md' } });
  }

  function handleDeleteProjectFile(cmd) {
    var fileId = cmd.fileId || cmd.file_id;
    if (fileId && filesById[fileId]) delete filesById[fileId];
    if (fileId && orphanLibraryUploads[fileId]) delete orphanLibraryUploads[fileId];
    return okResult({ json: {} });
  }

  globalThis.__cgwMockDelayMs = globalThis.__cgwMockDelayMs || 0;

  globalThis.__cgwApiInvoke = function (cmd) {
    delay();
    if (!cmd || !cmd.action) return errResult('missing_action');

    switch (cmd.action) {
      case 'ping':
        return { type: 'pong', ok: true };
      case 'echo':
        return okResult({ json: cmd });
      case 'getSession':
        return handleGetSession();
      case 'getApiContext':
        return okResult({ json: { authenticated: true, hasDeviceId: true, href: location.href } });
      case 'listProjects':
        return handleListProjects();
      case 'apiRequest':
        return handleApiRequest(cmd);
      case 'downloadFile':
        return handleDownload(cmd);
      case 'uploadFile':
        return handleUpload(cmd);
      case 'deleteProjectFile':
        return handleDeleteProjectFile(cmd);
      case 'attachProjectFile':
        return okResult({ json: { ok: true } });
      default:
        return errResult('unknown_action:' + cmd.action);
    }
  };
})();

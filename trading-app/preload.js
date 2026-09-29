// Puente seguro entre la ventana (renderer) y el proceso principal (Node).
// Solo expone funciones acotadas; el navegador nunca toca Node directamente.

'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  analyze: (args) => ipcRenderer.invoke('analyze', args),
  // Watchlist
  watchlistGet: () => ipcRenderer.invoke('watchlist:get'),
  watchlistAdd: (item) => ipcRenderer.invoke('watchlist:add', item),
  watchlistRemove: (item) => ipcRenderer.invoke('watchlist:remove', item),
  scanWatchlist: () => ipcRenderer.invoke('scan-watchlist'),
  // Ajustes
  settingsGet: () => ipcRenderer.invoke('settings:get'),
  settingsSet: (patch) => ipcRenderer.invoke('settings:set', patch),
  // Notificaciones nativas
  notify: (title, body) => ipcRenderer.invoke('notify', { title, body }),
});

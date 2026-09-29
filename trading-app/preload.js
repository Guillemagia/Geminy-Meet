// Puente seguro entre la ventana (renderer) y el proceso principal (Node).
// Solo expone una función acotada; el navegador nunca toca Node directamente.

'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  analyze: (args) => ipcRenderer.invoke('analyze', args),
});

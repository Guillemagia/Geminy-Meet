// ws.js — servidor WebSocket mínimo (RFC 6455) implementado solo con módulos nativos de Node.
// No usamos la librería "ws" de npm porque este entorno no tiene acceso a internet para instalarla,
// pero el protocolo es estándar así que funciona igual con cualquier navegador real.
const crypto = require('node:crypto');
const { EventEmitter } = require('node:events');

const WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';

class WSConnection extends EventEmitter {
  constructor(socket, id) {
    super();
    this.socket = socket;
    this.id = id;
    this.alive = true;
    this._buffer = Buffer.alloc(0);

    socket.on('data', (chunk) => this._onData(chunk));
    socket.on('close', () => {
      this.alive = false;
      this.emit('close');
    });
    socket.on('error', () => {
      this.alive = false;
    });
  }

  _onData(chunk) {
    this._buffer = Buffer.concat([this._buffer, chunk]);
    let frame;
    while ((frame = this._tryReadFrame())) {
      const { opcode, payload } = frame;
      if (opcode === 0x8) {
        // close
        this.close();
      } else if (opcode === 0x9) {
        this._sendRaw(0xa, payload); // pong
      } else if (opcode === 0x1 || opcode === 0x2) {
        this.emit('message', payload.toString('utf8'));
      }
    }
  }

  _tryReadFrame() {
    const buf = this._buffer;
    if (buf.length < 2) return null;
    const byte1 = buf[0];
    const byte2 = buf[1];
    const opcode = byte1 & 0x0f;
    const masked = (byte2 & 0x80) !== 0;
    let payloadLen = byte2 & 0x7f;
    let offset = 2;

    if (payloadLen === 126) {
      if (buf.length < offset + 2) return null;
      payloadLen = buf.readUInt16BE(offset);
      offset += 2;
    } else if (payloadLen === 127) {
      if (buf.length < offset + 8) return null;
      const high = buf.readUInt32BE(offset);
      const low = buf.readUInt32BE(offset + 4);
      payloadLen = high * 2 ** 32 + low;
      offset += 8;
    }

    let maskKey = null;
    if (masked) {
      if (buf.length < offset + 4) return null;
      maskKey = buf.slice(offset, offset + 4);
      offset += 4;
    }

    if (buf.length < offset + payloadLen) return null; // frame incompleto, esperar más datos

    let payload = buf.slice(offset, offset + payloadLen);
    if (masked) {
      const unmasked = Buffer.alloc(payload.length);
      for (let i = 0; i < payload.length; i++) {
        unmasked[i] = payload[i] ^ maskKey[i % 4];
      }
      payload = unmasked;
    }

    this._buffer = buf.slice(offset + payloadLen);
    return { opcode, payload };
  }

  _sendRaw(opcode, payload) {
    if (!this.alive) return;
    payload = Buffer.isBuffer(payload) ? payload : Buffer.from(payload || '');
    const len = payload.length;
    let header;
    if (len < 126) {
      header = Buffer.from([0x80 | opcode, len]);
    } else if (len < 65536) {
      header = Buffer.alloc(4);
      header[0] = 0x80 | opcode;
      header[1] = 126;
      header.writeUInt16BE(len, 2);
    } else {
      header = Buffer.alloc(10);
      header[0] = 0x80 | opcode;
      header[1] = 127;
      header.writeUInt32BE(Math.floor(len / 2 ** 32), 2);
      header.writeUInt32BE(len % 2 ** 32, 6);
    }
    try {
      this.socket.write(Buffer.concat([header, payload]));
    } catch {
      this.alive = false;
    }
  }

  send(obj) {
    const str = typeof obj === 'string' ? obj : JSON.stringify(obj);
    this._sendRaw(0x1, Buffer.from(str, 'utf8'));
  }

  close() {
    if (!this.alive) return;
    this.alive = false;
    try {
      this._sendRaw(0x8, Buffer.alloc(0));
      this.socket.end();
    } catch {}
    this.emit('close');
  }
}

class WSServer extends EventEmitter {
  // Se engancha al evento 'upgrade' de un http.Server existente.
  attach(httpServer, path = '/ws') {
    httpServer.on('upgrade', (req, socket, head) => {
      if (!req.url.startsWith(path)) {
        socket.destroy();
        return;
      }
      const key = req.headers['sec-websocket-key'];
      if (!key) {
        socket.destroy();
        return;
      }
      const accept = crypto
        .createHash('sha1')
        .update(key + WS_GUID)
        .digest('base64');

      const headers = [
        'HTTP/1.1 101 Switching Protocols',
        'Upgrade: websocket',
        'Connection: Upgrade',
        `Sec-WebSocket-Accept: ${accept}`,
        '',
        '',
      ].join('\r\n');
      socket.write(headers);

      const id = crypto.randomBytes(8).toString('hex');
      const conn = new WSConnection(socket, id);
      if (head && head.length) conn._onData(head);
      this.emit('connection', conn, req);
    });
  }
}

module.exports = { WSServer };

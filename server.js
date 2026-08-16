// server.js — backend real de Geminy Meet.
// Solo usa módulos nativos de Node (http, fs, crypto, node:sqlite) porque este entorno
// de desarrollo no tiene acceso a internet para instalar paquetes de npm. El protocolo
// HTTP y WebSocket que habla es 100% estándar, así que corre igual en cualquier hosting
// (Render, Railway, un VPS, etc.) sin cambiar nada del código.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { URL } = require('node:url');

const db = require('./db');
const auth = require('./auth');
const { WSServer } = require('./ws');

const PORT = process.env.PORT || 8080;
const RATE_PER_MIN = 10;
const COST_PHOTO = 5;
const COST_VIDEO = 15;
const MAX_BODY_BYTES = 12 * 1024 * 1024; // 12MB (para fotos/video en base64)

// ---------------- Helpers HTTP ----------------
function send(res, status, obj) {
  const body = JSON.stringify(obj);
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  });
  res.end(body);
}

function readJsonBody(req) {
  return new Promise((resolve, reject) => {
    let chunks = [];
    let size = 0;
    req.on('data', (c) => {
      size += c.length;
      if (size > MAX_BODY_BYTES) {
        reject(new Error('payload_too_large'));
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on('end', () => {
      if (!chunks.length) return resolve({});
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')));
      } catch {
        reject(new Error('invalid_json'));
      }
    });
    req.on('error', reject);
  });
}

function getBearer(req) {
  const h = req.headers['authorization'] || '';
  const m = h.match(/^Bearer (.+)$/);
  return m ? m[1] : null;
}

function requireAuth(req) {
  const token = getBearer(req);
  const userId = token ? auth.verify(token) : null;
  if (!userId) return null;
  return db.getUserById(userId);
}

function publicUser(user) {
  const wallet = db.getWallet(user.id);
  return {
    id: user.id,
    name: user.name,
    email: user.email,
    credits: wallet.credits_balance,
    earnings: wallet.earnings_balance,
  };
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// ---------------- Rutas API ----------------
const routes = {
  'POST /api/signup': async (req, res) => {
    const body = await readJsonBody(req);
    const name = (body.name || '').trim();
    const email = (body.email || '').trim().toLowerCase();
    const password = body.password || '';
    if (!name || !EMAIL_RE.test(email) || password.length < 6) {
      return send(res, 400, { error: 'Nombre, correo válido y contraseña de al menos 6 caracteres.' });
    }
    if (db.getUserByEmail(email)) {
      return send(res, 409, { error: 'Ya existe una cuenta con ese correo.' });
    }
    const { hash, salt } = auth.hashPassword(password);
    const user = db.createUser({ name, email, passwordHash: hash, passwordSalt: salt });
    const token = auth.sign(user.id);
    send(res, 201, { token, user: publicUser(user) });
  },

  'POST /api/login': async (req, res) => {
    const body = await readJsonBody(req);
    const email = (body.email || '').trim().toLowerCase();
    const password = body.password || '';
    const user = db.getUserByEmail(email);
    if (!user || !auth.verifyPassword(password, user.password_hash, user.password_salt)) {
      return send(res, 401, { error: 'Correo o contraseña incorrectos.' });
    }
    const token = auth.sign(user.id);
    send(res, 200, { token, user: publicUser(user) });
  },

  'GET /api/me': async (req, res) => {
    const user = requireAuth(req);
    if (!user) return send(res, 401, { error: 'No autenticado.' });
    send(res, 200, { user: publicUser(user) });
  },

  'POST /api/wallet/recharge': async (req, res) => {
    const user = requireAuth(req);
    if (!user) return send(res, 401, { error: 'No autenticado.' });
    const body = await readJsonBody(req);
    const amount = parseInt(body.amount, 10);
    const validPacks = [100, 550, 1200];
    if (!validPacks.includes(amount)) return send(res, 400, { error: 'Paquete inválido.' });
    // NOTA: esto es una recarga SIMULADA. En producción, este endpoint solo debe ejecutarse
    // desde un webhook verificado de Stripe (u otro procesador), nunca confiando en el cliente.
    db.addCredits(user.id, amount, 'Recarga simulada (demo, sin cobro real)');
    const wallet = db.getWallet(user.id);
    send(res, 200, { credits: wallet.credits_balance, simulated: true });
  },

  'GET /api/wallet/withdraw': async (req, res) => {
    const user = requireAuth(req);
    if (!user) return send(res, 401, { error: 'No autenticado.' });
    const wallet = db.getWallet(user.id);
    send(res, 200, {
      earnings: wallet.earnings_balance,
      status: 'not_implemented',
      note:
        'El retiro a cuenta bancaria requiere integrar un procesador de pagos con soporte de payouts ' +
        '(ej. Stripe Connect) y verificación de identidad (KYC). Aún no está implementado en este prototipo.',
    });
  },
};

function roomHistoryRoute(req, res, code) {
  const user = requireAuth(req);
  if (!user) return send(res, 401, { error: 'No autenticado.' });
  send(res, 200, { messages: db.getRoomHistory(code, 80) });
}

// ---------------- Servidor de archivos estáticos (frontend) ----------------
const PUBLIC_DIR = path.join(__dirname, 'public');
const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
};

function serveStatic(req, res, pathname) {
  let filePath = pathname === '/' ? '/index.html' : pathname;
  filePath = path.join(PUBLIC_DIR, filePath);
  if (!filePath.startsWith(PUBLIC_DIR)) {
    res.writeHead(403);
    return res.end('Forbidden');
  }
  fs.readFile(filePath, (err, data) => {
    if (err) {
      // fallback SPA -> index.html
      fs.readFile(path.join(PUBLIC_DIR, 'index.html'), (err2, data2) => {
        if (err2) {
          res.writeHead(404);
          return res.end('Not found');
        }
        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        res.end(data2);
      });
      return;
    }
    const ext = path.extname(filePath);
    res.writeHead(200, { 'Content-Type': MIME[ext] || 'application/octet-stream' });
    res.end(data);
  });
}

// ---------------- HTTP server ----------------
const server = http.createServer(async (req, res) => {
  const u = new URL(req.url, `http://${req.headers.host}`);
  const key = `${req.method} ${u.pathname}`;

  if (req.method === 'OPTIONS') {
    return send(res, 204, {});
  }

  const roomMatch = u.pathname.match(/^\/api\/rooms\/([a-z0-9-]+)\/messages$/i);
  if (req.method === 'GET' && roomMatch) {
    return roomHistoryRoute(req, res, roomMatch[1]);
  }

  if (routes[key]) {
    try {
      await routes[key](req, res);
    } catch (e) {
      if (e.message === 'payload_too_large') return send(res, 413, { error: 'Archivo demasiado grande.' });
      console.error(e);
      send(res, 500, { error: 'Error interno.' });
    }
    return;
  }

  if (u.pathname.startsWith('/api/')) {
    return send(res, 404, { error: 'Ruta no encontrada.' });
  }

  serveStatic(req, res, u.pathname);
});

// ---------------- WebSocket: chat, señalización WebRTC y cobro por créditos ----------------
const wss = new WSServer();
wss.attach(server, '/ws');

// roomCode -> Map(connId -> {conn, userId, name})
const rooms = new Map();
// userId -> Set(connId)  (para empujar actualizaciones de saldo a todas las pestañas abiertas)
const userConns = new Map();

function roomPeers(code) {
  const r = rooms.get(code);
  if (!r) return [];
  return [...r.values()].map((p) => ({ id: p.conn.id, name: p.name, userId: p.userId }));
}

function broadcastRoom(code, msg, exceptConnId) {
  const r = rooms.get(code);
  if (!r) return;
  for (const [connId, p] of r) {
    if (connId !== exceptConnId) p.conn.send(msg);
  }
}

function pushWallet(userId) {
  const wallet = db.getWallet(userId);
  const set = userConns.get(userId);
  if (!set) return;
  for (const conn of set) {
    conn.send({ type: 'wallet', credits: wallet.credits_balance, earnings: wallet.earnings_balance });
  }
}

function otherPeerInRoom(code, connId) {
  const r = rooms.get(code);
  if (!r) return null;
  for (const [id, p] of r) if (id !== connId) return p;
  return null;
}

wss.on('connection', (conn, req) => {
  const u = new URL(req.url, 'http://localhost');
  const token = u.searchParams.get('token');
  const userId = token ? require('./auth').verify(token) : null;
  const user = userId ? db.getUserById(userId) : null;

  if (!user) {
    conn.send({ type: 'error', message: 'Token inválido o vencido. Vuelve a iniciar sesión.' });
    conn.close();
    return;
  }

  let currentRoom = null;

  if (!userConns.has(user.id)) userConns.set(user.id, new Set());
  userConns.get(user.id).add(conn);

  conn.on('message', (raw) => {
    let msg;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }

    if (msg.type === 'join') {
      const code = String(msg.room || '').toLowerCase().trim();
      if (!code) return;
      db.ensureRoom(code);
      currentRoom = code;
      if (!rooms.has(code)) rooms.set(code, new Map());
      rooms.get(code).set(conn.id, { conn, userId: user.id, name: user.name });

      conn.send({
        type: 'joined',
        room: code,
        peers: roomPeers(code).filter((p) => p.id !== conn.id),
        history: db.getRoomHistory(code, 80),
      });
      broadcastRoom(code, { type: 'peer-joined', id: conn.id, name: user.name, userId: user.id }, conn.id);
      return;
    }

    if (!currentRoom) return; // hay que unirse a una sala antes de cualquier otra acción

    if (msg.type === 'chat') {
      const text = String(msg.text || '').slice(0, 2000);
      if (!text) return;
      db.saveMessage({ roomCode: currentRoom, userId: user.id, name: user.name, kind: 'text', content: text, cost: 0 });
      broadcastRoom(currentRoom, { type: 'chat', from: conn.id, userId: user.id, name: user.name, text });
      return;
    }

    if (msg.type === 'media') {
      const isVideo = !!msg.isVideo;
      const cost = isVideo ? COST_VIDEO : COST_PHOTO;
      const recipient = otherPeerInRoom(currentRoom, conn.id);
      const ok = db.transferCredits(
        user.id,
        recipient ? recipient.userId : null,
        cost,
        (isVideo ? 'Video' : 'Foto') + ' en sala ' + currentRoom
      );
      if (!ok) {
        conn.send({ type: 'error', message: 'No tienes créditos suficientes.' });
        return;
      }
      db.saveMessage({
        roomCode: currentRoom,
        userId: user.id,
        name: user.name,
        kind: isVideo ? 'video' : 'photo',
        content: msg.data,
        cost,
      });
      broadcastRoom(currentRoom, {
        type: 'media',
        from: conn.id,
        userId: user.id,
        name: user.name,
        mime: msg.mime,
        data: msg.data,
        isVideo,
        cost,
      });
      pushWallet(user.id);
      if (recipient) pushWallet(recipient.userId);
      return;
    }

    if (msg.type === 'call-tick') {
      // El cliente pide facturar otro minuto de llamada. El servidor es la única fuente de verdad:
      // si no hay créditos, se corta.
      const recipient = otherPeerInRoom(currentRoom, conn.id);
      const ok = db.transferCredits(
        user.id,
        recipient ? recipient.userId : null,
        RATE_PER_MIN,
        'Minuto de videollamada en sala ' + currentRoom
      );
      if (!ok) {
        conn.send({ type: 'call-denied', reason: 'Sin créditos suficientes.' });
        return;
      }
      conn.send({ type: 'call-billed', amount: RATE_PER_MIN });
      pushWallet(user.id);
      if (recipient) pushWallet(recipient.userId);
      return;
    }

    if (msg.type === 'signal') {
      // Relay de señalización WebRTC (offer/answer/ICE candidates) al otro miembro de la sala.
      const recipient = otherPeerInRoom(currentRoom, conn.id);
      if (recipient) recipient.conn.send({ type: 'signal', from: conn.id, payload: msg.payload });
      return;
    }
  });

  conn.on('close', () => {
    const set = userConns.get(user.id);
    if (set) {
      set.delete(conn);
      if (!set.size) userConns.delete(user.id);
    }
    if (currentRoom && rooms.has(currentRoom)) {
      rooms.get(currentRoom).delete(conn.id);
      broadcastRoom(currentRoom, { type: 'peer-left', id: conn.id, userId: user.id });
      if (!rooms.get(currentRoom).size) rooms.delete(currentRoom);
    }
  });
});

server.listen(PORT, () => {
  console.log(`Geminy Meet backend corriendo en http://localhost:${PORT}`);
});

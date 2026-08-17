// server.js — backend real de Geminy Meet.
// Solo usa módulos nativos de Node (http, https, fs, crypto, node:sqlite) porque este entorno
// de desarrollo no tiene acceso a internet para instalar paquetes de npm. El protocolo HTTP y
// WebSocket que habla es 100% estándar, así que corre igual en cualquier hosting sin cambios.
const http = require('node:http');
const https = require('node:https');
const fs = require('node:fs');
const path = require('node:path');
const { URL } = require('node:url');

const db = require('./db');
const auth = require('./auth');
const { WSServer } = require('./ws');

const PORT = process.env.PORT || 8080;
const MAX_BODY_BYTES = 12 * 1024 * 1024; // 12MB (fotos/video en base64)
const MAX_MEDIA_PRICE = 1000; // tope de créditos que una anfitriona puede pedir por una foto/video
const AUTO_OPENER_DELAY_MS = 20000;

// Reparto de ganancias: cuánto se lleva la anfitriona de cada crédito que gasta un miembro.
const COMPANION_SHARE_PERCENT = 70;

// Clave para las rutas de administrador. CAMBIA ESTO antes de desplegar (variable de entorno ADMIN_KEY en Render).
const ADMIN_KEY = process.env.ADMIN_KEY || 'cambia-esta-clave';

// Clave de DeepL para el botón "Traducir" (opcional). Sin esto, el botón responde con un error
// explicando que falta configurar el traductor — el resto de la app funciona igual sin ella.
const DEEPL_API_KEY = process.env.DEEPL_API_KEY || '';

// Catálogo de 20 regalos (debe coincidir con el catálogo de public/index.html, que solo se usa ahí para mostrar precios).
const GIFTS = {
  rose:       { name: 'Rosa',            emoji: '🌹', cost: 5 },
  tulip:      { name: 'Tulipán',         emoji: '🌷', cost: 8 },
  chocolate:  { name: 'Chocolate',       emoji: '🍫', cost: 12 },
  teddybear:  { name: 'Oso de peluche',  emoji: '🧸', cost: 15 },
  heart:      { name: 'Corazón',         emoji: '💖', cost: 20 },
  lipstick:   { name: 'Labial',          emoji: '💄', cost: 25 },
  perfume:    { name: 'Perfume',         emoji: '🌸', cost: 35 },
  champagne:  { name: 'Champaña',        emoji: '🍾', cost: 45 },
  star:       { name: 'Estrella',        emoji: '⭐', cost: 50 },
  balloon:    { name: 'Globo',           emoji: '🎈', cost: 55 },
  fireworks:  { name: 'Fuegos artificiales', emoji: '🎆', cost: 65 },
  butterfly:  { name: 'Mariposa',        emoji: '🦋', cost: 70 },
  ring:       { name: 'Anillo',          emoji: '💍', cost: 90 },
  watch:      { name: 'Reloj',           emoji: '⌚', cost: 110 },
  crown:      { name: 'Corona',          emoji: '👑', cost: 130 },
  sportscar:  { name: 'Auto deportivo',  emoji: '🏎️', cost: 160 },
  yacht:      { name: 'Yate',            emoji: '🛥️', cost: 200 },
  mansion:    { name: 'Mansión',         emoji: '🏰', cost: 260 },
  diamond:    { name: 'Diamante',        emoji: '💎', cost: 320 },
  rocket:     { name: 'Cohete',          emoji: '🚀', cost: 500 },
};

// Presencia en vivo: userId -> 'online' | 'in-call'. Ausente = desconectado.
// Se actualiza desde los manejadores de WebSocket más abajo, pero se declara aquí
// porque también la usa la ruta GET /api/discover.
const userPresence = new Map();
function presenceStatus(userId) {
  return userPresence.get(userId) || 'offline';
}

// ---------------- Helpers HTTP ----------------
function send(res, status, obj) {
  const body = JSON.stringify(obj);
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Admin-Key',
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

function safeJsonArray(text) {
  try {
    const v = JSON.parse(text || '[]');
    return Array.isArray(v) ? v : [];
  } catch {
    return [];
  }
}

// Código de sala estable y determinístico para una pareja de usuarios (no depende de quién lo pida).
const crypto = require('node:crypto');
function dmRoomCode(idA, idB) {
  const [x, y] = [idA, idB].sort();
  return 'dm-' + crypto.createHash('sha1').update(x + ':' + y).digest('hex').slice(0, 12);
}

function publicUser(user) {
  const wallet = db.getWallet(user.id);
  const tier = db.tierForMinutes(user.worked_minutes || 0);
  const offer = user.role === 'member' ? db.getActiveOffer(user.id) : null;
  return {
    id: user.id,
    name: user.name,
    email: user.email,
    role: user.role,
    verificationStatus: user.verification_status,
    referralCode: user.role === 'companion' ? user.referral_code : null,
    age: user.age,
    bio: user.bio,
    avatarUrl: user.avatar_url,
    gallery: safeJsonArray(user.gallery),
    openers: safeJsonArray(user.openers),
    preferredLanguage: user.preferred_language,
    credits: wallet.credits_balance,
    earnings: wallet.earnings_balance,
    tier: user.role === 'companion' ? { name: tier.name, ratePerMin: tier.rate, workedMinutes: user.worked_minutes } : null,
    ratings: user.role === 'companion' ? db.getRatingSummary(user.id) : null,
    activeOffer: offer ? { percentBonus: offer.percent_bonus, reason: offer.reason, expiresAt: offer.expires_at } : null,
    firstPurchaseAvailable: user.role === 'member' && !user.first_purchase_used,
  };
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// ---------------- Traducción (DeepL) ----------------
function deeplTranslate(text, targetLang) {
  return new Promise((resolve, reject) => {
    if (!DEEPL_API_KEY) return reject(new Error('DEEPL_API_KEY no configurada en el servidor.'));
    const body = new URLSearchParams({ text, target_lang: (targetLang || 'EN').toUpperCase() }).toString();
    const reqOpts = {
      hostname: 'api-free.deepl.com',
      path: '/v2/translate',
      method: 'POST',
      headers: {
        Authorization: `DeepL-Auth-Key ${DEEPL_API_KEY}`,
        'Content-Type': 'application/x-www-form-urlencoded',
        'Content-Length': Buffer.byteLength(body),
      },
    };
    const r = https.request(reqOpts, (resp) => {
      let data = '';
      resp.on('data', (c) => (data += c));
      resp.on('end', () => {
        try {
          const json = JSON.parse(data);
          if (json.translations && json.translations[0]) {
            resolve({ text: json.translations[0].text, detectedSourceLang: json.translations[0].detected_source_language });
          } else {
            reject(new Error(json.message || 'Respuesta inesperada del traductor.'));
          }
        } catch (e) {
          reject(e);
        }
      });
    });
    r.on('error', reject);
    r.write(body);
    r.end();
  });
}

// ---------------- Rutas API ----------------
const routes = {
  'POST /api/signup': async (req, res) => {
    const body = await readJsonBody(req);
    const name = (body.name || '').trim();
    const email = (body.email || '').trim().toLowerCase();
    const password = body.password || '';
    const role = body.role === 'companion' ? 'companion' : 'member';
    const preferredLanguage = (body.preferredLanguage || 'es').slice(0, 5);
    if (!name || !EMAIL_RE.test(email) || password.length < 6) {
      return send(res, 400, { error: 'Nombre, correo válido y contraseña de al menos 6 caracteres.' });
    }
    if (db.getUserByEmail(email)) {
      return send(res, 409, { error: 'Ya existe una cuenta con ese correo.' });
    }
    let avatarUrl = '';
    if (role === 'member') {
      const n = Math.floor(Math.random() * MEMBER_AVATAR_COUNT) + 1;
      avatarUrl = `/avatars/male-${String(n).padStart(2, '0')}.svg`;
    }
    // Código de invitación opcional: una anfitriona lo comparte para atraer clientes.
    // Solo aplica cuando un MIEMBRO se registra con el código de una anfitriona.
    let referredByUser = null;
    if (role === 'member' && body.inviteCode) {
      const owner = db.getUserByReferralCode(body.inviteCode);
      if (owner && owner.role === 'companion') referredByUser = owner;
    }
    const { hash, salt } = auth.hashPassword(password);
    const user = db.createUser({
      name, email, passwordHash: hash, passwordSalt: salt, role, avatarUrl, preferredLanguage,
      referredByUserId: referredByUser ? referredByUser.id : null,
    });
    const token = auth.sign(user.id);
    send(res, 201, {
      token,
      user: publicUser(user),
      referredCompanion: referredByUser ? { id: referredByUser.id, name: referredByUser.name, dmRoom: dmRoomCode(user.id, referredByUser.id) } : null,
    });
  },

  'POST /api/login': async (req, res) => {
    const body = await readJsonBody(req);
    const email = (body.email || '').trim().toLowerCase();
    const password = body.password || '';
    const user = db.getUserByEmail(email);
    if (!user || !auth.verifyPassword(password, user.password_hash, user.password_salt)) {
      return send(res, 401, { error: 'Correo o contraseña incorrectos.' });
    }
    db.checkAndIssueLapsedDiscount(user);
    const token = auth.sign(user.id);
    send(res, 200, { token, user: publicUser(db.getUserById(user.id)) });
  },

  'GET /api/me': async (req, res) => {
    const user = requireAuth(req);
    if (!user) return send(res, 401, { error: 'No autenticado.' });
    db.checkAndIssueLapsedDiscount(user);
    send(res, 200, { user: publicUser(db.getUserById(user.id)) });
  },

  'POST /api/profile': async (req, res) => {
    const user = requireAuth(req);
    if (!user) return send(res, 401, { error: 'No autenticado.' });
    const body = await readJsonBody(req);
    const patch = {};
    if (typeof body.bio === 'string') patch.bio = body.bio.slice(0, 500);
    if (Number.isInteger(body.age) && body.age >= 18 && body.age <= 99) patch.age = body.age;
    if (typeof body.avatarUrl === 'string') patch.avatarUrl = body.avatarUrl.slice(0, 500000);
    if (Array.isArray(body.gallery)) patch.gallery = body.gallery.slice(0, 6);
    if (Array.isArray(body.openers)) patch.openers = body.openers.filter((s) => typeof s === 'string').slice(0, 5).map((s) => s.slice(0, 200));
    if (typeof body.preferredLanguage === 'string') patch.preferredLanguage = body.preferredLanguage.slice(0, 5);
    const updated = db.updateProfile(user.id, patch);
    send(res, 200, { user: publicUser(updated) });
  },

  'GET /api/discover': async (req, res) => {
    const requester = requireAuth(req);
    if (!requester) return send(res, 401, { error: 'No autenticado.' });
    const oppositeRole = requester.role === 'member' ? 'companion' : 'member';
    const rows = db.listDiscoverable(oppositeRole);
    const profiles = rows.map((u) => ({
      ...publicProfile(u),
      status: presenceStatus(u.id),
      isFollowing: db.isFollowing(requester.id, u.id),
      dmRoom: dmRoomCode(requester.id, u.id),
    }));
    send(res, 200, { profiles });
  },

  'POST /api/follow/toggle': async (req, res) => {
    const requester = requireAuth(req);
    if (!requester) return send(res, 401, { error: 'No autenticado.' });
    const body = await readJsonBody(req);
    if (!body.userId || body.userId === requester.id) return send(res, 400, { error: 'userId inválido.' });
    const following = db.toggleFollow(requester.id, body.userId);
    send(res, 200, { following });
  },

  'GET /api/gifts': async (req, res) => {
    send(res, 200, { gifts: GIFTS });
  },

  'POST /api/translate': async (req, res) => {
    const user = requireAuth(req);
    if (!user) return send(res, 401, { error: 'No autenticado.' });
    const body = await readJsonBody(req);
    const text = (body.text || '').slice(0, 2000);
    if (!text) return send(res, 400, { error: 'Falta texto para traducir.' });
    try {
      const result = await deeplTranslate(text, body.targetLang || 'EN');
      send(res, 200, result);
    } catch (e) {
      send(res, 502, { error: 'No se pudo traducir: ' + e.message });
    }
  },

  'POST /api/wallet/recharge': async (req, res) => {
    const user = requireAuth(req);
    if (!user) return send(res, 401, { error: 'No autenticado.' });
    const body = await readJsonBody(req);
    const amount = parseInt(body.amount, 10);
    const validPacks = [100, 550, 1200];
    if (!validPacks.includes(amount)) return send(res, 400, { error: 'Paquete inválido.' });

    // NOTA: recarga SIMULADA. En producción este endpoint solo debe ejecutarse desde un
    // webhook verificado de Apple In-App Purchase / Stripe, nunca confiando en el cliente.
    let finalAmount = amount;
    let bonusApplied = null;
    if (!user.first_purchase_used) {
      finalAmount = Math.round(amount * (1 + db.FIRST_PURCHASE_BONUS_PERCENT / 100));
      db.markFirstPurchaseUsed(user.id);
      bonusApplied = { type: 'first_purchase', percent: db.FIRST_PURCHASE_BONUS_PERCENT };
    } else {
      const offer = db.getActiveOffer(user.id);
      if (offer) {
        finalAmount = Math.round(amount * (1 + offer.percent_bonus / 100));
        db.claimOffer(offer.id);
        bonusApplied = { type: 'lapsed_offer', percent: offer.percent_bonus };
      }
    }
    db.addCredits(user.id, finalAmount, 'Recarga simulada (demo, sin cobro real)' + (bonusApplied ? ` +bono ${bonusApplied.percent}%` : ''));
    const wallet = db.getWallet(user.id);
    send(res, 200, { credits: wallet.credits_balance, simulated: true, bonusApplied });
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

  'GET /api/admin/companions/pending': async (req, res) => {
    if (req.headers['x-admin-key'] !== ADMIN_KEY) return send(res, 403, { error: 'No autorizado.' });
    send(res, 200, { companions: db.listPendingCompanions() });
  },

  'POST /api/admin/companions/approve': async (req, res) => {
    if (req.headers['x-admin-key'] !== ADMIN_KEY) return send(res, 403, { error: 'No autorizado.' });
    const body = await readJsonBody(req);
    if (!body.userId) return send(res, 400, { error: 'Falta userId.' });
    const user = db.setVerificationStatus(body.userId, 'approved');
    send(res, 200, { user: user ? publicUser(user) : null });
  },

  'GET /api/admin/revenue': async (req, res) => {
    if (req.headers['x-admin-key'] !== ADMIN_KEY) return send(res, 403, { error: 'No autorizado.' });
    send(res, 200, { totalPlatformRevenue: db.getPlatformRevenueTotal() });
  },
};

function publicProfile(user) {
  const tier = db.tierForMinutes(user.worked_minutes || 0);
  return {
    id: user.id,
    name: user.name,
    role: user.role,
    age: user.age,
    bio: user.bio,
    avatarUrl: user.avatar_url,
    gallery: safeJsonArray(user.gallery),
    tier: user.role === 'companion' ? { name: tier.name, ratePerMin: tier.rate } : null,
    ratings: user.role === 'companion' ? db.getRatingSummary(user.id) : null,
  };
}

function userProfileRoute(req, res, id) {
  const requester = requireAuth(req);
  if (!requester) return send(res, 401, { error: 'No autenticado.' });
  const target = db.getUserById(id);
  if (!target) return send(res, 404, { error: 'Usuario no encontrado.' });
  send(res, 200, {
    profile: {
      ...publicProfile(target),
      status: presenceStatus(target.id),
      isFollowing: db.isFollowing(requester.id, target.id),
      dmRoom: dmRoomCode(requester.id, target.id),
    },
  });
}

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
  '.jpg': 'image/jpeg',
};

// Cuántos avatares placeholder de miembro existen en public/avatars/ (male-01.svg ... male-N.svg).
const MEMBER_AVATAR_COUNT = 8;

function serveStatic(req, res, pathname) {
  let filePath = pathname === '/' ? '/index.html' : pathname;
  filePath = path.join(PUBLIC_DIR, filePath);
  if (!filePath.startsWith(PUBLIC_DIR)) {
    res.writeHead(403);
    return res.end('Forbidden');
  }
  fs.readFile(filePath, (err, data) => {
    if (err) {
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

  if (req.method === 'OPTIONS') return send(res, 204, {});

  const roomMatch = u.pathname.match(/^\/api\/rooms\/([a-z0-9-]+)\/messages$/i);
  if (req.method === 'GET' && roomMatch) return roomHistoryRoute(req, res, roomMatch[1]);

  const userMatch = u.pathname.match(/^\/api\/users\/([a-z0-9]+)$/i);
  if (req.method === 'GET' && userMatch) return userProfileRoute(req, res, userMatch[1]);

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

  if (u.pathname.startsWith('/api/')) return send(res, 404, { error: 'Ruta no encontrada.' });

  serveStatic(req, res, u.pathname);
});

// ---------------- WebSocket: chat, señalización WebRTC y cobro por créditos ----------------
const wss = new WSServer();
wss.attach(server, '/ws');

// roomCode -> Map(connId -> {conn, userId, name, role, hasSpoken})
const rooms = new Map();
// userId -> Set(connId)
const userConns = new Map();
// Todas las conexiones autenticadas, para difundir cambios de presencia a todo el mundo.
const allConns = new Set();
// roomCode -> Set(connId ya con auto-apertura programada o enviada), evita duplicados
const autoOpenerHandled = new Map();

function broadcastPresence(userId, status) {
  const payload = { type: 'presence-update', userId, status };
  for (const conn of allConns) conn.send(payload);
}

function roomPeers(code) {
  const r = rooms.get(code);
  if (!r) return [];
  return [...r.values()].map((p) => ({ id: p.conn.id, name: p.name, userId: p.userId, role: p.role }));
}

function broadcastRoom(code, msg, exceptConnId) {
  const r = rooms.get(code);
  if (!r) return;
  for (const [connId, p] of r) if (connId !== exceptConnId) p.conn.send(msg);
}

function pushWallet(userId) {
  const wallet = db.getWallet(userId);
  const user = db.getUserById(userId);
  const set = userConns.get(userId);
  if (!set) return;
  const offer = user && user.role === 'member' ? db.getActiveOffer(userId) : null;
  for (const conn of set) {
    conn.send({
      type: 'wallet',
      credits: wallet.credits_balance,
      earnings: wallet.earnings_balance,
      activeOffer: offer ? { percentBonus: offer.percent_bonus, expiresAt: offer.expires_at } : null,
    });
  }
}

function otherPeerInRoom(code, connId) {
  const r = rooms.get(code);
  if (!r) return null;
  for (const [id, p] of r) if (id !== connId) return p;
  return null;
}

function maybeScheduleAutoOpener(roomCode) {
  const r = rooms.get(roomCode);
  if (!r || r.size < 2) return;
  const peers = [...r.values()];
  const companionPresence = peers.find((p) => p.role === 'companion');
  if (!companionPresence) return;

  if (!autoOpenerHandled.has(roomCode)) autoOpenerHandled.set(roomCode, new Set());
  const handled = autoOpenerHandled.get(roomCode);
  if (handled.has(companionPresence.conn.id)) return; // ya programado o ya envió algo
  handled.add(companionPresence.conn.id);

  setTimeout(() => {
    const stillRoom = rooms.get(roomCode);
    if (!stillRoom) return;
    const stillPresence = stillRoom.get(companionPresence.conn.id);
    if (!stillPresence || stillPresence.hasSpoken) return; // ya escribió algo real, o se fue
    if (stillRoom.size < 2) return; // el miembro ya no está

    const companionUser = db.getUserById(companionPresence.userId);
    const openers = safeJsonArray(companionUser ? companionUser.openers : '[]');
    const DEFAULT_OPENERS = ['¡Hola! ¿Cómo estás hoy? 😊', 'Hola, qué gusto verte por aquí 💕', '¡Hey! ¿Tienes tiempo para platicar un rato?'];
    const pool = openers.length ? openers : DEFAULT_OPENERS;
    const text = pool[Math.floor(Math.random() * pool.length)];

    db.saveMessage({ roomCode, userId: companionPresence.userId, name: companionPresence.name, kind: 'text', content: text, cost: 0, isAuto: true });
    broadcastRoom(roomCode, { type: 'chat', from: companionPresence.conn.id, userId: companionPresence.userId, name: companionPresence.name, text, isAuto: true });
  }, AUTO_OPENER_DELAY_MS);
}

wss.on('connection', (conn, req) => {
  const u = new URL(req.url, 'http://localhost');
  const token = u.searchParams.get('token');
  const userId = token ? auth.verify(token) : null;
  const authedUser = userId ? db.getUserById(userId) : null;

  if (!authedUser) {
    conn.send({ type: 'error', message: 'Token inválido o vencido. Vuelve a iniciar sesión.' });
    conn.close();
    return;
  }

  let currentRoom = null;

  allConns.add(conn);
  const isFirstConnForUser = !userConns.has(authedUser.id);
  if (!userConns.has(authedUser.id)) userConns.set(authedUser.id, new Set());
  userConns.get(authedUser.id).add(conn);
  if (isFirstConnForUser) {
    userPresence.set(authedUser.id, 'online');
    broadcastPresence(authedUser.id, 'online');
  }

  conn.on('message', (raw) => {
    let msg;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }
    const user = db.getUserById(authedUser.id); // fresco, por si cambió su rol/perfil

    if (msg.type === 'join') {
      const code = String(msg.room || '').toLowerCase().trim();
      if (!code) return;
      db.ensureRoom(code);
      db.checkAndIssueLapsedDiscount(user);
      currentRoom = code;
      if (!rooms.has(code)) rooms.set(code, new Map());
      rooms.get(code).set(conn.id, { conn, userId: user.id, name: user.name, role: user.role, hasSpoken: false });

      conn.send({
        type: 'joined',
        room: code,
        peers: roomPeers(code).filter((p) => p.id !== conn.id),
        history: db.getRoomHistory(code, 80),
      });
      broadcastRoom(code, { type: 'peer-joined', id: conn.id, name: user.name, userId: user.id, role: user.role }, conn.id);
      maybeScheduleAutoOpener(code);
      return;
    }

    if (msg.type === 'presence') {
      // Estado global (en línea / en llamada), no depende de estar en una sala.
      const status = msg.status === 'in-call' ? 'in-call' : 'online';
      userPresence.set(user.id, status);
      broadcastPresence(user.id, status);
      return;
    }

    if (!currentRoom) return;

    if (msg.type === 'chat') {
      const text = String(msg.text || '').slice(0, 2000);
      if (!text) return;
      const presence = rooms.get(currentRoom) && rooms.get(currentRoom).get(conn.id);
      if (presence) presence.hasSpoken = true; // cancela la apertura automática si ya habló de verdad
      db.saveMessage({ roomCode: currentRoom, userId: user.id, name: user.name, kind: 'text', content: text, cost: 0 });
      broadcastRoom(currentRoom, { type: 'chat', from: conn.id, userId: user.id, name: user.name, text });
      return;
    }

    if (msg.type === 'media') {
      // Los mensajes de texto son siempre gratis. Fotos/video: si el remitente es anfitriona,
      // ELLA decide el precio en el momento de enviar (0 = gratis, o el precio que quiera, con tope).
      // OJO: quien PAGA es quien RECIBE el archivo (el miembro), no quien lo envía.
      const isVideo = !!msg.isVideo;
      let price = 0;
      if (user.role === 'companion') {
        price = Math.max(0, Math.min(MAX_MEDIA_PRICE, parseInt(msg.price, 10) || 0));
      }
      const recipient = otherPeerInRoom(currentRoom, conn.id);
      const recipientUser = recipient ? db.getUserById(recipient.userId) : null;

      if (price > 0) {
        if (!recipientUser) {
          conn.send({ type: 'error', message: 'No hay nadie en la sala que reciba el cobro.' });
          return;
        }
        const ok = db.transferCreditsWithSplit(
          recipientUser.id,
          user,
          price,
          (isVideo ? 'Video' : 'Foto') + ' en sala ' + currentRoom,
          COMPANION_SHARE_PERCENT
        );
        if (!ok) {
          conn.send({ type: 'error', message: 'La otra persona no tiene créditos suficientes para esta foto/video — no se envió.' });
          return;
        }
      }

      db.saveMessage({ roomCode: currentRoom, userId: user.id, name: user.name, kind: isVideo ? 'video' : 'photo', content: msg.data, cost: price });
      broadcastRoom(currentRoom, {
        type: 'media', from: conn.id, userId: user.id, name: user.name,
        mime: msg.mime, data: msg.data, isVideo, cost: price,
      });
      pushWallet(user.id);
      if (recipientUser) pushWallet(recipientUser.id);
      return;
    }

    if (msg.type === 'gift') {
      const gift = GIFTS[msg.giftId];
      if (!gift) {
        conn.send({ type: 'error', message: 'Ese regalo no existe.' });
        return;
      }
      const recipient = otherPeerInRoom(currentRoom, conn.id);
      const recipientUser = recipient ? db.getUserById(recipient.userId) : null;
      const ok = db.transferCreditsWithSplit(user.id, recipientUser, gift.cost, 'Regalo (' + gift.name + ') en sala ' + currentRoom, COMPANION_SHARE_PERCENT);
      if (!ok) {
        conn.send({ type: 'error', message: 'No tienes créditos suficientes para ese regalo.' });
        return;
      }
      db.saveMessage({ roomCode: currentRoom, userId: user.id, name: user.name, kind: 'gift', content: msg.giftId, cost: gift.cost });
      broadcastRoom(currentRoom, {
        type: 'gift', from: conn.id, userId: user.id, name: user.name,
        giftId: msg.giftId, giftName: gift.name, emoji: gift.emoji, cost: gift.cost,
      });
      pushWallet(user.id);
      if (recipientUser) pushWallet(recipientUser.id);
      return;
    }

    if (msg.type === 'call-tick') {
      const recipient = otherPeerInRoom(currentRoom, conn.id);
      const recipientUser = recipient ? db.getUserById(recipient.userId) : null;
      const rate = recipientUser && recipientUser.role === 'companion'
        ? db.tierForMinutes(recipientUser.worked_minutes || 0).rate
        : 10;
      const ok = db.transferCreditsWithSplit(user.id, recipientUser, rate, 'Minuto de videollamada en sala ' + currentRoom, COMPANION_SHARE_PERCENT);
      if (!ok) {
        conn.send({ type: 'call-denied', reason: 'Sin créditos suficientes.' });
        return;
      }
      if (recipientUser && recipientUser.role === 'companion') db.addWorkedMinutes(recipientUser.id, 1);
      conn.send({ type: 'call-billed', amount: rate });
      pushWallet(user.id);
      if (recipientUser) pushWallet(recipientUser.id);
      return;
    }

    if (msg.type === 'rate-call') {
      // El miembro califica a la anfitriona al colgar. Anónimo: ella solo ve el promedio.
      const recipient = otherPeerInRoom(currentRoom, conn.id);
      const recipientUser = recipient ? db.getUserById(recipient.userId) : null;
      if (user.role !== 'member' || !recipientUser || recipientUser.role !== 'companion') return;
      const clamp = (v) => Math.max(0, Math.min(5, parseInt(v, 10) || 0));
      db.addRating(recipientUser.id, user.id, currentRoom, {
        carisma: clamp(msg.carisma), ojos: clamp(msg.ojos), sensualidad: clamp(msg.sensualidad),
        piernas: clamp(msg.piernas), belleza: clamp(msg.belleza),
      });
      return;
    }

    if (msg.type === 'signal') {
      const recipient = otherPeerInRoom(currentRoom, conn.id);
      if (recipient) recipient.conn.send({ type: 'signal', from: conn.id, payload: msg.payload });
      return;
    }
  });

  conn.on('close', () => {
    allConns.delete(conn);
    const set = userConns.get(authedUser.id);
    if (set) {
      set.delete(conn);
      if (!set.size) {
        userConns.delete(authedUser.id);
        userPresence.delete(authedUser.id);
        broadcastPresence(authedUser.id, 'offline');
      }
    }
    if (currentRoom && rooms.has(currentRoom)) {
      rooms.get(currentRoom).delete(conn.id);
      broadcastRoom(currentRoom, { type: 'peer-left', id: conn.id, userId: authedUser.id });
      if (!rooms.get(currentRoom).size) {
        rooms.delete(currentRoom);
        autoOpenerHandled.delete(currentRoom);
      }
    }
  });
});

server.listen(PORT, () => {
  console.log(`Geminy Meet backend corriendo en http://localhost:${PORT}`);
});

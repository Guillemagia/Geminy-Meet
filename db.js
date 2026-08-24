// db.js — persistencia con node:sqlite (nativo de Node, sin dependencias externas)
const { DatabaseSync } = require('node:sqlite');
const crypto = require('node:crypto');
const path = require('node:path');

const DB_PATH = path.join(__dirname, 'geminy.db');
const db = new DatabaseSync(DB_PATH);

db.exec(`
CREATE TABLE IF NOT EXISTS users (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  email TEXT UNIQUE NOT NULL,
  password_hash TEXT NOT NULL,
  password_salt TEXT NOT NULL,
  role TEXT NOT NULL DEFAULT 'member',                   -- member (paga) | companion (gana)
  verification_status TEXT NOT NULL DEFAULT 'approved',  -- approved | pending | rejected
  age INTEGER,
  bio TEXT DEFAULT '',
  avatar_url TEXT DEFAULT '',
  gallery TEXT DEFAULT '[]',            -- JSON: fotos adicionales de la anfitriona
  openers TEXT DEFAULT '[]',            -- JSON: frases de apertura editables de la anfitriona
  preferred_language TEXT DEFAULT 'es',
  worked_minutes INTEGER NOT NULL DEFAULT 0,   -- minutos acumulados en llamada (solo anfitrionas)
  low_credit_since INTEGER,             -- timestamp desde que anda con <10 créditos, o NULL
  first_purchase_used INTEGER NOT NULL DEFAULT 0,
  referral_code TEXT,                   -- código de invitación propio (solo anfitrionas)
  referred_by_user_id TEXT,             -- quién la invitó (solo miembros que usaron un código)
  is_ai INTEGER NOT NULL DEFAULT 0,     -- 1 = anfitriona de inteligencia artificial, no una persona real
  ai_persona TEXT DEFAULT '',           -- descripción de personalidad que se le manda al modelo
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS wallets (
  user_id TEXT PRIMARY KEY,
  credits_balance INTEGER NOT NULL DEFAULT 0,
  earnings_balance INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS transactions (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL,
  type TEXT NOT NULL,          -- topup | spend | earn | withdraw_request
  amount INTEGER NOT NULL,
  meta TEXT,
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS platform_ledger (
  id TEXT PRIMARY KEY,
  amount INTEGER NOT NULL,
  meta TEXT,
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS rooms (
  code TEXT PRIMARY KEY,
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS messages (
  id TEXT PRIMARY KEY,
  room_code TEXT NOT NULL,
  user_id TEXT NOT NULL,
  name TEXT NOT NULL,
  kind TEXT NOT NULL,          -- text | photo | video | gift
  content TEXT,
  cost INTEGER NOT NULL DEFAULT 0,
  is_auto INTEGER NOT NULL DEFAULT 0,   -- 1 = mensaje de apertura automático
  is_ai INTEGER NOT NULL DEFAULT 0,     -- 1 = lo escribió una anfitriona de IA
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS ratings (
  id TEXT PRIMARY KEY,
  companion_id TEXT NOT NULL,
  member_id TEXT NOT NULL,
  room_code TEXT NOT NULL,
  carisma INTEGER NOT NULL DEFAULT 0,
  ojos INTEGER NOT NULL DEFAULT 0,
  sensualidad INTEGER NOT NULL DEFAULT 0,
  piernas INTEGER NOT NULL DEFAULT 0,
  belleza INTEGER NOT NULL DEFAULT 0,
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS discount_offers (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL,
  percent_bonus INTEGER NOT NULL,
  reason TEXT,
  expires_at INTEGER NOT NULL,
  claimed INTEGER NOT NULL DEFAULT 0,
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS follows (
  follower_id TEXT NOT NULL,
  followed_id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  PRIMARY KEY (follower_id, followed_id)
);
`);

// Migraciones suaves: si la base de datos se creó con una versión anterior del esquema,
// le agregamos las columnas nuevas sin perder los datos que ya tenía.
function ensureColumn(table, column, definition) {
  const cols = db.prepare(`PRAGMA table_info(${table})`).all();
  if (!cols.some((c) => c.name === column)) {
    db.exec(`ALTER TABLE ${table} ADD COLUMN ${column} ${definition}`);
  }
}
ensureColumn('users', 'referral_code', 'TEXT');
ensureColumn('users', 'referred_by_user_id', 'TEXT');
ensureColumn('users', 'is_ai', 'INTEGER NOT NULL DEFAULT 0');
ensureColumn('users', 'ai_persona', "TEXT DEFAULT ''");
ensureColumn('messages', 'is_ai', 'INTEGER NOT NULL DEFAULT 0');
db.exec(`CREATE UNIQUE INDEX IF NOT EXISTS idx_users_referral_code ON users(referral_code) WHERE referral_code IS NOT NULL`);

const FREE_SIGNUP_CREDITS = 100;

// Niveles de anfitriona por minutos acumulados en llamada. Sube sola, sin aprobación.
const TIERS = [
  { name: 'Nueva',    min: 0,    rate: 10 },
  { name: 'Plata',    min: 500,  rate: 12 },
  { name: 'Oro',      min: 2000, rate: 15 },
  { name: 'Diamante', min: 6000, rate: 20 },
];

function tierForMinutes(minutes) {
  let current = TIERS[0];
  for (const t of TIERS) if (minutes >= t.min) current = t;
  return current;
}

function nowId() {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
}

// ---------- Users ----------
// Código de invitación de una anfitriona: corto, legible y sin caracteres que se confundan (0/O, 1/I).
const REFERRAL_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
function generateReferralCode() {
  for (let intento = 0; intento < 20; intento++) {
    let code = '';
    for (let i = 0; i < 6; i++) code += REFERRAL_ALPHABET[Math.floor(Math.random() * REFERRAL_ALPHABET.length)];
    if (!getUserByReferralCode(code)) return code;
  }
  return null; // prácticamente imposible; sin código la anfitriona sigue funcionando igual
}

function getUserByReferralCode(code) {
  if (!code) return null;
  return db.prepare(`SELECT * FROM users WHERE referral_code = ?`).get(String(code).trim().toUpperCase()) || null;
}

function createUser({ name, email, passwordHash, passwordSalt, role, avatarUrl, preferredLanguage, referredByUserId }) {
  const id = nowId();
  const safeRole = role === 'companion' ? 'companion' : 'member';
  const verificationStatus = safeRole === 'companion' ? 'pending' : 'approved';
  const referralCode = safeRole === 'companion' ? generateReferralCode() : null;
  db.prepare(
    `INSERT INTO users (id, name, email, password_hash, password_salt, role, verification_status, avatar_url, preferred_language, referral_code, referred_by_user_id, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(id, name, email.toLowerCase(), passwordHash, passwordSalt, safeRole, verificationStatus, avatarUrl || '', preferredLanguage || 'es', referralCode, referredByUserId || null, Date.now());
  db.prepare(`INSERT INTO wallets (user_id, credits_balance, earnings_balance) VALUES (?, ?, 0)`).run(id, FREE_SIGNUP_CREDITS);
  logTx(id, 'topup', FREE_SIGNUP_CREDITS, 'Bono de bienvenida');
  return getUserById(id);
}

function getUserByEmail(email) {
  return db.prepare(`SELECT * FROM users WHERE email = ?`).get(email.toLowerCase());
}

function getUserById(id) {
  return db.prepare(`SELECT * FROM users WHERE id = ?`).get(id);
}

function updateProfile(userId, { bio, age, avatarUrl, gallery, openers, preferredLanguage }) {
  const user = getUserById(userId);
  if (!user) return null;
  db.prepare(
    `UPDATE users SET bio = ?, age = ?, avatar_url = ?, gallery = ?, openers = ?, preferred_language = ? WHERE id = ?`
  ).run(
    bio !== undefined ? bio : user.bio,
    age !== undefined ? age : user.age,
    avatarUrl !== undefined ? avatarUrl : user.avatar_url,
    gallery !== undefined ? JSON.stringify(gallery) : user.gallery,
    openers !== undefined ? JSON.stringify(openers) : user.openers,
    preferredLanguage !== undefined ? preferredLanguage : user.preferred_language,
    userId
  );
  return getUserById(userId);
}

function addWorkedMinutes(userId, minutes) {
  db.prepare(`UPDATE users SET worked_minutes = worked_minutes + ? WHERE id = ?`).run(minutes, userId);
}

// ---------- Wallet ----------
function getWallet(userId) {
  return db.prepare(`SELECT * FROM wallets WHERE user_id = ?`).get(userId);
}

function logTx(userId, type, amount, meta) {
  db.prepare(
    `INSERT INTO transactions (id, user_id, type, amount, meta, created_at) VALUES (?, ?, ?, ?, ?, ?)`
  ).run(nowId(), userId, type, amount, meta || '', Date.now());
}

function addCredits(userId, amount, meta) {
  db.prepare(`UPDATE wallets SET credits_balance = credits_balance + ? WHERE user_id = ?`).run(amount, userId);
  logTx(userId, 'topup', amount, meta || '');
  return getWallet(userId);
}

// Transferencia con reparto: el pagador (miembro) paga "amount" completo.
// Si el destinatario es anfitriona (role='companion'), se lleva companionSharePercent%;
// el resto queda como ganancia de la plataforma (platform_ledger).
function transferCreditsWithSplit(payerId, recipientUser, amount, meta, companionSharePercent) {
  if (amount <= 0) return true; // gratis: nada que cobrar
  const wallet = getWallet(payerId);
  if (!wallet || wallet.credits_balance < amount) return false;

  db.prepare(`UPDATE wallets SET credits_balance = credits_balance - ? WHERE user_id = ?`).run(amount, payerId);
  logTx(payerId, 'spend', amount, meta || '');

  let recipientCut = 0;
  // Una anfitriona de IA no es una persona que pueda cobrar: su parte se queda íntegra en la plataforma.
  if (recipientUser && recipientUser.role === 'companion' && !recipientUser.is_ai) {
    recipientCut = Math.round((amount * companionSharePercent) / 100);
    db.prepare(`UPDATE wallets SET earnings_balance = earnings_balance + ? WHERE user_id = ?`).run(recipientCut, recipientUser.id);
    logTx(recipientUser.id, 'earn', recipientCut, meta || '');
  }
  const platformCut = amount - recipientCut;
  if (platformCut > 0) {
    db.prepare(`INSERT INTO platform_ledger (id, amount, meta, created_at) VALUES (?, ?, ?, ?)`).run(nowId(), platformCut, meta || '', Date.now());
  }
  return true;
}

function getPlatformRevenueTotal() {
  return db.prepare(`SELECT COALESCE(SUM(amount),0) as total FROM platform_ledger`).get().total;
}

// ---------- Anfitrionas / verificación ----------
function listPendingCompanions() {
  return db
    .prepare(`SELECT id, name, email, created_at as createdAt FROM users WHERE role='companion' AND verification_status='pending' ORDER BY created_at ASC`)
    .all();
}

function setVerificationStatus(userId, status) {
  db.prepare(`UPDATE users SET verification_status = ? WHERE id = ? AND role = 'companion'`).run(status, userId);
  return getUserById(userId);
}

// ---------- Rooms & messages ----------
function ensureRoom(code) {
  db.prepare(`INSERT OR IGNORE INTO rooms (code, created_at) VALUES (?, ?)`).run(code, Date.now());
}

function saveMessage({ roomCode, userId, name, kind, content, cost, isAuto, isAi }) {
  const id = nowId();
  db.prepare(
    `INSERT INTO messages (id, room_code, user_id, name, kind, content, cost, is_auto, is_ai, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(id, roomCode, userId, name, kind, content, cost || 0, isAuto ? 1 : 0, isAi ? 1 : 0, Date.now());
  return id;
}

// Devuelve los ÚLTIMOS `limit` mensajes de la sala, en orden cronológico.
// (Antes tomaba los primeros 80, así que en una conversación larga el historial se quedaba
// congelado en el principio y nunca mostraba lo reciente.)
function getRoomHistory(roomCode, limit = 80) {
  return db
    .prepare(
      `SELECT * FROM (
         SELECT id, user_id as userId, name, kind, content, cost, is_auto as isAuto, is_ai as isAi, created_at as createdAt
         FROM messages WHERE room_code = ? ORDER BY created_at DESC LIMIT ?
       ) ORDER BY createdAt ASC`
    )
    .all(roomCode, limit);
}

// ---------- Calificaciones ----------
function addRating(companionId, memberId, roomCode, { carisma, ojos, sensualidad, piernas, belleza }) {
  db.prepare(
    `INSERT INTO ratings (id, companion_id, member_id, room_code, carisma, ojos, sensualidad, piernas, belleza, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(nowId(), companionId, memberId, roomCode, carisma || 0, ojos || 0, sensualidad || 0, piernas || 0, belleza || 0, Date.now());
}

function getRatingSummary(companionId) {
  const row = db
    .prepare(
      `SELECT COUNT(*) as count,
              AVG(NULLIF(carisma,0)) as carisma, AVG(NULLIF(ojos,0)) as ojos,
              AVG(NULLIF(sensualidad,0)) as sensualidad, AVG(NULLIF(piernas,0)) as piernas,
              AVG(NULLIF(belleza,0)) as belleza
       FROM ratings WHERE companion_id = ?`
    )
    .get(companionId);
  const round1 = (v) => (v === null || v === undefined ? null : Math.round(v * 10) / 10);
  return {
    count: row.count,
    carisma: round1(row.carisma),
    ojos: round1(row.ojos),
    sensualidad: round1(row.sensualidad),
    piernas: round1(row.piernas),
    belleza: round1(row.belleza),
  };
}

// ---------- Descuentos ----------
// Se llama de forma oportunista (al hacer login / GET /api/me / entrar a una sala).
// Si el miembro lleva 3+ días de calendario con menos de 10 créditos, se le crea una
// oferta de 30% de bono, válida 48h, si no tiene ya una activa sin reclamar.
const LOW_CREDIT_THRESHOLD = 10;
const LOW_CREDIT_DAYS_MS = 3 * 24 * 60 * 60 * 1000;
const OFFER_VALID_MS = 48 * 60 * 60 * 1000;
const LAPSED_BONUS_PERCENT = 30;
const FIRST_PURCHASE_BONUS_PERCENT = 50;

function checkAndIssueLapsedDiscount(user) {
  if (!user || user.role !== 'member') return;
  const wallet = getWallet(user.id);
  const now = Date.now();

  if (wallet.credits_balance >= LOW_CREDIT_THRESHOLD) {
    if (user.low_credit_since) db.prepare(`UPDATE users SET low_credit_since = NULL WHERE id = ?`).run(user.id);
    return;
  }
  if (!user.low_credit_since) {
    db.prepare(`UPDATE users SET low_credit_since = ? WHERE id = ?`).run(now, user.id);
    return;
  }
  if (now - user.low_credit_since < LOW_CREDIT_DAYS_MS) return;

  const existing = db
    .prepare(`SELECT * FROM discount_offers WHERE user_id = ? AND claimed = 0 AND expires_at > ? LIMIT 1`)
    .get(user.id, now);
  if (existing) return;

  db.prepare(
    `INSERT INTO discount_offers (id, user_id, percent_bonus, reason, expires_at, created_at) VALUES (?, ?, ?, ?, ?, ?)`
  ).run(nowId(), user.id, LAPSED_BONUS_PERCENT, 'Créditos agotados por varios días', now + OFFER_VALID_MS, now);
}

function getActiveOffer(userId) {
  return db
    .prepare(`SELECT * FROM discount_offers WHERE user_id = ? AND claimed = 0 AND expires_at > ? ORDER BY created_at DESC LIMIT 1`)
    .get(userId, Date.now());
}

function claimOffer(offerId) {
  db.prepare(`UPDATE discount_offers SET claimed = 1 WHERE id = ?`).run(offerId);
}

function markFirstPurchaseUsed(userId) {
  db.prepare(`UPDATE users SET first_purchase_used = 1 WHERE id = ?`).run(userId);
}

// ---------- Seguir ----------
function isFollowing(followerId, followedId) {
  return !!db.prepare(`SELECT 1 FROM follows WHERE follower_id = ? AND followed_id = ?`).get(followerId, followedId);
}

function toggleFollow(followerId, followedId) {
  if (isFollowing(followerId, followedId)) {
    db.prepare(`DELETE FROM follows WHERE follower_id = ? AND followed_id = ?`).run(followerId, followedId);
    return false;
  }
  db.prepare(`INSERT INTO follows (follower_id, followed_id, created_at) VALUES (?, ?, ?)`).run(followerId, followedId, Date.now());
  return true;
}

// ---------- Anfitrionas de inteligencia artificial ----------
// Son filas normales en `users` (role='companion') con is_ai = 1: así reutilizan perfil,
// historial de chat y descubrimiento sin duplicar nada. No tienen contraseña utilizable.
function listAiCompanions() {
  return db.prepare(`SELECT * FROM users WHERE is_ai = 1 ORDER BY created_at ASC`).all();
}

function getAiCompanionByEmail(email) {
  return db.prepare(`SELECT * FROM users WHERE email = ? AND is_ai = 1`).get(String(email).toLowerCase()) || null;
}

function createAiCompanion({ name, email, age, bio, avatarUrl, persona, openers, preferredLanguage }) {
  const id = nowId();
  // Hash aleatorio con la misma forma que uno real: nadie puede iniciar sesión con la cuenta de una IA.
  const impossible = crypto.randomBytes(64).toString('hex');
  const impossibleSalt = crypto.randomBytes(16).toString('hex');
  db.prepare(
    `INSERT INTO users (id, name, email, password_hash, password_salt, role, verification_status,
                        age, bio, avatar_url, gallery, openers, preferred_language, referral_code,
                        is_ai, ai_persona, created_at)
     VALUES (?, ?, ?, ?, ?, 'companion', 'approved', ?, ?, ?, '[]', ?, ?, ?, 1, ?, ?)`
  ).run(
    id, name, String(email).toLowerCase(), impossible, impossibleSalt,
    age || null, bio || '', avatarUrl || '',
    JSON.stringify(Array.isArray(openers) ? openers.slice(0, 5) : []),
    preferredLanguage || 'es', generateReferralCode(), persona || '', Date.now()
  );
  db.prepare(`INSERT INTO wallets (user_id, credits_balance, earnings_balance) VALUES (?, 0, 0)`).run(id);
  return getUserById(id);
}

function updateAiCompanion(userId, { name, age, bio, avatarUrl, persona, openers, preferredLanguage }) {
  const user = getUserById(userId);
  if (!user || !user.is_ai) return null;
  db.prepare(
    `UPDATE users SET name = ?, age = ?, bio = ?, avatar_url = ?, openers = ?, preferred_language = ?, ai_persona = ? WHERE id = ?`
  ).run(
    name !== undefined ? name : user.name,
    age !== undefined ? age : user.age,
    bio !== undefined ? bio : user.bio,
    avatarUrl !== undefined ? avatarUrl : user.avatar_url,
    openers !== undefined ? JSON.stringify(openers.slice(0, 5)) : user.openers,
    preferredLanguage !== undefined ? preferredLanguage : user.preferred_language,
    persona !== undefined ? persona : user.ai_persona,
    userId
  );
  return getUserById(userId);
}

// ---------- Descubrimiento ----------
// Devuelve todos los usuarios del rol opuesto (para el feed deslizable).
// Si el traductor de IA está apagado (sin clave de API), `includeAi = false` esconde
// a las anfitrionas de IA en vez de mostrar tarjetas que no podrían contestar.
function listDiscoverable(oppositeRole, limit = 100, includeAi = true) {
  const sql = includeAi
    ? `SELECT * FROM users WHERE role = ? ORDER BY created_at DESC LIMIT ?`
    : `SELECT * FROM users WHERE role = ? AND is_ai = 0 ORDER BY created_at DESC LIMIT ?`;
  return db.prepare(sql).all(oppositeRole, limit);
}

module.exports = {
  TIERS,
  tierForMinutes,
  FIRST_PURCHASE_BONUS_PERCENT,
  createUser,
  getUserByEmail,
  getUserById,
  getUserByReferralCode,
  updateProfile,
  addWorkedMinutes,
  getWallet,
  addCredits,
  transferCreditsWithSplit,
  getPlatformRevenueTotal,
  listPendingCompanions,
  setVerificationStatus,
  ensureRoom,
  saveMessage,
  getRoomHistory,
  addRating,
  getRatingSummary,
  checkAndIssueLapsedDiscount,
  getActiveOffer,
  claimOffer,
  markFirstPurchaseUsed,
  isFollowing,
  toggleFollow,
  listDiscoverable,
  listAiCompanions,
  getAiCompanionByEmail,
  createAiCompanion,
  updateAiCompanion,
  logTx,
};

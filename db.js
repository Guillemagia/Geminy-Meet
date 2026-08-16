// db.js — persistencia con node:sqlite (nativo de Node, sin dependencias externas)
const { DatabaseSync } = require('node:sqlite');
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

CREATE TABLE IF NOT EXISTS rooms (
  code TEXT PRIMARY KEY,
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS messages (
  id TEXT PRIMARY KEY,
  room_code TEXT NOT NULL,
  user_id TEXT NOT NULL,
  name TEXT NOT NULL,
  kind TEXT NOT NULL,          -- text | photo | video
  content TEXT,                -- texto, o data-url para media
  cost INTEGER NOT NULL DEFAULT 0,
  created_at INTEGER NOT NULL
);
`);

const FREE_SIGNUP_CREDITS = 100;

function nowId() {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
}

// ---------- Users ----------
function createUser({ name, email, passwordHash, passwordSalt }) {
  const id = nowId();
  db.prepare(
    `INSERT INTO users (id, name, email, password_hash, password_salt, created_at) VALUES (?, ?, ?, ?, ?, ?)`
  ).run(id, name, email.toLowerCase(), passwordHash, passwordSalt, Date.now());
  db.prepare(
    `INSERT INTO wallets (user_id, credits_balance, earnings_balance) VALUES (?, ?, 0)`
  ).run(id, FREE_SIGNUP_CREDITS);
  logTx(id, 'topup', FREE_SIGNUP_CREDITS, 'Bono de bienvenida');
  return getUserById(id);
}

function getUserByEmail(email) {
  return db.prepare(`SELECT * FROM users WHERE email = ?`).get(email.toLowerCase());
}

function getUserById(id) {
  return db.prepare(`SELECT * FROM users WHERE id = ?`).get(id);
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

function addEarnings(userId, amount, meta) {
  db.prepare(`UPDATE wallets SET earnings_balance = earnings_balance + ? WHERE user_id = ?`).run(amount, userId);
  logTx(userId, 'earn', amount, meta || '');
  return getWallet(userId);
}

// Transferencia atómica: A paga créditos, B los gana. Devuelve false si A no tiene saldo.
function transferCredits(payerId, recipientId, amount, meta) {
  const wallet = getWallet(payerId);
  if (!wallet || wallet.credits_balance < amount) return false;
  db.prepare(`UPDATE wallets SET credits_balance = credits_balance - ? WHERE user_id = ?`).run(amount, payerId);
  logTx(payerId, 'spend', amount, meta || '');
  if (recipientId) {
    db.prepare(`UPDATE wallets SET earnings_balance = earnings_balance + ? WHERE user_id = ?`).run(amount, recipientId);
    logTx(recipientId, 'earn', amount, meta || '');
  }
  return true;
}

// ---------- Rooms & messages ----------
function ensureRoom(code) {
  db.prepare(`INSERT OR IGNORE INTO rooms (code, created_at) VALUES (?, ?)`).run(code, Date.now());
}

function saveMessage({ roomCode, userId, name, kind, content, cost }) {
  const id = nowId();
  db.prepare(
    `INSERT INTO messages (id, room_code, user_id, name, kind, content, cost, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(id, roomCode, userId, name, kind, content, cost || 0, Date.now());
  return id;
}

function getRoomHistory(roomCode, limit = 50) {
  return db
    .prepare(
      `SELECT id, user_id as userId, name, kind, content, cost, created_at as createdAt
       FROM messages WHERE room_code = ? ORDER BY created_at ASC LIMIT ?`
    )
    .all(roomCode, limit);
}

module.exports = {
  createUser,
  getUserByEmail,
  getUserById,
  getWallet,
  addCredits,
  addEarnings,
  transferCredits,
  ensureRoom,
  saveMessage,
  getRoomHistory,
  logTx,
};

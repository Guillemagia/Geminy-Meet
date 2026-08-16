// auth.js — hashing de contraseñas (scrypt) y tokens de sesión firmados (HMAC),
// implementado solo con el módulo "crypto" nativo de Node (sin jsonwebtoken/bcrypt).
const crypto = require('node:crypto');

const SESSION_SECRET =
  process.env.SESSION_SECRET || 'dev-secret-cambia-esto-en-produccion-' + 'geminy';
const TOKEN_TTL_MS = 7 * 24 * 60 * 60 * 1000; // 7 días

function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString('hex');
  const hash = crypto.scryptSync(password, salt, 64).toString('hex');
  return { hash, salt };
}

function verifyPassword(password, hash, salt) {
  const attempt = crypto.scryptSync(password, salt, 64).toString('hex');
  return crypto.timingSafeEqual(Buffer.from(attempt), Buffer.from(hash));
}

function base64url(input) {
  return Buffer.from(input).toString('base64url');
}

function sign(userId) {
  const payload = { uid: userId, exp: Date.now() + TOKEN_TTL_MS };
  const body = base64url(JSON.stringify(payload));
  const sig = crypto.createHmac('sha256', SESSION_SECRET).update(body).digest('base64url');
  return `${body}.${sig}`;
}

function verify(token) {
  if (!token || typeof token !== 'string' || !token.includes('.')) return null;
  const [body, sig] = token.split('.');
  const expected = crypto.createHmac('sha256', SESSION_SECRET).update(body).digest('base64url');
  if (sig !== expected) return null;
  try {
    const payload = JSON.parse(Buffer.from(body, 'base64url').toString());
    if (payload.exp < Date.now()) return null;
    return payload.uid;
  } catch {
    return null;
  }
}

module.exports = { hashPassword, verifyPassword, sign, verify };

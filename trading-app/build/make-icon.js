// Genera el icono de la app (build/icon.ico y build/icon.png) sin dependencias
// externas: dibuja los píxeles a mano y codifica PNG (con zlib nativo) e ICO.
// Diseño: cuadrado oscuro redondeado con velas japonesas y una línea de tendencia.
//
// Ejecuta:  node build/make-icon.js

'use strict';

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const S = 256;
const buf = Buffer.alloc(S * S * 4); // RGBA

function px(x, y, r, g, b, a = 255) {
  if (x < 0 || y < 0 || x >= S || y >= S) return;
  const i = (y * S + x) * 4;
  // alpha blending sobre lo existente
  const sa = a / 255;
  buf[i] = Math.round(r * sa + buf[i] * (1 - sa));
  buf[i + 1] = Math.round(g * sa + buf[i + 1] * (1 - sa));
  buf[i + 2] = Math.round(b * sa + buf[i + 2] * (1 - sa));
  buf[i + 3] = Math.max(buf[i + 3], a);
}

function rect(x0, y0, w, h, c) {
  for (let y = y0; y < y0 + h; y++)
    for (let x = x0; x < x0 + w; x++) px(x, y, c[0], c[1], c[2], c[3] ?? 255);
}

// Fondo redondeado
function roundedBg(radius, c) {
  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      let inside = true;
      const corners = [[radius, radius], [S - radius, radius], [radius, S - radius], [S - radius, S - radius]];
      if (x < radius && y < radius) inside = dist(x, y, corners[0]) <= radius;
      else if (x >= S - radius && y < radius) inside = dist(x, y, corners[1]) <= radius;
      else if (x < radius && y >= S - radius) inside = dist(x, y, corners[2]) <= radius;
      else if (x >= S - radius && y >= S - radius) inside = dist(x, y, corners[3]) <= radius;
      if (inside) px(x, y, c[0], c[1], c[2], 255);
    }
  }
}
function dist(x, y, c) { return Math.hypot(x - c[0], y - c[1]); }

// Línea gruesa (para la tendencia)
function line(x0, y0, x1, y1, thick, c) {
  const steps = Math.max(Math.abs(x1 - x0), Math.abs(y1 - y0));
  for (let s = 0; s <= steps; s++) {
    const t = s / steps;
    const x = Math.round(x0 + (x1 - x0) * t);
    const y = Math.round(y0 + (y1 - y0) * t);
    for (let dy = -thick; dy <= thick; dy++)
      for (let dx = -thick; dx <= thick; dx++)
        if (dx * dx + dy * dy <= thick * thick) px(x + dx, y + dy, c[0], c[1], c[2], c[3] ?? 255);
  }
}

// Una vela: mecha + cuerpo
function candle(cx, bodyTop, bodyH, wickTop, wickBot, halfW, c) {
  rect(cx - 2, wickTop, 4, wickBot - wickTop, c);       // mecha
  rect(cx - halfW, bodyTop, halfW * 2, bodyH, c);        // cuerpo
}

const DARK = [14, 17, 23];
const PANEL = [22, 27, 34];
const GREEN = [46, 160, 67];
const RED = [248, 81, 73];
const BLUE = [76, 141, 255];

// --- Dibujo ---
roundedBg(52, DARK);
// marco interior sutil
// velas (de izquierda a derecha)
candle(70, 150, 46, 120, 210, 15, RED);
candle(128, 96, 70, 70, 180, 15, GREEN);
candle(186, 60, 60, 40, 150, 15, GREEN);
// línea de tendencia ascendente sobre las velas
line(50, 175, 206, 70, 3, BLUE);

// --- Codificar PNG ---
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length, 0);
  const typeBuf = Buffer.from(type, 'ascii');
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])) >>> 0, 0);
  return Buffer.concat([len, typeBuf, data, crc]);
}
function crc32(buf) {
  let c = ~0;
  for (let i = 0; i < buf.length; i++) {
    c ^= buf[i];
    for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xEDB88320 & -(c & 1));
  }
  return ~c;
}
function encodePng() {
  const sig = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(S, 0); ihdr.writeUInt32BE(S, 4);
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  // scanlines con byte de filtro 0
  const raw = Buffer.alloc(S * (S * 4 + 1));
  for (let y = 0; y < S; y++) {
    raw[y * (S * 4 + 1)] = 0;
    buf.copy(raw, y * (S * 4 + 1) + 1, y * S * 4, (y + 1) * S * 4);
  }
  const idat = zlib.deflateSync(raw, { level: 9 });
  return Buffer.concat([sig, chunk('IHDR', ihdr), chunk('IDAT', idat), chunk('IEND', Buffer.alloc(0))]);
}

// --- Codificar ICO (con PNG embebido, válido para 256x256) ---
function encodeIco(png) {
  const dir = Buffer.alloc(6);
  dir.writeUInt16LE(0, 0); dir.writeUInt16LE(1, 2); dir.writeUInt16LE(1, 4);
  const entry = Buffer.alloc(16);
  entry[0] = 0; entry[1] = 0; // 0 = 256
  entry[2] = 0; entry[3] = 0;
  entry.writeUInt16LE(1, 4); entry.writeUInt16LE(32, 6);
  entry.writeUInt32LE(png.length, 8);
  entry.writeUInt32LE(6 + 16, 12);
  return Buffer.concat([dir, entry, png]);
}

const png = encodePng();
const ico = encodeIco(png);
fs.writeFileSync(path.join(__dirname, 'icon.png'), png);
fs.writeFileSync(path.join(__dirname, 'icon.ico'), ico);
console.log('Generado build/icon.png', png.length, 'bytes y build/icon.ico', ico.length, 'bytes');

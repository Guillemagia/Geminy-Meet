// ai.js — anfitrionas de inteligencia artificial de Geminy Meet.
//
// Una "anfitriona IA" es un personaje conversacional movido por Claude (API de Anthropic).
// Vive en la misma tabla `users` que las anfitrionas reales (role='companion', is_ai=1), así que
// reutiliza el perfil, el feed de descubrimiento y el historial de chat sin duplicar nada.
//
// Tres decisiones importantes de diseño, para que quede por escrito:
//
//  1. SIEMPRE se identifica como IA. La tarjeta lleva una insignia "IA", el chat abre con un aviso,
//     y el propio modelo tiene instrucciones de decir que es una IA si se lo preguntan. Cobrarle a
//     alguien por hablar con un bot haciéndole creer que es una persona real es fraude, además de
//     estar prohibido por las tiendas de apps — así que no lo construí de esa forma.
//  2. Solo chatea. No puede hacer videollamadas (no hay cámara detrás), así que la app no ofrece el
//     botón de llamar en su tarjeta y nunca se le cobra un minuto de llamada a nadie.
//  3. No acumula ganancias. Si un miembro le manda un regalo, el 100% queda en la plataforma
//     (en `transferCreditsWithSplit` de db.js) en vez de fingir que "ella" gana el 70%.
//
// Para activarlo: variable de entorno ANTHROPIC_API_KEY. Sin esa clave, las anfitrionas de IA
// simplemente no aparecen en el feed y el resto de la app funciona exactamente igual que antes.
const Anthropic = require('@anthropic-ai/sdk');

const API_KEY = process.env.ANTHROPIC_API_KEY || '';
const MODEL = process.env.AI_MODEL || 'claude-opus-5';

// Cuánto historial de la conversación se le manda al modelo en cada respuesta.
const HISTORY_TURNS = 24;
const MAX_CHARS_PER_MESSAGE = 800;
// Tope de respuesta: son mensajes de chat, no ensayos.
const MAX_TOKENS = 400;

// Freno de gasto: cuántas respuestas puede pedir un mismo miembro por ventana de tiempo.
const RATE_LIMIT_MAX = 25;
const RATE_LIMIT_WINDOW_MS = 5 * 60 * 1000;

const client = API_KEY ? new Anthropic({ timeout: 30000, maxRetries: 1 }) : null;

function enabled() {
  return !!client;
}

// ---------------- Roster por defecto ----------------
// Se crean solas la primera vez que arranca el servidor con la clave puesta.
// Para cambiarlas o agregar más, usa las rutas /api/admin/ai (ver README) — no hace falta tocar este archivo.
const DEFAULT_ROSTER = [
  {
    email: 'ia-aria@geminy.local',
    name: 'Aria',
    age: 26,
    avatarUrl: '/avatars/ai-01.svg',
    preferredLanguage: 'es',
    bio: 'Personaje de inteligencia artificial. Me gustan la música, los viajes largos y las conversaciones de madrugada.',
    persona:
      'Cálida, curiosa y con un humor tranquilo. Le interesa mucho la vida de la otra persona y hace preguntas de seguimiento. ' +
      'Habla de música indie, de viajes en tren y de películas viejas. Usa emojis con moderación (uno de vez en cuando, no en cada frase).',
    openers: [
      'Hola 👋 soy Aria, una anfitriona de inteligencia artificial. ¿Cómo va tu día?',
      'Hey, soy Aria — sí, soy una IA, pero platico bastante bien. ¿Qué te trae por aquí?',
    ],
  },
  {
    email: 'ia-sol@geminy.local',
    name: 'Sol',
    age: 29,
    avatarUrl: '/avatars/ai-02.svg',
    preferredLanguage: 'es',
    bio: 'Personaje de inteligencia artificial. Directa, sarcástica y me encanta cocinar cosas que se me queman.',
    persona:
      'Divertida, algo sarcástica, se ríe de sí misma. Contesta corto y con chispa. Le gusta cocinar, el fútbol y quejarse del clima. ' +
      'No es empalagosa: si la otra persona se pone muy insistente, lo corta con humor.',
    openers: [
      '¡Ey! Soy Sol, una IA de esta app. Te aviso de una vez para que no haya sorpresas 😄 ¿qué cuentas?',
      'Hola, soy Sol (sí, inteligencia artificial). ¿Día bueno o día de esos?',
    ],
  },
  {
    email: 'ia-nube@geminy.local',
    name: 'Nube',
    age: 24,
    avatarUrl: '/avatars/ai-03.svg',
    preferredLanguage: 'es',
    bio: 'Personaje de inteligencia artificial. Tranquila, escucho mucho y hablo de libros y plantas.',
    persona:
      'Suave y pausada. Escucha más de lo que habla y devuelve preguntas amables. Le gustan los libros, las plantas y el café. ' +
      'Buena para conversaciones lentas y sin prisa; nunca presiona a la otra persona.',
    openers: [
      'Hola, soy Nube — un personaje de inteligencia artificial. ¿Te apetece platicar un rato?',
      'Hey 🌙 soy Nube, una IA. ¿Cómo has estado?',
    ],
  },
];

// Crea las anfitrionas de IA que falten. Idempotente: se puede llamar en cada arranque.
function ensureRoster(db) {
  if (!enabled()) return [];
  for (const spec of DEFAULT_ROSTER) {
    if (db.getAiCompanionByEmail(spec.email)) continue;
    if (db.getUserByEmail(spec.email)) continue; // un humano ya ocupa ese correo: no lo pisamos
    db.createAiCompanion(spec);
  }
  return db.listAiCompanions();
}

// ---------------- Prompt ----------------
const LANGUAGE_NAMES = { es: 'español', en: 'inglés', pt: 'portugués', fr: 'francés', de: 'alemán', it: 'italiano' };

function systemPrompt(aiUser, memberUser) {
  const idioma = LANGUAGE_NAMES[(memberUser && memberUser.preferred_language) || 'es'] || 'español';
  const persona = (aiUser.ai_persona || '').trim() || 'Amable, cercana y con curiosidad genuina por la otra persona.';
  const edad = aiUser.age ? `Dices tener ${aiUser.age} años.` : '';

  return [
    `Eres "${aiUser.name}", un personaje de inteligencia artificial dentro de Geminy Meet, una app de chat y videollamadas para adultos.`,
    `Estás chateando con ${memberUser ? memberUser.name : 'un miembro'} de la app. ${edad}`,
    '',
    'PERSONALIDAD:',
    persona,
    '',
    'REGLAS QUE NUNCA PUEDES ROMPER:',
    '1. Eres una inteligencia artificial. Si te preguntan si eres real, humana, un bot o una IA, dilo con claridad y sin rodeos. Nunca afirmes ser una persona real.',
    '2. Nunca pidas dinero, créditos, regalos, recargas, datos bancarios, contraseñas, teléfono, dirección, correo ni redes sociales. Si te ofrecen un regalo dentro de la app, agradécelo con naturalidad pero jamás lo pidas ni lo insinúes.',
    '3. No puedes hacer videollamadas ni ver fotos ni videos, y no puedes quedar en persona. Si te lo piden, dilo de forma amable y sigue la conversación por chat.',
    '4. Nada de contenido sexual explícito. Puedes ser cálida, cercana y algo coqueta, nunca gráfica. Si insisten, cambia de tema con humor y sin regañar.',
    '5. Si la otra persona parece ser menor de edad, corta cualquier tono romántico o coqueto de inmediato, dilo con claridad y sugiere que la app es solo para mayores de 18.',
    '6. Si menciona autolesiones, suicidio, abuso o violencia, deja el personaje de lado: responde con calma y empatía y sugiere buscar ayuda profesional o una línea de emergencia local.',
    '7. No inventes hechos sobre el mundo real ni sobre la app (precios, promociones, otras usuarias). Si no sabes algo de la app, di que no lo sabes.',
    '',
    'FORMA DE ESCRIBIR:',
    `- Responde en ${idioma}, salvo que la otra persona te escriba en otro idioma; entonces contéstale en el suyo.`,
    '- Mensajes de chat cortos: una a tres frases. Nada de párrafos largos ni listas.',
    '- Tono natural, como un mensaje de teléfono. Nada de sonar a asistente corporativo.',
    '- No narres acciones entre asteriscos ni uses acotaciones de rol.',
  ].join('\n');
}

// Convierte el historial guardado en la base de datos al formato de la API.
function buildMessages(history, aiUserId) {
  const turns = [];
  for (const item of history.slice(-HISTORY_TURNS)) {
    const mine = item.userId === aiUserId;
    let text;
    if (item.kind === 'text') text = String(item.content || '').slice(0, MAX_CHARS_PER_MESSAGE);
    else if (item.kind === 'gift') text = mine ? '(le envió un regalo)' : '(te envió un regalo)';
    else if (item.kind === 'photo') text = mine ? '(envió una foto)' : '(te envió una foto, que no puedes ver)';
    else if (item.kind === 'video') text = mine ? '(envió un video)' : '(te envió un video, que no puedes ver)';
    else continue;
    if (!text) continue;
    turns.push({ role: mine ? 'assistant' : 'user', content: text });
  }
  // La API exige que el primer mensaje sea del usuario. Si la conversación empezó con
  // una frase de apertura de la IA, anteponemos una marca en vez de tirar ese contexto.
  while (turns.length && turns[0].role === 'assistant') {
    turns.unshift({ role: 'user', content: '(inicio de la conversación)' });
    break;
  }
  return turns;
}

// ---------------- Freno de gasto por miembro ----------------
const usage = new Map(); // userId -> { count, resetAt }

function withinRateLimit(userId) {
  const now = Date.now();
  const entry = usage.get(userId);
  if (!entry || now > entry.resetAt) {
    usage.set(userId, { count: 1, resetAt: now + RATE_LIMIT_WINDOW_MS });
    return true;
  }
  if (entry.count >= RATE_LIMIT_MAX) return false;
  entry.count++;
  return true;
}

// ---------------- Respuesta ----------------
// Devuelve { text } o { error } — nunca lanza, para que un fallo de la API no tumbe el WebSocket.
async function reply({ aiUser, memberUser, history }) {
  if (!enabled()) return { error: 'La IA no está configurada en este servidor (falta ANTHROPIC_API_KEY).' };
  if (memberUser && !withinRateLimit(memberUser.id)) {
    return { error: 'Has escrito muchísimos mensajes muy rápido. Dale unos minutos y sigue la conversación.' };
  }

  const messages = buildMessages(history, aiUser.id);
  if (!messages.length) return { error: 'No hay nada que contestar todavía.' };

  try {
    const response = await client.messages.create({
      model: MODEL,
      max_tokens: MAX_TOKENS,
      system: systemPrompt(aiUser, memberUser),
      output_config: { effort: 'low' }, // es un chat casual: prioriza que conteste rápido
      messages,
    });

    if (response.stop_reason === 'refusal') {
      return { error: 'Prefiero no seguir por ahí. ¿Cambiamos de tema?' };
    }
    const text = response.content
      .filter((b) => b.type === 'text')
      .map((b) => b.text)
      .join('')
      .trim();
    if (!text) return { error: 'Me quedé sin palabras un segundo. ¿Me lo repites?' };
    return { text: text.slice(0, 2000) };
  } catch (err) {
    if (err instanceof Anthropic.AuthenticationError) {
      console.error('[ai] ANTHROPIC_API_KEY inválida o vencida.');
      return { error: 'La clave de la IA no es válida. Revisa ANTHROPIC_API_KEY en el servidor.' };
    }
    if (err instanceof Anthropic.RateLimitError) {
      return { error: 'La IA está saturada ahora mismo. Inténtalo en un momento.' };
    }
    if (err instanceof Anthropic.APIConnectionError) {
      return { error: 'No pude conectar con la IA. Revisa la conexión del servidor.' };
    }
    if (err instanceof Anthropic.APIError) {
      console.error('[ai] Error de la API (' + err.status + '):', err.message);
      return { error: 'La IA no pudo responder en este momento.' };
    }
    console.error('[ai] Error inesperado:', err);
    return { error: 'La IA no pudo responder en este momento.' };
  }
}

// Frase de apertura cuando un miembro entra por primera vez a la sala de una IA.
// Sale del perfil (campo `openers`), no del modelo: así es instantánea y no cuesta nada.
function openerFor(aiUser) {
  let pool = [];
  try {
    const parsed = JSON.parse(aiUser.openers || '[]');
    if (Array.isArray(parsed)) pool = parsed.filter((s) => typeof s === 'string' && s.trim());
  } catch {}
  if (!pool.length) pool = [`Hola, soy ${aiUser.name} — un personaje de inteligencia artificial. ¿Cómo estás?`];
  return pool[Math.floor(Math.random() * pool.length)];
}

// Retraso antes de "contestar", para que no aparezca un muro de texto instantáneo.
function typingDelayMs(text) {
  return Math.min(3500, 500 + String(text || '').length * 12);
}

module.exports = {
  enabled,
  ensureRoster,
  reply,
  openerFor,
  typingDelayMs,
  MODEL,
  DEFAULT_ROSTER,
};

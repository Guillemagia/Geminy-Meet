# Geminy Meet — Prototipo funcional

Este es un **prototipo web** de Geminy Meet: chat de texto, envío de fotos/video y videollamada en tiempo real entre dos personas, con un sistema de créditos simulado. No requiere instalar nada ni programar servidores: es un solo archivo HTML.

## Cómo probarlo

1. Copia la carpeta `geminy-meet` a tu equipo (por ejemplo dentro de `C:\Users\guill\OneDrive\Desktop\APP`).
2. Abre `index.html` haciendo doble clic (se abre en tu navegador).
3. Escribe tu nombre y crea una sala (o deja el código en blanco para que se genere uno).
4. Abre el mismo `index.html` en **otra pestaña, otro navegador o el celular**, escribe otro nombre y entra con el **mismo código de sala**.
5. Ya puedes chatear, enviar fotos/videos y darle a 📹 para iniciar la videollamada. Los créditos se descuentan de verdad (localmente) para que sientas cómo funcionaría el modelo de negocio.

La videollamada usa WebRTC real a través de un servidor público de señalización gratuito (PeerJS), así que funciona entre dispositivos distintos siempre que ambos tengan internet — no es solo una simulación visual.

## Qué es real y qué es simulado

| Función | Estado |
|---|---|
| Chat de texto en tiempo real | ✅ Real (P2P vía WebRTC) |
| Envío de fotos/video en el chat | ✅ Real (hasta 8MB por demo) |
| Videollamada | ✅ Real (cámara y micrófono reales) |
| Descuento de créditos por minuto/envío | ✅ Real, pero **local** (no sincronizado entre los dos usuarios, no hay backend) |
| Compra de créditos | ⚠️ Simulada — no se cobra dinero |
| Retiro de créditos a una cuenta bancaria | ❌ No implementado |
| App instalable en iPhone/Android | ❌ Esto sigue siendo una página web |

## Lo que falta para convertirlo en un negocio real

Esto es la lista honesta de lo que viene después, porque quiero que tengas expectativas claras:

1. **Backend con base de datos** — para que los créditos, usuarios y mensajes no vivan solo en el navegador de cada quien.
2. **Autenticación y verificación de edad/identidad** — obligatorio si hay pagos e "intimidad" de por medio.
3. **Procesador de pagos** (Stripe, etc.) para cobrar de verdad, y **Stripe Connect** (o similar) si vas a pagarle a la gente que recibe créditos — esto es lo que permite mandar dinero a una cuenta bancaria de forma legal.
4. **Empaquetado como app nativa** — con esta misma lógica se puede envolver en Capacitor/React Native para publicarla en App Store y Google Play. Aquí es donde entra el punto importante:

   > Apple y Google revisan manualmente las apps que combinan **video en vivo + pago por interacción**. Si el contenido se percibe como sexual o de "compañía paga" es rechazada. Como me dijiste que el enfoque es citas/compañía sin contenido explícito, está dentro de lo permitido — pero conviene dejarlo claro en las reglas de la comunidad de la app desde el día uno.

5. **Moderación de contenido** para las fotos/videos que se envían.

## Siguiente paso sugerido

Dime cuál de estos quieres que construya después y seguimos:
- El **backend** (créditos reales por usuario, historial de chats, panel de administración)
- El **empaquetado como app** para probarla en tu celular (Android primero, es más rápido de probar sin cuenta de desarrollador)
- El **diseño de más pantallas** (perfil, descubrir personas, ajustes)

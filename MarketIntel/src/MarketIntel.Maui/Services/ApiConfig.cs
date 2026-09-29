namespace MarketIntel.Maui.Services;

/// <summary>Resolves the backend base URL. One place to switch from local dev to a real server.</summary>
public static class ApiConfig
{
    // ── PRODUCCIÓN ──────────────────────────────────────────────────────────────
    // Para apuntar al servidor de despliegue, pon aquí su URL pública, p. ej.:
    //   private const string ProductionUrl = "https://api.tuservidor.com";
    // Déjalo vacío ("") para usar el backend local (localhost / emulador) en desarrollo.
    private const string ProductionUrl = "";
    // ────────────────────────────────────────────────────────────────────────────

    public static string BaseUrl =>
        !string.IsNullOrEmpty(ProductionUrl)
            ? ProductionUrl
#if ANDROID
            // El emulador de Android llega al host por 10.0.2.2.
            : "http://10.0.2.2:5080";
#else
            : "http://localhost:5080";
#endif
}

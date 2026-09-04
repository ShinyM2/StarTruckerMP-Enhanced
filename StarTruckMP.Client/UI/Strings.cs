using System;
using System.Collections.Generic;

namespace StarTruckMP.Client.UI;

/// <summary>
/// Every word the mod shows a player, in every language the game ships.
///
/// The language is the game's own: whatever <c>StringTable</c> has loaded is what the player is
/// reading, and the mod follows it. English is the source text and the fallback for anything
/// a language lacks. The three words the game itself has — Back, On, Off — are taken from its
/// table so they match the menus around them exactly.
///
/// Columns, in order: en, ru, de, fr, es, pt-br, pl, it, zh-cn, zh-hant. Latin-American Spanish
/// (es-419) reads the Spanish column.
/// </summary>
internal static class Strings
{
    private static readonly string[] Columns = { "en", "ru", "de", "fr", "es", "pt-br", "pl", "it", "zh-cn", "zh-hant" };

    /// <summary>The game's language code, lower case, e.g. "en", "ru", "pt-br".</summary>
    public static string Language => Localisation.Code;

    /// <summary>A string in the player's language, with <c>{0}</c>-style arguments filled in.</summary>
    public static string Get(string key, params object[] args)
    {
        var text = Raw(key);
        if (args == null || args.Length == 0) return text;

        try { return string.Format(text, args); }
        catch (FormatException) { return text; }
    }

    /// <summary>The game's own word for it when the table has one, otherwise ours.</summary>
    public static string Back => FromGame("STR_BACK") ?? Get("common.back");
    public static string On => FromGame("STR_ON") ?? Get("common.on");
    public static string Off => FromGame("STR_OFF") ?? Get("common.off");

    /// <summary>Every key that starts with the prefix, resolved, for handing a whole page its words at once.</summary>
    public static Dictionary<string, string> All(string prefix)
    {
        var result = new Dictionary<string, string>();
        foreach (var key in Table.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                result[key] = Raw(key);
        }

        return result;
    }

    private static string Raw(string key)
    {
        if (!Table.TryGetValue(key, out var row)) return key;

        var column = ColumnFor(Language);
        if (column < row.Length && !string.IsNullOrEmpty(row[column])) return row[column];
        return row[0];
    }

    private static int ColumnFor(string language)
    {
        if (string.IsNullOrEmpty(language)) return 0;

        for (var i = 0; i < Columns.Length; i++)
        {
            if (string.Equals(Columns[i], language, StringComparison.OrdinalIgnoreCase)) return i;
        }

        // "es-419" and any other regional variant fall back to the plain language.
        var dash = language.IndexOf('-');
        if (dash > 0) return ColumnFor(language.Substring(0, dash));

        return 0;
    }

    private static string FromGame(string id)
    {
        try
        {
            if (!StringTable.isReady || !StringTable.Contains(id)) return null;
            var text = StringTable.Get(id);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    // en, ru, de, fr, es, pt-br, pl, it, zh-cn, zh-hant
    private static readonly Dictionary<string, string[]> Table = new()
    {
        // Shared words
        ["common.back"] = new[] { "Back", "Назад", "Zurück", "Retour", "Atrás", "Voltar", "Wstecz", "Indietro", "返回", "返回" },
        ["common.on"] = new[] { "on", "вкл.", "an", "activé", "sí", "ligado", "wł.", "sì", "开", "開" },
        ["common.off"] = new[] { "off", "выкл.", "aus", "désactivé", "no", "desligado", "wył.", "no", "关", "關" },

        // The game's menus
        ["menu.multiplayer"] = new[] { "Multiplayer", "Мультиплеер", "Mehrspieler", "Multijoueur", "Multijugador", "Multijogador", "Tryb wieloosobowy", "Multigiocatore", "多人游戏", "多人遊戲" },

        // Page titles
        ["title.multiplayer"] = new[] { "MULTIPLAYER", "МУЛЬТИПЛЕЕР", "MEHRSPIELER", "MULTIJOUEUR", "MULTIJUGADOR", "MULTIJOGADOR", "TRYB WIELOOSOBOWY", "MULTIGIOCATORE", "多人游戏", "多人遊戲" },
        ["title.host"] = new[] { "HOST", "ХОСТ", "HOST", "HÔTE", "ANFITRIÓN", "ANFITRIÃO", "HOST", "HOST", "主机", "主機" },
        ["title.player"] = new[] { "CONNECT", "ПОДКЛЮЧЕНИЕ", "VERBINDEN", "CONNEXION", "CONEXIÓN", "CONEXÃO", "POŁĄCZENIE", "CONNESSIONE", "连接", "連線" },
        ["title.display"] = new[] { "OTHER PLAYERS", "ДРУГИЕ ИГРОКИ", "ANDERE SPIELER", "AUTRES JOUEURS", "OTROS JUGADORES", "OUTROS JOGADORES", "INNI GRACZE", "ALTRI GIOCATORI", "其他玩家", "其他玩家" },
        ["title.radio"] = new[] { "RADIO AND CHAT", "РАЦИЯ И ЧАТ", "FUNK UND CHAT", "RADIO ET CHAT", "RADIO Y CHAT", "RÁDIO E CHAT", "RADIO I CZAT", "RADIO E CHAT", "无线电与聊天", "無線電與聊天" },
        ["title.friends"] = new[] { "FRIENDS", "ДРУЗЬЯ", "FREUNDE", "AMIS", "AMIGOS", "AMIGOS", "ZNAJOMI", "AMICI", "好友", "好友" },
        ["title.mod"] = new[] { "MOD", "МОД", "MOD", "MOD", "MOD", "MOD", "MOD", "MOD", "模组", "模組" },

        // Root page
        ["root.friends"] = new[] { "Friends — invite or join", "Друзья — пригласить или присоединиться", "Freunde — einladen oder beitreten", "Amis — inviter ou rejoindre", "Amigos — invitar o unirse", "Amigos — convidar ou entrar", "Znajomi — zaproś lub dołącz", "Amici — invita o unisciti", "好友 — 邀请或加入", "好友 — 邀請或加入" },
        ["root.player"] = new[] { "Connect by address", "Подключиться по адресу", "Über Adresse verbinden", "Se connecter par adresse", "Conectar por dirección", "Conectar por endereço", "Połącz przez adres", "Connetti tramite indirizzo", "按地址连接", "依位址連線" },
        ["root.host"] = new[] { "Your own server on this PC", "Свой сервер на этом ПК", "Eigener Server auf diesem PC", "Votre serveur sur ce PC", "Tu propio servidor en este PC", "Seu próprio servidor neste PC", "Własny serwer na tym PC", "Il tuo server su questo PC", "在本机运行自己的服务器", "在本機執行自己的伺服器" },
        ["root.display"] = new[] { "Other players", "Другие игроки", "Andere Spieler", "Autres joueurs", "Otros jugadores", "Outros jogadores", "Inni gracze", "Altri giocatori", "其他玩家", "其他玩家" },
        ["root.radio"] = new[] { "Radio, chat and microphone", "Рация, чат и микрофон", "Funk, Chat und Mikrofon", "Radio, chat et microphone", "Radio, chat y micrófono", "Rádio, chat e microfone", "Radio, czat i mikrofon", "Radio, chat e microfono", "无线电、聊天与麦克风", "無線電、聊天與麥克風" },
        ["root.mod"] = new[] { "Mod settings", "Настройки мода", "Mod-Einstellungen", "Paramètres du mod", "Ajustes del mod", "Configurações do mod", "Ustawienia moda", "Impostazioni della mod", "模组设置", "模組設定" },

        // Player page
        ["player.address"] = new[] { "Server address", "Адрес сервера", "Serveradresse", "Adresse du serveur", "Dirección del servidor", "Endereço do servidor", "Adres serwera", "Indirizzo del server", "服务器地址", "伺服器位址" },
        ["player.port"] = new[] { "Port", "Порт", "Port", "Port", "Puerto", "Porta", "Port", "Porta", "端口", "連接埠" },
        ["player.connect"] = new[] { "Connect", "Подключиться", "Verbinden", "Se connecter", "Conectar", "Conectar", "Połącz", "Connetti", "连接", "連線" },
        ["player.hint"] = new[] { "For a Steam friend you need none of this — use Friends on the previous page.", "К другу в Steam всё это не нужно — откройте «Друзья» на прошлой странице.", "Für einen Steam-Freund braucht es nichts davon — nutzen Sie „Freunde“ auf der vorigen Seite.", "Pour un ami Steam, rien de tout cela — utilisez « Amis » sur la page précédente.", "Para un amigo de Steam no hace falta nada de esto — usa «Amigos» en la página anterior.", "Para um amigo do Steam nada disso é preciso — use “Amigos” na página anterior.", "Do znajomego ze Steam nic z tego nie trzeba — wybierz „Znajomi” na poprzedniej stronie.", "Per un amico Steam non serve nulla di tutto ciò — usa “Amici” nella pagina precedente.", "加入 Steam 好友无需这些 — 请用上一页的“好友”。", "加入 Steam 好友無需這些 — 請用上一頁的「好友」。" },
        ["player.status.connected"] = new[] { "Status:  connected, {0}", "Состояние:  подключено, {0}", "Status:  verbunden, {0}", "État :  connecté, {0}", "Estado:  conectado, {0}", "Estado:  conectado, {0}", "Stan:  połączono, {0}", "Stato:  connesso, {0}", "状态：已连接，{0}", "狀態：已連線，{0}" },
        ["player.status.disconnected"] = new[] { "Status:  not connected", "Состояние:  не подключено", "Status:  nicht verbunden", "État :  non connecté", "Estado:  sin conexión", "Estado:  desconectado", "Stan:  brak połączenia", "Stato:  non connesso", "状态：未连接", "狀態：未連線" },
        ["player.status.unreachable"] = new[] { "Status:  server {0} is not answering — is it running, are ports 7777 TCP+UDP forwarded?", "Состояние:  сервер {0} не отвечает — запущен ли он, проброшен ли порт 7777 TCP+UDP?", "Status:  Server {0} antwortet nicht — läuft er, ist Port 7777 TCP+UDP weitergeleitet?", "État :  le serveur {0} ne répond pas — tourne-t-il, le port 7777 TCP+UDP est-il redirigé ?", "Estado:  el servidor {0} no responde — ¿está en marcha, está redirigido el puerto 7777 TCP+UDP?", "Estado:  o servidor {0} não responde — está rodando, a porta 7777 TCP+UDP está encaminhada?", "Stan:  serwer {0} nie odpowiada — czy działa, czy port 7777 TCP+UDP jest przekierowany?", "Stato:  il server {0} non risponde — è attivo, la porta 7777 TCP+UDP è aperta?", "状态：服务器 {0} 无响应 — 它在运行吗？7777 端口（TCP+UDP）已转发吗？", "狀態：伺服器 {0} 無回應 — 它在執行嗎？7777 連接埠（TCP+UDP）已轉發嗎？" },
        ["player.status.signingin"] = new[] { "Status:  signing in at {0}…", "Состояние:  вход на {0}…", "Status:  Anmeldung bei {0}…", "État :  connexion à {0}…", "Estado:  iniciando sesión en {0}…", "Estado:  entrando em {0}…", "Stan:  logowanie do {0}…", "Stato:  accesso a {0}…", "状态：正在登录 {0}…", "狀態：正在登入 {0}…" },
        ["player.status.waitsave"] = new[] { "Status:  server found; joins once a save is loaded", "Состояние:  сервер найден, подключится после загрузки сохранения", "Status:  Server gefunden; tritt bei, sobald ein Spielstand geladen ist", "État :  serveur trouvé ; rejoint dès qu'une sauvegarde est chargée", "Estado:  servidor encontrado; se unirá al cargar una partida", "Estado:  servidor encontrado; entra ao carregar um jogo salvo", "Stan:  serwer znaleziony; dołączy po wczytaniu zapisu", "Stato:  server trovato; entra al caricamento di un salvataggio", "状态：已找到服务器，加载存档后加入", "狀態：已找到伺服器，載入存檔後加入" },
        ["player.status.connecting"] = new[] { "Status:  joining {0}…", "Состояние:  подключение к {0}…", "Status:  Beitritt zu {0}…", "État :  connexion à {0}…", "Estado:  uniéndose a {0}…", "Estado:  entrando em {0}…", "Stan:  dołączanie do {0}…", "Stato:  ingresso in {0}…", "状态：正在加入 {0}…", "狀態：正在加入 {0}…" },

        ["player.copy"] = new[] { "Copy server address", "Скопировать адрес сервера", "Serveradresse kopieren", "Copier l'adresse du serveur", "Copiar dirección del servidor", "Copiar endereço do servidor", "Kopiuj adres serwera", "Copia l'indirizzo del server", "复制服务器地址", "複製伺服器位址" },
        ["player.online"] = new[] { "On the server ({0}):  {1}", "На сервере ({0}):  {1}", "Auf dem Server ({0}):  {1}", "Sur le serveur ({0}) :  {1}", "En el servidor ({0}):  {1}", "No servidor ({0}):  {1}", "Na serwerze ({0}):  {1}", "Sul server ({0}):  {1}", "服务器上（{0}）：{1}", "伺服器上（{0}）：{1}" },
        ["player.online.none"] = new[] { "On the server:  nobody yet — you will be the first", "На сервере:  пока никого — вы будете первым", "Auf dem Server:  noch niemand — Sie wären der Erste", "Sur le serveur :  personne pour l'instant — vous serez le premier", "En el servidor:  nadie todavía — serás el primero", "No servidor:  ninguém ainda — você será o primeiro", "Na serwerze:  jeszcze nikogo — będziesz pierwszy", "Sul server:  ancora nessuno — sarai il primo", "服务器上：暂时没有人 — 你将是第一个", "伺服器上：暫時沒有人 — 你將是第一個" },
        ["player.online.unknown"] = new[] { "On the server:  unknown until it answers", "На сервере:  неизвестно, пока сервер не ответит", "Auf dem Server:  unbekannt, bis er antwortet", "Sur le serveur :  inconnu tant qu'il ne répond pas", "En el servidor:  desconocido hasta que responda", "No servidor:  desconhecido até responder", "Na serwerze:  nieznane, dopóki nie odpowie", "Sul server:  sconosciuto finché non risponde", "服务器上：服务器响应前未知", "伺服器上：伺服器回應前未知" },
        ["player.online.old"] = new[] { "On the server:  the server is older than this mod and does not say", "На сервере:  сервер старше мода и не сообщает", "Auf dem Server:  der Server ist älter als der Mod und sagt es nicht", "Sur le serveur :  le serveur est plus ancien que le mod et ne le dit pas", "En el servidor:  el servidor es más antiguo que el mod y no lo dice", "No servidor:  o servidor é mais antigo que o mod e não informa", "Na serwerze:  serwer jest starszy od moda i nie podaje", "Sul server:  il server è più vecchio della mod e non lo dice", "服务器上：服务器版本早于本模组，无法获知", "伺服器上：伺服器版本早於本模組，無法得知" },

        // Server line and invitations
        ["server.checking"] = new[] { "Server {0}:  checking…", "Сервер {0}:  проверяю…", "Server {0}:  wird geprüft…", "Serveur {0} :  vérification…", "Servidor {0}:  comprobando…", "Servidor {0}:  verificando…", "Serwer {0}:  sprawdzanie…", "Server {0}:  verifica…", "服务器 {0}：检查中…", "伺服器 {0}：檢查中…" },
        ["server.online"] = new[] { "Server {0}:  {1} online — {2}", "Сервер {0}:  онлайн {1} — {2}", "Server {0}:  {1} online — {2}", "Serveur {0} :  {1} en ligne — {2}", "Servidor {0}:  {1} en línea — {2}", "Servidor {0}:  {1} online — {2}", "Serwer {0}:  {1} online — {2}", "Server {0}:  {1} online — {2}", "服务器 {0}：{1} 人在线 — {2}", "伺服器 {0}：{1} 人在線 — {2}" },
        ["server.empty"] = new[] { "Server {0}:  up, nobody on it yet", "Сервер {0}:  работает, пока никого", "Server {0}:  läuft, noch niemand drauf", "Serveur {0} :  en ligne, personne pour l'instant", "Servidor {0}:  activo, todavía nadie", "Servidor {0}:  ativo, ninguém ainda", "Serwer {0}:  działa, jeszcze nikogo", "Server {0}:  attivo, ancora nessuno", "服务器 {0}：在线，暂时没有人", "伺服器 {0}：在線，暫時沒有人" },
        ["server.down"] = new[] { "Server {0}:  not answering", "Сервер {0}:  не отвечает", "Server {0}:  antwortet nicht", "Serveur {0} :  ne répond pas", "Servidor {0}:  no responde", "Servidor {0}:  não responde", "Serwer {0}:  nie odpowiada", "Server {0}:  non risponde", "服务器 {0}：无响应", "伺服器 {0}：無回應" },
        ["server.old"] = new[] { "Server {0}:  answers, but is older than this mod", "Сервер {0}:  отвечает, но старше мода", "Server {0}:  antwortet, ist aber älter als der Mod", "Serveur {0} :  répond, mais est plus ancien que le mod", "Servidor {0}:  responde, pero es más antiguo que el mod", "Servidor {0}:  responde, mas é mais antigo que o mod", "Serwer {0}:  odpowiada, ale jest starszy od moda", "Server {0}:  risponde, ma è più vecchio della mod", "服务器 {0}：有响应，但版本早于本模组", "伺服器 {0}：有回應，但版本早於本模組" },
        ["join.notice"] = new[] { "Steam invite accepted — server {0}. Load a save to join.", "Приглашение Steam принято — сервер {0}. Загрузите сохранение, чтобы войти.", "Steam-Einladung angenommen — Server {0}. Laden Sie einen Spielstand, um beizutreten.", "Invitation Steam acceptée — serveur {0}. Chargez une sauvegarde pour rejoindre.", "Invitación de Steam aceptada — servidor {0}. Carga una partida para unirte.", "Convite do Steam aceito — servidor {0}. Carregue um jogo salvo para entrar.", "Zaproszenie Steam przyjęte — serwer {0}. Wczytaj zapis, aby dołączyć.", "Invito Steam accettato — server {0}. Carica un salvataggio per entrare.", "已接受 Steam 邀请 — 服务器 {0}。加载存档即可加入。", "已接受 Steam 邀請 — 伺服器 {0}。載入存檔即可加入。" },
        ["invite.steam"] = new[] { "Invite friends via Steam", "Пригласить друзей через Steam", "Freunde über Steam einladen", "Inviter des amis via Steam", "Invitar amigos por Steam", "Convidar amigos pelo Steam", "Zaproś znajomych przez Steam", "Invita amici tramite Steam", "通过 Steam 邀请好友", "透過 Steam 邀請好友" },
        ["invite.opened"] = new[] { "Steam overlay opened — pick the friends to invite", "Открыт оверлей Steam — выберите, кого пригласить", "Steam-Overlay geöffnet — Freunde zum Einladen wählen", "Overlay Steam ouvert — choisissez qui inviter", "Overlay de Steam abierto — elige a quién invitar", "Overlay do Steam aberto — escolha quem convidar", "Otwarto nakładkę Steam — wybierz, kogo zaprosić", "Overlay di Steam aperto — scegli chi invitare", "已打开 Steam 界面 — 选择要邀请的好友", "已開啟 Steam 介面 — 選擇要邀請的好友" },
        ["invite.noaddress"] = new[] { "Invite friends via Steam — start the server or connect first", "Пригласить через Steam — сначала запустите сервер или подключитесь", "Über Steam einladen — erst Server starten oder verbinden", "Inviter via Steam — démarrez d'abord le serveur ou connectez-vous", "Invitar por Steam — primero inicia el servidor o conéctate", "Convidar pelo Steam — inicie o servidor ou conecte-se primeiro", "Zaproś przez Steam — najpierw uruchom serwer lub połącz się", "Invita tramite Steam — prima avvia il server o connettiti", "通过 Steam 邀请 — 请先启动服务器或连接", "透過 Steam 邀請 — 請先啟動伺服器或連線" },
        ["invite.failed"] = new[] { "Steam did not open the invite dialog", "Steam не открыл окно приглашения", "Steam hat den Einladungsdialog nicht geöffnet", "Steam n'a pas ouvert la fenêtre d'invitation", "Steam no abrió el diálogo de invitación", "O Steam não abriu a janela de convite", "Steam nie otworzył okna zaproszenia", "Steam non ha aperto la finestra di invito", "Steam 未打开邀请窗口", "Steam 未開啟邀請視窗" },
        ["friends.header"] = new[] { "Friends on servers — press to join:", "Друзья на серверах — нажмите, чтобы присоединиться:", "Freunde auf Servern — zum Beitreten drücken:", "Amis sur des serveurs — appuyez pour rejoindre :", "Amigos en servidores — pulsa para unirte:", "Amigos em servidores — pressione para entrar:", "Znajomi na serwerach — naciśnij, aby dołączyć:", "Amici sui server — premi per unirti:", "好友所在的服务器 — 点击加入：", "好友所在的伺服器 — 點擊加入：" },
        ["friends.none"] = new[] { "No friends on a server right now. Invite them — while you are on a server they see “Join game” in Steam.", "Сейчас никого из друзей нет на серверах. Пригласите их — пока вы на сервере, у них в Steam есть кнопка «Присоединиться к игре».", "Gerade ist kein Freund auf einem Server. Laden Sie sie ein — solange Sie auf einem Server sind, sehen sie in Steam „Spiel beitreten“.", "Aucun ami sur un serveur pour l'instant. Invitez-les — tant que vous êtes sur un serveur, ils voient « Rejoindre la partie » dans Steam.", "Ningún amigo está en un servidor ahora. Invítalos — mientras estés en un servidor verán «Unirse a la partida» en Steam.", "Nenhum amigo em um servidor agora. Convide-os — enquanto você estiver em um servidor, eles veem “Entrar no jogo” no Steam.", "Żaden znajomy nie jest teraz na serwerze. Zaproś ich — gdy jesteś na serwerze, widzą w Steam „Dołącz do gry”.", "Nessun amico su un server al momento. Invitali — finché sei su un server vedono “Partecipa alla partita” in Steam.", "目前没有好友在服务器上。邀请他们 — 你在服务器上时，他们的 Steam 会显示“加入游戏”。", "目前沒有好友在伺服器上。邀請他們 — 你在伺服器上時，他們的 Steam 會顯示「加入遊戲」。" },
        ["friends.waiting"] = new[] { "Steam is still starting — the friends list appears in a moment", "Steam ещё запускается — список друзей появится через мгновение", "Steam startet noch — die Freundesliste erscheint gleich", "Steam démarre encore — la liste d'amis arrive dans un instant", "Steam aún se está iniciando — la lista de amigos aparecerá en un momento", "O Steam ainda está iniciando — a lista de amigos aparece em instantes", "Steam jeszcze się uruchamia — lista znajomych pojawi się za chwilę", "Steam si sta ancora avviando — la lista amici arriva tra un istante", "Steam 仍在启动 — 好友列表稍后显示", "Steam 仍在啟動 — 好友列表稍後顯示" },
        ["friends.join"] = new[] { "Join {0}  —  {1}", "Присоединиться к {0}  —  {1}", "{0} beitreten  —  {1}", "Rejoindre {0}  —  {1}", "Unirse a {0}  —  {1}", "Entrar com {0}  —  {1}", "Dołącz do {0}  —  {1}", "Unisciti a {0}  —  {1}", "加入 {0}  —  {1}", "加入 {0}  —  {1}" },
        ["update.available"] = new[] { "Version {0} is out — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "Вышла версия {0} — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "Version {0} ist da — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "La version {0} est sortie — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "Ya está la versión {0} — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "Saiu a versão {0} — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "Wyszła wersja {0} — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "È uscita la versione {0} — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "新版本 {0} 已发布 — github.com/ShinyM2/StarTruckerMP-Enhanced/releases", "新版本 {0} 已發布 — github.com/ShinyM2/StarTruckerMP-Enhanced/releases" },
        ["update.short"] = new[] { "{0} available", "доступна {0}", "{0} verfügbar", "{0} disponible", "{0} disponible", "{0} disponível", "dostępna {0}", "{0} disponibile", "有新版本 {0}", "有新版本 {0}" },
        ["monitor.stats"] = new[] { "{0} ms · loss {1}% · buffer {2} ms", "{0} мс · потери {1}% · буфер {2} мс", "{0} ms · Verlust {1}% · Puffer {2} ms", "{0} ms · pertes {1}% · tampon {2} ms", "{0} ms · pérdida {1}% · búfer {2} ms", "{0} ms · perda {1}% · buffer {2} ms", "{0} ms · straty {1}% · bufor {2} ms", "{0} ms · perdita {1}% · buffer {2} ms", "{0} 毫秒 · 丢包 {1}% · 缓冲 {2} 毫秒", "{0} 毫秒 · 丟包 {1}% · 緩衝 {2} 毫秒" },

        // Host page
        ["host.server"] = new[] { "Server:  ", "Сервер:  ", "Server:  ", "Serveur :  ", "Servidor:  ", "Servidor:  ", "Serwer:  ", "Server:  ", "服务器：", "伺服器：" },
        ["host.start"] = new[] { "start", "запустить", "starten", "démarrer", "iniciar", "iniciar", "uruchom", "avvia", "启动", "啟動" },
        ["host.stop"] = new[] { "stop", "остановить", "stoppen", "arrêter", "detener", "parar", "zatrzymaj", "arresta", "停止", "停止" },
        ["host.running"] = new[] { "Server is running on this machine", "Сервер работает на этом компьютере", "Der Server läuft auf diesem Rechner", "Le serveur tourne sur cette machine", "El servidor se está ejecutando en este equipo", "O servidor está rodando nesta máquina", "Serwer działa na tym komputerze", "Il server è in esecuzione su questo computer", "服务器正在本机运行", "伺服器正在本機執行" },
        ["host.notrunning"] = new[] { "Server is not running", "Сервер не запущен", "Der Server läuft nicht", "Le serveur est arrêté", "El servidor no está en ejecución", "O servidor não está rodando", "Serwer nie działa", "Il server non è in esecuzione", "服务器未运行", "伺服器未執行" },
        ["host.share"] = new[] { "Give friends:  {0}  ·  port {1} (TCP and UDP)", "Друзьям:  {0}  ·  порт {1} (TCP и UDP)", "Für Freunde:  {0}  ·  Port {1} (TCP und UDP)", "Pour vos amis :  {0}  ·  port {1} (TCP et UDP)", "Para tus amigos:  {0}  ·  puerto {1} (TCP y UDP)", "Para os amigos:  {0}  ·  porta {1} (TCP e UDP)", "Dla znajomych:  {0}  ·  port {1} (TCP i UDP)", "Per gli amici:  {0}  ·  porta {1} (TCP e UDP)", "告诉好友：{0}  ·  端口 {1}（TCP 和 UDP）", "告訴好友：{0}  ·  連接埠 {1}（TCP 和 UDP）" },
        ["host.share.hint"] = new[] { "Start the server to get an address to share", "Запустите сервер, чтобы получить адрес для друзей", "Starten Sie den Server, um eine Adresse zum Teilen zu erhalten", "Démarrez le serveur pour obtenir une adresse à partager", "Inicia el servidor para obtener una dirección que compartir", "Inicie o servidor para obter um endereço para compartilhar", "Uruchom serwer, aby uzyskać adres do udostępnienia", "Avvia il server per ottenere un indirizzo da condividere", "启动服务器以获取可分享的地址", "啟動伺服器以取得可分享的位址" },
        ["host.copied"] = new[] { "Copied to clipboard", "Скопировано в буфер обмена", "In die Zwischenablage kopiert", "Copié dans le presse-papiers", "Copiado al portapapeles", "Copiado para a área de transferência", "Skopiowano do schowka", "Copiato negli appunti", "已复制到剪贴板", "已複製到剪貼簿" },
        ["host.copy"] = new[] { "Copy details for friends", "Скопировать данные для друзей", "Daten für Freunde kopieren", "Copier les infos pour vos amis", "Copiar datos para tus amigos", "Copiar dados para os amigos", "Kopiuj dane dla znajomych", "Copia i dati per gli amici", "复制好友连接信息", "複製好友連線資訊" },
        ["host.text.title"] = new[] { "StarTruckMP server", "Сервер StarTruckMP", "StarTruckMP-Server", "Serveur StarTruckMP", "Servidor StarTruckMP", "Servidor StarTruckMP", "Serwer StarTruckMP", "Server StarTruckMP", "StarTruckMP 服务器", "StarTruckMP 伺服器" },
        ["host.text.address"] = new[] { "Address", "Адрес", "Adresse", "Adresse", "Dirección", "Endereço", "Adres", "Indirizzo", "地址", "位址" },
        ["host.text.port"] = new[] { "Port: {0} (TCP and UDP)", "Порт: {0} (TCP и UDP)", "Port: {0} (TCP und UDP)", "Port : {0} (TCP et UDP)", "Puerto: {0} (TCP y UDP)", "Porta: {0} (TCP e UDP)", "Port: {0} (TCP i UDP)", "Porta: {0} (TCP e UDP)", "端口：{0}（TCP 和 UDP）", "連接埠：{0}（TCP 和 UDP）" },
        ["host.text.local"] = new[] { "Same network", "В одной сети", "Im selben Netzwerk", "Même réseau", "Misma red", "Mesma rede", "Ta sama sieć", "Stessa rete", "同一网络", "同一網路" },
        ["host.msg.exited"] = new[] { "The server has exited.", "Сервер завершился.", "Der Server wurde beendet.", "Le serveur s'est arrêté.", "El servidor se ha cerrado.", "O servidor encerrou.", "Serwer zakończył pracę.", "Il server si è chiuso.", "服务器已退出。", "伺服器已結束。" },
        ["host.msg.crashed"] = new[] { "The server stopped right after starting (code {0}). Most likely the port is taken.", "Сервер остановился сразу после запуска (код {0}). Скорее всего, порт занят.", "Der Server hielt direkt nach dem Start an (Code {0}). Vermutlich ist der Port belegt.", "Le serveur s'est arrêté juste après le démarrage (code {0}). Le port est probablement occupé.", "El servidor se detuvo justo tras iniciarse (código {0}). Probablemente el puerto está ocupado.", "O servidor parou logo após iniciar (código {0}). Provavelmente a porta está ocupada.", "Serwer zatrzymał się zaraz po uruchomieniu (kod {0}). Prawdopodobnie port jest zajęty.", "Il server si è fermato subito dopo l'avvio (codice {0}). Probabilmente la porta è occupata.", "服务器启动后立即停止（代码 {0}）。端口很可能被占用。", "伺服器啟動後立即停止（代碼 {0}）。連接埠很可能被占用。" },
        ["host.msg.already"] = new[] { "The server is already running.", "Сервер уже запущен.", "Der Server läuft bereits.", "Le serveur tourne déjà.", "El servidor ya está en marcha.", "O servidor já está rodando.", "Serwer już działa.", "Il server è già in esecuzione.", "服务器已在运行。", "伺服器已在執行。" },
        ["host.msg.noexe"] = new[] { "There is no StarTruckMP.Server.exe next to the plugin.", "Рядом с плагином нет StarTruckMP.Server.exe.", "Neben dem Plugin liegt keine StarTruckMP.Server.exe.", "Aucun StarTruckMP.Server.exe à côté du plugin.", "No hay StarTruckMP.Server.exe junto al plugin.", "Não há StarTruckMP.Server.exe ao lado do plugin.", "Obok wtyczki nie ma StarTruckMP.Server.exe.", "Non c'è StarTruckMP.Server.exe accanto al plugin.", "插件旁边没有 StarTruckMP.Server.exe。", "外掛旁邊沒有 StarTruckMP.Server.exe。" },
        ["host.msg.portbusy"] = new[] { "Port {0} is already taken — a server seems to be running. Join as a player.", "Порт {0} уже занят — сервер, похоже, уже работает. Подключайтесь как игрок.", "Port {0} ist bereits belegt — offenbar läuft schon ein Server. Treten Sie als Spieler bei.", "Le port {0} est déjà pris — un serveur semble tourner. Rejoignez en tant que joueur.", "El puerto {0} ya está ocupado — parece que hay un servidor en marcha. Únete como jugador.", "A porta {0} já está ocupada — parece que um servidor está rodando. Entre como jogador.", "Port {0} jest już zajęty — serwer chyba już działa. Dołącz jako gracz.", "La porta {0} è già occupata — sembra che un server sia attivo. Entra come giocatore.", "端口 {0} 已被占用，似乎已有服务器在运行。请以玩家身份加入。", "連接埠 {0} 已被占用，似乎已有伺服器在執行。請以玩家身分加入。" },
        ["host.msg.refused"] = new[] { "Windows refused to start the server.", "Windows отказался запускать сервер.", "Windows hat den Serverstart verweigert.", "Windows a refusé de lancer le serveur.", "Windows se negó a iniciar el servidor.", "O Windows recusou iniciar o servidor.", "Windows odmówił uruchomienia serwera.", "Windows ha rifiutato di avviare il server.", "Windows 拒绝启动服务器。", "Windows 拒絕啟動伺服器。" },
        ["host.msg.started"] = new[] { "Server started.", "Сервер запущен.", "Server gestartet.", "Serveur démarré.", "Servidor iniciado.", "Servidor iniciado.", "Serwer uruchomiony.", "Server avviato.", "服务器已启动。", "伺服器已啟動。" },
        ["host.msg.failed"] = new[] { "Could not start the server: {0}", "Не удалось запустить сервер: {0}", "Server konnte nicht gestartet werden: {0}", "Impossible de démarrer le serveur : {0}", "No se pudo iniciar el servidor: {0}", "Não foi possível iniciar o servidor: {0}", "Nie udało się uruchomić serwera: {0}", "Impossibile avviare il server: {0}", "无法启动服务器：{0}", "無法啟動伺服器：{0}" },
        ["host.msg.stopped"] = new[] { "Server stopped.", "Сервер остановлен.", "Server gestoppt.", "Serveur arrêté.", "Servidor detenido.", "Servidor parado.", "Serwer zatrzymany.", "Server arrestato.", "服务器已停止。", "伺服器已停止。" },

        // Other players page: what the trucks of other players do on your screen
        ["display.nameplates"] = new[] { "Nameplates:  ", "Ники над грузовиками:  ", "Namensschilder:  ", "Pseudos au-dessus des camions :  ", "Nombres sobre los camiones:  ", "Nomes sobre os caminhões:  ", "Nazwy nad ciężarówkami:  ", "Nomi sopra i camion:  ", "卡车上方的玩家名：", "卡車上方的玩家名：" },
        ["display.collisions"] = new[] { "Collide with players:  ", "Столкновения с игроками:  ", "Kollisionen mit Spielern:  ", "Collisions avec les joueurs :  ", "Colisiones con jugadores:  ", "Colisões com jogadores:  ", "Kolizje z graczami:  ", "Collisioni con i giocatori:  ", "与玩家碰撞：", "與玩家碰撞：" },
        ["display.ghost"] = new[] { "Ghost at gates and bays:  ", "Прозрачные у ворот и боксов:  ", "Durchsichtig an Toren und Docks:  ", "Fantôme aux portails et quais :  ", "Transparentes en portales y muelles:  ", "Transparentes em portais e docas:  ", "Przezroczyste przy bramach i dokach:  ", "Trasparenti a portali e moli:  ", "在跃迁门和泊位处透明：", "在躍遷門和泊位處透明：" },

        // Mod page: the mod itself, not the game
        ["mod.nopause"] = new[] { "No pause while on a server:  ", "Без паузы на сервере:  ", "Keine Pause auf einem Server:  ", "Pas de pause sur un serveur :  ", "Sin pausa en un servidor:  ", "Sem pausa em um servidor:  ", "Bez pauzy na serwerze:  ", "Nessuna pausa su un server:  ", "在服务器上不暂停：", "在伺服器上不暫停：" },
        ["mod.nopause.hint"] = new[] { "Menus and the map no longer freeze the world — as it has to be with other players around.", "Меню и карта больше не останавливают мир — иначе с другими игроками нельзя.", "Menüs und die Karte halten die Welt nicht mehr an — mit anderen Spielern geht es nicht anders.", "Les menus et la carte ne figent plus le monde — indispensable avec d'autres joueurs.", "Los menús y el mapa ya no congelan el mundo — imprescindible con otros jugadores.", "Menus e o mapa não congelam mais o mundo — necessário com outros jogadores.", "Menu i mapa nie zatrzymują już świata — inaczej z innymi graczami się nie da.", "I menu e la mappa non congelano più il mondo — necessario con altri giocatori.", "菜单和地图不再冻结世界 — 有其他玩家时必须如此。", "選單和地圖不再凍結世界 — 有其他玩家時必須如此。" },
        ["mod.updates"] = new[] { "Check for updates:  ", "Проверять обновления:  ", "Nach Updates suchen:  ", "Vérifier les mises à jour :  ", "Buscar actualizaciones:  ", "Verificar atualizações:  ", "Sprawdzaj aktualizacje:  ", "Controlla aggiornamenti:  ", "检查更新：", "檢查更新：" },
        ["mod.version"] = new[] { "Mod version:  ", "Версия мода:  ", "Mod-Version:  ", "Version du mod :  ", "Versión del mod:  ", "Versão do mod:  ", "Wersja moda:  ", "Versione della mod:  ", "模组版本：", "模組版本：" },

        // Radio and chat page
        ["comms.chatkey"] = new[] { "Chat key:  ", "Клавиша чата:  ", "Chat-Taste:  ", "Touche du chat :  ", "Tecla del chat:  ", "Tecla do chat:  ", "Klawisz czatu:  ", "Tasto chat:  ", "聊天按键：", "聊天按鍵：" },
        ["comms.presskey"] = new[] { "press a key", "нажмите клавишу", "Taste drücken", "appuyez sur une touche", "pulsa una tecla", "pressione uma tecla", "naciśnij klawisz", "premi un tasto", "请按一个键", "請按一個鍵" },
        ["voice.trucks"] = new[] { "Hear radios in nearby trucks:  ", "Слышать рации соседних грузовиков:  ", "Funk aus nahen Lkw hören:  ", "Entendre la radio des camions proches :  ", "Oír la radio de camiones cercanos:  ", "Ouvir o rádio de caminhões próximos:  ", "Słyszeć radio z pobliskich ciężarówek:  ", "Sentire la radio dei camion vicini:  ", "听到附近卡车的电台：", "聽到附近卡車的電台：" },
        ["voice.microphone"] = new[] { "Microphone:  ", "Микрофон:  ", "Mikrofon:  ", "Microphone :  ", "Micrófono:  ", "Microfone:  ", "Mikrofon:  ", "Microfono:  ", "麦克风：", "麥克風：" },
        ["voice.auto"] = new[] { "auto", "авто", "auto", "auto", "auto", "auto", "auto", "auto", "自动", "自動" },
        ["voice.test"] = new[] { "Test microphone — hear yourself", "Проверить микрофон — услышать себя", "Mikrofon testen — sich selbst hören", "Tester le micro — s'entendre", "Probar micrófono — escucharte", "Testar microfone — ouvir a si mesmo", "Test mikrofonu — usłysz siebie", "Prova microfono — ascoltati", "测试麦克风 — 听听自己的声音", "測試麥克風 — 聽聽自己的聲音" },
        ["voice.testing"] = new[] { "Testing:  ", "Проверка:  ", "Test:  ", "Test :  ", "Prueba:  ", "Teste:  ", "Test:  ", "Prova:  ", "测试中：", "測試中：" },
        ["voice.test.stop"] = new[] { "  — press to stop", "  — нажмите, чтобы закончить", "  — zum Beenden drücken", "  — appuyez pour arrêter", "  — pulsa para detener", "  — pressione para parar", "  — naciśnij, aby zakończyć", "  — premi per fermare", "  — 按下停止", "  — 按下停止" },
        ["voice.test.nomic"] = new[] { "no microphone", "микрофон не найден", "kein Mikrofon", "aucun micro", "sin micrófono", "sem microfone", "brak mikrofonu", "nessun microfono", "未找到麦克风", "未找到麥克風" },
        ["voice.micvolume"] = new[] { "Microphone volume:  ", "Громкость микрофона:  ", "Mikrofonlautstärke:  ", "Volume du micro :  ", "Volumen del micrófono:  ", "Volume do microfone:  ", "Głośność mikrofonu:  ", "Volume microfono:  ", "麦克风音量：", "麥克風音量：" },
        ["voice.denoise"] = new[] { "Noise suppression:  ", "Шумоподавление:  ", "Rauschunterdrückung:  ", "Réduction du bruit :  ", "Supresión de ruido:  ", "Supressão de ruído:  ", "Redukcja szumów:  ", "Riduzione del rumore:  ", "噪声抑制：", "噪音抑制：" },
        ["voice.unavailable"] = new[] { "unavailable", "недоступно", "nicht verfügbar", "indisponible", "no disponible", "indisponível", "niedostępne", "non disponibile", "不可用", "不可用" },
        ["voice.radiovolume"] = new[] { "Radio volume:  ", "Громкость рации:  ", "Funklautstärke:  ", "Volume de la radio :  ", "Volumen de la radio:  ", "Volume do rádio:  ", "Głośność radia:  ", "Volume radio:  ", "无线电音量：", "無線電音量：" },
        ["voice.mutedialogue"] = new[] { "Mute players during a dialogue:  ", "Не слышать игроков во время диалога:  ", "Spieler während eines Dialogs stumm:  ", "Couper les joueurs pendant un dialogue :  ", "Silenciar jugadores durante un diálogo:  ", "Silenciar jogadores durante um diálogo:  ", "Wycisz graczy podczas dialogu:  ", "Silenzia i giocatori durante un dialogo:  ", "对话期间静音其他玩家：", "對話期間靜音其他玩家：" },
        ["voice.effect"] = new[] { "Radio sound:  ", "Звук рации:  ", "Funkklang:  ", "Son de la radio :  ", "Sonido de radio:  ", "Som de rádio:  ", "Brzmienie radia:  ", "Suono radio:  ", "无线电音效：", "無線電音效：" },
        ["voice.effect.off"] = new[] { "clean voice", "чистый голос", "klare Stimme", "voix claire", "voz limpia", "voz limpa", "czysty głos", "voce pulita", "原声", "原聲" },
        ["voice.effect.light"] = new[] { "light", "лёгкий", "leicht", "léger", "ligero", "leve", "lekkie", "leggero", "轻微", "輕微" },
        ["voice.effect.full"] = new[] { "CB radio", "как в рации", "CB-Funk", "radio CB", "radio CB", "rádio CB", "radio CB", "radio CB", "CB 电台", "CB 電台" },

        // Cab monitor
        ["monitor.title"] = new[] { "MULTIPLAYER", "МУЛЬТИПЛЕЕР", "MEHRSPIELER", "MULTIJOUEUR", "MULTIJUGADOR", "MULTIJOGADOR", "TRYB WIELOOSOBOWY", "MULTIGIOCATORE", "多人游戏", "多人遊戲" },
        ["monitor.noconnection"] = new[] { "NO CONNECTION TO THE SERVER", "НЕТ СВЯЗИ С СЕРВЕРОМ", "KEINE VERBINDUNG ZUM SERVER", "PAS DE CONNEXION AU SERVEUR", "SIN CONEXIÓN CON EL SERVIDOR", "SEM CONEXÃO COM O SERVIDOR", "BRAK POŁĄCZENIA Z SERWEREM", "NESSUNA CONNESSIONE AL SERVER", "未连接到服务器", "未連線到伺服器" },
        ["monitor.online"] = new[] { "Online: ", "На сервере: ", "Online: ", "En ligne : ", "En línea: ", "Online: ", "Online: ", "Online: ", "在线：", "線上：" },
        ["monitor.nobody"] = new[] { "nobody else", "больше никого", "sonst niemand", "personne d'autre", "nadie más", "mais ninguém", "nikogo więcej", "nessun altro", "没有其他人", "沒有其他人" },
        ["monitor.nearby"] = new[] { "nearby", "рядом", "in der Nähe", "à proximité", "cerca", "por perto", "w pobliżu", "vicino", "附近", "附近" },
        ["monitor.ms"] = new[] { " ms", " мс", " ms", " ms", " ms", " ms", " ms", " ms", " 毫秒", " 毫秒" },
        ["monitor.you"] = new[] { "You", "Вы", "Sie", "Vous", "Tú", "Você", "Ty", "Tu", "你", "你" },
        ["monitor.nochat"] = new[] { "No messages yet", "Чат пуст", "Noch keine Nachrichten", "Aucun message", "Sin mensajes todavía", "Nenhuma mensagem ainda", "Brak wiadomości", "Nessun messaggio", "暂无消息", "暫無訊息" },
        ["monitor.type"] = new[] { "TYPE", "НАПИСАТЬ", "SCHREIBEN", "ÉCRIRE", "ESCRIBIR", "ESCREVER", "PISZ", "SCRIVI", "输入", "輸入" },
        ["monitor.sit"] = new[] { "TAKE THE SEAT TO CHAT", "СЯДЬТЕ В КРЕСЛО, ЧТОБЫ ПИСАТЬ", "ZUM CHATTEN HINSETZEN", "ASSEYEZ-VOUS POUR ÉCRIRE", "SIÉNTATE PARA ESCRIBIR", "SENTE-SE PARA ESCREVER", "USIĄDŹ, ABY PISAĆ", "SIEDITI PER SCRIVERE", "坐到座位上即可聊天", "坐到座位上即可聊天" },

        // Units
        ["unit.m"] = new[] { "m", "м", "m", "m", "m", "m", "m", "m", "米", "米" },
        ["unit.km"] = new[] { "km", "км", "km", "km", "km", "km", "km", "km", "公里", "公里" },

        // Nameplates
        ["nameplate.ghost"] = new[] { "GHOST", "ГОСТ-РЕЖИМ", "GEIST", "FANTÔME", "FANTASMA", "FANTASMA", "DUCH", "FANTASMA", "幽灵", "幽靈" },

        // Notices (also shown in the overlay)
        ["notice.notconnected"] = new[] { "Not connected to a server.", "Не подключено к серверу.", "Nicht mit einem Server verbunden.", "Non connecté à un serveur.", "Sin conexión con un servidor.", "Não conectado a um servidor.", "Brak połączenia z serwerem.", "Non connesso a un server.", "未连接到服务器。", "未連線到伺服器。" },
        ["notice.addresssaved"] = new[] { "Address saved. Reconnecting…", "Адрес сохранён. Переподключаюсь…", "Adresse gespeichert. Verbinde neu…", "Adresse enregistrée. Reconnexion…", "Dirección guardada. Reconectando…", "Endereço salvo. Reconectando…", "Adres zapisany. Ponowne łączenie…", "Indirizzo salvato. Riconnessione…", "地址已保存。正在重新连接…", "位址已儲存。正在重新連線…" },
        ["notice.saved"] = new[] { "Settings saved.", "Настройки сохранены.", "Einstellungen gespeichert.", "Paramètres enregistrés.", "Ajustes guardados.", "Configurações salvas.", "Ustawienia zapisane.", "Impostazioni salvate.", "设置已保存。", "設定已儲存。" },

        // The F2 overlay
        ["overlay.title"] = new[] { "Multiplayer", "Мультиплеер", "Mehrspieler", "Multijoueur", "Multijugador", "Multijogador", "Tryb wieloosobowy", "Multigiocatore", "多人游戏", "多人遊戲" },
        ["overlay.connected"] = new[] { "connected", "подключено", "verbunden", "connecté", "conectado", "conectado", "połączono", "connesso", "已连接", "已連線" },
        ["overlay.disconnected"] = new[] { "not connected", "не подключено", "nicht verbunden", "non connecté", "sin conexión", "desconectado", "brak połączenia", "non connesso", "未连接", "未連線" },
        ["overlay.close"] = new[] { "Close", "Закрыть", "Schließen", "Fermer", "Cerrar", "Fechar", "Zamknij", "Chiudi", "关闭", "關閉" },
        ["overlay.tab.connect"] = new[] { "Connection", "Подключение", "Verbindung", "Connexion", "Conexión", "Conexão", "Połączenie", "Connessione", "连接", "連線" },
        ["overlay.tab.host"] = new[] { "Your server", "Свой сервер", "Eigener Server", "Votre serveur", "Tu servidor", "Seu servidor", "Twój serwer", "Il tuo server", "你的服务器", "你的伺服器" },
        ["overlay.tab.chat"] = new[] { "Chat", "Чат", "Chat", "Chat", "Chat", "Chat", "Czat", "Chat", "聊天", "聊天" },
        ["overlay.tab.settings"] = new[] { "Settings", "Настройки", "Einstellungen", "Paramètres", "Ajustes", "Configurações", "Ustawienia", "Impostazioni", "设置", "設定" },
        ["overlay.address"] = new[] { "Server address", "Адрес сервера", "Serveradresse", "Adresse du serveur", "Dirección del servidor", "Endereço do servidor", "Adres serwera", "Indirizzo del server", "服务器地址", "伺服器位址" },
        ["overlay.address.placeholder"] = new[] { "e.g. 203.0.113.10", "например 203.0.113.10", "z. B. 203.0.113.10", "p. ex. 203.0.113.10", "p. ej. 203.0.113.10", "ex.: 203.0.113.10", "np. 203.0.113.10", "es. 203.0.113.10", "例如 203.0.113.10", "例如 203.0.113.10" },
        ["overlay.port"] = new[] { "Port", "Порт", "Port", "Port", "Puerto", "Porta", "Port", "Porta", "端口", "連接埠" },
        ["overlay.saveconnect"] = new[] { "Save and connect", "Сохранить и подключиться", "Speichern und verbinden", "Enregistrer et se connecter", "Guardar y conectar", "Salvar e conectar", "Zapisz i połącz", "Salva e connetti", "保存并连接", "儲存並連線" },
        ["overlay.you"] = new[] { "You", "Вы", "Sie", "Vous", "Tú", "Você", "Ty", "Tu", "你", "你" },
        ["overlay.sector"] = new[] { "Sector", "Сектор", "Sektor", "Secteur", "Sector", "Setor", "Sektor", "Settore", "星区", "星區" },
        ["overlay.playerid"] = new[] { "Player number", "Номер игрока", "Spielernummer", "Numéro de joueur", "Número de jugador", "Número do jogador", "Numer gracza", "Numero giocatore", "玩家编号", "玩家編號" },
        ["overlay.orderhint"] = new[] { "Start order does not matter: if the server is not up yet, the client waits and connects by itself.", "Порядок запуска не важен: если сервер ещё не поднят, клиент подождёт и подключится сам.", "Die Startreihenfolge ist egal: Läuft der Server noch nicht, wartet der Client und verbindet sich von selbst.", "L'ordre de démarrage n'a pas d'importance : si le serveur n'est pas encore lancé, le client attend et se connecte tout seul.", "El orden de inicio no importa: si el servidor aún no está activo, el cliente espera y se conecta solo.", "A ordem de início não importa: se o servidor ainda não estiver ativo, o cliente espera e conecta sozinho.", "Kolejność uruchamiania nie ma znaczenia: jeśli serwer jeszcze nie działa, klient poczeka i połączy się sam.", "L'ordine di avvio non conta: se il server non è ancora attivo, il client aspetta e si connette da solo.", "启动顺序无关紧要：服务器尚未启动时，客户端会等待并自动连接。", "啟動順序無關緊要：伺服器尚未啟動時，客戶端會等待並自動連線。" },
        ["overlay.host.running"] = new[] { "The server is running on this machine.", "Сервер запущен на этой машине.", "Der Server läuft auf diesem Rechner.", "Le serveur tourne sur cette machine.", "El servidor se está ejecutando en este equipo.", "O servidor está rodando nesta máquina.", "Serwer działa na tym komputerze.", "Il server è in esecuzione su questo computer.", "服务器正在本机运行。", "伺服器正在本機執行。" },
        ["overlay.host.stop"] = new[] { "Stop the server", "Остановить сервер", "Server stoppen", "Arrêter le serveur", "Detener el servidor", "Parar o servidor", "Zatrzymaj serwer", "Arresta il server", "停止服务器", "停止伺服器" },
        ["overlay.host.start"] = new[] { "Start the server", "Запустить сервер", "Server starten", "Démarrer le serveur", "Iniciar el servidor", "Iniciar o servidor", "Uruchom serwer", "Avvia il server", "启动服务器", "啟動伺服器" },
        ["overlay.host.hint"] = new[] { "For friends to join, forward port 7777 — TCP and UDP — on your router to this computer and give them your public IP. Keep the game open while the server runs.", "Чтобы друзья могли зайти, на роутере нужно пробросить порт 7777 — и TCP, и UDP — на этот компьютер, а им сообщить ваш внешний IP. Пока сервер запущен, держите игру открытой.", "Damit Freunde beitreten können, leiten Sie am Router Port 7777 — TCP und UDP — an diesen Rechner weiter und geben ihnen Ihre öffentliche IP. Lassen Sie das Spiel offen, solange der Server läuft.", "Pour que vos amis se connectent, redirigez le port 7777 — TCP et UDP — de votre routeur vers cet ordinateur et donnez-leur votre IP publique. Gardez le jeu ouvert tant que le serveur tourne.", "Para que tus amigos entren, redirige el puerto 7777 — TCP y UDP — en tu router a este equipo y dales tu IP pública. Mantén el juego abierto mientras el servidor esté activo.", "Para os amigos entrarem, encaminhe a porta 7777 — TCP e UDP — no roteador para este computador e passe a eles o seu IP público. Mantenha o jogo aberto enquanto o servidor estiver rodando.", "Aby znajomi mogli dołączyć, przekieruj na routerze port 7777 — TCP i UDP — na ten komputer i podaj im swój publiczny adres IP. Nie zamykaj gry, gdy serwer działa.", "Perché gli amici possano entrare, apri sul router la porta 7777 — TCP e UDP — verso questo computer e dai loro il tuo IP pubblico. Tieni il gioco aperto finché il server è attivo.", "要让好友加入，请在路由器上将 7777 端口（TCP 和 UDP）转发到本机，并把你的公网 IP 告诉他们。服务器运行期间请保持游戏开启。", "要讓好友加入，請在路由器上將 7777 連接埠（TCP 和 UDP）轉發到本機，並把你的公網 IP 告訴他們。伺服器執行期間請保持遊戲開啟。" },
        ["overlay.host.missing"] = new[] { "There is no StarTruckMP.Server.exe next to the plugin, so nothing to start. The server can also run on its own — it ships with the release.", "Рядом с плагином нет StarTruckMP.Server.exe, запускать нечего. Сервер можно поднять отдельно — он лежит в релизе проекта.", "Neben dem Plugin liegt keine StarTruckMP.Server.exe, es gibt nichts zu starten. Der Server kann auch eigenständig laufen — er ist im Release enthalten.", "Aucun StarTruckMP.Server.exe à côté du plugin, rien à démarrer. Le serveur peut aussi tourner seul — il est fourni avec la version.", "No hay StarTruckMP.Server.exe junto al plugin, no hay nada que iniciar. El servidor también puede ejecutarse aparte — viene con la versión publicada.", "Não há StarTruckMP.Server.exe ao lado do plugin, nada para iniciar. O servidor também pode rodar separado — ele vem com a versão publicada.", "Obok wtyczki nie ma StarTruckMP.Server.exe, nie ma czego uruchomić. Serwer może też działać osobno — jest w wydaniu.", "Non c'è StarTruckMP.Server.exe accanto al plugin, niente da avviare. Il server può anche girare da solo — è incluso nella release.", "插件旁边没有 StarTruckMP.Server.exe，无法启动。服务器也可以单独运行，它随发行版一起提供。", "外掛旁邊沒有 StarTruckMP.Server.exe，無法啟動。伺服器也可以單獨執行，它隨發行版一起提供。" },
        ["overlay.chat.empty"] = new[] { "No messages yet.", "Сообщений пока нет.", "Noch keine Nachrichten.", "Aucun message pour l'instant.", "Sin mensajes todavía.", "Nenhuma mensagem ainda.", "Brak wiadomości.", "Nessun messaggio.", "暂无消息。", "暫無訊息。" },
        ["overlay.chat.all"] = new[] { "everyone", "всем", "alle", "à tous", "a todos", "a todos", "wszyscy", "a tutti", "所有人", "所有人" },
        ["overlay.chat.placeholder"] = new[] { "Message…", "Сообщение…", "Nachricht…", "Message…", "Mensaje…", "Mensagem…", "Wiadomość…", "Messaggio…", "消息…", "訊息…" },
        ["overlay.chat.send"] = new[] { "Send", "Отправить", "Senden", "Envoyer", "Enviar", "Enviar", "Wyślij", "Invia", "发送", "傳送" },
        ["overlay.chat.sectoronly"] = new[] { "Only my sector", "Только своему сектору", "Nur mein Sektor", "Seulement mon secteur", "Solo mi sector", "Só o meu setor", "Tylko mój sektor", "Solo il mio settore", "仅本星区", "僅本星區" },
        ["overlay.set.group.players"] = new[] { "Other players", "Другие игроки", "Andere Spieler", "Autres joueurs", "Otros jugadores", "Outros jogadores", "Inni gracze", "Altri giocatori", "其他玩家", "其他玩家" },
        ["overlay.set.group.comms"] = new[] { "Radio and chat", "Рация и чат", "Funk und Chat", "Radio et chat", "Radio y chat", "Rádio e chat", "Radio i czat", "Radio e chat", "无线电与聊天", "無線電與聊天" },
        ["overlay.set.group.mod"] = new[] { "Mod", "Мод", "Mod", "Mod", "Mod", "Mod", "Mod", "Mod", "模组", "模組" },
        ["overlay.set.ghost"] = new[] { "Ghost at gates and bays", "Прозрачные у ворот и боксов", "Durchsichtig an Toren und Docks", "Fantôme aux portails et quais", "Transparentes en portales y muelles", "Transparentes em portais e docas", "Przezroczyste przy bramach i dokach", "Trasparenti a portali e moli", "在跃迁门和泊位处透明", "在躍遷門和泊位處透明" },
        ["overlay.set.ghost.hint"] = new[] { "So a truck parked in a bay or a gate cannot block your own way through.", "Чтобы чужой грузовик в боксе или в воротах не перекрывал проезд.", "Damit ein fremder Lkw im Dock oder Tor den Weg nicht versperrt.", "Pour qu'un camion garé dans un quai ou un portail ne bloque pas le passage.", "Para que un camión aparcado en un muelle o portal no bloquee el paso.", "Para que um caminhão parado numa doca ou portal não bloqueie a passagem.", "Aby cudza ciężarówka w doku lub bramie nie blokowała przejazdu.", "Così un camion fermo in un molo o portale non blocca il passaggio.", "避免停在泊位或跃迁门里的卡车挡住你的路。", "避免停在泊位或躍遷門裡的卡車擋住你的路。" },
        ["overlay.set.chatkey"] = new[] { "Chat key (change it in the game menu)", "Клавиша чата (меняется в меню игры)", "Chat-Taste (im Spielmenü änderbar)", "Touche du chat (à changer dans le menu du jeu)", "Tecla del chat (se cambia en el menú del juego)", "Tecla do chat (altere no menu do jogo)", "Klawisz czatu (zmiana w menu gry)", "Tasto chat (si cambia nel menu di gioco)", "聊天按键（在游戏菜单中修改）", "聊天按鍵（在遊戲選單中修改）" },
        ["overlay.set.nameplates"] = new[] { "Nameplates above trucks", "Ники над грузовиками", "Namensschilder über Lkw", "Pseudos au-dessus des camions", "Nombres sobre los camiones", "Nomes sobre os caminhões", "Nazwy nad ciężarówkami", "Nomi sopra i camion", "卡车上方显示玩家名", "卡車上方顯示玩家名" },
        ["overlay.set.nameplates.hint"] = new[] { "Applies to players who appear after the change.", "Применится к тем, кто появится после переключения.", "Gilt für Spieler, die nach der Änderung erscheinen.", "S'applique aux joueurs qui apparaissent après le changement.", "Se aplica a los jugadores que aparezcan tras el cambio.", "Vale para os jogadores que aparecerem após a mudança.", "Dotyczy graczy, którzy pojawią się po zmianie.", "Vale per i giocatori che compaiono dopo la modifica.", "对更改后出现的玩家生效。", "對更改後出現的玩家生效。" },
        ["overlay.set.collisions"] = new[] { "Collide with other players' trucks", "Столкновения с чужими грузовиками", "Kollisionen mit fremden Lkw", "Collisions avec les camions des autres", "Colisiones con camiones ajenos", "Colisões com caminhões de outros", "Kolizje z cudzymi ciężarówkami", "Collisioni con i camion altrui", "与其他玩家的卡车碰撞", "與其他玩家的卡車碰撞" },
        ["overlay.set.collisions.hint"] = new[] { "Off by default: with any latency the other truck collides where it visually is not.", "По умолчанию выключены: из-за задержки чужой грузовик сталкивается там, где его визуально нет.", "Standardmäßig aus: Bei jeder Latenz kollidiert der fremde Lkw dort, wo er sichtbar nicht ist.", "Désactivé par défaut : avec la latence, l'autre camion percute là où il n'est pas visuellement.", "Desactivado por defecto: con latencia, el otro camión choca donde visualmente no está.", "Desligado por padrão: com latência, o outro caminhão colide onde visualmente não está.", "Domyślnie wyłączone: przy opóźnieniu cudza ciężarówka zderza się tam, gdzie jej wizualnie nie ma.", "Disattivato per default: con la latenza, l'altro camion urta dove visivamente non si trova.", "默认关闭：存在延迟时，对方卡车会在其显示位置之外发生碰撞。", "預設關閉：存在延遲時，對方卡車會在其顯示位置之外發生碰撞。" },
        ["overlay.set.ssl"] = new[] { "Do not verify the server certificate", "Не проверять сертификат сервера", "Serverzertifikat nicht prüfen", "Ne pas vérifier le certificat du serveur", "No verificar el certificado del servidor", "Não verificar o certificado do servidor", "Nie sprawdzaj certyfikatu serwera", "Non verificare il certificato del server", "不验证服务器证书", "不驗證伺服器憑證" },
        ["overlay.set.ssl.hint"] = new[] { "Needed for servers with a self-signed certificate — that is, nearly all of them.", "Нужно для серверов с самоподписанным сертификатом — то есть почти для всех.", "Nötig für Server mit selbstsigniertem Zertifikat — also fast alle.", "Nécessaire pour les serveurs à certificat autosigné — c'est-à-dire presque tous.", "Necesario para servidores con certificado autofirmado — es decir, casi todos.", "Necessário para servidores com certificado autoassinado — ou seja, quase todos.", "Potrzebne dla serwerów z certyfikatem samopodpisanym — czyli niemal wszystkich.", "Serve per i server con certificato autofirmato — cioè quasi tutti.", "自签名证书的服务器需要开启，几乎所有服务器都是如此。", "自簽憑證的伺服器需要開啟，幾乎所有伺服器都是如此。" },
        ["overlay.set.mic"] = new[] { "Microphone", "Микрофон", "Mikrofon", "Microphone", "Micrófono", "Microfone", "Mikrofon", "Microfono", "麦克风", "麥克風" },
        ["overlay.set.auto"] = new[] { "Automatic", "Автоматически", "Automatisch", "Automatique", "Automático", "Automático", "Automatycznie", "Automatico", "自动", "自動" },
        ["overlay.set.micgain"] = new[] { "Microphone volume", "Громкость микрофона", "Mikrofonlautstärke", "Volume du micro", "Volumen del micrófono", "Volume do microfone", "Głośność mikrofonu", "Volume microfono", "麦克风音量", "麥克風音量" },
        ["overlay.set.denoise"] = new[] { "Noise suppression", "Шумоподавление", "Rauschunterdrückung", "Réduction du bruit", "Supresión de ruido", "Supressão de ruído", "Redukcja szumów", "Riduzione del rumore", "噪声抑制", "噪音抑制" },
        ["overlay.set.radiovolume"] = new[] { "Radio volume", "Громкость рации", "Funklautstärke", "Volume de la radio", "Volumen de la radio", "Volume do rádio", "Głośność radia", "Volume radio", "无线电音量", "無線電音量" },
        ["overlay.set.effect"] = new[] { "Radio sound", "Звук рации", "Funkklang", "Son de la radio", "Sonido de radio", "Som de rádio", "Brzmienie radia", "Suono radio", "无线电音效", "無線電音效" },
        ["overlay.effect.off"] = new[] { "clean voice", "чистый голос", "klare Stimme", "voix claire", "voz limpia", "voz limpa", "czysty głos", "voce pulita", "原声", "原聲" },
        ["overlay.effect.light"] = new[] { "light", "лёгкий", "leicht", "léger", "ligero", "leve", "lekkie", "leggero", "轻微", "輕微" },
        ["overlay.effect.full"] = new[] { "CB radio", "как в рации", "CB-Funk", "radio CB", "radio CB", "rádio CB", "radio CB", "radio CB", "CB 电台", "CB 電台" },
        ["overlay.set.mutedialogue"] = new[] { "Mute players during a story call", "Не слышать игроков во время сюжетного диалога", "Spieler während eines Story-Anrufs stumm", "Couper les joueurs pendant un appel scénarisé", "Silenciar jugadores durante una llamada de historia", "Silenciar jogadores durante uma chamada da história", "Wycisz graczy podczas rozmowy fabularnej", "Silenzia i giocatori durante una chiamata di trama", "剧情通话期间静音其他玩家", "劇情通話期間靜音其他玩家" },
        ["overlay.set.trucks"] = new[] { "Hear radios in nearby trucks", "Слышать рации соседних грузовиков", "Funk aus nahen Lkw hören", "Entendre la radio des camions proches", "Oír la radio de camiones cercanos", "Ouvir o rádio de caminhões próximos", "Słyszeć radio z pobliskich ciężarówek", "Sentire la radio dei camion vicini", "听到附近卡车的电台", "聽到附近卡車的電台" },
        ["overlay.set.nopause"] = new[] { "No pause while on a server", "Без паузы на сервере", "Keine Pause auf einem Server", "Pas de pause sur un serveur", "Sin pausa en un servidor", "Sem pausa em um servidor", "Bez pauzy na serwerze", "Nessuna pausa su un server", "在服务器上不暂停", "在伺服器上不暫停" },
        ["overlay.set.nopause.hint"] = new[] { "Menus, the map and alt-tab no longer freeze the world; a frozen player stands still for everyone else and then leaps.", "Меню, карта и alt-tab больше не замораживают мир: замороженный игрок стоит на месте для остальных, а потом прыгает.", "Menüs, Karte und Alt-Tab frieren die Welt nicht mehr ein; ein eingefrorener Spieler steht für alle anderen still und springt dann.", "Les menus, la carte et alt-tab ne figent plus le monde ; un joueur figé reste immobile pour les autres puis saute.", "Los menús, el mapa y alt-tab ya no congelan el mundo; un jugador congelado se queda quieto para los demás y luego salta.", "Menus, mapa e alt-tab não congelam mais o mundo; um jogador congelado fica parado para os outros e depois salta.", "Menu, mapa i alt-tab nie zatrzymują już świata; zatrzymany gracz stoi w miejscu dla innych, a potem skacze.", "Menu, mappa e alt-tab non bloccano più il mondo; un giocatore bloccato resta fermo per gli altri e poi salta.", "菜单、地图和切换窗口不再冻结世界；被冻结的玩家在他人看来会停在原地然后跳变。", "選單、地圖和切換視窗不再凍結世界；被凍結的玩家在他人看來會停在原地然後跳變。" },
        ["overlay.set.updates"] = new[] { "Check for updates at startup", "Проверять обновления при запуске", "Beim Start nach Updates suchen", "Vérifier les mises à jour au démarrage", "Buscar actualizaciones al iniciar", "Verificar atualizações ao iniciar", "Sprawdzaj aktualizacje przy starcie", "Controlla aggiornamenti all'avvio", "启动时检查更新", "啟動時檢查更新" },
        ["overlay.update"] = new[] { "Version {0} is available on GitHub", "На GitHub доступна версия {0}", "Version {0} ist auf GitHub verfügbar", "La version {0} est disponible sur GitHub", "La versión {0} está disponible en GitHub", "A versão {0} está disponível no GitHub", "Wersja {0} jest dostępna na GitHubie", "La versione {0} è disponibile su GitHub", "GitHub 上有新版本 {0}", "GitHub 上有新版本 {0}" },
        ["overlay.footer"] = new[] { "F2 — open and close this menu, Esc — close", "F2 — открыть и закрыть это меню, Esc — закрыть", "F2 — dieses Menü öffnen und schließen, Esc — schließen", "F2 — ouvrir et fermer ce menu, Échap — fermer", "F2 — abrir y cerrar este menú, Esc — cerrar", "F2 — abrir e fechar este menu, Esc — fechar", "F2 — otwórz i zamknij to menu, Esc — zamknij", "F2 — apri e chiudi questo menu, Esc — chiudi", "F2 — 打开或关闭此菜单，Esc — 关闭", "F2 — 開啟或關閉此選單，Esc — 關閉" },
        ["overlay.players.onserver"] = new[] { "on server", "на сервере", "auf dem Server", "sur le serveur", "en el servidor", "no servidor", "na serwerze", "sul server", "在线", "線上" },
        ["overlay.players.insector"] = new[] { "{0} in your sector", "{0} в вашем секторе", "{0} in Ihrem Sektor", "{0} dans votre secteur", "{0} en tu sector", "{0} no seu setor", "{0} w twoim sektorze", "{0} nel tuo settore", "{0} 位在你的星区", "{0} 位在你的星區" },
        ["overlay.players.unknown"] = new[] { "unknown", "неизвестно", "unbekannt", "inconnu", "desconocido", "desconhecido", "nieznany", "sconosciuto", "未知", "未知" },
    };
}

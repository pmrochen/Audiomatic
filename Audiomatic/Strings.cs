namespace Audiomatic;

/// <summary>
/// Simple i18n system — call Strings.T("key") to get the current-language translation.
/// </summary>
public static class Strings
{
    private static string _lang = "en";

    public static string Language
    {
        get => _lang;
        set => _lang = value ?? "en";
    }

    /// <summary>Translate a key to the current language. Returns the key itself if not found.</summary>
    public static string T(string key)
    {
        if (Translations.TryGetValue(key, out var map) && map.TryGetValue(_lang, out var val))
            return val;
        // Fallback to English
        if (Translations.TryGetValue(key, out map) && map.TryGetValue("en", out val))
            return val;
        return key;
    }

    /// <summary>Translate with format arguments: Strings.T("key", arg1, arg2)</summary>
    public static string T(string key, params object[] args)
    {
        var template = T(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

	// ── All translations ────────────────────────────────────────
	// Key → { "en" → English, "fr" → French, "pl" → Polish }

	private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        // ── Navigation ──────────────────────────────────────────
        ["Library"] = new() { ["en"] = "Library", ["fr"] = "Bibliothèque", ["pl"] = "Biblioteka" },
        ["Playlists"] = new() { ["en"] = "Playlists", ["fr"] = "Playlists", ["pl"] = "Listy odtwarzania" },
        ["Queue"] = new() { ["en"] = "Queue", ["fr"] = "File d'attente", ["pl"] = "Kolejka" },
        ["Radio"] = new() { ["en"] = "Radio", ["fr"] = "Radio", ["pl"] = "Radio" },
        ["Podcasts"] = new() { ["en"] = "Podcasts", ["fr"] = "Podcasts", ["pl"] = "Podcasty" },
        ["Albums"] = new() { ["en"] = "Albums", ["fr"] = "Albums", ["pl"] = "Albumy" },
        ["Artists"] = new() { ["en"] = "Artists", ["fr"] = "Artistes", ["pl"] = "Artyści" },
        ["Visualizer"] = new() { ["en"] = "Visualizer", ["fr"] = "Visualiseur", ["pl"] = "Analizator widma" },
        ["Equalizer"] = new() { ["en"] = "Equalizer", ["fr"] = "Égaliseur", ["pl"] = "Equalizer" },
        ["Media"] = new() { ["en"] = "Media", ["fr"] = "Média", ["pl"] = "Media" },

        // ── Player ──────────────────────────────────────────────
        ["No track"] = new() { ["en"] = "No track", ["fr"] = "Aucune piste", ["pl"] = "Brak ścieżki" },
        ["LIVE"] = new() { ["en"] = "LIVE", ["fr"] = "EN DIRECT", ["pl"] = "NA ŻYWO" },
        ["No track playing"] = new() { ["en"] = "No track playing", ["fr"] = "Aucune piste en lecture", ["pl"] = "Nic nie jest odtwarzane" },

        // ── Tooltips ────────────────────────────────────────────
        ["Compact (Ctrl+L)"] = new() { ["en"] = "Compact (Ctrl+L)", ["fr"] = "Compact (Ctrl+L)", ["pl"] = "Kompaktowy (Ctrl+L)" },
        ["Pin on top"] = new() { ["en"] = "Pin on top", ["fr"] = "Épingler au premier plan", ["pl"] = "Przypnij na pierwszym planie" },
        ["Shuffle"] = new() { ["en"] = "Shuffle", ["fr"] = "Aléatoire", ["pl"] = "Losowo" },
        ["Previous"] = new() { ["en"] = "Previous", ["fr"] = "Précédent", ["pl"] = "Poprzedni" },
        ["Next"] = new() { ["en"] = "Next", ["fr"] = "Suivant", ["pl"] = "Następny" },
        ["Repeat"] = new() { ["en"] = "Repeat", ["fr"] = "Répéter", ["pl"] = "Powtarzaj" },
        ["Playback Speed"] = new() { ["en"] = "Playback Speed", ["fr"] = "Vitesse de lecture", ["pl"] = "Szybkość odtwarzania" },
        ["Back to playlists"] = new() { ["en"] = "Back to playlists", ["fr"] = "Retour aux playlists", ["pl"] = "Powrót do list odtwarzania" },
        ["Back to albums"] = new() { ["en"] = "Back to albums", ["fr"] = "Retour aux albums", ["pl"] = "Powrót do albumów" },
        ["Back to artists"] = new() { ["en"] = "Back to artists", ["fr"] = "Retour aux artistes", ["pl"] = "Powrót do listy artystów" },
        ["New Playlist"] = new() { ["en"] = "New Playlist", ["fr"] = "Nouvelle playlist", ["pl"] = "Nowa lista odtwarzania" },
        ["Clear Queue"] = new() { ["en"] = "Clear Queue", ["fr"] = "Vider la file", ["pl"] = "Wyczyść kolejkę" },
        ["Play stream"] = new() { ["en"] = "Play stream", ["fr"] = "Lire le flux", ["pl"] = "Odtwórz strumień" },
        ["Back"] = new() { ["en"] = "Back", ["fr"] = "Retour", ["pl"] = "Powrót" },
        ["Settings"] = new() { ["en"] = "Settings", ["fr"] = "Paramètres", ["pl"] = "Ustawienia" },

        // ── Search & Sort ───────────────────────────────────────
        ["Search"] = new() { ["en"] = "Search", ["fr"] = "Rechercher", ["pl"] = "Szukaj" },
        ["Title"] = new() { ["en"] = "Title", ["fr"] = "Titre", ["pl"] = "Tytuł" },
        ["Artist"] = new() { ["en"] = "Artist", ["fr"] = "Artiste", ["pl"] = "Artysta" },
        ["Album"] = new() { ["en"] = "Album", ["fr"] = "Album", ["pl"] = "Album" },
        ["Duration"] = new() { ["en"] = "Duration", ["fr"] = "Durée", ["pl"] = "Czas trwania" },
        ["BPM"] = new() { ["en"] = "BPM", ["fr"] = "BPM", ["pl"] = "BPM" },
        ["Ascending"] = new() { ["en"] = "Ascending", ["fr"] = "Croissant", ["pl"] = "Rosnąco" },
        ["Folder"] = new() { ["en"] = "Folder", ["fr"] = "Dossier", ["pl"] = "Folder" },

        // ── Radio ───────────────────────────────────────────────
        ["Radio Stream"] = new() { ["en"] = "Radio Stream", ["fr"] = "Flux radio", ["pl"] = "Strumień radiowy" },
        ["Enter stream URL (e.g. http://...)"] = new() { ["en"] = "Enter stream URL (e.g. http://...)", ["fr"] = "Entrer l'URL du flux (ex: http://...)", ["pl"] = "Wprowadź adres URL strumienia (np. http://...)" },
        ["Recent stations"] = new() { ["en"] = "Recent stations", ["fr"] = "Stations récentes", ["pl"] = "Ostatnie stacje" },
        ["Search podcasts..."] = new() { ["en"] = "Search podcasts...", ["fr"] = "Rechercher des podcasts...", ["pl"] = "Wyszukaj podcasty..." },
        ["Invalid URL. Please enter a valid http/https stream URL."] = new() { ["en"] = "Invalid URL. Please enter a valid http/https stream URL.", ["fr"] = "URL invalide. Veuillez entrer une URL de flux http/https valide.", ["pl"] = "Nieprawidłowy adres URL. Proszę wprowadzić poprawny adres strumienia http/https." },
        ["Connecting..."] = new() { ["en"] = "Connecting...", ["fr"] = "Connexion...", ["pl"] = "Łączenie..." },
        ["Playing: {0}"] = new() { ["en"] = "Playing: {0}", ["fr"] = "Lecture : {0}", ["pl"] = "Odtwarzanie: {0}" },

        // ── Speed ───────────────────────────────────────────────
        ["Speed"] = new() { ["en"] = "Speed", ["fr"] = "Vitesse", ["pl"] = "Szybkość" },
        ["Normal"] = new() { ["en"] = "Normal", ["fr"] = "Normal", ["pl"] = "Normalna" },

        // ── Context menu items ──────────────────────────────────
        ["Play"] = new() { ["en"] = "Play", ["fr"] = "Lire", ["pl"] = "Odtwarzaj" },
        ["Play Next"] = new() { ["en"] = "Play Next", ["fr"] = "Lire ensuite", ["pl"] = "Odtwarzaj jako następny" },
        ["Add to Queue"] = new() { ["en"] = "Add to Queue", ["fr"] = "Ajouter à la file", ["pl"] = "Dodaj do kolejki" },
        ["Add to Playlist"] = new() { ["en"] = "Add to Playlist", ["fr"] = "Ajouter à la playlist", ["pl"] = "Dodaj do listy odtwarzania" },
        ["New Playlist..."] = new() { ["en"] = "New Playlist...", ["fr"] = "Nouvelle playlist...", ["pl"] = "Nowa lista odtwarzania..." },
        ["Detect BPM"] = new() { ["en"] = "Detect BPM", ["fr"] = "Détecter le BPM", ["pl"] = "Wykryj BPM" },
        ["{0} BPM"] = new() { ["en"] = "{0} BPM", ["fr"] = "{0} BPM", ["pl"] = "{0} BPM" },
        ["Edit Tags"] = new() { ["en"] = "Edit Tags", ["fr"] = "Modifier les tags", ["pl"] = "Edytuj znaczniki" },
        ["Remove from Playlist"] = new() { ["en"] = "Remove from Playlist", ["fr"] = "Retirer de la playlist", ["pl"] = "Usuń z listy odtwarzania" },
        ["Rename"] = new() { ["en"] = "Rename", ["fr"] = "Renommer", ["pl"] = "Zmień nazwę" },
        ["Delete"] = new() { ["en"] = "Delete", ["fr"] = "Supprimer", ["pl"] = "Skasuj" },
        ["Move Up"] = new() { ["en"] = "Move Up", ["fr"] = "Monter", ["pl"] = "Przenieś w górę" },
        ["Move Down"] = new() { ["en"] = "Move Down", ["fr"] = "Descendre", ["pl"] = "Przenieś w dół" },
        ["Remove"] = new() { ["en"] = "Remove", ["fr"] = "Retirer", ["pl"] = "Usuń" },
        ["Expand"] = new() { ["en"] = "Expand", ["fr"] = "Agrandir", ["pl"] = "Rozszerz" },

        // ── Radio station context ───────────────────────────────
        ["Rename Station"] = new() { ["en"] = "Rename Station", ["fr"] = "Renommer la station", ["pl"] = "Zmień nazwę stacji" },
        ["Confirm"] = new() { ["en"] = "Confirm", ["fr"] = "Confirmer", ["pl"] = "Potwierdź" },

        // ── Podcast ─────────────────────────────────────────────
        ["Episodes"] = new() { ["en"] = "Episodes", ["fr"] = "Épisodes", ["pl"] = "Odcinki" },
        ["Subscribe"] = new() { ["en"] = "Subscribe", ["fr"] = "S'abonner", ["pl"] = "Subskrybuj" },
        ["Subscribed"] = new() { ["en"] = "Subscribed", ["fr"] = "Abonné", ["pl"] = "Subskrybowane" },
        ["Unsubscribe"] = new() { ["en"] = "Unsubscribe", ["fr"] = "Se désabonner", ["pl"] = "Odsubskrybuj" },
        ["Download"] = new() { ["en"] = "Download", ["fr"] = "Télécharger", ["pl"] = "Pobierz" },
        ["Cancel Download"] = new() { ["en"] = "Cancel Download", ["fr"] = "Annuler le téléchargement", ["pl"] = "Anuluj pobieranie" },
        ["Delete Download"] = new() { ["en"] = "Delete Download", ["fr"] = "Supprimer le téléchargement", ["pl"] = "Skasuj pobrane" },
        ["Mark as read"] = new() { ["en"] = "Mark as read", ["fr"] = "Marquer comme lu", ["pl"] = "Oznacz jako przeczytane" },
        ["Mark as unread"] = new() { ["en"] = "Mark as unread", ["fr"] = "Marquer comme non lu", ["pl"] = "Oznacz jako nieprzeczytane" },
        ["Downloading..."] = new() { ["en"] = "Downloading...", ["fr"] = "Téléchargement...", ["pl"] = "Pobieranie..." },
        ["Played"] = new() { ["en"] = "Played", ["fr"] = "Lu", ["pl"] = "Odtworzone" },
        ["Loading episodes..."] = new() { ["en"] = "Loading episodes...", ["fr"] = "Chargement des épisodes...", ["pl"] = "Ładowanie odcinków..." },
        ["No episodes found."] = new() { ["en"] = "No episodes found.", ["fr"] = "Aucun épisode trouvé.", ["pl"] = "Brak odcinków." },
        ["Error loading episodes: {0}"] = new() { ["en"] = "Error loading episodes: {0}", ["fr"] = "Erreur de chargement des épisodes : {0}", ["pl"] = "Błąd ładowania odcinków: {0}" },
        ["Searching..."] = new() { ["en"] = "Searching...", ["fr"] = "Recherche...", ["pl"] = "Wyszukiwanie..." },
        ["No podcasts found."] = new() { ["en"] = "No podcasts found.", ["fr"] = "Aucun podcast trouvé.", ["pl"] = "Podcasty nie odnalezione." },
        ["Download failed: {0}"] = new() { ["en"] = "Download failed: {0}", ["fr"] = "Échec du téléchargement : {0}", ["pl"] = "Pobieranie nieudane: {0}" },

        // ── Dialogs ─────────────────────────────────────────────
        ["Create"] = new() { ["en"] = "Create", ["fr"] = "Créer", ["pl"] = "Utwórz" },
        ["Cancel"] = new() { ["en"] = "Cancel", ["fr"] = "Annuler", ["pl"] = "Anuluj" },
        ["Playlist name"] = new() { ["en"] = "Playlist name", ["fr"] = "Nom de la playlist", ["pl"] = "Nazwa listy odtwarzania" },
        ["Rename Playlist"] = new() { ["en"] = "Rename Playlist", ["fr"] = "Renommer la playlist", ["pl"] = "Zmień nazwę listy odtwarzania" },

        // ── Metadata editor ─────────────────────────────────────
        ["Change"] = new() { ["en"] = "Change", ["fr"] = "Changer", ["pl"] = "Zmień" },
        ["Save"] = new() { ["en"] = "Save", ["fr"] = "Enregistrer", ["pl"] = "Zapisz" },
        ["Choose color"] = new() { ["en"] = "Choose color", ["fr"] = "Choisir la couleur", ["pl"] = "Wybierz kolor" },

        // ── Settings flyout ─────────────────────────────────────
        ["Actions"] = new() { ["en"] = "Actions", ["fr"] = "Actions", ["pl"] = "Akcje" },
        ["Add Folder"] = new() { ["en"] = "Add Folder", ["fr"] = "Ajouter un dossier", ["pl"] = "Dodaj folder" },
        ["Scan Library"] = new() { ["en"] = "Scan Library", ["fr"] = "Scanner la bibliothèque", ["pl"] = "Skanuj bibliotekę" },
        ["Reset Library"] = new() { ["en"] = "Reset Library", ["fr"] = "Réinitialiser la bibliothèque", ["pl"] = "Ponownie inicjalizuj bibliotekę" },
        ["Backdrop"] = new() { ["en"] = "Backdrop", ["fr"] = "Arrière-plan", ["pl"] = "Tło" },
        ["Acrylic"] = new() { ["en"] = "Acrylic", ["fr"] = "Acrylique", ["pl"] = "Akryl" },
        ["Custom Acrylic"] = new() { ["en"] = "Custom Acrylic", ["fr"] = "Acrylique personnalisé", ["pl"] = "Spersonalizowany akryl" },
        ["Mica"] = new() { ["en"] = "Mica", ["fr"] = "Mica", ["pl"] = "Mika" },
        ["Mica Alt"] = new() { ["en"] = "Mica Alt", ["fr"] = "Mica Alt", ["pl"] = "Mocniejsza mika" },
        ["None"] = new() { ["en"] = "None", ["fr"] = "Aucun", ["pl"] = "Brak" },
        ["Theme"] = new() { ["en"] = "Theme", ["fr"] = "Thème", ["pl"] = "Motyw" },
        ["System"] = new() { ["en"] = "System", ["fr"] = "Système", ["pl"] = "Systemowy" },
        ["Light"] = new() { ["en"] = "Light", ["fr"] = "Clair", ["pl"] = "Jasny" },
        ["Dark"] = new() { ["en"] = "Dark", ["fr"] = "Sombre", ["pl"] = "Ciemny" },
        ["Accent Color"] = new() { ["en"] = "Accent Color", ["fr"] = "Couleur d'accentuation", ["pl"] = "Kolor akcentu" },
        ["Choose Accent..."] = new() { ["en"] = "Choose Accent...", ["fr"] = "Choisir l'accent...", ["pl"] = "Wybierz akcent..." },
        ["30 FPS"] = new() { ["en"] = "30 FPS", ["fr"] = "30 IPS", ["pl"] = "30 FPS" },
        ["60 FPS"] = new() { ["en"] = "60 FPS", ["fr"] = "60 IPS", ["pl"] = "60 FPS" },
        ["Compact Mode"] = new() { ["en"] = "Compact Mode", ["fr"] = "Mode compact", ["pl"] = "Tryb kompaktowy" },
        ["Mini Player"] = new() { ["en"] = "Mini Player", ["fr"] = "Mini lecteur", ["pl"] = "Mini odtwarzacz" },
        ["Unpin from Top"] = new() { ["en"] = "Unpin from Top", ["fr"] = "Détacher du premier plan", ["pl"] = "Odepnij z pierwszego planu" },
        ["Pin on Top"] = new() { ["en"] = "Pin on Top", ["fr"] = "Épingler au premier plan", ["pl"] = "Przypnij na pierwszym planie" },
        ["Sleep Timer"] = new() { ["en"] = "Sleep Timer", ["fr"] = "Minuterie de veille", ["pl"] = "Zegar uśpienia" },
        ["Sleep ({0} min)"] = new() { ["en"] = "Sleep ({0} min)", ["fr"] = "Veille ({0} min)", ["pl"] = "Uśpienie ({0} min)" },
        ["Quit"] = new() { ["en"] = "Quit", ["fr"] = "Quitter", ["pl"] = "Wyjście" },

        // ── Language setting ────────────────────────────────────
        ["Language"] = new() { ["en"] = "Language", ["fr"] = "Langue", ["pl"] = "Język" },
        ["English"] = new() { ["en"] = "English", ["fr"] = "Anglais", ["pl"] = "Angielski" },
        ["French"] = new() { ["en"] = "French", ["fr"] = "Français", ["pl"] = "Francuski" },
		["Polish"] = new() { ["en"] = "Polish", ["fr"] = "Polonais", ["pl"] = "Polski" },

		// ── Custom Acrylic settings ─────────────────────────────
		["Tint Opacity"] = new() { ["en"] = "Tint Opacity", ["fr"] = "Opacité de teinte", ["pl"] = "Krycie odcienia" },
        ["Luminosity"] = new() { ["en"] = "Luminosity", ["fr"] = "Luminosité", ["pl"] = "Jasność" },
        ["Tint"] = new() { ["en"] = "Tint", ["fr"] = "Teinte", ["pl"] = "Odcień" },
        ["Fallback"] = new() { ["en"] = "Fallback", ["fr"] = "Repli", ["pl"] = "Zapasowy" },
        ["Style"] = new() { ["en"] = "Style", ["fr"] = "Style", ["pl"] = "Styl" },

        // ── Equalizer ───────────────────────────────────────────
        ["Preamp"] = new() { ["en"] = "Preamp", ["fr"] = "Préampli", ["pl"] = "Preamp" },
        ["Reset"] = new() { ["en"] = "Reset", ["fr"] = "Réinitialiser", ["pl"] = "Przywróć" },

        // ── Status messages ─────────────────────────────────────
        ["Scanning..."] = new() { ["en"] = "Scanning...", ["fr"] = "Analyse...", ["pl"] = "Skanowanie..." },
        ["{0} tracks"] = new() { ["en"] = "{0} tracks", ["fr"] = "{0} pistes", ["pl"] = "{0} utworów" },
        ["{0} albums"] = new() { ["en"] = "{0} albums", ["fr"] = "{0} albums", ["pl"] = "{0} albumów" },
        ["{0} artists"] = new() { ["en"] = "{0} artists", ["fr"] = "{0} artistes", ["pl"] = "{0} artystów" },
        ["{0} in queue"] = new() { ["en"] = "{0} in queue", ["fr"] = "{0} dans la file", ["pl"] = "{0} w kolejce" },
        ["{0} playlists"] = new() { ["en"] = "{0} playlists", ["fr"] = "{0} playlists", ["pl"] = "{0} list odtwarzania" },
        ["{0} playlist"] = new() { ["en"] = "{0} playlist", ["fr"] = "{0} playlist", ["pl"] = "{0} lista odtwarzania" },
        ["{0} sessions"] = new() { ["en"] = "{0} sessions", ["fr"] = "{0} sessions", ["pl"] = "{0} sesji" },
        ["{0} session"] = new() { ["en"] = "{0} session", ["fr"] = "{0} session", ["pl"] = "{0} sesja" },
        ["Error: {0}"] = new() { ["en"] = "Error: {0}", ["fr"] = "Erreur : {0}", ["pl"] = "Błąd: {0}" },
        ["0 tracks"] = new() { ["en"] = "0 tracks", ["fr"] = "0 pistes", ["pl"] = "0 utworów" },

        // ── Bottom bar ──────────────────────────────────────────
        ["Choose Folder"] = new() { ["en"] = "Choose Folder", ["fr"] = "Choisir un dossier", ["pl"] = "Wybierz folder" },

        // ── Drag & drop ─────────────────────────────────────────
        ["Add to Library"] = new() { ["en"] = "Add to Library", ["fr"] = "Ajouter à la bibliothèque", ["pl"] = "Dodaj do biblioteki" },

        // ── Media control ───────────────────────────────────────
        ["No media playing"] = new() { ["en"] = "No media playing", ["fr"] = "Aucun média en lecture", ["pl"] = "Nic nie jest odtwarzane" },
        ["Play music or a video to control it here"] = new() { ["en"] = "Play music or a video to control it here", ["fr"] = "Lancez de la musique ou une vidéo pour la contrôler ici", ["pl"] = "Rozpocznij odtwarzanie muzyki lub filmu aby tutaj sterować" },

        // ── Sleep timer details ─────────────────────────────────
        ["{0} min remaining"] = new() { ["en"] = "{0} min remaining", ["fr"] = "{0} min restantes", ["pl"] = "Pozostało {0} min" },
        ["{0}s remaining"] = new() { ["en"] = "{0}s remaining", ["fr"] = "{0}s restantes", ["pl"] = "Pozostało {0}s" },
        ["Cancel Timer"] = new() { ["en"] = "Cancel Timer", ["fr"] = "Annuler la minuterie", ["pl"] = "Wyłącz zegar" },
        ["{0} min"] = new() { ["en"] = "{0} min", ["fr"] = "{0} min", ["pl"] = "{0} min" },
        ["{0}h {1}min"] = new() { ["en"] = "{0}h {1}min", ["fr"] = "{0}h {1}min", ["pl"] = "{0}h {1}min" },
        ["{0}h"] = new() { ["en"] = "{0}h", ["fr"] = "{0}h", ["pl"] = "{0}h" },

        // ── Settings window ─────────────────────────────────────
        ["Folders"] = new() { ["en"] = "Folders", ["fr"] = "Dossiers", ["pl"] = "Foldery" },
        ["Appearance"] = new() { ["en"] = "Appearance", ["fr"] = "Apparence", ["pl"] = "Wygląd" },
        ["Music Folders"] = new() { ["en"] = "Music Folders", ["fr"] = "Dossiers musicaux", ["pl"] = "Foldery muzyczne" },
        ["Add folders containing your music files. They will be scanned for audio tracks."] = new() { ["en"] = "Add folders containing your music files. They will be scanned for audio tracks.", ["fr"] = "Ajoutez des dossiers contenant vos fichiers musicaux. Ils seront analysés pour trouver des pistes audio.", ["pl"] = "Dodaj foldery zawierające pliki muzyczne. Zostaną przeskanowane w poszukiwaniu ścieżek audio." },
        ["Scan Now"] = new() { ["en"] = "Scan Now", ["fr"] = "Scanner maintenant", ["pl"] = "Skanuj teraz" },
        ["No folders added yet."] = new() { ["en"] = "No folders added yet.", ["fr"] = "Aucun dossier ajouté.", ["pl"] = "Foldery nie zostały jeszcze dodane." },
        ["Choose the color theme for the application."] = new() { ["en"] = "Choose the color theme for the application.", ["fr"] = "Choisissez le thème de couleur de l'application.", ["pl"] = "Wybierz motyw kolorystyczny aplikacji." },
        ["Backdrop Effect"] = new() { ["en"] = "Backdrop Effect", ["fr"] = "Effet d'arrière-plan", ["pl"] = "Efekt tła" },
        ["Choose the visual effect applied to the window background."] = new() { ["en"] = "Choose the visual effect applied to the window background.", ["fr"] = "Choisissez l'effet visuel appliqué à l'arrière-plan de la fenêtre.", ["pl"] = "Wybierz efekt stosowany do tła okna." },
        ["Frame rate for the audio visualizer."] = new() { ["en"] = "Frame rate for the audio visualizer.", ["fr"] = "Fréquence d'images pour le visualiseur audio.", ["pl"] = "Częstotliwość odświeżania analizatora widma" },
        ["No tracks in library."] = new() { ["en"] = "No tracks in library.", ["fr"] = "Aucune piste dans la bibliothèque.", ["pl"] = "Brak utworów w bibliotece." },

        // ── Scan status messages ────────────────────────────────
        ["Scanned {0}/{1} files..."] = new() { ["en"] = "Scanned {0}/{1} files...", ["fr"] = "Analysé {0}/{1} fichiers...", ["pl"] = "Skanowanie plików ({0}/{1})..." },
        ["Done. {0} tracks added."] = new() { ["en"] = "Done. {0} tracks added.", ["fr"] = "Terminé. {0} pistes ajoutées.", ["pl"] = "Gotowe. Dodano {0} utworów." },
        ["Scan cancelled."] = new() { ["en"] = "Scan cancelled.", ["fr"] = "Analyse annulée.", ["pl"] = "Skanowanie anulowane." },
        ["Done. {0} new tracks found."] = new() { ["en"] = "Done. {0} new tracks found.", ["fr"] = "Terminé. {0} nouvelles pistes trouvées.", ["pl"] = "Gotowe. Znaleziono {0} nowych utworów." },
        ["Scanning all folders..."] = new() { ["en"] = "Scanning all folders...", ["fr"] = "Analyse de tous les dossiers...", ["pl"] = "Skanowanie wszystkich folderów..." },
        ["Unknown folder ({0})"] = new() { ["en"] = "Unknown folder ({0})", ["fr"] = "Dossier inconnu ({0})", ["pl"] = "Nieznany folder ({0})" },

        // ── Overlay widget ─────────────────────────────────────
        ["Overlay Widget"] = new() { ["en"] = "Overlay Widget", ["fr"] = "Widget flottant" },
        ["Show Overlay"] = new() { ["en"] = "Show Overlay", ["fr"] = "Afficher le widget" },
        ["Hide Overlay"] = new() { ["en"] = "Hide Overlay", ["fr"] = "Masquer le widget" },

        // ── Tray ────────────────────────────────────────────────
        ["Show\tCtrl+Alt+M"] = new() { ["en"] = "Show\tCtrl+Alt+M", ["fr"] = "Afficher\tCtrl+Alt+M", ["pl"] = "Pokaż\tCtrl+Alt+M" },
    };
}

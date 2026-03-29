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
    // Key → { "en" → English, "fr" → French }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        // ── Navigation ──────────────────────────────────────────
        ["Library"] = new() { ["en"] = "Library", ["fr"] = "Bibliothèque" },
        ["Playlists"] = new() { ["en"] = "Playlists", ["fr"] = "Playlists" },
        ["Queue"] = new() { ["en"] = "Queue", ["fr"] = "File d'attente" },
        ["Radio"] = new() { ["en"] = "Radio", ["fr"] = "Radio" },
        ["Podcasts"] = new() { ["en"] = "Podcasts", ["fr"] = "Podcasts" },
        ["Albums"] = new() { ["en"] = "Albums", ["fr"] = "Albums" },
        ["Artists"] = new() { ["en"] = "Artists", ["fr"] = "Artistes" },
        ["Visualizer"] = new() { ["en"] = "Visualizer", ["fr"] = "Visualiseur" },
        ["Equalizer"] = new() { ["en"] = "Equalizer", ["fr"] = "Égaliseur" },
        ["Media"] = new() { ["en"] = "Media", ["fr"] = "Média" },
        ["Tabs"] = new() { ["en"] = "Tabs", ["fr"] = "Onglets" },
        ["Stats"] = new() { ["en"] = "Stats", ["fr"] = "Stats" },

        // ── Stats ──────────────────────────────────────────────
        ["Total Plays"] = new() { ["en"] = "Total Plays", ["fr"] = "Écoutes" },
        ["Listening Time"] = new() { ["en"] = "Listening Time", ["fr"] = "Temps d'écoute" },
        ["{0}h {1}m"] = new() { ["en"] = "{0}h {1}m", ["fr"] = "{0}h {1}m" },
        ["{0} min"] = new() { ["en"] = "{0} min", ["fr"] = "{0} min" },
        ["Top Tracks"] = new() { ["en"] = "Top Tracks", ["fr"] = "Pistes les plus écoutées" },
        ["Top Artists"] = new() { ["en"] = "Top Artists", ["fr"] = "Artistes favoris" },
        ["{0} plays"] = new() { ["en"] = "{0} plays", ["fr"] = "{0} écoutes" },
        ["No listening history yet"] = new() { ["en"] = "No listening history yet", ["fr"] = "Aucun historique d'écoute" },

        // ── Player ──────────────────────────────────────────────
        ["No track"] = new() { ["en"] = "No track", ["fr"] = "Aucune piste" },
        ["LIVE"] = new() { ["en"] = "LIVE", ["fr"] = "EN DIRECT" },
        ["No track playing"] = new() { ["en"] = "No track playing", ["fr"] = "Aucune piste en lecture" },

        // ── Tooltips ────────────────────────────────────────────
        ["Compact (Ctrl+L)"] = new() { ["en"] = "Compact (Ctrl+L)", ["fr"] = "Compact (Ctrl+L)" },
        ["Pin on top"] = new() { ["en"] = "Pin on top", ["fr"] = "Épingler au premier plan" },
        ["Shuffle"] = new() { ["en"] = "Shuffle", ["fr"] = "Aléatoire" },
        ["Previous"] = new() { ["en"] = "Previous", ["fr"] = "Précédent" },
        ["Next"] = new() { ["en"] = "Next", ["fr"] = "Suivant" },
        ["Repeat"] = new() { ["en"] = "Repeat", ["fr"] = "Répéter" },
        ["Playback Speed"] = new() { ["en"] = "Playback Speed", ["fr"] = "Vitesse de lecture" },
        ["Back to playlists"] = new() { ["en"] = "Back to playlists", ["fr"] = "Retour aux playlists" },
        ["Back to albums"] = new() { ["en"] = "Back to albums", ["fr"] = "Retour aux albums" },
        ["Back to artists"] = new() { ["en"] = "Back to artists", ["fr"] = "Retour aux artistes" },
        ["New Playlist"] = new() { ["en"] = "New Playlist", ["fr"] = "Nouvelle playlist" },
        ["Clear Queue"] = new() { ["en"] = "Clear Queue", ["fr"] = "Vider la file" },
        ["Play stream"] = new() { ["en"] = "Play stream", ["fr"] = "Lire le flux" },
        ["Back"] = new() { ["en"] = "Back", ["fr"] = "Retour" },
        ["Settings"] = new() { ["en"] = "Settings", ["fr"] = "Paramètres" },

        // ── Search & Sort ───────────────────────────────────────
        ["Search"] = new() { ["en"] = "Search", ["fr"] = "Rechercher" },
        ["Title"] = new() { ["en"] = "Title", ["fr"] = "Titre" },
        ["Artist"] = new() { ["en"] = "Artist", ["fr"] = "Artiste" },
        ["Album"] = new() { ["en"] = "Album", ["fr"] = "Album" },
        ["Duration"] = new() { ["en"] = "Duration", ["fr"] = "Durée" },
        ["BPM"] = new() { ["en"] = "BPM", ["fr"] = "BPM" },
        ["Ascending"] = new() { ["en"] = "Ascending", ["fr"] = "Croissant" },
        ["Folder"] = new() { ["en"] = "Folder", ["fr"] = "Dossier" },

        // ── Radio ───────────────────────────────────────────────
        ["Radio Stream"] = new() { ["en"] = "Radio Stream", ["fr"] = "Flux radio" },
        ["Enter stream URL (e.g. http://...)"] = new() { ["en"] = "Enter stream URL (e.g. http://...)", ["fr"] = "Entrer l'URL du flux (ex: http://...)" },
        ["Recent stations"] = new() { ["en"] = "Recent stations", ["fr"] = "Stations récentes" },
        ["Search podcasts..."] = new() { ["en"] = "Search podcasts...", ["fr"] = "Rechercher des podcasts..." },
        ["Invalid URL. Please enter a valid http/https stream URL."] = new() { ["en"] = "Invalid URL. Please enter a valid http/https stream URL.", ["fr"] = "URL invalide. Veuillez entrer une URL de flux http/https valide." },
        ["Connecting..."] = new() { ["en"] = "Connecting...", ["fr"] = "Connexion..." },
        ["Playing: {0}"] = new() { ["en"] = "Playing: {0}", ["fr"] = "Lecture : {0}" },

        // ── Speed ───────────────────────────────────────────────
        ["Speed"] = new() { ["en"] = "Speed", ["fr"] = "Vitesse" },
        ["Normal"] = new() { ["en"] = "Normal", ["fr"] = "Normal" },

        // ── Context menu items ──────────────────────────────────
        ["Play"] = new() { ["en"] = "Play", ["fr"] = "Lire" },
        ["Play Next"] = new() { ["en"] = "Play Next", ["fr"] = "Lire ensuite" },
        ["Add to Queue"] = new() { ["en"] = "Add to Queue", ["fr"] = "Ajouter à la file" },
        ["Add to Playlist"] = new() { ["en"] = "Add to Playlist", ["fr"] = "Ajouter à la playlist" },
        ["New Playlist..."] = new() { ["en"] = "New Playlist...", ["fr"] = "Nouvelle playlist..." },
        ["Detect BPM"] = new() { ["en"] = "Detect BPM", ["fr"] = "Détecter le BPM" },
        ["{0} BPM"] = new() { ["en"] = "{0} BPM", ["fr"] = "{0} BPM" },
        ["Edit Tags"] = new() { ["en"] = "Edit Tags", ["fr"] = "Modifier les tags" },
        ["Remove from Playlist"] = new() { ["en"] = "Remove from Playlist", ["fr"] = "Retirer de la playlist" },
        ["Rename"] = new() { ["en"] = "Rename", ["fr"] = "Renommer" },
        ["Delete"] = new() { ["en"] = "Delete", ["fr"] = "Supprimer" },
        ["Move Up"] = new() { ["en"] = "Move Up", ["fr"] = "Monter" },
        ["Move Down"] = new() { ["en"] = "Move Down", ["fr"] = "Descendre" },
        ["Remove"] = new() { ["en"] = "Remove", ["fr"] = "Retirer" },
        ["Expand"] = new() { ["en"] = "Expand", ["fr"] = "Agrandir" },

        // ── Radio station context ───────────────────────────────
        ["Rename Station"] = new() { ["en"] = "Rename Station", ["fr"] = "Renommer la station" },
        ["Confirm"] = new() { ["en"] = "Confirm", ["fr"] = "Confirmer" },

        // ── Podcast ─────────────────────────────────────────────
        ["Episodes"] = new() { ["en"] = "Episodes", ["fr"] = "Épisodes" },
        ["Subscribe"] = new() { ["en"] = "Subscribe", ["fr"] = "S'abonner" },
        ["Subscribed"] = new() { ["en"] = "Subscribed", ["fr"] = "Abonné" },
        ["Unsubscribe"] = new() { ["en"] = "Unsubscribe", ["fr"] = "Se désabonner" },
        ["Download"] = new() { ["en"] = "Download", ["fr"] = "Télécharger" },
        ["Cancel Download"] = new() { ["en"] = "Cancel Download", ["fr"] = "Annuler le téléchargement" },
        ["Delete Download"] = new() { ["en"] = "Delete Download", ["fr"] = "Supprimer le téléchargement" },
        ["Mark as read"] = new() { ["en"] = "Mark as read", ["fr"] = "Marquer comme lu" },
        ["Mark as unread"] = new() { ["en"] = "Mark as unread", ["fr"] = "Marquer comme non lu" },
        ["Downloading..."] = new() { ["en"] = "Downloading...", ["fr"] = "Téléchargement..." },
        ["Played"] = new() { ["en"] = "Played", ["fr"] = "Lu" },
        ["Loading episodes..."] = new() { ["en"] = "Loading episodes...", ["fr"] = "Chargement des épisodes..." },
        ["No episodes found."] = new() { ["en"] = "No episodes found.", ["fr"] = "Aucun épisode trouvé." },
        ["Error loading episodes: {0}"] = new() { ["en"] = "Error loading episodes: {0}", ["fr"] = "Erreur de chargement des épisodes : {0}" },
        ["Searching..."] = new() { ["en"] = "Searching...", ["fr"] = "Recherche..." },
        ["No podcasts found."] = new() { ["en"] = "No podcasts found.", ["fr"] = "Aucun podcast trouvé." },
        ["Download failed: {0}"] = new() { ["en"] = "Download failed: {0}", ["fr"] = "Échec du téléchargement : {0}" },

        // ── Dialogs ─────────────────────────────────────────────
        ["Create"] = new() { ["en"] = "Create", ["fr"] = "Créer" },
        ["Cancel"] = new() { ["en"] = "Cancel", ["fr"] = "Annuler" },
        ["Playlist name"] = new() { ["en"] = "Playlist name", ["fr"] = "Nom de la playlist" },
        ["Rename Playlist"] = new() { ["en"] = "Rename Playlist", ["fr"] = "Renommer la playlist" },

        // ── Metadata editor ─────────────────────────────────────
        ["Change"] = new() { ["en"] = "Change", ["fr"] = "Changer" },
        ["Save"] = new() { ["en"] = "Save", ["fr"] = "Enregistrer" },
        ["Choose color"] = new() { ["en"] = "Choose color", ["fr"] = "Choisir la couleur" },

        // ── Settings flyout ─────────────────────────────────────
        ["Actions"] = new() { ["en"] = "Actions", ["fr"] = "Actions" },
        ["Add Folder"] = new() { ["en"] = "Add Folder", ["fr"] = "Ajouter un dossier" },
        ["Scan Library"] = new() { ["en"] = "Scan Library", ["fr"] = "Scanner la bibliothèque" },
        ["Import Playlist"] = new() { ["en"] = "Import Playlist", ["fr"] = "Importer une playlist" },
        ["Export Playlist"] = new() { ["en"] = "Export Playlist", ["fr"] = "Exporter une playlist" },
        ["Playlist imported: {0}/{1} tracks"] = new() { ["en"] = "Playlist imported: {0}/{1} tracks", ["fr"] = "Playlist importée : {0}/{1} pistes" },
        ["No playlist to export"] = new() { ["en"] = "No playlist to export", ["fr"] = "Aucune playlist à exporter" },
        ["Select a playlist to export"] = new() { ["en"] = "Select a playlist to export", ["fr"] = "Sélectionner une playlist à exporter" },
        ["Reset Library"] = new() { ["en"] = "Reset Library", ["fr"] = "Réinitialiser la bibliothèque" },
        ["About"] = new() { ["en"] = "About", ["fr"] = "À propos" },
        ["Developer"] = new() { ["en"] = "Developer", ["fr"] = "Développeur" },
        ["GitHub"] = new() { ["en"] = "GitHub", ["fr"] = "GitHub" },
        ["Buy Me a Coffee"] = new() { ["en"] = "Buy Me a Coffee", ["fr"] = "Buy Me a Coffee" },
        ["Backdrop"] = new() { ["en"] = "Backdrop", ["fr"] = "Arrière-plan" },
        ["Acrylic"] = new() { ["en"] = "Acrylic", ["fr"] = "Acrylique" },
        ["Custom Acrylic"] = new() { ["en"] = "Custom Acrylic", ["fr"] = "Acrylique personnalisé" },
        ["Mica"] = new() { ["en"] = "Mica", ["fr"] = "Mica" },
        ["Mica Alt"] = new() { ["en"] = "Mica Alt", ["fr"] = "Mica Alt" },
        ["None"] = new() { ["en"] = "None", ["fr"] = "Aucun" },
        ["Theme"] = new() { ["en"] = "Theme", ["fr"] = "Thème" },
        ["System"] = new() { ["en"] = "System", ["fr"] = "Système" },
        ["Light"] = new() { ["en"] = "Light", ["fr"] = "Clair" },
        ["Dark"] = new() { ["en"] = "Dark", ["fr"] = "Sombre" },
        ["Accent Color"] = new() { ["en"] = "Accent Color", ["fr"] = "Couleur d'accentuation" },
        ["Choose Accent..."] = new() { ["en"] = "Choose Accent...", ["fr"] = "Choisir l'accent..." },
        ["30 FPS"] = new() { ["en"] = "30 FPS", ["fr"] = "30 IPS" },
        ["60 FPS"] = new() { ["en"] = "60 FPS", ["fr"] = "60 IPS" },
        ["Compact Mode"] = new() { ["en"] = "Compact Mode", ["fr"] = "Mode compact" },
        ["Mini Player"] = new() { ["en"] = "Mini Player", ["fr"] = "Mini lecteur" },
        ["Unpin from Top"] = new() { ["en"] = "Unpin from Top", ["fr"] = "Détacher du premier plan" },
        ["Pin on Top"] = new() { ["en"] = "Pin on Top", ["fr"] = "Épingler au premier plan" },
        ["Sleep Timer"] = new() { ["en"] = "Sleep Timer", ["fr"] = "Minuterie de veille" },
        ["Sleep ({0} min)"] = new() { ["en"] = "Sleep ({0} min)", ["fr"] = "Veille ({0} min)" },
        ["Quit"] = new() { ["en"] = "Quit", ["fr"] = "Quitter" },

        // ── Language setting ────────────────────────────────────
        ["Language"] = new() { ["en"] = "Language", ["fr"] = "Langue" },
        ["English"] = new() { ["en"] = "English", ["fr"] = "Anglais" },
        ["French"] = new() { ["en"] = "French", ["fr"] = "Français" },

        // ── Custom Acrylic settings ─────────────────────────────
        ["Tint Opacity"] = new() { ["en"] = "Tint Opacity", ["fr"] = "Opacité de teinte" },
        ["Luminosity"] = new() { ["en"] = "Luminosity", ["fr"] = "Luminosité" },
        ["Tint"] = new() { ["en"] = "Tint", ["fr"] = "Teinte" },
        ["Fallback"] = new() { ["en"] = "Fallback", ["fr"] = "Repli" },
        ["Style"] = new() { ["en"] = "Style", ["fr"] = "Style" },

        // ── Equalizer ───────────────────────────────────────────
        ["Preamp"] = new() { ["en"] = "Preamp", ["fr"] = "Préampli" },
        ["Reset"] = new() { ["en"] = "Reset", ["fr"] = "Réinitialiser" },

        // ── Status messages ─────────────────────────────────────
        ["Scanning..."] = new() { ["en"] = "Scanning...", ["fr"] = "Analyse..." },
        ["{0} tracks"] = new() { ["en"] = "{0} tracks", ["fr"] = "{0} pistes" },
        ["{0} albums"] = new() { ["en"] = "{0} albums", ["fr"] = "{0} albums" },
        ["{0} artists"] = new() { ["en"] = "{0} artists", ["fr"] = "{0} artistes" },
        ["{0} in queue"] = new() { ["en"] = "{0} in queue", ["fr"] = "{0} dans la file" },
        ["{0} playlists"] = new() { ["en"] = "{0} playlists", ["fr"] = "{0} playlists" },
        ["{0} playlist"] = new() { ["en"] = "{0} playlist", ["fr"] = "{0} playlist" },
        ["{0} sessions"] = new() { ["en"] = "{0} sessions", ["fr"] = "{0} sessions" },
        ["{0} session"] = new() { ["en"] = "{0} session", ["fr"] = "{0} session" },
        ["Error: {0}"] = new() { ["en"] = "Error: {0}", ["fr"] = "Erreur : {0}" },
        ["0 tracks"] = new() { ["en"] = "0 tracks", ["fr"] = "0 pistes" },

        // ── Bottom bar ──────────────────────────────────────────
        ["Choose Folder"] = new() { ["en"] = "Choose Folder", ["fr"] = "Choisir un dossier" },

        // ── Drag & drop ─────────────────────────────────────────
        ["Add to Library"] = new() { ["en"] = "Add to Library", ["fr"] = "Ajouter à la bibliothèque" },

        // ── Media control ───────────────────────────────────────
        ["No media playing"] = new() { ["en"] = "No media playing", ["fr"] = "Aucun média en lecture" },
        ["Play music or a video to control it here"] = new() { ["en"] = "Play music or a video to control it here", ["fr"] = "Lancez de la musique ou une vidéo pour la contrôler ici" },

        // ── Sleep timer details ─────────────────────────────────
        ["{0} min remaining"] = new() { ["en"] = "{0} min remaining", ["fr"] = "{0} min restantes" },
        ["{0}s remaining"] = new() { ["en"] = "{0}s remaining", ["fr"] = "{0}s restantes" },
        ["Cancel Timer"] = new() { ["en"] = "Cancel Timer", ["fr"] = "Annuler la minuterie" },
        ["{0} min"] = new() { ["en"] = "{0} min", ["fr"] = "{0} min" },
        ["{0}h {1}min"] = new() { ["en"] = "{0}h {1}min", ["fr"] = "{0}h {1}min" },
        ["{0}h"] = new() { ["en"] = "{0}h", ["fr"] = "{0}h" },

        // ── Settings window ─────────────────────────────────────
        ["Folders"] = new() { ["en"] = "Folders", ["fr"] = "Dossiers" },
        ["Appearance"] = new() { ["en"] = "Appearance", ["fr"] = "Apparence" },
        ["Music Folders"] = new() { ["en"] = "Music Folders", ["fr"] = "Dossiers musicaux" },
        ["Add folders containing your music files. They will be scanned for audio tracks."] = new() { ["en"] = "Add folders containing your music files. They will be scanned for audio tracks.", ["fr"] = "Ajoutez des dossiers contenant vos fichiers musicaux. Ils seront analysés pour trouver des pistes audio." },
        ["Scan Now"] = new() { ["en"] = "Scan Now", ["fr"] = "Scanner maintenant" },
        ["No folders added yet."] = new() { ["en"] = "No folders added yet.", ["fr"] = "Aucun dossier ajouté." },
        ["Choose the color theme for the application."] = new() { ["en"] = "Choose the color theme for the application.", ["fr"] = "Choisissez le thème de couleur de l'application." },
        ["Backdrop Effect"] = new() { ["en"] = "Backdrop Effect", ["fr"] = "Effet d'arrière-plan" },
        ["Choose the visual effect applied to the window background."] = new() { ["en"] = "Choose the visual effect applied to the window background.", ["fr"] = "Choisissez l'effet visuel appliqué à l'arrière-plan de la fenêtre." },
        ["Frame rate for the audio visualizer."] = new() { ["en"] = "Frame rate for the audio visualizer.", ["fr"] = "Fréquence d'images pour le visualiseur audio." },
        ["No tracks in library."] = new() { ["en"] = "No tracks in library.", ["fr"] = "Aucune piste dans la bibliothèque." },

        // ── Scan status messages ────────────────────────────────
        ["Scanned {0}/{1} files..."] = new() { ["en"] = "Scanned {0}/{1} files...", ["fr"] = "Analysé {0}/{1} fichiers..." },
        ["Done. {0} tracks added."] = new() { ["en"] = "Done. {0} tracks added.", ["fr"] = "Terminé. {0} pistes ajoutées." },
        ["Scan cancelled."] = new() { ["en"] = "Scan cancelled.", ["fr"] = "Analyse annulée." },
        ["Done. {0} new tracks found."] = new() { ["en"] = "Done. {0} new tracks found.", ["fr"] = "Terminé. {0} nouvelles pistes trouvées." },
        ["Scanning all folders..."] = new() { ["en"] = "Scanning all folders...", ["fr"] = "Analyse de tous les dossiers..." },
        ["Unknown folder ({0})"] = new() { ["en"] = "Unknown folder ({0})", ["fr"] = "Dossier inconnu ({0})" },

        // ── Overlay widget ─────────────────────────────────────
        ["Overlay Widget"] = new() { ["en"] = "Overlay Widget", ["fr"] = "Widget flottant" },
        ["Show Overlay"] = new() { ["en"] = "Show Overlay", ["fr"] = "Afficher le widget" },
        ["Hide Overlay"] = new() { ["en"] = "Hide Overlay", ["fr"] = "Masquer le widget" },

        // ── Tray ────────────────────────────────────────────────
        ["Show\tCtrl+Alt+M"] = new() { ["en"] = "Show\tCtrl+Alt+M", ["fr"] = "Afficher\tCtrl+Alt+M" },
    };
}

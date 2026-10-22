using RadioWindows.Core.Models;

namespace RadioWindows.Core.Data;

/// <summary>
/// Built-in catalog: stations from radiopotok.ru/radiostations plus extra rock.
/// Stream URLs are the stations' own public HTTPS endpoints (not radiopotok proxies).
/// </summary>
public static class DefaultStations
{
    public static readonly IReadOnlyList<RadioStation> Catalog = Build();

    private static RadioStation S(
        string id,
        string name,
        string url,
        string genre,
        string? comment = null,
        string? logo = null) => new()
    {
        Id = id,
        Name = name,
        Url = url,
        Genre = genre,
        Comment = comment,
        LogoUrl = logo,
        BuiltIn = true,
    };

    private static List<RadioStation> Build() =>
    [
        // --- radiopotok.ru /radiostations ---
        S("rp-marusya", "Маруся ФМ", "https://radio-holding.ru:9433/marusya_default", GenreCatalog.Pop,
            "Новые русские хиты · radiopotok.ru", "https://top-radio.ru/assets/image/radio/180/marusyafm.png"),
        S("rp-rusradio", "Русское Радио", "https://rusradio.hostingradio.ru/rusradio96.aacp", GenreCatalog.Pop,
            "Отечественные исполнители · radiopotok.ru", "https://cdn.onlineradiobox.com/img/l/8/248.v10.png"),
        S("rp-vesti", "Вести FM", "https://icecast-vgtrk.cdnvideo.ru/vestifm_mp3_192kbps", GenreCatalog.News,
            "ВГТРК · radiopotok.ru"),
        S("rp-ruwave", "Русская Волна", "https://ru2.volna.top/RuWave48", GenreCatalog.Pop,
            "Русские песни · radiopotok.ru", "https://cdn.onlineradiobox.com/img/l/8/73538.v14.png"),
        S("rp-europa", "Европа Плюс", "https://europaplus.hostingradio.ru:8014/europaplus320.mp3", GenreCatalog.Pop,
            "radiopotok.ru", "https://europaplus.ru/favicon.ico"),
        S("rp-montecarlo", "Радио Монте-Карло", "https://montecarlo.hostingradio.ru/montecarlo96.aacp", GenreCatalog.Pop,
            "Зарубежные хиты · radiopotok.ru", "https://montecarlo.ru/favicon.ico"),
        S("rp-soloviev", "Соловьёв LIVE FM", "https://solovievfm.hostingradio.ru/solovievfm128.aacp", GenreCatalog.News,
            "radiopotok.ru"),
        S("rp-zvezda", "Радио Звезда", "https://zvezda-radio128.mediacdn.ru/radio/zvezda/zvezda_128", GenreCatalog.News,
            "radiopotok.ru"),
        S("rp-dorognoe", "Дорожное радио", "https://dorognoe.hostingradio.ru/radio", GenreCatalog.Pop,
            "radiopotok.ru", "https://dorognoe.ru/favicon.ico"),
        S("rp-chanson", "Радио Шансон", "https://chanson.hostingradio.ru:8041/chanson128.mp3", GenreCatalog.Shanson,
            "Шансон и барды · radiopotok.ru", "https://chanson.ru/favicon.ico"),
        S("rp-retrohit", "Ретро Хит", "https://retro.volna.top/Retro", GenreCatalog.Retro, "radiopotok.ru"),
        S("rp-soundpark", "SOUNDPARK DEEP", "https://h.getradio.me/spdeep/hls.m3u8", GenreCatalog.Electronic,
            "Deep House · radiopotok.ru",
            "https://play-lh.googleusercontent.com/fIXOGTWzNFCbbKHOF0JKbSUCh-Nvz2v8na0Da8tRNkLh3WumfUoSMDq3JKFkjQb2sZ8sz-1PXDS6PXE7xjyU=w240-h480-rw"),
        S("rp-choco", "Радио Шоколад", "https://choco.hostingradio.ru:10010/fm", GenreCatalog.Pop,
            "FM-каверы · radiopotok.ru", "https://chocoradio.ru/favicon.ico"),
        S("rp-rusrock", "Русский Рок", "https://rr-russkijrok.hostingradio.ru/russkijrok96.aacp", GenreCatalog.Rock,
            "Советский и русский рок · radiopotok.ru", "https://www.nashe.ru/favicons/apple-touch-icon.png"),
        S("rp-avto", "Авторадио", "https://pub0101.101.ru:8443/stream/air/aac/64/100", GenreCatalog.Pop,
            "radiopotok.ru", "https://avtoradio.ru/favicon.ico"),
        S("rp-novoe", "Новое радио", "https://stream.newradio.ru/novoe96.aacp", GenreCatalog.Pop,
            "radiopotok.ru", "https://newradio.ru/favicon.ico"),
        S("rp-kp", "Комсомольская правда", "https://kpradio.hostingradio.ru:8000/russia.radiokp128.mp3", GenreCatalog.News,
            "radiopotok.ru",
            "https://radiokp.ru/modules/custom/dna_kpradio/modules/kp_favicon/img/favicon-194x194.png"),
        S("rp-horoshee", "Хорошее Радио", "https://hr.amgradio.ru/Horoshee", GenreCatalog.Pop, "radiopotok.ru"),
        S("rp-mayak", "Маяк", "https://icecast-vgtrk.cdnvideo.ru/mayakfm_mp3_192kbps", GenreCatalog.News,
            "ВГТРК · radiopotok.ru"),
        S("rp-sputnik", "Sputnik", "https://icecast-rian.cdnvideo.ru/voicerus", GenreCatalog.News,
            "radiopotok.ru", "https://cdnn21.img.ria.ru/i/favicons/radiosputnik/apple-touch-icon.png"),
        S("rp-comedy", "Comedy Radio", "https://pub0101.101.ru:8443/stream/air/aac/64/202", GenreCatalog.Other,
            "Юмор · radiopotok.ru", "https://comedy-radio.ru/favicon.ico"),

        // --- Санкт-Петербург ---
        S("spb-piterfm", "Питер FM", "https://icecast-piterfm.cdnvideo.ru/piterfm", GenreCatalog.Pop,
            "100.9 FM · песни из русских фильмов и популярная музыка", "https://vote.mediastata.com/logos/16/pfm.png"),
        S("spb-metro", "Радио МЕТРО", "https://stream.radiometro.ru:8443/metro.mp3", GenreCatalog.World,
            "102.4 FM · музыка разных стран, городские новости и проекты", "https://radiometro.ru/favicon.ico"),
        S("spb-hermitage", "Радио Эрмитаж", "https://hermitage.hostingradio.ru/hermitage128.mp3", GenreCatalog.Jazz,
            "90.1 FM · джаз, блюз и лёгкая зарубежная музыка", "https://hermitage.fm/favicon.ico"),

        // --- extra rock ---
        S("rock-nashe", "Наше Радио", "https://nashe1.hostingradio.ru/nashe-128.mp3", GenreCatalog.Rock,
            "Русский рок", "https://www.nashe.ru/favicons/apple-touch-icon.png"),
        S("rock-nashe20", "Наше 2.0", "https://nashe1.hostingradio.ru:18000/nashe20-128.mp3", GenreCatalog.Rock,
            "Новый русский рок", "https://www.nashe.ru/favicons/apple-touch-icon.png"),
        S("rock-nashe-punks", "Наше Радио — Панки ХОЙ!", "https://nashe1.hostingradio.ru/nashepunks.mp3", GenreCatalog.Rock,
            null, "https://www.nashe.ru/favicons/apple-touch-icon.png"),
        S("rock-fm", "ROCK FM", "https://nashe1.hostingradio.ru/rock-128.mp3", GenreCatalog.Rock,
            "Зарубежный рок", "https://www.nashe.ru/favicons/apple-touch-icon.png"),
        S("rock-maximum", "Radio Maximum", "https://maximum.hostingradio.ru/maximum96.aacp", GenreCatalog.Rock,
            null, "https://www.maximum.ru/icon.ico"),
        S("rock-maximum-metallica", "Radio Maximum — Metallica", "https://metallica.hostingradio.ru/metallica96.aacp",
            GenreCatalog.Rock, null, "https://www.maximum.ru/icon.ico"),
        S("rock-maximum-rammstein", "Radio Maximum — Rammstein", "https://maximum-rammstein.hostingradio.ru/rammstein96.aacp",
            GenreCatalog.Rock, null, "https://www.maximum.ru/icon.ico"),
        S("rock-paradise", "Radio Paradise — Rock", "https://stream.radioparadise.com/rock-128", GenreCatalog.Rock,
            null, "https://radioparadise.com/apple-touch-icon.png"),
        S("rock-soma-metal", "SomaFM Metal Detector", "https://ice1.somafm.com/metal-128-mp3", GenreCatalog.Rock,
            "Metal / hard rock", "https://somafm.com/img3/groovesalad-400.jpg"),
        S("rock-soma-70s", "SomaFM Left Coast 70s", "https://ice1.somafm.com/seventies-128-mp3", GenreCatalog.Rock,
            "Classic rock 70s", "https://somafm.com/img3/indiepop-400.jpg"),
        S("rock-arrow", "Arrow Classic Rock", "https://stream.gal.io/arrow", GenreCatalog.Rock,
            "Нидерланды", "https://www.arrow.nl/wp-content/uploads/2020/08/logo.png"),
        S("kexp", "KEXP", "https://kexp-mp3-128.streamguys1.com/kexp128.mp3", GenreCatalog.Rock,
            "Seattle", "https://www.kexp.org/static/assets/img/favicon-32x32.png"),

        // --- international keepers ---
        S("rp-mix", "Radio Paradise — Mix", "https://stream.radioparadise.com/aac-128", GenreCatalog.Chill,
            "Эклектика без рекламы", "https://radioparadise.com/apple-touch-icon.png"),
        S("rp-mellow", "Radio Paradise — Mellow", "https://stream.radioparadise.com/mellow-128", GenreCatalog.Chill,
            null, "https://radioparadise.com/apple-touch-icon.png"),
        S("soma-groove", "SomaFM Groove Salad", "https://ice1.somafm.com/groovesalad-128-mp3", GenreCatalog.Chill,
            "Ambient / downtempo", "https://somafm.com/img3/groovesalad-400.jpg"),
        S("soma-drone", "SomaFM Drone Zone", "https://ice1.somafm.com/dronezone-128-mp3", GenreCatalog.Ambient,
            null, "https://somafm.com/img3/dronezone-400.jpg"),
        S("soma-indie", "SomaFM Indie Pop Rocks", "https://ice1.somafm.com/indiepop-128-mp3", GenreCatalog.Pop,
            null, "https://somafm.com/img3/indiepop-400.jpg"),
        S("soma-folk", "SomaFM Folk Forward", "https://ice1.somafm.com/folkfwd-128-mp3", GenreCatalog.Folk,
            null, "https://somafm.com/img3/folkfwd-400.jpg"),
        S("swiss-jazz", "Radio Swiss Jazz", "https://stream.srg-ssr.ch/m/rsj/mp3_128", GenreCatalog.Jazz,
            null, "https://www.radioswissjazz.ch/favicon.ico"),
        S("swiss-classic", "Radio Swiss Classic", "https://stream.srg-ssr.ch/m/rsc_de/mp3_128", GenreCatalog.Classical,
            null, "https://www.radioswissclassic.ch/favicon.ico"),
        S("fip", "FIP", "https://icecast.radiofrance.fr/fip-midfi.mp3", GenreCatalog.World,
            "Radio France", "https://www.radiofrance.fr/favicon.ico"),
        S("nts-1", "NTS Radio 1", "https://stream-relay-geo.ntslive.net/stream", GenreCatalog.Electronic,
            null, "https://www.nts.live/favicon.ico"),
        S("bbc-ws", "BBC World Service", "https://stream.live.vc.bbcmedia.co.uk/bbc_world_service", GenreCatalog.News,
            null, "https://www.bbc.com/favicon.ico"),
    ];
}

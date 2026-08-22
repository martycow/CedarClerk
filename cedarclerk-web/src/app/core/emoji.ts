// The editor's emoji picker set, out of the component because it is data and not behaviour.
//
// Deliberately a hand-picked set rather than a full Unicode table: a picker with every emoji in it
// needs a full name index, and this one carries its own names — two per entry, because the app has
// two UI languages and a search that only matched English would be a search only half the users
// could use.

export interface EmojiEntry {
    /** What gets inserted. */
    ch: string;
    /** Search terms, English then Russian. Matched as substrings, so stems are enough. */
    en: string;
    ru: string;
    /** Drawn under the glyph. Only the flags carry one — see the note on that group. */
    label?: string;
}

export interface EmojiGroup {
    key: string;
    emoji: EmojiEntry[];
    /** A group whose entries are colour runs rather than single glyphs, drawn as wide pills. */
    wide?: boolean;
}

const e = (ch: string, en: string, ru: string, label?: string): EmojiEntry => ({ ch, en, ru, label });

export const EMOJI_GROUPS: EmojiGroup[] = [
    {
        key: 'faces',
        emoji: [
            e('😀', 'grin smile happy', 'улыбка радость'),
            e('😃', 'smile happy open', 'улыбка радость'),
            e('😄', 'smile laugh happy', 'смех улыбка'),
            e('😁', 'beam grin teeth', 'ухмылка улыбка'),
            e('😅', 'sweat relief laugh', 'пот смех облегчение'),
            e('😂', 'joy tears laugh', 'слёзы смех'),
            e('🙂', 'slight smile', 'лёгкая улыбка'),
            e('😉', 'wink', 'подмигивание'),
            e('😊', 'blush smile warm', 'румянец улыбка'),
            e('😇', 'angel halo innocent', 'ангел нимб'),
            e('😍', 'love hearts eyes', 'любовь сердечки'),
            e('😘', 'kiss blow', 'поцелуй'),
            e('😋', 'yum tasty tongue', 'вкусно язык'),
            e('😜', 'tongue wink silly', 'язык дразнить'),
            e('🤪', 'zany crazy wild', 'безумие сумасшествие'),
            e('🤨', 'raised eyebrow doubt', 'бровь сомнение'),
            e('🧐', 'monocle inspect', 'монокль изучение'),
            e('😎', 'cool sunglasses', 'круто очки'),
            e('🥳', 'party celebrate', 'праздник вечеринка'),
            e('🤩', 'star struck amazed', 'звёзды восторг'),
            e('😏', 'smirk sly', 'ухмылка'),
            e('😒', 'unamused meh', 'недовольство'),
            e('😞', 'disappointed sad', 'разочарование грусть'),
            e('😢', 'cry sad tear', 'плач слеза грусть'),
            e('😭', 'sob cry loud', 'рыдание плач'),
            e('😤', 'triumph steam angry', 'пар злость'),
            e('😡', 'angry rage mad', 'злость ярость'),
            e('🤯', 'mind blown shock', 'взрыв мозга шок'),
            e('😱', 'scream fear', 'крик страх'),
            e('😳', 'flushed embarrassed', 'смущение'),
            e('🥺', 'pleading beg', 'умоляющий'),
            e('😬', 'grimace awkward', 'неловкость'),
            e('🙄', 'eye roll', 'закатить глаза'),
            e('😴', 'sleep zzz', 'сон спать'),
            e('🤒', 'sick fever', 'болезнь температура'),
            e('🤢', 'nausea sick', 'тошнота'),
            e('🤠', 'cowboy hat', 'ковбой'),
            e('🥸', 'disguise glasses', 'маскировка'),
            e('🤖', 'robot bot', 'робот бот'),
            e('👻', 'ghost spooky', 'призрак'),
        ],
    },
    {
        key: 'gestures',
        emoji: [
            e('👍', 'thumbs up like yes', 'палец вверх лайк'),
            e('👎', 'thumbs down dislike no', 'палец вниз дизлайк'),
            e('👌', 'ok perfect', 'окей отлично'),
            e('✌️', 'peace victory', 'мир победа'),
            e('🤞', 'fingers crossed luck', 'скрещенные пальцы удача'),
            e('🤙', 'call shaka', 'звони'),
            e('👋', 'wave hello bye', 'привет пока'),
            e('🤝', 'handshake deal', 'рукопожатие сделка'),
            e('🙏', 'pray thanks please', 'спасибо пожалуйста молитва'),
            e('👏', 'clap applause', 'аплодисменты'),
            e('💪', 'muscle strong', 'мышца сила'),
            e('🫡', 'salute', 'честь салют'),
            e('🤷', 'shrug dunno', 'пожать плечами'),
            e('🤦', 'facepalm', 'фейспалм'),
            e('🙌', 'raised hands celebrate', 'руки вверх'),
            e('👀', 'eyes look watch', 'глаза смотреть'),
            e('🧠', 'brain think', 'мозг думать'),
            e('🫶', 'heart hands love', 'сердце руками'),
            e('✍️', 'write pen', 'писать ручка'),
            e('🤌', 'pinched fingers', 'щепотка'),
        ],
    },
    {
        key: 'symbols',
        emoji: [
            e('❤️', 'red heart love', 'красное сердце любовь'),
            e('🧡', 'orange heart', 'оранжевое сердце'),
            e('💛', 'yellow heart', 'жёлтое сердце'),
            e('💚', 'green heart', 'зелёное сердце'),
            e('💙', 'blue heart', 'синее сердце'),
            e('💜', 'purple heart', 'фиолетовое сердце'),
            e('🖤', 'black heart', 'чёрное сердце'),
            e('💔', 'broken heart', 'разбитое сердце'),
            e('💯', 'hundred perfect', 'сто отлично'),
            e('🔥', 'fire hot lit', 'огонь'),
            e('✨', 'sparkles magic', 'искры магия'),
            e('⭐', 'star', 'звезда'),
            e('🌟', 'glowing star', 'сияющая звезда'),
            e('⚡', 'lightning power', 'молния'),
            e('💥', 'boom explosion', 'взрыв'),
            e('🎉', 'party popper', 'хлопушка праздник'),
            e('🎊', 'confetti', 'конфетти'),
            e('🚀', 'rocket launch ship', 'ракета запуск'),
            e('💡', 'idea bulb', 'идея лампочка'),
            e('🏆', 'trophy win', 'кубок победа'),
            e('✅', 'check done yes', 'галочка готово'),
            e('❌', 'cross no wrong', 'крестик нет'),
            e('⚠️', 'warning caution', 'предупреждение'),
            e('❓', 'question', 'вопрос'),
            e('❗', 'exclamation', 'восклицание'),
            e('➡️', 'right arrow', 'стрелка вправо'),
            e('⬅️', 'left arrow', 'стрелка влево'),
            e('🔁', 'repeat loop', 'повтор цикл'),
            e('🔒', 'lock closed private', 'замок закрыто'),
            e('🔓', 'unlock open', 'замок открыт'),
        ],
    },
    {
        key: 'objects',
        emoji: [
            e('📌', 'pin', 'булавка закрепить'),
            e('📎', 'clip attach', 'скрепка'),
            e('🔗', 'link chain', 'ссылка'),
            e('📷', 'camera photo', 'камера фото'),
            e('🎬', 'clapper film video', 'хлопушка кино видео'),
            e('🎧', 'headphones audio', 'наушники звук'),
            e('🎮', 'gamepad game', 'геймпад игра'),
            e('📚', 'books read', 'книги чтение'),
            e('📝', 'memo write note', 'заметка писать'),
            e('📅', 'calendar date', 'календарь дата'),
            e('💻', 'laptop computer', 'ноутбук компьютер'),
            e('🖱️', 'mouse', 'мышь'),
            e('⌨️', 'keyboard', 'клавиатура'),
            e('🗂️', 'folders index', 'папки'),
            e('📦', 'box package build', 'коробка сборка'),
            e('🛠️', 'tools fix', 'инструменты'),
            e('🧪', 'test tube experiment', 'пробирка эксперимент'),
            e('🧭', 'compass direction', 'компас'),
            e('☕', 'coffee', 'кофе'),
            e('🍺', 'beer', 'пиво'),
            e('🐮', 'cow moo', 'корова му'),
            e('🌲', 'tree pine cedar', 'дерево ель кедр'),
            e('🏔️', 'mountain', 'гора'),
            e('🌊', 'wave sea', 'волна море'),
            e('🌧️', 'rain', 'дождь'),
            e('❄️', 'snow cold', 'снег холод'),
            e('🌙', 'moon night', 'луна ночь'),
            e('☀️', 'sun day', 'солнце день'),
            e('🕹️', 'joystick arcade', 'джойстик'),
            e('🎲', 'dice random', 'кубик случайность'),
        ],
    },
    // The label is not decoration. Country flags are regional-indicator pairs, and Windows ships no
    // glyphs for them — in Chrome on Windows the picker showed nothing to pick (Marty). The code
    // under each button is what makes the group usable there, and it costs a Mac nothing.
    {
        key: 'flags',
        emoji: [
            e('🇺🇦', 'ukraine', 'украина', 'UA'), e('🇧🇾', 'belarus', 'беларусь', 'BY'),
            e('🇬🇪', 'georgia', 'грузия', 'GE'), e('🇷🇺', 'russia', 'россия', 'RU'),
            e('🇦🇲', 'armenia', 'армения', 'AM'), e('🇦🇿', 'azerbaijan', 'азербайджан', 'AZ'),
            e('🇰🇿', 'kazakhstan', 'казахстан', 'KZ'), e('🇰🇬', 'kyrgyzstan', 'киргизия', 'KG'),
            e('🇺🇿', 'uzbekistan', 'узбекистан', 'UZ'), e('🇹🇯', 'tajikistan', 'таджикистан', 'TJ'),
            e('🇹🇲', 'turkmenistan', 'туркмения', 'TM'), e('🇲🇩', 'moldova', 'молдова', 'MD'),
            e('🇱🇻', 'latvia', 'латвия', 'LV'), e('🇱🇹', 'lithuania', 'литва', 'LT'),
            e('🇪🇪', 'estonia', 'эстония', 'EE'), e('🇵🇱', 'poland', 'польша', 'PL'),
            e('🇩🇪', 'germany', 'германия', 'DE'), e('🇫🇷', 'france', 'франция', 'FR'),
            e('🇬🇧', 'britain uk', 'британия англия', 'GB'), e('🇺🇸', 'usa america', 'сша америка', 'US'),
            e('🇮🇹', 'italy', 'италия', 'IT'), e('🇪🇸', 'spain', 'испания', 'ES'),
            e('🇵🇹', 'portugal', 'португалия', 'PT'), e('🇳🇱', 'netherlands', 'нидерланды', 'NL'),
            e('🇨🇿', 'czechia', 'чехия', 'CZ'), e('🇸🇰', 'slovakia', 'словакия', 'SK'),
            e('🇷🇸', 'serbia', 'сербия', 'RS'), e('🇹🇷', 'turkey', 'турция', 'TR'),
            e('🇮🇱', 'israel', 'израиль', 'IL'), e('🇬🇷', 'greece', 'греция', 'GR'),
            e('🇫🇮', 'finland', 'финляндия', 'FI'), e('🇸🇪', 'sweden', 'швеция', 'SE'),
            e('🇳🇴', 'norway', 'норвегия', 'NO'), e('🇩🇰', 'denmark', 'дания', 'DK'),
            e('🇨🇭', 'switzerland', 'швейцария', 'CH'), e('🇨🇳', 'china', 'китай', 'CN'),
            e('🇯🇵', 'japan', 'япония', 'JP'), e('🇰🇷', 'korea', 'корея', 'KR'),
            e('🇮🇳', 'india', 'индия', 'IN'), e('🇧🇷', 'brazil', 'бразилия', 'BR'),
            e('🇨🇦', 'canada', 'канада', 'CA'), e('🇦🇺', 'australia', 'австралия', 'AU'),
            e('🇪🇺', 'european union', 'евросоюз', 'EU'), e('🇺🇳', 'united nations', 'оон', 'UN'),
            e('🏳️', 'white flag', 'белый флаг', '—'), e('🏴', 'black flag', 'чёрный флаг', '—'),
            e('🏁', 'chequered finish', 'финиш', '—'), e('🚩', 'triangular flag', 'красный флажок', '—'),
            e('🏳️‍🌈', 'rainbow pride', 'радужный флаг', '—'), e('🏴‍☠️', 'pirate jolly roger', 'пиратский флаг', '—'),
        ],
    },
    // Flags Unicode never got: the white-red-white flag and the 1991–1993 Russian tricolour with
    // its lighter blue. No codepoint exists for either, and how 🇷🇺 renders is the READER'S
    // platform font's choice, not ours — so these are the colour sequences people actually use in
    // Telegram, inserted as one button. Plain text end to end: editor, Telegram and blog all pass
    // them through untouched.
    {
        key: 'flagSequences',
        wide: true,
        emoji: [
            e('⚪️🔴⚪️', 'belarus white red white', 'беларусь бчб'),
            e('🤍❤️🤍', 'belarus white red white hearts', 'беларусь бчб сердца'),
            e('🤍💙❤️', 'russia tricolour 1991', 'россия триколор'),
            e('💙💛', 'ukraine blue yellow', 'украина сине жёлтый'),
        ],
    },
];

/** Entries whose glyph, either name or group name contains every word of the query. */
export function searchEmoji(groups: EmojiGroup[], query: string): EmojiGroup[] {
    const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    if (!words.length) return groups;
    return groups
        .map(g => ({
            ...g,
            emoji: g.emoji.filter(entry => {
                const hay = `${entry.ch} ${entry.en} ${entry.ru} ${entry.label ?? ''} ${g.key}`.toLowerCase();
                return words.every(w => hay.includes(w));
            }),
        }))
        .filter(g => g.emoji.length > 0);
}

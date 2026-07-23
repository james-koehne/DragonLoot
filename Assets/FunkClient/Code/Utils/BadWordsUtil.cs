using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using UnityEngine;

public class BadWordsUtil
{
	private static readonly string[] ManualBadWords =
	{
		"n16ger", "n16g3r", "n1663r", "n166er", "nikker", "autism", "fked", "fkd", "fk3d", "n1ga", "nig4", "niga", "n1g4", "nword", "nwurd", "niger", "nigur", "n.i.g.g.e.r.", "n.i.g.g.a.", "n.i.g.e.r.", "n1ger", "n1g3r", "n1gur", "scrot", "foreskin", "4skin", "4sk1n", "nuts", "nutz", "4r5e", "5h1t", "5hit", "a55", "anal", "anus", "ar5e", "arrse", "arse", "ass", "ass-fucker", "asses", "assfucker", "assfukka", "asshole", "assholes", "asswhole", "a_s_s", "b!tch", "b00bs", "b17ch", "b1tch", "ballbag", "balls", "nlgger", "killwhitepeople", "killblackpeople", "killyellowpeople", "killbrownpeople", "ballsack", "bastard", "beastial", "beastiality", "bellend", "bestial", "bestiality", "bi+ch", "biatch", "bitch", "bitcher", "bitchers", "bitches", "bitchin", "bitching", "bloody", "blow job", "blowjob", "blowjobs", "boiolas", "bollock", "bollok", "boner", "boob", "boobs", "booobs", "boooobs", "booooobs", "booooooobs", "breasts", "buceta", "bugger", "bum", "bunny fucker", "butt", "butthole", "buttmuch", "buttplug", "c0ck", "c0cksucker", "carpet muncher", "cawk", "chink", "cipa", "cl1t", "clit", "clitoris", "clits", "cnut", "cock", "cock-sucker", "cockface", "cockhead", "cockmunch", "cockmuncher", "cocks", "cocksuck", "cocksucked", "cocksucker", "cocksucking", "cocksucks", "cocksuka", "cocksukka", "cok", "cokmuncher", "coksucka", "coon", "cox", "crap", "cum", "cummer", "cumming", "cums", "cumshot", "cunilingus", "cunillingus", "cunnilingus", "cunt", "cuntlick", "cuntlicker", "cuntlicking", "cunts", "cyalis", "cyberfuc", "cyberfuck", "cyberfucked", "cyberfucker", "cyberfuckers", "cyberfucking", "d1c", "d1ck", "d1k", "damn", "dic", "dick", "dickhead", "d1ldo", "dildo", "dildos", "dink", "dinks", "dirsa", "dlck", "dog-fucker", "doggin", "dogging", "donkeyribber", "doosh", "duche", "dyke", "ejaculate", "ejaculated", "ejaculates", "ejaculating", "ejaculatings", "ejaculation", "ejakulate", "f u c k", "f u c k e r", "f4nny", "fag", "fagging", "faggitt", "faggot", "faggs", "fagot", "fagots", "fags", "fanny", "fannyflaps", "fannyfucker", "fanyy", "fatass", "fcuk", "fck", "fcuker", "fcuking", "feck", "fecker", "felching", "fellate", "fellatio", "fingerfuck", "fingerfucked", "fingerfucker", "fingerfuckers", "fingerfucking", "fingerfucks", "fistfuck", "fistfucked", "fistfucker", "fistfuckers", "fistfucking", "fistfuckings", "fistfucks", "flange", "fook", "fooker", "fuck", "f*ck", "fu*k", "f**k", "fucka", "fucked", "fucker", "fuckers", "fuckhead", "fuckheads", "fuckin", "fucking", "fuckings", "fuckingshitmotherfucker", "fuckme", "fucks", "fuckwhit", "fuckwit", "fudge packer", "fudgepacker", "fuk", "fuker", "fukker", "fukkin", "fuks", "fukwhit", "fukwit", "fux", "fux0r", "f_u_c_k", "gangbang", "gangbanged", "gangbangs", "gaylord", "gaysex", "goatse", "god-dam", "god-damned", "goddamn", "goddamned", "hardcoresex", "hell", "heshe", "hoar", "hoare", "hoer", "homo", "hore", "horniest", "horny", "hotsex", "jack-off", "jackoff", "jap", "jerk-off", "jism", "jiz", "jizm", "jizz", "kawk", "knob", "knobead", "knobed", "knobend", "knobhead", "knobjocky", "knobjokey", "kock", "kondum", "kondums", "kum", "kummer", "kumming", "kums", "kunilingus", "l3i+ch", "l3itch", "labia", "lmfao", "lust", "lusting", "m0f0", "m0fo", "m45terbate", "ma5terb8", "ma5terbate", "masochist", "master-bate", "masterb8", "masterbat*", "masterbat3", "masterbate", "masterbation", "masterbations", "masturbate", "mo-fo", "mof0", "mofo", "mothafuck", "mothafucka", "mothafuckas", "mothafuckaz", "mothafucked", "mothafucker", "mothafuckers", "mothafuckin", "mothafucking", "mothafuckings", "mothafucks", "motherfucker", "motherfuck", "motherfucked", "motherfucker", "motherfuckers", "motherfuckin", "motherfucking", "motherfuckings", "motherfuckka", "motherfucks", "muff", "mutha", "muthafecker", "muthafuckker", "muther", "mutherfucker", "n1gga", "n1gger", "nazi", "n4z1", "n4zi", "naz1", "nigg3r", "nigg", "n1gg", "n|gg", "nigg4h", "nigga", "niggah", "niggas", "niggaz", "nigger", "niggers", "nob", "nob jokey", "nobhead", "nobjocky", "nobjokey", "numbnuts", "nutsack", "orgasim", "orgasims", "orgasm", "orgasms", "p0rn", "pawn", "pecker", "penis", "penisfucker", "phonesex", "phuck", "phuk", "phuked", "phuking", "phukked", "phukking", "phuks", "phuq", "pigfucker", "pimpis", "piss", "pissed", "pisser", "pissers", "pisses", "pissflaps", "pissin", "pissing", "pissoff", "poop", "porn", "porno", "pornography", "pornos", "prick", "pricks", "pron", "pube", "pusse", "pussi", "pussies", "pussy", "pussys", "rectum", "retard", "rimjaw", "rimming", "s hit", "s.o.b.", "sadist", "schlong", "screwing", "scroat", "scrote", "scrotum", "semen", "sex", "sh!+", "sh!t", "sh1t", "shag", "shagger", "shaggin", "shagging", "shemale", "sh|t", "shi+", "shit", "shitdick", "shite", "shited", "shitey", "shitfuck", "shitfull", "shithead", "shiting", "shitings", "shits", "shitted", "shitter", "shitters", "shitting", "shittings", "shitty", "skank", "slut", "sluts", "smegma", "smut", "snatch", "son-of-a-bitch", "spunk", "s_h_i_t", "t1tt1e5", "t1tties", "teets", "teez", "testical", "testicle", "tit", "titfuck", "tits", "titt", "tittie5", "tittiefucker", "titties", "tittyfuck", "tittywank", "titwank", "tosser", "turd", "tw4t", "twat", "twathead", "twatty", "twunt", "twunter", "v14gra", "v1gra", "vagina", "viagra", "vulva", "w00se", "jew", "j3w", "wang", "wank", "wanker", "wanky", "whoar", "whore", "wh0re", "wh0r3", "whor3", "willies", "willy", "xrated", "bollocks", "child-fucker", "child", "Christonabike", "Christonacracker", "swearword", "godsdamn", "holyshit", "Jesus", "JesusChrist", "JesusH.Christ", "JesusHaroldChrist", "Jesuswept", "Jesus,MaryandJoseph", "JudasPriest", "shitass", "shitass", "sonofabitch", "sonofamotherlessgoat", "sonofawhore", "sweetJesus", "2g1c", "2girls1cup", "acrotomophilia", "alabamahotpocket", "alaskanpipeline", "anilingus", "apeshit", "arsehole", "assmunch", "autoerotic", "autoerotic", "babeland", "babybatter", "babyjuice", "ballgag", "ballgravy", "ballkicking", "balllicking", "ballsack", "ballsucking", "bangbros", "bareback", "barelylegal", "barenaked", "bastardo", "bastinado", "bbw", "bdsm", "beaner", "beaners", "beavercleaver", "beaverlips", "bigblack", "bigbreasts", "bigknockers", "bigtits", "bimbos", "birdlock", "blackcock", "blondeaction", "blondeonblondeaction", "blowyourload", "bluewaffle", "blumpkin", "bondage", "bootycall", "brownshowers", "brunetteaction", "bukkake", "bulldyke", "bulletvibe", "bullshit", "bunghole", "bunghole", "busty", "buttcheeks", "cameltoe", "camgirl", "camslut", "camwhore", "carpetmuncher", "chocolaterosebuds", "circlejerk", "clevelandsteamer", "cloverclamps", "clusterfuck", "coprolagnia", "coprophilia", "cornhole", "coons", "creampie", "darkie", "daterape", "daterape", "deepthroat", "deepthroat", "dendrophilia", "dingleberry", "dingleberries", "dirtypillows", "dirtysanchez", "doggiestyle", "doggiestyle", "doggystyle", "doggystyle", "dogstyle", "dolcett", "domination", "dominatrix", "dommes", "donkeypunch", "doubledong", "doublepenetration", "dpaction", "dryhump", "dvda", "eatmyass", "ecchi", "erotic", "erotism", "escort", "eunuch", "fecal", "felch", "feltch", "femalesquirting", "femdom", "figging", "fingerbang", "fingering", "fisting", "footfetish", "footjob", "frotting", "fuckbuttons", "fucktards", "futanari", "gangbang", "gaysex", "genitals", "giantcock", "girlon", "girlontop", "girlsgonewild", "goatcx", "goddamn", "gokkun", "goldenshower", "goodpoop", "googirl", "goregasm", "grope", "groupsex", "g-spot", "guro", "handjob", "handjob", "hardcore", "hentai", "homoerotic", "honkey", "hooker", "hotcarl", "hotchick", "howtokill", "howtomurder", "hugefat", "humping", "incest", "intercourse", "jackoff", "jailbait", "jailbait", "jellydonut", "jerkoff", "jigaboo", "jiggaboo", "jiggerboo", "juggs", "kike", "kinbaku", "kinkster", "kinky", "knobbing", "leatherrestraint", "leatherstraightjacket", "lemonparty", "lolita", "lovemaking", "makemecome", "malesquirting", "menageatrois", "milf", "missionaryposition", "moundofvenus", "mrhands", "nig", "n1g", "muffdiver", "muffdiving", "nambla", "nawashi", "negro", "neonazi", "nignog", "nimphomania", "nipple", "nipples", "nsfwimages", "nude", "nudity", "nympho", "nymphomania", "octopussy", "omorashi", "onecuptwogirls", "oneguyonejar", "orgy", "paedophile", "paki", "panties", "panty", "pedobear", "pedophile", "hitler", "h1tler", "h1tl3r", "hitl3r", "pegging", "phonesex", "pieceofshit", "pisspig", "pisspig", "playboy", "pleasurechest", "polesmoker", "ponyplay", "poof", "poon", "poontang", "punany", "poopchute", "poopchute", "princealbertpiercing", "pthc", "pubes", "queaf", "queef", "quim", "raghead", "ragingboner", "rape", "raping", "rapist", "reversecowgirl", "rimjob", "rosypalm", "rosypalmandher5sisters", "rustytrombone", "sadism", "santorum", "scat", "scissoring", "sexo", "sexy", "shavedbeaver", "shavedpussy", "shibari", "shitblimp", "shota", "shrimping", "skeet", "slanteye", "s&m", "snowballing", "sodomize", "sodomy", "spic", "splooge", "sploogemoose", "spooge", "spreadlegs", "strapon", "strapon", "strappado", "stripclub", "styledoggy", "suck", "sucks", "suicidegirls", "sultrywomen", "swastika", "swinger", "taintedlove", "tastemy", "teabagging", "threesome", "throating", "tiedup", "tightwhite", "titty", "tongueina", "topless", "towelhead", "tranny", "tribadism", "tubgirl", "tubgirl", "tushy", "twink", "twinkie", "twogirlsonecup", "undressing", "upskirt", "urethraplay", "urophilia", "venusmound", "vibrator", "violet wand", "vorarephilia", "voyeur", "wetback", "wetdream", "iqqer", "whitepower", "wrappingmen", "wrinkledstarfish", "yaoi", "yellowshowers", "yiffy", "zoophilia", "a54", "buttmunch", "donkeypunch", "fleshflute", "asswipe", "bitchass", "moomoofoofoo", "trumped", "assbag", "assbandit", "assbanger", "assbite", "assclown", "asscock", "asscracker", "assface", "assfuck", "assgoblin", "asshat", "ass-hat", "asshead", "asshopper", "ass-jabber", "assjacker", "asslick", "asslicker", "assmonkey", "assmuncher", "assnigger", "asspirate", "ass-pirate", "assshit", "assshole", "asssucker", "asswad", "axwound", "bampot", "bitchtits", "bitchy", "bollox", "brotherfucker", "bumblefuck", "butt plug", "buttfucka", "butt-pirate", "buttfucker", "chesticle", "chinc", "choad", "chode", "clitface", "clitfuck", "cockass", "cockbite", "cockburger", "cockfucker", "cockjockey", "cockknoker", "cockmaster", "cockmongler", "cockmongruel", "cockmonkey", "cocknose", "cocknugget", "cockshit", "cocksmith", "cocksmoke", "cocksmoker", "cocksniffer", "cockwaffle", "coochie", "coochy", "cooter", "cracker", "cumbubble", "cumdumpster", "cumguzzler", "cumjockey", "cumslut", "cumtart", "cunnie", "cuntass", "cuntface", "cunthole", "cuntrag", "cuntslut", "dago", "deggo", "dic", "dickbag", "dickbeaters", "dickface", "dickfuck", "dickfucker", "dickhole", "dickjuice", "dickmilk ", "dickmonger", "dicks", "dickslap", "dick-sneeze", "dicksucker", "dicksucking", "dicktickler", "dickwad", "dickweasel", "dickweed", "dickwod", "dik", "dike", "dipshit", "doochbag", "dookie", "douche", "douchebag", "douche-fag", "douchewaffle", "dumass", "dumb ass", "dumbass", "dumbfuck", "dumbshit", "dumshit", "fagbag", "fagfucker", "faggit", "faggotcock", "fagtard", "flamer", "fuckass", "fuckbag", "fuckboy", "fuckbrain", "fuckbutt", "fuckbutter", "fuckersucker", "fuckface", "fuckhole", "fucknut", "fucknutt", "fuckoff", "fuckstick", "fucktard", "fucktart", "fuckup", "fuckwad", "fuckwitt", "gay", "gayass", "gaybob", "gaydo", "gayfuck", "gayfuckist", "gaytard", "gaywad", "goddamnit", "gooch", "gook", "gringo", "guido", "hard on", "heeb", "hoe", "homodumbshit", "jackass", "jagoff", "jerkass", "jungle bunny", "junglebunny", "kooch", "kootch", "kraut", "kunt", "kyke", "lameass", "lardass", "lesbian", "lesbo", "lezzie", "mcfagget", "mick", "minge", "muffdiver", "munging", "nigaboo", "niglet", "nutsack", "panooch", "peckerhead", "penisbanger", "penispuffer", "pissedoff", "polesmoker", "pollock", "poonani", "poonany", "porchmonkey", "porchmonkey", "punanny", "punta", "pussylicking", "puto", "queer", "queerbait", "queerhole", "renob", "ruski", "sandnigger", "sandnigger", "shitbag", "shitbagger", "shitbrains", "shitbreath", "shitcanned", "shitcunt", "shitface", "shitfaced", "shithole", "shithouse", "shitspitter", "shitstain", "shittiest", "shiz", "shiznit", "skullfuck", "slutbag", "smeg", "spick", "spook", "suckass", "tard", "thundercunt", "twatlips", "twats", "twatwaffle", "unclefucker", "vag", "vajayjay", "va-j-j", "vjayjay", "wankjob", "whorebag", "whoreface", "wop", "fuckyou", "pissoff", "dickhead", "bloodyhell", "crikey", "rubbish", "takingthepiss", "jerk", "knobend", "lmao", "omg", "wtf", "bint", "ginger", "git", "minger", "munter", "sodoff", "chinky", "chocice", "gippo", "golliwog", "hun", "iap", "jock", "nig-nog", "pikey", "polack", "sambo", "slope", "spade", "taff", "wog", "beaver", "beefcurtains", "bloodclaat", "clunge", "flaps", "gash", "punani", "battyboy", "bender", "bumboy", "bumclat", "bummer", "chi-chiman", "chickwithadick", "fudge-packer", "genderbender", "he-she", "lezza/lesbo", "pansy", "shirtlifter", "cretin", "cripple", "div", "looney", "midget", "mong", "nutter", "psycho", "schizo", "veqtable", "windowlicker", "fenian", "kafir", "taig", "yid", "iberianslap", "middlefinger", "twofingerswithtongue", "twofingers", "nonce", "prickteaser", "rapey", "slag", "tart", "coffindodger", "old bag", "frenchify", "bescumber", "microphallus", "coccydynia", "ninnyhammer", "buncombe", "hircismus", "corpulent", "feist", "fice", "cacafuego", "assfuck", "assfaces", "assmucus", "bang(one's)box", "bastards", "beefcurtain", "bitchtit", "blowme", "blowmud", "bluewaffle", "blumpkin", "bustaload", "buttfuck", "choade", "chotabags", "clitlicker", "clittylitter", "cockpocket", "cocksnot", "cocksuck", "cocksucked", "cocksuckers", "cocksucks", "copsomewood", "cornhole", "corpwhore", "cumchugger", "cumdumpster", "cumfreak", "cumguzzler", "cumdump", "cunthair", "cuntbag", "cuntlick", "cuntlicker", "cuntlicking", "cuntsicle", "cunt-struck", "cutrope", "cyberfuck", "cyberfucked", "cyberfucking", "dickhole", "dickshy", "dickheads", "dirtySanchez", "eatadick", "eathairpie", "ejaculates", "ejaculating", "facial", "faggots", "fingerfuck", "fingerfucked", "fingerfucker", "fingerfucking", "fingerfucks", "fistfuck", "fistfucked", "fistfucker", "fistfuckers", "fistfucking", "fistfuckings", "fistfucks", "flogthelog", "fuc", "fuckhole", "fuckpuppet", "fuck trophy   ", "fuck yo mama   ", "fuck   ", "fuck-ass   ", "fuck-bitch   ", "fuckedup", "fuckme ", "fuckmeat   ", "fucktoy   ", "fukkers", "fuq", "gang-bang   ", "gassy ass", "ham flap   ", "how to murdep", "jackasses", "jiz ", "jizm ", "kinky Jesus   ", "kwif   ", "LEN", "mafugly   ", "mothafucked ", "mothafucking ", "mother fucker   ", "muff puff   ", "need the dick   ", "nut butter   ", "pisses", "pissin", "pissoff", "pussyfart", "pussypalace", "queaf", "sandbar", "sausagequeen", "shit fucker", "shitheads", "shitters", "shittier", "slope", "slut bucket", "smartass", "smartasses", "tit wank", "tities", "wiseass", "wiseasses", "boong", "coonnass", "darn", "Breeder", "Cocklump", "Doublelift", "Dumbcunt", "Fuck off", "Poopuncher", "Sandler", "cockeye", "crotte", "cus", "foah", "fucktwat", "jaggi", "kunja", "pust", "sanger", "seks", "zubb", "zibbi", "MildCholester", "proxtitute"
	};

	private static readonly string[] RootWords =
	{
		"fuck","shit","cunt","dick","cock","pussy","bitch","asshole","nigger","nigga","faggot","retard","rape","porn","sex","cum","jizz","twat","wank","prick","bollock","anus","anal","vagina","penis","boob","tit","kys","suicide","hitler","nazi","swastika","futa"
	};

	private static readonly HashSet<string> SafeWords = new HashSet<string>
	{
		"class", "classes", "classic", "classical", "classroom", "classmate", "classmates", "classify", "classified", "classification", "classicality", "assistant", "assistants", "assistance", "assist", "assisted", "assisting", "assignment", "assignments", "assign", "assigned", "assigning", "assess", "assessed", "assessing", "assessment", "assessments", "pass", "passes", "passing", "passage", "passages", "passport", "password", "passwords", "passcode", "passphrase", "passive", "passively", "passivity", "grass", "grasses", "grassland", "grasshopper", "glass", "glasses", "glassware", "glasshouse", "mass", "masses", "massive", "massively", "compass", "compasses", "brass", "canvas", "canvass", "bass", "bassline", "passenger", "passengers", "surpass", "surpassed", "surpassing", "trespass", "trespassed", "trespassing", "assorted", "assortment", "assurance", "assured", "embassy", "compartment", "document", "documents", "documentation", "documentary", "cumulative", "accumulate", "accumulated", "accumulating", "accumulation", "circumference", "circumvent", "circumvented", "vacuum", "vacuumed", "vacuuming", "attitude", "attitudes", "latitude", "altitude", "gratitude", "entitle", "entitled", "entitlement", "petition", "petitions", "petitioned", "competition", "competitor", "competitive", "quantity", "quantities", "utility", "utilities", "exhibit", "exhibits", "exhibition", "habit", "habits", "orbit", "orbits", "rabbit", "rabbits", "debit", "credit", "credits", "submit", "submitted", "submitting", "submission", "analysis", "analyses", "analyst", "analysts", "analytics", "signal", "signals", "regional", "final", "finally", "finalist", "finalists", "dictionary", "dictionaries", "verdict", "verdicts", "predict", "prediction", "predictable", "indicate", "indicated", "indicating", "indicator", "indicators", "addiction", "addictive", "legacy", "legacies", "engage", "engaged", "engagement", "language", "languages", "convey", "conveyed", "conveying", "assault", "assaulted", "assaulting", "assaults", "assassin", "assassins", "assassinate", "assassinated", "assassination", "companion", "companions", "character", "characters", "attribute", "attributes", "ability", "abilities", "experience", "inventory", "equipment", "legend", "legends", "legendary", "dragon", "dragons", "shadow", "shadows", "knight", "knights", "myth", "mythic", "phantom", "reaper", "sniper", "warrior", "guardian", "hero", "heroes", "mage", "archer", "ranger", "tank", "healer", "fighter", "battle", "battles", "finalbattle", "finalboss", "quest", "quests", "skill", "skills", "skilltree", "level", "levels", "rank", "ranking", "ranked", "player", "players", "clan", "clans", "team", "teams", "match", "matches", "victory", "defeat", "damage", "health", "shield", "armor", "weapon", "weapons", "attack", "defense", "speed", "power", "score", "session", "sessions", "asset", "assets", "assert", "assertion", "association", "associate", "associated", "message", "messages"
	};

	private static readonly Dictionary<string, HashSet<char>> LeetMap = new Dictionary<string, HashSet<char>>();

	static BadWordsUtil()
	{
		void Add( string key, char value )
		{
			if ( !LeetMap.TryGetValue( key, out var set ) )
				LeetMap[ key ] = set = new HashSet<char>();
			set.Add( value );
		}

		Add( "4", 'a' );
		Add( "/\\", 'a' );
		Add( "@", 'a' );
		Add( "/-\\", 'a' );
		Add( "^", 'a' );
		Add( "8", 'b' );
		Add( "|3", 'b' );
		Add( "13", 'b' );
		Add( "ß", 'b' );
		Add( "!3", 'b' );
		Add( "(3", 'b' );
		Add( "/3", 'b' );
		Add( ")3", 'b' );
		Add( "[", 'c' );
		Add( "(", 'c' );
		Add( "<", 'c' );
		Add( "¢", 'c' );
		Add( "©", 'c' );
		Add( "|)", 'd' );
		Add( "(|", 'd' );
		Add( "[)", 'd' );
		Add( "|>", 'd' );
		Add( "I>", 'd' );
		Add( "3", 'e' );
		Add( "&", 'e' );
		Add( "€", 'e' );
		Add( "£", 'e' );
		Add( "[-", 'e' );
		Add( "|=", 'e' );
		Add( "|=", 'f' );
		Add( "|#", 'f' );
		Add( "ph", 'f' );
		Add( "/=", 'f' );
		Add( "6", 'g' );
		Add( "9", 'g' );
		Add( "(_+", 'g' );
		Add( "C-", 'g' );
		Add( "#", 'h' );
		Add( "/-/", 'h' );
		Add( "[-]", 'h' );
		Add( "]-[", 'h' );
		Add( "|-|", 'h' );
		Add( "}{", 'h' );
		Add( "!-!", 'h' );
		Add( "1-1", 'h' );
		Add( "1", 'i' );
		Add( "|", 'i' );
		Add( "!", 'i' );
		Add( "._|", 'j' );
		Add( "_]", 'j' );
		Add( ",_|", 'j' );
		Add( ">|", 'k' );
		Add( "|<", 'k' );
		Add( "1<", 'k' );
		Add( "|c", 'k' );
		Add( "|_", 'l' );
		Add( "7", 'l' );
		Add( "2", 'l' );
		Add( "/\\/\\", 'm' );
		Add( "|\\/|", 'm' );
		Add( "^^", 'm' );
		Add( "(v)", 'm' );
		Add( "(V)", 'm' );
		Add( "|\\|", 'n' );
		Add( "/\\/", 'n' );
		Add( "[\\]", 'n' );
		Add( "^/", 'n' );
		Add( "0", 'o' );
		Add( "()", 'o' );
		Add( "[]", 'o' );
		Add( "<>", 'o' );
		Add( "Ø", 'o' );
		Add( "|*", 'p' );
		Add( "|o", 'p' );
		Add( "|>", 'p' );
		Add( "[]D", 'p' );
		Add( "(_,)", 'q' );
		Add( "()_", 'q' );
		Add( "0_", 'q' );
		Add( "<|", 'q' );
		Add( "|2", 'r' );
		Add( "12", 'r' );
		Add( "|~", 'r' );
		Add( "|?", 'r' );
		Add( "/2", 'r' );
		Add( "5", 's' );
		Add( "$", 's' );
		Add( "z", 's' );
		Add( "§", 's' );
		Add( "7", 't' );
		Add( "+", 't' );
		Add( "-|", 't' );
		Add( "(_)", 'u' );
		Add( "|_|", 'u' );
		Add( "v", 'u' );
		Add( "L|", 'u' );
		Add( "\\/", 'v' );
		Add( "|/", 'v' );
		Add( "\\|", 'v' );
		Add( "\\/\\/", 'w' );
		Add( "vv", 'w' );
		Add( "\\^/", 'w' );
		Add( "\\|/", 'w' );
		Add( "uu", 'w' );
		Add( "><", 'x' );
		Add( ")(", 'x' );
		Add( "×", 'x' );
		Add( "`/", 'y' );
		Add( "\\|/", 'y' );
		Add( "¥", 'y' );
		Add( "7_", 'z' );
		Add( "-/", 'z' );

		NormalizedBadWords = new HashSet<string>( AllBadWords.Select( Normalize ) );
	}

	public static HashSet<string> AllBadWords = BuildBadWords();
	public static HashSet<string> NameSafeWords = BuildSafeNameWords();

	private static HashSet<string> NormalizedBadWords;

	public static bool ContainsBadWord( string input )
	{
		return FindBadWord( input ) != null;
	}

	public static string Censor( string input )
	{
		if ( string.IsNullOrEmpty( input ) )
			return input;

		string lowered = input.ToLower();
		char[] chars = input.ToCharArray();
		bool[] mask = new bool[ input.Length ];

		var norm = new StringBuilder();
		var indexMap = new List<int>();

		for ( int i = 0; i < lowered.Length; i++ )
		{
			var variants = ReplaceLeetAll( lowered[ i ].ToString() );
			char c = variants[ 0 ][ 0 ];

			if ( c >= 'a' && c <= 'z' )
			{
				norm.Append( c );
				indexMap.Add( i );
			}
		}

		string normalized = norm.ToString();

		foreach ( string bad in NormalizedBadWords )
		{
			int start = 0;

			while ( ( start = normalized.IndexOf( bad, start, StringComparison.Ordinal ) ) != -1 )
			{
				int end = start + bad.Length - 1;

				int originalStart = indexMap[ start ];
				int originalEnd = indexMap[ end ];

				if ( IsSafeContext( lowered, originalStart, originalEnd ) )
				{
					start++;
					continue;
				}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
				string originalFragment = input.Substring( originalStart, originalEnd - originalStart + 1 );
				Debug.Log( $"[CENSOR] Input: {input} Normalized: {normalized} MatchedBadWord: {bad} OriginalFragment: {originalFragment} Indices: {originalStart}-{originalEnd}" );
#endif

				for ( int i = originalStart; i <= originalEnd; i++ )
					mask[ i ] = true;

				start++;
			}
		}

		for ( int i = 0; i < chars.Length; i++ )
		{
			if ( mask[ i ] )
				chars[ i ] = '*';
		}

		string safeOutput = new string( chars );

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		Debug.Log( $"[CENSOR] SafeOutput: {safeOutput}" );
#endif

		return safeOutput;
	}

	private static string FindBadWord( string input )
	{
		if ( string.IsNullOrEmpty( input ) )
			return null;

		string lowered = input.ToLower();
		string normalized = Normalize( lowered );

		foreach ( string bw in AllBadWords )
		{
			if ( normalized.Contains( Normalize( bw ) ) )
			{
				if ( !IsSafe( lowered ) )
					return bw;
			}
		}

		foreach ( string root in RootWords )
		{
			if ( normalized.Contains( root ) )
			{
				if ( !IsSafe( lowered ) )
					return root;
			}
		}

		return null;
	}

	private static bool IsSafeContext( string text, int start, int end )
	{
		int left = start;
		int right = end;

		while ( left > 0 && char.IsLetter( text[ left - 1 ] ) )
			left--;

		while ( right < text.Length - 1 && char.IsLetter( text[ right + 1 ] ) )
			right++;

		string word = text.Substring( left, right - left + 1 );

		// If the full word is safe, don't censor
		if ( SafeWords.Contains( word ) )
			return true;

		if ( NameSafeWords.Contains( word ) )
			return true;

		return false;
	}


	private static string Normalize( string text )
	{
		text = text.ToLower();

		text = ReplaceLeetAll( text )[ 0 ];

		text = Regex.Replace( text, @"(.)\1{2,}", "$1$1" );
		text = Regex.Replace( text, @"[\p{Mn}\p{Me}\u200B-\u200D\uFEFF]", "" );
		text = Regex.Replace( text, @"[^a-z]", "" );

		return text;
	}

	private static List<string> ReplaceLeetAll( string text )
	{
		var patterns = LeetMap.Keys.OrderByDescending( k => k.Length ).ToList();
		List<string> results = new() { "" };
		int i = 0;

		while ( i < text.Length )
		{
			bool matched = false;

			foreach ( var p in patterns )
			{
				if ( i + p.Length <= text.Length && text.AsSpan( i, p.Length ).SequenceEqual( p ) )
				{
					var newResults = new List<string>();

					foreach ( var r in results )
						foreach ( var c in LeetMap[ p ] )
							newResults.Add( r + c );

					results = newResults;
					i += p.Length;
					matched = true;
					break;
				}
			}

			if ( !matched )
			{
				for ( int r = 0; r < results.Count; r++ )
					results[ r ] += text[ i ];

				i++;
			}

			if ( results.Count > 64 )
				results = results.Take( 64 ).ToList();
		}

		return results.OrderBy( s => s.Length ).ThenBy( s => s ).ToList();
	}

	private static bool IsSafe( string text )
	{
		foreach ( string safe in SafeWords )
		{
			if ( text.Contains( safe ) )
				return true;
		}
		return false;
	}

	public static HashSet<string> BuildBadWords()
	{
		var set = new HashSet<string>();

		foreach ( string root in ManualBadWords )
			set.Add( root );

		foreach ( string root in RootWords )
			set.Add( root );

		return set;
	}

	public static HashSet<string> BuildSafeNameWords()
	{
		var set = new HashSet<string>();

		foreach ( string name in FunkCloudUser.firstName )
			set.Add( name.ToLower() );

		foreach ( string name in FunkCloudUser.lastName )
			set.Add( name.ToLower() );

		return set;
	}
}

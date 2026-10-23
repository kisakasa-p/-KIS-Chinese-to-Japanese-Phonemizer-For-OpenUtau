using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace ChineseToJapanesePhonemizer
{
    [Phonemizer("Chinese to Japanese Phonemizer", "ZH to JA", author: "KISAKASA", language: "ZH")]
    public class ChineseToJapanesePhonemizer : Phonemizer
    {
        private USinger singer;
        private bool useRomajiMode = false;
        private string CurrentPinyin = null;

        private static Dictionary<string, List<string[]>> PinyinMapAlternatives = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int[]> PinyinWeightOverrides = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        // ========== 音头保护配置 ==========
        private static bool onsetProtectionEnabled = false;
        private static List<string> onsetProtectionObjects = new List<string>();
        private static int onsetProtectionMagnitude = 10;

        // ========== 音头断音标记 ==========
        private const char HEAD_BREAK_CHAR = '\'';

        // ========== 回退规则静态表 ==========
        private static readonly Dictionary<string, string> SmallKanaToLarge = new Dictionary<string, string>
        {
            { "ぁ", "あ" }, { "ぃ", "い" }, { "ぅ", "う" }, { "ぇ", "え" }, { "ぉ", "お" },
            { "ゃ", "や" }, { "ゅ", "ゆ" }, { "ょ", "よ" },
            { "ゎ", "わ" }, { "ヵ", "か" }, { "ヶ", "け" },
            { "ァ", "ア" }, { "ィ", "イ" }, { "ゥ", "ウ" }, { "ェ", "エ" }, { "ォ", "オ" },
            { "ャ", "ヤ" }, { "ュ", "ユ" }, { "ョ", "ヨ" }, { "ヮ", "ワ" }
        };

        private static readonly Dictionary<string, string> SmallKanaToVowel = new Dictionary<string, string>
        {
            { "ぁ", "あ" }, { "ぃ", "い" }, { "ぅ", "う" }, { "ぇ", "え" }, { "ぉ", "お" },
            { "ゃ", "や" }, { "ゅ", "ゆ" }, { "ょ", "よ" },
            { "ゎ", "わ" },
            { "ァ", "ア" }, { "ィ", "イ" }, { "ゥ", "ウ" }, { "ェ", "エ" }, { "ォ", "オ" },
            { "ャ", "ヤ" }, { "ュ", "ユ" }, { "ョ", "ヨ" }, { "ヮ", "ワ" }
        };

        private static readonly HashSet<string> UDanKana = new HashSet<string>
        {
            "う", "く", "ぐ", "す", "ず", "つ", "づ", "ぬ", "ふ", "ぶ", "ぷ", "む", "ゆ", "る",
            "ウ", "ク", "グ", "ス", "ズ", "ツ", "ヅ", "ヌ", "フ", "ブ", "プ", "ム", "ユ", "ル"
        };

        private const string DEFAULT_PINYIN_MAP =
@"ONSET_PROTECTION=TRUE
OBJECT(S)=し,ふ,sha,shi,shu,she,sho,fa,fi,fu,fe,fo
MAGNITUDE=50

a=あ
o=お
e=お
er=あ,う,85,15

ai=あ,い,85,15
ei=え,い,85,15
ao=あ,お,85,15
ou=お,う,85,15
an=あ,ん,85,15
en=お,ん,85,15
ang=あ,ん,85,15
eng=お,ん,85,15

ya=や
yo=よ
yao=や,お,85,15
you=よ,う,90,10
yan=いぇ,ん,85,15
yang=や,ん,85,15
yin=い,ん,85,15
ying=い,ん,85,15
yong=よ,ん,85,15
ye=いぇ
yi=い
yu=ゆ
yue=ゆ,え,15,85
yuan=ゆ,え,ん,15,75,10
yun=ゆ,ん,90,10

wa=わ
wo=うぉ
wai=わ,い,85,15
wei=うぇ,い,85,15
wan=わ,ん,85,15
wen=う,お,ん,80,10,10
wang=わ,ん,85,15
weng=う,お,ん,80,10,10
wu=う

ba=ば
bo=ぶ,お,16,84
bi=び
bu=ぶ
bai=ば,い,85,15
bei=べ,い,85,15
bao=ば,お,85,15
bie=びぇ,15,85
biao=びゃ,お,85,15
bian=びぇ,ん,85,15
bin=び,ん,85,15
bing=び,ん,85,15
ben=ぼ,ん,85,15
beng=ぼ,ん,85,15
ban=ば,ん,85,15
bang=ば,ん,85,15

pa=ぱ
po=ぷ,お,16,84
pou=ぽ,う,90,10
pi=ぴ
pu=ぷ
pai=ぱ,い,85,15
pei=ぺ,い,85,15
pao=ぱ,お,85,15
pie=ぴぇ
piao=ぴゃ,お,85,15
pian=ぴぇ,ん,85,15
pin=ぴ,ん,85,15
ping=ぴ,ん,85,15
pen=ぽ,ん,85,15
peng=ぽ,ん,85,15
pan=ぱ,ん,85,15
pang=ぱ,ん,85,15

ma=ま
mo=む,お,13,87
me=も
mi=み
mu=む
mai=ま,い,85,15
mei=め,い,85,15
mao=ま,お,85,15
mou=も,う,90,10
mie=みぇ
miao=みゃ,お,85,15
miu=みょ,う,90,10
mian=みぇ,ん,85,15
min=み,ん,85,15
ming=み,ん,85,15
man=ま,ん,85,15
men=も,ん,85,15
mang=ま,ん,85,15
meng=も,ん,85,15

fa=ふぁ
fo=ふぉ
fei=ふぇ,い,85,15
fou=ふぉ,う,85,15
fan=ふぁ,ん,85,15
fen=ふぉ,ん,85,15
fang=ふぁ,ん,85,15
feng=ふぉ,ん,85,15
fu=ふ

da=だ
de=ど
di=でぃ
du=どぅ
duo=どぅ,お,15,85
dui=どぅ,え,い,15,75,10
duan=どぅ,あ,ん,15,70,15
dun=どぅ,お,ん,20,60,20
dong=ど,ん,85,15
dai=だ,い,85,15
dei=で,い,85,15
dao=だ,お,85,15
dou=ど,う,90,10
dan=だ,ん,85,15
den=ど,ん,85,15
dang=だ,ん,85,15
deng=ど,ん,85,15
dia=でぃ,あ,15,85
dian=でぃ,え,ん,10,75,15
diao=でぃ,あ,お,10,75,15
die=でぃ,え,15,85
ding=でぃ,ん,85,15
diu=でぃ,よ,う,10,72,8

ta=た
te=た
ti=てぃ
tu=とぅ
tuo=とぅ,お,15,85
tui=とぅ,え,い,15,75,10
tuan=とぅ,あ,ん,15,70,15
tun=とぅ,お,ん,20,60,20
tong=と,ん,85,15
tai=た,い,85,15
tao=た,お,85,15
tou=と,う,90,10
tan=た,ん,85,15
tang=た,ん,85,15
teng=と,ん,85,15
tian=てぃ,え,ん,10,75,15
tiao=てぃ,あ,お,10,75,15
tie=てぃ,え,15,85
ting=てぃ,ん,85,15

na=な
ne=の
ni=に
nu=ぬ
nv=にゅ
nun=ぬ,お,ん,20,60,20
nvn=にゅ,ん
nuo=ぬ,お,15,85
nve=にゅ,え,15,85
nuan=ぬ,あ,ん,15,70,15
nong=の,ん,85,15
nai=な,い,85,15
nei=ね,い,85,15
nao=な,お,85,15
nou=の,う,90,10
nan=な,ん,85,15
nen=の,ん,85,15
nang=な,ん,85,15
neng=の,ん,85,15
niang=にゃ,ん,85,15
niao=にゃ,お,85,15
nie=にぇ,15,85
nin=に,ん,85,15
ning=に,ん,85,15
niu=にょ,う,90,10
nian=にぇ,ん,85,15

la=ら
lo=ろ
le=ろ
li=り
lu=る
lv=りゅ
luo=る,お,15,85
lve=りゅ,え,15,85
luan=る,あ,ん,15,70,15
lun=る,お,ん,20,60,20
long=ろ,ん,85,15
lai=ら,い,85,15
lei=れ,い,85,15
lao=ら,お,85,15
lou=ろ,う,90,10
lan=ら,ん,85,15
lang=ら,ん,85,15
leng=ろ,ん,85,15
lia=りゃ,85,15
lie=りぇ,15,85
liao=りゃ,お,85,15
lian=りぇ,ん,85,15
liang=りゃ,ん,85,15
lin=り,ん,85,15
ling=り,ん,85,15
liu=りょ,う,90,10

ga=が
ge=ご
gu=ぐ
gua=ぐぁ,20,80
guo=ぐぉ,20,80
guai=ぐぁ,い,85,15
guan=ぐぁ,ん,85,15
guang=ぐぁ,ん,85,15
gui=ぐぇ,い,85,15
gun=ぐぉ,ん,85,15
gong=ご,ん,85,15
gai=が,い,85,15
gei=げ,い,85,15
gao=が,お,85,15
gou=ご,う,90,10
gan=が,ん,85,15
gen=ご,ん,85,15
gang=が,ん,85,15
geng=ご,ん,85,15

ka=か
ke=か
ku=く
kua=くぁ
kuo=くぉ,20,80
kuai=くぁ,い,85,15
kuan=くぁ,ん,85,15
kuang=くぁ,ん,85,15
kui=くぇ,い,85,15
kun=くぉ,ん,85,15
kong=こ,ん,85,15
kai=か,い,85,15
kei=け,い,85,15
kao=か,お,85,15
kou=こ,う,90,10
kan=か,ん,85,15
ken=こ,ん,85,15
kang=か,ん,85,15
keng=こ,ん,85,15

ha=は
he=ほ
hu=ほ,う,5,95
hua=ほ,わ,5,95
huo=ほ,うぉ,5,95
huai=ほ,わ,い,5,80,15
huan=ほ,わ,ん,5,80,15
huang=ほ,わ,ん,5,80,15
hui=ほ,うぇ,い,5,80,15
hun=ほ,うぇ,ん,5,80,15
hong=ほ,ん,85,15
hai=は,い,85,15
hei=へ,い,85,15
hao=は,お,85,15
hou=ほ,う,90,10
han=は,ん,85,15
hen=は,ん,85,15
hang=は,ん,85,15
heng=ほ,ん,85,15

ji=じ
ju=じゅ
jia=じゃ
jie=じぇ
jiao=じゃ,お,90,10
jiu=じょ,う,90,10
jian=じぇ,ん,85,15
jiang=じゃ,ん,90,10
jiong=じょ,ん,90,10
juan=じゅ,え,ん,15,70,15
jue=じゅ,え,15,85
jun=じゅ,ん,85,15
jin=じ,ん,85,15
jing=じ,ん,85,15

qi=ち
qu=ちゅ
qia=ちゃ
qie=ちぇ
qiao=ちゃ,お,85,15
qiu=ちょ,う,90,10
qian=ちぇ,ん,85,15
qiang=ちゃ,ん,85,15
qiong=ちょ,ん,85,15
quan=ちゅ,え,ん,15,70,15
que=ちゅ,え,15,85
qun=ちゅ,ん,85,15
qin=ち,ん,85,15
qing=ち,ん,85,15

xi=し
xu=しゅ
xia=しゃ
xie=しぇ
xiao=しゃ,お,85,15
xiu=しょ,う,90,10
xian=しぇ,ん,85,15
xiang=しゃ,ん,85,15
xiong=しょ,ん,85,15
xuan=しゅ,え,ん,15,70,15
xue=しゅ,え,15,85
xun=しゅ,ん,85,15
xin=し,ん,85,15
xing=し,ん,85,15

za=ざ
ze=ぞ
zu=ず,う,10,90
zuo=ず,お,15,85
zai=ざ,い,85,15
zei=ぜ,い,85,15
zao=ざ,お,85,15
zou=ぞ,う,90,10
zan=ざ,ん,85,15
zen=ぞ,ん,85,15
zang=ざ,ん,85,15
zeng=ぞ,ん,85,15
zong=ぞ,ん,85,15
zuan=ず,あ,ん,15,70,15
zui=ず,え,い,15,70,15
zun=ず,お,ん,15,70,15

ca=つぁ
ce=つぉ
cu=つ,う,10,90
cuo=つ,お,15,85
cai=つぁ,い,85,15
cao=つぁ,お,85,15
cou=つぉ,う,90,10
can=つぁ,ん,85,15
cen=つぉ,ん,85,15
cang=つぁ,ん,85,15
ceng=つぉ,ん,85,15
cong=つぉ,ん,85,15
cuan=つ,わ,ん,15,70,15
cui=つ,え,い,15,70,15
cun=つ,お,ん,15,70,15

sa=さ
se=そ
su=す,う,10,90
suo=す,お,15,85
sai=さ,い,85,15
sao=さ,お,85,15
sou=そ,う,90,10
san=さ,ん,85,15
sen=そ,ん,85,15
sang=さ,ん,85,15
seng=そ,ん,85,15
song=そ,ん,85,15
suan=す,あ,ん,15,70,15
sui=す,え,い,15,70,15
sun=す,お,ん,15,70,15

zhi=じゅ
zha=じゃ
zhe=じょ
zhu=じゅ,う,10,90
zhuo=じゅ,お,15,85
zhai=じゃ,い,85,15
zhei=じぇ,い,85,15
zhao=じゃ,お,85,15
zhou=じょ,う,90,10
zhan=じゃ,ん,85,15
zhen=じょ,ん,85,15
zhang=じゃ,ん,85,15
zheng=じょ,ん,85,15
zhong=じょ,ん,85,15
zhua=じゅ,わ,15,85
zhuai=じゅ,わ,い,10,80,10
zhuan=じゅ,わ,ん,15,70,15
zhuang=じゅ,わ,ん,15,70,15
zhui=じゅ,え,い,15,70,15
zhun=じゅ,お,ん,15,70,15

chi=ちゅ
cha=ちゃ
che=ちょ
chu=ちゅ,う,10,90
chuo=ちゅ,お,15,85
chai=ちゃ,い,85,15
chao=ちゃ,お,85,15
chou=ちょ,う,90,10
chan=ちゃ,ん,85,15
chen=ちょ,ん,85,15
chang=ちゃ,ん,85,15
cheng=ちょ,ん,85,15
chong=ちょ,ん,85,15
chua=ちゅ,わ,15,85
chuai=ちゅ,わ,い,10,80,10
chuan=ちゅ,わ,ん,15,70,15
chuang=ちゅ,わ,ん,15,70,15
chui=ちゅ,え,い,15,70,15
chun=ちゅ,お,ん,15,70,15

shi=しゅ
sha=しゃ
she=しょ
shu=しゅ,う,10,90
shuo=しゅ,お,15,85
shai=しゃ,い,85,15
shei=しぇ,い,85,15
shao=しゃ,お,85,15
shou=しょ,う,90,10
shan=しゃ,ん,85,15
shen=しょ,ん,85,15
shang=しゃ,ん,85,15
sheng=しょ,ん,85,15
shua=しゅ,わ,15,85
shuai=しゅ,わ,い,10,80,10
shuan=しゅ,わ,ん,15,70,15
shuang=しゅ,わ,ん,15,70,15
shui=しゅ,え,い,15,70,15
shun=しゅ,お,ん,15,70,15

ri=う
re=うぉ
ru=う,う,5,95
ruo=う,お,15,85
rao=わ,お,85,15
rou=うぉ,う,90,10
ran=わ,ん,85,15
ren=うぉ,ん,85,15
rang=わ,ん,85,15
reng=うぉ,ん,85,15
rong=うぉ,ん,85,15
rua=う,わ,15,85
ruan=う,わ,ん,15,70,15
rui=う,い,15,85
run=う,え,ん,15,70,15

zi=ず
ci=つ
si=す
";

        // ========== 元音/罗马字映射 ==========
        private static readonly Dictionary<string, string> VowelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "あ", "a" }, { "か", "a" }, { "が", "a" }, { "さ", "a" }, { "ざ", "a" },
            { "た", "a" }, { "だ", "a" }, { "な", "a" }, { "は", "a" }, { "ば", "a" },
            { "ぱ", "a" }, { "ま", "a" }, { "や", "a" }, { "ら", "a" }, { "わ", "a" },
            { "ゃ", "a" }, { "ぁ", "a" }, { "ゎ", "a" },
            { "い", "i" }, { "き", "i" }, { "ぎ", "i" }, { "し", "i" }, { "じ", "i" },
            { "ち", "i" }, { "に", "i" }, { "ひ", "i" }, { "び", "i" }, { "ぴ", "i" },
            { "み", "i" }, { "り", "i" }, { "ぃ", "i" }, { "ィ", "i" },
            { "う", "u" }, { "く", "u" }, { "ぐ", "u" }, { "す", "u" }, { "ず", "u" },
            { "つ", "u" }, { "づ", "u" }, { "ぬ", "u" }, { "ふ", "u" }, { "ぶ", "u" },
            { "ぷ", "u" }, { "む", "u" }, { "ゆ", "u" }, { "る", "u" }, { "ぅ", "u" },
            { "ゅ", "u" }, { "ゥ", "u" }, { "ュ", "u" },
            { "え", "e" }, { "け", "e" }, { "せ", "e" }, { "ぜ", "e" },
            { "て", "e" }, { "で", "e" }, { "ね", "e" }, { "へ", "e" }, { "べ", "e" },
            { "ぺ", "e" }, { "め", "e" }, { "れ", "e" }, { "ぇ", "e" }, { "ェ", "e" },
            { "お", "o" }, { "こ", "o" }, { "ご", "o" }, { "そ", "o" }, { "ぞ", "o" },
            { "と", "o" }, { "ど", "o" }, { "の", "o" }, { "ほ", "o" }, { "ぼ", "o" },
            { "ぽ", "o" }, { "も", "o" }, { "よ", "o" }, { "ろ", "o" }, { "を", "o" },
            { "ょ", "o" }, { "ぉ", "o" }, { "ョ", "o" }, { "ォ", "o" },
            { "ん", "n" }, { "ン", "n" }
        };

        private static readonly Dictionary<string, string> RomajiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "あ", "a" }, { "い", "i" }, { "う", "u" }, { "え", "e" }, { "お", "o" },
            { "か", "ka" }, { "き", "ki" }, { "く", "ku" }, { "け", "ke" }, { "こ", "ko" },
            { "が", "ga" }, { "ぎ", "gi" }, { "ぐ", "gu" }, { "げ", "ge" }, { "ご", "go" },
            { "さ", "sa" }, { "し", "shi" }, { "す", "su" }, { "せ", "se" }, { "そ", "so" },
            { "ざ", "za" }, { "じ", "ji" }, { "ず", "zu" }, { "ぜ", "ze" }, { "ぞ", "zo" },
            { "た", "ta" }, { "ち", "chi" }, { "つ", "tsu" }, { "て", "te" }, { "と", "to" },
            { "だ", "da" }, { "ぢ", "di" }, { "づ", "du" }, { "で", "de" }, { "ど", "do" },
            { "な", "na" }, { "に", "ni" }, { "ぬ", "nu" }, { "ね", "ne" }, { "の", "no" },
            { "は", "ha" }, { "ひ", "hi" }, { "ふ", "fu" }, { "へ", "he" }, { "ほ", "ho" },
            { "ば", "ba" }, { "び", "bi" }, { "ぶ", "bu" }, { "べ", "be" }, { "ぼ", "bo" },
            { "ぱ", "pa" }, { "ぴ", "pi" }, { "ぷ", "pu" }, { "ぺ", "pe" }, { "ぽ", "po" },
            { "ま", "ma" }, { "み", "mi" }, { "む", "mu" }, { "め", "me" }, { "も", "mo" },
            { "や", "ya" }, { "ゆ", "yu" }, { "よ", "yo" },
            { "ら", "ra" }, { "り", "ri" }, { "る", "ru" }, { "れ", "re" }, { "ろ", "ro" },
            { "わ", "wa" }, { "を", "wo" }, { "ん", "n" },
            { "きゃ", "kya" }, { "きゅ", "kyu" }, { "きょ", "kyo" },
            { "しゃ", "sha" }, { "しゅ", "shu" }, { "しょ", "sho" },
            { "ちゃ", "cha" }, { "ちゅ", "chu" }, { "ちょ", "cho" },
            { "にゃ", "nya" }, { "にゅ", "nyu" }, { "にょ", "nyo" },
            { "ひゃ", "hya" }, { "ひゅ", "hyu" }, { "ひょ", "hyo" },
            { "みゃ", "mya" }, { "みゅ", "myu" }, { "みょ", "myo" },
            { "りゃ", "rya" }, { "りゅ", "ryu" }, { "りょ", "ryo" },
            { "ぎゃ", "gya" }, { "ぎゅ", "gyu" }, { "ぎょ", "gyo" },
            { "じゃ", "ja" }, { "じゅ", "ju" }, { "じょ", "jo" },
            { "びゃ", "bya" }, { "びゅ", "byu" }, { "びょ", "byo" },
            { "ぴゃ", "pya" }, { "ぴゅ", "pyu" }, { "ぴょ", "pyo" },
            { "きぇ", "kye" }, { "しぇ", "she" }, { "ちぇ", "che" },
            { "にぇ", "nye" }, { "ひぇ", "hye" }, { "みぇ", "mye" },
            { "りぇ", "rye" }, { "ぎぇ", "gye" }, { "じぇ", "je" },
            { "びぇ", "bye" }, { "ぴぇ", "pye" },
            { "でぃ", "di" }, { "てぃ", "ti" }, { "どぅ", "du" }, { "とぅ", "tu" },
            { "ふぁ", "fa" }, { "ふぃ", "fi" }, { "ふぇ", "fe" }, { "ふぉ", "fo" },
            { "うぃ", "wi" }, { "うぇ", "we" }, { "うぉ", "wo" }, { "うぁ", "wa" },
            { "ヴ", "vu" }, { "いぇ", "ye" }, { "いぃ", "yi" }
        };

        private static readonly Dictionary<string, string> RomajiToKanaMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "a", "あ" }, { "i", "い" }, { "u", "う" }, { "e", "え" }, { "o", "お" },
            { "ka", "か" }, { "ki", "き" }, { "ku", "く" }, { "ke", "け" }, { "ko", "こ" },
            { "ga", "が" }, { "gi", "ぎ" }, { "gu", "ぐ" }, { "ge", "げ" }, { "go", "ご" },
            { "sa", "さ" }, { "shi", "し" }, { "su", "す" }, { "se", "せ" }, { "so", "そ" },
            { "za", "ざ" }, { "ji", "じ" }, { "zu", "ず" }, { "ze", "ぜ" }, { "zo", "ぞ" },
            { "ta", "た" }, { "chi", "ち" }, { "tsu", "つ" }, { "te", "て" }, { "to", "と" },
            { "da", "だ" }, { "di", "ぢ" }, { "du", "づ" }, { "de", "で" }, { "do", "ど" },
            { "na", "な" }, { "ni", "に" }, { "nu", "ぬ" }, { "ne", "ね" }, { "no", "の" },
            { "ha", "は" }, { "hi", "ひ" }, { "fu", "ふ" }, { "he", "へ" }, { "ho", "ほ" },
            { "ba", "ば" }, { "bi", "び" }, { "bu", "ぶ" }, { "be", "べ" }, { "bo", "ぼ" },
            { "pa", "ぱ" }, { "pi", "ぴ" }, { "pu", "ぷ" }, { "pe", "ぺ" }, { "po", "ぽ" },
            { "ma", "ま" }, { "mi", "み" }, { "mu", "む" }, { "me", "め" }, { "mo", "も" },
            { "ya", "や" }, { "yu", "ゆ" }, { "yo", "よ" },
            { "ra", "ら" }, { "ri", "り" }, { "ru", "る" }, { "re", "れ" }, { "ro", "ろ" },
            { "wa", "わ" }, { "wo", "を" }, { "n", "ん" },
            { "kya", "きゃ" }, { "kyu", "きゅ" }, { "kyo", "きょ" },
            { "sha", "しゃ" }, { "shu", "しゅ" }, { "sho", "しょ" },
            { "cha", "ちゃ" }, { "chu", "ちゅ" }, { "cho", "ちょ" },
            { "nya", "にゃ" }, { "nyu", "にゅ" }, { "nyo", "にょ" },
            { "hya", "ひゃ" }, { "hyu", "ひゅ" }, { "hyo", "ひょ" },
            { "mya", "みゃ" }, { "myu", "みゅ" }, { "myo", "みょ" },
            { "rya", "りゃ" }, { "ryu", "りゅ" }, { "ryo", "りょ" },
            { "gya", "ぎゃ" }, { "gyu", "ぎゅ" }, { "gyo", "ぎょ" },
            { "ja", "じゃ" }, { "ju", "じゅ" }, { "jo", "じょ" },
            { "bya", "びゃ" }, { "byu", "びゅ" }, { "byo", "びょ" },
            { "pya", "ぴゃ" }, { "pyu", "ぴゅ" }, { "pyo", "ぴょ" },
            { "kye", "きぇ" }, { "she", "しぇ" }, { "che", "ちぇ" },
            { "nye", "にぇ" }, { "hye", "ひぇ" }, { "mye", "みぇ" },
            { "rye", "りぇ" }, { "gye", "ぎぇ" }, { "je", "じぇ" },
            { "bye", "びぇ" }, { "pye", "ぴぇ" },
            { "fa", "ふぁ" }, { "fi", "ふぃ" }, { "fe", "ふぇ" }, { "fo", "ふぉ" },
            { "wi", "うぃ" }, { "we", "うぇ" },
            { "vu", "ヴ" }, { "ye", "いぇ" }, { "yi", "いぃ" },
            { "xtsu", "っ" }, { "ltu", "っ" },
            { "xn", "ん" }, { "nn", "ん" }
        };

        static ChineseToJapanesePhonemizer()
        {
            LoadBuiltinMaps(DEFAULT_PINYIN_MAP);
        }

        private static void LoadBuiltinMaps(string content)
        {
            var map = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
            var overrides = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            ParseMapContent(content, map, overrides);
            PinyinMapAlternatives = map;
            PinyinWeightOverrides = overrides;
        }

        private static void ParseMapContent(
            string content,
            Dictionary<string, List<string[]>> targetMap,
            Dictionary<string, int[]> targetOverrides)
        {
            if (string.IsNullOrEmpty(content)) return;

            onsetProtectionEnabled = false;
            onsetProtectionObjects.Clear();
            onsetProtectionMagnitude = 10;

            try
            {
                string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                bool inWeightsSection = false;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();

                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("#")) continue;

                    if (TryParseOnsetProtectionLine(line, out bool consumed) && consumed)
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        string section = line.Substring(1, line.Length - 2).Trim().ToLower();
                        inWeightsSection = (section == "weights");
                        continue;
                    }

                    if (inWeightsSection) continue;

                    int equalIndex = line.IndexOf('=');
                    if (equalIndex <= 0) continue;

                    string pinyin = line.Substring(0, equalIndex).Trim().ToLower();
                    string valuePart = line.Substring(equalIndex + 1).Trim();

                    if (string.IsNullOrEmpty(pinyin) || string.IsNullOrEmpty(valuePart))
                        continue;

                    string[] alternatives = valuePart.Split(new[] { "||" }, StringSplitOptions.None);
                    List<string[]> phonemeAlternatives = new List<string[]>();
                    int[] overrideWeights = null;

                    foreach (string alt in alternatives)
                    {
                        var rawParts = alt.Split(',')
                            .Select(p => p.Trim())
                            .Where(p => !string.IsNullOrEmpty(p))
                            .ToList();

                        List<string> phonemes = new List<string>();
                        List<int> weights = new List<int>();

                        foreach (var part in rawParts)
                        {
                            if (IsExplicitVcvOrKana(part))
                                phonemes.Add(part);
                            else if (int.TryParse(part, out int w))
                                weights.Add(w);
                        }

                        if (phonemes.Count > 0)
                        {
                            phonemeAlternatives.Add(phonemes.ToArray());

                            if (weights.Count == phonemes.Count && overrideWeights == null)
                                overrideWeights = weights.ToArray();
                        }
                    }

                    if (phonemeAlternatives.Count > 0)
                        targetMap[pinyin] = phonemeAlternatives;

                    if (overrideWeights != null)
                        targetOverrides[pinyin] = overrideWeights;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ZH to JA] 解析映射表失败");
            }
        }

        private static bool IsExplicitVcvOrKana(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (Regex.IsMatch(text, @"^[\u3040-\u309F\u30A0-\u30FF]+$")) return true;
            return Regex.IsMatch(text, @"^[a-zA-Z]\s+[\u3040-\u309F\u30A0-\u30FF]+$");
        }

        private static bool TryParseOnsetProtectionLine(string line, out bool consumed)
        {
            consumed = false;
            int eq = line.IndexOf('=');
            if (eq <= 0) return false;

            string key = line.Substring(0, eq).Trim().ToUpperInvariant();
            string value = line.Substring(eq + 1).Trim();

            switch (key)
            {
                case "ONSET_PROTECTION":
                    onsetProtectionEnabled = value.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                    consumed = true;
                    return true;

                case "OBJECT(S)":
                case "OBJECTS":
                case "OBJECT":
                    onsetProtectionObjects.Clear();
                    foreach (var part in value.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string p = part.Trim();
                        if (!string.IsNullOrEmpty(p))
                            onsetProtectionObjects.Add(p);
                    }
                    consumed = true;
                    return true;

                case "MAGNITUDE":
                    if (int.TryParse(value, out int mag) && mag > 0)
                        onsetProtectionMagnitude = mag;
                    consumed = true;
                    return true;

                default:
                    return false;
            }
        }

        private static bool IsKanaStatic(string text)
        {
            return Regex.IsMatch(text, @"^[\u3040-\u309F\u30A0-\u30FF]+$");
        }

        // ========== 显式 VCV 拆解 ==========
        private void SplitExplicitVcv(string phoneme, out string prefixVowel, out string body)
        {
            prefixVowel = null;
            body = phoneme;
            if (string.IsNullOrEmpty(phoneme)) return;

            var m = Regex.Match(phoneme, @"^([a-zA-Z])\s+([\u3040-\u309F\u30A0-\u30FF]+)$");
            if (m.Success)
            {
                prefixVowel = m.Groups[1].Value;
                body = m.Groups[2].Value;
            }
        }

        // ========== 音头断音解析 ==========
        private string ParseHeadBreak(string lyric, out bool headBreak)
        {
            headBreak = false;
            if (string.IsNullOrEmpty(lyric)) return lyric;

            int start = 0;
            while (start < lyric.Length && lyric[start] == HEAD_BREAK_CHAR)
            {
                headBreak = true;
                start++;
            }
            return start > 0 ? lyric.Substring(start) : lyric;
        }

        // ========== 音头保护命中检测 ==========
        private bool PhonemeHitsProtection(string phoneme)
        {
            if (!onsetProtectionEnabled || onsetProtectionObjects.Count == 0) return false;
            if (string.IsNullOrEmpty(phoneme)) return false;

            SplitExplicitVcv(phoneme, out _, out string body);
            string romaji = ConvertToRomaji(body) ?? body;

            foreach (var obj in onsetProtectionObjects)
            {
                if (body.IndexOf(obj, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (!string.IsNullOrEmpty(romaji) &&
                    romaji.IndexOf(obj, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private bool IsAdjacentForward(Note current, Note next)
        {
            return current.position + current.duration == next.position;
        }

        private bool NextNoteTriggersProtection(Note current, Note? next)
        {
            if (!onsetProtectionEnabled) return false;
            if (!next.HasValue) return false;
            if (!IsAdjacentForward(current, next.Value)) return false;

            string nextLyric = next.Value.lyric ?? "";
            if (string.IsNullOrEmpty(nextLyric)) return false;

            string firstPhoneme = PeekFirstPhoneme(nextLyric, next.Value);
            if (string.IsNullOrEmpty(firstPhoneme)) return false;

            return PhonemeHitsProtection(firstPhoneme);
        }

        private string PeekFirstPhoneme(string lyric, Note note)
        {
            if (string.IsNullOrEmpty(lyric)) return null;

            string cleanLyric = ParseHeadBreak(lyric, out _);
            if (string.IsNullOrEmpty(cleanLyric)) return null;

            if (IsDirectMapping(cleanLyric, note, out _, out string[] directPhonemes, out _))
                return directPhonemes.Length > 0 ? directPhonemes[0] : null;

            string trimmed = GetFirstChar(cleanLyric);
            if (string.IsNullOrEmpty(trimmed)) return null;

            if (Regex.IsMatch(trimmed, @"[\u3040-\u309F\u30A0-\u30FF]"))
                return trimmed;

            string pinyin = null;
            if (IsChineseCharacter(trimmed))
            {
                string p = ConvertToPinyin(trimmed);
                if (!string.IsNullOrEmpty(p) && p != trimmed) pinyin = p;
            }
            else if (IsPinyin(trimmed))
            {
                pinyin = trimmed;
            }

            if (pinyin != null
                && PinyinMapAlternatives.TryGetValue(pinyin.ToLower(), out var alts)
                && alts.Count > 0 && alts[0].Length > 0)
            {
                return alts[0][0];
            }

            return trimmed;
        }

        // ========== 音头保护：叠加到 double 权重上 ==========
        private void ApplyOnsetProtectionToDoubleWeights(Note note, List<string> phonemes, double[] weights, Note? nextNote)
        {
            if (!onsetProtectionEnabled || onsetProtectionObjects.Count == 0) return;
            if (phonemes == null || phonemes.Count < 2) return;
            if (weights == null || weights.Length != phonemes.Count) return;

            if (!NextNoteTriggersProtection(note, nextNote)) return;

            weights[weights.Length - 1] += onsetProtectionMagnitude / 100.0;
        }

        // ========== 回退候选 ==========
        private List<string[]> GetFallbackCandidates(string[] primary)
        {
            var result = new List<string[]>();

            var perPhonemeOptions = new List<List<string>>();
            foreach (var phoneme in primary)
                perPhonemeOptions.Add(GetPhonemeFallbackOptions(phoneme));

            var combinations = new List<string[]>();
            GenerateCombinations(perPhonemeOptions, 0, new List<string>(), combinations);

            combinations.Sort((a, b) =>
            {
                int da = GetDegradationLevel(a, primary);
                int db = GetDegradationLevel(b, primary);
                return da.CompareTo(db);
            });

            foreach (var combo in combinations)
            {
                if (!combo.SequenceEqual(primary))
                    result.Add(combo);
            }

            return result;
        }

        private List<string> GetPhonemeFallbackOptions(string phoneme)
        {
            var options = new List<string> { phoneme };

            SplitExplicitVcv(phoneme, out string explicitPrefix, out string body);

            if (!string.IsNullOrEmpty(explicitPrefix) && !string.IsNullOrEmpty(body))
            {
                if (!options.Contains(body)) options.Add(body);
                foreach (var opt in GetPhonemeFallbackOptions(body))
                {
                    if (!options.Contains(opt)) options.Add(opt);
                }
                return options;
            }

            if (phoneme.Length >= 2)
            {
                string lastChar = phoneme.Substring(phoneme.Length - 1);
                string head = phoneme.Substring(0, phoneme.Length - 1);

                if (SmallKanaToLarge.ContainsKey(lastChar))
                {
                    string split = head + " " + lastChar;
                    if (!options.Contains(split)) options.Add(split);

                    string vowelKana = SmallKanaToVowel[lastChar];
                    string degraded = head + " " + vowelKana;
                    if (!options.Contains(degraded)) options.Add(degraded);

                    if (UDanKana.Contains(head))
                    {
                        string uSplit = head + " う " + lastChar;
                        if (!options.Contains(uSplit)) options.Add(uSplit);

                        string uDegraded = head + " う " + vowelKana;
                        if (!options.Contains(uDegraded)) options.Add(uDegraded);
                    }
                }
            }

            return options;
        }

        private void GenerateCombinations(List<List<string>> options, int index, List<string> current, List<string[]> result)
        {
            if (index == options.Count)
            {
                result.Add(current.ToArray());
                return;
            }

            foreach (var opt in options[index])
            {
                current.Add(opt);
                GenerateCombinations(options, index + 1, current, result);
                current.RemoveAt(current.Count - 1);
            }
        }

        private int GetDegradationLevel(string[] combo, string[] primary)
        {
            int level = 0;
            for (int i = 0; i < combo.Length && i < primary.Length; i++)
            {
                if (combo[i] != primary[i])
                    level++;
            }
            return level;
        }

        private string[] ExpandPhonemeArray(string[] input)
        {
            var result = new List<string>();
            foreach (var item in input)
            {
                if (item.Contains(' '))
                    result.AddRange(item.Split(' ').Where(s => !string.IsNullOrEmpty(s)));
                else
                    result.Add(item);
            }
            return result.ToArray();
        }

        private string[] ProcessRomanizationWithCoda(string romanization, Note note)
        {
            if (string.IsNullOrEmpty(romanization)) return null;

            CurrentPinyin = romanization.ToLower();

            if (PinyinMapAlternatives.TryGetValue(romanization.ToLower(), out var alternatives))
            {
                foreach (var phonemes in alternatives)
                {
                    if (AllPhonemesHitOto(phonemes, note))
                        return phonemes;
                }

                foreach (var phonemes in alternatives)
                {
                    var fallbacks = GetFallbackCandidates(phonemes);
                    foreach (var fallback in fallbacks)
                    {
                        var expanded = ExpandPhonemeArray(fallback);
                        if (AllPhonemesHitOto(expanded, note))
                            return expanded;
                    }
                }

                if (alternatives.Count > 0)
                    return alternatives[0];
            }

            return new[] { romanization };
        }

        private bool AllPhonemesHitOto(string[] phonemes, Note note)
        {
            foreach (var phoneme in phonemes)
            {
                SplitExplicitVcv(phoneme, out string explicitPrefix, out string body);

                if (!string.IsNullOrEmpty(explicitPrefix))
                {
                    if (CheckOtoHit(phoneme, note, out _)) continue;
                    if (CheckOtoHit(body, note, out _)) continue;
                    return false;
                }

                string checkTarget = ExtractBasePhoneme(body);
                if (!CheckOtoHit(checkTarget, note, out _))
                    return false;
            }
            return true;
        }

        private bool IsVowel(string phoneme)
        {
            if (string.IsNullOrEmpty(phoneme)) return false;
            return phoneme == "a" || phoneme == "i" || phoneme == "u" || phoneme == "e" || phoneme == "o";
        }

        // ========== 权重（基础权重唯一来源） ==========
        private double GetPhonemeWeight(string phoneme, string pinyin, int indexInPhonemes, int totalPhonemes, bool hasExtender = false)
        {
            SplitExplicitVcv(phoneme, out _, out string basePhoneme);

            if (hasExtender && indexInPhonemes == totalPhonemes - 1)
            {
                if (basePhoneme == "ん" || basePhoneme == "n" || basePhoneme == "ng")
                    return 0.35;
                if (basePhoneme == "あ" || basePhoneme == "い" || basePhoneme == "う" ||
                    basePhoneme == "え" || basePhoneme == "お")
                    return 0.65;
            }

            string key = pinyin;
            if (!string.IsNullOrEmpty(key))
            {
                key = key.Trim('[', ']').ToLower();
                if (PinyinWeightOverrides.TryGetValue(key, out var ov) && ov.Length == totalPhonemes)
                {
                    int sum = 0;
                    foreach (var w in ov) sum += w;
                    if (sum > 0)
                        return ov[indexInPhonemes] / (double)sum;
                }
            }

            return 1.0 / totalPhonemes;
        }

        private double[] BuildBaseWeights(
            List<string> phonemes,
            int[] userWeights,
            List<string> perPhonemePinyin,
            List<int> perPhonemeIndex,
            List<int> perPhonemeTotal,
            bool hasExtender)
        {
            int count = phonemes.Count;
            double[] weights = new double[count];

            if (userWeights != null && userWeights.Length == count)
            {
                int sum = 0;
                foreach (var w in userWeights) sum += w;
                for (int i = 0; i < count; i++)
                    weights[i] = sum > 0 ? (double)userWeights[i] / sum : 1.0 / count;
                return weights;
            }

            for (int i = 0; i < count; i++)
            {
                string pinyin = (perPhonemePinyin != null && i < perPhonemePinyin.Count) ? perPhonemePinyin[i] : null;
                int idx = (perPhonemeIndex != null && i < perPhonemeIndex.Count) ? perPhonemeIndex[i] : i;
                int total = (perPhonemeTotal != null && i < perPhonemeTotal.Count) ? perPhonemeTotal[i] : count;
                weights[i] = GetPhonemeWeight(phonemes[i], pinyin, idx, total, hasExtender);
            }

            return weights;
        }

        // ========== 最终时长分配 ==========
        private Result SplitWithDoubleWeights(Note note, List<string> phonemes, double[] weights, int totalDuration)
        {
            int count = phonemes.Count;
            if (count == 0)
                return MakeSimpleResult("sil", note);

            double sumW = 0;
            foreach (var w in weights) sumW += w;
            if (sumW <= 0) sumW = count;

            int[] durations = new int[count];
            int sumDur = 0;
            for (int i = 0; i < count; i++)
            {
                durations[i] = (int)(totalDuration * weights[i] / sumW);
                sumDur += durations[i];
            }
            if (sumDur != totalDuration && count > 0)
                durations[count - 1] += totalDuration - sumDur;

            var phonemeList = new List<Phoneme>();
            int currentPos = 0;

            for (int i = 0; i < count; i++)
            {
                string result = phonemes[i];
                if (useRomajiMode)
                    result = ConvertToRomaji(result);

                var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
                string color = attr.voiceColor ?? "";
                int toneShift = attr.toneShift ?? 0;

                if (singer != null && singer.TryGetMappedOto(result, note.tone + toneShift, color, out var oto))
                    result = oto.Alias;

                phonemeList.Add(new Phoneme { phoneme = result, position = currentPos });
                currentPos += durations[i];
            }
            return new Result { phonemes = phonemeList.ToArray() };
        }

        private Result FinalizeWithProtection(
            Note note,
            List<string> phonemes,
            int[] userWeights,
            List<string> perPhonemePinyin,
            List<int> perPhonemeIndex,
            List<int> perPhonemeTotal,
            int totalDuration,
            bool hasExtender,
            Note? nextNote)
        {
            int count = phonemes.Count;
            if (count == 0)
                return MakeSimpleResult("sil", note);

            double[] baseWeights = BuildBaseWeights(
                phonemes, userWeights,
                perPhonemePinyin, perPhonemeIndex, perPhonemeTotal,
                hasExtender);

            ApplyOnsetProtectionToDoubleWeights(note, phonemes, baseWeights, nextNote);

            return SplitWithDoubleWeights(note, phonemes, baseWeights, totalDuration);
        }

        private Result SplitHintPhonemes(
            Note note, List<string> phonemes, int[] weights,
            int totalDuration, bool hasExtender,
            Note? prevNote, Note? nextNote, bool headBreak)
        {
            int count = phonemes.Count;
            if (count == 0) return MakeSimpleResult("sil", note);

            string prevVowel = null;
            if (!headBreak && prevNote.HasValue)
            {
                string prevFinal = GetFinalHiraganaFromNote(prevNote.Value);
                if (!string.IsNullOrEmpty(prevFinal))
                    prevVowel = GetTailVowel(prevFinal);
            }

            List<string> finalPhonemes = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string current = phonemes[i];
                SplitExplicitVcv(current, out string explicitPrefix, out string body);

                string foundPhoneme = null;

                if (!string.IsNullOrEmpty(explicitPrefix))
                {
                    if (CheckOtoHit(current, note, out _))
                        foundPhoneme = current;
                    else if (CheckOtoHit(body, note, out _))
                        foundPhoneme = body;
                    else
                        foundPhoneme = body;
                }
                else
                {
                    string connectVowel;
                    if (i == 0)
                        connectVowel = headBreak ? null : prevVowel;
                    else
                    {
                        SplitExplicitVcv(phonemes[i - 1], out _, out string prevBody);
                        connectVowel = GetTailVowel(prevBody);
                    }

                    if (!string.IsNullOrEmpty(connectVowel))
                    {
                        string vcv = $"{connectVowel} {body}";
                        if (CheckOtoHit(vcv, note, out _))
                            foundPhoneme = vcv;
                    }
                    if (foundPhoneme == null && i == 0)
                    {
                        string noPrev = $"- {body}";
                        if (CheckOtoHit(noPrev, note, out _))
                            foundPhoneme = noPrev;
                    }
                    if (foundPhoneme == null)
                        foundPhoneme = body;
                }

                finalPhonemes.Add(foundPhoneme);
            }

            return FinalizeWithProtection(note, finalPhonemes, weights,
                null, null, null,
                totalDuration, hasExtender, nextNote);
        }

        private Result MakeSimpleResult(string phoneme, Note note)
        {
            string result = phoneme;

            if (!string.IsNullOrEmpty(result))
            {
                result = ParseHeadBreak(result, out _);
            }

            if (useRomajiMode)
                result = ConvertToRomaji(result);

            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
            string color = attr.voiceColor ?? "";
            int toneShift = attr.toneShift ?? 0;

            if (singer != null && singer.TryGetMappedOto(result, note.tone + toneShift, color, out var oto))
                result = oto.Alias;

            return new Result { phonemes = new Phoneme[] { new Phoneme { phoneme = result } } };
        }

        private bool CheckOtoHit(string input, Note note, out UOto oto)
        {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
            string color = attr.voiceColor ?? "";

            string alternate = attr.alternate.HasValue ? attr.alternate.Value.ToString() : "";
            int toneShift = attr.toneShift ?? 0;

            if (singer != null && singer.TryGetMappedOto(input + alternate, note.tone + toneShift, color, out var otoAlt))
            {
                oto = otoAlt;
                return true;
            }

            if (singer != null && singer.TryGetMappedOto(input, note.tone + toneShift, color, out var otoCandidacy))
            {
                oto = otoCandidacy;
                return true;
            }
            return false;
        }

        private string GetFinalHiraganaFromNote(Note note)
        {
            string lyric = note.lyric ?? "";
            if (string.IsNullOrEmpty(lyric)) return null;

            string[] hiraganas = ConvertToHiraganaArray(lyric, note);
            if (hiraganas == null || hiraganas.Length == 0) return null;

            SplitExplicitVcv(hiraganas[hiraganas.Length - 1], out _, out string body);
            return body;
        }

        private string[] ConvertToHiraganaArray(string lyric, Note note)
            => ConvertToHiraganaArray(lyric, note, out _);

        private string[] ConvertToHiraganaArray(string lyric, Note note, out int[] weights)
        {
            weights = null;
            if (string.IsNullOrEmpty(lyric)) return null;

            string clean = ParseHeadBreak(lyric, out _);
            if (string.IsNullOrEmpty(clean)) return null;

            if (IsDirectMapping(clean, note, out string displayText, out string[] directPhonemes, out int[] directWeights))
            {
                CurrentPinyin = null;
                weights = directWeights;
                return directPhonemes;
            }

            string trimmed = GetFirstChar(clean);

            if (Regex.IsMatch(trimmed, @"[\u3040-\u309F\u30A0-\u30FF]"))
            {
                CurrentPinyin = null;
                return new[] { trimmed };
            }

            if (IsChineseCharacter(trimmed))
            {
                string romanization = ConvertToPinyin(trimmed);
                if (!string.IsNullOrEmpty(romanization) && romanization != trimmed)
                    return ProcessRomanizationWithCoda(romanization, note);
                CurrentPinyin = null;
                return null;
            }

            if (IsPinyin(trimmed))
                return ProcessRomanizationWithCoda(trimmed, note);

            CurrentPinyin = null;
            return new[] { trimmed };
        }

        private string ConvertToPinyin(string chinese)
        {
            try
            {
                var helper = new PinyinLyricsHelper();
                string pinyinWithTones = helper.Convert(chinese);
                string pinyinWithoutTones = Regex.Replace(pinyinWithTones, @"[\d]", "");
                return pinyinWithoutTones.Trim();
            }
            catch
            {
                return chinese;
            }
        }

        private string GetFirstChar(string lyric)
        {
            if (string.IsNullOrEmpty(lyric)) return lyric;
            string clean = ParseHeadBreak(lyric, out _);
            if (string.IsNullOrEmpty(clean)) return lyric;
            if (IsChineseCharacter(clean)) return clean[0].ToString();
            if (clean.Contains(' ')) return clean.Split(' ')[0];
            return clean;
        }

        private bool IsPinyin(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            bool hasLetter = Regex.IsMatch(text, @"[a-z]");
            bool hasJapanese = Regex.IsMatch(text, @"[\u3040-\u309F\u30A0-\u30FF]");
            return hasLetter && !hasJapanese;
        }

        private bool IsChineseCharacter(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return Regex.IsMatch(text, @"[\u4e00-\u9fff]");
        }

        private string ExtractBasePhoneme(string vcvPhoneme)
        {
            if (string.IsNullOrEmpty(vcvPhoneme)) return vcvPhoneme;
            var parts = vcvPhoneme.Split(' ');
            if (parts.Length >= 2)
                return parts[1];
            return vcvPhoneme;
        }

        private string ConvertToRomaji(string phoneme)
        {
            if (string.IsNullOrEmpty(phoneme)) return phoneme;

            var parts = phoneme.Split(' ');
            if (parts.Length >= 2)
            {
                string vowel = parts[0];
                string kana = parts[1];
                if (RomajiMap.TryGetValue(kana, out var romaji))
                    return $"{vowel} {romaji}";
                return phoneme;
            }

            if (RomajiMap.TryGetValue(phoneme, out var result))
                return result;
            return phoneme;
        }

        private bool IsKana(string text)
        {
            return Regex.IsMatch(text, @"^[\u3040-\u309F\u30A0-\u30FF]+$");
        }

        private bool IsDirectMapping(string lyric, Note note, out string displayText, out string[] phonemes, out int[] weights)
        {
            displayText = null;
            phonemes = null;
            weights = null;
            if (string.IsNullOrEmpty(lyric)) return false;

            var match = Regex.Match(lyric, @"^(.*?)\[([^\]]+)\]$");
            if (!match.Success) return false;

            displayText = match.Groups[1].Value.Trim();
            string content = match.Groups[2].Value;
            if (string.IsNullOrEmpty(content)) return false;

            string hintLyric = !string.IsNullOrEmpty(displayText) ? displayText : lyric;
            if (!ResolveHintPhonemes(content, hintLyric, note, out var hintPhonemes, out var hintWeights))
                return false;

            phonemes = hintPhonemes.ToArray();
            weights = hintWeights;
            return true;
        }

        // ========== phoneticHint / lyric[hint] 解析 ==========
        /// <summary>
        /// 按引号 + 分隔符切分 hint。
        /// 规则：
        ///   - 双引号内 "..." 整体作为一个 token（保留内部空格）
        ///   - 引号外按 ',' 和空格切分
        ///   - 未闭合的引号按普通字符处理
        /// 示例：
        ///   ka ki ku              → ["ka", "ki", "ku"]
        ///   "e い"                → ["e い"]
        ///   ka,"e い",ku          → ["ka", "e い", "ku"]
        ///   "e い",30 70          → ["e い", "30", "70"]
        /// </summary>
        private List<string> SplitHintTokens(string hint)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(hint)) return result;

            int i = 0;
            var current = new StringBuilder();

            while (i < hint.Length)
            {
                char c = hint[i];

                if (c == '"')
                {
                    int close = hint.IndexOf('"', i + 1);
                    if (close < 0)
                    {
                        current.Append(c);
                        i++;
                        continue;
                    }

                    if (current.Length > 0)
                    {
                        string t = current.ToString().Trim();
                        if (!string.IsNullOrEmpty(t)) result.Add(t);
                        current.Clear();
                    }

                    string inner = hint.Substring(i + 1, close - i - 1).Trim();
                    if (!string.IsNullOrEmpty(inner)) result.Add(inner);

                    i = close + 1;
                    continue;
                }

                if (c == ',' || char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        string t = current.ToString().Trim();
                        if (!string.IsNullOrEmpty(t)) result.Add(t);
                        current.Clear();
                    }
                    i++;
                    continue;
                }

                current.Append(c);
                i++;
            }

            if (current.Length > 0)
            {
                string t = current.ToString().Trim();
                if (!string.IsNullOrEmpty(t)) result.Add(t);
            }

            return result;
        }

        private bool ResolveHintPhonemes(
            string hint,
            string lyric,
            Note note,
            out List<string> phonemes,
            out int[] weights)
        {
            phonemes = new List<string>();
            weights = null;

            if (string.IsNullOrWhiteSpace(hint))
                return false;

            var parts = SplitHintTokens(hint);

            if (parts.Count == 0)
                return false;

            var hintPhonemes = new List<string>();
            var hintWeights = new List<int>();
            bool hasAnyNonNumeric = false;

            foreach (var part in parts)
            {
                if (int.TryParse(part, out int w))
                {
                    hintWeights.Add(w);
                }
                else
                {
                    hasAnyNonNumeric = true;
                    if (IsKana(part))
                        hintPhonemes.Add(part);
                    else if (IsExplicitVcvOrKana(part))
                        hintPhonemes.Add(part);   // 引号内 "e い" 整体保留
                    else if (RomajiToKanaMap.TryGetValue(part.ToLower(), out string kana))
                        hintPhonemes.Add(kana);
                    else
                        hintPhonemes.Add(part);
                }
            }

            if (!hasAnyNonNumeric)
            {
                string pinyin = GetPinyinFromLyric(lyric, note);
                if (string.IsNullOrEmpty(pinyin))
                    return false;

                if (!PinyinMapAlternatives.TryGetValue(pinyin, out var alternatives)
                    || alternatives == null || alternatives.Count == 0)
                    return false;

                string[] defaultPhonemes = alternatives
                    .FirstOrDefault(a => a.Length == hintWeights.Count)
                    ?? alternatives[0];

                if (defaultPhonemes.Length == 0)
                    return false;

                if (hintWeights.Count != defaultPhonemes.Length)
                {
                    Log.Warning($"[ZH to JA] hint 权重数量({hintWeights.Count})与映射表假名数量({defaultPhonemes.Length})不符，忽略权重。pinyin={pinyin}");
                    phonemes = defaultPhonemes.ToList();
                    weights = null;
                    return true;
                }

                phonemes = defaultPhonemes.ToList();
                weights = hintWeights.ToArray();
                return true;
            }

            if (hintPhonemes.Count == 0)
                return false;

            phonemes = hintPhonemes;
            if (hintWeights.Count == hintPhonemes.Count)
                weights = hintWeights.ToArray();

            return true;
        }

        private string GetPinyinFromLyric(string lyric, Note note)
        {
            if (string.IsNullOrEmpty(lyric))
                return null;

            string clean = ParseHeadBreak(lyric, out _);
            string trimmed = GetFirstChar(clean);

            if (IsChineseCharacter(trimmed))
            {
                string pinyin = ConvertToPinyin(trimmed);
                if (!string.IsNullOrEmpty(pinyin) && pinyin != trimmed)
                    return pinyin.ToLower();
                return null;
            }

            if (IsPinyin(trimmed))
                return trimmed.ToLower();

            return null;
        }

        private string GetTailVowel(string hiragana)
        {
            if (string.IsNullOrEmpty(hiragana)) return null;
            var lastChar = hiragana.Length >= 2 ? hiragana.Substring(hiragana.Length - 1) : hiragana;
            return VowelMap.TryGetValue(lastChar, out var vowel) ? vowel : null;
        }

        private Result ProcessMergedNotes(Note[] notes, List<string> lyrics, int totalDuration,
                                          Note? prevNote, Note? nextNote, bool hasExtender = false)
        {
            if (notes.Length == 0 || lyrics.Count == 0)
                return MakeSimpleResult("sil", notes.Length > 0 ? notes[0] : default);

            var firstNote = notes[0];

            string firstClean = ParseHeadBreak(lyrics.Count > 0 ? lyrics[0] : "", out bool headBreak);

            if (!string.IsNullOrEmpty(firstNote.phoneticHint))
            {
                if (ResolveHintPhonemes(firstNote.phoneticHint.Trim(), firstClean, firstNote,
                                        out var hintPhonemes, out var hintWeights))
                {
                    return SplitHintPhonemes(firstNote, hintPhonemes, hintWeights,
                        totalDuration, hasExtender, prevNote, nextNote, headBreak);
                }
            }

            List<string> allHiraganas = new List<string>();
            List<string> perPhonemePinyin = new List<string>();
            List<int> perPhonemeIndex = new List<int>();
            List<int> perPhonemeTotal = new List<int>();

            foreach (var rawLyric in lyrics)
            {
                string cleanLyric = ParseHeadBreak(rawLyric, out _);
                string[] hiraganas = ConvertToHiraganaArray(cleanLyric, firstNote, out _);
                string pinyinForThisNote = CurrentPinyin;

                if (hiraganas != null && hiraganas.Length > 0)
                {
                    for (int i = 0; i < hiraganas.Length; i++)
                    {
                        allHiraganas.Add(hiraganas[i]);
                        perPhonemePinyin.Add(pinyinForThisNote);
                        perPhonemeIndex.Add(i);
                        perPhonemeTotal.Add(hiraganas.Length);
                    }
                }
            }

            if (allHiraganas.Count == 0)
                return MakeSimpleResult(string.Join("", lyrics), firstNote);

            List<string> outputPhonemes = new List<string>();

            string prevVowel = null;
            if (!headBreak && prevNote.HasValue)
            {
                string prevFinal = GetFinalHiraganaFromNote(prevNote.Value);
                if (!string.IsNullOrEmpty(prevFinal))
                    prevVowel = GetTailVowel(prevFinal);
            }

            for (int i = 0; i < allHiraganas.Count; i++)
            {
                string current = allHiraganas[i];
                SplitExplicitVcv(current, out string explicitPrefix, out string body);

                string foundPhoneme = null;

                if (!string.IsNullOrEmpty(explicitPrefix))
                {
                    if (CheckOtoHit(current, firstNote, out _))
                        foundPhoneme = current;
                    else if (CheckOtoHit(body, firstNote, out _))
                        foundPhoneme = body;
                    else
                        foundPhoneme = body;
                }
                else
                {
                    string connectVowel;
                    if (i == 0)
                        connectVowel = headBreak ? null : prevVowel;
                    else
                    {
                        SplitExplicitVcv(allHiraganas[i - 1], out _, out string prevBody);
                        connectVowel = GetTailVowel(prevBody);
                    }

                    if (!string.IsNullOrEmpty(connectVowel))
                    {
                        string vcv = $"{connectVowel} {body}";
                        if (CheckOtoHit(vcv, firstNote, out _))
                            foundPhoneme = vcv;
                    }
                    if (foundPhoneme == null && i == 0)
                    {
                        string noPrev = $"- {body}";
                        if (CheckOtoHit(noPrev, firstNote, out _))
                            foundPhoneme = noPrev;
                    }
                    if (foundPhoneme == null)
                        foundPhoneme = body;
                }

                outputPhonemes.Add(foundPhoneme);
            }

            return FinalizeWithProtection(firstNote, outputPhonemes, null,
                perPhonemePinyin, perPhonemeIndex, perPhonemeTotal,
                totalDuration, hasExtender, nextNote);
        }

        private Result ProcessNoteVCV(Note note, Note? prevNote, Note? nextNote)
        {
            string rawLyric = note.lyric ?? "";
            if (string.IsNullOrEmpty(rawLyric))
                return MakeSimpleResult("sil", note);

            string lyric = ParseHeadBreak(rawLyric, out bool headBreak);
            if (string.IsNullOrEmpty(lyric))
                return MakeSimpleResult("sil", note);

            if (!string.IsNullOrEmpty(note.phoneticHint))
            {
                if (ResolveHintPhonemes(note.phoneticHint.Trim(), lyric, note,
                                        out var hintPhonemes, out var hintWeights))
                {
                    return SplitHintPhonemes(note, hintPhonemes, hintWeights,
                        note.duration, false, prevNote, nextNote, headBreak);
                }
            }

            string[] hiraganas = ConvertToHiraganaArray(lyric, note, out int[] directWeights);
            if (hiraganas == null || hiraganas.Length == 0)
                return MakeSimpleResult(lyric, note);

            string savedPinyin = CurrentPinyin;

            List<string> outputPhonemes = new List<string>();
            string prevVowel = null;
            if (!headBreak && prevNote.HasValue)
            {
                string prevFinal = GetFinalHiraganaFromNote(prevNote.Value);
                if (!string.IsNullOrEmpty(prevFinal))
                    prevVowel = GetTailVowel(prevFinal);
            }

            for (int i = 0; i < hiraganas.Length; i++)
            {
                string current = hiraganas[i];
                SplitExplicitVcv(current, out string explicitPrefix, out string body);

                string foundPhoneme = null;

                if (!string.IsNullOrEmpty(explicitPrefix))
                {
                    if (CheckOtoHit(current, note, out _))
                        foundPhoneme = current;
                    else if (CheckOtoHit(body, note, out _))
                        foundPhoneme = body;
                    else
                        foundPhoneme = body;
                }
                else
                {
                    string connectVowel;
                    if (i == 0)
                        connectVowel = headBreak ? null : prevVowel;
                    else
                    {
                        SplitExplicitVcv(hiraganas[i - 1], out _, out string prevBody);
                        connectVowel = GetTailVowel(prevBody);
                    }

                    if (!string.IsNullOrEmpty(connectVowel))
                    {
                        string vcv = $"{connectVowel} {body}";
                        if (CheckOtoHit(vcv, note, out _))
                            foundPhoneme = vcv;
                    }
                    if (foundPhoneme == null && i == 0)
                    {
                        string noPrev = $"- {body}";
                        if (CheckOtoHit(noPrev, note, out _))
                            foundPhoneme = noPrev;
                    }
                    if (foundPhoneme == null)
                        foundPhoneme = body;
                }

                outputPhonemes.Add(foundPhoneme);
            }

            List<string> pinyinList = new List<string>();
            List<int> indexList = new List<int>();
            List<int> totalList = new List<int>();
            for (int i = 0; i < outputPhonemes.Count; i++)
            {
                pinyinList.Add(savedPinyin);
                indexList.Add(i);
                totalList.Add(outputPhonemes.Count);
            }

            return FinalizeWithProtection(note, outputPhonemes, directWeights,
                pinyinList, indexList, totalList,
                note.duration, false, nextNote);
        }

        public override void SetSinger(USinger singer)
        {
            this.singer = singer;

            string singerDir = singer?.Location;

            string configPath = null;
            if (!string.IsNullOrEmpty(singerDir))
            {
                GenerateDefaultConfig(singerDir);
                configPath = Path.Combine(singerDir, "pinyin_map.txt");
            }
            else
            {
                string dllPath = typeof(ChineseToJapanesePhonemizer).Assembly.Location;
                string pluginDir = Path.GetDirectoryName(dllPath);
                GenerateDefaultConfig(pluginDir);
                configPath = Path.Combine(pluginDir, "pinyin_map.txt");
            }

            if (File.Exists(configPath))
            {
                try
                {
                    string content = File.ReadAllText(configPath, Encoding.UTF8);
                    var externalMap = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
                    var externalOverrides = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
                    ParseMapContent(content, externalMap, externalOverrides);

                    if (externalMap.Count > 0)
                    {
                        PinyinMapAlternatives = externalMap;
                        PinyinWeightOverrides = externalOverrides;
                        Log.Information($"[ZH to JA] 加载歌手映射表: {configPath}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[ZH to JA] 加载配置失败");
                }
            }

            if (singer != null)
            {
                bool supportsKana = singer.TryGetMappedOto("か", 60, "", out _);
                bool supportsRomaji = !supportsKana && singer.TryGetMappedOto("ka", 60, "", out _);
                useRomajiMode = supportsRomaji;
            }

            Log.Information($"[ZH to JA] 音头保护: enabled={onsetProtectionEnabled}, " +
                            $"objects=[{string.Join(",", onsetProtectionObjects)}], " +
                            $"magnitude={onsetProtectionMagnitude}");
        }

        private static void GenerateDefaultConfig(string targetDirectory)
        {
            if (string.IsNullOrEmpty(targetDirectory))
                return;

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            string mapPath = Path.Combine(targetDirectory, "pinyin_map.txt");
            if (!File.Exists(mapPath))
                File.WriteAllText(mapPath, DEFAULT_PINYIN_MAP, Encoding.UTF8);
        }

        public override Result Process(Note[] notes, Note? prev, Note? next,
                                        Note? prevNeighbour, Note? nextNeighbour,
                                        Note[] prevNeighbours)
        {
            if (notes == null || notes.Length == 0)
                return MakeSimpleResult("sil", default);

            var regularNotes = new List<Note>();
            var totalLyrics = new List<string>();
            int totalDuration = 0;
            bool hasExtender = false;

            foreach (var note in notes)
            {
                string noteLyric = note.lyric ?? "";
                if (noteLyric == "+")
                {
                    hasExtender = true;
                    totalDuration += note.duration;
                    continue;
                }
                regularNotes.Add(note);
                totalLyrics.Add(noteLyric);
                totalDuration += note.duration;
            }

            if (regularNotes.Count == 0)
                return MakeSimpleResult("sil", notes[0]);

            if (regularNotes.Count == 1 && !hasExtender)
                return ProcessNoteVCV(regularNotes[0], prevNeighbour, nextNeighbour);

            return ProcessMergedNotes(regularNotes.ToArray(), totalLyrics, totalDuration,
                                      prevNeighbour, nextNeighbour, hasExtender);
        }
    }
}
